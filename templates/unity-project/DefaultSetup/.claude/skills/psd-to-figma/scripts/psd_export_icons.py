#!/usr/bin/env python3
"""Export icon PSDs to RGBA PNGs and build icons_index.json.

Each entry in the icons ``sources`` list names a leaf PSD directory, its
expected canvas size, an optional skip set and variant overrides, and the
prefixes that map a stem to its component set and variant. The legacy
single-source form (top-level ``subdir``/``skip``/``variantOverrides``/
``setPrefixes``) is still accepted when no ``sources`` list is present.
"""

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
            "psd2figma.json has no \"icons\" section: add a sources list "
            "(or the legacy subdir/skip/variantOverrides/setPrefixes) before "
            "running this stage")
    return icons


def source_list(icons: dict) -> list:
    if "sources" in icons:
        return icons["sources"]
    return [{
        "subdir": icons["subdir"],
        "size": icons.get("size", [128, 128]),
        "skip": icons.get("skip", []),
        "variantOverrides": icons.get("variantOverrides", {}),
        "setPrefixes": icons["setPrefixes"],
    }]


def derive_variant(stem: str, source: dict) -> str:
    override = source.get("variantOverrides", {}).get(stem)
    if override:
        return override
    for prefix, _ in source["setPrefixes"]:
        if stem.startswith(prefix):
            return stem[len(prefix):].replace("_", " ").title()
    raise ValueError(f"Unknown prefix for {stem}")


def derive_set(stem: str, source: dict) -> str:
    for prefix, set_name in source["setPrefixes"]:
        if stem.startswith(prefix):
            return set_name
    raise ValueError(f"Unknown prefix for {stem}")


def export_icon(psd_path: Path, out_path: Path, size: tuple) -> Image.Image:
    psd = PSDImage.open(psd_path)
    img = psd.composite()
    if img.mode != "RGBA":
        img = img.convert("RGBA")
    assert img.size == size, \
        f"{psd_path.name}: expected {size[0]}x{size[1]}, got {img.size}"
    img.save(out_path, "PNG")
    return img


def main():
    cfg, _ = resolve()
    icons = icon_settings(cfg)
    out_dir = cfg.path("icons")
    index_path = cfg.path("icons_index.json")
    out_dir.mkdir(parents=True, exist_ok=True)

    index = cfg.load_optional("icons_index.json", {}) or {}
    errors = []
    exported = 0

    for source in source_list(icons):
        src_dir = cfg.psd_dir.joinpath(*source["subdir"])
        size = tuple(source["size"])
        skip = set(source.get("skip", []))
        if not src_dir.is_dir():
            print(f"source directory not found, skipping: {src_dir}")
            continue
        psd_files = sorted(
            p for p in src_dir.rglob("*.psd") if p.stem not in skip
        )
        if not psd_files:
            raise SystemExit(
                f"no .psd files under {src_dir}; refusing to run an empty "
                "source")

        seen = {}
        for psd_path in psd_files:
            stem = psd_path.stem
            out_path = out_dir / f"{stem}.png"
            try:
                img = export_icon(psd_path, out_path, size)
            except Exception as e:
                errors.append(f"{stem}: {e}")
                continue

            if img.getbbox() is None:
                errors.append(f"{stem}: fully transparent (blank icon)")

            set_name = derive_set(stem, source)
            variant = derive_variant(stem, source)
            claimed = seen.setdefault(set_name, {})
            if variant in claimed:
                errors.append(
                    f"{set_name}: variant {variant!r} from {stem} collides "
                    f"with {claimed[variant]}")
            else:
                claimed[variant] = stem

            index[stem] = {
                "file": f"{stem}.png",
                "set": set_name,
                "variant": variant,
                "size": [size[0], size[1]],
            }
            exported += 1

    index_path.write_text(json.dumps(index, indent=2) + "\n")

    per_set = Counter(v["set"] for v in index.values())
    breakdown = ", ".join(f"{n} {s}" for s, n in sorted(per_set.items()))

    print(f"Exported {exported} icons this run; "
          f"index holds {len(index)} ({breakdown})")
    print(f"Index written to {index_path}")

    if errors:
        print(f"\nERRORS ({len(errors)}):")
        for e in errors:
            print(f"  {e}")
        sys.exit(1)

    for stem, entry in index.items():
        png = Image.open(out_dir / entry["file"])
        if "size" in entry:
            assert tuple(png.size) == tuple(entry["size"]), \
                f"{stem}: final PNG is {png.size}, expected {tuple(entry['size'])}"
        assert png.mode == "RGBA", f"{stem}: mode is {png.mode}, expected RGBA"

    print("All verification checks passed.")


if __name__ == "__main__":
    main()
