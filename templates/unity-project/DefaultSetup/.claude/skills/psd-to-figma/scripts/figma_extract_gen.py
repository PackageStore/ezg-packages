"""Print the figma_extract Plugin API script with CONFIG injected from the
project's extract config. Paste the result into the use_figma MCP tool.

Consumes --data-dir / --project-root via pipeline_config; the rest of argv is
--frames A,B (Figma frame names), --keys a,b (output keys), or neither (all),
plus --out <path> (default: stdout).
"""
import argparse
import json
import sys
from pathlib import Path

from pipeline_config import resolve

PLACEHOLDER = "/*__CONFIG__*/"


def load_extract_config(cfg):
    primary = cfg.load_optional("extract")
    if isinstance(primary, dict) and "pageName" in primary and "frames" in primary:
        return primary
    fallback = cfg.load_optional("figma_extract_config.json")
    if not (isinstance(fallback, dict) and "pageName" in fallback and "frames" in fallback):
        raise SystemExit(
            "figma_extract_gen: no extract config with pageName/frames "
            "(checked tables.extract and figma_extract_config.json)"
        )
    tbl = cfg.settings.get("tables", {}).get("extract", "extract_config.json")
    sys.stderr.write(
        f"figma_extract_gen: tables.extract ({tbl!r}) has no pageName/frames; "
        "using figma_extract_config.json\n"
    )
    return fallback


def select_frames(all_frames, frames_arg, keys_arg):
    if frames_arg and keys_arg:
        raise SystemExit("figma_extract_gen: pass --frames or --keys, not both")
    if not frames_arg and not keys_arg:
        return dict(all_frames)
    if frames_arg:
        want = [s for s in frames_arg.split(",") if s]
        unknown = [f for f in want if f not in all_frames]
        if unknown:
            raise SystemExit(
                "figma_extract_gen: frame(s) not in config: "
                + ", ".join(unknown)
                + " (have: " + ", ".join(all_frames) + ")"
            )
        want_set = set(want)
        return {fn: k for fn, k in all_frames.items() if fn in want_set}
    want = [s for s in keys_arg.split(",") if s]
    unknown = [k for k in want if k not in all_frames.values()]
    if unknown:
        raise SystemExit(
            "figma_extract_gen: key(s) not in config frames: "
            + ", ".join(unknown)
            + " (have: " + ", ".join(all_frames.values()) + ")"
        )
    want_set = set(want)
    return {fn: k for fn, k in all_frames.items() if k in want_set}


def main():
    cfg, argv = resolve()
    parser = argparse.ArgumentParser(prog="figma_extract_gen.py")
    parser.add_argument("--frames")
    parser.add_argument("--keys")
    parser.add_argument("--out")
    args = parser.parse_args(argv)

    ec = load_extract_config(cfg)
    config = dict(ec)
    config["frames"] = select_frames(ec["frames"], args.frames, args.keys)

    template = (Path(__file__).resolve().parent / "figma_extract.js").read_text(encoding="utf-8")
    if template.count(PLACEHOLDER) != 1:
        raise SystemExit(
            f"figma_extract_gen: expected exactly one {PLACEHOLDER} in figma_extract.js"
        )
    block = "const CONFIG = " + json.dumps(config, indent=2, ensure_ascii=False) + ";"
    script = template.replace(PLACEHOLDER, block, 1)

    if args.out:
        Path(args.out).write_text(script, encoding="utf-8")
    else:
        sys.stdout.write(script)


if __name__ == "__main__":
    main()
