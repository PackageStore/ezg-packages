"""prepare: make the source model rig-ready and save it as a NEW file (the source is never saved).

  * leave Edit Mode everywhere, bake object transforms into the meshes (location 0, rotation 0, scale 1)
  * scale to game size (1 unit = 1 m): --height H (top of --ref meshes) or --scale S; --height is kept in the file
    (scene rk_height) as the character height check_anim scales its tolerances with
  * put the feet on the ground (lowest point of --ground-mesh at z = 0) and centre the body on X = 0
  * scene at 30 fps (GameAnimation_QuyChuan 3.1), metric, unit scale 1

  blender -b src.blend --factory-startup --python rk.py -- prepare --scale 3.4 --ground-mesh body --out work.blend
"""
import argparse
import os

import bpy
import numpy as np
from mathutils import Matrix

from . import common as C


def main(argv):
    ap = argparse.ArgumentParser(prog="prepare")
    ap.add_argument("--out", required=True)
    ap.add_argument("--scale", type=float, default=0.0)
    ap.add_argument("--height", type=float, default=0.0, help="target height of the --ref meshes (m)")
    ap.add_argument("--ref", default="", help="comma list of meshes that define the height (default: all)")
    ap.add_argument("--ground-mesh", default="", help="mesh whose lowest point goes to z=0 and centre to x=0")
    ap.add_argument("--fps", type=int, default=30)
    a = ap.parse_args(argv)
    C.force_object_mode()
    rep = C.Report("prepare")
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    for ob in meshes:
        if ob.parent is not None:
            mw = ob.matrix_world.copy()
            ob.parent = None
            ob.matrix_world = mw
        if ob.data.users > 1:
            ob.data = ob.data.copy()
        ob.data.transform(ob.matrix_world)
        ob.matrix_world = Matrix.Identity(4)
        if ob.data.shape_keys:
            rep.warn("SHAPE_KEYS", "%s has shape keys: they were transformed with the mesh, check them" % ob.name)
    refs = [bpy.data.objects[n] for n in a.ref.split(",") if n] or meshes
    pts = np.concatenate([C.world_coords(o) for o in refs])
    h = float(pts[:, 2].max() - pts[:, 2].min())
    s = a.scale or (a.height / h if a.height else 1.0)
    gm = bpy.data.objects.get(a.ground_mesh) if a.ground_mesh else None
    gpts = C.world_coords(gm) if gm else pts
    shift = np.array([-(gpts[:, 0].max() + gpts[:, 0].min()) * 0.5, 0.0, -gpts[:, 2].min()]) if gm else np.zeros(3)
    if abs(shift[0]) < 1e-5:
        shift[0] = 0.0
    M = Matrix.Scale(s, 4) @ Matrix.Translation(shift.tolist())
    for ob in meshes:
        ob.data.transform(M)
        ob.data.update()
    sc = bpy.context.scene
    if a.height:
        sc["rk_height"] = float(a.height)   # the declared character height: check_anim scales its tolerances with it
    sc.render.fps = a.fps
    sc.render.fps_base = 1.0
    sc.unit_settings.system = "METRIC"
    sc.unit_settings.scale_length = 1.0
    pts2 = np.concatenate([C.world_coords(o) for o in refs])
    rep.info.update({"scale": C.r(s, 5), "shift_before_scale": C.r(shift), "height_before": C.r(h),
                     "height_after": C.r(float(pts2[:, 2].max() - pts2[:, 2].min())),
                     "ground_z_after": C.r(float(C.world_coords(gm)[:, 2].min()) if gm else float(pts2[:, 2].min()))})
    bpy.context.preferences.filepaths.save_version = 0
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(a.out), copy=True)
    rep.info["saved"] = os.path.abspath(a.out)
    return rep.dump(a.out.replace(".blend", "_prepare.json"))
