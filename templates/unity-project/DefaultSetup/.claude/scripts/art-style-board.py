#!/usr/bin/env python3
"""art-style-board.py — dựng "board" tham chiếu art cho .claude/docs/ArtStyle.md.

Vì sao có script này: agent chỉ `Read` được ảnh PNG/JPG, không đọc được PSD, và đoán style theo tên
file là nguồn lệch style số một. Board = contact sheet các sprite/PSD của kit chuẩn mà ArtStyle.md
khai, để mọi skill visual (refactor-ui, mockup-drafter, ui-visual-reviewer, gen-icon…) NHÌN thấy
house style trước khi quyết định.

Nguồn: khối fenced ```art-style-boards``` trong ArtStyle.md, mỗi dòng:

    <tên-board>: <path>[, <path>…]      # path tương đối repo root; thư mục = quét đệ quy *.png/*.psd

Output: .claude/docs/ArtStyle/<tên-board>.png (đè bản cũ). Chạy lại mỗi khi kit trong khối đó đổi.

Usage:
    python3 .claude/scripts/art-style-board.py            # dựng mọi board
    python3 .claude/scripts/art-style-board.py --list     # chỉ in board + số file nguồn, không ghi
    python3 .claude/scripts/art-style-board.py kit-common # dựng 1 board

Cần Pillow. PSD cần thêm `psd-tools` (pip install psd-tools); thiếu thì bỏ qua PSD và cảnh báo.
"""
import os
import re
import sys

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DOC = os.path.join(REPO_ROOT, ".claude", "docs", "ArtStyle.md")
OUT_DIR = os.path.join(REPO_ROOT, ".claude", "docs", "ArtStyle")
EXTS = (".png", ".psd", ".jpg", ".jpeg")
CELL = 200          # px mỗi ô thumbnail
LABEL_H = 22        # px dải tên file dưới ô
COLS = 6
MAX_ITEMS = 60      # board quá dài thì agent đọc không nổi — chia board nhỏ hơn
BG = (58, 58, 64, 255)
FG = (235, 235, 235, 255)


def parse_boards(text):
    m = re.search(r"```art-style-boards\s*\n(.*?)```", text, re.S)
    if not m:
        return {}
    boards = {}
    for line in m.group(1).splitlines():
        line = line.split("#", 1)[0].strip()
        if not line or ":" not in line:
            continue
        name, paths = line.split(":", 1)
        boards[name.strip()] = [p.strip() for p in paths.split(",") if p.strip()]
    return boards


def collect(paths):
    files = []
    for rel in paths:
        p = os.path.join(REPO_ROOT, rel)
        if os.path.isdir(p):
            for root, _, names in os.walk(p):
                files += [os.path.join(root, n) for n in names if n.lower().endswith(EXTS)]
        elif os.path.isfile(p) and p.lower().endswith(EXTS):
            files.append(p)
        else:
            print(f"  WARN không thấy: {rel}", file=sys.stderr)
    return sorted(set(files))


def load(path, psd_image):
    from PIL import Image
    if path.lower().endswith(".psd"):
        if psd_image is None:
            return None
        return psd_image.open(path).composite().convert("RGBA")
    return Image.open(path).convert("RGBA")


def render(name, files, psd_image):
    from PIL import Image, ImageDraw
    if len(files) > MAX_ITEMS:
        print(f"  WARN {name}: {len(files)} file > {MAX_ITEMS}, chỉ lấy {MAX_ITEMS} đầu — tách board", file=sys.stderr)
        files = files[:MAX_ITEMS]
    rows = max(1, (len(files) + COLS - 1) // COLS)
    sheet = Image.new("RGBA", (COLS * CELL, rows * (CELL + LABEL_H)), BG)
    draw = ImageDraw.Draw(sheet)
    placed = 0
    for path in files:
        try:
            im = load(path, psd_image)
        except Exception as e:  # file hỏng không được làm hỏng cả board
            print(f"  WARN đọc lỗi {os.path.relpath(path, REPO_ROOT)}: {e}", file=sys.stderr)
            continue
        if im is None:
            continue
        w, h = im.size
        scale = min((CELL - 10) / w, (CELL - 10) / h, 1.0)
        thumb = im.resize((max(1, int(w * scale)), max(1, int(h * scale))), Image.LANCZOS)
        x, y = (placed % COLS) * CELL, (placed // COLS) * (CELL + LABEL_H)
        sheet.alpha_composite(thumb, (x + (CELL - thumb.width) // 2, y + (CELL - thumb.height) // 2))
        label = f"{os.path.splitext(os.path.basename(path))[0][:24]} {w}x{h}"
        draw.text((x + 4, y + CELL + 4), label, fill=FG)
        placed += 1
    os.makedirs(OUT_DIR, exist_ok=True)
    out = os.path.join(OUT_DIR, f"{name}.png")
    sheet.convert("RGB").save(out, optimize=True)
    print(f"  OK  {os.path.relpath(out, REPO_ROOT)} ({placed} ô)")


def main(argv):
    if not os.path.isfile(DOC):
        print(f"Không có {os.path.relpath(DOC, REPO_ROOT)} — copy từ ArtStyle.template.md rồi điền trước.", file=sys.stderr)
        return 1
    boards = parse_boards(open(DOC, encoding="utf-8").read())
    if not boards:
        print("ArtStyle.md chưa có khối ```art-style-boards``` — không có gì để dựng.")
        return 0
    only = [a for a in argv if not a.startswith("--")]
    if "--list" in argv:
        for name, paths in boards.items():
            print(f"{name}: {len(collect(paths))} file")
        return 0
    try:
        import PIL  # noqa: F401
    except ImportError:
        print("Cần Pillow: pip install pillow", file=sys.stderr)
        return 1
    try:
        from psd_tools import PSDImage
    except ImportError:
        PSDImage = None
        print("  WARN thiếu psd-tools — bỏ qua file .psd (pip install psd-tools)", file=sys.stderr)
    for name, paths in boards.items():
        if only and name not in only:
            continue
        render(name, collect(paths), PSDImage)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
