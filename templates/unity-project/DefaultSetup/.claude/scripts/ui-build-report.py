#!/usr/bin/env python3
"""Create or validate the final evidence report for a /new-ui build."""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import struct
import sys
from pathlib import Path

from ui_spec_common import ROOT, UISpecError, canonical_json, kit_hash, load_spec, spec_hash, validate_spec


def png_size(path: Path) -> list[int]:
    data = path.read_bytes()[:24]
    if len(data) < 24 or data[:8] != b"\x89PNG\r\n\x1a\n" or data[12:16] != b"IHDR":
        raise UISpecError(f"{path}: not a valid PNG header")
    return list(struct.unpack(">II", data[16:24]))


def file_hash(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def validate_report(report: dict, report_path: Path | None = None) -> dict:
    errors = []
    base = report_path.parent if report_path else Path.cwd()

    def resolved(value):
        path = Path(value)
        if path.is_absolute():
            return path
        repo_path = (ROOT / path).resolve()
        return repo_path if repo_path.exists() else (base / path).resolve()

    for key in ("spec", "prefab", "unityScreenshot", "visualDiff"):
        if not isinstance(report.get(key), str) or not report[key]:
            errors.append(f"missing {key}")
        elif not resolved(report[key]).exists():
            errors.append(f"{key} does not exist: {report[key]}")
    if errors:
        return {"ok": False, "errors": errors}

    try:
        spec, _, _ = load_spec(resolved(report["spec"]), prefer_sidecar=False)
        validation = validate_spec(spec, mode="build")
        if not validation["ok"]:
            errors.append("spec does not pass build validation")
        if report.get("specHash") != spec_hash(spec):
            errors.append("specHash does not match current spec")
        if report.get("kitHash") != kit_hash():
            errors.append("kitHash does not match current UI kit")
        if png_size(resolved(report["unityScreenshot"])) != [1080, 2400]:
            errors.append("unityScreenshot must be 1080x2400")
        diff = json.loads(resolved(report["visualDiff"]).read_text(encoding="utf-8"))
        if not diff.get("ok"):
            errors.append("visualDiff must be ok")
        if diff.get("actualSha256") != file_hash(resolved(report["unityScreenshot"])):
            errors.append("visualDiff was not computed from the current Unity screenshot")
        spec_path = resolved(report["spec"])
        suffix = ".ui-spec.json"
        if not spec_path.name.endswith(suffix):
            errors.append("v1 report spec must use the .ui-spec.json suffix")
        else:
            approved_png = spec_path.with_name(spec_path.name[: -len(suffix)] + ".png")
            if not approved_png.exists():
                errors.append(f"approved mockup does not exist: {approved_png}")
            elif diff.get("referenceSha256") != file_hash(approved_png):
                errors.append("visualDiff was not computed from the approved sibling PNG")
    except (OSError, UISpecError, json.JSONDecodeError) as exc:
        errors.append(str(exc))

    for key in ("structuralValidation", "visualReview", "localizationValidation"):
        if report.get(key) != "pass":
            errors.append(f"{key} must be pass")
    if report.get("missingReferences") != 0:
        errors.append("missingReferences must be 0")
    return {"ok": not errors, "errors": errors}


def create(args) -> int:
    try:
        spec, source, _ = load_spec(args.spec, prefer_sidecar=False)
        validation = validate_spec(spec, mode="build")
        if not validation["ok"]:
            raise UISpecError(json.dumps(validation, ensure_ascii=False))
        if png_size(args.screenshot) != [1080, 2400]:
            raise UISpecError("Unity screenshot must be 1080x2400")
        if not args.prefab.exists():
            raise UISpecError(f"prefab does not exist: {args.prefab}")
        def portable(path: Path) -> str:
            resolved = path.resolve()
            try:
                return str(resolved.relative_to(ROOT))
            except ValueError:
                return str(resolved)

        report = {
            "reportVersion": 1,
            "generatedAt": dt.datetime.now(dt.timezone.utc).isoformat(),
            "spec": portable(source),
            "specHash": spec_hash(spec),
            "kitHash": kit_hash(),
            "prefab": portable(args.prefab),
            "unityScreenshot": portable(args.screenshot),
            "visualDiff": portable(args.visual_diff),
            "structuralValidation": args.structural,
            "visualReview": args.visual,
            "localizationValidation": args.localization,
            "missingReferences": args.missing_references,
        }
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(canonical_json(report), encoding="utf-8")
        checked = validate_report(report, args.output)
        if not checked["ok"]:
            raise UISpecError("; ".join(checked["errors"]))
        print(json.dumps({"ok": True, "output": str(args.output)}, indent=2))
        return 0
    except (OSError, UISpecError) as exc:
        print(json.dumps({"ok": False, "error": str(exc)}, ensure_ascii=False, indent=2))
        return 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    make = sub.add_parser("create")
    make.add_argument("--spec", type=Path, required=True)
    make.add_argument("--prefab", type=Path, required=True)
    make.add_argument("--screenshot", type=Path, required=True)
    make.add_argument("--visual-diff", type=Path, required=True)
    make.add_argument("--output", type=Path, required=True)
    make.add_argument("--structural", choices=("pass", "block"), required=True)
    make.add_argument("--visual", choices=("pass", "block"), required=True)
    make.add_argument("--localization", choices=("pass", "block"), required=True)
    make.add_argument("--missing-references", type=int, required=True)
    check = sub.add_parser("validate")
    check.add_argument("report", type=Path)
    args = parser.parse_args()
    if args.command == "create":
        return create(args)
    try:
        report = json.loads(args.report.read_text(encoding="utf-8"))
        result = validate_report(report, args.report.resolve())
    except (OSError, json.JSONDecodeError) as exc:
        result = {"ok": False, "errors": [str(exc)]}
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if result["ok"] else 1


if __name__ == "__main__":
    sys.exit(main())
