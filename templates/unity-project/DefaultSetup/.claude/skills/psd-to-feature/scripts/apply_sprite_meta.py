#!/usr/bin/env python3
"""
Set import settings cho đám PNG vừa xuất từ PSD: pixels-per-unit, pivot, 9-slice border, và
nhóm field quyết định ĐỘ NÉT của mép sprite (mipmap / wrap / mesh type / format từng platform).

Sửa THẲNG file `.png.meta` (YAML) nên chạy được khi không mở Unity. Chỉ đụng đúng những dòng
liệt kê trong `FIELDS` (+ block platform khi có `platform`), guid và mọi field khác giữ nguyên
— đổi guid là mọi prefab đang trỏ tới sprite đó thành ref chết.

    python3 apply_sprite_meta.py --dir Visuals/ShopPsd --config sprite_meta.json [--dry-run]

`--config`:

    {
      "groups": {
        "ui":   { "ppu": 100, "platform": { "Android": [48, 100], "iOS": [48, 100] } },
        "prop": { "ppu": 139 }
      },
      "borders": {
        "ui/card_upgrade_bg": [40, 40, 40, 40],
        "ui/bar_frame":       [60, 40, 60, 40]
      },
      "pivots": {
        "prop/mannequin": [0.5, 0]
      },
      "platforms": {
        "ui/btn_close": { "Android": [48, 100], "iOS": [48, 100] }
      }
    }

Mọi group mặc định `textureType: 8` (Sprite 2D and UI) + `spriteMode: 1` (Single) — PNG mới
import theo default của project thường ra Texture, và `LoadAssetAtPath<Sprite>` trả null.
Ghi đè trong group khi cần: `{ "ppu": 100, "spriteMode": 2 }`.

- `ppu` — prop trong scene phải dùng ĐÚNG px/unit suy ra từ camera (xem SKILL.md), không thì
  sprite to/nhỏ sai tỉ lệ so với mockup. UI trong Canvas thì 100 (mặc định) là được, vì
  RectTransform tính theo pixel chứ không theo unit.
- `borders` — chỉ đặt cho sprite bị KÉO GIÃN (nền thẻ, pill, thanh bar, nền tab). Border sai
  là góc bo bị méo khi thẻ giãn ra trên máy rộng. Sprite icon để 0.
- `pivots` — mặc định giữa (0.5, 0.5). Nhân vật/vật đứng trên sàn thì để chân (0.5, 0) vì code
  đặt `transform.position` = chỗ chân chạm đất.

## Vì sao phải ép cả nhóm field "độ nét"

Unity import PNG mới theo default của nó, KHÔNG theo convention của project. Ba default đầu
tiên sai cho sprite UI, và cả ba đều không log gì — chỉ thấy mép nút "nham nhở":

- `mipmaps` (mặc định 0 = tắt) — default của Unity là BẬT. UI gần như luôn bị canvas thu nhỏ
  (CanvasScaler Expand: 1080x2400 vẽ trên game view lùn → scale ~0.7), lúc đó Unity nhảy sang
  mip 1 và trộn với mip 0, viền tối cứng của nút bệt ra như bị nhoè. Art UI của project là
  149 file tắt / 8 file bật — bộ `Buttons/9Slice/Bt_*` bị thay ra cũng tắt.
- `wrap` (mặc định 1 = Clamp) — default của Unity là Repeat. Sprite nào có nét vẽ chạm sát mép
  ảnh (art xuất theo bbox layer nên rất hay) thì texel biên nội suy sang mép ĐỐI DIỆN, ra một
  vệt 1px sai màu quanh viền.
- `meshType` (mặc định: FullRect khi có border, Tight khi không) — Sliced/Tiled cần FullRect.
- `platform` — `{"Android": [format, quality]}`, chỉ ghi khi có trong config. `48` =
  `ASTC_4x4`, `50` = `ASTC_6x6` (xem `TextureImporterFormat`). Không override thì Unity tự
  chọn mức nén thấp hơn: block compression 4x4 ăn vào đúng chỗ tương phản cao (viền tối trên
  nền trong suốt) nên nút viền đậm rỗ hẳn trên máy thật. Đặt cho art mép cứng, KHÔNG đặt cho
  sprite khổ lớn (ASTC 4x4 = 8bpp, ảnh 1000x1600 tốn 1.5MB VRAM). Vì đây là lựa chọn theo
  TỪNG ẢNH chứ không theo group, `platforms` (top-level, key `group/tên`) ghi đè `platform`
  của group — group để trống rồi bật cho đúng mấy sprite mép cứng là cách dùng thường gặp.

`.meta` chưa có (PNG mới, Unity chưa import) thì file đó bị bỏ qua — mở Unity cho nó import
một lượt rồi chạy lại.
"""

import argparse
import json
import os
import re
import sys

# Console Windows mặc định cp1252 mà log của script là tiếng Việt → `print` ném UnicodeEncodeError
# giữa lượt chạy (sau khi đã sửa xong một phần .meta). Xem chú thích cùng chỗ trong psd_export.py.
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, "reconfigure"):
        _stream.reconfigure(encoding="utf-8", errors="replace")

FIELDS = ("textureType", "spriteMode", "alphaIsTransparency",
          "spritePixelsToUnits", "alignment", "spritePivot", "spriteBorder",
          "enableMipMap", "wrapU", "wrapV", "spriteMeshType")

# Một phần tử của list `platformSettings`: mở bằng "  - serializedVersion: 4" rồi các dòng con
# thụt 4 space. Phải khoanh đúng block mới sửa được format của RIÊNG Android/iOS — sub() thẳng
# trên cả file sẽ đụng luôn `DefaultTexturePlatform` (mức nén dùng trong Editor, do project
# quyết định) và `Standalone`.
PLATFORM_BLOCK = re.compile(r"^  - serializedVersion: \d+\n(?:    .*\n)+", re.MULTILINE)


def patch_platform(text, platforms):
    """Ép textureFormat/compressionQuality cho từng buildTarget trong `platforms`.

    `platforms` = {"Android": [format, quality]}. `overridden: 1` là BẮT BUỘC — để 0 thì Unity
    bỏ qua textureFormat vừa ghi và vẫn tự chọn format, sửa xong mà không đổi gì.
    """
    changed = []

    def sub_block(match):
        block = match.group(0)
        target = re.search(r"^    buildTarget: (.+)$", block, re.MULTILINE)
        if target is None or target.group(1).strip() not in platforms:
            return block
        name = target.group(1).strip()
        fmt, quality = platforms[name]
        for field, value in (("textureFormat", fmt),
                             ("compressionQuality", quality),
                             ("overridden", 1)):
            pattern = re.compile(r"^(    %s: )(.*)$" % field, re.MULTILINE)
            current = pattern.search(block)
            if current is None:
                continue
            if current.group(2).strip() != str(value):
                changed.append(f"{name}.{field}={value}")
            block = pattern.sub(lambda m: m.group(1) + str(value), block, count=1)
        return block

    return PLATFORM_BLOCK.sub(sub_block, text), changed


def patch(text, settings, ppu, pivot, border):
    """Ghi đè các field sprite trong YAML meta. Trả (text mới, danh sách field đã đổi)."""
    values = {
        # PNG mới xuất ra được Unity import theo default của project — ở đây là Texture (0),
        # KHÔNG phải Sprite. `LoadAssetAtPath<Sprite>` trả null, importer chạy sạch mà không
        # gán được ảnh nào. 8 = Sprite (2D and UI), 1 = Single (2 = Multiple mà không có slice
        # nào thì cũng không ra Sprite).
        "textureType": str(settings.get("textureType", 8)),
        "spriteMode": str(settings.get("spriteMode", 1)),
        "alphaIsTransparency": str(settings.get("alphaIsTransparency", 1)),
        "spritePixelsToUnits": str(ppu),
        # alignment 0 = Center, 9 = Custom (đọc spritePivot). Pivot khác giữa thì BẮT BUỘC là 9,
        # không thì Unity bỏ qua spritePivot và ảnh vẫn xoay quanh tâm.
        "alignment": "0" if tuple(pivot) == (0.5, 0.5) else "9",
        "spritePivot": "{x: %g, y: %g}" % (pivot[0], pivot[1]),
        "spriteBorder": "{x: %g, y: %g, z: %g, w: %g}" % tuple(border),
        # Xem phần "Vì sao phải ép cả nhóm field độ nét" ở đầu file: cả ba default của Unity
        # đều sai cho sprite UI và đều im lặng.
        "enableMipMap": str(settings.get("mipmaps", 0)),
        "wrapU": str(settings.get("wrap", 1)),
        "wrapV": str(settings.get("wrap", 1)),
        # Sprite có border LUÔN phải FullRect (0): Sliced/Tiled dựng lưới 9 ô nên không dùng
        # được mesh Tight. Không border thì để Tight (1) như phần còn lại của project — bớt
        # overdraw vùng trong suốt.
        "spriteMeshType": str(settings.get("meshType", 0 if any(border) else 1)),
    }
    changed = []
    for field in FIELDS:
        pattern = re.compile(r"^(\s*%s:\s*)(.*)$" % field, re.MULTILINE)
        match = pattern.search(text)
        if match is None:
            continue
        if match.group(2).strip() != values[field]:
            changed.append(f"{field}={values[field]}")
        text = pattern.sub(lambda m: m.group(1) + values[field], text, count=1)
    return text, changed


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--dir", required=True, help="thư mục PNG đã xuất (có các thư mục con group)")
    parser.add_argument("--config", required=True, help="file JSON groups/borders/pivots")
    parser.add_argument("--dry-run", action="store_true", help="chỉ in ra, không ghi file")
    args = parser.parse_args()

    # encoding="utf-8": file config có chú thích tiếng Việt, mà `open()` trên Windows mặc định
    # cp1252 → UnicodeDecodeError trước khi đọc được cấu hình nào.
    config = json.load(open(args.config, encoding="utf-8"))
    groups = config.get("groups", {})
    borders = config.get("borders", {})
    pivots = config.get("pivots", {})
    platforms_by_key = config.get("platforms", {})

    touched = skipped = 0
    for root, _, files in os.walk(args.dir):
        for name in sorted(files):
            if not name.endswith(".png"):
                continue
            group = os.path.basename(root)
            key = f"{group}/{name[:-4]}"
            settings = groups.get(group)
            if settings is None:
                print(f"  bỏ qua {key} — group '{group}' không có trong config")
                skipped += 1
                continue

            meta_path = os.path.join(root, name + ".meta")
            if not os.path.exists(meta_path):
                print(f"  bỏ qua {key} — chưa có .meta (mở Unity cho import rồi chạy lại)")
                skipped += 1
                continue

            with open(meta_path, encoding="utf-8") as handle:
                text = handle.read()
            new_text, changed = patch(
                text, settings,
                settings.get("ppu", 100),
                pivots.get(key, settings.get("pivot", [0.5, 0.5])),
                borders.get(key, settings.get("border", [0, 0, 0, 0])))

            platforms = platforms_by_key.get(key, settings.get("platform"))
            if platforms:
                new_text, platform_changed = patch_platform(new_text, platforms)
                changed += platform_changed

            if not changed:
                continue
            touched += 1
            print(f"  {key}: {', '.join(changed)}")
            if not args.dry_run:
                with open(meta_path, "w", encoding="utf-8", newline="\n") as handle:
                    handle.write(new_text)

    verb = "sẽ sửa" if args.dry_run else "đã sửa"
    print(f"\n{verb} {touched} meta, bỏ qua {skipped}.")
    if touched and not args.dry_run:
        print("Quay lại Unity cho nó reimport (focus cửa sổ Editor) trước khi chạy importer.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
