# CHANGELOG — Game Animation

Quy chuẩn: `GameAnimation_QuyChuan.md`. Nền lý thuyết: `GameAnimation_NguyenLy.md`.

## 0.1.9 — 2026-09-30

Skill `game-animation` mang theo bản chép tài liệu của module trong `docs/` (quy chuẩn, nguyên lý, phân tích pack
ExplosiveLLC, CHANGELOG). Trước đây tài liệu chỉ có trong folder module: skill cài một mình (Feature Hub,
`~/.claude/skills`) ở project chưa có module không có quy chuẩn để đọc, mà muốn có thì phải cài cả module (kéo theo
AssetPostprocessor đổi tên xương Mixamo). Luật, code không đổi.

- `game-animation` đọc quy chuẩn theo thứ tự: `Docs` của module trong project, rồi `<skill>/docs/`, không có thì dừng.
  `blender-rig-animate` đọc bản của module, không có thì `docs/` của skill `game-animation`.
- `docs/` là bản chép, không sửa tay: repo phát triển module chép và kiểm bằng `Docs/tools/sync_skill_docs.mjs`
  (`--check` trước khi commit, cài skill, đẩy lên Feature Hub).

## 0.1.8 — 2026-09-30

Sửa chiều cao nhân vật của `check_anim` (mọi ngưỡng tính theo nó). Trước đây chiều cao lấy từ da mềm: nhân vật có da mềm
chỉ là cái áo (0,72 m trên 1,46 m) thì mọi ngưỡng chặt gấp đôi, PROP_UNDER_FLOOR báo nhầm 4 lỗi (khiên 1,5–2,5 cm dưới
sàn) và Die báo DROP_FAR ở 0,57–0,66 m.

- Chiều cao: `check_anim --height`, không có thì con số đã đưa cho `prepare --height` (giờ `prepare` lưu vào file,
  scene `rk_height`), không có nữa thì tư thế nghỉ của da mềm cộng mọi phần cứng trừ vũ khí / khiên cầm tay (`props`
  của rig spec): mặt, mắt, mũ tính vào. Báo cáo ghi `height_m` và nguồn.
- Kiểm: chibi ví dụ ra 1,607 m (tính mũ), lỗi giữ nguyên (rìu, mũ dưới sàn), không thêm cảnh báo; nhân vật tròn ra
  1,46 m, PROP_UNDER_FLOOR khớp đúng từng mục với gate sàn riêng của project đó (7 / 7 trên cùng một file).

## 0.1.7 — 2026-09-30

Ba thứ đưa ngược từ lần làm một nhân vật tròn không tay chân, kiếm và khiên lơ lửng bằng rigkit: lò xo theo đà, gate
vật cứng xuyên sàn, verify_fbx đo đúng đạo cụ rời tay. API cũ của rigkit giữ nguyên, chỉ thêm.

- Lò xo theo đà (follow-through, quy chuẩn 7.6.9): `rigkit/follow.py`, API `Clip.spring(bone, kind="turn"|"grip",
  gain, hz, zeta, max_deg, windows)` chạy trong bake sau pose và IK, trước drag / drop, trên hướng của xương trong
  armature space; `Clip.spring_channel(target, gain, hz, zeta, room, windows)` trên kênh của key. Khối treo trên đế di
  chuyển, cản theo vận tốc tương đối: đế đi đều thì không lệch, dừng gắt thì đi tiếp, vọt quá rồi lắng (mặc định 2,8 Hz,
  zeta 0,3). Ghi ngược thành góc giải phẫu bằng hiệu góc trước / sau khi xoay (wrap ±180), trong giới hạn khớp mềm, góc
  có trần mềm `max_deg`. Loop chạy 3 vòng lấy vòng cuối; clip thường có cửa sổ, mờ về 0 trước frame cuối nên pose trung
  tâm và khung chạm giữ đúng key. Cảnh báo SPRING_UNUSED khi lò xo không có gì để theo (kênh chỉ có wave, xương không có).
- Gate mới `PROP_UNDER_FLOOR` trong `check_anim`: mesh cứng (100% một xương: vũ khí, khiên, mũ, mặt) thấp hơn sàn > 2%
  chiều cao là lỗi; `GROUND` vẫn chỉ đo da mềm. Chạy lại chibi ví dụ: gate bắt rìu cắm sàn 8,3 / 24,5 cm ở 2 pose và
  24,8 cm ở clip Attack, mũ lún sàn 4,3 cm ở Die (lỗi thật, trước đây không gate nào đo); ví dụ chưa sửa.
- `verify_fbx`: gỡ nối (use_connect) mọi xương sau import rồi mới đo. Importer FBX của Blender nối con duy nhất vào đuôi
  xương cha và bỏ qua location của nó; Unity không có "nối". Clip Die có đạo cụ rơi xa: trước báo lệch 1,19 m, sau
  0,01 mm; key lệch 5 cm vẫn báo 0,050 m.
- Tài liệu: SKILL.md (luật 15, bước 8, 10), `reference/specs.md` (API lò xo, cửa sổ, kênh ảo phải có trong base, bảng
  mã gate), `failure-catalog.md` (mục E, dòng mới ở B, C, D), `rules.md` (nguồn [RB], A22–A24, X3b), quy chuẩn 5.10.
- Kiểm: `tests/selftest_follow.py` 25 mục (tỉ lệ vọt 0,369 so với lý thuyết 0,372; gain 0 bake y hệt; giới hạn mềm; bộ
  ba góc không chuẩn: 0,15° lò xo đổi góc < 0,16°; loop không có đường nối); `selftest_weights.py` qua; ví dụ chibi:
  anim, export, verify_fbx như cũ, thêm lò xo vào Idle và Interact: mọi gate 0 lỗi.

## 0.1.6 — 2026-09-30

Dùng chung cho mọi project: quy chuẩn, module và hai skill không mang thông tin riêng của game nào; điều riêng của một
game nằm trong file của game đó. Không đổi luật, không đổi code (rigkit chỉ sửa chú thích).

- Skill `game-animation` dời vào module (`Skill~/game-animation/`, trước ở `.claude/skills/` của repo), cạnh
  `blender-rig-animate`. Cài: chép thư mục skill vào `~/.claude/skills/` hoặc `.claude/skills/` của project. Project
  không có quy chuẩn thì skill đọc ở module nguồn (`<skill>/../../Docs`); vẫn không thấy thì báo cài module.
- `game-animation`: điều riêng của game (preset cảm giác, tên xương, cỡ nhân vật, bộ vũ khí, brief, quyết định lệch quy
  chuẩn) đọc và ghi ở file của game: brief 9.2, chưa có chỗ thì đề xuất `ProjectSettings/GameAnimationProject.md`.
- `blender-rig-animate`: bỏ tên project và đường dẫn máy (tìm Blender qua `BLENDER`, `where blender`, chỗ cài thường
  gặp). Spec của từng nhân vật (rig, skin, key pose, clip, `run.sh`) là file của project game, để cạnh model (ví dụ
  `<folder model>/Blender~/<Subject>_rigkit/`). Ví dụ đủ cũ ra khỏi skill, sang tài liệu phát triển của repo module (không
  đi kèm module); bài học của nó vẫn ở `reference/failure-catalog.md` và `proportions.md` ("chibi ví dụ"). Nguồn `[RK]`
  thay nguồn mang tên nhân vật trong `reference/rules.md`.
- Quy chuẩn (2.1, 5.3, 5.10, 12.2, 12.3), nguyên lý và ghi chép các bản trước bỏ tên project.
- Eval của `game-animation`: bản dựng thử bỏ mọi folder `Skill~` để nhánh không skill không thấy skill.
- Kiểm: selftest weight của rigkit qua; chạy lại cả chuỗi của chibi ví dụ từ chỗ mới: mọi gate 0 lỗi (cảnh báo như cũ),
  `verify_fbx` OK; code Editor của module compile trong một project khác cùng template (Unity 6000.3).

## 0.1.5 — 2026-09-28

Gate pose trung tâm của rigkit (`HUB_START`, `HUB_END`) làm đúng như gate A-22 của 0.1.4. Chỉ sửa code rigkit và tài liệu của nó.

- Pose trung tâm lấy theo bộ: clip so với Idle nền có tiền tố tên dài nhất trùng với nó (`Hero_Sword2H_Atk_01` →
  `Hero_Sword2H_Idle`); Idle nền là `<Subject>[_<Bộ>]_Idle[_01]`, idle break và `_Idle_Static` không tính.
- Nhóm Hit được lệch frame đầu: `Clip(..., hub_start=False)`, mặc định cho tên có `_Hit`. Không tìm thấy Idle nào thì
  cảnh báo `HUB_MISSING`.
- Thử trên bộ clip giả hai bộ (Idle, Sword2H_Idle, idle break, Hit bắt đầu lệch) và chạy lại cả chuỗi của chibi ví dụ: 0 lỗi.

## 0.1.4 — 2026-09-28

Tay nghề từ hai sách: Richard Williams, *The Animator's Survival Kit* (bản mở rộng, 2009) và Jonathan Annand, *Animation Craft
for 3D and 2D Animators* (2025). Chỉ có tài liệu, chưa thêm code.

- Đọc hết hai sách; mọi bảng, số frame đọc trên ảnh trang, không theo lớp chữ OCR. Số của sách tính ở 24 fps, quy chuẩn đổi ra
  30 fps và ghi số gốc.
- Quy chuẩn:
  - thuật ngữ mới: extreme, timing / spacing, ease, favor, accent, moving hold, take, overlap / follow-through, line / path of
    action, contact / down / passing / up, bước, trọng tâm, pose trung tâm, on ones / twos (1);
  - dòng 13 "trọng lượng và thăng bằng", nguyên lý Annand thêm; chỉnh luật và cách kiểm của 12 dòng cũ (2);
  - ones và twos, strobing khi code dời sprite, đổi số 24 fps ra 30 fps (3.1); lấy đà vô hình cho người chơi (3.2); luật lấy đà
    cho telegraph (3.3); frame va chạm là accent cứng, pose đã qua điểm chạm, rung kiểu stagger (3.4, 3.7, 7.5);
  - bảng nhịp đi, chạy lấy thẳng từ Williams (tr. 110 và chương Runs), thêm bước 20, 24, 32 và 3 frame, số frame bay khi chạy,
    số hình tối thiểu cho 2D (3.6);
  - loop: soi chỗ nối, đường về khác đường đi, bớt máy móc (7.1); chân trụ trượt đều, cách kiểm cycle tại chỗ, giới hạn tốc độ
    phát (7.2); `AE_Footstep` ở pose contact, hình không đi sau tiếng (7.4);
  - mục mới: pose, timing, spacing (7.6); đi, chạy, nhảy, thú bốn chân (7.7); trọng lượng, thăng bằng, đặt chân, pose trung
    tâm (7.8); mặt, lời thoại (7.9); rig cần có cho các luật đó (7.10);
  - quy trình: kế hoạch timing ở mốc 2, blocking là key + extreme + breakdown, spline là lượt rút gọn, polish làm từng lượt
    (9.1); brief thêm ý chính, trọng lượng, pose trung tâm, âm thanh (9.2); checklist thêm mục ở cả ba phần (9.3); cách góp ý
    khi duyệt (9.4);
  - gate A-20 chân trụ trượt đều, A-21 curve không gai, A-22 khớp pose trung tâm (cùng ngưỡng với gate của rigkit, 5.10)
    (10, 11, 12.3); câu hỏi mở 5–7 (12.2).
- Nguyên lý: dòng Williams, Annand trong từng nguyên lý, mục mới 2.13 trọng lượng và thăng bằng, thêm ý vào 4.2, 4.4, 4.6, 4.7,
  dòng Spacing ở mục 5, bảng nguồn mục 6, nguồn [44] [45] kèm số trang.

## 0.1.3 — 2026-09-28

Rig, skin, pose và animate bằng Blender headless có gate đo được: bộ script `rigkit` và skill Claude
`blender-rig-animate`. Có code Python chạy trong Blender, không có code Unity.

- `Skill~/blender-rig-animate/` (Unity bỏ qua): SKILL.md (quy trình từ khảo sát tới xuất FBX), `scripts/rigkit/` (inspect,
  prepare, measure, rig, skin, check_weights, rom, anim, check_anim, review, export, verify_fbx), selftest cho gate weight,
  tài liệu tham chiếu (luật kèm nguồn manual Blender vol 3 và video Ryan King [42] [43], catalog lỗi, định dạng spec, tỉ lệ
  chibi, ghi chú API Blender 4.x), ví dụ đủ một chibi cầm rìu và khiên (0.1.6 dời ra khỏi skill). Cài cho Claude Code:
  chép thư mục vào `~/.claude/skills/`.
- Làm lại một chibi cầm rìu và khiên. Bản rig bằng script tay không gate, đo lại có 55 lỗi (5 weight, 50
  animation). rigkit: 0 lỗi ở weight, ROM, 12 key pose, 5 clip; FBX lệch < 0,1 mm sau vòng xuất–nhập.
- Gate pose trung tâm (HUB_START, HUB_END) và biên độ (LOW_AMPLITUDE) hiệu chỉnh trên pack ExplosiveLLC, đo cùng thước với
  gate (`tests/measure_clips.py`: đòn lệch Idle trung bình 0–9°, idle 15–16°, đòn 42–86°), và trên một bộ clip chibi sinh
  bằng code team chê: pack qua sạch.
- Quy chuẩn: 5.3 thêm luật skin (không weight theo toạ độ, vùng chi, khoá da dưới phụ kiện, 4 influence ổn định, vai
  chibi, Preserve Volume, giới hạn khớp chốt bằng ROM); 5.8 thêm nguồn "key pose viết bằng code" (nhân vật chính vẫn key
  tay); mục mới 5.10; 12.3 thêm rigkit. Nguyên lý: nguồn [42] [43], dòng cho 5.3 và 5.10 ở mục 6.

## 0.1.2 — 2026-09-28

Nguồn 2D là file `.psd`, không dùng `.psb`, theo cách team đang làm. Chỉ có tài liệu, chưa thêm code.

- Unity mặc định import `.psd` bằng Texture Importer (ra ảnh phẳng, không rig được): 6.2 thêm dòng chọn importer
  `UnityEditor.U2D.PSD.PSDImporter`, gate A-11 kiểm importer, tool Preset import (12.3) sẽ tự đặt bằng
  `AssetDatabase.SetImporterOverride`.
- Đổi PSB thành PSD ở 1, 4.2, 4.5, 6.1, 6.2, 8.2, 11; thêm dòng quyết định vào 12.1.

## 0.1.1 — 2026-09-28

Học từ pack ExplosiveLLC (*RPG Character Mecanim Animation Pack FREE* 2.5.2). Chỉ có tài liệu, chưa thêm code.

- Tài liệu mới `GameAnimation_PackExplosiveLLC.md`: pack gồm gì, controller dựng thế nào (sub-state machine theo bộ vũ khí,
  Any State, layer thân trên, blend tree), code điều khiển ra sao (Actions API, root motion qua `OnAnimatorMove`, khoá theo
  giây, IK tay, rút / cất vũ khí), số đo, bảng học / sửa / bỏ, cách chuyển clip sang chuẩn.
- Đo bằng Unity 6000.3.16f1 batch trên bản chép project: tốc độ và pha chân của 43 clip loop, quãng dời, frame va chạm, độ trễ
  trong controller, và thử layer phụ với state chờ (Humanoid, Generic, Write Defaults bật / tắt, None / clip rỗng).
- Quy chuẩn 0.1.1: bố cục Animator Controller (bộ, nhóm, Exit về cha), luật state chờ, không state trống ở đường vào hành
  động, trigger mỗi nhóm + `Variant` + `Stance`, Any State và Can Transition To Self, blend tree 2D theo vận tốc thật (8.3);
  tại chỗ nghĩa là không có `OnAnimatorMove`, một nguồn dời, đo tốc độ gốc bằng tool (7.2); đoạn `<Bộ>` trong tên, nhóm
  `Equip`, `Grip_L`, socket cất vũ khí (4, 5.5); event `AE_Prop`, receiver có sẵn trong prefab (7.4); hitstop bằng
  `Animator.speed` (7.5); weight IK theo curve của clip (8.6); dùng pack mua sẵn (8.8); gate A-15 … A-19.
- Còn mở thêm (12.2): đường vào hành động (transition hay CrossFade), pack ExplosiveLLC dùng tới đâu.

## 0.1.0 — 2026-09-26

Bản đầu: quy chuẩn rigging và animation 2D / 3D, và tool đầu tiên.

- Quy chuẩn 0.1 và tài liệu 12 nguyên lý 0.1. Đã chốt: Blender, Unity 2D Animation, mọi clip tại chỗ (code dời nhân vật),
  T-pose, mood và combat theo loại game, mocap / Mixamo retarget lúc làm.
- `GameAnimMixamoImport`: lúc import model, đổi tên xương `mixamorig:*` sang tên chuẩn (quy chuẩn mục 5.9) và đổi clip
  `mixamo.com` thành tên file. Đổi ở bước `OnPostprocessMeshHierarchy`, trước khi Unity tạo avatar và clip, nên curve của
  clip bám theo tên mới. Tự chạy cho model trong `Art/Characters`, `Art/Props`, `Art/Vehicles`, `Art/Animation`; chỗ khác
  bật bằng menu Tools/Game Animation. Cờ giữ tên gốc thắng tất cả. Cờ lưu trong userData của model; userData đang dùng cho
  tool khác thì menu bỏ qua và báo.
- Test EditMode: bảng đổi tên (cả cây Mixamo không trùng tên), phạm vi và cờ, import một FBX mẫu giống file Mixamo (xương,
  tên clip, đường dẫn curve).
