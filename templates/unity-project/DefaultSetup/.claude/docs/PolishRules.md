---
description: Luật polish bắt buộc cho mọi game mobile build từ EZG template — nguồn chuẩn mà rule game-polish, validator ui-spec, /new-ui (Phase C/D), /refactor-ui, ui-visual-reviewer, code-reviewer, performance-reviewer, qa-verifier, task-planner và các skill VFX/SFX/animation tham chiếu theo SỐ MỤC + ID checklist. Không nạp sẵn — đọc đúng mục cần.
---

# Game Polish Rules

Luật polish bắt buộc cho mọi game mobile làm trên EZG template. File dùng chung (Feature Hub cập nhật):
project **không sửa** file này — số riêng của game ghi ở `.claude/docs/ArtStyle.md` (visual) hoặc
`.claude/project-profile.json` → `polish` (ngưỡng đo được, module Part II).

**Giữ nguyên số mục §1–§14, M1–M4 và ID `P01…`** — skill/agent/validator trỏ theo số. Thêm luật mới
→ thêm ID mới ở cuối bảng Checklist, không đánh số lại.

## Cách áp dụng

- **Part I – Core** áp cho mọi game. **Part II – Module** chỉ áp khi game có tính năng tương ứng —
  game khai module nào ở `project-profile.json` → `polish.modules` (`combat` = M1, `tiers` = M2,
  `procgen` = M3, `gacha` = M4).
- **Số là mặc định, nguyên tắc là bắt buộc.** Thứ tự ưu tiên khi mâu thuẫn:
  **lời dev trong task → art hoạ sĩ giao cho đúng màn đó (PSD / Figma) → `ArtStyle.md` của game → file
  này** (cùng thứ tự rule `art-style`). Nguồn trên được đổi con số (cỡ chữ, nhịp motion…), không được bỏ
  nguyên tắc. Riêng **sàn đo được** (chữ < 30 px, vùng chạm < 128 px) mà art hoạ sĩ / ArtStyle vi phạm:
  agent không tự vẽ đè lên art của hoạ sĩ — giữ nguyên và báo dev bằng dòng `polish:`.
- **Canvas chuẩn: 1080×2400 dọc** (khớp CanvasScaler của `screen_template`; mockup / ui-spec / ảnh chụp
  kiểm cùng kích thước này — `designResolution` trong profile). Mọi số px trong file tính trên canvas đó;
  ngang / tablet quy đổi theo tỷ lệ. Spec cũ 1080×1920 đã duyệt vẫn hợp lệ — bề ngang vẫn 1080 nên các
  ngưỡng px giữ nguyên.
- **Khi nào kiểm:** trước khi báo xong một task có phần người chơi thấy/chạm, trước review và trước mọi
  push/build. Checklist có ID ở cuối file; cột "Ai kiểm" nói gate nào chặn dòng đó.
- **Mức:** `block` = đo/kiểm được, sai thì gate chặn (tự sửa tối đa 2 vòng như mọi gate) · `warn` = cảm
  tính, ghi vào report, không chặn · `human` = agent **không** tự chấm được (nghe trên máy thật, đo LUFS,
  so trên store) — ghi vào "Manual verification steps", **không bao giờ** tự đánh pass.
- **Không tham chiếu game khác** (rule `no-external-game-refs`): "chuẩn thể loại" = quy ước chung của thể
  loại, tả bằng lời trung tính; mốc so sánh trong repo là màn đã duyệt ở ArtStyle §8.

---

# PART I – CORE

## 1. Nguyên tắc nền

1. **Một bản sắc.** Icon app, splash, menu, UI, nhân vật, VFX cùng một art style (ArtStyle.md). Màn lệch
   tông, placeholder hay "trông như demo" là lỗi. Thứ không hợp hướng thì làm lại, không vá.
2. **Đọc là hiểu.** Hình hợp chức năng, VFX hợp hành động, tên hợp vật, icon hợp hiệu ứng — người chơi
   đoán đúng mà không cần đọc chữ.
3. **Mọi thao tác có phản hồi ngay:** hình + tiếng, thêm rung khi hợp. Không có "bấm mà không gì xảy ra".
4. **Không gì khó hiểu.** Mỗi cơ chế, ký hiệu, con số, nhãn tự giải thích, hoặc có một dòng giải thích lần
   đầu xuất hiện. Không làm được thì bỏ.
5. **Màn sạch.** Gameplay luôn đọc được. Không lặp thông tin, không phí chỗ, không trang trí vô nghĩa.
6. **Mượt.** Mọi chi phí "lần đầu" trả ở màn loading. Trong gameplay: không khựng, không nháy, không giật.
7. **Không làm ẩu.** Không dùng lại design/asset cũ không hợp hướng; không ghép asset cho xong; không VFX
   ảnh tĩnh; không xoá thay vì làm đúng.
8. **Đúng phạm vi.** Làm đúng điều được giao. Không tự bù số, không tự thêm luồng. Bị chê điểm nào sửa
   đúng điểm đó.
9. **Tham chiếu trước khi dựng.** Rút chuẩn + số đo của thể loại (tả trung tính) và màn đã duyệt
   (ArtStyle §8) trước khi dựng — không tự chế.
10. **Sửa tận gốc.** Tìm root cause, quét mọi chỗ cùng lỗi, thêm test chống tái phát.

---

## 2. Art

### 2.1 Art bible
- **Mỗi project có art bible = `ArtStyle.md`** (shape language, mức chi tiết, shading, outline, palette, góc
  camera, tỷ lệ). Asset lệch art bible là lỗi, kể cả khi trông đẹp.
- **Đọc được ở cỡ điện thoại thật quan trọng hơn chi tiết.** Khối lớn, tương phản rõ, ít chi tiết nhỏ.
  Chi tiết nhỏ hơn ~2–3 px ở cỡ hiển thị là nhiễu.
- **Không trộn asset** của project khác hay asset store chưa chuyển style.
- **Giữ model nhân vật nhất quán** (mặt, mắt, tỷ lệ, màu đặc trưng) ở mọi asset phái sinh: animation,
  icon, màn kết quả.

### 2.2 Silhouette và kích thước
- **Thử silhouette không màu ở cỡ hiển thị thật.** Mọi loại / biến thể / cấp phải phân biệt được.
- **Biến thể / cấp khác nhau ở silhouette**, không chỉ ở màu.
- **Kích thước có trần và sàn.** Trần: không che nhân vật chính hay thông tin chính. Sàn: vật nhỏ nhất
  vẫn đọc được.
- **Thứ bậc kích thước phản ánh tầm quan trọng** (thường < elite < boss; item thường < item hiếm).

### 2.3 Màu
- **Dành riêng một màu cho nguy hiểm / lỗi** (thường là đỏ). Không dùng nó để trang trí.
- **Phân lớp thị giác** từ êm nhất tới nổi nhất: nền / sàn → vật tương tác → nhân vật chính → cảnh báo.
  Đạo cụ nền tối hoặc nhạt hơn vùng chơi.
- **Màu đặc trưng nhân vật / thương hiệu** không phủ mảng UI lớn.
- **Không truyền thông tin chỉ bằng màu.** Luôn kèm icon hoặc hình để người mù màu vẫn đọc được.

### 2.4 Sản xuất asset
- **Chỉ đổi style hay góc thì giữ cấu trúc + tỷ lệ gốc.**
- **Vẽ vật như lúc được dùng** — tư thế / trạng thái đang hoạt động, không cất đi.
- **Tính trước cho animation:** tách layer; không nướng FX vào sprite.
- **Art UI (thẻ, icon) tách khỏi sprite trong game.**
- **Vệ sinh asset:** sprite padding ≥ 4 px; pivot đúng (chân cho vật đứng, tâm cho FX); không import nhỏ
  hơn cỡ hiển thị; ghi đè thì giữ ID/GUID (`.meta`); không trùng tên asset.
- **Sprite dài (dây, xích, cầu) lặp theo chiều dài,** không kéo giãn một trục.
- **Lật / xoay / scale không được méo** (kiểm cả shader). Vật tròn vẫn tròn.

### 2.5 Icon app và splash
- **Icon:** chủ thể lớn đầy khung, biểu cảm mạnh, nền đơn giản tương phản cao; đọc được ở 48 px. Thử ở
  1024 / 180 / 48 / 29 px, trên nền tối, sáng và hình nền máy `[human: so trên kệ store cạnh app cùng thể loại]`.
- **Splash / loading:** chất lượng như icon, chủ thể ở giữa, chừa chỗ cho logo + thanh loading; không bị
  UI che ở bất kỳ tỷ lệ nào.

### 2.6 Duyệt art
- **Bảng duyệt gồm:** nhiều cỡ (lớn / cỡ gameplay / cỡ icon); ghép vào màn thật cạnh nhân vật chính và
  asset cùng cấp đang có; một hàng silhouette không màu; đặt trên vài nền khác nhau.
- **Đổi hướng art** là quyết định của dev: dừng sản xuất hàng loạt, đưa 2–3 phương án để dev chọn. Agent
  tự chủ (`/refactor-ui`, `/run-backlog`) **không** tự đổi art direction — chỉ làm bên trong ArtStyle.md
  (ArtStyle § Bootstrap bước 4).

---

## 3. Animation

- **Chất lượng bắt buộc, kỹ thuật tuỳ chọn** (rig / frame-by-frame / procedural). Bản mới kém hơn thì giữ bản cũ.
- **Mọi clip của một nhân vật cùng một design.** Frame đầu + cuối là tư thế nghỉ, đổi trạng thái không
  giật hay nháy.
- **Nhất quán trong clip:** tay cầm, hướng vật, tỷ lệ không đổi. Hành động hướng về mục tiêu.
- **Công cụ sinh không vẽ hiệu ứng vào sprite** — hiệu ứng đến từ hệ VFX.
- **Chuyển động khớp tốc độ thật** (bước chân khớp quãng đường). Không sửa bằng cách tua nhanh. Tốc độ
  đổi thì animation scale theo.
- **Trọng lượng:** vật to nặng chậm và chắc; vật nhẹ nhanh và nảy. Đám đông lệch pha nhau.
- **Hành động có nhịp:** chuẩn bị → hành động → theo đà → hồi.
- **Squash / stretch giới hạn ~±10%** và luôn về tỷ lệ gốc.
- **Nhân vật ở menu không đứng im.** Idle thở + biến thể ngẫu nhiên; chạm vào thì phản ứng.
- **Màn kết quả có animation cảm xúc** (vui khi thắng, buồn khi thua).
- **Tốc độ animation phụ:** không lề mề (thường ≤ 1 s); không quá nhanh ở khoảnh khắc hồi hộp (mở
  thưởng) — cho chạm để bỏ qua.
- **Logic không bao giờ phụ thuộc animation.** State, tiền, item được ghi ngay lúc người chơi hành động;
  animation chỉ là hình. Input trong lúc animation chạy không bao giờ mất dữ liệu. (Template: cấp thưởng
  qua `RewardsService` / `PurchaseManager` trước, beat chạy sau; đóng view giữa beat vẫn cấp đúng một lần.)
- **Vật nảy / lăn / rơi luôn bám đất:** bóng scale theo độ cao, squash nhẹ khi chạm đất, xoay theo hướng lăn.
- **Vật phản ứng khi bị đánh** (rung, nứt, nảy). Phá huỷ nhiều giai đoạn; mảnh vỡ là art thật, không cắt từ sprite.

---

## 4. VFX

- **Một ngôn ngữ VFX khớp art style** (ArtStyle §6b). Asset thư viện phải chuyển style trước khi dùng.
- **Nhiều lớp, mỗi lớp chuyển động riêng:** lõi, loé, hạt, khói / tan, vết trên đất. Không VFX "ảnh tĩnh"
  chỉ trượt hay xoay.
- **Khớp hành động + chủ đề** (đâm ≠ chém ≠ bắn; lửa ≠ sét). Map tên / hiệu ứng → VFX qua một bảng duy nhất.
- **Scale theo sức mạnh / độ hiếm:** hiếm hơn = dày, sáng, riêng hơn.
- **Vùng VFX = vùng tác dụng thật.**
- **Phát ra từ đúng nguồn** (nòng, tay, miệng), quỹ đạo rõ.
- **Không che gameplay:** loé ở trên mục tiêu, khói / vết ở dưới; VFX của nhân vật chính không che nhân
  vật chính; VFX gameplay không vẽ đè UI.
- **Không vòng / hào quang / marker vô nghĩa.** Marker chỉ cho mục tiêu đặc biệt, không gắn cả đám.
- **Hiệu ứng trạng thái:** mỗi trạng thái một hình + màu riêng, nhỏ, gắn vào mục tiêu. Không tô cả người,
  không phủ cả màn.
- **Xuất hiện lẫn biến mất đều có VFX:** spawn, chết, NPC đến / đi, nhận item.
- **Bị chê chói / mỏi mắt** → giảm tần suất hoặc cường độ, giữ art.
- **Ngân sách:** atlas hết, ít material, giới hạn số hạt / FX mỗi frame, đo fps mỗi lần thêm một cái.
- **Kiểm kê:** mọi VFX đi qua hệ VFX chuẩn (skill `game-vfx` / `create-vfx`); không để sót VFX lệch style.

---

## 5. Âm thanh và rung

- **Phân lớp âm:** nhạc là nền, SFX quan trọng rõ, âm môi trường dưới nhạc. Không rè, không lách tách,
  không clip, không bị cắt cụt.
- **Mặc định:** tổng độ to ~−16 đến −18 LUFS, limiter ở master, không clip `[human: đo trên máy thật]`;
  tối đa ~6–8 voice cùng lúc; cùng một âm ≤ ~20 lần/s; voice bị chiếm fade-out (~30 ms); clip có đuôi fade.
- **Nhạc:** duck khi popup / khoảnh khắc lớn; crossfade khi đổi bài; pause / resume đúng vị trí.
- **SFX:** âm lặp nhiều có biến thể ngẫu nhiên; âm khớp chất liệu; "juice" âm tăng theo cấp.
- **Rung:** chỉ cho khoảnh khắc quan trọng (va mạnh, thưởng, lỗi); nhẹ, có giới hạn tần suất. Template:
  `VibrateManager` (tôn trọng toggle Rung trong Settings). `ButtonVibrate` rung **mỗi lần bấm**, không giới hạn
  tần suất — chỉ gắn cho nút của khoảnh khắc lớn, không gắn hàng loạt.
- **Settings luôn có:** Nhạc, SFX, Rung (+ Rung màn hình nếu game có screen shake).
- **Thử trên máy thật, bằng tai người** `[human]`.

---

## 6. Game feel / juice

- **Mọi tương tác chính có gói feedback:** hình (loé / nảy / biến dạng) + tiếng, thêm chữ / số và rung khi hợp.
- **Juice theo cấp.** Thao tác thường nhẹ, khoảnh khắc lớn mạnh, các cấp khác nhau rõ. Tham số để ở
  config (hằng số / CSV), không rải magic number.
- **Nhịp "giữ – loé – bay" khi nhận / mua:**
  1. Chạm: vật giữ tại chỗ, loé + pop (~0.3 s).
  2. Bay về đích; đích nảy (~×1.2 trong 0.25 s).
  3. Kết quả hiện khi vật chạm đích.

  Mọi biến thể của cùng một hành động có cùng feedback. Giá trị nhịp riêng của game ở ArtStyle §7 (thắng số ở đây).
- **Lựa chọn (thẻ, phần thưởng):** cái được chọn nâng lên + loé, cái khác tối đi, giữ ~0.5 s rồi mới đóng.
- **Số bay:** mã màu cố định theo loại, có viền, gộp khi dày, giới hạn số lượng, luôn trong safe area.
- **Chữ thông báo theo cấp.** Sự kiện nhỏ chữ nhỏ ở vùng phụ; chỉ sự kiện lớn mới to ở giữa. Không che popup.
- **Kết thúc gọn.** Thắng / xong hiện ngay, không có thời gian chết.
- **Khoảnh khắc lớn** (combo, thắng, thưởng hiếm) xếp lớp: freeze frame, rung, âm, chữ lớn, mưa thưởng, tổng gộp.
- **Slow motion chỉ cho khoảnh khắc kết liễu.** Đủ dài để đọc là có chủ đích, không làm animation frame-by-frame giật.

---

## 7. Camera và chuyển cảnh

- **Chủ thể chính không bao giờ bị UI / panel che.** Panel mở thì dời chủ thể vào vùng nhìn thấy.
- **Follow mượt:** một lớp smoothing, có deadzone, kẹp biên, không vượt quá.
- **Pan tới mục tiêu:** một ease ~0.5 s → giữ → pan về. Trong lúc camera trình bày, gameplay dừng (không
  bị đánh).
- **Chuyển cảnh liền:** fade ~0.3 s, nền không nhảy, vào thẳng nội dung thật.
- **Banner / UI kết quả neo vào tâm màn hình**, không theo vị trí camera.

---

## 8. Không gian, bám đất, phân lớp, va chạm

- **Mọi vật đứng bám đất.** Bóng / AO ôm mọi điểm tiếp xúc, pivot ở chân, frame idle không nhấc vật lên.
- **Bóng cùng hướng sáng,** scale theo vật, không bao giờ chồng đôi.
- **Sàn gameplay "êm":** sạch, ít hoa văn. Chiều sâu nằm ngoài vùng chơi.
- **Sort theo vị trí chân + kích thước,** không theo tâm sprite. Bóng luôn dưới; vật lớn che vật sau;
  telegraph nằm trên đất, dưới thân; mọi gameplay dưới UI.
- **Marker / vòng gắn vật** đi theo vật và luôn ở giữa.
- **Va chạm:** biên map có độ dày và chặn di chuyển; không đi xuyên vật; đồ trang trí không chặn đường;
  vật ở trung tâm thật sự ở giữa.

---

## 9. UI/UX

### 9.1 Bố cục
Số mặc định trên canvas dọc **1080×2400**; ngang / tablet quy đổi theo tỷ lệ.
- **Lưới và khoảng cách:** lưới 8 px; spacing 8 / 16 / 24 / 32 / 48; lề màn hình ≥ 48.
- **Vùng chạm ≥ 128 px** (≈ 44 pt); CTA cao 152–176 px. Visual nhỏ hơn thì thêm vùng raycast vô hình
  (ui-spec `hitArea`).
- **Chữ:** tối thiểu 32 px (không bao giờ dưới 30), body 40 px. Bảng chữ của game (ArtStyle §5) thắng
  nếu khác — nhưng sàn 30 px giữ nguyên.
- **Panel che ≤ 1/3 vùng gameplay.** CTA chính ở vùng ngón cái (nửa dưới).
- **Neo theo safe area.** Bộ thử tối thiểu: 1080×2400 (20:9), 1080×1920 (9:16), 1125×2436 (tai thỏ /
  safe area), 1080×2520 (21:9), 1536×2048 (tablet 3:4).
- **Xem chuẩn bố cục của thể loại + màn đã duyệt (ArtStyle §8) trước khi dựng màn.** Không tự chế.
- **Bốn lỗi luôn kiểm:** lệch lưới · khoảng cách chật hoặc không đều · chữ / nút quá nhỏ · popup chật, khô.

### 9.2 Chữ
- **Không tràn, cắt, chồng, thiếu glyph.** Font phủ mọi ngôn ngữ hỗ trợ và mọi ký hiệu dùng tới.
- **Chừa chỗ cho chữ dài hơn ~30%** khi dịch. Auto-size được, nhưng có cỡ tối thiểu.
- **Chữ căn giữa mặt nút; nhãn nút một dòng.**
- **Hệ font:** 1 font display + 1 font body, có bản quyền, không font demo.
- **Dùng ngôn ngữ người chơi,** không thuật ngữ nội bộ. Mô tả nói rõ tăng cái gì, cho cái gì, khi nào.
  Gợi ý ≤ ~12 từ mỗi dòng.
- **Hiện số thật.** Ký hiệu mơ hồ → icon + số + tooltip.
- **Rút gọn số lớn** (1.2K / 1.2M / 1.2B) bằng kiểu số không tràn (`long`/`double`). Template: `Utils.MoneyConvert`.
- **Không bao giờ hiện số âm, "0s", "+0", "−0"; không lặp thông tin.**
- **Đổi ngôn ngữ cập nhật ngay** mọi màn đang mở (label tĩnh qua component localize; label động nghe sự
  kiện đổi ngôn ngữ và set lại).

### 9.3 Nút
- **Nút trông chắc và rõ là bấm được:** viền, highlight phía trên, gờ hoặc bóng phía dưới.
- **Màu theo vai trò, nhất quán toàn game:** 1 màu primary và **tối đa 1 nút primary mỗi màn** (màn thông tin
  / danh sách không có hành động chính thì không cần nút primary — đừng bịa ra); 1 màu secondary; 1 màu riêng
  cho xem quảng cáo, kèm icon ▶; 1 màu huỷ; xám = khoá. Không trộn loạn màu nút trên một màn. (ui-spec: element
  `role` = primary / secondary / ads / cancel / close / back / nav / tab / item / locked.)
- **Cặp nút nằm ngang cùng chiều cao,** theo một quy ước cố định (secondary trái, primary phải). Không xếp
  dọc dài; nút secondary không nhỏ tới mức trông như lỗi.
- **Nút dùng được không bao giờ xám.** Khoá = xám + lý do (toast, dòng phụ, "Mở ở …"). Bấm nút khoá
  không bao giờ tốn tiền hay lượt.
- **Thiếu tài nguyên:** hiện rõ trên nút, cập nhật realtime. Bấm thì nút rung + dẫn tới chỗ kiếm / mua.
- **Cả thẻ bấm được thì không lồng nút bên trong.**
- **Không nút chết, không "Coming soon" trong build.**

### 9.4 Popup
- **Một tiêu điểm** (hình chủ thể + 1 CTA). Cỡ theo nội dung; không chật, không cắt, không tường chữ.
- **Vào / ra nhất quán.** Nút nảy khi bấm; nút khoá rung. (Template: `UITransition` qua `FeatureBaseController.AnimOpenUI/AnimCloseUI`.)
- **Chạm ra ngoài để đóng.** Template: field `ClickBackgroundToExit` của `FeatureBaseController` — **mặc định
  false**, popup phải bật nó trên prefab. Ngoại lệ (để false): màn full-screen, popup bắt buộc chọn / nhận
  thưởng, panel mà bấm nhầm là mất lượt.
- **Nút Back Android** làm đúng việc nút đóng / back của màn. Template: `UIManager` bắt Escape và đóng
  màn trên cùng qua `CloseMe` — màn có bước đóng đặc biệt phải đặt nó trong `CloseMe` (override + `base`),
  không treo riêng ở `onClick` của nút X.
- **Hàng đợi popup:** không chồng sai thứ tự, không mở lúc pause / chuyển cảnh, thử lại sau loading / resume.
- **Hành động có hậu quả** (mất item, tiêu tiền thật) nói rõ hậu quả và bắt xác nhận.
- **Màn trống** có hình + một dòng nói cách có nội dung.

### 9.5 HUD
- **Mỏng, gọn, có nhóm;** số cùng cỡ; không che gameplay.
- **Không phí chỗ.** Thông tin chính (máu boss, đồng hồ) sát HUD.
- **Người chơi luôn biết** mình đang ở đâu và còn bao nhiêu.
- **Không phần tử lạc** (debug, version, marker thừa) trong gameplay.
- **Giữ ~0.4 s để xem thông tin;** thả tay không kích hoạt hành động.
- **Người chơi luôn xem lại được** build / trang bị / buff hiện tại.

### 9.6 Thanh tiến độ (HP, năng lượng, XP)
- **Trông chắc, bo góc.** Cùng thông tin dùng cùng màu ở mọi nơi.
- **Lớp trễ (bóng sát thương) chỉ chạy khi giảm.** Max đổi thì nhảy ngay; lúc nghỉ thì ẩn.
- **Thanh của vật phụ** chỉ hiện khi đổi (~1–1.5 s), biến mất cùng vật, giới hạn số lượng.
- **Sự kiện đặc biệt** (vỡ khiên, đầy năng lượng) có animation ngắn riêng.

### 9.7 Shop, thẻ, danh sách
- **Item là nhân vật chính:** hình lớn, cỡ item đồng đều, chữ lớn, ít chỉ số.
- **Nhãn khớp trạng thái thật;** không gợi ý thừa.
- **Bấm item nhận đúng item đó với đúng giá đó.** Giá hiện đúng ngay từ đầu; giá tăng theo độ hiếm.
- **Trạng thái rõ:** đã mua (mờ + ✓), đang chọn (sáng + viền + ✓), khoá (xám + lý do).
- **Refresh / reroll không bao giờ ra kết quả y hệt;** hết lượt thì khoá và nói vì sao.

### 9.8 Icon và tiền tệ
- **Icon có màu, khối lớn.** Không icon trắng phẳng cho chức năng quan trọng hay thứ đang bán.
- **Chỉ một khung độ hiếm,** không viền chồng viền.
- **Tiền = icon + số,** không bọc thêm hộp.

### 9.9 Input
- **Ngón tay đang giữ từ màn trước không kích hoạt màn sau.** Nút quan trọng (tốn tiền, nhận thưởng, xem
  quảng cáo, đóng màn có hậu quả) khoá ~0.5 s sau khi hiện. Template **không** tự khoá lúc mở: `UIManager.Show`
  không gọi `EnableTouch(false)`, `AnimOpenUI` chỉ bật lại touch khi xong — màn tự khoá nút của mình
  (`interactable = false` rồi mở sau ~0.5 s, hoặc khi animation vào xong).
- **Debounce chạm nhanh** (không bao giờ trừ tiền hai lần): khoá nút / cờ đang xử lý từ lúc bấm tới lúc
  `PurchaseManager` / `RewardsService` trả kết quả.
- **Hành động dễ bấm nhầm mà có hậu quả** (quảng cáo, tiền thật, mất item) bắt xác nhận.

---

## 10. Onboarding / FTUE

- **Chơi ngay.** ≤ ~10 s từ lúc bấm Play tới thành công đầu tiên. FTUE ngắn, mỗi bước một hành động, không
  tường chữ hay hội thoại dài.
- **Kịch bản cố định, không ngẫu nhiên.** Thứ được dạy phải có tác dụng thấy được ngay.
- **Mỗi bước có feedback ngay.**
- **Không bao giờ kẹt:** bước hoàn thành bằng hành động thật; overlay không chặn hành động đang dạy; bước
  không thoả điều kiện thì tự bỏ qua; kill app giữa chừng mở lại thì tiếp đúng chỗ.
- **Lần đầu không hiện hình phạt.** Tutorial không che HUD quan trọng.
- **Mở tính năng dần theo tiến độ** (template: `UnlockFeature`). Dạy đúng lúc cần, một lần, một dòng.
  Không spam toast.
- **Không cơ chế ẩn.**
- **Thử với save mới trước mỗi build** `[human/qa]`.

---

## 11. Nhịp và cảm giác tiến bộ

- **Polish vòng lặp chính trước, chỉnh số sau.**
- **Không thời gian chết** (chờ spawn, chờ animation, sau khi thắng). Người chơi mạnh hơn thì nhịp nhanh hơn.
- **Mọi điểm tiêu** có ít nhất một lựa chọn vừa túi và hữu ích.
- **Popup lên cấp / thưởng** không chen ngang lúc căng thẳng và không dồn đống.
- **Thua vẫn có thưởng tối thiểu** và đường để mạnh hơn. Vào màn mới phải thấy mình mạnh hơn.
- **Không bao giờ phá đầu tư của người chơi.** Ghép / đổi / thay giữ nguyên giá trị.
- **Thứ hiếm hơn khó có hơn;** thưởng không bao giờ cảm giác keo kiệt.
- **Mọi con số hiển thị** (sức mạnh, gợi ý, dự đoán) dùng cùng công thức với giá trị thật.
- **Báo cáo cân bằng bằng biểu đồ / số đo.**

---

## 12. Monetization UX

- **Nút quảng cáo có màu + icon riêng.** Dễ bấm nhầm thì xác nhận trước; huỷ không bao giờ tốn lượt.
- **Không quảng cáo cho hành động lặp liên tục.** Thay bằng lượt miễn phí + giá tăng có trần.
- **Không interstitial ngay sau một chạm hay giữa hành động.**
- **Remove Ads có giá trị rõ.** Mọi rewarded ad đi qua một cổng chung (template: `AdsController`).
- **IAP:** không bán pack chưa giao được thưởng; ẩn SKU chưa tồn tại; có Restore Purchases; mua lỗi báo
  rõ (template: `PurchaseManager` / `InAppPurchase`).
- **Ưu tiên điểm chạm quảng cáo tự nhiên** (x2 thưởng, hồi sinh) hơn xem bắt buộc.
- **Shop theo quy ước của thể loại** (tả trung tính, không tên game): offer cùng một kiểu thẻ, có nhãn giá
  trị và đồng hồ hết hạn.

---

## 13. Hiệu năng và ổn định

- **Mục tiêu:** 60 fps (hoặc 30 fps ổn định trên máy yếu nếu đó là mục tiêu đã chọn); mọi khoảnh khắc
  ≤ 33 ms/frame; sau loading, tương tác thường không có frame ≥ 20 ms. Báo p95 + số frame vượt ngân sách.
- **Chi phí lần đầu trả ở loading:** warm-up shader; prewarm pool / popup / font / âm. Loading giữ tới khi
  xong; mỗi bước có timeout + log.
- **Trong gameplay:** không Instantiate / Destroy (dùng pool); không log / exception / alloc mỗi frame; UI
  vẽ lại theo cờ dirty.
- **Giới hạn đồng thời** cho FX, số bay, voice, vật thể. Chất lượng động theo máy, có hysteresis, không
  đổi cấp giữa chuyển cảnh.
- **Tối ưu không được gây nháy / pop.**
- **Texture và outline lấy mẫu ở cỡ hiển thị.** Kiểm độ nét 1:1 trên máy thật `[human]`.
- **Đo trước khi kết luận nguyên nhân.**
- **Phiên dài, nền lâu, resume** vẫn mượt. Gián đoạn (cuộc gọi, thông báo, mất mạng) không mất tiến độ.
- **Loading:** thanh tiến độ thật, mất mạng thì thử lại, không treo mãi.
- **Thử trên máy yếu mục tiêu;** theo dõi RAM, nhiệt, pin `[human]`.

---

## 14. Quy trình polish

- **Trước khi bắt đầu:** đọc mọi feedback, kể cả ảnh / video; tham chiếu chuẩn thể loại + ArtStyle §8;
  thay đổi mang tính hệ thống thì dựng mock A/B thật + đưa 1 khuyến nghị (dev chọn — xem §2.6).
- **Nộp duyệt:** before / after chụp trong game cùng điều kiện; cỡ thật (1:1) + bản phóng; UI gồm các ngôn
  ngữ chính; tự chấm + một reviewer độc lập chấm toàn bộ trước khi nộp (`ui-visual-reviewer`,
  `code-reviewer`…).
- **Duyệt trong pipeline tự chủ:** agent được commit / push (backlog vẫn chạy), nhưng thay đổi người chơi
  thấy **chưa phải là "đã duyệt"** cho tới khi dev duyệt: màn mới / làm lại vào
  `ArtStyle/screens/pending/` + bảng "Chờ duyệt" ArtStyle §8; dev duyệt → `approved/`, dev chê → §9.
  Không báo xong khi chưa push / build xong.
- **Build gate:** chơi từ save mới qua onboarding + vòng lặp đầu tiên; 0 lỗi console; fps đạt mục tiêu;
  regression pass; layout pass ở bộ thiết bị chuẩn (§9.1). Fail build khi: màn có lối vào mà thiếu nội
  dung, file ngôn ngữ rỗng, asset / VFX lệch chuẩn.
- **Bug:** root cause → sửa → quét cùng loại → test chống tái phát → ghi vào danh sách regression. "Không
  phải bug" thì giải thích vì sao và sửa tài liệu cho khớp build.
- **Code:** số hiển thị dùng cùng hàm với logic; không đọc dữ liệu trong constructor / initializer; override
  lifecycle gọi `base`; số lớn dùng kiểu không tràn.
- **Tham số chỉnh được để ở config.** Mỗi lần sửa kèm số đo. Hỏi trước khi xoá bất cứ thứ gì.

---

# PART II – MODULE

Chỉ áp khi game khai module ở `project-profile.json` → `polish.modules`.

## M1. Combat / hành động thời gian thực (`combat`)

- **Hitstop:** đòn thường ~50–70 ms, chí mạng ~100 ms, trúng nhiều mục tiêu ~50 ms. Freeze cả màn chỉ cho
  mốc lớn. Mọi nguồn chung một ngân sách.
- **Rung màn hình:** chỉ khi va chạm thật; đòn thường rung nhẹ hoặc không. Đòn nặng rung có hướng ~7–14 px
  (canvas ngang 1080). Tối đa ~1 lần / 0.25 s, không cộng dồn; có toggle.
- **Gói va chạm:** loé trắng ~0.06 s + đẩy lùi + squash + bắn tung + số bật lên + âm trầm.
- **Hướng vũ khí theo kiểu đánh:** đâm / bắn xoay mũi về mục tiêu; chém / vung chỉ lật ngang.
- **Lướt / lao là chuyển động thật,** không nhảy cóc. Có bóng mờ + vệt; lướt gây sát thương thêm hiệu ứng xé gió.
- **Dễ đọc:** nhân vật chính nổi nhất (outline / màu riêng, loé trắng khi bị đánh); địch tách khỏi nền, boss
  có không gian riêng; nguồn sát thương luôn thấy được; địch ngoài màn có chỉ báo; mục tiêu cuối có marker.
- **AI đúng vai:** đánh xa giữ khoảng cách, đánh gần áp sát. Tự ngắm 360° theo khoảng cách.
- **Telegraph ≥ 0.5–0.6 s;** vùng cảnh báo = vùng sát thương.
- **Boss không bị khống chế liên hoàn:** hiệu lực giảm dần + miễn nhiễm ngắn.
- **Clear = mọi địch thật sự hết.** Spawn hiện trong tầm nhìn, có cảnh báo.
- **Trận mở đầu có nhịp chuẩn bị:** đếm ngược hoặc chờ 1–1.5 s.
- **Slow-mo chỉ cho đòn kết cuối / boss chết.**

## M2. Item, vũ khí, nhân vật có cấp và tiến hoá (`tiers`)

- **Nhìn là đoán được chức năng.** Không vật trừu tượng; tên khớp vật; hiệu ứng chỉ có khi vật gợi ra tự nhiên.
- **Mỗi item một cách dùng riêng;** hai cái trùng thì bỏ một.
- **Mỗi cấp đổi thấy được ở cỡ thật** bằng thay đổi có lý: thêm bộ phận, đổi tỷ lệ, đổi chất liệu. Không mạ
  vàng / kim cương đồng loạt.
- **Nhánh tiến hoá** khác nhau ở silhouette và lối chơi, không bao giờ yếu hơn cấp trước.
- **Ghép / hợp nhất tạo vật mới** mang đặc điểm của cả hai nguồn. Không dán ghép; khác rõ bản gốc, có màn reveal riêng.
- **Độ hiếm cao có VFX / khoảnh khắc riêng.** Item yếu được làm lại về cơ chế, cảm giác và số.
- **Mỗi bonus bị động chỉ tăng 1 chỉ số.** Nhiều đường nâng cấp để người chơi chọn build.

## M3. Màn sinh thủ tục (`procgen`)

- **Thứ tự theo seed có ràng buộc:** không nội dung khó ngay đầu hay ngay trước boss; không đi quá lâu mà
  không có điểm tiêu.
- **Sự kiện ngẫu nhiên:** chỉ rút sự kiện hợp lệ lúc đó; luôn có Bỏ qua; xử lý nhanh (≤ ~8 s); đánh đổi
  rõ; EV có trần.
- **Kết quả xấu phải thật** (không "−0"); thưởng không dùng được thì hoàn lại.
- **Sự kiện trong màn** dùng đạo cụ thật trong scene, không popup trần.
- **Chế độ phụ có mục tiêu rõ:** chơi nhanh / thoả mãn dễ lan truyền / lạ và bất ngờ. Dựa trên cơ chế
  quen thuộc của thể loại (tả trung tính); tránh concept chung chung.

## M4. Gacha / mở thưởng (`gacha`)

- **Hồi hộp có nhịp:** rung → bùng theo độ hiếm → lật. Cho chạm để bỏ qua.
- **Thưởng to và rõ;** thông tin tỷ lệ gọn nhưng luôn mở được.

---

# LỖI HAY GẶP NHẤT — KIỂM TRƯỚC

1. Vật lơ lửng, bóng lệch, không bám đất.
2. Chữ tràn / chồng / lệch tâm; thiếu glyph.
3. Méo khi lật / xoay / scale.
4. Khựng lần đầu (vào màn, mở popup, hiệu ứng đầu tiên); giật khi nhiều vật.
5. Marker / vòng / outline thừa hoặc lệch tâm.
6. VFX ảnh tĩnh, lệch style hoặc lệch chủ đề.
7. Chủ thể chính bị UI che; sai thứ tự sort.
8. Animation đổi tay / lật hướng / trượt chân / nháy khi đổi trạng thái.
9. Popup chật, bố trí nút sai, HUD phí chỗ.
10. Thanh HP / tiến độ sai logic hoặc lệch màu.
11. Đi xuyên vật, kẹt vào đồ trang trí.
12. Đổi ngoài phạm vi, đổi lặng lẽ không before / after.

---

# CHECKLIST TRƯỚC REVIEW / PUSH

Mỗi dòng có ID cố định để gate trích. **Mức:** `block` / `warn` / `human` (xem § Cách áp dụng).
**Ai kiểm:** `spec` = `ui-spec-validator.py` (tự động) · `build` = builder tự chấm (`/new-ui` Phase C/D,
`/refactor-ui`) · `ui` = `ui-visual-reviewer` (ảnh tự chụp) · `code` = `code-reviewer` · `perf` =
`performance-reviewer` · `qa` = `qa-verifier` / `/auto-test-device` · `human` = dev trên máy thật.
Dòng không áp cho task (không đụng phần đó) → `na`, không phải `pass`.

| ID | Câu hỏi | § | Mức | Ai kiểm |
|---|---|---|---|---|
| **Art và nội dung** | | | | |
| P01 | Theo art bible (ArtStyle.md §11 pass); nhất quán với icon / splash / nhân vật? | §1.1, §2.1 | block | build, ui |
| P02 | Vật gameplay / icon mới qua thử silhouette ở cỡ thật? | §2.2 | warn | ui |
| P03 | Biến thể / cấp khác nhau rõ (silhouette, không chỉ màu)? | §2.2, M2 | warn | ui |
| P04 | Không thông tin nào truyền chỉ bằng màu (luôn kèm icon / hình / chữ)? | §2.3 | block | ui |
| P05 | Asset: padding ≥ 4 px, pivot đúng, import ≥ cỡ hiển thị, giữ GUID, không trùng tên? | §2.4 | warn | build, code |
| P06 | Không méo khi lật / xoay / scale; sprite dài lặp thay vì kéo giãn? | §2.4 | block | ui |
| **Animation và VFX** | | | | |
| P07 | Clip loop không giật; tay / hướng đúng; không trượt chân? | §3 | warn | ui, human |
| P08 | Logic không chờ animation: state / tiền / item ghi ngay lúc hành động, đóng view giữa beat vẫn cấp đúng một lần? | §3, §14 | block | code |
| P09 | VFX nhiều lớp, khớp hành động + chủ đề, đúng layer, không che chủ thể chính / chữ / CTA? | §4 | block | build, ui |
| P10 | Vật đứng có bóng tiếp đất, không lơ lửng, sort theo chân? | §8 | block | ui |
| **Cảm giác** | | | | |
| P11 | Mọi thao tác chính có feedback hình + tiếng (+ rung khi hợp) — không có nút bấm im lặng? | §1.3, §6 | block | build, code, ui |
| P12 | Không nháy / khựng khi chuyển cảnh, camera, mở lần đầu (đã prewarm)? | §1.6, §13 | block | perf, qa |
| P13 | Nhận / mua theo nhịp giữ – loé – bay; khoảnh khắc lớn xếp lớp? | §6 | warn | build, ui |
| **UI** | | | | |
| P14 | Bám lưới 8 px, khoảng cách đều; lề màn ≥ 48; CTA cao 152–176? | §9.1 | warn | spec, ui |
| P15 | Chữ không bao giờ < 30 px (30–31 chỉ cảnh báo, chuẩn ≥ 32); vùng chạm ≥ 128 px (hoặc vùng raycast vô hình)? Chrome của template (nút đóng / back / tiêu đề, `CheatMenu`) do template quyết, không chấm. | §9.1 | block | spec, build, ui |
| P16 | Không tràn / chồng / lệch chữ ở mọi ngôn ngữ (VI + EN + một ngôn ngữ dài) và mọi tỷ lệ chuẩn §9.1; chừa 30%? | §9.1, §9.2 | block | spec (warn), build, ui |
| P17 | Tối đa 1 CTA primary; màu nút theo vai trò; cặp nút nằm ngang cùng chiều cao? | §9.3 | block | spec, ui |
| P18 | Nút khoá = xám + lý do; bấm nút khoá không tốn tiền / lượt? | §9.3 | block | code, ui |
| P19 | Thiếu tài nguyên hiện rõ trên nút, cập nhật realtime; bấm thì rung + dẫn tới chỗ kiếm / mua? | §9.3 | block | code, qa |
| P20 | Không nút chết, không "Coming soon" trong build? | §9.3 | block | code, qa |
| P21 | Popup vừa nội dung; chạm ngoài để đóng (`ClickBackgroundToExit` bật trên prefab, trừ ngoại lệ §9.4); Back Android = nút đóng / back? | §9.4 | block | code, qa |
| P22 | Hành động có hậu quả (mất item, tiền thật, quảng cáo dễ bấm nhầm) nói rõ hậu quả + xác nhận? | §9.4, §9.9 | block | code |
| P23 | Màn / danh sách trống có hình + một dòng hướng dẫn? | §9.4 | block | build, ui |
| P24 | HUD gọn, không phí chỗ, không phần tử lạc (debug / version) — trừ `CheatMenu` của template (chỉ hiện khi bật cheat)? | §9.5 | block | ui |
| P25 | Thanh tiến độ: cùng thông tin cùng màu; lớp trễ chỉ chạy khi giảm? | §9.6 | warn | code, ui |
| P26 | Trạng thái item rõ: đã mua (mờ + ✓) / đang chọn / khoá + lý do? | §9.7 | block | build, ui |
| P27 | Số: rút gọn K / M / B, kiểu không tràn, không âm / "0s" / "+0" / "−0", không lặp? | §9.2, §14 | block | code |
| P28 | Đổi ngôn ngữ cập nhật ngay mọi màn đang mở (label tĩnh có localize, label động set lại)? | §9.2 | block | code, qa |
| P29 | Input: nút quan trọng khoá ~0.5 s sau khi hiện (template không tự khoá); debounce — không trừ tiền / cấp thưởng hai lần? | §9.9 | block | code |
| **Rõ ràng** | | | | |
| P30 | Không cơ chế / ký hiệu khó hiểu; số hiển thị dùng cùng hàm với giá trị thật? | §1.4, §11, §14 | block | code, ui |
| P31 | Onboarding chạy từ save mới không kẹt? | §10 | block | qa, human |
| **Hiệu năng** | | | | |
| P32 | Đạt fps mục tiêu ở cảnh nặng nhất; không Instantiate / Destroy / alloc / log mỗi frame trong gameplay; prewarm xong ở loading? | §13 | block | perf |
| P33 | Resume / gián đoạn / mất mạng không mất tiến độ? | §13 | block | code, qa |
| **Monetization** | | | | |
| P34 | Nút quảng cáo có màu + ▶ riêng; không interstitial ngay sau chạm; mọi rewarded qua `AdsController`? | §12 | block | code |
| P35 | Không bán pack chưa giao được thưởng; có Restore Purchases; mua lỗi báo rõ? | §12 | block | code |
| **Âm thanh** | | | | |
| P36 | Settings có Nhạc, SFX, Rung (+ rung màn hình nếu có); rung tôn trọng toggle? | §5 | block | code, qa |
| P37 | Không rè / clip / cắt cụt; âm lặp có biến thể; độ to ~−16 đến −18 LUFS? | §5 | human | human |
| **Quy trình** | | | | |
| P38 | Có before / after cùng điều kiện; màn mới / làm lại vào ArtStyle `screens/pending/`? | §14 | block | build |
| P39 | Đúng phạm vi task, không tự bù số / tự thêm luồng? | §1.8 | block | code |
| P40 | Bug: root cause + quét cùng loại + test chống tái phát? | §1.10, §14 | warn | code |
