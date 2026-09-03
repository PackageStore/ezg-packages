#!/usr/bin/env python3
"""Export role:art layers from PSD files as trimmed RGBA PNGs.

Effects are baked into pixels; layer opacity is NOT baked (preserved as
metadata for Figma node opacity, set by plan 19).
"""

import json
import sys

from PIL import Image
from psd_tools import PSDImage

from pipeline_config import resolve

MAX_BYTES = 10 * 1024 * 1024  # 10 MB upload_assets limit


def has_enabled_effects(layer):
    if not layer.effects:
        return False
    return any(e.enabled for e in layer.effects)


def find_layer(psd, psd_name, psd_left, psd_top, psd_right, psd_bottom):
    candidates = [l for l in psd.descendants() if l.name == psd_name]
    if len(candidates) == 1:
        return candidates[0]
    for l in candidates:
        if l.bbox == (psd_left, psd_top, psd_right, psd_bottom):
            return l
    for l in candidates:
        if (abs(l.bbox[0] - psd_left) <= 2
                and abs(l.bbox[1] - psd_top) <= 2):
            return l
    return None


def export_layer_image(layer):
    if has_enabled_effects(layer):
        img = layer.composite(viewport=layer.bbox)
    else:
        img = layer.topil()

    if img is None:
        raise RuntimeError(f"No image data for layer {layer.name!r}")
    return img.convert("RGBA")


def main():
    cfg, _ = resolve()
    export = cfg.settings.get("export", {})
    skip_assets = set(export.get("skipAssets", []))
    tie_break = export.get("tieBreak", {})
    psd_dir = cfg.psd_dir
    assets_dir = cfg.path("assets")
    assets_index = cfg.path("assets_index.json")

    manifest = cfg.load("psd_manifest.json")
    screens = manifest["screens"]
    layers = manifest["layers"]

    # Build unique asset map; tie-break prefers a specific instance size
    asset_map = {}
    for entry in layers:
        if entry["role"] != "art":
            continue
        key = entry["asset"]
        if key in skip_assets:
            continue
        if key not in asset_map:
            asset_map[key] = entry
        elif (key in tie_break
                and entry["w"] == tie_break[key]["w"]
                and entry["h"] == tie_break[key]["h"]):
            asset_map[key] = entry

    print(f"Unique art assets to export: {len(asset_map)}")

    psd_cache = {}
    for sk, si in screens.items():
        path = psd_dir / si["psd"]
        if not path.exists():
            print(f"  skipped {si['psd']} (not found)")
            continue
        psd_cache[sk] = PSDImage.open(str(path))
        print(f"  loaded {si['psd']} ({psd_cache[sk].width}x{psd_cache[sk].height})")

    assets_dir.mkdir(parents=True, exist_ok=True)

    index = {}
    errors = []

    for key, entry in sorted(asset_map.items()):
        screen = entry["screen"]
        if screen not in psd_cache:
            existing = assets_dir / f"{key}.png"
            if existing.exists():
                nbytes = existing.stat().st_size
                from PIL import Image as _Img
                with _Img.open(str(existing)) as _im:
                    ew, eh = _im.size
                index[key] = {"file": f"assets/{key}.png", "w": ew, "h": eh,
                              "bytes": nbytes}
                print(f"  {key}: {ew}x{eh}  {nbytes:,} bytes  [preserved, PSD absent]")
            else:
                errors.append(f"PSD MISSING for {screen}, no existing PNG for {key}")
            continue
        psd = psd_cache[screen]
        si = screens[screen]
        dx, dy = si["dx"], si["dy"]

        psd_left = entry["x"] - dx
        psd_top = entry["y"] - dy
        psd_right = psd_left + entry["w"]
        psd_bottom = psd_top + entry["h"]
        target_w, target_h = entry["w"], entry["h"]

        layer = find_layer(psd, entry["psdName"], psd_left, psd_top,
                           psd_right, psd_bottom)
        if layer is None:
            errors.append(f"NOT FOUND: {entry['psdName']!r} in {screen}")
            continue

        try:
            img = export_layer_image(layer)
        except Exception as e:
            errors.append(f"EXPORT FAIL {key}: {e}")
            continue

        w, h = img.size
        if w != target_w or h != target_h:
            errors.append(
                f"SIZE MISMATCH {key}: expected {target_w}x{target_h}, "
                f"got {w}x{h}")
            continue

        out = assets_dir / f"{key}.png"
        img.save(str(out), "PNG")
        nbytes = out.stat().st_size

        if nbytes > MAX_BYTES:
            errors.append(f"TOO LARGE {key}: {nbytes} bytes > 10 MB")

        index[key] = {"file": f"assets/{key}.png", "w": w, "h": h,
                      "bytes": nbytes}
        print(f"  {key}: {w}x{h}  {nbytes:,} bytes"
              f"  [effects baked]" if has_enabled_effects(layer) else
              f"  {key}: {w}x{h}  {nbytes:,} bytes")

    with open(assets_index, "w") as f:
        json.dump(index, f, indent=2)
    print(f"\nWrote {assets_index.name} ({len(index)} entries)")

    if errors:
        print("\nERRORS:")
        for e in errors:
            print(f"  {e}")
        sys.exit(1)

    print(f"\nDone: {len(index)} assets exported.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
