"""Write the object use_figma returned into figma_extract_<key>.json files.

Consumes --data-dir / --project-root via pipeline_config; the rest of argv is
--in <file> (default: stdin), --allow-unknown, --dry-run. The input is the
mapping key -> extract that figma_extract.js returns, or that same object as a
JSON string (unwrapped once).
"""
import argparse
import difflib
import json
import sys

from pipeline_config import resolve


def read_input(path):
    if path and path != "-":
        text = open(path, "r", encoding="utf-8").read()
    else:
        text = sys.stdin.read()
    data = json.loads(text)
    if isinstance(data, str):
        data = json.loads(data)
    return data


def render(obj):
    return json.dumps(obj, indent=2) + "\n"


def main():
    cfg, argv = resolve()
    parser = argparse.ArgumentParser(prog="figma_extract_save.py")
    parser.add_argument("--in", dest="inp")
    parser.add_argument("--allow-unknown", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args(argv)

    data = read_input(args.inp)
    if not isinstance(data, dict):
        raise SystemExit("figma_extract_save: expected a JSON object mapping key -> extract")

    screens = cfg.load("screens")
    unknown = [k for k in data if k not in screens]
    if unknown and not args.allow_unknown:
        raise SystemExit(
            "figma_extract_save: key(s) not in screens.json: "
            + ", ".join(unknown)
            + " (pass --allow-unknown to write anyway)"
        )

    for key, obj in data.items():
        if not isinstance(obj, dict) or "frameW" not in obj or "frameH" not in obj:
            raise SystemExit(f"figma_extract_save: {key!r} is missing frameW/frameH")

    for key, obj in data.items():
        dest = cfg.path(f"figma_extract_{key}.json")
        new_text = render(obj)
        n = len(obj.get("nodes", []))
        if args.dry_run:
            old_text = dest.read_text(encoding="utf-8") if dest.is_file() else ""
            diff = difflib.unified_diff(
                old_text.splitlines(keepends=True),
                new_text.splitlines(keepends=True),
                fromfile=f"a/figma_extract_{key}.json",
                tofile=f"b/figma_extract_{key}.json",
            )
            sys.stdout.writelines(diff)
            sys.stdout.write(f"[dry-run] figma_extract_{key}.json ({n} nodes)\n")
        else:
            dest.write_text(new_text, encoding="utf-8")
            print(f"wrote figma_extract_{key}.json ({n} nodes)")


if __name__ == "__main__":
    main()
