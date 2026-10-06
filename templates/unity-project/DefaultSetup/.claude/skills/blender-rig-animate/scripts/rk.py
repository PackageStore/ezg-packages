"""rigkit runner. Every step is one Blender call:

  blender -b <file.blend> --factory-startup --python-exit-code 1 --python rk.py -- <step> [options]

Steps: inspect, prepare, measure, rig, skin, check_weights, rom, anim, check_anim, review, export, verify_fbx.
Run `... -- <step> -h` for the options of a step. Exit code 1 when the step reports errors, so a failing
gate stops a shell pipeline.
"""
import importlib
import os
import sys
import traceback

sys.dont_write_bytecode = True  # no __pycache__ in the skill folder (it lives in a repo and in ~/.claude/skills)
HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

STEPS = {
    "inspect": "rigkit.inspect_model",
    "prepare": "rigkit.prepare",
    "measure": "rigkit.measure",
    "rig": "rigkit.rig",
    "skin": "rigkit.skin",
    "check_weights": "rigkit.check_weights",
    "rom": "rigkit.rom",
    "anim": "rigkit.anim",
    "check_anim": "rigkit.check_anim",
    "review": "rigkit.review",
    "export": "rigkit.export",
    "verify_fbx": "rigkit.export",
}


def run():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if not args or args[0] not in STEPS:
        print(__doc__)
        sys.exit(2)
    step, rest = args[0], args[1:]
    mod = importlib.import_module(STEPS[step])
    fn = getattr(mod, "main_" + step, None) or getattr(mod, "main")
    try:
        ok = fn(rest)
    except SystemExit:
        raise
    except Exception:
        traceback.print_exc()
        sys.exit(1)
    if ok is False:
        sys.exit(1)


run()
