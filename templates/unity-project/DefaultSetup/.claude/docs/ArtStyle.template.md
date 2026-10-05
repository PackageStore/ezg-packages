<!--
ArtStyle.template.md — KHUNG dùng chung, đi theo bộ AI của template (Feature Hub cập nhật được file này).
KHÔNG điền giá trị của game vào đây. File thật của project là `.claude/docs/ArtStyle.md`:
  - bootstrap.sh / bootstrap.ps1 tự copy khung này thành ArtStyle.md khi project chưa có;
  - skill visual nào thấy thiếu ArtStyle.md cũng copy khung này rồi làm § Bootstrap bên dưới.
ArtStyle.md là file CỦA PROJECT: Feature Hub không bao giờ ghi đè nó, sửa thoải mái.
Giữ nguyên tên + SỐ của các mục (§1 Bản sắc … §9 Hướng đã loại): skill dùng chung tham chiếu theo số.
-->
# Art Style — <Tên game>

**Status:** template
<!-- template = chưa điền (skill visual phải chạy § Bootstrap trước) · draft = agent tự rút, dev chưa
     duyệt (dùng được, nhưng báo dev) · approved = dev đã duyệt (nguồn chuẩn, không tự ý đổi) -->
**Cập nhật:** <YYYY-MM-DD> · **Người duyệt:** <dev/hoạ sĩ>

## 0. File này là gì

Nguồn chuẩn **duy nhất** về visual của game: palette, kit UI, bố cục, chữ, icon/hero art, motion, các
hướng đã bị loại. Mọi skill/agent ra quyết định hình ảnh (`refactor-ui`, `create-ui` / `/new-ui`,
`/ui-mockup` + `mockup-drafter`, `ui-visual-reviewer`, `gen-icon`, `merge-psd-ui`, `psd-to-feature`,
`figma-to-unity`…) đọc file này **trước** khi quyết định — không suy style từ tên game hay tên file.

**Thứ tự ưu tiên khi mâu thuẫn:** lời dev trong task → art của hoạ sĩ giao cho đúng màn đó (PSD/Figma)
→ file này → màn mẫu đã duyệt (§8) → suy luận của agent (chỉ cho chỗ file này chưa nói, và phải ghi lại).

## 1. Bản sắc

- **Thể loại / bối cảnh:** <vd: idle tycoon nông trại, casual, mọi lứa tuổi>
- **Cảm giác:** <3–5 tính từ: ấm, vui, no đủ, tươi…>
- **Kiểu render:** <vd: cartoon bóng bẩy, khối tròn, gradient mềm, highlight góc trên trái, đáy đậm hơn>
- **Từ khoá KHÔNG:** <những cảm giác/kiểu cấm: tối, kim loại lạnh, neon, flat vector, ...>

## 2. Palette (token)

Spec, mockup, recolor và text **chỉ dùng token trong bảng này**. Cần màu mới → thêm token vào đây
(ghi lý do + màn dùng) rồi mới dùng; không rải hex lẻ trong spec từng màn.

| Token | Hex | Vai trò |
|---|---|---|
| `<nhóm.tên>` | `#RRGGBB` | <panel / chữ / CTA / shade / ...> |

**Màu rarity / trạng thái** (cố định toàn game):

| Token | Hex | Dùng cho |
|---|---|---|

**Họ màu được phép khi recolor theo màn** (offer, event…):

| Họ | Token chính | Khi nào |
|---|---|---|

## 3. Kit UI chuẩn

| Vai trò | Asset (path) | Ghi chú (9-slice, size, khi nào dùng) |
|---|---|---|
| CTA chính | | |
| Nút phụ / huỷ | | |
| Nút đóng (X) | | |
| Khung popup | | |
| Ruy băng / tiêu đề | | |
| Header full-screen | | |
| Thanh tiến độ | | |
| Badge / tag / red dot | | |
| FX dùng chung (glow, rays, sparkle) | | |

**Board tham chiếu** — ảnh contact sheet để agent NHÌN kit (agent không đọc được PSD). Khai nguồn ở
khối dưới rồi chạy `python3 .claude/scripts/art-style-board.py` → `.claude/docs/ArtStyle/<tên>.png`.
Skill visual `Read` các board này trước khi chọn sprite/màu.

```art-style-boards
# <tên-board>: <path thư mục hoặc file>, <path>…   (tương đối repo root, quét đệ quy *.png/*.psd)
```

## 4. Bố cục

- **Canvas / tỷ lệ:** <độ phân giải thiết kế, CanvasScaler, safe area>
- **Màn full-screen:** <header ở đâu, nút back/X ở đâu, chừa chỗ cho thanh tiền tệ không>
- **Popup thường:** <khung, tiêu đề, X, nút CTA>
- **Popup offer / IAP:** <họ khung riêng nếu có>
- **Luật chung:** <CTA chính duy nhất, khoảng cách bội số, ...>

## 5. Chữ

| Vai trò | Font (asset) | Cỡ | Màu | Viền / bóng |
|---|---|---|---|---|
| Tiêu đề | | | | |
| Heading | | | | |
| Body | | | | |
| Chữ trên nút CTA | | | | |
| Số / giá | | | | |

## 6. Icon & hero art

- **Style:** <góc nhìn, viền, độ bóng, mức chi tiết, nguồn sáng>
- **Prompt nền cho AI** (ghép trước mô tả vật thể):
  > <đoạn mô tả style cố định cho mọi lần sinh ảnh>
- **Ảnh tham chiếu phải đính kèm khi sinh:** <path 2–3 icon chuẩn>
- **Kỹ thuật:** <kích thước, nền tách, compression, 1 icon/ảnh…>

## 7. Motion

<cảm giác chung: nảy nhẹ / mềm / nhanh gọn; easing chủ đạo; khoảnh khắc thưởng; những thứ cấm>.
Luật kỹ thuật DOTween/UniTask nằm ở skill `refactor-ui` §5 — ở đây chỉ ghi phần riêng của game.

## 8. Màn mẫu đã duyệt

Màn dev đã khen/duyệt — mốc "đúng style" để so. Thêm dòng mỗi khi dev duyệt một màn.

| Màn | Prefab / TechSpec | Lấy mẫu gì |
|---|---|---|

## 9. Hướng đã bị loại

Không lặp lại. Thêm dòng mỗi khi dev chê một hướng — đây là bộ nhớ chung của mọi session/agent.

| Ngày | Màn | Hướng bị loại | Lý do dev nêu |
|---|---|---|---|

## 10. Bảo trì

- Dev **duyệt** một màn → thêm §8. Dev **chê** hướng nào → thêm §9 (nguyên văn lý do). Kit đổi → sửa §3
  + chạy lại `art-style-board.py`. Token mới → §2.
- Đổi `Status` sang `approved` chỉ khi dev xác nhận.

## Bootstrap (khi Status = template)

Agent gặp file chưa điền thì **không** tự chế style từ tên game. Làm theo thứ tự, rồi đặt
`Status: draft` và ghi trong report cuối là dev cần duyệt file này:
1. Gom nguồn thật: art hoạ sĩ trong project (PSD/PNG ở `Visuals/`, GUI pack đang dùng), màn đã ship,
   GDD/TechSpec có nhắc màu/theme. Dựng contact sheet và **xem**.
2. Điền §1–§6 từ nguồn đó: đo hex bằng Pillow trên sprite thật, font + cỡ đọc từ text template prefab.
3. Khai `art-style-boards` cho kit chính, chạy `art-style-board.py`.
4. Không có art nào để bám (project trắng) → dừng phần visual và hỏi dev hướng art; đây là quyết định
   của dev, không phải của agent.
