#!/usr/bin/env python3
"""Turn a build plan JSON into one use_figma payload.

Stdlib only; no project settings are read. The plan carries every value the
build needs (reference/build-plan.md). Flags:

  --plan <file>            build plan JSON
  --font Family/Style      default font for text ops (sets plan.font)
  --at X,Y                 frame position override
  --out <file>             write the payload here instead of stdout
  --selftest --page <name> emit a self-verifying payload (three containers,
                           deleted on success) on that page
  --helpers-selftest --page-id <id> --font Family/Style
                           emit figma_helpers.js + its self-test with PAGE_ID
                           and FONT filled in
"""
import argparse
import json
import sys
from pathlib import Path

PLACEHOLDER = "/*__PLAN__*/"
SCRIPTS = Path(__file__).resolve().parent


def parse_font(spec):
    if spec is None:
        return None
    if isinstance(spec, dict):
        return spec
    if "/" not in spec:
        raise SystemExit("figma_build_gen: --font wants Family/Style, got " + repr(spec))
    family, style = spec.split("/", 1)
    return {"family": family, "style": style}


def emit_payload(plan, selftest=False):
    helpers = (SCRIPTS / "figma_helpers.js").read_text(encoding="utf-8")
    template = (SCRIPTS / "figma_build.js").read_text(encoding="utf-8")
    if template.count(PLACEHOLDER) != 1:
        raise SystemExit(
            f"figma_build_gen: expected exactly one {PLACEHOLDER} in figma_build.js"
        )
    block = "const PLAN = " + json.dumps(plan, indent=2, ensure_ascii=False) + ";"
    body = template.replace(PLACEHOLDER, block, 1)

    if selftest:
        n = len(plan["ops"])
        return (
            helpers + "\n"
            "const __r = await (async function() {\n"
            + body + "\n"
            "})();\n"
            "if (!__r.frameId) throw new Error('selftest: no frameId');\n"
            f"if (Object.keys(__r.ops).length !== {n})\n"
            "  throw new Error('selftest: ops count ' + Object.keys(__r.ops).length);\n"
            "await deleteByIds([__r.frameId]);\n"
            "return { pass: true };\n"
        )
    return helpers + "\n" + body


def emit_helpers_selftest(page_id, font):
    helpers = (SCRIPTS / "figma_helpers.js").read_text(encoding="utf-8")
    test = (SCRIPTS / "figma_helpers_selftest.js").read_text(encoding="utf-8")
    for token in ("__PAGE_ID__", "__FONT_FAMILY__", "__FONT_STYLE__"):
        if test.count(token) != 1:
            raise SystemExit(f"figma_build_gen: expected exactly one {token} in figma_helpers_selftest.js")
    test = (test.replace("__PAGE_ID__", page_id)
                .replace("__FONT_FAMILY__", font["family"])
                .replace("__FONT_STYLE__", font["style"]))
    return helpers + "\n" + test


def selftest_plan(page_name):
    return {
        "key": "__selftest__",
        "frameName": "__Selftest-Build__",
        "frame": {"x": 0, "y": 0, "w": 200, "h": 200},
        "gridStyleId": None,
        "pageName": page_name,
        "replace": True,
        "ops": [
            {"op": "container", "id": "o1", "name": "Container-A", "parent": None,
             "x": 10, "y": 10, "w": 180, "h": 80},
            {"op": "container", "id": "o2", "name": "Container-B", "parent": "o1",
             "x": 5, "y": 5, "w": 80, "h": 40},
            {"op": "container", "id": "o3", "name": "Container-C", "parent": "o1",
             "x": 90, "y": 5, "w": 80, "h": 40},
        ],
        "warnings": [],
    }


def apply_overrides(plan, font=None, at=None):
    if at:
        parts = at.split(",")
        plan["frame"]["x"] = int(parts[0])
        plan["frame"]["y"] = int(parts[1])
    if font:
        plan["font"] = parse_font(font)
    return plan


def write_out(script, out, label):
    if out:
        Path(out).write_text(script, encoding="utf-8")
        sys.stderr.write(f"figma_build_gen: wrote {out} ({len(script)} chars, {label})\n")
    else:
        sys.stdout.write(script)
        sys.stderr.write(f"figma_build_gen: {label} payload {len(script)} chars\n")


def main(argv=None):
    parser = argparse.ArgumentParser(prog="figma_build_gen.py")
    parser.add_argument("--plan")
    parser.add_argument("--font")
    parser.add_argument("--at")
    parser.add_argument("--out")
    parser.add_argument("--selftest", action="store_true")
    parser.add_argument("--page")
    parser.add_argument("--helpers-selftest", action="store_true")
    parser.add_argument("--page-id")
    args = parser.parse_args(argv)

    if args.helpers_selftest:
        if not args.page_id or not args.font:
            raise SystemExit("figma_build_gen: --helpers-selftest needs --page-id and --font")
        return write_out(emit_helpers_selftest(args.page_id, parse_font(args.font)),
                         args.out, "helpers selftest")
    if args.selftest:
        if not args.page:
            raise SystemExit("figma_build_gen: --selftest needs --page <name>")
        return write_out(emit_payload(selftest_plan(args.page), selftest=True),
                         args.out, "build selftest")
    if not args.plan:
        raise SystemExit("figma_build_gen: --plan <file> is required")

    plan = json.loads(Path(args.plan).read_text(encoding="utf-8"))
    apply_overrides(plan, font=args.font, at=args.at)
    write_out(emit_payload(plan), args.out, plan.get("key", "plan"))


if __name__ == "__main__":
    main()
