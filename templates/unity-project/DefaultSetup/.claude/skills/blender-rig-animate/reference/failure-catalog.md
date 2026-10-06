# Lỗi đã gặp: triệu chứng → nguyên nhân → luật → gate bắt

Đọc trước khi rig một nhân vật mới. Mỗi dòng là một lỗi thật, có số đo. Mục A–D: một chibi cầm rìu và khiên (cao
1,46 m ở cỡ game); "script tay" là lần rig nó ngày 2026-09-25 bằng script viết tay (không gate), "rigkit" là lúc làm
lại bằng bộ công cụ này ngày 2026-09-28. Mục E: một nhân vật tròn không tay chân, kiếm và khiên lơ lửng (cao 1,46 m),
làm bằng rigkit ngày 2026-09-30.

## Kết quả trên cùng một nhân vật, cùng gate

| | Script tay (cũ) | rigkit (mới) |
|---|---|---|
| `check_weights --stress` | 5 lỗi (FLIP ở gập 30°), 202 weight < 0,01, 4 vùng xương bị chia mảnh | 0 lỗi |
| ROM (`rom` + `check_anim`) | không có ROM | 0 lỗi, sau khi hạ giới hạn cổ, đầu, cánh tay, đùi và `limb_grow` (mục A) |
| `check_anim --poses` (12 key pose) | không có | 0 lỗi (lần đầu: 23 lỗi trong 10 pose) |
| `check_anim` (5 clip) | 50 lỗi: PROP_PENETRATION 15, LIMB_PENETRATION 11, FOOT_SLIDE 11, SCALE_CHILDREN 5, GROUND 4, FLIP 4 | 0 lỗi, 0 cảnh báo |
| Biên độ, pose trung tâm (đo cùng thước) | Idle thân trên trung bình 5,6°; đòn lệch Idle f0 trung bình 1,8° | Idle 8,5° (pack 15–16°); đòn lệch 0,7° |
| FBX vòng xuất–nhập | không kiểm | lệch < 0,1 mm, 1 mesh, không xương `_end` |

(Bản script tay rig ở cỡ gốc 0,47 m nên các số dưới đây của nó nhân 3,4 thì ra cỡ game.) Cột rigkit là gate của ngày
2026-09-28; gate PROP_UNDER_FLOOR thêm sau đó bắt thêm 3 lỗi trên chính nhân vật này (rìu cắm sàn ở 2 pose và clip
Attack, mũ lún sàn ở Die: mục B, C). Gate sạch chỉ nói những gì gate đo.

## A. Weight

| Triệu chứng | Nguyên nhân | Luật | Gate |
|---|---|---|---|
| Thân gập thì gãy khúc; ảnh phân vùng thấy thân thành sọc ngang | Script tay tính weight bằng ngưỡng z (Hips 0,095, Spine 0,14 …) và `x >= 0` chia trái phải | Dùng bone heat, không bao giờ weight theo toạ độ | `check_weights --png` (ảnh phân vùng), FLIP / TEAR khi stress |
| 202 weight lẻ < 0,01 | không Clean | Clean < 0,02 rồi Limit Total 4, Normalize | TINY_WEIGHTS |
| Vung rìu thì miệng, má bị kéo lệch (gai 4,3 ở giữa mặt) | bone heat để đuôi dài: UpperArm_L 0,04 trên mặt; cắt 4 influence từng đỉnh làm đỉnh kề nhau giữ xương thứ 4 khác nhau (Spine / UpperArm_L) | Mặt nạ vùng chi (`limb_regions`); chọn 4 xương trên weight đã smooth | TEAR khi stress, ở đúng toạ độ |
| Xương vai (Shoulder) kéo cả mảng ngực | thân tròn chibi: vai nằm trong thân, bone heat trải rộng | Chibi: Shoulder không deform | ảnh `--heat Shoulder_L`, DISCONNECTED |
| Tay hạ 45–55° thì nách lật tam giác | tay 26 cm mọc từ thân tròn, khớp vai gập sâu | Hạ tay tối đa -35° (READY), giới hạn chibi UpperArm side ≥ -40 | FLIP (pose), LIMB_PENETRATION |
| Mũ lún vào đầu, vành mũ cắm vào gáy khi đầu cúi | da dưới mũ blend Head / Neck / Chest | Khoá vùng dưới phụ kiện cứng về xương mang nó (`locks`) | ACCESSORY_SKIN, PROP_PENETRATION |
| Tay đưa ra trước 81–99° thì nách, ngực rách (gai 1,6–1,8); cổ và đầu xoay 40° thì cổ áo lật | giới hạn `chibi` chung rộng hơn cái mesh này chịu được | Hiệu chỉnh giới hạn từng xương bằng ROM (UpperArm flex ≤ 70, Neck twist ±25, Head twist ±35) | TEAR, FLIP ở ROM |
| Đầu to nghiêng sang bên thì lún vào vai 3,2–3,8 cm; tay giơ lên 90° chạm đầu; đùi đưa trước 54° chạm bụng | như trên | Neck side ±15, Head side ±12, UpperArm side ≤ 60, UpperLeg flex ≤ 45 | LIMB_PENETRATION ở ROM |
| Tay đưa ra trước thì ngực trước lật tam giác | dải blend của tay (`limb_grow` 0,05 chiều cao) kéo theo chi tiết ở ngực | `limb_grow` 0,035 | FLIP ở ROM |
| Đếm nhầm lật tam giác ở cổ áo, thắt lưng (25 cái ngay ở READY) | đo lật bằng pháp tuyến trung bình quanh đỉnh: chi tiết mảnh vốn gập | Đo lật so với pháp tuyến rest xoay theo xương chính của tam giác | (sửa trong thước đo, selftest) |

## B. Pose

| Triệu chứng | Nguyên nhân | Luật | Gate |
|---|---|---|---|
| Chân lún đất 4,1 cm (Move), 3,4 cm (Attack); chân trượt 13,4 cm lúc đang chạm đất | Script tay dời hông mà chân FK: hông xuống là chân xuống theo | Chân luôn IK cắm đất; hông nhún thì gối gập | GROUND, FOOT_SLIDE |
| Cả người méo xiên khi "nén giãn" | Script tay scale không đều ở Hips (xương có con) | Nén giãn bằng pose; scale đều chỉ ở xương lá | SCALE_CHILDREN |
| Tay chìm vào thân 1,2–1,8 cm ở mọi clip | READY hạ tay 58° trên tay chibi | Đo góc tay chạm thân trước (proportions.md) | LIMB_PENETRATION |
| Rìu cắt ngang mặt khi lấy đà | tay giơ thẳng lên dưới vành mũ; rìu dài 1,05 m | Vung rìu trong mặt phẳng ngoài vành mũ (tay ra ngang, azimuth ~100) | PROP_PENETRATION axe/hat |
| Cổ tay gập 77–92° để giữ hướng rìu | IK tay chọn pole khuỷu bất kỳ, cẳng tay không xoay | Giải tay ở key: dò swivel khuỷu + xoay cẳng tay, chấm điểm cổ tay thẳng | LIMIT Hand_* |
| Cánh tay xoắn -94 … -110° | pole khuỷu ngược tự nhiên | Như trên; giới hạn chibi UpperArm twist ±60 | LIMIT UpperArm_* |
| Cổ chân gập 47° khi lao tới | hông lao trước 1,5 cm + xuống 2,2 cm trên chân 21 cm | Hông hạ tối đa 2,5 cm; lao tới bằng thân trên | LIMIT Foot_* |
| Nhấc gót làm cổ chân gập thêm (36° → 51°) | chân ngắn, bàn chân dài: nhấc gót nâng cổ chân 6,8 cm, gối gập thêm | Tự nhấc gót thử nhiều mức, giữ mức ít vi phạm nhất (có khi là không nhấc) | LIMIT Foot_* |
| Khiên chạm vành mũ | khiên 1 m, tay đưa ra ngang | Khiên thấp và phía trước (tay READY) | PROP_PENETRATION hat/shield |
| Rìu cắm sàn 8,3 cm ở pose chém, 24,5 cm ở pose theo đà; 12 pose "0 lỗi" vì chưa gate nào đo (tìm ra 2026-09-30, khi thêm PROP_UNDER_FLOOR) | rìu 1,05 m vung xuống trước người; GROUND chỉ đo da mềm | Vũ khí dài chạm sàn thì dừng trên sàn: nâng tay, gập cổ tay ở pose chém và theo đà | PROP_UNDER_FLOOR |

## C. Clip

| Triệu chứng | Nguyên nhân | Luật | Gate |
|---|---|---|---|
| Chạy: chân duỗi quá gối, lún 3,4 cm, cổ chân 87° | bước 22 cm trên chân chỉ với được ±7 cm | Tính tầm với trước (proportions.md); chibi bước ≤ 10 cm | LIMIT, GROUND, IK_REACH |
| Mũi chân cắm đất 8–14 cm giữa pha nhấc | bàn chân buông theo ống chân (gối 90°) | Ràng buộc hở đất theo đế giày thật; chibi `follow_max` ~0,3 | GROUND |
| Giật 21° ở đầu và cuối pha chạm đất | tham số chuỗi (`pivot: "heel"`) không nội suy theo frame | Mọi tham số điều khiển là số (pivot -1..1) | POP |
| Mũ của clip Die nằm dưới chân trong clip Idle | clip không key location của Prop_Hat, xương giữ giá trị clip trước | Mỗi clip key đủ loc / rot / scale mọi xương | PROP_PENETRATION ở mọi frame |
| Mũ cắm vào gáy 9 cm lúc tiếp đất | `drag` (overlap) trên mũ ôm sát đầu | Phụ kiện ôm sát không lắc riêng; lắc cả đầu (`drag Head`) | PROP_PENETRATION hat |
| Đạo cụ rơi xuống chỗ thân sắp ngã, bị thân đè | vận tốc thả cộng vận tốc tay đang hất về sau | Thả đúng lúc (khiên khi trúng đòn, mũ khi đầu chạm đất), bay ra xa chỗ ngã | PROP_PENETRATION |
| Chạy trông như đứng nhún (bản rigkit đầu tiên, 0 lỗi gate) | chân chibi ở giới hạn nên chỉ nảy 0,8 cm, tay đánh 6° | Lực đặt vào thân trên: `wave` nén giãn lưng, ngực, đầu lệch nhau một frame, tay 15° (proportions.md) | review (mắt); LIMIT khi thử tăng nảy |
| Khiên văng 4,4 m khỏi thân lúc ngã, ảnh review cắt mất | thả ở key `impact`: tay đang đi 9,2 m/s và đạo cụ giữ nguyên vận tốc đó; khung review lấy theo frame đầu | `drop(..., inherit=0.1)`; review lấy khung theo mọi frame | DROP_FAR |
| Idle "đúng mà chán": thân trên xoay trung bình 1,5° (đầu 6°, tay 2°), 0 lỗi gate | chỉ thở nhẹ; tay khiên nâng lên là khiên chạm vành mũ nên không dám cho tay động | Idle ≥ 8° trung bình: đầu nhìn quanh ±10°, xoắn lan từ lưng lên đầu, cổ tay đưa rìu, khiên nghiêng; tay khiên đứng gần READY | LOW_AMPLITUDE |
| Clip hành động bắt đầu ở READY nhưng Idle f0 lệch READY (gối 10,9°) | key 0 của Idle có hông và ngực riêng; sóng có pha khác 0 | Key 0 của Idle là `{}`; action bắt đầu, kết thúc bằng `{}` trên cùng base | HUB_START, HUB_END |
| Mũ lún sàn 4,3 cm ở frame đầu chạm đất của Die rồi mới bật ra (tìm ra 2026-09-30) | `drop` mũ đúng frame đầu chạm đất: frame đó mũ còn trên đầu | Thả trước một frame, lúc đầu còn trên sàn, hoặc cho đầu dừng trên sàn | PROP_UNDER_FLOOR |
| Cứng như robot, dừng ở mọi key (idle của bản script tay) | Bezier auto-clamped có key mỗi 15 frame, pose giữ im | Idle bằng 2 key + lớp sóng (`wave`) lệch pha; hold luôn có chuyển động nhỏ | review (mắt) |

## D. Công cụ và kiểm

| Triệu chứng | Nguyên nhân | Luật |
|---|---|---|
| FBX lệch 0,19–0,32 m so với nguồn khi kiểm | import FBX của Blender dời animation +1 frame | So khớp với `anim_offset=0` |
| Stress test báo rách ở mọi rig (cả rig tốt) | gập mọi khớp 45°, đo cạnh ngắn ở nếp gấp | Gập 70% giới hạn của từng khớp; đo gai so với lân cận, hiệu chỉnh bằng selftest |
| Độ sâu xuyên 25–29 cm vô lý | đo mặt gần nhất trên vùng mesh hở, xa chỗ cắt nhau | Chỉ đo trên đỉnh của tam giác thật sự cắt nhau |
| Ảnh review xỉn màu | view transform AgX | Workbench + Standard |
| (Nhân vật mục E) verify_fbx báo DEVIATION 1,2 m ở clip Die (prop rơi xa tay), trong Unity clip lệch nguồn chỉ 5–11 mm (nén Optimal) | importer FBX của Blender nối (use_connect) con duy nhất vào đuôi xương cha (prop dưới bàn tay), xương đã nối bỏ qua location; Unity không có "nối", luôn chơi đủ TRS | verify_fbx gỡ nối mọi xương sau import rồi mới đo (rigkit làm sẵn); vẫn bắt lỗi thật (key lệch 5 cm báo 0,050 m) |
| (Nhân vật mục E) mọi ngưỡng theo chiều cao chặt gấp đôi: khiên lệch dưới sàn 1,5–2,5 cm báo lỗi (ngưỡng 2% còn 14 mm), Die báo DROP_FAR ở 0,57–0,66 m | check_anim lấy chiều cao từ da mềm; da mềm của nhân vật này chỉ là cái áo (0,72 m trên 1,46 m) | Chiều cao = `prepare --height` (lưu trong file), không có thì lấy tư thế nghỉ của da mềm cộng mọi phần cứng trừ vũ khí / khiên cầm tay (mặt, mũ tính vào); `--height` để ghi đè; báo cáo ghi `height_m` |
| `review` chạy song song vào cùng thư mục thì crash | các lượt dùng chung file tạm `_rk_frame.png` | Chạy lần lượt, hoặc mỗi lượt một thư mục `--out` riêng |
| Ngưỡng pose trung tâm lấy theo ghi chép (lệch 0–3°) đánh lỗi chính pack mà team khen | ghi chép đo khác thước (trung bình, bộ xương khác); đo lại bằng thước của gate: pack lệch trung bình 2,5–9,2°, xương tệ nhất 15–42,5° | Hiệu chỉnh ngưỡng bằng `scripts/tests/measure_clips.py` (cùng thước với gate) trên clip team khen và clip team chê (HUB lỗi ở trung bình > 20° hoặc một xương > 90°) |

## E. Theo đà (lò xo) và vật cứng (nhân vật tròn, vũ khí lơ lửng)

| Triệu chứng | Nguyên nhân | Luật | Gate |
|---|---|---|---|
| "Follow through đang quá nhẹ": thân dừng thì các phần dừng theo sau một frame | overlap chỉ trễ frame (`drag`, trễ cố định), không bao giờ vọt quá | Lò xo tắt dần: `spring` / `spring_channel` (hz 2,8, zeta 0,3). Sau khi thêm, đầu kiếm lệch bản không lò xo 17–48° ở đòn đánh, 9° ở Idle, 5° ở Move | review (mắt, video) |
| Kiếm lắc 95° lúc bật nhảy, dù thực tế nó gần như không xoay | lò xo riêng trên từng khớp của tay giải bằng IK: các khớp dư bù trừ nhau, lò xo từng khớp phá thế bù đó | Một lò xo trên hướng của chính vật, trong armature space: `spring(bone, "turn")`, hoặc `"grip"` để vật trễ theo cả cú nảy của thân | review; LIMIT |
| Chém dọc 2 frame quét ~180°: tay tự do văng thêm ~100°, cổ tay ra ngoài giới hạn; kẹp từng trục thì hướng vỡ (lệch 197°) | lò xo không có trần; kẹp riêng từng góc giải phẫu làm sai hướng | Trần mềm cho góc lò xo (`max_deg`: kiếm 40°, khiên 30°), giới hạn khớp mềm từng trục | LIMIT |
| 0,1° lò xo thành một cú xoay lớn (kiếm cắm vào mũ) | ghi ngược bằng "góc sau khi xoay − giá trị kênh trên key": cùng một hướng có thể là bộ ba góc khác | Lấy góc giải phẫu cả trước lẫn sau khi xoay, trừ nhau, wrap ±180 (rigkit làm sẵn, `tests/selftest_follow.py` kiểm) | PROP_PENETRATION, POP |
| Lò xo nén áo thêm, áo phình vào mặt | lò xo trên kênh ảo (mức nén) đẩy quá giá trị key | `room` nhỏ cho kênh ảo, hạ gain ở clip đó | PROP_PENETRATION (tỉ lệ điểm > 4%) |
| Khiên, kiếm cắm sàn 5,9–10,6 cm (lò xo đẩy xuống), không gate nào báo | GROUND chỉ đo da mềm; kiếm, khiên, mũ, mặt là mesh cứng | Gate cho mesh cứng: lệch dưới sàn > 2% chiều cao là lỗi (chạm sàn được, xuyên thì không) | PROP_UNDER_FLOOR |
| Lò xo mạnh dần: gain 1,0 qua gate, 1,2 trượt 3 lỗi, 1,5 trượt 9 lỗi | lò xo đẩy vật vào người, vào sàn | Tăng dần và chạy gate sau mỗi nấc; gate phải bắt được khi quá tay | PROP_UNDER_FLOOR, PROP_PENETRATION |
