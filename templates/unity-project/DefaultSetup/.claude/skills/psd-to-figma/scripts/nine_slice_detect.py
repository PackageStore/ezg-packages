"""Detect 9-slice borders for the UI plate PNGs.

A plate is 9-sliceable when it has a band of consecutive identical columns
(and rows) in the middle: that band is what may be stretched. The longest such
run is the centre slice; everything outside it is a fixed corner/edge.
Output feeds both the Figma rebuild and Unity's Sprite.border.
"""
import json
import os
import sys

from PIL import Image

from pipeline_config import resolve

# Plates that are stretched to more than one size, or that are frames/buttons
# whose art is a border + flat centre. Icons and demo art are excluded: they
# must scale uniformly. The list is project data — see export.plates in the
# settings file.

TOLERANCES = (0, 2, 6, 12)


def longest_uniform_run(lines, tol):
    """Longest run of consecutive near-identical lines. Returns (start, end)."""
    best = (0, 0)
    start = 0
    for i in range(1, len(lines)):
        if max_diff(lines[i - 1], lines[i]) <= tol:
            continue
        if i - start > best[1] - best[0]:
            best = (start, i)
        start = i
    if len(lines) - start > best[1] - best[0]:
        best = (start, len(lines))
    return best


def max_diff(a, b):
    m = 0
    for x, y in zip(a, b):
        d = x - y
        if d < 0:
            d = -d
        if d > m:
            m = d
            if m > 255:
                break
    return m


def analyse(name, assets_dir):
    im = Image.open(os.path.join(assets_dir, name + ".png")).convert("RGBA")
    w, h = im.size
    px = im.tobytes()
    stride = w * 4

    rows = [px[y * stride:(y + 1) * stride] for y in range(h)]
    cols = [b"".join(px[y * stride + x * 4:y * stride + x * 4 + 4] for y in range(h)) for x in range(w)]

    result = {"size": [w, h]}
    for axis, lines, extent in (("x", cols, w), ("y", rows, h)):
        for tol in TOLERANCES:
            a, b = longest_uniform_run(lines, tol)
            if b - a >= 2 and a >= 1 and extent - b >= 1:
                result[axis] = {"lo": a, "hi": extent - b, "band": b - a, "tol": tol}
                break
        else:
            result[axis] = {"lo": 0, "hi": 0, "band": 0, "tol": None}
    return result


def main():
    cfg, _ = resolve()
    assets_dir = str(cfg.path("assets"))
    plates = cfg.settings.get("export", {}).get("plates", [])
    dest = str(cfg.path("nine_slice.json"))
    existing = {}
    if os.path.exists(dest):
        with open(dest) as fh:
            existing = json.load(fh)
    out = {}
    for name in plates:
        r = analyse(name, assets_dir)
        w, h = r["size"]
        border = [r["x"]["lo"], r["y"]["lo"], r["x"]["hi"], r["y"]["hi"]]
        entry = {
            "size": [w, h],
            "border": {"left": border[0], "top": border[1], "right": border[2], "bottom": border[3]},
            "band": [r["x"]["band"], r["y"]["band"]],
            "tol": [r["x"]["tol"], r["y"]["tol"]],
            "sliceable": {"x": r["x"]["band"] >= 2, "y": r["y"]["band"] >= 2},
        }
        if name in existing and "applied" in existing[name]:
            entry["applied"] = existing[name]["applied"]
        out[name] = entry
        print(f"{name:18s} {w:4d}x{h:<4d} border L{border[0]:<4d} T{border[1]:<4d} "
              f"R{border[2]:<4d} B{border[3]:<4d}  band={r['x']['band']}x{r['y']['band']} "
              f"tol={r['x']['tol']},{r['y']['tol']}")
    with open(dest, "w") as fh:
        json.dump(out, fh, indent=2)
    print("\nwrote", dest)


if __name__ == "__main__":
    sys.exit(main())
