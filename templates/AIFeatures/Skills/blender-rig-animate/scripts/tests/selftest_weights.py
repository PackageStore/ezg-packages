"""Self-test for the weight checks: a heat-weighted cylinder must pass, corrupted weights must fail.

  blender -b --factory-startup --python-exit-code 1 --python tests/selftest_weights.py -- [--verbose]

Also used to calibrate the thresholds in rigkit/check_weights.py (printed with --verbose).
"""
import math
import os
import sys

import bpy
import numpy as np

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(HERE))

from rigkit import common as C  # noqa: E402
from rigkit import rig as RIG  # noqa: E402
from rigkit import skin as SKIN  # noqa: E402
from rigkit import check_weights as CW  # noqa: E402

VERBOSE = "--verbose" in sys.argv


def clear():
    for ob in list(bpy.data.objects):
        C.remove_object(ob)
    bpy.context.view_layer.update()


def cylinder(name="limb", r=0.12, h=1.0, around=32, rings=50):
    verts, faces = [], []
    for i in range(rings + 1):
        z = h * i / rings
        for k in range(around):
            a = 2 * math.pi * k / around
            verts.append((r * math.cos(a), r * math.sin(a), z))
    for i in range(rings):
        for k in range(around):
            a0 = i * around + k
            a1 = i * around + (k + 1) % around
            faces.append((a0, a1, a1 + around, a0 + around))
    bottom = len(verts)
    verts.append((0, 0, 0))
    top = len(verts)
    verts.append((0, 0, h))
    for k in range(around):
        faces.append((bottom, (k + 1) % around, k))
        faces.append((top, rings * around + k, rings * around + (k + 1) % around))
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.update()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def build():
    clear()
    ob = cylinder()
    spec = {"armature": "TestRig", "mirror_x": False, "skin_meshes": [ob.name], "bones": [
        {"name": "Base", "head": [0, 0, 0.02], "tail": [0, 0, 0.5], "bend": [0, -1, 0]},
        {"name": "Tip", "head": [0, 0, 0.5], "tail": [0, 0, 0.98], "parent": "Base", "bend": [0, -1, 0], "connect": True,
         "limits": {"flex": [-60, 90], "side": [-45, 45], "twist": [-45, 45]}}]}
    rep = C.Report("selftest")
    arm = RIG.build(spec, rep)
    sp = {"object": ob.name, "method": "heat", "smooth": 2}
    SKIN.skin_soft(ob, arm, sp, {"symmetric": False, "max_influences": 4, "min_weight": 0.01}, rep)
    assert rep.ok, rep.errors
    return ob, arm


def run_checks(ob, arm, label):
    rep = C.Report(label)
    CW.check_mesh(ob, arm, rep, 4, 0.02)
    CW.stress_test(arm, [ob], rep)
    if VERBOSE:
        for row in rep.info["stress"]["Tip"]:
            print("   %-12s %-10s spike_max=%5.2f spike_frac=%.4f collapse=%.4f flip=%.4f" % (
                label, row["pose"], row["spike_max"], row["spike_frac"], row["collapse_frac"], row["flip_frac"]))
    return rep


def set_weights(ob, W):
    C.write_weights(ob, W, ["Base", "Tip"])


def main():
    ob, arm = build()
    W0, _ = C.read_weights(ob, ["Base", "Tip"])
    co = C.world_coords(ob)
    rng = np.random.default_rng(7)
    results = {}

    results["heat"] = run_checks(ob, arm, "heat")

    # A: a handful of vertices of the upper half follow the wrong bone
    W = W0.copy()
    ids = rng.choice(np.nonzero(co[:, 2] > 0.65)[0], 12, replace=False)
    W[ids] = [1.0, 0.0]
    set_weights(ob, W)
    results["wrong_bone"] = run_checks(ob, arm, "wrong_bone")

    # B: hard split, no blend at the joint (looks like a broken pipe when bent)
    W = np.zeros_like(W0)
    W[co[:, 2] <= 0.5, 0] = 1
    W[co[:, 2] > 0.5, 1] = 1
    set_weights(ob, W)
    results["hard_split"] = run_checks(ob, arm, "hard_split")

    # C: noisy weights around the joint
    W = W0.copy()
    near = np.nonzero(np.abs(co[:, 2] - 0.5) < 0.2)[0]
    noise = rng.uniform(-0.35, 0.35, len(near))
    W[near, 1] = np.clip(W[near, 1] + noise, 0, 1)
    W[near, 0] = 1 - W[near, 1]
    set_weights(ob, W)
    results["noisy"] = run_checks(ob, arm, "noisy")

    set_weights(ob, W0)
    ok = True
    expect = {"heat": True, "wrong_bone": False, "hard_split": False, "noisy": False}
    for k, want in expect.items():
        got = results[k].ok
        codes = sorted({e["code"] for e in results[k].errors})
        print("SELFTEST %-11s expected %-4s got %-4s %s" % (k, "pass" if want else "fail", "pass" if got else "fail", codes))
        ok &= (got == want)
    print("SELFTEST", "OK" if ok else "FAILED")
    if not ok:
        sys.exit(1)


main()
