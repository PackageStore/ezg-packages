#!/usr/bin/env python3
"""Compute the expected glyph ink box for a text string rendered at a given size."""

import sys
from collections import OrderedDict
from pathlib import Path

from PIL import ImageFont


def _line_bbox(font, text):
    bbox = font.getbbox(text, anchor="ls")
    return bbox[2] - bbox[0], bbox[1] - bbox[3]


def _line_bbox_tracked(font, text, letter_spacing):
    if len(text) <= 1:
        return _line_bbox(font, text)
    w = 0.0
    for i, ch in enumerate(text):
        bx = font.getbbox(ch, anchor="ls")
        char_w = bx[2] - bx[0]
        if i < len(text) - 1:
            w += font.getlength(ch) + letter_spacing
        else:
            w += char_w
    bbox = font.getbbox(text[0], anchor="ls")
    h = bbox[1] - bbox[3]
    return w, h


def _load_font(font_path, size, weight_hint=""):
    font = ImageFont.truetype(str(font_path), size)
    if weight_hint:
        try:
            font.set_variation_by_name(weight_hint)
        except Exception:
            pass
    return font


def ink_box(text, font_path, size, tracking=0, weight_hint=""):
    font = _load_font(font_path, size, weight_hint)
    letter_spacing = tracking / 1000.0 * size if tracking else 0

    lines = text.replace("\r", "\n").split("\n")
    lines = [l for l in lines if l]
    if not lines:
        return None

    if letter_spacing:
        line_dims = [_line_bbox_tracked(font, l, letter_spacing) for l in lines]
    else:
        line_dims = [_line_bbox(font, l) for l in lines]

    max_w = max(d[0] for d in line_dims)

    if len(lines) == 1:
        h = line_dims[0][1]
        source = "freetype"
    else:
        metrics = font.getmetrics()
        leading = metrics[0] + metrics[1]
        if leading > 0:
            h = leading * len(lines)
            source = "freetype"
        else:
            h = size * 1.2 * len(lines)
            source = "freetype-approx"

    result = OrderedDict([
        ("w", round(max_w, 1)),
        ("h", round(abs(h), 1)),
        ("source", source),
    ])
    if tracking:
        result["tracking"] = tracking
    return result


def _selftest():
    candidates = [
        Path("/System/Library/Fonts/Helvetica.ttc"),
        Path("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"),
        Path("/usr/share/fonts/TTF/DejaVuSans.ttf"),
    ]
    font_path = None
    for p in candidates:
        if p.exists():
            font_path = p
            break
    if font_path is None:
        print("SKIP: no system font found", file=sys.stderr)
        sys.exit(0)

    r = ink_box("H", font_path, 45)
    assert r is not None, "ink_box returned None"
    assert r["w"] > 0, f"w should be positive, got {r['w']}"
    assert r["h"] > 0, f"h should be positive, got {r['h']}"
    assert r["source"] == "freetype"

    r2 = ink_box("Hello\nWorld", font_path, 45)
    assert r2 is not None
    assert r2["w"] > r["w"]
    assert r2["h"] > r["h"]

    r3 = ink_box("AB", font_path, 45, tracking=100)
    r4 = ink_box("AB", font_path, 45, tracking=0)
    assert r3["w"] > r4["w"], "tracked text should be wider"
    assert r3.get("tracking") == 100

    r5 = ink_box("", font_path, 45)
    assert r5 is None

    print("text_ink selftest: OK")


if __name__ == "__main__":
    if "--selftest" in sys.argv:
        _selftest()
    else:
        print("Usage: text_ink.py --selftest", file=sys.stderr)
        sys.exit(1)
