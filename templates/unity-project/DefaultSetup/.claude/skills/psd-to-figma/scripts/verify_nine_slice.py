#!/usr/bin/env python3
"""Verify recovered nine-slice borders against Unity sprite meta files.

Plan 18 of docs/plans/figma-bridge-fork.

What this checks, and what it deliberately does not:

Collapsing a slice grid is an OPTIMISATION, not a correctness fix - the
uncollapsed grid already renders correctly via FigmaImage.ImageTransform and
Figma constraints. So a border the importer declined to recover is acceptable,
and only a WRONG border is a regression. The gate reflects that: `skipped` passes,
`MISMATCH` and `UNEXPECTED` fail.

Assets are matched by pixel size, not by name. Figma names an image fill by
imageRef, a content hash, so the name never matches. Size is doing all the work,
and it is not injective: three 216x176 assets and two 306x424 assets share a size
in nine_slice.json, and Figma dedupes identical art into a single fill. Where a
size is shared, this script matches on the border VALUE within the size group and
reports anything it cannot pin down as `ambiguous` rather than inventing a
per-name verdict.
"""

import argparse
import json
import os
import re
import struct
import sys
from collections import defaultdict

from pipeline_config import resolve

BORDER_RE = re.compile(
    r"spriteBorder:\s*\{x:\s*(-?[\d.]+),\s*y:\s*(-?[\d.]+),"
    r"\s*z:\s*(-?[\d.]+),\s*w:\s*(-?[\d.]+)\}"
)

ZERO = (0, 0, 0, 0)


def read_image_fill_folder(cfg):
    settings_asset = os.path.join(
        cfg.project_root, "Assets", "UnityFigmaBridgeSettings.asset"
    )
    try:
        with open(settings_asset) as f:
            for line in f:
                if "ImageFillFolder:" in line:
                    val = line.split(":", 1)[1].strip()
                    if val:
                        return os.path.join(cfg.project_root, val)
    except FileNotFoundError:
        pass
    rel = cfg.settings.get("paths", {}).get("spriteDir")
    if not rel:
        raise SystemExit(
            "no sprite folder: the Unity settings asset carries no "
            "ImageFillFolder, so add paths.spriteDir to psd2figma.json")
    return os.path.join(cfg.project_root, rel)


def png_size(path):
    with open(path, "rb") as f:
        if f.read(8)[:4] != b"\x89PNG":
            return None
        f.read(8)  # IHDR length + type
        return struct.unpack(">II", f.read(8))


def parse_meta_border(meta_path):
    """Unity stores x=left, y=bottom, z=right, w=top. Return (L, T, R, B)."""
    try:
        with open(meta_path) as f:
            m = BORDER_RE.search(f.read())
    except FileNotFoundError:
        return None
    if not m:
        return None
    x, y, z, w = (int(float(v)) for v in m.groups())
    return (x, w, z, y)


def load_expected(cfg):
    """name -> (size, (L, T, R, B)). applied.border is the authority."""
    with open(cfg.path("nine_slice.json")) as f:
        data = json.load(f)
    out = {}
    for name, entry in data.items():
        applied = entry.get("applied", {}).get("border")
        border = tuple(int(v) for v in applied) if applied else ZERO
        out[name] = (tuple(entry["size"]), border)
    return out


def scan_sprites(sprite_dir):
    """size -> [(filename, (L, T, R, B))]"""
    out = defaultdict(list)
    if not os.path.isdir(sprite_dir):
        return out
    # Fills are grouped into Screens/<Screen>/ and Components/<Component>/ subfolders, so this
    # has to recurse; a flat listing silently finds nothing and the gate passes on no data.
    found = []
    for root, _, files in os.walk(sprite_dir):
        for fn in files:
            if fn.endswith(".png"):
                found.append(os.path.join(root, fn))
    for path in sorted(found):
        size = png_size(path)
        if size is None:
            continue
        rel = os.path.relpath(path, sprite_dir)
        out[size].append((rel, parse_meta_border(path + ".meta") or ZERO))
    return out


def fmt(b, labels="LTRB"):
    return " ".join(f"{c}{v}" for c, v in zip(labels, b))


def classify_group(names, expected, sprites):
    """Resolve one size group. Returns [(name, found_or_None, status)].

    Within a group, a sprite is claimed by the name whose expected border it
    equals. Each sprite is claimed at most once, so a single deduped fill cannot
    satisfy several names at the same time.
    """
    remaining = list(sprites)
    rows = []
    ambiguous = len(names) > 1

    for name in names:
        want = expected[name][1]
        hit = next((i for i, (_, b) in enumerate(remaining) if b == want), None)
        if hit is not None:
            remaining.pop(hit)
            rows.append((name, want, "ok"))
            continue
        rows.append((name, None, None))  # unresolved for now

    # Hand any leftover sprite to an unresolved name so a wrong border is seen.
    for idx, (name, found, status) in enumerate(rows):
        if status is not None:
            continue
        want = expected[name][1]
        if not remaining:
            rows[idx] = (name, None, "ambiguous" if ambiguous else "no sprite")
            continue
        _, got = remaining.pop(0)
        if got == ZERO and want != ZERO:
            rows[idx] = (name, got, "skipped")
        elif got != ZERO and want == ZERO:
            rows[idx] = (name, got, "UNEXPECTED")
        elif ambiguous:
            rows[idx] = (name, got, "ambiguous")
        else:
            rows[idx] = (name, got, "MISMATCH")

    return rows


def main():
    cfg, argv = resolve()

    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--sprites", help="override the image fill folder")
    ap.add_argument("--json", action="store_true", help="machine-readable output")
    args = ap.parse_args(argv)

    sprite_dir = os.path.abspath(args.sprites or read_image_fill_folder(cfg))
    expected = load_expected(cfg)
    sprites = scan_sprites(sprite_dir)

    by_size = defaultdict(list)
    for name, (size, _) in expected.items():
        by_size[size].append(name)

    results = []
    for size in sorted(by_size):
        for name, found, status in classify_group(
            sorted(by_size[size]), expected, sprites.get(size, [])
        ):
            want = expected[name][1]
            if found is not None and any(found):
                left, top, right, bottom = found
                width, height = size
                if left + right >= width or top + bottom >= height:
                    status = "MISMATCH"
            results.append(
                {
                    "name": name,
                    "size": size,
                    "expected": want,
                    "found": found,
                    "status": status,
                }
            )

    results.sort(key=lambda r: r["name"])
    counts = defaultdict(int)
    for r in results:
        counts[r["status"]] += 1
    sliced = sum(1 for group in sprites.values() for _, b in group if any(b))
    failures = counts["MISMATCH"] + counts["UNEXPECTED"]

    if args.json:
        print(
            json.dumps(
                {
                    "spriteFolder": sprite_dir,
                    "results": [
                        {
                            "name": r["name"],
                            "status": r["status"],
                            "size": list(r["size"]),
                            "expected": list(r["expected"]),
                            "found": list(r["found"]) if r["found"] else None,
                        }
                        for r in results
                    ],
                    "summary": {
                        "ok": counts["ok"],
                        "skipped": counts["skipped"],
                        "ambiguous": counts["ambiguous"],
                        "noSprite": counts["no sprite"],
                        "mismatched": counts["MISMATCH"],
                        "unexpected": counts["UNEXPECTED"],
                        "slicedSprites": sliced,
                        "failures": failures,
                    },
                },
                indent=2,
            )
        )
    else:
        print(f"Sprite folder: {sprite_dir}")
        print("Matched by pixel size; Figma names fills by content hash, so names")
        print("never match. Sizes are not unique, so shared sizes report 'ambiguous'.\n")
        hdr = f"{'Name':<28} {'Size':<12} {'Expected (L,T,R,B)':<22} {'Found (L,T,R,B)':<22} Status"
        print(hdr)
        print("-" * len(hdr))
        for r in results:
            size_str = f"{r['size'][0]}x{r['size'][1]}"
            found_str = fmt(r["found"]) if r["found"] is not None else "-"
            print(
                f"{r['name']:<28} {size_str:<12} {fmt(r['expected']):<22} "
                f"{found_str:<22} {r['status']}"
            )

        print()
        print(
            f"ok {counts['ok']}   skipped {counts['skipped']}   "
            f"ambiguous {counts['ambiguous']}   no-sprite {counts['no sprite']}   "
            f"MISMATCH {counts['MISMATCH']}   UNEXPECTED {counts['UNEXPECTED']}"
        )
        print(f"sprites carrying a border: {sliced}")
        print()
        print("ok         border recovered and equal to what the PSD import applied")
        print("skipped    a border was expected but the sprite was left unsliced -")
        print("           acceptable, the uncollapsed grid still renders correctly")
        print("ambiguous  several assets share this pixel size and Figma dedupes")
        print("           identical art, so no per-name verdict is possible")
        print("no-sprite  the asset is not present in the imported screens")
        print("MISMATCH   a border was recovered but differs - a regression")
        print("UNEXPECTED a border was applied where none belongs - a regression")

    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
