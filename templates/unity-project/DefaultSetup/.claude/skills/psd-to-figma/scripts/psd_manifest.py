#!/usr/bin/env python3
"""
PSD Manifest Generator — single source of truth for the import.

Reads both PSDs, applies the Menu dx=-16 shift, classifies every layer,
and writes psd_manifest.json. Idempotent: running twice produces
byte-identical output.
"""

import json
import os
import sys
from collections import OrderedDict
from pathlib import Path

from psd_tools import PSDImage

from pipeline_config import resolve

_CFG, _ARGV = resolve()

PSD_DIR = _CFG.psd_dir
OUTPUT = _CFG.path("psd_manifest.json")
FRAME = _CFG.settings.get("frame", {})

SCREENS = OrderedDict(_CFG.load("screens").items())

_NAMES = _CFG.load("nodeNames")
SKIP_NAMES = set(_NAMES["skipNames"])
SKIP_ARTBOARD = _NAMES["skipArtboard"]
# UpgradeRow content box origin (inside the 2px outside stroke)
ROW_CONTENT_ORIGIN = tuple(_NAMES["rowContentOrigin"])
# UpgradeRow art box origin (includes stroke)
ROW_ART_ORIGIN = tuple(_NAMES["rowArtOrigin"])
ROW_PITCH = _NAMES["rowPitch"]
NODE_NAMES = _NAMES["nodeNames"]
ASSET_KEYS = _NAMES["assetKeys"]
TEXT_STYLES = _NAMES["textStyles"]

def classify_layer(layer, screen_key):
    """Return role: 'art', 'text', or 'skip'."""
    name = layer.name
    bbox = layer.bbox
    x, y = bbox[0], bbox[1]
    w, h = bbox[2] - bbox[0], bbox[3] - bbox[1]

    # Skip: wrapper artboard
    if name in SKIP_NAMES:
        return "skip"

    # Skip: zero-area
    if w == 0 and h == 0:
        return "skip"

    # Skip: hidden layers
    if not layer.visible:
        return "skip"

    # Text
    if layer.kind == "type":
        return "text"

    # Everything else is art
    return "art"


def get_text_type(layer):
    """Extract text type info from a type layer."""
    ed = layer.engine_dict
    rd = layer.resource_dict
    t = layer.transform

    raw_text = ed['Editor']['Text']
    text = raw_text.value if hasattr(raw_text, 'value') else str(raw_text)
    # Strip trailing \r (PSD text always ends with one)
    if text.endswith('\r'):
        text = text[:-1]
    # Convert internal \r to \n for JSON
    text = text.replace('\r', '\n')

    style_runs = ed['StyleRun']['RunArray']
    font_size = None
    font_idx = 0
    fill_color = None
    for run in style_runs:
        ssd = run['StyleSheet']['StyleSheetData']
        if 'FontSize' in ssd:
            font_size = float(ssd['FontSize'])
        if 'Font' in ssd:
            font_idx = int(ssd['Font'])
        if 'FillColor' in ssd:
            fc = ssd['FillColor']
            vals = [float(v) for v in fc.get('Values', [])]
            fill_color = vals

    font_set = rd.get('FontSet', [])
    if font_idx < len(font_set):
        raw_name = font_set[font_idx]['Name']
        font_name = raw_name.value if hasattr(raw_name, 'value') else str(raw_name)
    else:
        font_name = "unknown"

    scale_y = float(t[3])
    eff_size = round(font_size * scale_y) if font_size else 0
    raw_size = round(font_size, 5) if font_size else 0

    # Enabled effects
    enabled_effects = []
    if hasattr(layer, 'effects') and layer.effects:
        for e in layer.effects:
            if getattr(e, 'enabled', True):
                enabled_effects.append(type(e).__name__)

    style_name = TEXT_STYLES.get(layer.name, "Unknown")

    result = OrderedDict([
        ("style", style_name),
        ("content", text),
        ("font", font_name),
        ("rawSize", raw_size),
        ("scale", round(scale_y, 6)),
        ("effSize", eff_size),
    ])
    if fill_color:
        result["fillColor"] = [round(float(v), 5) for v in fill_color]
    if enabled_effects:
        result["effects"] = enabled_effects

    return result


def process_layer(layer, screen_key, dx, dy, parent_group=None):
    """Process a single layer and return a manifest entry (or None for skips)."""
    role = classify_layer(layer, screen_key)

    bbox = layer.bbox
    raw_x, raw_y = bbox[0], bbox[1]
    w, h = bbox[2] - bbox[0], bbox[3] - bbox[1]

    # Apply screen shift
    x = raw_x + dx
    y = raw_y + dy

    opacity = round(int(layer.opacity) / 255.0, 4)

    name = layer.name
    qname = f"{screen_key}/{name}"
    node = NODE_NAMES.get(qname, NODE_NAMES.get(name, name))

    # For BTN_main inside groups, use the group context for the node name
    if name == "BTN_main" and parent_group:
        node = f"Btn_{parent_group}"

    entry = OrderedDict()
    entry["screen"] = screen_key
    entry["psdName"] = name
    entry["node"] = node
    entry["role"] = role

    if role == "art":
        asset = ASSET_KEYS.get(qname, ASSET_KEYS.get(name, name.lower().replace(" ", "_")))
        entry["asset"] = asset

    entry["x"] = x
    entry["y"] = y
    entry["w"] = w
    entry["h"] = h
    entry["opacity"] = opacity

    if role == "text":
        entry["type"] = get_text_type(layer)

    if parent_group:
        entry["group"] = parent_group

    return entry


def process_upgrade_row_children(layers, screen_key, dx, dy):
    """Process a repeating row's children with row-local offsets."""
    results = []
    content_ox, content_oy = ROW_CONTENT_ORIGIN
    art_ox, art_oy = ROW_ART_ORIGIN

    for layer in layers:
        role = classify_layer(layer, screen_key)
        if role == "skip":
            entry = OrderedDict([
                ("screen", screen_key),
                ("psdName", layer.name),
                ("node", NODE_NAMES.get(layer.name, layer.name)),
                ("role", "skip"),
                ("x", 0), ("y", 0),
                ("w", layer.bbox[2] - layer.bbox[0]),
                ("h", layer.bbox[3] - layer.bbox[1]),
                ("opacity", round(int(layer.opacity) / 255.0, 4)),
            ])
            results.append(entry)
            continue

        bbox = layer.bbox
        raw_x, raw_y = bbox[0], bbox[1]
        w, h = bbox[2] - bbox[0], bbox[3] - bbox[1]

        # Row-local offset from content box origin
        local_x = raw_x - content_ox
        local_y = raw_y - content_oy

        # Absolute position (with screen shift)
        abs_x = raw_x + dx
        abs_y = raw_y + dy

        opacity = round(int(layer.opacity) / 255.0, 4)
        name = layer.name
        qname = f"{screen_key}/{name}"
        node = NODE_NAMES.get(qname, NODE_NAMES.get(name, name))

        entry = OrderedDict()
        entry["screen"] = screen_key
        entry["psdName"] = name
        entry["node"] = node
        entry["role"] = role

        if role == "art":
            asset = ASSET_KEYS.get(qname, ASSET_KEYS.get(name, name.lower().replace(" ", "_")))
            entry["asset"] = asset

        entry["x"] = abs_x
        entry["y"] = abs_y
        entry["w"] = w
        entry["h"] = h
        entry["opacity"] = opacity

        # Row-local offsets for component building
        entry["rowLocal"] = OrderedDict([
            ("x", local_x),
            ("y", local_y),
        ])
        entry["rowArtOrigin"] = OrderedDict([
            ("x", raw_x - art_ox),
            ("y", raw_y - art_oy),
        ])

        if role == "text":
            entry["type"] = get_text_type(layer)

        entry["isRowChild"] = True
        results.append(entry)

    return results


def walk_screen(psd, screen_key, dx, dy, screen_cfg):
    """Walk all layers in a PSD screen, depth-first."""
    layers = []
    unclassified = []
    walk_mode = screen_cfg.get("walkMode", "tree")
    row_children_names = set(screen_cfg.get("rowChildren", []))

    def _walk(layer, parent_group=None):
        name = layer.name

        # For menu: groups contain children we process individually
        if hasattr(layer, '__iter__') and layer.kind in ("group", "artboard"):
            if name == SKIP_ARTBOARD:
                # Skip the artboard wrapper itself, process children
                entry = process_layer(layer, screen_key, dx, dy)
                layers.append(entry)
                for child in layer:
                    _walk(child)
                return

            # Named groups (Shop, Boots, talent, etc.)
            group_name = name.replace(" - Smart Object Group", "")
            entry = OrderedDict([
                ("screen", screen_key),
                ("psdName", name),
                ("node", f"Group_{group_name}"),
                ("role", "group"),
                ("x", layer.bbox[0] + dx),
                ("y", layer.bbox[1] + dy),
                ("w", layer.bbox[2] - layer.bbox[0]),
                ("h", layer.bbox[3] - layer.bbox[1]),
                ("opacity", round(int(layer.opacity) / 255.0, 4)),
            ])
            layers.append(entry)
            for child in layer:
                _walk(child, parent_group=group_name)
            return

        entry = process_layer(layer, screen_key, dx, dy, parent_group)
        if entry:
            layers.append(entry)
            role = entry["role"]
            if role not in ("art", "text", "skip", "group"):
                unclassified.append(entry)

    if walk_mode == "rows":
        for layer in psd:
            if layer.name in row_children_names:
                row_entries = process_upgrade_row_children([layer], screen_key, dx, dy)
                layers.extend(row_entries)
            else:
                entry = process_layer(layer, screen_key, dx, dy)
                if entry:
                    layers.append(entry)
    else:
        for layer in psd:
            _walk(layer)

    return layers, unclassified


def build_manifest():
    manifest = OrderedDict()

    # Screens metadata
    screens_meta = OrderedDict()
    for key, cfg in SCREENS.items():
        screens_meta[key] = OrderedDict([
            ("psd", cfg["psd"]),
            ("psdW", cfg["psdW"]),
            ("psdH", cfg["psdH"]),
            ("frame", OrderedDict([("w", FRAME["w"]), ("h", FRAME["h"])])),
            ("dx", cfg["dx"]),
            ("dy", cfg["dy"]),
        ])
    manifest["screens"] = screens_meta

    # Load existing manifest to preserve data for screens whose PSDs are absent
    existing_layers_by_screen = {}
    if OUTPUT.exists():
        with open(OUTPUT, "r", encoding="utf-8") as f:
            existing = json.load(f)
        for entry in existing.get("layers", []):
            existing_layers_by_screen.setdefault(entry["screen"], []).append(entry)

    all_layers = []
    all_unclassified = []

    for key, cfg in SCREENS.items():
        psd_path = PSD_DIR / cfg["psd"]
        if not psd_path.exists():
            preserved = existing_layers_by_screen.get(key, [])
            print(f"PSD missing for {key}, preserving {len(preserved)} existing layers")
            all_layers.extend(preserved)
            continue
        psd = PSDImage.open(str(psd_path))
        layers, unclassified = walk_screen(psd, key, cfg["dx"], cfg["dy"], cfg)
        all_layers.extend(layers)
        all_unclassified.extend(unclassified)

    manifest["layers"] = all_layers

    # Print summary
    role_counts = {}
    for entry in all_layers:
        role = entry["role"]
        role_counts[role] = role_counts.get(role, 0) + 1

    print(f"Layer counts by role: {dict(sorted(role_counts.items()))}")
    print(f"Total layers: {len(all_layers)}")

    # Count unique assets
    assets = set()
    for entry in all_layers:
        if "asset" in entry:
            assets.add(entry["asset"])
    print(f"Unique art assets: {len(assets)}")

    # Unclassified
    if all_unclassified:
        print("\nUNCLASSIFIED LAYERS:")
        for entry in all_unclassified:
            print(f"  {entry['screen']}/{entry['psdName']}: role={entry['role']}")

    # Skip summary
    skip_layers = [e for e in all_layers if e["role"] == "skip"]
    print(f"\nSkipped layers ({len(skip_layers)}):")
    for e in skip_layers:
        print(f"  {e['screen']}/{e['psdName']}: {e['w']}x{e['h']} vis={'visible' if e.get('opacity', 0) > 0 else 'hidden'}")

    return manifest


def main():
    manifest = build_manifest()

    with open(OUTPUT, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2, ensure_ascii=False)
        f.write("\n")

    print(f"\nWrote {OUTPUT}")


if __name__ == "__main__":
    main()
