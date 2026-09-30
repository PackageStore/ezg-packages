"""skin: weights that deform well, from Blender's bone heat plus the clean-up a rigger would do by hand.

  blender -b rigged.blend --python rk.py -- skin --spec skin_spec.json --out skinned.blend

Never hand-code weights from coordinates (z thresholds, x >= 0 splits): that is what produced "weights wrong
everywhere". Pipeline per soft mesh:
  1. split the big islands (the skin) from small ones (eyes, buttons, belt parts)
  2. bone heat (Armature Deform With Automatic Weights) on a copy of the skin with holes filled, deform bones only
  3. heat failed or left vertices without weight -> voxel proxy: remesh a closed copy, heat it, transfer back
     by nearest-surface interpolation
  4. small islands copy the weights of the skin surface under them (they move with it)
  5. side mask (left vertices never follow right bones), region locks (e.g. everything under a rigid helmet = Head)
  6. Laplacian smoothing, keep the 4 strongest influences, drop < min_weight, normalise
Rigid meshes (props, helmets) get 100% on one bone. Every mesh gets an Armature modifier with Preserve Volume OFF,
because Unity skins with linear blending: what you see in Blender must be what Unity shows.

Spec:
{
 "armature": "Rig_Hero",
 "soft": [{"object": "body", "method": "heat", "small_islands": "follow_surface", "small_island_max_frac": 0.08,
           "exclude_bones": ["Prop_*"], "side_band": 0.02,
           "locks": [{"bone": "Head", "z_min": 0.95, "falloff": 0.06}], "smooth": 3}],
 "rigid": [{"object": "axe", "bone": "Prop_Axe"}],
 "max_influences": 4, "min_weight": 0.01
}
"""
import argparse
import fnmatch
import os

import bmesh
import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

from . import common as C


def deform_bone_names(arm, exclude=()):
    out = []
    for b in arm.data.bones:
        if not b.use_deform:
            continue
        if any(fnmatch.fnmatch(b.name, pat) for pat in exclude):
            continue
        out.append(b.name)
    return out


def copy_islands(ob, keep_mask, fill_holes=True):
    """New object with only the vertices in keep_mask (order kept), holes filled (indices unchanged)."""
    me = ob.data.copy()
    me.name = "_rk_tmp_" + ob.name
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    drop = [v for v in bm.verts if not keep_mask[v.index]]
    if drop:
        bmesh.ops.delete(bm, geom=drop, context="VERTS")
    if fill_holes:
        boundary = [e for e in bm.edges if e.is_boundary]
        if boundary:
            bmesh.ops.holes_fill(bm, edges=boundary, sides=0)
    bm.to_mesh(me)
    bm.free()
    tmp = bpy.data.objects.new("_rk_tmp_" + ob.name, me)
    tmp.matrix_world = ob.matrix_world.copy()
    C.link_to_scene(tmp, ob)
    tmp.vertex_groups.clear()
    return tmp


def heat_weights(tmp, arm, bones):
    """Bone heat on tmp with only `bones` deforming. Returns W[n, len(bones)]."""
    saved = {b.name: b.use_deform for b in arm.data.bones}
    for b in arm.data.bones:
        b.use_deform = b.name in bones
    try:
        C.select_only([tmp, arm], arm)
        with C.ctx(arm, [tmp, arm]):
            res = bpy.ops.object.parent_set(type="ARMATURE_AUTO", keep_transform=True)
    finally:
        for b in arm.data.bones:
            b.use_deform = saved[b.name]
    W, _ = C.read_weights(tmp, bones)
    for m in list(tmp.modifiers):
        tmp.modifiers.remove(m)
    tmp.parent = None
    return W, res


def voxel_proxy(tmp, voxel):
    mod = tmp.modifiers.new("_rk_remesh", "REMESH")
    mod.mode = "VOXEL"
    mod.voxel_size = voxel
    mod.adaptivity = 0.0
    mod.use_smooth_shade = False
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(tmp.evaluated_get(dg))
    tmp.modifiers.remove(mod)
    px = bpy.data.objects.new("_rk_proxy", me)
    px.matrix_world = tmp.matrix_world.copy()
    C.link_to_scene(px, tmp)
    return px


def surface_sampler(src_co, src_tris):
    bvh = BVHTree.FromPolygons([tuple(p) for p in src_co], [tuple(t) for t in src_tris])

    def sample(points, Wsrc):
        out = np.zeros((len(points), Wsrc.shape[1]))
        for i, p in enumerate(points):
            loc, nor, ti, dist = bvh.find_nearest(Vector(p))
            if loc is None:
                continue
            a, b, c = src_tris[ti]
            A, B, Cc = Vector(src_co[a]), Vector(src_co[b]), Vector(src_co[c])
            # barycentric coordinates of loc in triangle ABC
            v0, v1, v2 = B - A, Cc - A, loc - A
            d00, d01, d11 = v0.dot(v0), v0.dot(v1), v1.dot(v1)
            d20, d21 = v2.dot(v0), v2.dot(v1)
            den = d00 * d11 - d01 * d01
            if abs(den) < 1e-20:
                wa, wb, wc = 1.0, 0.0, 0.0
            else:
                wb = (d11 * d20 - d01 * d21) / den
                wc = (d00 * d21 - d01 * d20) / den
                wa = 1.0 - wb - wc
            wa, wb, wc = max(wa, 0), max(wb, 0), max(wc, 0)
            s = wa + wb + wc or 1.0
            out[i] = (wa * Wsrc[a] + wb * Wsrc[b] + wc * Wsrc[c]) / s
        return out

    return sample


def laplacian(W, edges, mask, iterations, factor=0.5):
    """Smooth rows of W over mesh edges; only rows in mask change."""
    if iterations <= 0 or len(edges) == 0:
        return W
    a, b = edges[:, 0], edges[:, 1]
    deg = np.bincount(np.concatenate([a, b]), minlength=len(W)).astype(np.float64)
    deg[deg == 0] = 1
    for _ in range(iterations):
        S = np.zeros_like(W)
        np.add.at(S, a, W[b])
        np.add.at(S, b, W[a])
        mean = S / deg[:, None]
        W = np.where(mask[:, None], (1 - factor) * W + factor * mean, W)
    return W


def limit_normalize(W, max_inf, min_w, edges=None, mask=None):
    """Keep the max_inf strongest bones per vertex, drop weights < min_w, normalise.
    With edges: the bones are chosen from a smoothed copy of W, so neighbouring vertices keep the SAME set of
    bones. Picking the top 4 per vertex independently makes the 4th bone flip between neighbours (Spine here,
    UpperArm there) and the skin spikes when that bone moves."""
    W = np.clip(W, 0, None)
    s = W.sum(1, keepdims=True)
    W = np.divide(W, s, out=np.zeros_like(W), where=s > 0)
    W[W < min_w * 0.5] = 0.0
    if W.shape[1] > max_inf:
        pick = W if edges is None else laplacian(W, edges, np.ones(len(W), bool) if mask is None else mask, 3, 0.5)
        idx = np.argsort(-pick, axis=1)[:, max_inf:]
        np.put_along_axis(W, idx, 0.0, axis=1)
    s = W.sum(1, keepdims=True)
    W = np.divide(W, s, out=np.zeros_like(W), where=s > 0)
    keep_top = W.max(1, keepdims=True)
    W[(W < min_w) & (W < keep_top)] = 0.0
    s = W.sum(1, keepdims=True)
    return np.divide(W, s, out=np.zeros_like(W), where=s > 0)


LIMB_KEYS = {"arm": ("Shoulder", "UpperArm", "LowerArm", "Hand", "Arm", "ForeArm", "Thumb", "Index", "Middle",
                     "Ring", "Little", "Pinky"),
             "leg": ("UpperLeg", "LowerLeg", "Foot", "Toe", "UpLeg", "Leg")}


def limb_of(name):
    """('arm'|'leg', 'L'|'R') for limb bones, None for the torso (works for QuyChuan and Mixamo names)."""
    n = name.split(":")[-1]
    side = "L" if (n.endswith("_L") or n.startswith("Left")) else ("R" if (n.endswith("_R") or n.startswith("Right")) else None)
    if side is None:
        return None
    for kind in ("arm", "leg"):
        if any(k in n for k in LIMB_KEYS[kind]):
            if kind == "leg" and "Arm" in n:
                continue
            return (kind, side)
    return None


def geodesic_grow(seed, edges, co, radius):
    """Vertices within `radius` (metres, along mesh edges) of the seed set."""
    import heapq
    n = len(co)
    dist = np.full(n, np.inf)
    dist[seed] = 0.0
    nb = [[] for _ in range(n)]
    L = np.linalg.norm(co[edges[:, 0]] - co[edges[:, 1]], axis=1)
    for (a, b), l in zip(edges, L):
        nb[a].append((b, l))
        nb[b].append((a, l))
    heap = [(0.0, int(i)) for i in np.nonzero(seed)[0]]
    heapq.heapify(heap)
    while heap:
        d, v = heapq.heappop(heap)
        if d > dist[v] or d > radius:
            continue
        for u, l in nb[v]:
            nd = d + l
            if nd < dist[u] and nd <= radius:
                dist[u] = nd
                heapq.heappush(heap, (nd, u))
    return dist <= radius


def limb_masks(W, names, edges, co, grow):
    """Bone heat diffuses: an arm ends up weighting the chest front and even the face at a few percent, and the
    face is pulled when the arm swings. Each limb may only weight the vertices where that limb dominates, plus a
    blend band `grow` metres wide measured along the surface. Returns [(columns, allowed_mask)]."""
    limbs = {}
    for j, n in enumerate(names):
        lb = limb_of(n)
        if lb:
            limbs.setdefault(lb, []).append(j)
    if not limbs:
        return []
    torso = [j for j, n in enumerate(names) if limb_of(n) is None]
    groups = list(limbs.values()) + ([torso] if torso else [])
    S = np.stack([W[:, g].sum(1) for g in groups], 1)
    dom = np.argmax(S, 1)
    has = S.max(1) > 0
    out = []
    for gi, cols in enumerate(limbs.values()):
        own = (dom == gi) & has
        if own.any():
            out.append((cols, geodesic_grow(own, edges, co, grow)))
    return out


def apply_masks(W, masks):
    removed = 0
    for cols, allowed in masks:
        sub = W[:, cols]
        removed += int((sub[~allowed] > 0).sum())
        sub[~allowed] = 0.0
        W[:, cols] = sub
    return W, removed


def prune_far(W, arm, names, co, ratio=3.0, margin_frac=0.05):
    """Bone heat leaves long tails (an arm weighting the middle of the face at 0.04). Drop every weight whose
    bone is much farther than the nearest deform bone."""
    height = float(co[:, 2].max() - co[:, 2].min()) or 1.0
    D = np.zeros_like(W)
    for j, n in enumerate(names):
        b = arm.data.bones[n]
        h, t = np.array(b.head_local), np.array(b.tail_local)
        ht = t - h
        L2 = float(ht @ ht) or 1e-12
        s = np.clip(((co - h) @ ht) / L2, 0, 1)
        D[:, j] = np.linalg.norm(co - (h + s[:, None] * ht), axis=1)
    near = D.min(1, keepdims=True)
    far = D > ratio * near + margin_frac * height
    W = W.copy()
    had = W.sum(1) > 0
    W[far] = 0.0
    lost = had & (W.sum(1) <= 0)
    if lost.any():  # never leave a vertex without a bone: give it to the nearest one
        W[lost, np.argmin(D[lost], axis=1)] = 1.0
    return W, int((far & (W == 0)).sum())


def side_mask(W, names, x, band):
    for j, n in enumerate(names):
        if n.endswith("_R"):
            W[x > band, j] = 0.0
        elif n.endswith("_L"):
            W[x < -band, j] = 0.0
    return W


def apply_locks(W, names, co, locks):
    for lk in locks:
        j = names.index(lk["bone"])
        w = np.ones(len(co))
        if "z_min" in lk:
            f = float(lk.get("falloff", 0.0))
            z = co[:, 2]
            w = np.clip((z - (lk["z_min"] - f)) / f, 0, 1) if f > 0 else (z >= lk["z_min"]).astype(float)
        if "box" in lk:
            lo, hi = np.array(lk["box"][0]), np.array(lk["box"][1])
            w = w * np.all((co >= lo) & (co <= hi), axis=1)
        w = w * float(lk.get("weight", 1.0))
        W[:] = W * (1 - w[:, None])
        W[:, j] += w
    return W


def bind(ob, arm):
    for m in list(ob.modifiers):
        if m.type == "ARMATURE":
            ob.modifiers.remove(m)
    mw = ob.matrix_world.copy()
    ob.parent = arm
    ob.parent_type = "OBJECT"
    ob.matrix_parent_inverse = arm.matrix_world.inverted()
    ob.matrix_world = mw
    mod = ob.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    mod.use_vertex_groups = True
    mod.use_bone_envelopes = False
    mod.use_deform_preserve_volume = False  # Unity = linear blend skinning
    return mod


def skin_soft(ob, arm, sp, spec, rep):
    bones = deform_bone_names(arm, sp.get("exclude_bones", ["Prop_*"]))
    me = ob.data
    co = C.world_coords(ob)
    n = len(co)
    labels, n_isl = C.islands(me)
    counts = np.bincount(labels, minlength=n_isl)
    frac = float(sp.get("small_island_max_frac", 0.08))
    big = counts >= frac * n
    if not big.any():
        big[np.argmax(counts)] = True
    main = big[labels]
    height = float(co[:, 2].max() - co[:, 2].min())
    tmp = copy_islands(ob, main, fill_holes=True)
    method = sp.get("method", "heat")
    used = method
    Wm = None
    if method == "heat":
        Wm, res = heat_weights(tmp, arm, bones)
        cover = float((Wm.sum(1) > 1e-6).mean())
        empty = [b for j, b in enumerate(bones) if Wm[:, j].max() < 1e-3]
        rep.info.setdefault("heat", {})[ob.name] = {"coverage": C.r(cover), "empty_bones": empty,
                                                    "result": list(res) if res else None}
        if cover < 0.999:
            rep.warn("HEAT_PARTIAL", "%s: bone heat weighted only %.1f%% of the skin -> voxel proxy"
                     % (ob.name, 100 * cover))
            used = "proxy"
    if method == "proxy" or used == "proxy":
        px = voxel_proxy(tmp, float(sp.get("voxel", height / 160.0)))
        Wp, _ = heat_weights(px, arm, bones)
        pco = C.world_coords(px)
        ptri = C.triangles_array(px.data)
        rep.info.setdefault("proxy", {})[ob.name] = {"verts": len(pco), "coverage": C.r(float((Wp.sum(1) > 1e-6).mean()))}
        samp = surface_sampler(pco, ptri)
        tco = C.world_coords(tmp)
        Wm = samp(tco, Wp)
        C.remove_object(px)
        used = "proxy"
    rep.info.setdefault("method", {})[ob.name] = used
    # map back: the tmp copy kept the order of the main vertices
    W = np.zeros((n, len(bones)))
    main_idx = np.nonzero(main)[0]
    W[main_idx] = Wm[: len(main_idx)]
    edges = C.edges_array(me)
    masks = []
    if sp.get("limb_regions", True):
        masks = limb_masks(W, bones, edges, co, float(sp.get("limb_grow", 0.05)) * float(co[:, 2].ptp()))
        W, removed = apply_masks(W, masks)
        rep.info.setdefault("limb_region_weights_removed", {})[ob.name] = removed
    W[main_idx], pruned = prune_far(W[main_idx], arm, bones, co[main_idx], float(sp.get("prune_ratio", 3.0)))
    rep.info.setdefault("pruned_far_weights", {})[ob.name] = pruned
    # side mask and locks on the skin, then smooth, then small islands follow the smoothed skin
    x = co[:, 0]
    band = float(sp.get("side_band", 0.02)) * height
    if spec.get("symmetric", True):
        W = side_mask(W, bones, x, band)
    W = apply_locks(W, bones, co, sp.get("locks", []))
    W = laplacian(W, edges, main, int(sp.get("smooth", 2)), float(sp.get("smooth_factor", 0.5)))
    if spec.get("symmetric", True):
        W = side_mask(W, bones, x, band)
    W, _ = apply_masks(W, masks)  # smoothing leaks a few rings past the limb regions: cut again
    W = apply_locks(W, bones, co, sp.get("locks", []))
    small_idx = np.nonzero(~main)[0]
    if len(small_idx):
        mode = sp.get("small_islands", "follow_surface")
        tri = C.triangles_array(me)
        tri_main = tri[main[tri].all(1)]
        samp = surface_sampler(co, tri_main)
        if mode == "rigid_nearest":
            for k in np.unique(labels[small_idx]):
                ids = np.nonzero(labels == k)[0]
                cen = co[ids].mean(0)
                W[ids] = samp([cen], W)[0]
        else:
            W[small_idx] = samp(co[small_idx], W)
    if spec.get("symmetric", True):
        W = side_mask(W, bones, x, band)  # islands sampled across the midline must not bring the other side back
    W = limit_normalize(W, int(spec.get("max_influences", 4)), float(spec.get("min_weight", 0.02)), edges)
    unweighted = int((W.sum(1) < 0.5).sum())
    if unweighted:
        rep.error("UNWEIGHTED", "%s: %d vertices have no weight" % (ob.name, unweighted))
    ob.vertex_groups.clear()
    C.write_weights(ob, W, bones)
    bind(ob, arm)
    C.remove_object(tmp)
    rep.info.setdefault("soft", {})[ob.name] = {"verts": n, "islands": int(n_isl), "small_island_verts": int(len(small_idx)),
                                               "bones": len(bones)}


def skin_rigid(ob, arm, bone, rep):
    if bone not in arm.data.bones:
        rep.error("RIGID_BONE", "%s: bone %s does not exist" % (ob.name, bone))
        return
    ob.vertex_groups.clear()
    vg = ob.vertex_groups.new(name=bone)
    vg.add(list(range(len(ob.data.vertices))), 1.0, "REPLACE")
    bind(ob, arm)
    if not arm.data.bones[bone].use_deform:
        rep.error("RIGID_NODEFORM", "%s is weighted to %s which does not deform" % (ob.name, bone))


def main(argv):
    ap = argparse.ArgumentParser(prog="skin")
    ap.add_argument("--spec", required=True)
    ap.add_argument("--out", required=True)
    a = ap.parse_args(argv)
    spec = C.load_json(a.spec)
    C.force_object_mode()
    rep = C.Report("skin")
    arm = bpy.data.objects[spec["armature"]]
    for pb in arm.pose.bones:  # bind in the rest pose
        pb.location = (0, 0, 0)
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.rotation_euler = (0, 0, 0)
        pb.scale = (1, 1, 1)
    arm.data.pose_position = "REST"
    bpy.context.view_layer.update()
    for sp in spec.get("soft", []):
        skin_soft(bpy.data.objects[sp["object"]], arm, sp, spec, rep)
    for rg in spec.get("rigid", []):
        skin_rigid(bpy.data.objects[rg["object"]], arm, rg["bone"], rep)
    arm.data.pose_position = "POSE"
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(a.out), copy=True)
    rep.info["saved"] = os.path.abspath(a.out)
    return rep.dump(os.path.splitext(a.out)[0] + "_skin.json")
