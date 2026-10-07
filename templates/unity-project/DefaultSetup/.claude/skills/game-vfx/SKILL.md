---
name: game-vfx
description: Quy chuẩn VFX 2D, 3D cho game mobile của team (GameVFX_QuyChuan.md, module Game VFX), thư viện hiệu ứng dùng chung (FXEffect, shader EZG/VFX/Particle) và bộ tạo VFX flipbook vẽ 100% bằng code (Pillow, 19 recipe × 9 nguyên tố, QA, dựng prefab Unity tự động, render duyệt), dùng cho mọi project Unity. Dùng khi tạo, làm, hỏi hoặc duyệt hiệu ứng (trúng đòn, nổ, chém, bắn, đạn, niệm phép, vùng, buff, hồi máu, khiên, aura, thiên thạch, cảnh báo, xuất hiện, chết, môi trường) hoặc hạt trên UI canvas (UIParticle); ParticleSystem, flipbook / texture sheet, erosion, shader VFX, Max Particles, stop action, pool, sorting layer, texture, material ab / add, overdraw, ngân sách mobile; lấy hiệu ứng thư viện; quét nợ VFX cả project; kể cả khi user không nhắc quy chuẩn. Trigger: "tạo vfx …", "làm fx …", "tạo hiệu ứng nổ / hit", "make an explosion VFX", "optimize particles for mobile", "review this VFX prefab". Không dùng cho tween, rung, loé màn hình, số sát thương UI (UI Motion), animation nhân vật, hitstop (Game Animation).
---

# Game VFX: làm theo quy chuẩn

Luật nằm trong `GameVFX_QuyChuan.md`; hiệu ứng có sẵn và cách dùng nằm trong `GameVFX_ThuVien.md`. File này chỉ nói cách
đọc, cách làm, cách kiểm. Luật đổi thì sửa quy chuẩn, không sửa skill. Tạo hiệu ứng flipbook mới bằng code: mục 4.

Skill này dùng cho mọi project và không chứa thông tin riêng của project nào. Điều chỉ đúng cho một game (bảng sorting layer
và order, màu phe, shader được dùng, thư viện UI particle đã chọn, máy thử theo tier, đường spawn và pool, quyết định lệch
quy chuẩn) nằm trong file của chính game đó: xem mục 2. Không dùng trí nhớ về game khác: tên, số, quyết định của game A sai
ở game B.

`<skill>` dưới đây là folder chứa file này.

## 1. Tìm và đọc

1. Glob `**/GameVFX/Docs/GameVFX_QuyChuan.md` trong project: có thì đọc bản đó (đúng bản module của project). Không
   thấy: đọc bản đi kèm skill, `<skill>/docs/GameVFX_QuyChuan.md` (cùng bản với skill, không cần cài module chỉ để đọc
   luật). `docs/` là bản chép `Docs` của module, không sửa ở đây. Vẫn không thấy thì dừng và báo user, không làm theo trí
   nhớ. Cần `FXEffect`, shader chung hay hiệu ứng của thư viện mà project chưa có module: gói `GameVFX` ở tab Unity
   Packages của Feature Hub (chỉ phần lõi, xem mục 3).
2. Đọc từ đầu tới hết mục 0: phiên bản, cách hiểu các con số (mặc định, *(đề xuất)*, *(đo)*), bảng "Bạn là / Đọc trước",
   bảy nguyên tắc gốc.
3. Grep `^#{2,3} ` trong file để lấy danh sách mục kèm số dòng. Chọn **mọi** mục dính tới việc, không chỉ mục trùng tên
   việc: một hiệu ứng trúng đòn dính cấp (2.1), yếu tố chính / phụ (2.2), timing (3.1), giờ game (3.4), motion của hạt
   cho hiệu ứng va chạm (3.7), texture (4), màu (5), trần theo cấp (6.2), prefab và ParticleSystem (7.1–7.3), tên (8.2), checklist (9.3). Đọc các mục đó bằng Read với
   offset và limit.
4. Gặp dẫn chéo ("theo 6.2", "mục 3.4") mà mục đó ảnh hưởng tới việc đang làm thì đọc luôn.
5. Việc lớn (duyệt cả bộ hiệu ứng của một game, đưa module vào game, đặt ngân sách cả game) thì đọc hết file.
6. Cần lý do của một luật: `GameVFX_NguyenLy.md`. Hiệu ứng có sẵn, `FXEffect`, pool, UIParticle: `GameVFX_ThuVien.md`
   (ảnh xem trước của từng hiệu ứng ở `Previews~/<key>.jpg` cạnh file, Read xem được). Cả hai cùng thư mục với quy chuẩn.

## 2. Điều riêng của game

- Đọc brief VFX của game (quy chuẩn 9.2) và các quyết định đã ghi, nếu project có. Project đã có chỗ để brief thì dùng chỗ
  đó; chưa có thì đề xuất `ProjectSettings/GameVFXProject.md` (bảng sorting layer và order, màu phe, shader được dùng, thư
  viện UI particle, máy thử theo tier, đường spawn và pool, quyết định lệch quy chuẩn kèm ngày và lý do), hỏi user rồi mới
  tạo.
- Điều mới về game (user chốt một lựa chọn, lệch quy chuẩn có lý do) ghi vào file đó. Không ghi vào skill, cũng không ghi
  vào folder module: module thay nguyên folder khi nâng bản.

## 3. Làm

- Hiệu ứng mới: chốt cấp trước (2.1, GD xếp), rồi mới tới timing, trần, hình, màu. Ghi cấp đã chọn vào câu trả lời.
- Prefab hiệu ứng mới **luôn tạo bằng tool**, không dựng cây tay (khuôn 7.1, tên 8.2, 8.4): flipbook vẽ bằng code thì bộ tạo
  ở mục 4 (prefab dựng xong từ spec); còn lại (hạt, texture có sẵn, shader) thì script tạo khung rồi dựng tiếp trong Unity:

  ```bash
  node <skill>/scripts/vfx_new.mjs <đường dẫn>/fx_<nhóm>_<tên>.prefab --layers impact_add,glow_ab_sec,ring_add [--dry-run]
  ```

  Ra cây root (Stop Action Callback khi project có `FXEffect`, không có thì Disable: 7.1) → `containers` → các lớp, thiết
  lập theo 7.2, lớp `_add` gán `_mat_add`, lớp `_ab` gán `_mat_ab` (tìm trong project; chưa có thì tạo). Không truyền
  `--layers` thì một lớp `impact_add`. Tên sai luật thì script từ chối và nói lý do; đã có file cùng tên thì không ghi đè.
  Chạy `--dry-run` trước để xem cây và material sẽ dùng. Xong thì chỉnh tiếp trong Unity (texture, timing, motion 3.7) và
  kiểm bằng mắt (mục 6). Vai lớp chỉ lấy trong bảng 8.4; cần vai mới thì sửa bảng 8.4 và `ROLES` trong script, không đặt tên
  ngoài bảng.
- Xem thư viện trước khi làm mới: danh mục ở `GameVFX_ThuVien.md` mục 3 (nhóm, cấp, dùng cho 2D / 3D, ảnh). Có hiệu ứng
  gần đúng thì dùng thẳng, hoặc chép prefab sang folder VFX của game rồi sửa (tên theo 8.2, material mới nếu đổi màu). Không
  sửa prefab, material, texture trong folder module.
- Cần flipbook phát sáng mới (trúng đòn, nổ, cast, vùng…) ở game có dùng frame-by-frame: skill `create-vfx` vẽ sheet bằng
  code và dựng prefab theo khuôn 7.1 / tên 8.2, 8.4; luật vẫn theo quy chuẩn này.
- Prefab của thư viện chỉ có khi project chứa `Library/World`, `Library/UI` của module (repo phát triển module; Feature Hub
  chỉ phát gói lõi `FXEffect` + shader vì thư viện chứa file của pack mua). Project không có thì **không dừng việc, không hỏi
  nguồn**: danh mục `GameVFX_ThuVien.md` chỉ để tham khảo dáng, cấp, timing; làm hiệu ứng bằng bộ tạo ở mục 4 (flipbook vẽ
  bằng code) hoặc tự dựng hạt theo khung `vfx_new.mjs`. Chỉ nhắc một dòng trong báo cáo rằng thư viện không có trong project;
  không tự tìm nguồn khác. Việc user giao đích danh "dùng / thay bằng hiệu ứng của thư viện" thì mới hỏi đường dẫn repo module.
- Chọn cách dựng hình: hạt + texture tĩnh, flipbook (Texture Sheet Animation chạy khung, 4.4; sinh bằng mục 4), hoặc một
  hình ăn mòn bằng shader (4.7: Erosion, UV Scroll, Mask, Ramp). Cả ba đều hợp lệ, trộn được trong một hiệu ứng. Game đã chốt
  hướng trong brief (9.2) thì theo brief.
- Hai kiểu trộn lớp (8.4): `_ab` là lớp **nền** (alpha blend, `_mat_ab`, đọc được trên nền sáng lẫn tối); `_add` là lớp cộng
  sáng phủ **lên trên** (`_mat_add`), chỉ thêm khi lớp ab chưa đủ sáng. Không có hiệu ứng chỉ toàn lớp add.
- Code: mỗi hiệu ứng là một prefab tự báo xong: có module GameVFX thì `FXEffect` trên root (Stop Action Callback), pool thu
  lại khi `Finished`; không có module thì root Stop Action Disable, pool thu lại ở `OnDisable`. Phát bằng bật object hoặc
  `Play()`, không hẹn giờ tắt; hiệu ứng lặp thì nơi gọi dừng (quy chuẩn 6.8, 7.1, 7.8; mẫu `ObjectPool` ở `GameVFX_ThuVien.md` mục 2). Game có sẵn đường spawn riêng thì
  nối vào đường đó, không viết đường thứ hai.
- Hiệu ứng va chạm (trúng đòn, nổ, va đập): dựng motion theo 3.7 (tia, mảnh vỡ: tốc độ đầu lớn + Limit Velocity), không tự chế
  bằng `Speed Modifier`. Loại khác (đạn bay, vùng, buff, lặp, môi trường) chỉ lấy từng cách làm của 3.7 khi hợp, không áp cả mục.
- Shader của pack hay thư viện: đọc source shader trước khi đoán nghĩa một property. Tên property không nói nó tính thế nào.
- Đổi hướng lớn (đổi shader, dựng lại cả hiệu ứng): làm prefab **mới** (`_v2`), không ghi đè tại chỗ, không xoá asset của bản
  cũ cho tới khi user chốt bỏ. User có thể muốn lấy lại bản cũ.
- Biến thể (đổi màu, đổi cỡ) hoặc làm tiếp trên bản user đã sửa tay: **chép** prefab và material rồi chỉ ghi đè đúng phần cần
  đổi. Không dựng lại từ đầu: dựng lại là xoá mất chỉnh tay của user.
- Áp đúng luật đã đọc, kèm số mục ("theo 6.2", "gate V-3") để user dò lại được. Số ghi *(đề xuất)* thì nói là đề xuất.
- Việc dính câu hỏi còn mở (12.2): nêu các lựa chọn, để user chốt.
- Muốn làm khác quy chuẩn: nói lệch ở mục nào và vì sao, để user quyết. Không lặng lẽ làm khác. Thấy quy chuẩn tự mâu thuẫn
  hoặc thiếu: nói ra, không tự chọn một bên.

## 4. Tạo flipbook vẽ bằng code

Sinh hiệu ứng mới khi thư viện không có cái hợp: flipbook vẽ 100% bằng code (Pillow, không model ảnh, không tốn tiền) — lõi
trắng nóng, glow nướng vào alpha, tia kim, vòng mảnh, hạt sáng — rồi dựng prefab Unity từ spec JSON theo khuôn 7.1, 8.2, 8.4.
Style riêng của game nằm ở `.claude/docs/ArtStyle.md` §6b của project (nếu có); dải màu trong `vfx_recipes.py` chỉ là điểm
xuất phát.

`M = python <skill>/scripts/make_vfx.py` (macOS / Linux `python3`), chạy từ gốc project. Cần Python 3 + `numpy`, `Pillow`,
`scipy` (`pip install numpy pillow scipy`); `ffmpeg` chỉ cho video `grid`.

| Khi | Đọc |
|---|---|
| Luôn luôn | Quy chuẩn theo mục 1: tối thiểu 2.1, 3.1, 4.4, 6.2, 7.1–7.3, 8.2, 8.4, 9.3; brief VFX của game (mục 2) |
| Luôn luôn | ArtStyle.md §0, §6b, §9 của project nếu có; `$M config` in cấu hình đã đọc từ §6b |
| Luôn luôn | [reference/style-guide.md](reference/style-guide.md): hình bộ vẽ tạo ra, nhịp, điều cấm |
| Request khớp recipe | [reference/recipes.md](reference/recipes.md) §1–§2 (map request → recipe, tham số) |
| Dáng mới, không recipe nào khớp | recipes.md §4 + [reference/painter-api.md](reference/painter-api.md) |
| Dựng prefab, debug Unity, gắn vào gameplay | [reference/unity-integration.md](reference/unity-integration.md) |

**Cổng trước khi làm**
1. **Style:** ArtStyle.md chưa có hoặc §6b trống → game chưa có hướng VFX. Game đã có FX đang ship → khai `ref`, `grounds` ở
   §6b (Bootstrap của ArtStyle). Project trắng → hỏi user hướng VFX; user đồng ý dùng look mặc định thì ghi vào §6b.
2. **Sorting layer:** `$M config` → `sortingLayers`. Layer chưa có trong project → đề xuất tên (mặc định `FX_Ground` dưới
   nhân vật, `FX` trên), user đồng ý thì `GameVfxPrefabBuilder.EnsureSortingLayers("FX_Ground", "FX", "<layer nhân vật>")` và
   ghi vào §6b + brief. Không tự thêm layer, không để `Default` (7.3, V-14).

**Kết quả:** prefab `fx_[<subject>_]<nhóm>_<tên>.prefab`, sheet `FX_TX_<Tên>_<cột>x<hàng>.png`, spec `<Base>.gamevfx.json` ở
`<vfxRoot>/<Base>/` (`vfxRoot` trong `.claude/project-profile.json`, mặc định
`Assets/_Project/Visual/ArtAsset/Shared/VFX/Generated`; `--out` để đặt chỗ khác). Sprite chung `FX_TX_Spark.png`,
`FX_TX_Mote.png` ở `<vfxRoot>/_Shared/`. Kit Unity ở `<sourceRoot>/Editor/GameVfxKit/` (asmdef `Ezg.GameVfx.Editor`). Ảnh
duyệt ở `Temp/GameVfx/<Base>/` (git-ignore). `<nhóm>` theo 8.2, mặc định theo recipe, đổi bằng `--group`; `--subject hero`
cho hiệu ứng của một đối tượng. Không để prefab trong `Resources/` trừ khi game nạp nó thật (8.3.1).

**Quy trình**
0. **Request → recipe + nguyên tố + tên** (recipes.md §1). Nguyên tố: lời user → `default` của §6b → mặc định recipe; ngoài
   `allow` của §6b thì hỏi. Ghi **cấp** (2.1); chọn `--size/--life` trong khung timing của cấp (3.1) và trần (6.2).
1. **Kit (idempotent):** `$M install` (`--showcase` nếu cần scene showcase) và `$M shared`. File `.cs`/asmdef mới hoặc đổi →
   `Assets/Refresh` → chờ compile → `unity_get_compilation_errors`. Cần package `com.unity.2d.sprite`; `install` báo nếu thiếu.
2. **Vẽ (chỉ Python):** `$M make --recipe <r> --element <e> --name <tên> [--subject --group --size --life --frames --px --seed]`.
   Đọc **QA**: không ship khi còn `CLIPPED`, `POP_AT_END`, `JUMP`, `LOOP_SEAM`, `SLOW_START` (bảng sửa recipes.md §5); dòng
   `old` = file không thuộc spec nào, xoá khi GUID không còn ai tham chiếu. Tự mở `Temp/GameVfx/<Base>/<Base>.sheet.png` (nửa
   trên nền tối nhất, nửa dưới nền sáng nhất) và xem; lệch style thì sửa, chạy lại.
3. **Dựng prefab** (Unity MCP `unity_execute_code`, không recompile):
   ```csharp
   var p = Ezg.GameVfx.EditorTools.GameVfxPrefabBuilder.Build("<vfxRoot>/<Base>/<Base>.gamevfx.json");
   return p + "\n" + string.Join("\n", Ezg.GameVfx.EditorTools.GameVfxPrefabBuilder.LastWarnings) + "\n" + Ezg.GameVfx.EditorTools.GameVfxPrefabBuilder.Describe(p);
   ```
   `Describe` in cây (root → `containers` → lớp `<vai>_<ab|add>`), stop action, max particles, sorting, material: soát với
   7.1–7.3, 8.4. `LastWarnings` khác rỗng thì xử lý hoặc báo. Dựng lại tất cả: `BuildAll("Assets")` / menu
   `Tools/GameVFX/Rebuild All Specs`. Không có Unity MCP → dừng ở bước 2, báo `prefab: chưa dựng` kèm menu
   `Tools/GameVFX/Build Prefab From Spec...`.
4. **Render thật** (mục 6): mỗi nền trong `reviewGrounds` của spec →
   `GameVfxPrefabBuilder.RenderFrames(prefab, "<abs>/Temp/GameVfx/<Base>/render_<i>", ortho, camY, 60, life + 0.15f, 360, 7, "<#nền>")`
   (ortho / camY: unity-integration.md §6) → `$M contact --frames <dir> --out Temp/GameVfx/<Base>/<Base>.contact_<i>.png` → mở
   xem. `$M compare --names <Base> --kinds <kind> --out Temp/GameVfx/<Base>/<Base>.compare.png` đặt FX mới cạnh FX đã duyệt
   (`ref` ở §6b) — **cổng style**; chưa có `ref` thì cổng style là user duyệt ảnh, nói rõ. Tuỳ chọn: video `$M grid`,
   `BuildShowcase(...)`.
5. **Thay hiệu ứng cũ:** chạy lại `make` cùng `--name/--subject/--group` → ghi đè tại chỗ, giữ `.meta` và GUID. Prefab user đã
   sửa tay: không dựng đè, chép `_v2` (mục 3). Lồng vào prefab đối tượng: unity-integration.md §7.

**Luật của bộ tạo**
- Chỉ Pillow, không model ảnh; báo cáo ghi "vẽ 100% bằng code".
- Style lấy từ game (§6b + `ref`), không từ trí nhớ. User duyệt / chê một hiệu ứng → cập nhật ArtStyle.md ngay (duyệt: thêm
  `ref`; chê: §9 kèm lý do + luật "Cấm" ở §6b). Không ghi giá trị style của game vào skill.
- Không sửa C# khi thêm VFX: mọi thứ qua recipe (Python) và spec (JSON). Chỉ sửa builder khi thêm *loại lớp mới*, sửa ở
  `<skill>/unity/` rồi `install` (bản trong `Assets/` bị ghi đè).
- Không sửa dáng recipe đã duyệt khi user không yêu cầu (được sửa lỗi QA). Biến thể: `--element/--size/--life/--seed` hoặc
  recipe mới.
- Lớp `ab` là nền (glow nướng vào alpha); lớp `add` tuỳ chọn, thêm bằng khoá `blend: "add"` ở lớp / extras của recipe khi ab
  chưa đủ sáng trên nền tối nhất (8.4). Lớp chính của recipe luôn `ab`.
- Không có frame blending (shader Mobile): hiệu ứng dài hơn 0.6 s mà giật thì tăng `--frames` (≈ 25–30 fps theo life), vẫn
  trong trần texture 6.2.
- Chưa Play-test thì nói rõ là chưa.

## 5. Quét VFX của cả project

Chỉ đọc file (chạy được khi Unity đang mở project), cần Node 18+:

```bash
node <skill>/scripts/vfx_scan.mjs <gốc project> <thư mục kết quả> --packs "<đoạn đường dẫn folder pack>,..."
node <skill>/scripts/report.cjs <thư mục kết quả>
node <skill>/scripts/tex_report.cjs <thư mục kết quả>
```

- `--packs`: đoạn đường dẫn (chữ thường) của các folder pack mua sẵn trong project, để tách hiệu ứng của pack khỏi hiệu ứng
  của game. Không truyền thì chỉ nhận các folder `Plugins`, `ThirdParty`, `Samples`, `Asset Store`, `Demo`, `Examples`.
- "Vào build" = đi được theo GUID từ scene đang bật trong Build Settings và folder `Resources`: có thể nạp, chưa chứng minh
  game gọi tới.
- Ghi `effects.csv` (mỗi hiệu ứng một dòng), `systems.csv`, `materials.csv`, `textures.csv`, `summary.json`. Project cỡ
  5000–6500 prefab mất khoảng 1 phút; project rất lớn chạy `node --max-old-space-size=8192`.
- Đọc số theo luật: `maxParticles` để 1000 (7.2, gate V-3), stop action None (6.8, V-10), hiệu ứng gameplay chạy giờ thật
  (3.4, V-13), Always Simulate (6.5, V-11), prewarm (7.2, V-12), sorting layer `Default` (7.3, V-14), texture nguồn > 2048
  hoặc không override Android (4.2, 4.5, V-5), material thiếu (V-6), `sheetAnim` > 0 (system chạy khung thật; `sheets` gồm
  cả sheet chỉ chọn hình tĩnh): đối chiếu trần texture 6.2 và hướng của brief (9.2). Báo là ứng viên kèm đường dẫn prefab, không phải kết
  luận.

## 6. Kiểm bằng mắt

Script quét (mục 5) không thấy được hiệu ứng trông ra sao. Làm xong hay sửa xong một hiệu ứng thì render ra ảnh rồi xem.

- Cách render trong Unity Editor: mở một scene trống **additive** (không đụng scene user đang mở), instantiate prefab,
  `ParticleSystem.Simulate(t, true, true)` trên root cho từng mốc `t`, camera render vào RenderTexture, ghi PNG, đóng scene
  không lưu.
- Bảng khung theo thời gian: 5–12 mốc phủ đủ chuẩn bị, bùng, tan (3.1), camera đúng góc gameplay của game.
- Hai nền: tối nhất và sáng nhất của game (5.5.1). Additive mất trên nền sáng.
- Bản tắt yếu tố phụ `_Sec` (2.2, 6.4): vẫn phải đọc ra hiệu ứng.
- Ảnh đen trắng của pha đỉnh và pha tan (2.3.7).
- Màu hay độ sáng sai: **render từng lớp riêng** trước khi chỉnh số. Nhiều lớp additive chồng nhau làm lệch màu ở ảnh tổng,
  nhìn ảnh tổng không biết lớp nào gây ra; tách ra thường thấy ngay.
- Xem prefab user (hay người khác) đã sửa tay: in **mọi** module đang bật của từng system (hoặc so YAML với bản trước), không in
  theo danh sách tự chọn. Thiếu một module là đọc sai ý người sửa.

## 7. Video tham khảo

- User đưa video ref: lấy khung bằng skill `watch` nếu có, hoặc ffmpeg. Hiệu ứng chỉ vài giây thì lấy dày, ghép thành một bảng:

  ```bash
  ffmpeg -i ref.mp4 -vf "fps=12,scale=300:-1,tile=6x7" sheet.png
  ```

  Vài khung rải đều (mặc định của các tool lấy khung) không đủ đọc motion.
- Ghi nhịp đọc được thành timing (chuẩn bị, bùng, tan, mảnh vỡ, khói) và các lớp trước khi dựng. Video có ảnh tham khảo khác
  màu, khác hình thì ghi rõ lấy gì từ video (nhịp, motion) và gì từ ảnh (màu, hình).

## 8. Bẫy đã gặp

- `Frame over Time` của Texture Sheet Animation trải 0–1 trên cả sheet, không phải số thứ tự khung. Lấy khung `i` của sheet `n`
  khung bằng hằng số `(i + 0.5) / n`.
- `Max Particle Size` cắt cụt hạt to theo tỉ lệ màn hình, không thu nhỏ. Hạt chính bị cắt thì giảm `Start Size` thật, đừng hạ
  trần này cho nó.
- `useAutoRandomSeed` và `randomSeed` nằm trên `ParticleSystem`, không trên `main`.
- Module của `ParticleSystem` là struct: `var m = ps.main; m.startSize = …;`. Gán thẳng `ps.main.startSize = …` không biên
  dịch.
- Shader có Color Ramp tra theo độ sáng: kiểm xem độ sáng đó đã nhân alpha chưa (vd AllIn1Vfx: luminance × alpha rồi cộng
  `_ColorRampLuminosity`). Hình alpha thấp thì không bao giờ chạm đầu sáng của ramp; nhiều lớp additive chồng nhau thì vượt
  qua cả dải màu thành trắng.

## 9. Trước khi trả lời xong

- Soát phần khớp với việc trong checklist duyệt (9.3), gate (10) và Định nghĩa "xong" (11). Thiếu gì thì bổ sung vào câu
  trả lời.
- Đã kiểm bằng mắt theo mục 6; mục nào chưa làm được (Unity không mở, không render được) thì nói rõ.
- Cuối câu trả lời ghi một dòng: phiên bản quy chuẩn, các mục đã áp, file riêng của game đã đọc / sửa.
