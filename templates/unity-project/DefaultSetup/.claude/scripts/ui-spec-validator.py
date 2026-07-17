#!/usr/bin/env python3
"""Validate a UI spec sidecar or a legacy HTML-embedded spec."""

import argparse
import json
import sys
from pathlib import Path

from ui_spec_common import UISpecError, load_spec, validate_spec


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("path", type=Path, help=".ui-spec.json or .html")
    parser.add_argument("--mode", choices=("draft", "approve", "build"), default="draft")
    parser.add_argument("--require-lane", action="store_true", help="require mockupLane on newly drafted v1 specs")
    args = parser.parse_args()
    try:
        spec, source, embedded = load_spec(args.path)
        result = validate_spec(spec, mode=args.mode, embedded=embedded, require_lane=args.require_lane)
        result["source"] = str(source)
    except (OSError, UISpecError) as exc:
        result = {
            "ok": False,
            "errors": [{"code": "load", "path": str(args.path), "message": str(exc)}],
            "warnings": [],
        }
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if result["ok"] else 1


if __name__ == "__main__":
    sys.exit(main())
