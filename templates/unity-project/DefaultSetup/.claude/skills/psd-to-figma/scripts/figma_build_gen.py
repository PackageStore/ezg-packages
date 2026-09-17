#!/usr/bin/env python3
"""Generate the use_figma payload for one screen via the figma-build skill.

Resolves the plan path, font and page from the project data dir, then calls
figma-build/scripts/figma_build_gen.py. Flags after --data-dir / --project-root:
--key <screen_key>, --at X,Y, --out <path>, --selftest [--out].
"""
import argparse
import sys
from pathlib import Path

from pipeline_config import resolve

FIGMA_BUILD_SCRIPTS = Path(__file__).resolve().parents[2] / "figma-build" / "scripts"
if not (FIGMA_BUILD_SCRIPTS / "figma_build_gen.py").is_file():
    raise SystemExit(f"figma_build_gen: figma-build skill not found at {FIGMA_BUILD_SCRIPTS}")
sys.path.insert(0, str(FIGMA_BUILD_SCRIPTS))
import figma_build_gen as shared  # noqa: E402


def body_font(cfg):
    return cfg.settings.get("figma", {}).get("fonts", {}).get("body")


def selftest(cfg, rem):
    parser = argparse.ArgumentParser(prog="figma_build_gen.py --selftest")
    parser.add_argument("--selftest", action="store_true")
    parser.add_argument("--out")
    args = parser.parse_args(rem)
    pages = cfg.load_optional("page_ids.json", {})
    comp_page = next((n for n in pages if n.lower().startswith("component")), None)
    if not comp_page:
        raise SystemExit("figma_build_gen --selftest: no Components page in page_ids.json")
    script = shared.emit_payload(shared.selftest_plan(comp_page), selftest=True)
    shared.write_out(script, args.out, "build selftest")


def main():
    cfg, rem = resolve()
    if "--selftest" in rem:
        return selftest(cfg, rem)
    parser = argparse.ArgumentParser(prog="figma_build_gen.py")
    parser.add_argument("--key", required=True)
    parser.add_argument("--at")
    parser.add_argument("--out")
    args = parser.parse_args(rem)

    plan = cfg.load(f"build_plan_{args.key}.json")
    shared.apply_overrides(plan, font=body_font(cfg), at=args.at)
    shared.write_out(shared.emit_payload(plan), args.out, args.key)


if __name__ == "__main__":
    main()
