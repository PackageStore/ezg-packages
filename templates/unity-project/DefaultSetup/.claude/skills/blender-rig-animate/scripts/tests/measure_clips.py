"""Measure clips with the metrics of check_anim's hub / amplitude gates, to calibrate them on clips the team judged.
Read-only: nothing is saved.

  blender -b --factory-startup --python tests/measure_clips.py -- fbx <folder> <idle.fbx> <clip.fbx> ...
  blender -b anim.blend --factory-startup --python tests/measure_clips.py -- blend <idle action> <action> ...

For each clip: the rotation range of the upper-body bones (mean over bones and the largest) and, against the first
frame of the first clip (the hub), how far its first and last poses are (mean over bones and the worst bone).
The fbx mode reads skeletons imported as armatures or as empties (3ds Max bipeds, e.g. the ExplosiveLLC pack).
Numbers found on 2026-09-28: ExplosiveLLC idles range 15-16 mean, attacks 42-86; attacks start 2.5 (Unarmed) and 9.2
(2Hand-Sword) mean away from the idle; a code-generated chibi idle the team rejected 5.6.
"""
import math
import os
import sys

import bpy

LEGS = ("thigh", "calf", "foot", "toe", "upleg", "leftleg", "rightleg", "lowerleg", "upperleg")
SKIP = ("nub", "weapon", "root", "motion", "prop", "finger", "_end")  # root / root-motion nodes, like Root in check_anim


def ang(q0, q1):
    return math.degrees(2.0 * math.acos(min(1.0, abs(q0.dot(q1)))))


def armature_frames(arm, act):
    arm.animation_data_create()
    arm.animation_data.action = act
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    out = []
    for f in range(f0, f1 + 1):
        bpy.context.scene.frame_set(f)
        d = {}
        for pb in arm.pose.bones:
            q = pb.matrix.to_quaternion()
            d[pb.name] = (pb.parent.matrix.to_quaternion().inverted() @ q) if pb.parent else q
        out.append(d)
    return out


def fbx_frames(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path, anim_offset=0.0)
    arm = next((o for o in bpy.data.objects if o.type == "ARMATURE"), None)
    if arm is not None and arm.animation_data and arm.animation_data.action:
        return armature_frames(arm, arm.animation_data.action)
    obs = [o for o in bpy.data.objects if o.type == "EMPTY" and o.animation_data and o.animation_data.action]
    f0 = min(int(o.animation_data.action.frame_range[0]) for o in obs)
    f1 = max(int(o.animation_data.action.frame_range[1]) for o in obs)
    out = []
    for f in range(f0, f1 + 1):
        bpy.context.scene.frame_set(f)
        out.append({o.name: o.matrix_local.to_quaternion() for o in obs})
    return out


def keep(n):
    s = n.lower().split(":")[-1]
    return not any(w in s for w in SKIP)


def summary(label, frames, hub):
    bones = [b for b in frames[0] if keep(b)]
    upper = [b for b in bones if not any(w in b.lower().split(":")[-1] for w in LEGS)]
    rng = {}
    for b in upper:
        qs = [fr[b] for fr in frames[::2]]
        rng[b] = max(ang(x, y) for x in qs for y in qs)
    mx = max(rng.values()) if rng else 0.0
    mean = sum(rng.values()) / max(len(rng), 1)
    line = "%-40s range mean %5.1f max %5.1f" % (label[:40], mean, mx)
    if hub is not None:
        common = [b for b in bones if b in hub]
        for tag, pose in (("start", frames[0]), ("end", frames[-1])):
            ds = sorted(((ang(pose[b], hub[b]), b) for b in common), reverse=True)
            line += " | %s mean %4.1f max %5.1f (%s)" % (tag, sum(d for d, _ in ds) / len(ds), ds[0][0], ds[0][1])
    print("MEASURE", line)


def main():
    args = sys.argv[sys.argv.index("--") + 1:]
    mode, rest = args[0], args[1:]
    hub = None
    if mode == "fbx":
        folder, names = rest[0], rest[1:]
        items = [(n, lambda n=n: fbx_frames(os.path.join(folder, n))) for n in names]
    else:
        arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
        items = [(n, lambda n=n: armature_frames(arm, bpy.data.actions[n])) for n in rest]
    for i, (name, get) in enumerate(items):
        frames = get()
        if i == 0:
            hub = frames[0]
            summary(name + " [hub]", frames, None)
        else:
            summary(name, frames, hub)


main()
