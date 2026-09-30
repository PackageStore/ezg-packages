"""inspect: inventory of a source .blend before rigging.

Reports per mesh: size, islands, non-manifold edges, duplicate vertices, symmetry, unapplied transforms,
and hints for the rig (scale to game size, rigid parts, heat-weighting risk).

  blender -b model.blend --factory-startup --python rk.py -- inspect --out report.json
"""
import argparse

import bpy
import numpy as np
from mathutils.kdtree import KDTree

from . import common as C


def mesh_stats(ob, height):
    me = ob.data
    co = C.world_coords(ob)
    n = len(co)
    st = {"verts": n, "faces": len(me.polygons)}
    if n == 0:
        return st
    labels, n_isl = C.islands(me)
    st["islands"] = n_isl
    counts = np.bincount(labels)
    order = np.argsort(-counts)
    isl = []
    for k in order[:12]:
        pts = co[labels == k]
        isl.append({"island": int(k), "verts": int(counts[k]), "min": C.r(pts.min(0)), "max": C.r(pts.max(0))})
    st["largest_islands"] = isl
    # edges used by != 2 faces
    face_count = np.zeros(len(me.edges), dtype=np.int64)
    ek_index = {tuple(sorted(e.vertices)): e.index for e in me.edges}
    for p in me.polygons:
        for ek in p.edge_keys:
            face_count[ek_index[tuple(sorted(ek))]] += 1
    st["boundary_edges"] = int((face_count == 1).sum())
    st["nonmanifold_edges"] = int((face_count > 2).sum())
    st["loose_edges"] = int((face_count == 0).sum())
    # duplicate vertices (merge-by-distance candidates)
    kd = KDTree(n)
    for i, p in enumerate(co):
        kd.insert(p, i)
    kd.balance()
    tol = max(height * 1e-5, 1e-7)
    dup = 0
    for i, p in enumerate(co):
        if len(kd.find_range(p, tol)) > 1:
            dup += 1
    st["duplicate_verts"] = dup
    # symmetry across X: fraction of vertices with a mirrored partner
    stol = height * 0.004
    hits = 0
    for p in co:
        _, _, d = kd.find((-p[0], p[1], p[2]))
        if d is not None and d < stol:
            hits += 1
    st["x_symmetry"] = round(hits / n, 3)
    st["center_x"] = C.r(float(co[:, 0].mean()))
    return st


def main(argv):
    ap = argparse.ArgumentParser(prog="inspect")
    ap.add_argument("--out", required=True)
    a = ap.parse_args(argv)
    C.force_object_mode()
    rep = C.Report("inspect")
    sc = bpy.context.scene
    rep.info["blender"] = bpy.app.version_string
    rep.info["scene"] = {"fps": sc.render.fps, "fps_base": sc.render.fps_base, "unit_system": sc.unit_settings.system,
                         "unit_scale": sc.unit_settings.scale_length, "frame_range": [sc.frame_start, sc.frame_end]}
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    allco = np.concatenate([C.world_coords(o) for o in meshes if len(o.data.vertices)]) if meshes else np.zeros((1, 3))
    lo, hi = allco.min(0), allco.max(0)
    height = float(hi[2] - lo[2])
    rep.info["bbox"] = {"min": C.r(lo), "max": C.r(hi), "height": C.r(height)}
    objs = []
    for ob in bpy.data.objects:
        d = {"name": ob.name, "type": ob.type, "parent": ob.parent.name if ob.parent else None,
             "loc": C.r(ob.location), "rot_deg": C.r([v * C.R2D for v in ob.rotation_euler], 2), "scale": C.r(ob.scale),
             "modifiers": [(m.name, m.type) for m in ob.modifiers], "vertex_groups": len(ob.vertex_groups),
             "materials": [m.name for m in getattr(ob.data, "materials", []) if m]}
        if ob.type == "MESH":
            d.update(mesh_stats(ob, height))
            if any(abs(s - 1) > 1e-4 for s in ob.scale) or any(abs(v) > 1e-4 for v in ob.rotation_euler):
                rep.warn("UNAPPLIED_TRANSFORM", "%s has rotation/scale not applied: bake it into the mesh before skinning"
                         % ob.name)
            if d.get("nonmanifold_edges", 0) or d.get("boundary_edges", 0) > 0.02 * d.get("verts", 1):
                rep.warn("OPEN_MESH", "%s: %d boundary / %d non-manifold edges -> bone heat may fail, plan the voxel proxy"
                         % (ob.name, d.get("boundary_edges", 0), d.get("nonmanifold_edges", 0)))
            if d.get("duplicate_verts", 0):
                rep.warn("DUPLICATE_VERTS", "%s: %d vertices overlap another vertex (split seams); weights must be equal "
                         "on both, heat weighting treats them as separate islands" % (ob.name, d["duplicate_verts"]))
            if d.get("islands", 1) > 1:
                rep.warn("ISLANDS", "%s has %d loose parts: map each rigid part to one bone in the skin spec"
                         % (ob.name, d["islands"]))
            if d.get("x_symmetry", 1) < 0.8:
                rep.warn("ASYMMETRIC", "%s: only %.0f%% of vertices have an X mirror; do not mirror weights blindly"
                         % (ob.name, 100 * d["x_symmetry"]))
        objs.append(d)
    rep.info["objects"] = objs
    rep.info["armatures"] = [{"name": a.name, "bones": len(a.bones)} for a in bpy.data.armatures]
    rep.info["actions"] = [{"name": a.name, "range": C.r(a.frame_range, 1)} for a in bpy.data.actions]
    rep.info["images"] = [{"name": i.name, "path": i.filepath, "size": list(i.size)} for i in bpy.data.images]
    if height < 0.5 or height > 4.0:
        rep.warn("SCALE", "model is %.3f m tall: scale it to game size (1 unit = 1 m) and apply before rigging" % height)
    if abs(lo[2]) > 0.01 * max(height, 1e-6):
        rep.warn("GROUND", "lowest point is at z=%.4f, not on the ground (z=0)" % lo[2])
    cx = 0.5 * (lo[0] + hi[0])
    if abs(cx) > 0.01 * max(height, 1e-6):
        rep.warn("CENTER", "model is not centred on X=0 (centre %.4f); symmetric rigging assumes X=0" % cx)
    return rep.dump(a.out)
