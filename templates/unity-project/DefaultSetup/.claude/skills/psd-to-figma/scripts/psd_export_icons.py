#!/usr/bin/env python3
"""Export icon PSDs to 128x128 RGBA PNGs and build icons_index.json."""

import json
import sys
from collections import Counter
from pathlib import Path

from PIL import Image
from psd_tools import PSDImage

from pipeline_config import resolve

def icon_settings(cfg) -> dict:
    icons = cfg.settings.get("icons")
    if not icons:
        raise SystemExit(
            "psd2figma.json has no \"icons\" section: add subdir, skip, "
            "variantOverrides and setPrefixes before running this stage")
    return icons


def derive_variant(stem: str, icons: dict) -> str:
    override = icons.get("variantOverrides", {}).get(stem)
    if override:
        return override
    for prefix, _ in icons["setPrefixes"]:
        if stem.startswith(prefix):
            raw = stem[len(prefix):]
            return raw.replace("_", " ").title()
    raise ValueError(f"Unknown prefix for {stem}")


def derive_set(stem: str, icons: dict) -> str:
    for prefix, set_name in icons["setPrefixes"]:
        if stem.startswith(prefix):
            return set_name
    raise ValueError(f"Unknown prefix for {stem}")


def export_icon(psd_path: Path, out_path: Path) -> Image.Image:
    psd = PSDImage.open(psd_path)
    img = psd.composite()
    if img.mode != "RGBA":
        img = img.convert("RGBA")
    assert img.size == (128, 128), f"{psd_path.name}: expected 128x128, got {img.size}"
    img.save(out_path, "PNG")
    return img


def main():
    cfg, _ = resolve()
    icons = icon_settings(cfg)
    icon_dir = cfg.psd_dir.joinpath(*icons["subdir"])
    out_dir = cfg.path("icons")
    index_path = cfg.path("icons_index.json")

    if not icon_dir.is_dir():
        raise SystemExit(f"icon source directory not found: {icon_dir}")

    skip = set(icons.get("skip", []))
    psd_files = sorted(
        p for p in icon_dir.rglob("*.psd")
        if p.stem not in skip
    )
    if not psd_files:
        raise SystemExit(
            f"no .psd files under {icon_dir}; refusing to overwrite "
            f"{index_path.name} with an empty index")

    out_dir.mkdir(parents=True, exist_ok=True)

    index = {}
    errors = []

    for psd_path in psd_files:
        stem = psd_path.stem
        out_path = out_dir / f"{stem}.png"
        try:
            img = export_icon(psd_path, out_path)
        except Exception as e:
            errors.append(f"{stem}: {e}")
            continue

        bbox = img.getbbox()
        if bbox is None:
            errors.append(f"{stem}: fully transparent (blank icon)")

        index[stem] = {
            "file": f"{stem}.png",
            "set": derive_set(stem, icons),
            "variant": derive_variant(stem, icons),
        }

    index_path.write_text(json.dumps(index, indent=2) + "\n")

    per_set = Counter(v["set"] for v in index.values())
    breakdown = ", ".join(f"{n} {s}" for s, n in sorted(per_set.items()))

    print(f"Exported {len(index)} icons ({breakdown})")
    print(f"Index written to {index_path}")

    if errors:
        print(f"\nERRORS ({len(errors)}):")
        for e in errors:
            print(f"  {e}")
        sys.exit(1)

    for stem, entry in index.items():
        png = Image.open(out_dir / entry["file"])
        assert png.size == (128, 128), f"{stem}: final PNG is {png.size}, expected 128x128"
        assert png.mode == "RGBA", f"{stem}: mode is {png.mode}, expected RGBA"

    print("All verification checks passed.")


if __name__ == "__main__":
    main()
