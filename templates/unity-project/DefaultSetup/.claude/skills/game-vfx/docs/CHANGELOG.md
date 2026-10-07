# CHANGELOG — Game VFX

Quy chuẩn: `GameVFX_QuyChuan.md`. Nền lý thuyết và nguồn: `GameVFX_NguyenLy.md`. Thư viện hiệu ứng: `GameVFX_ThuVien.md`.

## 0.2.5 — 2026-10-07

Gộp bộ tạo flipbook vẽ bằng code vào skill `game-vfx`; flipbook hợp lệ trở lại.

- Quy chuẩn 0.2.5: bỏ luật "game không dùng frame-by-frame". 4.4 không còn dòng "bỏ mục này"; flipbook là một trong ba cách
  dựng hình (hạt + texture tĩnh, flipbook, hình ăn mòn bằng shader), trộn được. 4.7 đổi thành kỹ thuật tuỳ chọn ("Hình ăn mòn
  bằng shader"), luật 5 cho giữ flipbook khi hình đổi nhiều qua các khung. Brief 9.2 bỏ mục "có dùng frame-by-frame hay không".
- Stop action (7.1 luật 1, cây 7.1, bảng 7.2, 6.8.3, V-10): root Callback khi project có module GameVFX (`FXEffect`), không có
  thì Disable (pool thu lại ở `OnDisable`); không bao giờ None. Hiệu ứng lặp: nơi gọi dừng.
- 8.4: `ab` là lớp **nền** (glow vẽ sẵn vào alpha, đọc được trên nền tối lẫn sáng); `add` là lớp cộng sáng đặt lên trên, tuỳ
  chọn, chỉ khi ab chưa đủ sáng. 8.2: script tạo khung và bộ tạo flipbook cùng nhận `add`, `ab`.
- Skill `game-vfx`:
  - Mục mới 4 "Tạo flipbook vẽ bằng code": painter Python (`scripts/fxpaint.py`, `vfx_recipes.py`, `make_vfx.py`: 19 recipe ×
    9 nguyên tố, QA `CLIPPED` / `SLOW_START` / `POP_AT_END` / `JUMP` / `LOOP_SEAM` / `WASTED_SPACE`, render / contact / compare /
    grid), tài liệu `reference/` (painter-api, recipes, style-guide, unity-integration). Đọc ArtStyle.md §6b và `vfxRoot` của
    `.claude/project-profile.json` của project.
  - Kit Unity riêng: `unity/GameVfxPrefabBuilder.cs`, `GameVfxShowcaseLoop.cs`, asmdef `Ezg.GameVfx.Editor` (namespace
    `Ezg.GameVfx`), cài vào `<sourceRoot>/Editor/GameVfxKit/`; menu `Tools/GameVFX/…`; spec `*.gamevfx.json`; ảnh duyệt
    `Temp/GameVfx/`.
  - Kiểu trộn theo từng lớp: khoá `blend` (`ab` mặc định / `add`) ở lớp, extras (`_sparks`, `_motes`), trail, stream của recipe
    và ở spec; builder gán `_mat_ab` / `_mat_add` (tìm theo tên như `vfx_new.mjs`, chưa có thì tạo Mobile/Particles; spec có thể
    chỉ rõ `material` / `materialAdd`), đặt tên lớp `<vai>_<ab|add>`; lớp add không chỉ rõ order thì order 11 (trên lớp ab).
    19 recipe giữ nguyên dáng (toàn ab).
  - `vfx_new.mjs`: Stop Action của root theo việc project có `FXEffect.cs` (Assets/, Packages/, Library/PackageCache/):
    Callback hoặc Disable.
  - Mô tả skill viết lại, gồm trigger tạo VFX ("tạo vfx …", "make an explosion VFX"): `game-vfx` là lối vào chính. Mục 3 bỏ
    luật cấm frame-by-frame, thêm chọn cách dựng hình và hai kiểu trộn; mục 5 (quét) đọc `sheetAnim` theo trần 6.2 và brief.
    Các mục sau đánh số lại (Kiểm bằng mắt là mục 6).
  - Bỏ eval `08-khong-frame-by-frame`.
- Thư viện: ghi chú 16 hiệu ứng đã đổi khỏi frame-by-frame thành lịch sử 0.2.2; các hiệu ứng giữ nguyên.
- **Ghi chú:** skill `create-vfx` (nếu project còn) không đổi: vẫn có cổng frame-by-frame, kit `Ezg.VfxKit`, spec `*.vfx.json`.
  Hai bản painter chạy độc lập; sửa recipe ở `game-vfx` không sang `create-vfx`.

## 0.2.4 — 2026-10-07

Khuôn prefab và script tạo khung.

- Quy chuẩn 0.2.4, khuôn prefab mới lấy từ prefab hiệu ứng găng đã ship của team: root điều khiển (Stop Action Callback) → một
  node `containers` (không phát hạt, renderer tắt, Stop Action None) → các lớp (7.1, bảng 7.2).
- Tên (8.1, 8.2, 8.4): prefab, tên con, material đổi sang snake_case chữ thường (`fx_<nhóm>_<tên>`, `impact_add`,
  `_mat_add`). Lớp theo mẫu `<vai>(_<vai>)*_<add|ab>(_<số>)?(_sec)?`: vai trong bảng cố định ở 8.4, kiểu trộn bắt buộc,
  yếu tố phụ đuôi `_sec` (trước là `_Sec`; sửa theo ở 2.2, 2.7, 6.4, 9.2, 11). Material dùng chung theo kiểu trộn `_mat_add`,
  `_mat_ab`; material riêng `mat_<tên>_<kiểu trộn>`. Nhóm và hậu tố prefab viết thường. Texture, mesh, shader, atlas giữ
  `FX_TX_`, `FX_SM_`, `FX_SH_`, `FX_AT_`.
- Skill `game-vfx`: script `scripts/vfx_new.mjs` (Node 18+, không cần mở Unity) tạo prefab khung đúng khuôn từ mẫu YAML do Unity
  6000.3 sinh (`scripts/templates/`). Kiểm tên prefab, tên lớp, trùng lớp; không ghi đè; tìm `_mat_add` / `_mat_ab` trong
  `Assets/` (trùng tên thì lấy bản được prefab VFX dùng nhiều nhất), chưa có thì tạo (shader `Mobile/Particles`). `SKILL.md` mục
  3: prefab mới luôn tạo khung bằng script.
- **Nợ:**
  - `FXEffect.LowQuality` còn tìm đuôi `_Sec`: phải sửa để nhận `_sec` (không phân biệt hoa thường) trước khi dùng tier Thấp
    với hiệu ứng theo khuôn mới.
  - 67 hiệu ứng của thư viện còn theo khuôn và tên cũ (`GameVFX_ThuVien.md` 4).
  - Material tạo mới dùng `Mobile/Particles`: chỉ đúng ở Built-in, project URP phải thay shader.

## 0.2.3 — 2026-10-06

Motion của hạt cho hiệu ứng va chạm.

- Quy chuẩn 0.2.3: mục mới 3.7 "Motion của hạt: hiệu ứng va chạm", viết chủ yếu cho impact (trúng đòn, nổ, va đập); loại khác
  chỉ lấy từng cách làm khi hợp (bảng cách dựng: tia, mảnh vỡ bằng tốc độ đầu lớn + Limit Velocity; đầu tia
  bằng Stretched Billboard; khối mềm bằng `Speed Modifier`; nở bùng, sóng lan bằng Size over Lifetime; nhịp chuẩn bị bằng hạt
  tối; xoay ngẫu nhiên; chín luật). 3.2.1 trỏ sang 3.7.
- Limit Velocity: 6.5.2 thêm ngoại lệ cho burst ngắn ở điểm va chạm (không cần ghi lý do); 6.5.3 ghi số đo chưa tách burst /
  lặp, chưa nên tính hết vào nợ; bảng 7.2 tách Limit Velocity thành dòng riêng; gate V-9 trừ trường hợp này; checklist 9.3 thêm
  một dòng motion.
- Nguyên lý: lý do của 3.7 ở mục 4, thêm dòng 3.7 vào bảng luật và nguồn (mục 10).
- Skill `game-vfx`: mục 1 thêm 3.7 vào danh sách mục cần đọc; mục 3 thêm: motion theo 3.7, đọc source shader trước khi đoán
  property, đổi hướng lớn thì làm prefab mới, làm biến thể thì chép rồi chỉ đổi phần cần đổi;
  mục mới 5 "Kiểm bằng mắt" (bảng khung theo thời gian ở góc camera game, nền tối và sáng, tắt `_Sec`, tách từng lớp khi màu
  sai, đọc đủ mọi module khi xem prefab người khác đã sửa); mục mới 6 "Video tham khảo"; mục mới 7 "Bẫy đã gặp".

## 0.2.2 — 2026-10-01

Thư viện không frame-by-frame: chuyển động của hình làm bằng hạt và shader.

- Shader `EZG/VFX/Particle` thêm bốn tính năng, bật riêng từng cái (material không bật thì không tốn gì): Erosion (alpha của hạt
  là ngưỡng ăn mòn hình theo noise, có viền màu, noise trôi, lệch noise theo từng hạt qua stream StableRandom.x, dải ngưỡng
  Erosion Range), UV Scroll, Mask, Ramp (tô màu texture xám theo độ sáng).
- Quy chuẩn 0.2.2: mục mới 4.7 "Không frame-by-frame: hạt và shader" (cách làm khói, lửa, đĩa nổ, vòng sóng, vệt chém, đổi
  tông; bảy luật: alpha là ngưỡng, số lần đọc texture, noise dùng chung, stream ngẫu nhiên, hình vẽ tay, `_Time` lúc pause,
  duyệt). 4.4 trỏ sang 4.7 khi game không dùng frame-by-frame; 6.6.8 kể bốn tính năng của shader chung; brief của game (9.2)
  ghi có dùng frame-by-frame hay không.
- Thư viện: 16 hiệu ứng chạy khung (12 world, 4 UI; 24 system) đổi sang một khung đầy hình nhất + Erosion. Đĩa, vòng, sóng ăn
  mòn hướng tâm (đĩa khoét thành vòng, vòng mỏng dần từ trong ra); lửa có noise trôi lên; khói tan mềm. Texture dùng chung
  mới: `FX_TX_Noise_Erosion`, `FX_TX_Erosion_Radial`. 10 hiệu ứng có sheet chỉ để chọn ngẫu nhiên một hình tĩnh giữ nguyên.
  Ảnh xem trước dựng lại.
- Nợ mới: hình vẽ tay đổi nhiều qua các khung (lửa 2D, lõi thiên thạch, lửa UI) kém sống động hơn bản chạy khung, cần vẽ lại cho
  shader; texture dùng chung thêm vào làm 8 hiệu ứng vượt trần số texture (hiệu ứng trong trần: 28 thành 24).
- Tool dựng thư viện: bước `ConvertFrameByFrame` chạy sau khi chuẩn hoá (giữ sheet chạy khung bằng `-fxKeepFbf`); xuất gói lõi
  cho Feature Hub bằng `ExportCorePackage -fxPackage <file>`. Danh mục ghi số hiệu ứng đã đổi và tên từng hiệu ứng.
- Skill `game-vfx`: game không dùng frame-by-frame thì làm theo 4.7; câu thử mới `08-khong-frame-by-frame`. Script quét thêm
  cột `sheetAnim` (system chạy khung thật; `sheets` gồm cả sheet chỉ chọn hình tĩnh), và không còn bỏ qua folder tên `Library`,
  `Build`, `Temp`… trong `Assets` (bỏ qua đúng như Unity: tên bắt đầu bằng `.`, kết thúc bằng `~`): trước đây quét project
  có module này là sót cả thư viện. Sáu game đã khảo sát không có prefab nào trong các folder đó: số khảo sát không đổi.
- Gói `GameVFX` trên Feature Hub và skill `game-vfx` cập nhật theo (shader, quy chuẩn, tài liệu thư viện).

## 0.2.1 — 2026-09-30

Skill Claude `game-vfx` và gói lõi trên Feature Hub.

- `Skill~/game-vfx` (Unity bỏ qua folder `~`): skill đọc và áp quy chuẩn cho mọi project (tìm quy chuẩn của module trong
  project, không có thì đọc bản chép `docs/` đi kèm skill), dùng hiệu ứng của thư viện, quét VFX cả project; 7 câu thử
  (`evals/`). Không chứa thông tin riêng của project nào: điều riêng của game ghi ở brief của game (quy chuẩn 9.2), đề xuất
  `ProjectSettings/GameVFXProject.md`.
- Script quét VFX (`vfx_scan.mjs`, `report.cjs`, `tex_report.cjs`) chuyển từ tài liệu phát triển vào `scripts/` của skill:
  chỉ đọc file, Node 18+.
- Feature Hub: skill `game-vfx` ở tab AI Feature; gói `GameVFX` ở tab Unity Packages chỉ có phần lõi (`Runtime/FXEffect.cs`,
  shader `EZG/VFX/Particle`, tài liệu). Prefab, material, texture của thư viện không lên hub vì có file của pack mua (link
  của hub tải được không cần đăng nhập); lấy từ repo phát triển module.
- Quy chuẩn 0.2.1, trỏ tới skill và script quét (đầu tài liệu, 10, 12.3), và vá các chỗ chưa khớp mà lượt thử skill tìm ra:
  - Ví dụ tên ở 7.1, 8.2, 8.3, 9.2 khớp danh sách nhóm của 8.2: `FX_Skill_Fireball_Impact` thành `FX_Hero_Hit_Fireball`,
    `FX_Hero_Skill_Fireball_Cast` thành `FX_Hero_Cast_Fireball` (nhóm `Skill` chỉ dùng khi không tách được Cast / Proj / Hit).
  - 7.1: `Flash` thành `Flash_Sec` (loé là yếu tố phụ, 2.3.4). 8.4: `_Sec` luôn ở cuối tên (`Sparks_01_Sec`).
  - 3.1: pha bùng bắt đầu đúng frame va chạm và tới đỉnh trong 0–2 frame (trước đây câu dưới bảng nói đỉnh trùng frame va
    chạm, trái với bảng).
  - 6.2: cột System không tính root điều khiển; cạnh texture của sheet flipbook tính theo một khung; hiệu ứng UI lấy trần theo
    hai hàng UI, cấp 2.1 chỉ quyết mức sáng, đậm, quy mô trong hàng.
  - 6.4.3: giảm hạt × 0,5 ở tier Thấp không được làm mất hình của yếu tố chính.
  - 6.5.5, 7.5.6, V-14, V-17: hạt dưới UIParticle được miễn luật renderer tắt và sorting layer; tắt Raycast Target trên
    UIParticle.
- Thư viện: texture mà hiệu ứng 3D dùng bật mipmap (quy chuẩn 4.5, 58 texture; texture chỉ hiệu ứng 2D và UI dùng vẫn tắt);
  UIParticle của 16 hiệu ứng UI tắt Raycast Target (trước đây có thể chặn chạm vào nút nằm dưới). Tool dựng thư viện làm
  đúng hai việc này ở lần dựng sau. Tài liệu thư viện ghi rõ phần nào có trong gói trên hub, và nợ `LowQuality` chưa giảm hạt
  × 0,5.

## 0.2 — 2026-09-30

Thư viện hiệu ứng dùng chung, lấy từ hiệu ứng đã ship của các game của team; khảo sát thêm ba game 3D.

- `Library/`: 67 hiệu ứng đã chuẩn hoá theo quy chuẩn, 51 world (trúng đòn, chém, đầu nòng, đạn, vùng, hào quang, buff, trạng
  thái, xuất hiện, chết, môi trường, nhặt đồ; billboard dùng cho cả 2D lẫn 3D, vòng trên đất cho 3D, trạng thái vẽ phẳng cho 2D)
  và 16 trên UI canvas (UIParticle: merge, hoàn thành, dùng item, phá hộp, tăng tốc, mở khoá, loé icon tài nguyên, lửa). Mỗi hiệu ứng có root
  điều khiển với stop action Callback, chạy giờ game (world) hoặc giờ thật (UI), `Max Particles` theo số hạt thật, không script
  của game, không system chết, không tham chiếu treo. Texture cài ASTC theo trần của cấp, nguồn > 2048 đã thu về ≤ 2048, mesh
  `.blend` tách ra `.asset`. GUID mới cho mọi file: không đụng asset sẵn có của game import thư viện.
- Shader chung `EZG/VFX/Particle` (`Library/Shared/Shaders/FX_SH_Particle.shader`): unlit, bốn kiểu trộn, chạy cả Built-in lẫn
  URP, có stencil cho Mask; 50 material dùng chung.
- `Runtime/FXEffect.cs`: component trên root của hiệu ứng: `Play`, `Stop`, sự kiện `Finished` khi hạt cuối cùng tắt (cho pool),
  `whenFinished` (Disable / Destroy / None), `autoStopAfter`, `LowQuality` (tắt con `_Sec`), `DefaultSortingLayer`.
- `GameVFX_ThuVien.md`: cách dùng, danh mục có ảnh xem trước (`Docs/Previews~`), số đo từng hiệu ứng so với trần 6.2, nợ còn lại
  (39 / 67 vượt ít nhất một trần, chủ yếu material và texture: cần atlas), cách thêm hiệu ứng.
- Project module khai báo package UIParticle 4.13.2 (`com.coffee.ui-particle`) cho hiệu ứng UI.
- Quy chuẩn 0.2: trỏ tới thư viện, shader chung và `FXEffect` (6.6.8, 6.8.3, 7.1.6, 7.5.1, 7.8, 12); thêm số đo của ba game 3D ở
  chỗ khác đáng kể (Max Particles, stop action, sorting layer).
- Tool dựng thư viện (harvest, chuẩn hoá và render ảnh trong Unity, kiểm tham chiếu, sinh danh mục) và khảo sát ba game 3D là tài
  liệu phát triển của repo module (`Docs/GameVFX/`), không đi kèm module.

## 0.1 — 2026-09-30

Bản nháp đầu, chỉ có tài liệu (chưa có code, như Game Animation 0.1). Nguồn chính là hướng dẫn VFX công khai của một studio
MOBA PC lớn (bản lưu trong thư viện Visual Library của team), tài liệu công khai của studio đó, của một studio mobile lớn, một
publisher mobile lớn, của Unity, Arm, W3C, Microsoft (39 nguồn), và khảo sát ba game mobile đã ship của team.

- `GameVFX_QuyChuan.md`: bảy nguyên tắc gốc; đọc được (sáu cấp quan trọng, yếu tố chính / phụ, dải sáng và đậm theo mảng, vùng
  và hitbox, màu phe, cảnh báo, màn hình đông, chớp sáng và rung); timing ba pha và số theo cấp, giờ game / giờ thật, mood dùng
  chung trục với UI Motion và Game Animation; hình, texture, flipbook, import; màu (cấu trúc bảng màu, 12 bảng màu nguyên tố có
  hex, additive và bloom); ngân sách mobile theo cấp và theo tier máy, overdraw, culling, shader, material, pool; thiết lập
  Unity (prefab, ParticleSystem, renderer, sorting 2D, UI particle, âm thanh, pack mua sẵn, cửa gọi chung); đặt tên `FX_` và thư
  mục; quy trình, brief, checklist duyệt; gate V-1 … V-20 (đề xuất); định nghĩa xong; câu hỏi mở và kế hoạch pilot.
- `GameVFX_NguyenLy.md`: tóm tắt từng phần hướng dẫn VFX của studio MOBA PC, số đo trên biểu đồ dải sáng / đậm và 12 bảng
  màu (lấy mẫu pixel), chỗ lệch của bộ slide tiếng Việt trong thư viện; nguồn cho đọc được trên mobile, timing và va chạm, hiệu
  năng mobile, an toàn thị giác, quy ước sản xuất; tóm tắt khảo sát; danh sách nguồn và bảng luật → nguồn.
- Khảo sát ba game và script quét dùng cho nó là tài liệu phát triển của repo module (`Docs/GameVFX/`), không đi kèm module.
