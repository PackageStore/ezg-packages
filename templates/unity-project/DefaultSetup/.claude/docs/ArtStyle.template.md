<!--
ArtStyle.template.md — KHUNG dùng chung, đi theo bộ AI của template (Feature Hub cập nhật được file này).
KHÔNG điền giá trị của game vào đây. File thật của project là `.claude/docs/ArtStyle.md`:
  - bootstrap.sh / bootstrap.ps1 tự copy khung này thành ArtStyle.md khi project chưa có;
  - skill visual nào thấy thiếu ArtStyle.md cũng copy khung này rồi làm § Bootstrap bên dưới.
ArtStyle.md là file CỦA PROJECT: Feature Hub không bao giờ ghi đè nó, sửa thoải mái.
Giữ nguyên tên + SỐ của các mục (§1 Bản sắc … §11 Tiêu chí duyệt): skill dùng chung tham chiếu theo số.
Phân vai: skill/agent dùng chung chỉ chứa STEP đảm bảo style ("đọc §N, chấm §11, block khi…"), KHÔNG chứa
giá trị style (màu, font, cỡ, nhịp motion, concept, kit, bố cục khoá). Mọi giá trị đó nằm ở ArtStyle.md.
Skill cần một chỗ chứa mới → thêm mục/slot TRỐNG vào khung này, giá trị điền ở ArtStyle.md của project.
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
`figma-to-unity`, `create-vfx`, `game-vfx`…) đọc file này **trước** khi quyết định — không suy style từ tên game hay tên file.

**Thứ tự ưu tiên khi mâu thuẫn:** lời dev trong task → art của hoạ sĩ giao cho đúng màn đó (PSD/Figma)
→ file này → màn mẫu đã duyệt (§8) → suy luận của agent (chỉ cho chỗ file này chưa nói, và phải ghi lại).

## 1. Bản sắc

- **Thể loại / bối cảnh:** <vd: idle tycoon nông trại, casual, mọi lứa tuổi>
- **Cảm giác:** <3–5 tính từ: ấm, vui, no đủ, tươi…>
- **Kiểu render:** <vd: cartoon bóng bẩy, khối tròn, gradient mềm, highlight góc trên trái, đáy đậm hơn>
- **Từ khoá KHÔNG:** <những cảm giác/kiểu cấm: tối, kim loại lạnh, neon, flat vector, ...>
- **Luật concept** (skill rework đặt concept/ẩn dụ cho màn — concept phải qua luật này):
  - Được lấy từ: <thế giới/chất liệu nào của game>
  - Cấm: <ẩn dụ/chủ đề đã chứng minh lệch style — đồng bộ với §9>
  - Họ màn có bố cục khoá (§4b): concept chỉ là <phần được đổi, vd hero + họ màu>, không phải bố cục mới

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
- **Luật chung:** <CTA chính duy nhất, ...>
- **Khoảng cách / lưới:** <thang spacing, padding chuẩn, bội số>

### 4b. Họ màn có bố cục khoá

Refactor một màn thuộc họ dưới đây chỉ đổi cột "Được đổi"; giữ nguyên phần khoá không bị tính là "chưa
đổi bố cục". Màn không thuộc họ nào → bố cục tự do trong §4.

| Họ màn | Phần khoá | Được đổi khi refactor |
|---|---|---|

### 4c. Ngôn ngữ trạng thái

Mỗi trạng thái nhận ra trong ~0.5 s bằng màu + icon + độ sáng (không chỉ bằng chữ). Trạng thái chưa có
dòng → agent ghi đề xuất vào đây (`Status: draft`), không tự chế mỗi màn một kiểu.

| Trạng thái | Cách thể hiện (token §2 / kit §3 / motion §7) |
|---|---|
| locked | |
| available / claimable | |
| claimed / đã nhận | |
| cooldown | |
| disabled | |
| empty | |

## 5. Chữ

**Khung cỡ:** <min–max dùng khi vai trò không có trong bảng>. Best-fit min trong bảng là sàn tuyệt đối
khi bản dịch dài.

| Vai trò | Font (asset) | Cỡ (best-fit min–max) | Màu | Viền / bóng |
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

### 6b. VFX

Style hiệu ứng trong thế giới game (trúng đòn, nổ, đạn, vùng, buff…). Phần kỹ thuật (cấp, ngân sách mobile, pool,
frame-by-frame hay không) là của quy chuẩn `game-vfx` + brief VFX của game; mục này chỉ giữ phần **nhìn**.
Skill `create-vfx` đọc khối `vfx-style` bằng script, nên giữ đúng cú pháp từng dòng.

- **Nhận diện:** <họ hình: flipbook phát sáng vẽ tay / hạt + shader / pixel…; có viền không, độ bão hoà, glow>
- **Từ vựng hình:** <lõi, tia, vòng, vỏ, lưỡi liềm, hạt, khói… dùng gì, cái gì sáng nhất>
- **Màu theo nguyên tố:** <nguyên tố → dải màu; nguyên tố mặc định khi không nói; tương phản với nền gameplay>
- **Nhịp:** <đòn trúng đọc ngay khung đầu? đuôi hạt dài hay gọn? hiệu ứng lặp nhanh hay chậm>
- **Cấm:** <hướng VFX đã bị loại — chi tiết + lý do ở §9>

```vfx-style
# default: <nguyên tố khi dev không nói, vd fire>
# allow: <nguyên tố được dùng, cách nhau dấu phẩy; bỏ dòng = tất cả>
# layers: <sorting layer dưới nhân vật>, <sorting layer trên nhân vật>          (bỏ dòng = FX_Ground, FX)
# grounds: <#nền gameplay tối nhất>, <#nền gameplay sáng nhất>                  (nền duyệt hiệu ứng)
# ramp <nguyên tố>: 0:#rrggbb 0.3:#rrggbb 0.6:#rrggbb 1:#ffffff | spark #rrggbb #rrggbb #rrggbb
# ref <impact|muzzle|explode|cast|slash|projectile|aura>: <path sheet.png> <cột>x<hàng> @<cột>,<hàng>
```

## 7. Motion

<cảm giác chung: nảy nhẹ / mềm / nhanh gọn; easing chủ đạo; khoảnh khắc thưởng; những thứ cấm>.
Luật kỹ thuật DOTween/UniTask nằm ở skill `refactor-ui` §5 — ở đây chỉ ghi phần riêng của game.

| Lớp / beat | Giá trị (easing, thời lượng, biên độ) |
|---|---|
| Vào (tổng, khung chính, stagger phần tử con) | |
| Idle (chỉ trên focal point: rays / glow / shine / float) | |
| Feedback (nhấn, claim/purchase, stamp, số đếm) | |
| Ra / đổi tab | |

## 8. Màn mẫu đã duyệt

Màn dev đã khen/duyệt — mốc "đúng style" để so. Thêm dòng mỗi khi dev duyệt một màn. **Chỉ** màn có trong
bảng này mới là mốc; màn đang ship mà chưa duyệt không bao giờ là mốc.

Ảnh: `.claude/docs/ArtStyle/screens/approved/<Màn>.png` (chụp ở tỷ lệ thiết kế, rộng ~540 px cho nhẹ repo).
Agent so bố cục/họ art bằng cách `Read` ảnh này — bảng chữ thôi không đủ để so.

| Màn | Ảnh | Prefab / TechSpec | Lấy mẫu gì |
|---|---|---|---|

**Chờ duyệt** — màn agent vừa ship, ảnh ở `.claude/docs/ArtStyle/screens/pending/<Màn>.png`. Không phải
mốc; dev duyệt → chuyển ảnh sang `approved/` + dòng lên bảng trên, dev chê → ảnh sang `rejected/` + §9.

| Màn | Ảnh | Ngày ship |
|---|---|---|

## 9. Hướng đã bị loại

Không lặp lại. Thêm dòng mỗi khi dev chê một hướng — đây là bộ nhớ chung của mọi session/agent.
Ảnh (nếu còn chụp được): `.claude/docs/ArtStyle/screens/rejected/<Màn>-<YYYY-MM-DD>.png`.

| Ngày | Màn | Hướng bị loại | Lý do dev nêu | Ảnh |
|---|---|---|---|---|

## 10. Bảo trì

- Dev **duyệt** một màn → §8 (ảnh `pending/` → `approved/`). Dev **chê** hướng nào → §9 (nguyên văn lý do,
  ảnh → `rejected/`) + cập nhật luật concept §1 nếu là chuyện concept. Kit đổi → sửa §3 + chạy lại
  `art-style-board.py`. Token mới → §2.
- Đổi `Status` sang `approved` chỉ khi dev xác nhận.

## 11. Tiêu chí duyệt

Checklist mà builder tự chấm và `ui-visual-reviewer` chấm lại trên ảnh **tự chụp**. Mỗi dòng là câu
có/không kiểm được trên ảnh hoặc prefab, trỏ về mục chứa giá trị. Dòng mức `block` sai = verdict block.
Thêm dòng khi dev chê một lỗi mà checklist chưa bắt được.

| # | Câu hỏi | Mục | Mức |
|---|---|---|---|
| 1 | Mọi màu đọc ra là token §2 (hoặc họ recolor §2) — không có họ màu lạ? | §2 | block |
| 2 | Khung, nút, X, ruy băng, tiến độ là sprite của kit §3 — không có bản vẽ tay "giống kit"? | §3 | block |
| 3 | Màn thuộc họ §4b: mọi phần khoá còn nguyên? | §4b | block |
| 4 | Đúng một CTA chính nổi nhất; vị trí X/back và loại màn (popup / full-screen) đúng §4? | §4 | block |
| 5 | Font + cỡ + viền/bóng của từng vai trò khớp bảng §5, kể cả ngôn ngữ dài? | §5 | block |
| 6 | Không có yếu tố nào trong "Từ khoá KHÔNG" và concept qua được luật concept §1? | §1 | block |
| 7 | Không giống hướng nào ở §9 (đối chiếu cả ảnh `rejected/`)? | §9 | block |
| 8 | Đặt cạnh ảnh §8 cùng họ: cùng một "gia đình" (kit, xử lý chữ, nhịp dọc, mật độ)? | §8 | block |
| 9 | Art mới (hero/FX) đặt cạnh board kit + hero (FX: cạnh `ref` §6b) không lộ ra là một họ art khác? | §6, §6b | block |
| 10 | Mọi trạng thái của màn thể hiện theo §4c? | §4c | minor |
| 11 | Motion nằm trong khoảng §7? | §7 | minor |

## Bootstrap (khi Status = template)

Agent gặp file chưa điền thì **không** tự chế style từ tên game. Làm theo thứ tự, rồi đặt
`Status: draft` và ghi trong report cuối là dev cần duyệt file này:
1. Gom nguồn thật: art hoạ sĩ trong project (PSD/PNG ở `Visuals/`, GUI pack đang dùng), màn đã ship,
   GDD/TechSpec có nhắc màu/theme. Dựng contact sheet và **xem**.
2. Điền §1–§6b từ nguồn đó: đo hex bằng Pillow trên sprite thật, font + cỡ đọc từ text template prefab.
   VFX (§6b): xem các sheet/prefab FX đang ship, khai `ref` cho vài khung tiêu biểu, `grounds` đo trên nền gameplay;
   game chưa có FX nào thì để khối `vfx-style` trống và hỏi dev hướng VFX khi lần đầu cần.
3. Khai `art-style-boards` cho kit chính, chạy `art-style-board.py`.
   Màn của hoạ sĩ đang ship (art giao tận tay, chưa bị chê) → chụp vào `screens/approved/` + §8: đó là mốc
   đầu tiên. Không có thì §8 để trống — đừng lấy màn agent tự làm làm mốc.
   §11 giữ các dòng mặc định; §4b/§4c điền khi có họ màn / trạng thái thấy được trong art thật.
4. Không có art nào để bám (project trắng) → dừng phần visual và hỏi dev hướng art; đây là quyết định
   của dev, không phải của agent.
