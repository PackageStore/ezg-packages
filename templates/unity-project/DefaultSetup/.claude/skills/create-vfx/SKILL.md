---
name: create-vfx
description: Tạo VFX prefab cho game bằng flipbook vẽ 100% bằng code (Pillow, không AI ảnh, không tốn tiền) — lõi trắng nóng, glow bão hoà nướng vào alpha, tia kim, vòng mảnh, hạt sáng — rồi dựng prefab Unity tự động từ spec JSON theo khuôn của quy chuẩn game-vfx (root điều khiển → containers → lớp <vai>_ab[_sec], tên fx_<nhóm>_<tên>, texture FX_TX_<Tên>_<cột>x<hàng>, material chung _mat_ab, FXEffect khi project có module GameVFX, pool-friendly). 19 recipe (impact ×2, muzzle ×2, explode ×2, cast ×2, slash, đạn orb loop, aura loop, buff EXP, heal zone, shield, aura bất tử, time-slow, ground slam, meteor nhiều sheet, blade storm vùng xoay + dao bay) × 9 nguyên tố + dải màu riêng của game khai ở ArtStyle.md §6b, QA tự động, render thật trong Unity, so style với FX đã duyệt. Dùng khi user nói "tạo vfx …", "làm vfx/fx …", "tạo hiệu ứng …", "vfx nổ / explode", "vfx trúng đòn / hit / impact", "vfx bắn / muzzle", "vfx niệm phép / cast", "vfx chém / slash", "vfx đạn / projectile / orb", "vfx aura / buff / vùng / hồi máu / khiên", "vfx thiên thạch / meteor", "vfx lốc kiếm / blade storm / dậm đất", "create vfx", "make an explosion / hit / muzzle flash effect". KHÔNG dùng khi game chọn không frame-by-frame (brief game-vfx 4.7 → làm bằng hạt + shader theo skill game-vfx), cho hạt trên UI canvas, tween/rung/loé màn hình (UI Motion) hay animation nhân vật (Game Animation).
---

# Create VFX: flipbook vẽ bằng code → prefab Unity theo khuôn game-vfx

Skill này **sinh** hiệu ứng mới. Luật VFX của team (cấp, timing, ngân sách mobile, pool, sorting, tên) nằm ở quy chuẩn
của skill `game-vfx`; style riêng của game nằm ở `.claude/docs/ArtStyle.md` §6b. Skill này không chứa giá trị style của
game nào: dải màu trong `vfx_recipes.py` chỉ là điểm xuất phát, game chốt dải của mình ở §6b.

`<skill>` = `.claude/skills/create-vfx`. `M = python3 <skill>/scripts/make_vfx.py` (Windows: `py`), chạy từ root project.
Cần Python 3 + `numpy`, `Pillow`, `scipy` (`pip install numpy pillow scipy`); `ffmpeg` chỉ cho video `grid` (tùy chọn).

## Đọc trước

| Khi | Đọc |
|---|---|
| Luôn luôn | Quy chuẩn `game-vfx` theo cách skill `game-vfx` mục 1 chỉ: tối thiểu 2.1 (cấp), 3.1 (timing), 4.4 (flipbook), 4.7, 6.2 (trần), 7.1–7.3, 8.2, 8.4, 9.3. Brief VFX của game (`ProjectSettings/GameVFXProject.md` nếu có) |
| Luôn luôn | `.claude/docs/ArtStyle.md` §0, §6b (VFX), §9 (hướng đã loại) + rule `art-style`. `$M config` in ra cấu hình đã đọc từ §6b |
| Luôn luôn | [reference/style-guide.md](reference/style-guide.md): hình mà bộ vẽ này tạo ra, nhịp, những gì cấm |
| Request khớp recipe có sẵn | [reference/recipes.md](reference/recipes.md) §1–§2 (map request → recipe, tham số) |
| Cần dáng mới, không recipe nào khớp | recipes.md §4 (viết recipe mới) + [reference/painter-api.md](reference/painter-api.md) |
| Dựng prefab / debug Unity / gắn vào gameplay | [reference/unity-integration.md](reference/unity-integration.md) |

## Cổng trước khi làm

1. **Game có dùng frame-by-frame không** (brief của game, quy chuẩn 9.2 / 4.7). Brief ghi *không* → dừng, skill này
   không áp dụng: làm theo `game-vfx` 4.7 (hạt + shader ăn mòn). Brief chưa ghi → hỏi dev một lần, rồi ghi vào brief.
2. **Style:** ArtStyle.md `Status: template` hoặc §6b trống → chưa có hướng VFX của game. Game đã có FX đang ship → làm
   § Bootstrap của ArtStyle (khai `ref`, `grounds` ở §6b), `Status: draft`. Project trắng chưa có FX nào → hỏi dev hướng
   VFX trước (đây là quyết định của dev); dev đồng ý dùng look mặc định của skill thì ghi điều đó vào §6b.
3. **Sorting layer:** `$M config` → `sortingLayers`. Layer chưa có trong project (template mặc định không có layer VFX) →
   đề xuất tên với dev (mặc định `FX_Ground` dưới nhân vật, `FX` trên), dev đồng ý thì
   `VfxPrefabBuilder.EnsureSortingLayers("FX_Ground", "FX", "<layer của nhân vật>")` và ghi vào §6b dòng `layers:` + brief.
   Không tự ý thêm layer, không để VFX ở `Default` (7.3, gate V-14).

## Kết quả

| Gì | Ở đâu |
|---|---|
| Prefab `fx_[<subject>_]<nhóm>_<tên>.prefab`, sheet `FX_TX_<Tên>_<cột>x<hàng>.png` (+ sheet lớp, sprite bay), spec `<Base>.vfx.json` | `<vfxRoot>/<Base>/` — `vfxRoot` trong `.claude/project-profile.json` (mặc định `Assets/_Project/Visual/ArtAsset/Shared/VFX/Generated`); `--out` để đặt chỗ khác (vd `Visuals/VFX` của một feature) |
| Sprite dùng chung `FX_TX_Spark.png`, `FX_TX_Mote.png` | `<vfxRoot>/_Shared/` |
| Kit Unity (builder Editor + asmdef) | `<sourceRoot>/Editor/VfxKit/` (`Ezg.VfxKit.Editor`) |
| Ảnh duyệt (sheet trên nền tối + sáng, contact, compare) | `Temp/CreateVfx/<Base>/` — git-ignore, dùng xong bỏ |

- `<nhóm>` theo bảng 8.2 (`hit`, `proj`, `muzzle`, `slash`, `cast`, `skill`, `aoe`, `buff`, `heal`, `shield`, `aura`…):
  mặc định theo recipe, đổi bằng `--group`. `--subject hero` cho hiệu ứng của một đối tượng (`fx_hero_cast_fireball`).
- Prefab không để trong `Resources/` trừ khi game nạp nó thật (8.3.1).

## Quy trình

**0. Hiểu request → recipe + nguyên tố + tên.** Bảng map ở recipes.md §1. Nguyên tố: lời dev → `default` của §6b →
mặc định của recipe; nguyên tố ngoài `allow` của §6b thì hỏi dev. Ghi **cấp** của hiệu ứng (2.1) vào câu trả lời và chọn
`--size/--life` trong khung thời gian của cấp (3.1) và trần (6.2). Chỉ hỏi lại khi không suy ra được loại hiệu ứng.

**1. Kit Unity (idempotent):**
1. `$M install` (thêm `--showcase` nếu cần scene showcase) và `$M shared`.
2. `.cs`/asmdef mới hoặc đổi → trước khi Refresh gọi `unity_editor_state` + `unity_agents_list` (session khác đang
   Play thì chờ) → `Assets/Refresh` → chờ compile → `unity_get_compilation_errors` (rule compile-validation).
3. Cần package `com.unity.2d.sprite` (API slice); `install` báo nếu thiếu.

**2. Vẽ flipbook (chỉ Python, không đụng Unity):**
1. `$M make --recipe <r> --element <e> --name <tên> [--subject <s>] [--group <g>] [--size --life --frames --px --seed]`.
2. Đọc **QA**. Không ship khi còn `CLIPPED`, `POP_AT_END`, `JUMP`, `LOOP_SEAM`, `SLOW_START`; bảng sửa ở recipes.md §5.
   Dòng `old` = file trong thư mục không thuộc spec nào (lưới cũ, lớp đổi tên): xoá khi GUID không còn ai tham chiếu.
3. **Tự mở** `Temp/CreateVfx/<Base>/<Base>.sheet.png` (nửa trên nền tối nhất, nửa dưới nền sáng nhất của game) và xem.
4. Lệch style-guide hoặc §6b thì sửa và chạy lại. Vòng này rẻ, lặp tới khi đạt.

**3. Dựng prefab** (Unity MCP `unity_execute_code`, không recompile):
```csharp
var p = Ezg.VfxKit.EditorTools.VfxPrefabBuilder.Build("<vfxRoot>/<Base>/<Base>.vfx.json");
return p + "\n" + string.Join("\n", Ezg.VfxKit.EditorTools.VfxPrefabBuilder.LastWarnings) + "\n" + Ezg.VfxKit.EditorTools.VfxPrefabBuilder.Describe(p);
```
`Describe` in cây prefab (root → `containers` → lớp), stop action, max particles, sorting, material: soát với 7.1–7.3,
8.4. `LastWarnings` không rỗng (layer thiếu, material lạ) thì xử lý hoặc báo. Dựng lại tất cả: `BuildAll("Assets")` hoặc
menu `Tools/VFX/Rebuild All Specs`. Không có Unity MCP → dừng ở bước 2, báo
`prefab: chưa dựng (Unity MCP không kết nối)` kèm menu `Tools/VFX/Build Prefab From Spec...`.

**4. Kiểm bằng render thật** (game-vfx mục 5):
1. Với **mỗi** nền trong `reviewGrounds` của spec:
   `VfxPrefabBuilder.RenderFrames(prefab, "<abs>/Temp/CreateVfx/<Base>/render_<i>", ortho, camY, 60, life + 0.15f, 360, 7, "<#nền>")`
   (ortho/camY ở unity-integration.md §6) → `$M contact --frames <dir> --out Temp/CreateVfx/<Base>/<Base>.contact_<i>.png` → mở xem.
2. `$M compare --names <Base> --kinds <kind> --out Temp/CreateVfx/<Base>/<Base>.compare.png`: FX đã duyệt của game (`ref` ở
   §6b) cạnh FX mới — **cổng style**. Chưa có `ref` nào thì cổng style là dev duyệt ảnh: nói rõ trong báo cáo.
3. Tùy chọn: video `$M grid ...` (cần ffmpeg), scene showcase `VfxPrefabBuilder.BuildShowcase(...)`.
   Âm đi kèm hiệu ứng: skill `create-sfx` (`--prefab <prefab FX> --play-on-enable` sau khi thêm `SoundPlayController`).
4. Soát checklist 9.3 của quy chuẩn phần khớp việc (cấp, timing, trần, tên, sorting, stop action, pool).

**5. Thay hiệu ứng cũ** (khi dev bảo làm lại / thay một FX đang dùng):
- Chạy lại `make` cùng `--name/--subject/--group` → file cùng tên được ghi đè tại chỗ (giữ `.meta`, giữ GUID), prefab dựng
  lại tại chỗ (giữ GUID, chỗ đang tham chiếu prefab vẫn trỏ đúng). Prefab dev đã sửa tay: không dựng đè — chép ra `_v2`
  (game-vfx mục 3), để dev chọn.
- Lồng prefab mới vào prefab của đối tượng thay cho particle cũ: unity-integration.md §7. File `old`: grep GUID trong
  `Assets/`, không còn tham chiếu mới xoá (`AssetDatabase.DeleteAsset`); còn thì giữ và báo.

**6. Báo cáo** (rule output-format): file kết quả (prefab, sheet, spec) và file đã xoá; ảnh duyệt trong `Temp/CreateVfx`
để dev tự xem; recipe × nguyên tố, cấp, size/life, QA, warning của builder, bước đã bỏ qua (chưa Play-test, không có
Unity, chưa có `ref` để so). Cuối cùng một dòng: phiên bản quy chuẩn game-vfx, các mục đã áp, §6b đã đọc / sửa.

## Luật

- **Chỉ Pillow.** Không dùng model ảnh nào. Báo cáo khẳng định "vẽ 100% bằng code".
- **Style lấy từ game, không từ trí nhớ:** ArtStyle §6b + FX đã duyệt (`ref`). Dev duyệt / chê một hiệu ứng → cập nhật
  ArtStyle.md ngay trong lượt (duyệt: thêm `ref`; chê: §9 kèm nguyên văn lý do + luật "Cấm" ở §6b), theo rule `art-style`.
  Không ghi giá trị style của game vào skill này.
- **Không sửa C# khi thêm VFX.** Mọi thứ đi qua recipe (Python) và spec (JSON). Chỉ sửa `VfxPrefabBuilder` khi thêm *loại
  lớp mới*, sửa ở `<skill>/unity/` trước rồi `install` (bản trong `Assets/` bị `install` ghi đè).
- **Không sửa dáng các recipe đã duyệt** khi dev không yêu cầu; được sửa lỗi QA. Biến thể dùng
  `--element/--size/--life/--seed` hoặc recipe mới.
- Tên lớp chỉ lấy vai trong bảng 8.4 (`role` trong recipe). Cần vai mới → sửa quy chuẩn trước (skill `game-vfx`).
- Không có frame blending (shader Mobile), nên độ mượt phụ thuộc số frame: hiệu ứng dài hơn 0.6 s mà giật thì tăng
  `--frames` (≈ 25–30 fps theo life), vẫn trong trần texture 6.2.
- Prefab one-shot tự báo xong: có `FXEffect` → stop action Callback, pool nghe `Finished`; không có module GameVFX → stop
  action Disable, pool thu lại ở `OnDisable` (`PoolingManager`). Không hẹn giờ tắt. Prefab lặp thì người gọi dừng.
- Chưa Play-test thì nói rõ là chưa.
