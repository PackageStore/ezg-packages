#!/usr/bin/env python3
"""
Xuất layer của một file PSD mockup ra PNG + manifest toạ độ cho importer Unity.

Thay cho script .jsx "Export Layers to Files (Fast)" (cần Photoshop, máy mac/CI không có).
Cùng đầu ra — một PNG mỗi layer — nhưng KÈM bbox của layer trong PSD, thứ bản .jsx không
xuất, mà importer cần để đặt đúng vị trí trong prefab/scene thay vì kéo tay từng ảnh.

Cài 1 lần:  python3 -m pip install 'psd-tools[composite]'

    # 1. soi cây layer để viết map
    python3 psd_export.py --psd Art/Shop.psd --tree
    # 2. soi text layer (font / size / baseline / màu) để đặt số trong importer
    python3 psd_export.py --psd Art/Shop.psd --text
    # 3. xuất
    python3 psd_export.py --psd Art/Shop.psd --map shop_map.json --out Visuals/ShopPsd

`--map` là file JSON:

    {
      "manifest": "shop_layout.json",
      "origin":  [x, y, w, h],
      "layers":  { "<tên layer trong PSD>": ["<group>", "<tên file>", "<ghi chú, tuỳ chọn>"] },
      "anchors": { "<tên layer>": "<tên anchor>" },
      "crops":   { "<tên layer>": [x, y, w, h] },
      "show":    ["<tên layer đang tắt mắt nhưng vẫn muốn xuất>"],
      "noclip":  ["<tên layer NỀN đang bị layer clipping mask đè lên>"]
    }

- `origin`  — rect của ARTBOARD trong file PSD (`[x, y, w, h]`, đọc từ dòng artboard của `--tree`).
  Mọi toạ độ trong manifest được trừ đi `x,y` và `width/height` của manifest ghi `w,h` — tức
  importer làm việc với hệ toạ độ của artboard, y như PSD một artboard. Cần khi hoạ sĩ để nhiều
  artboard trong một file (bản mockup + ảnh chụp game làm tham chiếu): không có nó thì mọi rect
  lệch đúng `x` px và cả màn trôi ra ngoài canvas. Bỏ trống = dùng khổ PSD như cũ.
- `layers`  — layer được xuất PNG vào `<out>/<group>/<tên file>.png` + ghi bbox vào manifest.
  `group` là thư mục con tuỳ ý, quy ước: `ui` (đi vào Canvas) và `prop` (SpriteRenderer).
- `anchors` — CHỈ lấy toạ độ vào manifest, KHÔNG xuất PNG. Dùng cho layer chỉ chỗ
  (nhân vật, điểm spawn) mà trong game là prefab có sẵn.
- `crops` — cắt layer theo rect (toạ độ PSD) trước khi lưu, và manifest ghi rect đã cắt.
  Cần cho art nền: hoạ sĩ hay vẽ tràn ra ngoài artboard cả nghìn px, xuất nguyên bản thì
  texture vượt `maxTextureSize` và bị Unity ép nhỏ lại, mất luôn độ nét theo chiều dọc.
- Key dạng `"Group/Child"` khi có hai layer trùng tên ở hai nhánh khác nhau.
- `show` — ép xuất layer đang TẮT MẮT. Mặc định layer tắt mắt được giữ nguyên PNG cũ (hoạ sĩ
  tắt = "không nằm trong thiết kế"), nhưng có một ca ngược lại rất hay gặp: mockup chỉ bày ĐƯỢC
  một trạng thái, nên trạng thái kia bị tắt mắt dù vẫn là art cần dùng (icon toggle ON/OFF,
  nút bật/tắt). Liệt kê ở đây là opt-in từng layer, không nới lỏng luật chung.
- `noclip` — layer NỀN của một chuỗi clipping mask. `composite()` của một layer nền LUÔN kéo
  theo cả đám layer clipping đè lên nó (đúng như Photoshop vẽ), nên nền panel xuất ra là đã
  bị dính sẵn mấy cái thẻ/icon nằm trên — đắp vào Unity là ảnh đôi. Liệt kê ở đây thì script
  composite layer đó MỘT MÌNH, bỏ hết layer clipping. Đây là chiều NGƯỢC LẠI của cảnh báo
  "layer là CLIPPING MASK" ở cuối output: cái kia là layer ĐÈ xuất ra rỗng, cái này là layer
  BỊ ĐÈ xuất ra thừa.
- Key dạng `"<tên>#N"` (hoặc `"Group/Child#N"`) khi hai layer TRÙNG TÊN trong CÙNG một nhánh —
  `N` là thứ tự xuất hiện (1-based) tính từ trên xuống theo cây layer. Hoạ sĩ copy một nút rồi
  quên đổi tên là ra ca này (Setting.psd có hai `Bt_Blue`: một cái trong popup, một cái ở dải
  nút mẫu). Không có `#N` thì script chặn luôn thay vì đoán bừa. `#N` được ưu tiên hơn key trần,
  nên map cũ không dùng `#` thì hành vi giữ nguyên.
- `smartObject` (hoặc cờ `--smart-object`) — hoạ sĩ gửi "raw pack": một file gộp nhiều màn, mỗi
  màn là một SMART OBJECT bọc nguyên artboard. Nhìn từ ngoài chỉ thấy một layer bẹt duy nhất
  cho cả màn, không xuất được gì; layer thật nằm trong file .psb NHÚNG bên trong nó. Khai tên
  smart object ở đây thì script mở file nhúng đó rồi làm việc y như một PSD bình thường (kể cả
  `--tree`/`--text`). Nhận cùng dạng key với `layers` (`"tên#N"` khi trùng tên — raw pack rất
  hay có hai artboard cùng tên "Artboard 2").

Layer không có trong map thì bỏ qua và được liệt kê ở cuối — hoạ sĩ thêm art mới là thấy
ngay chứ không im lặng rơi mất.
"""

import argparse
import json
import os
import sys
import tempfile

# Console Windows mặc định cp1252 mà mọi dòng log của script này là tiếng Việt: thiếu dòng dưới
# thì `print` ném UnicodeEncodeError GIỮA lượt chạy — PNG đã ghi ra đĩa xong, manifest xong, rồi
# chết đúng ở đoạn liệt kê "layer chưa map", nên nhìn output tưởng tool hỏng chứ không biết là đã
# xuất đủ. `errors="replace"` để terminal nào không hiện được dấu thì hiện "?" chứ đừng ném.
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, "reconfigure"):
        _stream.reconfigure(encoding="utf-8", errors="replace")


def load_psd(path):
    try:
        from psd_tools import PSDImage
    except ImportError:
        sys.exit("Thiếu psd-tools. Cài: python3 -m pip install 'psd-tools[composite]'")
    return PSDImage.open(os.path.abspath(path))


def walk(node, prefix=""):
    """Duyệt cây layer, trả (path, layer). Group cũng được trả về (có thể map cả group)."""
    for layer in node:
        path = f"{prefix}{layer.name}"
        yield path, layer
        if layer.is_group():
            yield from walk(layer, path + "/")


def walk_indexed(node):
    """
    Như `walk` nhưng kèm thứ tự xuất hiện (1-based) của tên trần và của path đầy đủ.

    Cần cho key dạng `"tên#N"`: hai layer trùng tên trong cùng một nhánh thì path cũng trùng,
    không còn cách nào phân biệt ngoài thứ tự trên cây.
    """
    name_seen, path_seen = {}, {}
    for path, layer in walk(node):
        name = path.rsplit("/", 1)[-1]
        name_seen[name] = name_seen.get(name, 0) + 1
        path_seen[path] = path_seen.get(path, 0) + 1
        yield path, layer, name_seen[name], path_seen[path]


def key_of(path, mapping, name_occurrence=None, path_occurrence=None):
    """
    Layer khớp map theo: tên#N, path#N, tên trần, rồi path đầy đủ.

    Dạng có `#N` đứng trước để map cũ (không dùng `#`) giữ nguyên hành vi, còn khi hoạ sĩ để
    trùng tên thì key `#N` luôn thắng key trần chứ không phụ thuộc thứ tự dict.
    """
    name = path.rsplit("/", 1)[-1]
    candidates = []
    if name_occurrence is not None:
        candidates.append(f"{name}#{name_occurrence}")
    if path_occurrence is not None:
        candidates.append(f"{path}#{path_occurrence}")
    candidates += [name, path]
    for candidate in candidates:
        if candidate in mapping:
            return candidate
    return None


def is_blank(image):
    """Ảnh trong suốt hoàn toàn (alpha max = 0). Ảnh không có kênh alpha thì không bao giờ rỗng."""
    if "A" not in image.getbands():
        return False
    return image.getchannel("A").getextrema()[1] == 0


def open_smart_object(psd, key):
    """
    Mở file .psb NHÚNG bên trong một smart object, trả về nó như một PSD bình thường.

    Dùng cho "raw pack": hoạ sĩ gộp nhiều màn vào một file, mỗi màn bọc trong một smart object.
    Từ ngoài mỗi màn chỉ là MỘT layer bẹt — `--tree` in ra đúng một dòng cho cả màn và không
    xuất được layer nào; toàn bộ cây layer thật nằm trong file nhúng.

    Chỉ nhận smart object dạng NHÚNG (`kind == "data"`). Bản LIÊN KẾT (hoạ sĩ để file rời cạnh
    PSD rồi link vào) không mang dữ liệu theo, nên báo lỗi luôn thay vì xuất ra một màn trống.
    """
    from psd_tools import PSDImage

    matches, smart_objects = [], []
    for path, layer, name_occurrence, path_occurrence in walk_indexed(psd):
        if layer.kind != "smartobject":
            continue
        name = path.rsplit("/", 1)[-1]
        smart_objects.append(f"{name}#{name_occurrence}")
        if key in (f"{name}#{name_occurrence}", f"{path}#{path_occurrence}", name, path):
            matches.append((path, layer))

    if not matches:
        sys.exit(f"Không thấy smart object {key!r} trong PSD.\n"
                 f"Các smart object có trong file: {smart_objects}")
    if len(matches) > 1:
        sys.exit(f"{key!r} khớp {len(matches)} smart object — thêm '#N' để chỉ đúng cái nào "
                 f"(chạy --tree để xem N): {[path for path, _ in matches]}")

    path, layer = matches[0]
    embedded = layer.smart_object
    if embedded is None or embedded.kind != "data" or not embedded.data:
        sys.exit(f"Smart object {path!r} không phải bản NHÚNG (kind="
                 f"{getattr(embedded, 'kind', None)!r}) — không có dữ liệu layer bên trong. "
                 "Xin hoạ sĩ file gốc của màn này.")

    # psd-tools chỉ mở được từ đường dẫn, mà .psb nhúng nằm trong RAM. Ghi ra file tạm và GIỮ
    # nguyên ở đó: PSDImage đọc lazy (composite từng layer lúc xuất), xoá file ngay là mọi lệnh
    # sau đó ném IOError. File nằm trong thư mục tạm của HĐH nên tự dọn, không rác vào repo.
    handle = tempfile.NamedTemporaryFile(prefix="psd_smart_", suffix=".psb", delete=False)
    handle.write(embedded.data)
    handle.close()
    print(f"[smart object] {path!r} -> {embedded.filename} ({len(embedded.data) / 1e6:.1f} MB nhúng)")
    return PSDImage.open(handle.name)


def cmd_tree(psd, mapping, anchors):
    dupes = {}
    for path, _layer, name_occurrence, _path_occurrence in walk_indexed(psd):
        dupes.setdefault(path.rsplit("/", 1)[-1], []).append(name_occurrence)

    for path, layer, name_occurrence, path_occurrence in walk_indexed(psd):
        mapped = mapping.get(key_of(path, mapping, name_occurrence, path_occurrence) or "")
        anchor = anchors.get(key_of(path, anchors, name_occurrence, path_occurrence) or "")
        tag = f"-> {mapped[0]}/{mapped[1]}" if mapped else (f"-> anchor {anchor}" if anchor else "")
        vis = "" if layer.visible else " [TẮT MẮT]"
        # Trùng tên thì in sẵn key `#N` phải dùng, khỏi phải tự đếm dòng.
        dupe = f" [TRÙNG TÊN — key: \"{path.rsplit('/', 1)[-1]}#{name_occurrence}\"]" \
            if len(dupes[path.rsplit("/", 1)[-1]]) > 1 else ""
        depth = path.count("/")
        print(f"{'  ' * depth}[{layer.kind}] {path!r} bbox={layer.bbox}{vis}{dupe} {tag}")


def cmd_text(psd):
    """
    Dump text layer: font, size THẬT, baseline, màu.

    Photoshop lưu FontSize theo hệ toạ độ CHƯA nhân transform của layer, nên size hiện trong
    panel Character có thể khác size thật trên artboard. Size thật = FontSize * yy (scale dọc
    trong transform). Baseline = ty — đây là con số nên dùng để đặt chữ trong Unity
    (TextAlignmentOptions.Baseline), vì khi autosize co chữ lại thì baseline KHÔNG trôi,
    còn canh giữa khối chữ thì trôi.
    """
    found = 0
    for path, layer in walk(psd):
        if layer.kind != "type":
            continue
        found += 1
        transform = getattr(layer, "transform", (1, 0, 0, 1, 0, 0))
        scale_y = transform[3] if len(transform) > 3 else 1.0
        tx, ty = (transform[4], transform[5]) if len(transform) > 5 else (0, 0)
        text = (layer.text or "").replace("\r", " / ").strip()

        fonts, sizes, colors = [], [], []
        try:
            engine = layer.engine_dict
            font_set = layer.resource_dict["FontSet"]
            for run in engine["StyleRun"]["RunArray"]:
                data = run["StyleSheet"]["StyleSheetData"]
                if "Font" in data:
                    fonts.append(str(font_set[int(data["Font"])]["Name"]).strip("'"))
                if "FontSize" in data:
                    sizes.append(float(data["FontSize"]))
                fill = data.get("FillColor")
                if fill is not None:
                    a, r, g, b = [float(v) for v in fill["Values"]]
                    colors.append("#%02X%02X%02X" % (round(r * 255), round(g * 255), round(b * 255)))
        except Exception as error:  # PSD cũ / text rasterize dở có thể thiếu engine data
            print(f"  (không đọc được engine data: {error})")

        print(f"{path!r}")
        print(f"    text     {text!r}")
        print(f"    bbox     {layer.bbox}  baseline ty={ty:.1f} tx={tx:.1f}")
        for i, size in enumerate(sizes):
            font = fonts[i] if i < len(fonts) else "?"
            color = colors[i] if i < len(colors) else "?"
            print(f"    run {i}    {font}  {size:.3f}pt x{scale_y:.3f} = {size * scale_y:.1f}px  {color}")
    if not found:
        print("PSD không có text layer nào (hoạ sĩ rasterize hết rồi) — "
              "phải đo size/baseline từ pixel, sai số ~1px vì antialias.")


def cmd_export(psd, psd_path, mapping, anchors, crops, out_dir, manifest_name, show=(), origin=None,
               noclip=()):
    # Map theo TÊN nên tên trùng là hỏng: hai layer cùng tên thì cái sau ghi đè cái trước,
    # ảnh ra vẫn "đúng tên" mà sai nội dung. Chặn ngay thay vì để lọt.
    hits, show_hits, noclip_hits = {}, set(), set()
    for path, layer, name_occurrence, path_occurrence in walk_indexed(psd):
        key = (key_of(path, mapping, name_occurrence, path_occurrence)
               or key_of(path, anchors, name_occurrence, path_occurrence))
        if key:
            hits.setdefault(key, []).append(f"{path} (#{name_occurrence})")
        show_key = key_of(path, show, name_occurrence, path_occurrence)
        if show_key:
            show_hits.add(show_key)
        noclip_key = key_of(path, noclip, name_occurrence, path_occurrence)
        if noclip_key:
            noclip_hits.add(noclip_key)
    clashes = {key: paths for key, paths in hits.items() if len(paths) > 1}
    if clashes:
        sys.exit("Tên layer trùng nhau trong PSD, không biết lấy cái nào (đổi key trong map sang "
                 "dạng \"Group/Child\", hoặc \"tên#N\" nếu trùng trong cùng một nhánh — "
                 "chạy --tree để xem N):\n" +
                 "\n".join(f"  {key!r} khớp {paths}" for key, paths in clashes.items()))

    os.makedirs(out_dir, exist_ok=True)
    # Toạ độ trong manifest quy về gốc ARTBOARD, không phải gốc file PSD — xem `origin` ở đầu file.
    origin_x, origin_y = (origin[0], origin[1]) if origin else (0, 0)
    manifest = {"psd": os.path.basename(psd_path),
                "width": origin[2] if origin else psd.width,
                "height": origin[3] if origin else psd.height, "layers": []}
    unmapped, hidden, empty, shown, clipped = [], [], [], [], []
    skip_prefix = None  # group đã được map -> composite cả cụm, không chui vào trong

    for path, layer, name_occurrence, path_occurrence in walk_indexed(psd):
        if skip_prefix and path.startswith(skip_prefix):
            continue
        skip_prefix = None

        anchor_key = key_of(path, anchors, name_occurrence, path_occurrence)
        layer_key = key_of(path, mapping, name_occurrence, path_occurrence)
        if not anchor_key and not layer_key:
            if not layer.is_group():
                unmapped.append((path, layer.bbox))
            continue
        if layer.is_group():
            skip_prefix = path + "/"

        crop = crops.get(key_of(path, crops, name_occurrence, path_occurrence) or "")
        left, top, right, bottom = layer.bbox
        if crop:
            left, top = crop[0], crop[1]
            right, bottom = left + crop[2], top + crop[3]
        entry = {"name": anchors[anchor_key] if anchor_key else mapping[layer_key][1],
                 "group": "anchor" if anchor_key else mapping[layer_key][0],
                 "psdName": path, "x": left - origin_x, "y": top - origin_y,
                 "w": right - left, "h": bottom - top}

        if anchor_key:
            manifest["layers"].append(entry)
            print(f"  [anchor] {entry['name']}  {entry['w']}x{entry['h']} @{entry['x']},{entry['y']}")
            continue

        # Layer tắt mắt -> composite() trả ảnh TRONG SUỐT đúng kích thước chứ không trả None,
        # ghi đè lên là mất sprite cũ mà không ai biết. Vẫn phải ghi vào manifest: thiếu entry
        # thì Sprite() bên Unity trả null còn Rect() rơi về mặc định, prop mất ảnh + nhảy chỗ.
        # Trừ khi layer được liệt kê trong `show` — xem chú thích đầu file.
        forced = key_of(path, show, name_occurrence, path_occurrence) is not None
        if not layer.visible and not forced:
            hidden.append((path, f"{entry['group']}/{entry['name']}"))
            manifest["layers"].append(entry)
            continue

        # Layer nền của chuỗi clipping mask: composite() kéo theo cả đám layer đè lên nó, nên
        # phải lọc bỏ layer clipping ra. Xem chú thích `noclip` đầu file.
        drop_clipping = key_of(path, noclip, name_occurrence, path_occurrence) is not None
        layer_filter = (lambda item: not getattr(item, "clipping", False)) if drop_clipping else None

        # `force=True` cần cho layer tắt mắt: không có nó composite() trả ảnh trong suốt.
        was_visible = layer.visible
        layer.visible = True
        try:
            image = layer.composite(viewport=(left, top, right, bottom), force=True,
                                    layer_filter=layer_filter) if crop \
                else layer.composite(force=True, layer_filter=layer_filter)
        finally:
            layer.visible = was_visible
        if forced:
            shown.append((path, f"{entry['group']}/{entry['name']}"))
        if image is None:
            empty.append(path)
            continue

        # LAYER CLIPPING MASK: trong Photoshop nó chỉ hiện ở phần ĐÈ LÊN layer nền ngay dưới.
        # Composite một mình thì không có nền để cắt -> ra ảnh TRONG SUỐT HOÀN TOÀN, mà
        # `composite()` vẫn trả về ảnh đúng kích thước chứ không trả None. Ghi đè là mất sprite
        # cũ, prefab hiện ô rỗng, console không kêu một tiếng. Chặn ghi + báo để còn xử lý tay
        # (thường là crop ra từ bản BẸT hoạ sĩ đã gộp sẵn, xem `crops`).
        if getattr(layer, "clipping", False) and is_blank(image):
            clipped.append((path, f"{entry['group']}/{entry['name']}"))
            manifest["layers"].append(entry)
            continue

        folder = os.path.join(out_dir, entry["group"])
        os.makedirs(folder, exist_ok=True)
        image.save(os.path.join(folder, entry["name"] + ".png"))
        manifest["layers"].append(entry)
        print(f"  {entry['group']}/{entry['name']}.png  {entry['w']}x{entry['h']} "
              f"@{entry['x']},{entry['y']}")

    manifest["layers"].sort(key=lambda item: (item["group"], item["name"]))
    with open(os.path.join(out_dir, manifest_name), "w") as handle:
        json.dump(manifest, handle, indent=1)
    print(f"\n{len(manifest['layers'])} layer -> {out_dir}")

    # Key có trong map mà PSD không còn: hoạ sĩ xoá/đổi tên. PNG cũ vẫn nằm lại trong Visuals/
    # và thành asset mồ côi, phải biết mà dọn.
    missing = ([key for key in list(mapping) + list(anchors) if key not in hits]
               + [key for key in show if key not in show_hits]
               + [key for key in noclip if key not in noclip_hits])
    if missing:
        print("\nCẢNH BÁO — có trong map nhưng PSD không còn (PNG cũ thành mồ côi):")
        for key in missing:
            entry = mapping.get(key)
            if entry:
                target = f"{entry[0]}/{entry[1]}"
            elif key in anchors:
                target = f"anchor {anchors[key]}"
            elif key in show:
                target = "show (ép xuất layer tắt mắt)"
            else:
                target = "noclip (bỏ layer clipping đè lên)"
            print(f"  {key!r} -> {target}")

    if empty:
        print("\nLayer rỗng, không xuất được ảnh:")
        for path in empty:
            print(f"  {path!r}")

    if shown:
        print("\nLayer tắt mắt được ép xuất vì có trong `show`:")
        for path, target in shown:
            print(f"  {path!r} -> {target}")

    if clipped:
        print("\nCẢNH BÁO — layer là CLIPPING MASK, composite một mình ra ảnh trong suốt. "
              "GIỮ NGUYÊN png cũ (không ghi đè).\n"
              "  Layer này trong Photoshop chỉ hiện ở phần đè lên layer nền ngay DƯỚI nó, nên "
              "tách rời ra là không còn gì.\n"
              "  Cách xử lý: tìm bản BẸT hoạ sĩ đã gộp sẵn (thường có, để xem tổng thể) rồi "
              "`crops` lấy đúng vùng đó.")
        for path, target in clipped:
            print(f"  {path!r} -> {target}")

    if hidden:
        print("\nCẢNH BÁO — layer bị tắt mắt, GIỮ NGUYÊN png cũ (không ghi đè). "
              "Nếu đây là art thật (trạng thái kia của toggle…) thì thêm vào `show`:")
        for path, target in hidden:
            print(f"  {path!r} -> {target}")

    if unmapped:
        print(f"\n{len(unmapped)} layer chưa map (bỏ qua) — soát xem có art mới không:")
        for path, bbox in unmapped:
            print(f"  {path!r} bbox={bbox}")


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--psd", required=True, help="file .psd mockup")
    parser.add_argument("--map", help="file JSON map layer -> (group, tên file)")
    parser.add_argument("--out", help="thư mục xuất PNG + manifest")
    parser.add_argument("--manifest", help="tên file manifest (mặc định lấy từ map, rồi tới layout.json)")
    parser.add_argument("--tree", action="store_true", help="in cây layer rồi thoát")
    parser.add_argument("--text", action="store_true", help="in font/size/baseline của text layer rồi thoát")
    parser.add_argument("--smart-object", dest="smart_object",
                        help="tên smart object bọc artboard cần mở (raw pack). Mặc định lấy "
                             "`smartObject` trong map.")
    args = parser.parse_args()

    # encoding="utf-8" BẮT BUỘC: trên Windows `open()` mặc định cp1252, mà file map thì có ghi
    # chú thích tiếng Việt (đúng nếp của project) — thiếu nó là script chết ngay ở dòng này với
    # UnicodeDecodeError, chưa kịp mở PSD.
    config = json.load(open(args.map, encoding="utf-8")) if args.map else {}
    mapping = {key: tuple(value) for key, value in config.get("layers", {}).items()}
    anchors = dict(config.get("anchors", {}))

    psd = load_psd(args.psd)

    smart_object = args.smart_object or config.get("smartObject")
    if smart_object:
        psd = open_smart_object(psd, smart_object)

    if args.text:
        cmd_text(psd)
        return
    if args.tree:
        cmd_tree(psd, mapping, anchors)
        return
    if not args.map or not args.out:
        sys.exit("Cần --map và --out để xuất (hoặc dùng --tree / --text).")

    origin = config.get("origin")
    if origin is not None and len(origin) != 4:
        sys.exit("`origin` phải là [x, y, w, h] — rect artboard, đọc từ dòng artboard của --tree.")

    cmd_export(psd, args.psd, mapping, anchors, config.get("crops", {}), args.out,
               args.manifest or config.get("manifest") or "layout.json",
               list(config.get("show", [])), origin, list(config.get("noclip", [])))


if __name__ == "__main__":
    main()
