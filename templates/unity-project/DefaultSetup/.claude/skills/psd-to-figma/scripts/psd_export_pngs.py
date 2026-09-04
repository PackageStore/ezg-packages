#!/usr/bin/env python3
"""Export role:art layers from PSD files as trimmed RGBA PNGs.

Effects are baked into pixels; layer opacity is NOT baked (preserved as
metadata for Figma node opacity, set by plan 19).
"""

import hashlib
import json
import sys

from PIL import Image
from psd_tools import PSDImage

from pipeline_config import resolve
from psd_opacity import bakes_fill_opacity, has_enabled_effects

MAX_BYTES = 10 * 1024 * 1024  # 10 MB upload_assets limit


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
    if bakes_fill_opacity(layer):
        img = layer.composite(viewport=layer.bbox)
    else:
        img = layer.topil()

    if img is None:
        raise RuntimeError(f"No image data for layer {layer.name!r}")
    return img.convert("RGBA")


def allow_shared_reason(allow_shared, key, errors):
    if key not in allow_shared:
        return False
    reason = allow_shared.get(key)
    if not (isinstance(reason, str) and reason.strip()):
        msg = f"ALLOWSHARED MISSING REASON: {key}"
        if msg not in errors:
            errors.append(msg)
    return True


def render_digest(entry, psd_cache, screens):
    psd = psd_cache[entry["screen"]]
    si = screens[entry["screen"]]
    dx, dy = si["dx"], si["dy"]
    psd_left = entry["x"] - dx
    psd_top = entry["y"] - dy
    layer = find_layer(psd, entry["psdName"], psd_left, psd_top,
                       psd_left + entry["w"], psd_top + entry["h"])
    if layer is None:
        return None
    try:
        img = export_layer_image(layer)
    except Exception:
        return None
    return hashlib.sha1(img.tobytes()).hexdigest()


def main():
    cfg, _ = resolve()
    export = cfg.settings.get("export", {})
    skip_assets = set(export.get("skipAssets", []))
    tie_break = export.get("tieBreak", {})
    allow_shared = export.get("allowShared", {})
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

    stem_layers = {}
    for entry in layers:
        if entry["role"] != "art":
            continue
        key = entry["asset"]
        if key in skip_assets:
            continue
        stem_layers.setdefault(key, []).append(entry)

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

    if assets_index.exists():
        with open(assets_index) as f:
            index = json.load(f)
    else:
        index = {}
    errors = []
    winner_digest = {}

    for key, entry in sorted(asset_map.items()):
        screen = entry["screen"]
        if screen not in psd_cache:
            existing = assets_dir / f"{key}.png"
            if existing.exists():
                nbytes = existing.stat().st_size
                from PIL import Image as _Img
                with _Img.open(str(existing)) as _im:
                    ew, eh = _im.size
                    winner_digest[key] = (
                        hashlib.sha1(_im.convert("RGBA").tobytes()).hexdigest(),
                        ew, eh, entry)
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

        winner_digest[key] = (hashlib.sha1(img.tobytes()).hexdigest(),
                              w, h, entry)

        out = assets_dir / f"{key}.png"
        img.save(str(out), "PNG")
        nbytes = out.stat().st_size

        if nbytes > MAX_BYTES:
            errors.append(f"TOO LARGE {key}: {nbytes} bytes > 10 MB")

        baked = "fill" if bakes_fill_opacity(layer) else "none"
        fo = entry.get("fillOpacity")
        if fo is not None:
            lo = entry.get("layerOpacity", 1.0)
            expected = lo if baked == "fill" else round(lo * fo, 4)
            if abs(entry["opacity"] - expected) > 2e-4:
                errors.append(
                    f"OPACITY PATH MISMATCH {entry['psdName']!r} ({key}): "
                    f"manifest opacity {entry['opacity']} != {expected} for "
                    f"bakedOpacity={baked!r}")
                continue

        index[key] = {"file": f"assets/{key}.png", "w": w, "h": h,
                      "bytes": nbytes, "bakedOpacity": baked}
        print(f"  {key}: {w}x{h}  {nbytes:,} bytes"
              f"  [effects baked]" if has_enabled_effects(layer) else
              f"  {key}: {w}x{h}  {nbytes:,} bytes")

    with open(assets_index, "w") as f:
        json.dump(index, f, indent=2)
    print(f"\nWrote {assets_index.name} ({len(index)} entries)")

    collisions = []
    for key in sorted(winner_digest):
        if allow_shared_reason(allow_shared, key, errors):
            continue
        w_digest, w, h, w_entry = winner_digest[key]
        for other in stem_layers.get(key, []):
            if other is w_entry or other["screen"] not in psd_cache:
                continue
            if (other["w"], other["h"]) != (w, h):
                continue
            od = render_digest(other, psd_cache, screens)
            if od is None:
                errors.append(
                    f"COLLISION CHECK: could not render "
                    f"{other['psdName']!r} in {other['screen']} for {key}")
                continue
            if od != w_digest:
                collisions.append(
                    f"STEM COLLISION {key}: "
                    f"{w_entry['screen']}/{w_entry['psdName']} {w}x{h} vs "
                    f"{other['screen']}/{other['psdName']} "
                    f"{other['w']}x{other['h']}")
                break

    icons_index = cfg.load_optional("icons_index.json")
    if icons_index:
        for stem in sorted(set(index) & set(icons_index)):
            if allow_shared_reason(allow_shared, stem, errors):
                continue
            collisions.append(
                f"STEM COLLISION {stem}: assets_index.json vs "
                f"icons_index.json (namespace overlap)")

    if collisions:
        print("\nCOLLISIONS:")
        for c in collisions:
            print(f"  {c}")

    if errors:
        print("\nERRORS:")
        for e in errors:
            print(f"  {e}")

    if collisions:
        sys.exit(3)
    if errors:
        sys.exit(1)

    print(f"\nDone: {len(index)} assets exported.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
