"""Self-test for the follow-through springs (rigkit/follow.py; Clip.spring_channel, Clip.spring).

  blender -b --factory-startup --python-exit-code 1 --python tests/selftest_follow.py -- [--verbose]

Math: a base at constant speed leaves no offset; a dead stop overshoots in the direction of motion and successive peaks
shrink by exp(-pi zeta / sqrt(1 - zeta^2)); gain 0 gives nothing; a window starts from rest (a stop on its first frame
still kicks) and fades to 0; a loop is periodic; soft limits never leave the range.
Bake, on a 4-bone test rig built here (no model needed): gain 0 bakes the clip unchanged; a channel spring carries the
chest past its dead stop inside its joint limit; a turn spring carries the head on in world space with the first and
last frames on the keys; a tiny spring on a bone keyed with a non-canonical angle triple changes no anatomical angle by
more than 0.5 deg (the change is read before and after the turn, not against the keyed channel); a looping spring has no
seam.
"""
import math
import os
import sys

import bpy
from mathutils import Quaternion, Vector

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

from rigkit import anim as A  # noqa: E402
from rigkit import common as C  # noqa: E402
from rigkit import follow as FL  # noqa: E402
from rigkit import rig as RIG  # noqa: E402
from rigkit import skeleton as SK  # noqa: E402

VERBOSE = "--verbose" in sys.argv
FPS = 30
RESULTS = []


def check(name, ok, detail=""):
    RESULTS.append(bool(ok))
    show = detail and (VERBOSE or not ok)
    print("SELFTEST %-52s %s%s" % (name, "ok" if ok else "FAIL", ("   " + detail) if show else ""))


def extremes(xs, after):
    """(frame, value) of the local extremes of xs after frame `after`."""
    return [(f, xs[f]) for f in range(max(after, 1), len(xs) - 1)
            if (xs[f] - xs[f - 1]) * (xs[f + 1] - xs[f]) < 0 and abs(xs[f]) > 1e-6]


def wrap(d):
    return (d + 180.0) % 360.0 - 180.0


# --------------------------------------------------------------------------- math
def math_tests():
    ys = [0.5 * f for f in range(31)]
    off = FL.spring(ys, FPS)
    check("constant speed: no offset", max(abs(o) for o in off) < 1e-9, "max %.2e" % max(abs(o) for o in off))

    stop = [3.0 * min(f, 10) for f in range(61)]   # moves 3 per frame, stops dead on f10
    off = FL.spring(stop, FPS)
    pk = extremes(off, 10)
    want = math.exp(-math.pi * FL.ZETA / math.sqrt(1.0 - FL.ZETA ** 2))
    ratio = abs(pk[1][1] / pk[0][1]) if len(pk) > 1 else 0.0
    check("dead stop: carries on forward, peak 1-4 frames later", bool(pk) and pk[0][1] > 0 and 1 <= pk[0][0] - 10 <= 4,
          "first extreme %s" % (pk[:1],))
    check("dead stop: peaks shrink by exp(-pi z / sqrt(1 - z^2))", abs(ratio - want) < 0.2 * want,
          "ratio %.3f, want %.3f" % (ratio, want))
    check("dead stop: settles", bool(pk) and abs(off[-1]) < 0.02 * abs(pk[0][1]), "last %.4f" % off[-1])

    def run(s, st, lp):
        return FL.spring(s, FPS, FL.HZ, FL.ZETA, st, lp)

    whole = FL.norm_windows(None, 60, 4)
    z = FL.windowed(stop, run, float, 60, False, True, whole, 0.0)
    check("gain 0: nothing", max(abs(o) for o in z) == 0.0)
    w = FL.windowed(stop, run, float, 60, False, True, whole, 1.0)
    check("default window: first and last frame on the keys", w[0] == 0.0 and abs(w[60]) < 1e-12,
          "f0 %g, f60 %g" % (w[0], w[60]))
    w = FL.windowed(stop, run, float, 60, False, True, FL.norm_windows([(10, 40)], 60, 4), 1.0)
    check("window from the stop frame: kicks, 0 outside, faded", all(x == 0.0 for x in w[:10] + w[41:]) and
          max(abs(x) for x in w[11:15]) > 0.1 and abs(w[40]) < 1e-12, "f11-14 %s" % [round(x, 3) for x in w[11:15]])
    w = FL.windowed(stop, run, float, 60, False, True, FL.norm_windows([(0, 10)], 60, 4), 1.0)
    check("window ending on the stop: the keys play untouched", max(abs(x) for x in w) < 1e-9)
    w = FL.windowed(stop, run, float, 60, False, False, whole, 1.0)
    check("hub_end=False: not faded at the end", abs(w[-1] - off[-1]) < 1e-12)

    cyc = [2.0 * f if f < 10 else (20.0 if f < 15 else max(0.0, 20.0 - 2.0 * (f - 15))) for f in range(30)]
    a = FL.spring(cyc, FPS, loop=True)
    old = FL.LOOP_CYCLES
    FL.LOOP_CYCLES = 6
    b = FL.spring(cyc, FPS, loop=True)
    FL.LOOP_CYCLES = old
    amp = max(abs(x) for x in b)
    check("loop: periodic (3 cycles = 6 cycles)", amp > 0.1 and max(abs(x - y) for x, y in zip(a, b)) < 0.01 * amp,
          "amp %.3f" % amp)

    lim = (-20.0, 30.0)
    inside = all(-20.0 - 1e-9 <= 10.0 + FL.soft_limit(0.5 * i, 10.0, lim) <= 30.0 + 1e-9 for i in range(-400, 401))
    slope = FL.soft_limit(1e-4, 10.0, lim) / 1e-4
    check("soft limit: stays inside, slope 1 at 0", inside and abs(slope - 1.0) < 1e-3, "slope %.5f" % slope)
    check("soft limit: no room, no offset", FL.soft_limit(5.0, 30.0, lim) == 0.0 and FL.soft_limit(-5.0, -20.0, lim) == 0.0)
    big = math.degrees(FL.soft_cap(Vector((0.0, 0.0, math.radians(100.0))), 40.0).length)
    small = math.degrees(FL.soft_cap(Vector((0.0, 0.0, math.radians(1.0))), 40.0).length)
    check("soft cap: under max_deg, ~unchanged near 0", big < 40.0 and abs(small - 1.0) < 0.02,
          "100 -> %.1f, 1 -> %.3f" % (big, small))

    axis = Vector((0.0, 0.0, 1.0))
    qs = [Quaternion(axis, math.radians(4.0 * f)) for f in range(31)]
    off = FL.spring_rot(qs, FL.turn_spin(FPS), FPS)
    noise = math.degrees(max(o.length for o in off))   # mathutils works in 32-bit floats: noise ~1e-5 deg
    check("turn: constant spin, no offset", noise < 1e-3, "max %.2e deg" % noise)
    qs = [Quaternion(axis, math.radians(6.0 * min(f, 10))) for f in range(41)]
    off = FL.spring_rot(qs, FL.turn_spin(FPS), FPS)
    first = max(off[11:16], key=lambda o: o.length)
    check("turn: a dead stop carries on about the same axis", first.length > 1e-3 and first.normalized().dot(axis) > 0.99,
          "%.2f deg" % math.degrees(first.length))
    sg = [(Vector((0.0, 0.0, 0.0)), Vector((0.02 * min(f, 10), 0.0, -0.5))) for f in range(41)]
    off = FL.spring_rot(sg, FL.grip_spin(FPS), FPS)
    swing = Vector((0.2, 0.0, -0.5)).cross(Vector((0.6, 0.0, 0.0))).normalized()
    first = max(off[11:16], key=lambda o: o.length)
    check("grip: the pendulum swings on after the grip stops", first.length > 1e-3 and first.normalized().dot(swing) > 0.95,
          "%.2f deg" % math.degrees(first.length))


# --------------------------------------------------------------------------- bake on a test rig
def build_rig():
    for ob in list(bpy.data.objects):
        C.remove_object(ob)
    spec = {"armature": "FollowRig", "mirror_x": False, "bones": [
        {"name": "Root", "head": [0, 0, 0], "tail": [0, 0, 0.2], "deform": False, "roll_z": [0, -1, 0]},
        {"name": "Hips", "head": [0, 0, 0.3], "tail": [0, 0, 0.5], "parent": "Root", "bend": [0, -1, 0]},
        {"name": "Chest", "head": [0, 0, 0.5], "tail": [0, 0, 0.8], "parent": "Hips", "bend": [0, -1, 0], "connect": True,
         "limits": {"flex": [-15, 35], "side": [-15, 15], "twist": [-25, 25]}},
        {"name": "Head", "head": [0, 0, 0.8], "tail": [0, 0, 1.0], "parent": "Chest", "bend": [0, -1, 0], "connect": True,
         "limits": {"flex": [-30, 30], "side": [-20, 20], "twist": [-45, 45]}},
        {"name": "Prop_Stick", "head": [0, 0, 1.0], "tail": [0, -0.3, 1.0], "parent": "Head", "roll_z": [0, 0, 1]}]}
    rep = C.Report("selftest_follow")
    arm = RIG.build(spec, rep)
    assert rep.ok, rep.errors
    return arm


def stop_clip(name, frames=40, loop=False, base=None):
    """The chest bends forward 30 deg in 8 frames and stops dead (impact), then holds."""
    c = A.Clip(name, frames=frames, loop=loop, base=base or {})
    c.key(0, {"Chest": {"flex": 0.0}})
    c.key(8, {"Chest": {"flex": 30.0}}, ease="impact")
    if loop:
        c.key(20, {"Chest": {"flex": 30.0}})
    else:
        c.key(frames, {"Chest": {"flex": 30.0}})
    return c


def bake(arm, clip):
    rep = C.Report("bake")
    A.bake(arm, clip, rep)
    return rep.info["clips"][clip.name]


def sample(arm, clip):
    """Per frame: {bone: (armature-space matrix, anatomical angles)}."""
    C.assign_action(arm, bpy.data.actions[clip.name])
    sk = SK.Skeleton(arm)
    out = []
    for f in range(clip.frames + 1):
        bpy.context.scene.frame_set(f)
        sk.read(arm)
        out.append({pb.name: (pb.matrix.copy(), sk.get_anat(pb.name)) for pb in arm.pose.bones})
    return out


def rot_diff(a, b):
    return math.degrees(a.to_quaternion().rotation_difference(b.to_quaternion()).angle)


def bake_tests():
    arm = build_rig()
    plain = stop_clip("T_Plain")
    bake(arm, plain)
    P = sample(arm, plain)

    zero = stop_clip("T_Zero")
    zero.spring_channel("Chest", gain=0.0)
    zero.spring("Head", gain=0.0)
    bake(arm, zero)
    Z = sample(arm, zero)
    d = max(max(abs(x) for row in (P[f][n][0] - Z[f][n][0]) for x in row) for f in range(41) for n in P[f])
    check("bake: gain 0 leaves the clip unchanged", d < 1e-6, "max matrix diff %.2e" % d)

    chan = stop_clip("T_Chan")
    chan.spring_channel(("Chest", "flex"), gain=1.0)
    info = bake(arm, chan)
    S = sample(arm, chan)
    flex = [S[f]["Chest"][1][0] for f in range(41)]
    check("bake: channel spring carries the chest past its stop", max(flex[9:20]) > 31.0 and max(flex) <= 35.0 + 1e-3,
          "max %.2f (keyed 30, limit 35), info %s" % (max(flex), info.get("springs")))
    check("bake: channel spring, first and last frame on the keys", abs(flex[0]) < 1e-3 and abs(flex[40] - 30.0) < 1e-3,
          "f0 %.4f, f40 %.4f" % (flex[0], flex[40]))
    strong = stop_clip("T_Strong")
    strong.spring_channel(("Chest", "flex"), gain=4.0)
    bake(arm, strong)
    top = max(s["Chest"][1][0] for s in sample(arm, strong))
    check("bake: gain 4 stays inside the joint limit (soft)", top <= 35.0 + 1e-3, "max %.3f" % top)

    turn = stop_clip("T_Turn")
    turn.spring("Head", kind="turn", gain=1.0, max_deg=40)
    info = bake(arm, turn)
    T = sample(arm, turn)
    diff = [rot_diff(P[f]["Head"][0], T[f]["Head"][0]) for f in range(41)]
    head = [T[f]["Head"][1][0] for f in range(41)]
    check("bake: turn spring carries the head on after the stop", max(diff[9:17]) > 1.0 and max(head[9:17]) > 0.5,
          "max %.2f deg, head flex %.2f, info %s" % (max(diff[9:17]), max(head[9:17]), info.get("springs")))
    check("bake: turn spring, first and last frame on the keys", diff[0] < 1e-3 and diff[40] < 1e-3,
          "f0 %.4f, f40 %.4f" % (diff[0], diff[40]))
    check("bake: turn spring inside the head limits", all(-30.0 - 1e-3 <= x <= 30.0 + 1e-3 for x in head))

    base = {"Prop_Stick": {"twist": 200.0}}   # another triple for the orientation of twist -160
    kp = stop_clip("T_TinyPlain", base=base)
    bake(arm, kp)
    KP = sample(arm, kp)
    tiny = stop_clip("T_Tiny", base=base)
    tiny.spring("Prop_Stick", kind="turn", gain=0.02)
    info = bake(arm, tiny)
    TI = sample(arm, tiny)
    worst = max(abs(wrap(a - b)) for f in range(41) for a, b in zip(TI[f]["Prop_Stick"][1], KP[f]["Prop_Stick"][1]))
    spring_deg = (info.get("springs") or {}).get("Prop_Stick", 0.0)
    check("bake: tiny spring on a non-canonical triple, angles move < 0.5", 0.0 < spring_deg < 0.5 and worst < 0.5,
          "spring %.3f deg, worst angle change %.3f deg" % (spring_deg, worst))

    lp = stop_clip("T_LoopPlain", frames=30, loop=True)
    bake(arm, lp)
    LP = sample(arm, lp)
    loop = stop_clip("T_Loop", frames=30, loop=True)
    loop.spring("Head", kind="turn", gain=1.0)
    bake(arm, loop)
    L = sample(arm, loop)
    seam = rot_diff(L[0]["Head"][0], L[30]["Head"][0])
    moved = max(rot_diff(LP[f]["Head"][0], L[f]["Head"][0]) for f in range(31))
    check("bake: looping spring, no seam", seam < 1e-3 and moved > 0.5, "seam %.5f deg, spring up to %.2f deg" % (seam, moved))


def main():
    math_tests()
    bake_tests()
    ok = all(RESULTS)
    print("SELFTEST", "OK" if ok else "FAILED", "(%d checks)" % len(RESULTS))
    if not ok:
        sys.exit(1)


main()
