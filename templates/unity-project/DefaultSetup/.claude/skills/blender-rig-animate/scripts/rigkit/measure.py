"""measure: find joint landmarks from mesh cross-sections, and render ortho views with the landmarks drawn.

Joints must sit INSIDE the limb, at the centre of its cross-section, at the real pivot of the body part.
Guessing them from a picture is the first cause of bad deformation. This step slices the skin mesh:
  * Z slices (horizontal): legs (two contours) -> crotch (contours merge), torso width, neck (narrowest part)
  * X slices (vertical, across the arm): arm centre line, where the arm leaves the torso (shoulder), wrist
It writes suggested landmarks + the raw profiles; Claude reviews the render and writes the rig spec.

  blender -b prepared.blend --python rk.py -- measure --mesh body --out landmarks.json --png landmarks.png
"""
import argparse

import bpy
import numpy as np

from . import common as C
from . import review as RV


def plane_contours(V, T, axis, value):
    """Cross-section of a triangle mesh with the plane V[:, axis] == value.
    Returns a list of contours, each an (n, 3) array of points (unordered but connected)."""
    d = V[:, axis] - value
    d[np.abs(d) < 1e-9] = 1e-9  # avoid vertices exactly on the plane
    s = d[T] > 0
    cut = s.any(1) & ~s.all(1)
    tris = T[cut]
    if len(tris) == 0:
        return []
    # each crossing triangle has exactly two crossing edges
    seg_keys = []
    pts = {}
    for tri in tris:
        ks = []
        for i in range(3):
            a, b = tri[i], tri[(i + 1) % 3]
            if (d[a] > 0) != (d[b] > 0):
                k = (a, b) if a < b else (b, a)
                if k not in pts:
                    t = d[a] / (d[a] - d[b])
                    pts[k] = V[a] + (V[b] - V[a]) * t
                ks.append(k)
        if len(ks) == 2:
            seg_keys.append(ks)
    # union-find over edge keys
    parent = {k: k for k in pts}

    def find(k):
        while parent[k] != k:
            parent[k] = parent[parent[k]]
            k = parent[k]
        return k

    for k1, k2 in seg_keys:
        r1, r2 = find(k1), find(k2)
        if r1 != r2:
            parent[r1] = r2
    groups = {}
    for k, p in pts.items():
        groups.setdefault(find(k), []).append(p)
    return [np.array(g) for g in groups.values() if len(g) >= 3]


def contour_info(c):
    lo, hi = c.min(0), c.max(0)
    return {"center": C.r((lo + hi) * 0.5), "mean": C.r(c.mean(0)), "min": C.r(lo), "max": C.r(hi),
            "size": C.r(hi - lo), "n": int(len(c))}


def skin_arrays(ob, min_island_frac=0.05):
    """World-space vertices and triangles of the big islands of a mesh (drops eyes, buttons, belts)."""
    me = ob.data
    V = C.world_coords(ob)
    T = C.triangles_array(me)
    labels, n = C.islands(me)
    counts = np.bincount(labels, minlength=n)
    keep_isl = np.nonzero(counts >= min_island_frac * len(V))[0]
    keep_v = np.isin(labels, keep_isl)
    keep_t = keep_v[T].all(1)
    return V, T[keep_t], keep_v


def z_profile(V, T, n=90):
    lo, hi = V[:, 2].min(), V[:, 2].max()
    out = []
    for i in range(n):
        z = lo + (hi - lo) * (i + 0.5) / n
        cs = [contour_info(c) for c in plane_contours(V, T, 2, z)]
        cs.sort(key=lambda c: c["center"][0])
        out.append({"z": C.r(z), "contours": cs})
    return out


def x_profile(V, T, n=60):
    hi = V[:, 0].max()
    out = []
    for i in range(n):
        x = hi * (i + 0.5) / n
        cs = [contour_info(c) for c in plane_contours(V, T, 0, x)]
        cs.sort(key=lambda c: -c["size"][1] * c["size"][2])
        out.append({"x": C.r(x), "contours": cs})
    return out


def suggest(zp, xp, V):
    """Heuristic landmarks for a biped standing in T-pose / A-pose, facing -Y. Always review the render."""
    lo, hi = V.min(0), V.max(0)
    height = hi[2] - lo[2]
    s = {"ground_z": C.r(lo[2]), "top_z": C.r(hi[2]), "height": C.r(height)}
    # legs: slices in the lower half with two contours, one each side of X=0 (higher up, T-pose arms do the same)
    two = [row for row in zp if row["z"] - lo[2] < 0.5 * height and len(row["contours"]) >= 2
           and row["contours"][0]["center"][0] < -0.02 * height and row["contours"][-1]["center"][0] > 0.02 * height]
    crotch = None
    for row in zp:  # the crotch: the first slice above a two-leg slice that is one piece again
        if row["z"] - lo[2] > 0.6 * height:
            break
        if row not in two and any(r2 in two for r2 in zp if r2["z"] < row["z"]):
            crotch = row["z"]
            break
    leg_rows = [row for row in two if crotch is None or row["z"] < crotch]
    if leg_rows:
        s["crotch_z"] = crotch if crotch is not None else leg_rows[-1]["z"]
        left = [row["contours"][-1] for row in leg_rows]
        s["leg_L_axis"] = [[c["center"][0], c["center"][1], row["z"]] for c, row in zip(left, leg_rows)][::3]
        widths = [c["size"][0] for c in left]
        s["leg_L_min_width_z"] = leg_rows[int(np.argmin(widths))]["z"]
    # arms: X slices whose only / smallest contour is the arm
    arm = []
    for row in xp:
        if not row["contours"]:
            continue
        big = row["contours"][0]
        arm.append((row["x"], big))
    if arm:
        # the torso cross-section is much taller (Z) than the arm; the arm starts where the slice gets small
        sizes_z = np.array([c["size"][2] for _, c in arm])
        med_tip = np.median(sizes_z[-max(3, len(sizes_z) // 5):])
        start = next((i for i, sz in enumerate(sizes_z) if sz < 2.2 * med_tip), None)
        if start is not None:
            s["arm_L_start_x"] = arm[start][0]
            s["arm_L_axis"] = [[x, c["center"][1], c["center"][2]] for x, c in arm[start:]][::2]
            s["arm_L_end_x"] = arm[-1][0]
            s["arm_L_thickness"] = C.r(float(np.median([c["size"][2] for _, c in arm[start:]])))
    # neck: a waist in the width profile of the upper body, narrower than the widest slice below AND above it
    # (the crown of the head narrows too, but nothing wider sits above it); chibi heads on round bodies have none
    tor = [(row["z"], max(c["size"][0] for c in row["contours"])) for row in zp
           if row["contours"] and 0.5 * height < row["z"] - lo[2] < 0.98 * height]
    best = None
    for i in range(1, len(tor) - 1):
        below = max(w for _, w in tor[:i])
        above = max(w for _, w in tor[i + 1:])
        ratio = tor[i][1] / min(below, above)
        if ratio < 0.85 and (best is None or ratio < best[0]):
            best = (ratio, i)
    if best is not None:
        s["neck_z"] = tor[best[1]][0]
        s["neck_width"] = C.r(tor[best[1]][1])
        s["neck_ratio"] = C.r(best[0])
    return s


def main(argv):
    ap = argparse.ArgumentParser(prog="measure")
    ap.add_argument("--mesh", required=True, help="skin mesh object (the body, not props)")
    ap.add_argument("--out", required=True)
    ap.add_argument("--png", default=None)
    ap.add_argument("--points", default=None, help="optional JSON {name: [x,y,z]} to draw instead of suggestions")
    a = ap.parse_args(argv)
    C.force_object_mode()
    ob = bpy.data.objects[a.mesh]
    V, T, _ = skin_arrays(ob)
    zp = z_profile(V, T)
    xp = x_profile(V, T)
    sug = suggest(zp, xp, V)
    rep = C.Report("measure")
    rep.info["suggested"] = sug
    rep.info["z_profile"] = [{"z": r["z"], "contours": [{k: c[k] for k in ("center", "size")} for c in r["contours"]]}
                             for r in zp]
    rep.info["x_profile"] = [{"x": r["x"], "contours": [{k: c[k] for k in ("center", "size")} for c in r["contours"]][:3]}
                             for r in xp]
    if a.png:
        pts = C.load_json(a.points) if a.points else {}
        if not pts:
            if "crotch_z" in sug:
                pts["crotch"] = (0, 0, sug["crotch_z"])
            for i, p in enumerate(sug.get("leg_L_axis", [])):
                pts["legL%d" % i] = p
            for i, p in enumerate(sug.get("arm_L_axis", [])):
                pts["armL%d" % i] = p
            if "neck_z" in sug:
                pts["neck"] = (0, 0, sug["neck_z"])
        RV.markers(pts, radius=sug["height"] * 0.012)
        RV.sheet([lambda: None], ["front", "side"], a.png, size=720, xray=0.35)
        RV.clear_prefix()
    return rep.dump(a.out)
