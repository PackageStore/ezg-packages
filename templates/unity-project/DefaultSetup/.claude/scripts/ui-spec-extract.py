#!/usr/bin/env python3
"""Extract a legacy HTML embedded spec to a backward-compatible sidecar."""

import argparse
import json
import sys
from pathlib import Path

from ui_spec_common import UISpecError, canonical_json, embedded_spec, sidecar_for, spec_hash


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("html", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--force", action="store_true")
    args = parser.parse_args()
    output = args.output or sidecar_for(args.html)
    try:
        if output.exists() and not args.force:
            raise UISpecError(f"{output} already exists; pass --force to replace it")
        spec = embedded_spec(args.html)
        # Extraction is lossless. Missing specVersion remains legacy v0; upgrading
        # localization/questions requires human decisions and is never guessed here.
        spec.setdefault("specVersion", 0)
        output.write_text(canonical_json(spec), encoding="utf-8")
        print(json.dumps({
            "ok": True,
            "source": str(args.html),
            "output": str(output),
            "specVersion": spec["specVersion"],
            "specHash": spec_hash(spec),
        }, ensure_ascii=False, indent=2))
        return 0
    except (OSError, UISpecError) as exc:
        print(json.dumps({"ok": False, "error": str(exc)}, ensure_ascii=False, indent=2))
        return 1


if __name__ == "__main__":
    sys.exit(main())
