# Luật và nguồn

Các nguồn, ghi ký hiệu ở cuối mỗi luật:

- **[M p.N]** *Blender Reference Manual, Volume 3: Painting and Sculpting, Rigging, Animation, and Physics*
  (bản in từ manual chính thức ngày 2016-07-07, Blender 2.77). Tên menu, phím tắt đã đổi ở Blender 4.x (bone layers →
  bone collections, Bone Groups → màu theo collection, Pose Library → asset); khái niệm và thuật toán thì còn nguyên.
  Chỗ đổi API xem `blender-notes.md`.
- **[V m:ss]** Ryan King Art, *Animation for Beginners! (Blender Tutorial)*, YouTube, 2022,
  https://www.youtube.com/watch?v=CBJp82tlR3M (44 phút: keyframe, timeline, graph editor, handle, interpolation,
  animate khối lập phương nhảy).
- **[Q n]** `GameAnimation_QuyChuan.md` mục n; **[N n]** `GameAnimation_NguyenLy.md` mục n.
- **[RK]** Bài học đo được khi làm lại một chibi cầm rìu và khiên bằng rigkit, 2026-09-28; chi tiết trong
  `failure-catalog.md`.
- **[E]** Đo pack ExplosiveLLC 2.5.2 (*RPG Character Mecanim Animation Pack FREE*, Asset Store; clip Unarmed và
  2Hand-Sword) bằng đúng thước của `check_anim`, 2026-09-28: biên độ thân trên, độ lệch pose đầu / cuối so với Idle.
- **[T]** Nhận xét của team, 2026-09-28: một bộ clip chibi sinh bằng code "chán, tệ", còn pack ExplosiveLLC chuyển
  state mượt.
- **[RB]** Bài học khi làm một nhân vật tròn không tay chân, kiếm và khiên lơ lửng, bằng rigkit, 2026-09-30 (người
  dùng: "follow through đang quá nhẹ"); chi tiết trong `failure-catalog.md` mục E.

## 1. Rig

| # | Luật | Nguồn |
|---|---|---|
| R1 | Rig giải bài toán chuyển động: bắt đầu từ rig đơn giản, thêm công cụ khi clip cần | [M p.82] |
| R2 | Sửa rig ở Edit Mode là sửa rest pose, mọi pose đã làm đổi theo: chốt rig trước khi skin và pose | [M p.192] |
| R3 | Trục Y của xương dọc theo xương (trục roll). Chọn roll có chủ đích: rigkit đặt X là trục gập từ `bend` | [M p.167, p.207–208] |
| R4 | Cặp đối xứng đặt tên có hậu tố trái/phải (`_L/_R`, `.L/.R`, `Left/Right` đầu tên) để mirror và Paste Flipped chạy | [M p.212–214, p.203] [Q 5.2] |
| R5 | Tắt Deform thì xương không vào automatic weights và mất mọi ảnh hưởng: xương điều khiển, socket để không deform | [M p.175] [Q 5.3.3] |
| R6 | Xương mặc định thừa kế xoay và scale của cha, lan xuống cả cây: scale Hips là scale cả người. Scale không đều trên xương có con làm con bị xiên | [M p.234–235] [Q 5.2.6] |
| R7 | Khuỷu người không gập ngược, không gập sang ngang: giới hạn bằng Limit Rotation hoặc bảng giới hạn | [M p.229] |
| R8 | IK chỉ đi qua xương nối (connected); pole target quyết hướng gối, khuỷu; hai xương cẳng tay để chia xoắn cổ tay | [M p.238–240, p.132–133] |
| R9 | Limit Rotation không khoá xương do IK điều khiển: IK có giới hạn riêng. rigkit kiểm giới hạn sau khi giải | [M p.119] |
| R10 | Phụ kiện đổi cha (vũ khí rơi khỏi tay) dùng Child Of và key influence; clip game thì bake ra xương | [M p.146–147, p.334–335] |
| R11 | Chân dẫm sàn: Floor constraint (Sticky) là ý tưởng; rigkit làm bằng IK chân cắm đất và gate trượt chân | [M p.150] |

## 2. Skin (weight)

| # | Luật | Nguồn |
|---|---|---|
| S1 | Dùng Armature Deform "With Automatic Weights" (bone heat), không tự tính weight theo khoảng cách hay toạ độ | [M p.257] [RK] |
| S2 | Vùng quanh khớp weight nhẹ dần (10–40%) để da giãn; xương trên 1,0 giảm dần về 0 qua khuỷu, xương dưới ngược lại | [M p.56, p.261] |
| S3 | Đỉnh không thuộc nhóm nào hiện màu đen trong Weight Paint: đó là lỗi weight dễ thấy nhất | [M p.46] |
| S4 | Blender tự chuẩn hoá lúc deform nên thiếu chuẩn hoá không lộ trong Blender, nhưng Unity lại cần: Normalize All trước khi xuất | [M p.48, p.59] [Q 5.3.2] |
| S5 | Clean (bỏ weight nhỏ, giữ ít nhất một) rồi Limit Total (bỏ weight nhỏ nhất tới đủ giới hạn) | [M p.61–62, p.66] [Q 5.3.1] |
| S6 | Mirror weight chỉ mirror trong một nhóm; muốn chép sang xương đối diện thì đổi tên nhóm (Flip Group Names) | [M p.59–60] |
| S7 | Transfer Weights chép weight từ object khác đặt trùng chỗ: đây là đường dự phòng proxy (voxel) của rigkit | [M p.65–66] |
| S8 | Preserve Volume (dual quaternion) tránh bẹp khớp nhưng Unity trộn tuyến tính: tắt để Blender hiện đúng cái game hiện | [M p.257–258] |
| S9 | Chỉ Vertex Groups, tắt Envelope trên Armature modifier (Unity chỉ đọc vertex group) | [M p.257, p.259] |
| S10 | Shape key relative dùng sửa deform ở khớp (corrective); driver không sang Unity, phải lái bằng code | [M p.301, p.320] |
| S11 | Mảnh rời nhỏ (mắt, khoá thắt lưng) chép weight của bề mặt bên dưới; da dưới mũ cứng khoá 100% theo xương đầu | [RK] |
| S12 | Mỗi chi chỉ có weight trong vùng của nó (+ dải blend đo dọc bề mặt): đuôi bone heat làm cánh tay kéo méo miệng | [RK] |
| S13 | Chọn 4 xương mỗi đỉnh sao cho đỉnh kề nhau cùng bộ (chọn trên weight đã smooth); chọn riêng từng đỉnh làm da gai | [RK] |
| S14 | ROM ở 90% giới hạn của từng xương là thước đo skin: lỗi thì hạ giới hạn của xương đó (hoặc sửa skin) rồi chạy lại. Giới hạn sau ROM là giới hạn thật của nhân vật, pose và clip phải nằm trong nó | [Q 5.3.5] [RK] |
| S15 | Dải blend của chi vào thân (`limb_grow`) vừa đủ: rộng quá thì chi kéo theo chi tiết gần gốc chi | [RK] |

## 3. Pose

| # | Luật | Nguồn |
|---|---|---|
| P1 | Pose FK đi từ gốc ra ngọn; IK đặt đầu mút, chuỗi tự theo | [M p.236, p.238] |
| P2 | Pose của mỗi xương là độ lệch so với rest trong không gian riêng của nó (loc 0, rot 0, scale 1 là rest) | [M p.228, p.231] |
| P3 | Copy / Paste Flipped chép theo tên và theo không gian riêng của từng xương: pose đối xứng cần rest đối xứng | [M p.232–233] |
| P4 | Push / Relax / Breakdowner: đẩy pose, kéo về, tạo breakdown giữa hai key | [M p.232] |
| P5 | Chân luôn cắm đất bằng IK; hông nhún thì gối gập, không để chân lún hay trượt | [Q 7.2] [RK] |
| P6 | Tay cầm vũ khí: giải ở key pose (IK + swivel khuỷu + xoay cẳng tay), nội suy bằng FK: tay đi cung, không giật | [M p.240] [RK] |
| P7 | Duyệt pose ở góc camera game, bằng silhouette; xoay quanh pose để bắt xuyên | [Q 2 dòng 3, 11] |

## 4. Animation

| # | Luật | Nguồn |
|---|---|---|
| A1 | Key là mốc thời gian mang giá trị; Blender nội suy giữa các key theo interpolation | [M p.287] [V 1:07] |
| A2 | Key kênh nào thì chỉ kênh đó có animation: key đủ location, rotation, scale ở mỗi pose chính (keying set) | [V 4:25] [M p.292–299] |
| A3 | Màu thuộc tính: vàng = key ở frame này, xanh = có animation, tím = driver | [V 6:04] [M p.299–300] |
| A4 | fps là số hình mỗi giây; video dùng 24, game của team 30 (Unity nội suy khi chạy 60) | [V 9:07] [Q 3.1] |
| A5 | Đổi timing bằng dời key; giữ pose bằng nhân đôi key (hold) | [V 10:12, 11:06] |
| A6 | Auto keying nhanh nhưng dễ key nhầm: tắt khi không animate (script thì key có chủ đích) | [V 12:56] |
| A7 | Graph editor: trục ngang thời gian, trục dọc giá trị; handle quyết ease | [V 19:39, 23:07] [M p.296–297] |
| A8 | Va chạm (đập tường): handle nhọn, tới nhanh và dừng ngay. rigkit: ease `impact` | [V 23:42–25:51] |
| A9 | Interpolation: Constant (bậc thang, dùng cho blocking), Linear (đều, máy móc, dùng cho camera, vật máy), Bezier (ease in/out, mặc định) | [V 26:03–27:30] [Q 9.1] |
| A10 | Rơi nhanh hơn lên (trọng lực); sau tiếp đất còn trượt nhẹ vì đà | [V 30:47, 31:20, 34:06] [N 2.6, 2.9] |
| A11 | Xoay dư, lắc ngược lúc gần tới key (wobble): sửa key đỉnh cho gần pose cuối. rigkit: tiếp tuyến clamped, không vọt quá | [V 36:18–36:50] |
| A12 | Xem ở view orthographic cạnh, trước, trên để kiểm cung và độ lệch | [V 32:26] [M p.250–253 motion paths] |
| A13 | Motion path và ghost (onion skin) để thấy cung và spacing mà không cần play | [M p.248–253, p.288–291] |
| A14 | Mỗi object chỉ chơi một action; nhiều action phải có Fake User để được lưu; NLA để trộn | [M p.296, p.300] |
| A15 | Visual keying: key theo transform nhìn thấy (sau constraint) khi bake | [M p.293] |
| A16 | Render ra chuỗi ảnh rồi ghép, không render thẳng ra video (render dài). Video duyệt của `review --video` là Workbench vài giây nên ghi thẳng MP4; hỏng thì chạy lại | [V 41:02] |
| A17 | Chuyển động phụ vật lý (soft body, cloth, rigid body) chạy trên mesh, Unity không nhận: bake ra xương hoặc làm spring ở runtime. rigkit `drop` giả lập rơi có trọng lực 9,81 | [M p.337, p.467–468] [Q 2 dòng 5] |
| A18 | Mỗi clip key đủ mọi kênh của mọi xương; clip thiếu kênh giữ giá trị clip trước (Blender) hoặc tuỳ Write Defaults (Unity) | [RK] |
| A19 | Loop: frame cuối = frame đầu và tiếp tuyến liền qua chỗ nối | [Q 7.1] |
| A20 | Clip hành động bắt đầu và kết thúc ở pose trung tâm (frame 0 của Idle): trong rigkit key đầu và cuối là `{}` trên base READY, sóng của Idle gần 0 ở frame 0. Pack lệch trung bình 0–9°, clip bị chê lệch 35° (một xương 134°) | [E] [T] |
| A21 | Idle phải sống: xương thân trên xoay trung bình ≥ 8° (pack 15–16°, idle chibi bị chê ~5°); đòn ≥ 20° (pack 42–86°). Chibi bị chặn (khiên chạm vành mũ, tay ngắn): dồn biên độ vào đầu, cổ tay, độ xoắn lan dần từ lưng lên đầu, tay không cầm khiên | [E] [T] [RK] |
| A22 | Theo đà sau cú dừng gắt: phần lỏng đi tiếp, vọt quá rồi lắng trong vài nhịp nhỏ dần (lò xo tắt dần), không chỉ trễ một frame rồi dừng theo. Phần xa hơn thì lỏng hơn, chậm hơn (ngực → đầu, tay → vũ khí) để cú dừng lăn dọc cơ thể. rigkit: `spring`, `spring_channel` | [Q 2] [Q 7.6.9] [RB] |
| A23 | Vật cầm, vật lơ lửng theo đà bằng một lò xo trên hướng của chính vật (armature space), không lò xo từng khớp của tay IK; góc lò xo có trần mềm, khớp có giới hạn mềm; ghi ngược bằng hiệu góc trước / sau khi xoay | [RB] |
| A24 | Vật cứng (vũ khí, khiên, mũ, mặt) được chạm sàn, không được xuyên sàn: đo bằng gate riêng, GROUND chỉ đo da mềm | [RB] [RK] |

## 5. Xuất và Unity

| # | Luật | Nguồn |
|---|---|---|
| X1 | FBX: FBX Units Scale, Forward -Z Up Y, tắt Apply Transform, tắt Add Leaf Bones, chỉ xương deform, Bake Animation + Key All Bones, Force Start/End Keying, Simplify 0 | [Q 5.7] |
| X2 | Một SkinnedMeshRenderer mỗi nhân vật (gộp mesh lúc xuất, .blend giữ riêng) | [Q 5.3.6] |
| X3 | Import Blender của FBX dời animation +1 frame (`anim_offset` mặc định 1): so khớp phải đặt 0 | [RK] |
| X3b | Import Blender của FBX nối con duy nhất vào đuôi xương cha, xương đã nối bỏ qua location; Unity không có "nối": so khớp phải gỡ nối mọi xương sau import | [RB] |
| X4 | Unity: Generic, Root node None, Optimal 0,5°, Remove Constant Scale Curves; event lấy từ `<out>.events.json` | [Q 8.1, 7.4] |
