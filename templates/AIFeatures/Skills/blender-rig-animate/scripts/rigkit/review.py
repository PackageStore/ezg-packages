"""review: Workbench renders for visual checks (headless-safe).

Library functions used by other steps:
  sheet(setters, views, out_png, ...)   rows = views, columns = poses/frames
  bone_sticks(arm_ob)                   temporary mesh that shows bones in renders (armatures do not render)
  markers(points)                       temporary spheres at landmark positions
  weight_colors(ob, group)              write a heat-map color attribute for one vertex group

Step:
  blender -b rig.blend --python rk.py -- review --action Idle --frames 0,10,20 --views front,side,game --out x.png
      [--video idle.mp4 --video-view game --loops 3]
The sheet shows poses and silhouettes; timing and spacing only show in motion, so send the video too.
"""
import argparse
import json
import math
import os

import bpy
import numpy as np
from mathutils import Matrix, Vector

from . import common as C

TMP_PREFIX = "_rk_"

# view name -> (direction from target to camera, orthographic?)
VIEWS = {
    "front": ((0, -1, 0), True),
    "back": ((0, 1, 0), True),
    "side": ((1, 0, 0), True),        # from the character's left
    "side_r": ((-1, 0, 0), True),
    "top": ((0, -0.001, 1), True),
    "front34": ((-0.55, -1.0, 0.45), False),
    "left34": ((0.8, -0.85, 0.4), False),
    "back34": ((0.7, 0.9, 0.45), False),
    "game": ((0.0, -1.0, 1.0), False),  # top-down 45 degrees, like most mobile gameplay cameras
    "low": ((-0.3, -1.0, 0.05), False),
}


def _scene_box(objs):
    pts = []
    for ob in objs:
        if ob.type == "MESH" and len(ob.data.vertices):
            pts.append(C.world_coords(ob, evaluated=True))
    if not pts:
        return None
    p = np.concatenate(pts)
    return p.min(0), p.max(0)


def _box_bounds(lo, hi):
    return Vector(((lo + hi) * 0.5).tolist()), float(np.linalg.norm(hi - lo))


def setup(size=360, color_type="OBJECT", xray=0.0, outline=True):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.view_settings.view_transform = "Standard"  # AgX / Filmic wash the review colours out
    sc.view_settings.look = "None"
    sh = sc.display.shading
    sh.light = "STUDIO"
    sh.color_type = color_type
    sh.show_cavity = False
    sh.show_object_outline = outline
    sh.show_xray = xray > 0
    if xray > 0:
        sh.xray_alpha = xray
    sh.show_shadows = False
    sc.render.resolution_x = size
    sc.render.resolution_y = size
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = "PNG"
    sc.render.film_transparent = False
    cam = bpy.data.objects.get(TMP_PREFIX + "cam")
    if cam is None:
        cd = bpy.data.cameras.new(TMP_PREFIX + "cam")
        cd.clip_start = 0.001
        cd.clip_end = 1000
        cam = bpy.data.objects.new(TMP_PREFIX + "cam", cd)
        sc.collection.objects.link(cam)
    sc.camera = cam
    return cam


def ground(size, z=0.0, color=(0.33, 0.4, 0.33, 1)):
    ob = bpy.data.objects.get(TMP_PREFIX + "ground")
    if ob is None:
        me = bpy.data.meshes.new(TMP_PREFIX + "ground")
        me.from_pydata([(-1, -1, 0), (1, -1, 0), (1, 1, 0), (-1, 1, 0)], [], [(0, 1, 2, 3)])
        ob = bpy.data.objects.new(TMP_PREFIX + "ground", me)
        bpy.context.scene.collection.objects.link(ob)
    ob.scale = (size, size, 1)
    ob.location = (0, 0, z)
    ob.color = color
    return ob


def place_camera(cam, view, center, extent, lens=50.0, margin=1.15):
    d, ortho = VIEWS[view]
    d = Vector(d).normalized()
    cam.data.type = "ORTHO" if ortho else "PERSP"
    if ortho:
        cam.data.ortho_scale = extent * margin
        dist = extent * 3.0
    else:
        cam.data.lens = lens
        fov = 2 * math.atan(18.0 / lens)
        dist = (extent * margin * 0.5) / math.tan(fov * 0.5)
    cam.location = center + d * dist
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()


def _render_array(path):
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(path, check_existing=False)
    w, h = img.size
    arr = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    bpy.data.images.remove(img)
    return arr


def save_array(arr, path):
    h, w = arr.shape[:2]
    out = bpy.data.images.new(TMP_PREFIX + "sheet", w, h, alpha=True)
    out.pixels = arr.ravel()
    out.filepath_raw = path
    out.file_format = "PNG"
    out.save()
    bpy.data.images.remove(out)


def sheet(setters, views, out_path, size=320, color_type="OBJECT", xray=0.0, frame_objects=None, fixed_bounds=None,
          lens=50.0, show_ground=True, rows="views"):
    """Render a grid: one row per view, one column per setter (a callable that poses the scene).
    The camera framing is computed once over every setter (or fixed_bounds=(center, extent)): the character
    moves inside a fixed frame, and props thrown away (a Die clip) stay in the picture. rows="setters" puts one
    pose per row."""
    out_path = os.path.abspath(out_path)
    cam = setup(size, color_type, xray)
    objs = frame_objects or [o for o in bpy.context.scene.objects if o.type == "MESH" and not o.name.startswith(TMP_PREFIX)]
    if fixed_bounds is None:
        lo = hi = None
        for st in setters:
            st()
            bpy.context.view_layer.update()
            box = _scene_box(objs)
            if box is not None:
                lo = box[0] if lo is None else np.minimum(lo, box[0])
                hi = box[1] if hi is None else np.maximum(hi, box[1])
        center, extent = _box_bounds(lo, hi) if lo is not None else (Vector((0, 0, 0)), 1.0)
    else:
        center, extent = fixed_bounds
    center = Vector(center)
    g = ground(extent * 2.0) if show_ground else None
    tmp = os.path.join(os.path.dirname(os.path.abspath(out_path)), TMP_PREFIX + "frame.png")
    grid = {}
    for view in views:
        if g is not None:
            g.hide_render = view in ("top",) or VIEWS[view][0][2] < -0.1
        for i, st in enumerate(setters):
            st()
            bpy.context.view_layer.update()
            place_camera(cam, view, center, extent, lens)
            grid[(view, i)] = _render_array(tmp)
    if rows == "views":
        lines = [np.concatenate([grid[(v, i)] for i in range(len(setters))], axis=1) for v in views]
    else:
        lines = [np.concatenate([grid[(v, i)] for v in views], axis=1) for i in range(len(setters))]
    full = np.concatenate(lines[::-1], axis=0)  # image rows are bottom-up
    save_array(full, out_path)
    if os.path.exists(tmp):
        os.remove(tmp)
    return out_path


def video(arm, act, out_path, view="game", size=480, color_type="TEXTURE", loops=3, lens=50.0):
    """MP4 (H.264) of one action from one view, fixed camera framing every frame of it; a loop plays `loops` times.
    Returns the path written, or None when this Blender has no FFmpeg."""
    sc = bpy.context.scene
    out_path = os.path.abspath(out_path)
    meta = json.loads(act.get("rk_clip", "{}")) if act.get("rk_clip") else {}
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    loop = bool(meta.get("loop", act.use_cyclic))
    cam = setup(size, color_type)
    objs = [o for o in sc.objects if o.type == "MESH" and not o.name.startswith(TMP_PREFIX)]
    C.assign_action(arm, act)
    lo = hi = None
    for f in range(f0, f1 + 1, 2):
        sc.frame_set(f)
        box = _scene_box(objs)
        if box is not None:
            lo = box[0] if lo is None else np.minimum(lo, box[0])
            hi = box[1] if hi is None else np.maximum(hi, box[1])
    center, extent = _box_bounds(lo, hi) if lo is not None else (Vector((0, 0, 0)), 1.0)
    g = ground(extent * 2.0)
    g.hide_render = VIEWS[view][0][2] < -0.1
    place_camera(cam, view, Vector(center), extent, lens)
    end, track = f1, None
    if loop and loops > 1:  # repeat through the NLA; the last frame of a loop is its first, so play f0..f1-1
        ad = arm.animation_data
        ad.action = None
        track = ad.nla_tracks.new()
        strip = track.strips.new(act.name, f0, act)
        if hasattr(strip, "action_slot") and len(getattr(act, "slots", [])):
            strip.action_slot = act.slots[0]
        strip.action_frame_start, strip.action_frame_end = f0, f1
        strip.repeat = loops
        end = f0 + (f1 - f0) * loops - 1
    old = (sc.frame_start, sc.frame_end, sc.render.fps, sc.render.filepath)
    sc.frame_start, sc.frame_end = f0, end
    sc.render.fps = int(meta.get("fps", sc.render.fps))
    tmp = os.path.join(os.path.dirname(out_path), TMP_PREFIX + "video_")
    written = None
    try:
        sc.render.image_settings.file_format = "FFMPEG"
        sc.render.ffmpeg.format = "MPEG4"
        sc.render.ffmpeg.codec = "H264"
        sc.render.ffmpeg.constant_rate_factor = "HIGH"
        sc.render.filepath = tmp
        bpy.ops.render.render(animation=True)
        folder = os.path.dirname(out_path)
        made = sorted((os.path.join(folder, n) for n in os.listdir(folder) if n.startswith(TMP_PREFIX + "video_")),
                      key=os.path.getmtime)
        if made:  # Blender appends the frame range to the name
            os.replace(made[-1], out_path)
            written = out_path
    except (TypeError, RuntimeError) as e:
        print("REVIEW video skipped:", e)
    finally:
        sc.render.image_settings.file_format = "PNG"
        sc.frame_start, sc.frame_end, sc.render.fps, sc.render.filepath = old
        if track is not None:
            arm.animation_data.nla_tracks.remove(track)
        C.assign_action(arm, act)
    return written


# --------------------------------------------------------------------------- helper geometry for renders
SIDE_COLORS = {"L": (0.2, 0.45, 1.0, 1), "R": (1.0, 0.25, 0.2, 1), "C": (1.0, 0.85, 0.1, 1)}


def _side(name):
    if name.endswith("_L") or name.endswith(".L"):
        return "L"
    if name.endswith("_R") or name.endswith(".R"):
        return "R"
    return "C"


def bone_sticks(arm_ob, radius=None, only_deform=False):
    """Build (or rebuild) one mesh object per bone showing the bone as a tapered stick, parented to the bone,
    so it follows the pose. Colour: left blue, right red, centre yellow."""
    clear_prefix(TMP_PREFIX + "bone_")
    L = max((b.length for b in arm_ob.data.bones), default=0.1)
    obs = []
    for b in arm_ob.data.bones:
        if only_deform and not b.use_deform:
            continue
        rr = radius or max(b.length * 0.12, L * 0.03)
        me = bpy.data.meshes.new(TMP_PREFIX + "bone_" + b.name)
        # octahedron from head (0,0,0) to tail (0,length,0) in bone space
        ln = b.length
        w = rr
        verts = [(0, 0, 0), (w, ln * 0.2, 0), (0, ln * 0.2, w), (-w, ln * 0.2, 0), (0, ln * 0.2, -w), (0, ln, 0)]
        faces = [(0, 1, 2), (0, 2, 3), (0, 3, 4), (0, 4, 1), (5, 2, 1), (5, 3, 2), (5, 4, 3), (5, 1, 4)]
        me.from_pydata(verts, [], faces)
        ob = bpy.data.objects.new(me.name, me)
        bpy.context.scene.collection.objects.link(ob)
        ob.parent = arm_ob
        ob.parent_type = "BONE"
        ob.parent_bone = b.name
        # parent_type BONE puts the child at the bone TAIL; move it back to the head
        ob.matrix_parent_inverse = Matrix.Identity(4)
        ob.location = (0, -ln, 0)
        ob.color = SIDE_COLORS[_side(b.name)]
        ob.show_in_front = True
        obs.append(ob)
    return obs


def markers(points, radius, color=(1, 0, 1, 1), name="mark"):
    """points: dict name -> xyz. Returns objects."""
    obs = []
    for k, p in points.items():
        me = bpy.data.meshes.new(TMP_PREFIX + name + "_" + k)
        import bmesh
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=10, v_segments=6, radius=radius)
        bm.to_mesh(me)
        bm.free()
        ob = bpy.data.objects.new(me.name, me)
        bpy.context.scene.collection.objects.link(ob)
        ob.location = p
        ob.color = color if not isinstance(color, dict) else color.get(k, (1, 0, 1, 1))
        obs.append(ob)
    return obs


def polyline(points, radius, color=(1, 1, 1, 1), name="trail"):
    """A thin tube through the given points (motion trail)."""
    cu = bpy.data.curves.new(TMP_PREFIX + name, "CURVE")
    cu.dimensions = "3D"
    cu.bevel_depth = radius
    cu.bevel_resolution = 2
    sp = cu.splines.new("POLY")
    sp.points.add(len(points) - 1)
    for i, p in enumerate(points):
        sp.points[i].co = (p[0], p[1], p[2], 1)
    ob = bpy.data.objects.new(TMP_PREFIX + name, cu)
    bpy.context.scene.collection.objects.link(ob)
    ob.color = color
    return ob


def clear_prefix(prefix=TMP_PREFIX):
    for ob in list(bpy.data.objects):
        if ob.name.startswith(prefix):
            C.remove_object(ob)
    for cu in list(bpy.data.curves):
        if cu.name.startswith(prefix) and cu.users == 0:
            bpy.data.curves.remove(cu)


def weight_colors(ob, group, attr="rk_weight"):
    """Heat-map colour attribute (blue 0 -> green -> red 1, black = not in group) for Workbench ATTRIBUTE shading."""
    me = ob.data
    if attr in me.color_attributes:
        me.color_attributes.remove(me.color_attributes[attr])
    ca = me.color_attributes.new(attr, "FLOAT_COLOR", "POINT")
    me.color_attributes.active_color = ca
    vg = ob.vertex_groups.get(group)
    n = len(me.vertices)
    w = np.full(n, -1.0)
    if vg is not None:
        gi = vg.index
        for v in me.vertices:
            for g in v.groups:
                if g.group == gi:
                    w[v.index] = g.weight
    col = np.zeros((n, 4), dtype=np.float32)
    col[:, 3] = 1
    m = w >= 0
    t = np.clip(w[m], 0, 1)
    # blue (0) -> green (0.5) -> red (1); black = vertex not in the group
    col[m, 0] = np.clip((t - 0.5) * 2.0, 0, 1)
    col[m, 1] = np.clip(1.0 - np.abs(t - 0.5) * 2.0 + 0.1, 0, 1)
    col[m, 2] = np.clip((0.5 - t) * 2.0, 0, 1)
    col[~m] = (0.05, 0.05, 0.05, 1)
    ca.data.foreach_set("color", col.ravel())
    return attr


# --------------------------------------------------------------------------- weight segmentation view
PALETTE = [(0.90, 0.10, 0.29), (0.24, 0.71, 0.29), (1.00, 0.88, 0.10), (0.00, 0.51, 0.78), (0.96, 0.51, 0.19),
           (0.57, 0.12, 0.71), (0.27, 0.94, 0.94), (0.94, 0.20, 0.90), (0.82, 0.96, 0.24), (0.98, 0.75, 0.83),
           (0.00, 0.50, 0.50), (0.86, 0.75, 1.00), (0.67, 0.43, 0.16), (1.00, 0.98, 0.78), (0.50, 0.00, 0.00),
           (0.67, 1.00, 0.76), (0.50, 0.50, 0.00), (1.00, 0.84, 0.71), (0.00, 0.00, 0.50), (0.50, 0.50, 0.50),
           (1.00, 1.00, 1.00), (0.40, 0.25, 0.60), (0.10, 0.30, 0.10), (0.85, 0.55, 0.40)]


def segment_colors(ob, names, attr="rk_seg"):
    """Colour = weight-blended colour of the bones: one image shows every region and how smoothly they blend.
    A patch of the wrong colour = vertices following the wrong bone."""
    me = ob.data
    if attr in me.color_attributes:
        me.color_attributes.remove(me.color_attributes[attr])
    ca = me.color_attributes.new(attr, "FLOAT_COLOR", "POINT")
    me.color_attributes.active_color = ca
    W, _ = C.read_weights(ob, names)
    pal = np.array([PALETTE[i % len(PALETTE)] for i in range(len(names))], dtype=np.float64)
    s = W.sum(1, keepdims=True)
    rgb = np.divide(W @ pal, s, out=np.zeros((len(W), 3)), where=s > 1e-6)
    col = np.ones((len(W), 4), dtype=np.float32)
    col[:, :3] = rgb
    col[s[:, 0] <= 1e-6, :3] = 0.0
    ca.data.foreach_set("color", col.ravel())
    return {n: "#%02x%02x%02x" % tuple(int(255 * c) for c in PALETTE[i % len(PALETTE)]) for i, n in enumerate(names)}


def legend_png(names, out_path, row_px=26, width_px=260):
    """Colour swatches with bone names (text objects rendered by Workbench)."""
    sc = bpy.context.scene
    shown = [o for o in sc.objects if not o.hide_render]
    for o in shown:
        o.hide_render = True
    made = []
    base = Vector((1000.0, 1000.0, 0.0))
    for i, n in enumerate(names):
        me = bpy.data.meshes.new(TMP_PREFIX + "sw" + str(i))
        me.from_pydata([(0, 0, 0), (0.8, 0, 0), (0.8, 0.8, 0), (0, 0.8, 0)], [], [(0, 1, 2, 3)])
        sw = bpy.data.objects.new(me.name, me)
        sc.collection.objects.link(sw)
        sw.location = base + Vector((0, -i * 1.0, 0))
        c = PALETTE[i % len(PALETTE)]
        sw.color = (c[0], c[1], c[2], 1)
        cu = bpy.data.curves.new(TMP_PREFIX + "tx" + str(i), "FONT")
        cu.body = n
        cu.size = 0.75
        tx = bpy.data.objects.new(cu.name, cu)
        sc.collection.objects.link(tx)
        tx.location = base + Vector((1.1, -i * 1.0 + 0.1, 0))
        tx.color = (0.95, 0.95, 0.95, 1)
        made += [sw, tx]
    cam = setup(size=width_px, color_type="OBJECT")
    rows_h = len(names) * 1.0 + 0.4
    sc.render.resolution_x = width_px
    sc.render.resolution_y = int(row_px * rows_h)
    cols_w = rows_h * width_px / sc.render.resolution_y
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = max(cols_w, rows_h)
    cam.location = base + Vector((cols_w * 0.5 - 0.2, -(len(names) - 1) + rows_h * 0.5 - 0.3, 10))
    cam.rotation_euler = (0, 0, 0)
    sc.render.filepath = os.path.abspath(out_path)
    bpy.ops.render.render(write_still=True)
    for o in made:
        C.remove_object(o)
    for cu in list(bpy.data.curves):
        if cu.name.startswith(TMP_PREFIX) and cu.users == 0:
            bpy.data.curves.remove(cu)
    for o in shown:
        o.hide_render = False
    return os.path.abspath(out_path)


def hstack_png(paths, out_path):
    arrs = []
    for p in paths:
        img = bpy.data.images.load(os.path.abspath(p), check_existing=False)
        w, h = img.size
        arrs.append(np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4))
        bpy.data.images.remove(img)
    H = max(a.shape[0] for a in arrs)
    padded = []
    for a in arrs:
        if a.shape[0] < H:
            pad = np.zeros((H - a.shape[0], a.shape[1], 4), dtype=np.float32)
            pad[..., 3] = 1
            pad[..., :3] = 0.23
            a = np.concatenate([pad, a], axis=0)  # rows are bottom-up: the pad goes under the image
        padded.append(a)
    save_array(np.concatenate(padded, axis=1), os.path.abspath(out_path))
    return out_path


# --------------------------------------------------------------------------- step
def main(argv):
    ap = argparse.ArgumentParser(prog="review")
    ap.add_argument("--armature", default=None)
    ap.add_argument("--action", default=None, help="action name (or comma list); frames are rendered per action")
    ap.add_argument("--frames", default="", help="comma list of frames; empty = keys of the action")
    ap.add_argument("--step", type=int, default=0, help="render every N frames instead of --frames")
    ap.add_argument("--views", default="front34,side,game")
    ap.add_argument("--size", type=int, default=300)
    ap.add_argument("--textured", action="store_true")
    ap.add_argument("--bones", action="store_true", help="draw bone sticks (x-ray)")
    ap.add_argument("--trail", default="", help="comma list of bone names: draw the path of their tails")
    ap.add_argument("--out", required=True)
    ap.add_argument("--video", default="", help="also write an MP4 of the action (one per action: name_<action>.mp4)")
    ap.add_argument("--video-view", default="game")
    ap.add_argument("--video-size", type=int, default=480)
    ap.add_argument("--loops", type=int, default=3, help="how many times a loop clip plays in the video")
    a = ap.parse_args(argv)
    C.force_object_mode()
    arm = bpy.data.objects.get(a.armature) if a.armature else next((o for o in bpy.data.objects if o.type == "ARMATURE"), None)
    sc = bpy.context.scene
    actions = [bpy.data.actions[n] for n in a.action.split(",")] if a.action else [None]
    outs = []
    for act in actions:
        if act is not None and arm is not None:
            C.assign_action(arm, act)
            f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
        else:
            f0, f1 = sc.frame_start, sc.frame_end
        if a.frames:
            frames = [int(x) for x in a.frames.split(",")]
        elif a.step:
            frames = list(range(f0, f1 + 1, a.step))
            if frames[-1] != f1:
                frames.append(f1)
        elif act is not None:
            ks = sorted({int(round(k.co.x)) for fc in C.action_fcurves(act) for k in fc.keyframe_points})
            frames = ks if len(ks) <= 12 else ks[::max(1, len(ks) // 10)]
        else:
            frames = [sc.frame_current]
        if a.bones and arm is not None:
            bone_sticks(arm)
        if a.trail and arm is not None:
            for bn in a.trail.split(","):
                pts = []
                for f in range(f0, f1 + 1):
                    sc.frame_set(f)
                    pb = arm.pose.bones[bn]
                    pts.append(arm.matrix_world @ pb.tail)
                polyline(pts, radius=0.004 * (arm.dimensions.length or 1), color=(1, 0.2, 1, 1), name="trail_" + bn)

        def setter(f):
            return lambda: sc.frame_set(f)

        out = a.out if len(actions) == 1 else a.out.replace(".png", "_%s.png" % act.name)
        sheet([setter(f) for f in frames], a.views.split(","), out, size=a.size,
              color_type="TEXTURE" if a.textured else "OBJECT", xray=0.55 if a.bones else 0.0)
        print("REVIEW", out, "frames", frames)
        outs.append(out)
        if a.video and act is not None and arm is not None:
            vp = a.video if len(actions) == 1 else a.video.replace(".mp4", "_%s.mp4" % act.name)
            got = video(arm, act, vp, view=a.video_view, size=a.video_size,
                        color_type="TEXTURE" if a.textured else "OBJECT", loops=a.loops)
            print("REVIEW video", got)
    clear_prefix()
    return True
