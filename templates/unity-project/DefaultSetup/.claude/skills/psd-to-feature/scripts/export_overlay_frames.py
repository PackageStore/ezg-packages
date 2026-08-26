#!/usr/bin/env python3
"""
Xuất layer có hiệu ứng ColorOverlay blend=Overlay mà psd-tools KHÔNG render đúng.

Ca thật: frame_rarity_net.psd — 16 khung rarity là CÙNG một smart object xám (viền tối + bóng
đáy), mỗi bản chỉ khác hiệu ứng Color Overlay (blend Overlay) đổi màu. `layer.composite()` và cả
`psd.composite(layer_filter=...)` của psd-tools đè màu PHẲNG lên toàn alpha (mất viền, mất bóng);
bản preview đúng chỉ vì Photoshop đã lưu ảnh flatten sẵn trong file.

Tool này lấy pixel GỐC của layer (`topil()`, chưa effect) rồi áp công thức Overlay của Photoshop
theo từng kênh với màu của effect: base < 0.5 → 2·base·blend, ngược lại → 1 − 2·(1−base)·(1−blend).
Alpha giữ nguyên. Chỉ xử lý layer trong map có đúng một effect ColorOverlay blend Overlay; layer
khác giữ nguyên PNG do psd_export.py xuất.

    python3 export_overlay_frames.py --psd Art/frame_rarity_net.psd --map Art/frame_rarity_net_map.json --out Resources/NetGacha
"""

import argparse
import json
import os
import sys

for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, "reconfigure"):
        _stream.reconfigure(encoding="utf-8", errors="replace")


def overlay(base, blend):
    if base < 0.5:
        return 2.0 * base * blend
    return 1.0 - 2.0 * (1.0 - base) * (1.0 - blend)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--psd", required=True)
    parser.add_argument("--map", required=True)
    parser.add_argument("--out", required=True)
    args = parser.parse_args()

    from psd_tools import PSDImage
    from PIL import Image

    psd = PSDImage.open(os.path.abspath(args.psd))
    with open(args.map, encoding="utf-8") as f:
        mapping = json.load(f)["layers"]

    # Khớp key "tên#N" như psd_export: đếm thứ tự xuất hiện của tên trần.
    seen = {}
    done = 0
    for layer in psd.descendants():
        name = layer.name
        seen[name] = seen.get(name, 0) + 1
        keys = (f"{name}#{seen[name]}", name)
        entry = next((mapping[k] for k in keys if k in mapping), None)
        if entry is None:
            continue

        effects = [e for e in layer.effects if e.__class__.__name__ == "ColorOverlay"]
        if len(effects) != 1 or effects[0].blend_mode != b"overlay":
            continue

        color = effects[0].color
        blend = (color[b"Rd  "] / 255.0, color[b"Grn "] / 255.0, color[b"Bl  "] / 255.0)
        opacity = effects[0].opacity / 100.0

        raw = layer.topil().convert("RGBA")
        # Bảng tra 256 mức cho từng kênh — đủ nhanh cho ảnh 216x176 × 16, không cần numpy.
        luts = []
        for c in range(3):
            lut = []
            for v in range(256):
                b = v / 255.0
                o = overlay(b, blend[c])
                lut.append(int(round(255 * (b + (o - b) * opacity))))
            luts.append(lut)

        r, g, bl, a = raw.split()
        out = Image.merge("RGBA", (r.point(luts[0]), g.point(luts[1]), bl.point(luts[2]), a))

        group, file_name = entry[0], entry[1]
        path = os.path.join(args.out, group, file_name + ".png")
        os.makedirs(os.path.dirname(path), exist_ok=True)
        out.save(path)
        done += 1
        print(f"  {group}/{file_name}.png  overlay #{int(blend[0]*255):02X}{int(blend[1]*255):02X}{int(blend[2]*255):02X}")

    print(f"\n{done} layer da ap Overlay -> {args.out}")


if __name__ == "__main__":
    main()
