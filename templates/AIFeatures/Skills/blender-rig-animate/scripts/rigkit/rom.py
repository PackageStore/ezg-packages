"""rom: range-of-motion clip (GameAnimation_QuyChuan 5.3.5). Every joint group bends to 90% of its limits and
back, left and right together, then a section with the arms down (the T-pose bind makes shoulders weak there).

  blender -b skinned.blend --python rk.py -- rom --subject Hero --out rom.blend [--fraction 0.9] [--arms-down -35]

Run check_anim on the result: the ROM must pass TEAR / FLIP / PROP / LIMB on every frame. Deliver it with the rig.
"""
import argparse
import json
import os

import bpy

from . import anim as A
from . import common as C

GROUPS = [("Spine", "Chest"), ("Neck", "Head"), ("UpperArm_L", "UpperArm_R"), ("LowerArm_L", "LowerArm_R"),
          ("Hand_L", "Hand_R"), ("UpperLeg_L", "UpperLeg_R"), ("LowerLeg_L", "LowerLeg_R"), ("Foot_L", "Foot_R")]


def build_clip(arm, subject, fraction, arms_down, seg=8):
    meta = json.loads(arm.get("rk_meta", "{}"))
    clip = A.Clip(subject + "_ROM", frames=1, loop=False, base={})
    clip.kind = "rom"
    f = 0
    clip.key(f, {}, ease="stop")
    for group in GROUPS:
        bones = [b for b in group if b in meta and meta[b].get("limits")]
        if not bones:
            continue
        lim = meta[bones[0]]["limits"]
        for axis in ("flex", "side", "twist"):
            lo, hi = lim.get(axis, (0, 0))
            for end in (1, 0):
                if abs((hi, lo)[1 - end]) < 8:
                    continue
                # each bone of the group goes to ITS OWN limit (Neck and Head differ)
                pose = {}
                for b in bones:
                    blo, bhi = meta[b]["limits"].get(axis, (0, 0))
                    pose[b] = {axis: fraction * (bhi if end else blo)}
                # legs are FK here: the ROM tests the skin, not the IK
                if any(b.startswith(("UpperLeg", "LowerLeg", "Foot")) for b in bones):
                    pose.update({"leg_L": {"fk": 1.0}, "leg_R": {"fk": 1.0}})
                f += seg // 2
                clip.key(f, pose, ease="stop")
                f += seg // 2
                clip.key(f, {"leg_L": {"fk": pose.get("leg_L", {}).get("fk", 0.0)},
                             "leg_R": {"fk": pose.get("leg_R", {}).get("fk", 0.0)}}, ease="stop")
    # arms down: the pose a T-pose bind deforms worst
    f += seg
    clip.key(f, {"UpperArm_L": {"side": arms_down}, "UpperArm_R": {"side": arms_down},
                 "LowerArm_L": {"flex": 30}, "LowerArm_R": {"flex": 30}}, ease="stop")
    f += seg
    clip.key(f, {}, ease="stop")
    clip.frames = f
    return clip


def main(argv):
    ap = argparse.ArgumentParser(prog="rom")
    ap.add_argument("--subject", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--armature", default=None)
    ap.add_argument("--fraction", type=float, default=0.9)
    ap.add_argument("--arms-down", type=float, default=None, help="UpperArm side for the arms-down section "
                    "(default: fraction x the lower side limit)")
    a = ap.parse_args(argv)
    C.force_object_mode()
    arm = bpy.data.objects.get(a.armature) if a.armature else next(o for o in bpy.data.objects if o.type == "ARMATURE")
    rep = C.Report("rom")
    arms_down = a.arms_down
    if arms_down is None:
        lim = json.loads(arm.get("rk_meta", "{}")).get("UpperArm_L", {}).get("limits", {}).get("side", (-60, 0))
        arms_down = a.fraction * lim[0]
    clip = build_clip(arm, a.subject, a.fraction, arms_down)
    A.bake(arm, clip, rep)
    bpy.context.scene.frame_start, bpy.context.scene.frame_end = 0, clip.frames
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(a.out), copy=True)
    rep.info.update({"clip": clip.name, "frames": clip.frames, "saved": os.path.abspath(a.out)})
    return rep.dump(os.path.splitext(a.out)[0] + "_rom.json")
