#!/usr/bin/env python3
"""Measure approved-mockup vs Unity screenshot drift and optionally write a diff image."""

import argparse
import hashlib
import json
import sys
from pathlib import Path

from PIL import Image, ImageChops, ImageStat


def file_hash(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def portable(path: Path) -> str:
    root = Path(__file__).resolve().parents[2]
    try:
        return str(path.resolve().relative_to(root))
    except ValueError:
        return str(path.resolve())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("reference", type=Path)
    parser.add_argument("actual", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--diff-image", type=Path)
    parser.add_argument("--max-mae", type=float, help="optional 0..1 gate; omit for report-only mode")
    parser.add_argument("--pixel-threshold", type=int, default=24)
    args = parser.parse_args()
    try:
        reference = Image.open(args.reference).convert("RGB")
        actual = Image.open(args.actual).convert("RGB")
        if reference.size != (1080, 2400) or actual.size != (1080, 2400):
            raise ValueError("both images must be 1080x2400")
        diff = ImageChops.difference(reference, actual)
        stat = ImageStat.Stat(diff)
        mae = sum(stat.mean) / (3 * 255)
        rmse = (sum(value * value for value in stat.rms) / 3) ** 0.5 / 255
        gray = diff.convert("L")
        histogram = gray.histogram()
        changed = sum(histogram[args.pixel_threshold + 1 :])
        changed_ratio = changed / (reference.width * reference.height)
        gate = "threshold" if args.max_mae is not None else "review-only"
        ok = args.max_mae is None or mae <= args.max_mae
        result = {
            "ok": ok,
            "gate": gate,
            "reference": portable(args.reference),
            "actual": portable(args.actual),
            "referenceSha256": file_hash(args.reference),
            "actualSha256": file_hash(args.actual),
            "resolution": [1080, 2400],
            "meanAbsoluteError": round(mae, 6),
            "rootMeanSquareError": round(rmse, 6),
            "changedPixelRatio": round(changed_ratio, 6),
            "pixelThreshold": args.pixel_threshold,
            "maxMae": args.max_mae,
            "note": "v0 wireframe uses review-only metrics; ui-visual-reviewer remains the visual gate",
        }
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        if args.diff_image:
            args.diff_image.parent.mkdir(parents=True, exist_ok=True)
            diff.save(args.diff_image)
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 0 if ok else 1
    except (OSError, ValueError) as exc:
        print(json.dumps({"ok": False, "error": str(exc)}, ensure_ascii=False, indent=2))
        return 1


if __name__ == "__main__":
    sys.exit(main())
