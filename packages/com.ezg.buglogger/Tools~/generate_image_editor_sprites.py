#!/usr/bin/env python3
"""
Sinh sprite cho màn Chỉnh sửa ảnh của Bug Logger (icon công cụ + atlas nét vẽ/chữ số).

Chạy từ gốc package:  python3 "Tools~/generate_image_editor_sprites.py"
Cần Pillow. Icon trắng nền trong suốt 128 px (tint bằng màu trong Unity) như các icon sẵn có; atlas
`bl_annotation_atlas.png` gồm một ô trắng (tô hình đặc) + chữ số 0–9 của Be Vietnam Pro SemiBold.
Đổi bố cục atlas thì cập nhật hằng số trong Runtime/UI/Scripts/ImageEditor/BugAnnotationAtlas.cs theo số
liệu script in ra.
"""
import json
import math
import os

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SPRITES = os.path.join(ROOT, "Runtime", "UI", "Sprites")
FONT = os.path.join(ROOT, "Runtime", "UI", "Fonts", "BeVietnamPro-SemiBold.ttf")

SIZE = 128
SS = 4  # siêu lấy mẫu rồi thu nhỏ ⇒ viền mịn
STROKE = 10.5
WHITE = (255, 255, 255, 255)


def canvas():
    return Image.new("RGBA", (SIZE * SS, SIZE * SS), (255, 255, 255, 0))


def s(v):
    return v * SS


def save(img, name):
    img = img.resize((SIZE, SIZE), Image.LANCZOS)
    img.save(os.path.join(SPRITES, name + ".png"))


def dot(d, p, r, fill=WHITE):
    d.ellipse([s(p[0] - r), s(p[1] - r), s(p[0] + r), s(p[1] + r)], fill=fill)


def polyline(d, pts, width=STROKE, closed=False, fill=WHITE):
    """Nét đều đầu tròn, khớp tròn."""
    if closed:
        pts = list(pts) + [pts[0]]
    d.line([(s(x), s(y)) for x, y in pts], fill=fill, width=int(s(width)), joint="curve")
    for p in pts:
        dot(d, p, width / 2, fill)


def arc_points(cx, cy, r, a0, a1, n=48):
    return [(cx + r * math.cos(math.radians(a0 + (a1 - a0) * i / n)),
             cy + r * math.sin(math.radians(a0 + (a1 - a0) * i / n))) for i in range(n + 1)]


def rounded_rect_points(x0, y0, x1, y1, r, n=12):
    pts = []
    pts += arc_points(x1 - r, y0 + r, r, -90, 0, n)
    pts += arc_points(x1 - r, y1 - r, r, 0, 90, n)
    pts += arc_points(x0 + r, y1 - r, r, 90, 180, n)
    pts += arc_points(x0 + r, y0 + r, r, 180, 270, n)
    return pts


def icon_edit():
    img = canvas(); d = ImageDraw.Draw(img)
    # Bút chì chéo: thân + đầu nhọn + vạch ngăn đầu tẩy.
    a, b = (92, 22), (106, 36)
    tip = (24, 104)
    body = [(a[0], a[1]), (b[0], b[1]), (44, 98), (30, 84)]
    polyline(d, [body[0], body[1], body[2], tip, body[3]], closed=True)
    polyline(d, [(80, 34), (94, 48)])
    save(img, "ic_edit")


def icon_crop():
    img = canvas(); d = ImageDraw.Draw(img)
    polyline(d, [(38, 14), (38, 90), (114, 90)])
    polyline(d, [(14, 38), (90, 38), (90, 114)])
    save(img, "ic_crop")


def icon_rect():
    img = canvas(); d = ImageDraw.Draw(img)
    polyline(d, rounded_rect_points(22, 30, 106, 98, 10), closed=True)
    save(img, "ic_rect")


def icon_ellipse():
    img = canvas(); d = ImageDraw.Draw(img)
    polyline(d, arc_points(64, 64, 42, 0, 360, 96), closed=True)
    save(img, "ic_ellipse")


def icon_arrow():
    img = canvas(); d = ImageDraw.Draw(img)
    polyline(d, [(26, 102), (84, 44)])
    head = [(104, 24), (104, 70), (58, 24)]
    d.polygon([(s(x), s(y)) for x, y in head], fill=WHITE)
    polyline(d, head, width=8, closed=True)
    save(img, "ic_arrow")


def icon_pen():
    img = canvas(); d = ImageDraw.Draw(img)
    pts = []
    for i in range(0, 121):
        t = i / 120
        x = 18 + 92 * t
        y = 70 - 26 * math.sin(t * math.pi * 2.2) * (1 - 0.35 * t) + 14 * t
        pts.append((x, y))
    polyline(d, pts)
    save(img, "ic_pen")


def icon_marker():
    img = canvas(); d = ImageDraw.Draw(img)
    dot(d, (64, 64), 48)
    font = ImageFont.truetype(FONT, s(66))
    d.text((s(64), s(66)), "1", font=font, fill=(255, 255, 255, 0), anchor="mm")
    save(img, "ic_marker")


def icon_spotlight():
    img = canvas(); d = ImageDraw.Draw(img)
    outer = rounded_rect_points(12, 20, 116, 108, 14)
    d.polygon([(s(x), s(y)) for x, y in outer], fill=(255, 255, 255, 96))
    inner = rounded_rect_points(40, 42, 88, 86, 8)
    d.polygon([(s(x), s(y)) for x, y in inner], fill=(255, 255, 255, 0))
    polyline(d, inner, width=9, closed=True)
    save(img, "ic_spotlight")


def icon_undo(mirror=False):
    img = canvas(); d = ImageDraw.Draw(img)
    pts = [(36, 46), (78, 46)] + arc_points(78, 72, 26, -90, 90, 32)[1:] + [(50, 98)]
    head = [(54, 26), (34, 46), (54, 66)]
    if mirror:
        pts = [(128 - x, y) for x, y in pts]
        head = [(128 - x, y) for x, y in head]
    polyline(d, pts)
    polyline(d, head)
    save(img, "ic_redo" if mirror else "ic_undo")


def icon_trash():
    img = canvas(); d = ImageDraw.Draw(img)
    polyline(d, [(22, 34), (106, 34)])
    polyline(d, [(48, 34), (52, 20), (76, 20), (80, 34)])
    polyline(d, [(34, 36), (40, 106), (88, 106), (94, 36)])
    polyline(d, [(56, 54), (57, 88)])
    polyline(d, [(72, 54), (71, 88)])
    save(img, "ic_trash")


# --- Atlas: ô trắng + chữ số 0–9 ---------------------------------------------------------------
ATLAS_W, ATLAS_H = 512, 256
WHITE_RECT = (0, 0, 16, 16)          # x, y, w, h (toạ độ ảnh, gốc trên-trái)
CELL_W, CELL_H = 96, 112
CELL_X0 = 16
ROW_Y = (16, 136)
DIGIT_FONT_PX = 104


def atlas():
    img = Image.new("RGBA", (ATLAS_W, ATLAS_H), (255, 255, 255, 0))
    d = ImageDraw.Draw(img)
    x, y, w, h = WHITE_RECT
    d.rectangle([x, y, x + w - 1, y + h - 1], fill=WHITE)

    font = ImageFont.truetype(FONT, DIGIT_FONT_PX)
    ascent, _ = font.getmetrics()
    ink_top = None
    ink_bottom = None
    digits = []
    for i in range(10):
        ch = str(i)
        row, col = divmod(i, 5)
        cx = CELL_X0 + col * CELL_W
        cy = ROW_Y[row]
        advance = font.getlength(ch)
        # Đường cơ sở chung: căn theo ascent ⇒ mọi số cùng mép trên/dưới.
        baseline = cy + (CELL_H - ascent) / 2 + ascent * 0.86
        d.text((cx + CELL_W / 2, baseline), ch, font=font, fill=WHITE, anchor="ms")
        bbox = d.textbbox((cx + CELL_W / 2, baseline), ch, font=font, anchor="ms")
        top, bottom = bbox[1] - cy, bbox[3] - cy
        ink_top = top if ink_top is None else min(ink_top, top)
        ink_bottom = bottom if ink_bottom is None else max(ink_bottom, bottom)
        digits.append({"digit": i, "x": cx, "y": cy, "advance": round(advance, 2)})

    img.save(os.path.join(SPRITES, "bl_annotation_atlas.png"))
    return {
        "size": [ATLAS_W, ATLAS_H], "white": WHITE_RECT, "cell": [CELL_W, CELL_H],
        "inkTop": ink_top, "inkBottom": ink_bottom, "digits": digits,
    }


if __name__ == "__main__":
    icon_edit(); icon_crop(); icon_rect(); icon_ellipse(); icon_arrow(); icon_pen(); icon_marker()
    icon_spotlight(); icon_undo(); icon_undo(True); icon_trash()
    print(json.dumps(atlas(), indent=1))
