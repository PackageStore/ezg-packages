"""check_weights: hard gate on skin weights + a per-joint deformation stress test + heat-map renders.

  blender -b skinned.blend --python rk.py -- check_weights --out weights.json [--png weights.png] [--stress]

Errors (must be fixed before animating):
  UNWEIGHTED, NOT_NORMALIZED, TOO_MANY_INFLUENCES, NONDEFORM_WEIGHT, CROSS_SIDE, STRAY_WEIGHT, RIGID_SPLIT,
  TEAR (an edge stretches > 3x when one joint bends inside its limits), COLLAPSE (an edge shrinks < 0.15x)
Warnings: DISCONNECTED (a bone's strong region is in several pieces), ACCESSORY_SKIN (skin under a rigid
accessory follows another bone -> they will intersect), TINY_WEIGHTS.
Works on any armature (QuyChuan names, Mixamo names, Blender .L/.R).
"""
import argparse

import bpy
import numpy as np
from mathutils import Quaternion
from mathutils.kdtree import KDTree

from . import common as C


def side_of(name):
    n = name.split(":")[-1]
    if n.endswith(("_L", ".L", "_l", ".l")) or n.startswith("Left") or "Left" in n:
        return "L"
    if n.endswith(("_R", ".R", "_r", ".r")) or n.startswith("Right") or "Right" in n:
        return "R"
    return "C"


def armature_meshes(arm):
    out = []
    for ob in bpy.data.objects:
        if ob.type != "MESH":
            continue
        for m in ob.modifiers:
            if m.type == "ARMATURE" and m.object == arm:
                out.append(ob)
                break
    return out


def seg_dist(P, h, t):
    ht = t - h
    L2 = float(ht @ ht) or 1e-12
    s = np.clip(((P - h) @ ht) / L2, 0, 1)
    proj = h + s[:, None] * ht
    return np.linalg.norm(P - proj, axis=1)


def is_rigid(ob, W):
    """A mesh whose every vertex has the same single bone is a rigid prop."""
    nz = (W > 1e-4).sum(1)
    if not (nz == 1).all():
        return False
    return len(np.unique(np.argmax(W, 1))) == 1


def check_mesh(ob, arm, rep, max_inf, band_frac):
    bones = arm.data.bones
    groups = [vg.name for vg in ob.vertex_groups]
    W, names = C.read_weights(ob, groups)
    co = C.world_coords(ob)
    n = len(co)
    height = float(co[:, 2].max() - co[:, 2].min()) if n else 1.0
    deform = [j for j, g in enumerate(names) if g in bones and bones[g].use_deform]
    nondeform = [j for j, g in enumerate(names) if g in bones and not bones[g].use_deform]
    Wd = W[:, deform]
    dn = [names[j] for j in deform]
    total = Wd.sum(1)
    info = {"verts": n, "groups": len(names), "deform_groups": len(deform)}
    unw = np.nonzero(total < 1e-4)[0]
    if len(unw):
        rep.error("UNWEIGHTED", "%s: %d vertices move with nothing (e.g. %s)" % (ob.name, len(unw), C.r(co[unw[0]], 3)),
                  mesh=ob.name, count=int(len(unw)))
    notn = np.nonzero((total > 1e-4) & (np.abs(total - 1) > 2e-3))[0]
    if len(notn):
        rep.error("NOT_NORMALIZED", "%s: %d vertices do not sum to 1 (Unity normalises differently than Blender)"
                  % (ob.name, len(notn)), mesh=ob.name, count=int(len(notn)))
    inf = (Wd > 1e-4).sum(1)
    info["max_influences"] = int(inf.max()) if n else 0
    many = np.nonzero(inf > max_inf)[0]
    if len(many):
        rep.error("TOO_MANY_INFLUENCES", "%s: %d vertices have more than %d bones" % (ob.name, len(many), max_inf),
                  mesh=ob.name, count=int(len(many)))
    for j in nondeform:
        if W[:, j].max() > 1e-4:
            rep.error("NONDEFORM_WEIGHT", "%s: group %s belongs to a non-deform bone" % (ob.name, names[j]))
    tiny = int(((Wd > 1e-4) & (Wd < 0.01)).sum())
    if tiny:
        rep.warn("TINY_WEIGHTS", "%s: %d weights below 0.01 (clean them)" % (ob.name, tiny))
    if n == 0 or not deform:
        return info, W, names
    if is_rigid(ob, Wd):
        info["rigid"] = dn[int(np.argmax(Wd[0]))]
        return info, W, names
    # cross-side contamination
    band = band_frac * height
    x = co[:, 0]
    for j, g in enumerate(dn):
        s = side_of(g)
        if s == "C":
            continue
        wrong = (x > band) if s == "R" else (x < -band)
        bad = np.nonzero(wrong & (Wd[:, j] > 0.05))[0]
        if len(bad):
            rep.error("CROSS_SIDE", "%s: %d vertices on the %s side follow %s (max %.2f)"
                      % (ob.name, len(bad), "left" if s == "R" else "right", g, Wd[bad, j].max()),
                      mesh=ob.name, bone=g, count=int(len(bad)))
    # stray weights: a vertex follows a bone much farther away than the nearest deform bone
    labels, n_isl = C.islands(ob.data)
    counts = np.bincount(labels, minlength=n_isl)
    main = counts[labels] >= 0.08 * n
    all_def = [bb for bb in bones if bb.use_deform and not bb.name.split(":")[-1].startswith("Prop_")]
    D = np.stack([seg_dist(co, np.array(bb.head_local), np.array(bb.tail_local)) for bb in all_def], 1)
    nearest = D.min(1)
    col_of = {bb.name: k for k, bb in enumerate(all_def)}
    for j, g in enumerate(dn):
        if g not in col_of:
            continue
        dist = D[:, col_of[g]]
        far = np.nonzero((Wd[:, j] > 0.25) & (dist > 3.0 * nearest + 0.08 * height))[0]
        if len(far):
            rep.error("STRAY_WEIGHT", "%s: %d vertices follow %s from %.2f m away while another bone is %.2f m away"
                      % (ob.name, len(far), g, dist[far].max(), nearest[far].min()), mesh=ob.name, bone=g,
                      count=int(len(far)), example=C.r(co[far[0]], 3))
        # region split into pieces (only on the main skin)
        strong = (Wd[:, j] > 0.5) & main
        if strong.sum() >= 6:
            e = C.edges_array(ob.data)
            keep = strong[e[:, 0]] & strong[e[:, 1]]
            ids = np.nonzero(strong)[0]
            remap = -np.ones(n, dtype=np.int64)
            remap[ids] = np.arange(len(ids))
            parent = np.arange(len(ids))

            def find(a):
                while parent[a] != a:
                    parent[a] = parent[parent[a]]
                    a = parent[a]
                return a

            for a, bb in e[keep]:
                ra, rb = find(remap[a]), find(remap[bb])
                if ra != rb:
                    parent[ra] = rb
            roots = np.array([find(i) for i in range(len(ids))])
            _, cnt = np.unique(roots, return_counts=True)
            pieces = int((cnt >= max(3, 0.02 * len(ids))).sum())
            if pieces > 1:
                rep.warn("DISCONNECTED", "%s: the strong region of %s is in %d pieces" % (ob.name, g, pieces),
                         mesh=ob.name, bone=g)
    return info, W, names


def accessory_check(meshes, arm, rep, tol):
    """Skin under a rigid accessory must follow the accessory's parent bone, or they separate / intersect."""
    rigid = []
    soft = []
    for ob in meshes:
        W, names = C.read_weights(ob)
        if len(names) == 1 or is_rigid(ob, W):
            rigid.append((ob, names[int(np.argmax(W[0]))] if len(W) else None))
        else:
            soft.append((ob, W, names))
    for ob, bone in rigid:
        b = arm.data.bones.get(bone)
        if b is None or b.parent is None:
            continue
        par = b.parent.name
        # only accessories resting on the body (hat, backpack), not props in the hand
        if par.split(":")[-1].startswith(("Hand", "LeftHand", "RightHand")) or "Hand" in par:
            continue
        aco = C.world_coords(ob)
        kd = KDTree(len(aco))
        for i, p in enumerate(aco):
            kd.insert(p, i)
        kd.balance()
        for sob, W, names in soft:
            if par not in names:
                continue
            j = names.index(par)
            co = C.world_coords(sob)
            near = [i for i, p in enumerate(co) if kd.find(p)[2] < tol]
            if not near:
                continue
            wj = W[near, j]
            bad = int((wj < 0.9).sum())
            if bad > 0.05 * len(near):
                rep.warn("ACCESSORY_SKIN", "%s rests on %d vertices of %s but %d of them are not >= 0.9 on %s: the skin "
                         "will slide under / poke through it (lock that region to %s)"
                         % (ob.name, len(near), sob.name, bad, par, par), accessory=ob.name, bone=par)


class MeshTopo:
    """Rest data of a soft mesh for the deformation metrics."""

    def __init__(self, ob, arm=None):
        self.ob = ob
        self.arm = arm
        me = ob.data
        self.rest = C.world_coords(ob)
        self.edges = C.edges_array(me)
        self.tris = C.triangles_array(me)
        n = len(self.rest)
        a, b = self.edges[:, 0], self.edges[:, 1]
        self.a, self.b = a, b
        self.deg = np.maximum(np.bincount(np.concatenate([a, b]), minlength=n), 1).astype(np.float64)
        L = np.linalg.norm(self.rest[a] - self.rest[b], axis=1)
        sumL = np.zeros(n)
        np.add.at(sumL, a, L)
        np.add.at(sumL, b, L)
        self.edge_len = np.maximum(sumL / self.deg, 1e-9)
        t = self.tris
        self.n0 = np.cross(self.rest[t[:, 1]] - self.rest[t[:, 0]], self.rest[t[:, 2]] - self.rest[t[:, 0]])
        self.A0 = np.linalg.norm(self.n0, axis=1) * 0.5
        self.big = self.A0 > np.percentile(self.A0, 20)
        self.height = float(self.rest[:, 2].max() - self.rest[:, 2].min()) or 1.0
        # dominant bone of every triangle: its rest normal turned by that bone is the expected normal
        self.tri_bone = None
        if arm is not None:
            W, names = C.read_weights(ob)
            keep = [j for j, g in enumerate(names) if g in arm.data.bones]
            self.bones = [names[j] for j in keep]
            if self.bones:
                Wt = W[:, keep][t].sum(1)
                self.tri_bone = np.argmax(Wt, 1)
                self.rest_inv = [np.array(arm.data.bones[b].matrix_local.to_3x3().inverted()) for b in self.bones]
        self.n0u = self.n0 / np.maximum(np.linalg.norm(self.n0, axis=1, keepdims=True), 1e-12)


def deform_metrics(topo, co, moving=None):
    """spike: |displacement - mean displacement of the neighbours| / local edge length (a vertex that follows the
    wrong bone moves unlike its neighbours); collapse: triangles whose area drops below 15%; flip: triangles whose
    normal turns more than 120 degrees away from its neighbours' average motion."""
    d = co - topo.rest
    S = np.zeros_like(d)
    np.add.at(S, topo.a, d[topo.b])
    np.add.at(S, topo.b, d[topo.a])
    spike = np.linalg.norm(d - S / topo.deg[:, None], axis=1) / topo.edge_len
    t = topo.tris
    n1 = np.cross(co[t[:, 1]] - co[t[:, 0]], co[t[:, 2]] - co[t[:, 0]])
    A1 = np.linalg.norm(n1, axis=1) * 0.5
    ratio = A1 / np.maximum(topo.A0, 1e-12)
    collapse = topo.big & (ratio < 0.15)
    n1u = n1 / np.maximum(np.linalg.norm(n1, axis=1, keepdims=True), 1e-12)
    if topo.tri_bone is not None and topo.arm is not None:
        # expected normal = rest normal turned by the triangle's dominant bone; flipped = turned > ~110 deg from it
        pb = topo.arm.pose.bones
        R = np.stack([np.array(pb[b].matrix.to_3x3()) @ inv for b, inv in zip(topo.bones, topo.rest_inv)])
        exp = np.einsum("tij,tj->ti", R[topo.tri_bone], topo.n0u)
        cosang = np.einsum("ij,ij->i", n1u, exp)
        flip = topo.big & (cosang < -0.35)
    else:
        vn = np.zeros_like(co)
        for k in range(3):
            np.add.at(vn, t[:, k], n1)
        avg = vn[t[:, 0]] + vn[t[:, 1]] + vn[t[:, 2]]
        cosang = np.einsum("ij,ij->i", n1u, avg) / np.maximum(np.linalg.norm(avg, axis=1), 1e-12)
        flip = topo.big & (cosang < -0.5)
    return spike, collapse, flip


def test_angles(meta, name):
    """Poses for the stress test: 70% of each limit, or +-30 degrees when the rig has no metadata."""
    lim = (meta.get(name) or {}).get("limits")
    out = []
    if lim:
        for key in ("flex", "side", "twist"):
            lo, hi = lim.get(key, (0, 0))
            for v in (hi, lo):
                if abs(v) >= 8:
                    out.append((key, 0.7 * v))
    else:
        out = [("flex", 30), ("flex", -30), ("side", 30), ("side", -30), ("twist", 30)]
    return out


# thresholds calibrated on tests/selftest_weights.py (heat-weighted cylinder vs. corrupted weights)
SPIKE_ERR = 1.2      # spike ratio above this on a vertex = it tears away from its neighbours
SPIKE_FRAC = 0.002   # ...on more than 0.2% of the vertices
COLLAPSE_MAX = 0.01  # more than 1% of the (non-tiny) triangles crushed below 15% area
FLIP_MAX = 0.005     # more than 0.5% of the triangles flipped


def stress_test(arm, meshes, rep):
    """Bend each deform joint alone, inside its limits, and measure how the skin behaves."""
    from . import skeleton as SK
    import json
    soft = []
    for ob in meshes:
        W, _ = C.read_weights(ob)
        if is_rigid(ob, W):
            continue
        soft.append(MeshTopo(ob, arm))
    if not soft:
        return
    meta = arm.get("rk_meta")
    meta = json.loads(meta) if isinstance(meta, str) else {}
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
        pb.rotation_quaternion = Quaternion()
        pb.location = (0, 0, 0)
        pb.scale = (1, 1, 1)
    summary = {}
    for pb in arm.pose.bones:
        b = pb.bone
        if not b.use_deform or b.name.split(":")[-1].startswith("Prop_") or b.parent is None:
            continue
        rows = []
        for key, ang in test_angles(meta, b.name):
            kw = {"flex": 0.0, "side": 0.0, "twist": 0.0}
            kw[key] = ang
            pb.rotation_quaternion = SK.anat_quat(kw["flex"], kw["side"], kw["twist"], SK.side_sign(b.name))
            bpy.context.view_layer.update()
            for topo in soft:
                co = C.world_coords(topo.ob, evaluated=True)
                spike, collapse, flip = deform_metrics(topo, co)
                i = int(np.argmax(spike))
                fl = np.nonzero(flip)[0]
                flip_at = C.r(topo.rest[topo.tris[fl[0]]].mean(0), 3) if len(fl) else None
                rows.append({"pose": "%s %+.0f" % (key, ang), "mesh": topo.ob.name, "flip_at": flip_at,
                             "spike_max": round(float(spike[i]), 2), "spike_at": C.r(topo.rest[i], 3),
                             "spike_frac": round(float((spike > SPIKE_ERR).mean()), 4),
                             "collapse_frac": round(float(collapse.sum() / max(topo.big.sum(), 1)), 4),
                             "flip_frac": round(float(flip.sum() / max(topo.big.sum(), 1)), 4)})
            pb.rotation_quaternion = Quaternion()
        bpy.context.view_layer.update()
        summary[b.name] = rows
        for r in rows:
            if r["spike_frac"] > SPIKE_FRAC:
                rep.error("TEAR", "%s %s: %.1f%% of %s tears away from its neighbours (worst %.1f at %s)"
                          % (b.name, r["pose"], 100 * r["spike_frac"], r["mesh"], r["spike_max"], r["spike_at"]),
                          bone=b.name, pose=r["pose"])
                break
        for r in rows:
            if r["collapse_frac"] > COLLAPSE_MAX:
                rep.error("COLLAPSE", "%s %s: %.1f%% of the triangles of %s are crushed"
                          % (b.name, r["pose"], 100 * r["collapse_frac"], r["mesh"]), bone=b.name, pose=r["pose"])
                break
        for r in rows:
            if r["flip_frac"] > FLIP_MAX:
                rep.error("FLIP", "%s %s: %.1f%% of the triangles of %s turn inside out (e.g. at %s)"
                          % (b.name, r["pose"], 100 * r["flip_frac"], r["mesh"], r["flip_at"]), bone=b.name,
                          pose=r["pose"])
                break
    rep.info["stress"] = summary


def weight_images(arm, meshes, out_png, heat_bones=()):
    """<out>.png: blended bone colours (front, back, both sides) + legend.
    <out>_heat.png (with heat_bones): one row per bone, blue 0 -> red 1, black = not in the group."""
    import os
    from . import review as RV
    soft = [ob for ob in meshes if not is_rigid(ob, C.read_weights(ob)[0])]
    rigid = [ob for ob in meshes if ob not in soft]
    for ob in rigid:
        ob.hide_render = True
    arm.data.pose_position = "REST"
    names = [b.name for b in arm.data.bones if b.use_deform and not b.name.split(":")[-1].startswith("Prop_")]
    colors = {}
    for ob in soft:
        colors.update(RV.segment_colors(ob, names))
    base = os.path.splitext(os.path.abspath(out_png))[0]
    RV.sheet([lambda: None], ["front", "back", "side", "side_r"], base + "_views.png", size=380, color_type="VERTEX",
             show_ground=False, rows="setters")
    RV.legend_png(names, base + "_legend.png")
    RV.hstack_png([base + "_views.png", base + "_legend.png"], out_png)
    for p in (base + "_views.png", base + "_legend.png"):
        if os.path.exists(p):
            os.remove(p)
    if heat_bones:
        setters = []
        for g in heat_bones:
            def s(g=g):
                for ob in soft:
                    RV.weight_colors(ob, g)
            setters.append(s)
        RV.sheet(setters, ["front", "back", "side"], base + "_heat.png", size=260, color_type="VERTEX",
                 show_ground=False, rows="setters")
    for ob in rigid:
        ob.hide_render = False
    arm.data.pose_position = "POSE"
    return colors


def main(argv):
    ap = argparse.ArgumentParser(prog="check_weights")
    ap.add_argument("--armature", default=None)
    ap.add_argument("--out", required=True)
    ap.add_argument("--png", default=None, help="weight segmentation image (+ <png>_heat.png with --heat)")
    ap.add_argument("--heat", default="", help="comma list of bones for per-bone heat maps")
    ap.add_argument("--stress", action="store_true")
    ap.add_argument("--max-influences", type=int, default=4)
    ap.add_argument("--side-band", type=float, default=0.02, help="fraction of height around X=0 shared by L and R")
    a = ap.parse_args(argv)
    C.force_object_mode()
    arm = bpy.data.objects.get(a.armature) if a.armature else next(o for o in bpy.data.objects if o.type == "ARMATURE")
    rep = C.Report("check_weights")
    meshes = armature_meshes(arm)
    if not meshes:
        rep.error("NO_MESH", "no mesh uses an Armature modifier with %s" % arm.name)
        return rep.dump(a.out)
    per = {}
    for ob in meshes:
        mod = next(m for m in ob.modifiers if m.type == "ARMATURE")
        if mod.use_deform_preserve_volume:
            rep.warn("PRESERVE_VOLUME", "%s: Preserve Volume is on; Unity uses linear blending, so Blender shows a "
                     "different deformation than the game" % ob.name)
        if mod.use_bone_envelopes:
            rep.warn("ENVELOPES", "%s: bone envelopes are on; Unity only reads vertex groups" % ob.name)
        info, _, _ = check_mesh(ob, arm, rep, a.max_influences, a.side_band)
        per[ob.name] = info
    rep.info["meshes"] = per
    accessory_check(meshes, arm, rep, tol=0.012 * max(arm.dimensions.z, 1e-3))
    if a.stress:
        stress_test(arm, meshes, rep)
    if a.png:
        rep.info["colors"] = weight_images(arm, meshes, a.png, [g for g in a.heat.split(",") if g])
    return rep.dump(a.out)
