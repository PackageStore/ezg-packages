---
name: blender-rig-animate
description: Rig, skin, pose and animate a 3D game character, monster, prop or vehicle in headless Blender for Unity, with follow-through springs and gates that measure weights, joint limits, penetration (props into the floor too), foot sliding and loops frame by frame. Use when asked to "rig this model", "animate idle / attack / run / die", "làm anim cho nhân vật", "rig và animate", "weight sai", "anim bị xuyên", "pose kỳ quặc", "follow through quá nhẹ", "fix the skinning", "export FBX animation for Unity", or whenever a .blend / .fbx / .glb model must get bones, weights or clips. Does not do 2D (Unity 2D Animation) or runtime Unity code.
argument-hint: [model file] [clip list]
---

# Rig và animate bằng Blender headless (rigkit)

Skill này thay cách làm cũ (viết tay weight theo toạ độ, pose bằng Euler, duyệt bằng mắt trên ảnh nhỏ) bằng một
chuỗi bước có **gate đo được**. Nó thực thi `GameAnimation_QuyChuan.md` (trong `Docs` của module Game Animation;
project không có module thì đọc bản đi kèm skill `game-animation`, `<skills>/game-animation/docs/`):
tên xương mục 5.2, skin 5.3, xuất FBX 5.7, clip tại chỗ 7.2, event 7.4, fps 30, rigkit 5.10. Dùng cùng skill
`game-animation` (đọc và áp quy chuẩn); skill này làm phần Blender. Quy chuẩn 5.8: nhân vật chính vẫn key tay,
clip do rigkit dựng dùng cho quái, NPC, nhân vật phụ, clip tạm và blocking.

**Luật số 1: không bước nào qua khi gate còn lỗi.** Ảnh render để duyệt cảm giác (silhouette, nhịp, tính cách);
gate để bắt cái mắt bỏ sót trên ảnh 260 px: weight lạc, chân lún đất 3–4 cm (nhân vật cao 47 cm), rìu cắt ngang mặt, cổ chân gập 47°.
Một lần rig chibi bằng script tay, không gate (2026-09-25): đo lại thì 55 lỗi (5 weight, 50 animation), người dùng thấy
ngay "pose kỳ quặc, weight sai tùm lum" (`reference/failure-catalog.md`).

| Thứ | Ở đâu |
|---|---|
| Blender | `blender.exe` 4.2+ (đã chạy trên 4.5 LTS). Tìm: biến môi trường `BLENDER`, `where blender`, `C:/Program Files/Blender Foundation/*`, `<thư viện Steam>/steamapps/common/Blender`; không thấy thì hỏi người dùng |
| Runner | `scripts/rk.py`: mỗi bước một lệnh `blender -b <file> --factory-startup --python-exit-code 1 --python scripts/rk.py -- <bước> ...`, exit 1 khi gate lỗi |
| Thư viện | `scripts/rigkit/` (numpy + mathutils, không cần cài gì) |
| Tự kiểm | `scripts/tests/selftest_weights.py`: weight tốt phải qua, weight hỏng phải trượt (chạy khi sửa rigkit). `scripts/tests/selftest_follow.py`: lò xo theo đà (toán và bake trên rig thử tự dựng). `scripts/tests/measure_clips.py`: đo biên độ và pose trung tâm của clip bất kỳ (FBX của pack, .blend) bằng thước của gate, để hiệu chỉnh ngưỡng |
| File của nhân vật | rig spec, skin spec, key pose, clip, `run.sh` chạy cả chuỗi: dữ liệu của project, để trong project game (mục dưới). Lỗi đã gặp và spec đã chỉnh vì gate nào, trên một chibi cầm rìu và khiên: `reference/failure-catalog.md` |
| Luật và nguồn | `reference/rules.md` (manual Blender, video, quy chuẩn) · `reference/failure-catalog.md` (lỗi đã gặp) |
| Định dạng spec | `reference/specs.md` · Tỉ lệ chibi, ngắn chân: `reference/proportions.md` · API Blender 4.x: `reference/blender-notes.md` |

Không sửa file nguồn của người dùng: bước đầu `prepare` ghi ra file mới, mọi bước sau đọc file trước và ghi file
sau. Làm trong scratchpad, chỉ chép sản phẩm (FBX, .blend nguồn, events.json) vào project khi người dùng đồng ý.

## File của nhân vật

Skill này không chứa thông tin của project nào. Spec của một nhân vật (`rig_spec.json`, `skin_spec.json`, `poses.py`,
`clips.py`, `run.sh` chạy cả chuỗi, bảng kết quả gate) là dữ liệu của project đó: để trong project game, cạnh model
nguồn, trong folder Unity bỏ qua, ví dụ `<folder model>/Blender~/<Subject>_rigkit/`. Project đã có chỗ khác thì theo
chỗ đó; chưa rõ thì hỏi người dùng. `run.sh` tìm `rk.py` qua biến `RK` hoặc đường dẫn skill đang cài, không ghi cứng
đường dẫn máy. Không lấy số trong spec của nhân vật khác (kể cả ví dụ trong `reference/`) làm số cho nhân vật mới: đo
lại (`measure`, `reference/proportions.md`). Bài học chung rút ra (lỗi mới, luật mới): báo người giữ skill để thêm vào
`reference/`, không kèm tên project.

## Quy trình

```
0 khảo sát ─ 1 inspect ─ 2 prepare ─ 3 measure + rig spec ─ 4 rig ─ 5 skin ─ 6 check_weights (--stress)
─ 6b ROM + check_anim ─ 7 blocking: key pose + check_anim --poses ─ 8 clip: anim + check_anim ─ 9 review
─ 10 export + verify_fbx
```

**0. Khảo sát.** Model nằm đâu, định dạng gì; project đích dùng tên xương nào (quy chuẩn 5.2 hay Mixamo, xem
Avatar Mask, controller, code tìm xương theo tên); nhân vật khác trong game cao bao nhiêu (scale); danh sách clip và
mood của game (quy chuẩn 2.1, 3.x, frame data). Hỏi người dùng khi không suy ra được; ghi vào brief (quy chuẩn 9.2).

**1. inspect** — `-- inspect --out inspect.json`. Đọc: số mảnh rời (mắt, nút là mảnh nhỏ), cạnh hở, đỉnh trùng,
độ đối xứng, scale chưa apply, chiều cao. Model AI (Tripo, Meshy) thường có mảnh rời và lỗ: bình thường, bước skin
xử lý được.

**2. prepare** — `-- prepare --scale S | --height H --ground-mesh body --out work.blend`. Bake transform vào mesh,
đưa về cỡ game (1 unit = 1 m), chân chạm z = 0, căn X = 0, 30 fps. Nhân vật phải to đúng cỡ game trước khi rig:
bone heat và mọi dung sai tính theo mét.

**3. measure + rig spec** — `-- measure --mesh body --out m.json --png m.png`. Cắt lát mesh ra háng, trục chân,
trục tay, chỗ tay rời thân, cổ tay. Viết `rig_spec.json` (reference/specs.md): khớp **nằm giữa tiết diện chi**,
đúng điểm xoay thật; mỗi xương có `bend` (hướng gập tự nhiên), khuỷu và gối `prebend` 2–3°; chỉ khai bên `_L`,
bên `_R` tự sinh; `limits`: `humanoid` hoặc `chibi`. Vũ khí cầm tay khai trong `props` (điểm cầm, trục, hướng lưỡi,
pose cầm): tool đặt nó vào tay đúng hướng.

**4. rig** — `-- rig --spec rig_spec.json --out rigged.blend --png rig.png`. Gate: không `OUTSIDE` (khớp ngoài mesh),
`MIRROR`, `PAIR`, `NAME`. Xem ảnh: que xanh là trái, đỏ là phải, vàng là giữa.

**5. skin** — `-- skin --spec skin_spec.json --out skinned.blend`. Bone heat trên bản chép đã vá lỗ; hỏng thì proxy
voxel; mảnh nhỏ bám bề mặt; mask trái/phải; mặt nạ vùng chi; khoá vùng dưới phụ kiện cứng; smooth; 4 influence
chọn ổn định; bỏ weight < 0,02; chuẩn hoá. Đạo cụ cứng 100% một xương. Preserve Volume tắt (Unity trộn tuyến tính).
`limb_grow` (bề rộng dải blend của chi vào thân, phần chiều cao) mặc định 0,05. Dải rộng thì nếp gấp mềm nhưng kéo
theo chi tiết gần gốc chi; chibi thân tròn trong `reference/failure-catalog.md` phải hạ còn 0,035 vì ROM báo FLIP ở ngực
khi tay đưa ra trước. Chỉnh theo ROM,
không đoán.

**6. check_weights** — `-- check_weights --out w.json --stress --png w.png [--heat Bone1,Bone2]`. **Phải 0 lỗi.**
Stress test gập từng khớp tới 70% giới hạn và đo gai, bẹp, lật tam giác. Ảnh `w.png`: mỗi xương một màu, pha theo
weight; mảng sai màu là weight lạc. Lỗi thì sửa spec (khoá, `limb_grow`, cho xương không deform, hạ giới hạn khớp
cho đúng cái mesh chịu được), không vẽ weight bằng toạ độ.

**6b. ROM** — `-- rom --subject <Subject> --out rom.blend`, rồi `-- check_anim --actions <Subject>_ROM --out rom.json`.
**Phải 0 lỗi.** Từng nhóm khớp (trái và phải cùng lúc) gập tới 90% giới hạn của **từng xương** rồi về, cộng một đoạn
hạ tay; gate đo rách, lật da, tay chân lún vào thân (ROM bỏ qua đất, đạo cụ, trượt chân). Lỗi nghĩa là giới hạn rộng
hơn cái mesh chịu được: hạ `limits` riêng của xương đó trong rig spec (hoặc sửa skin), chạy lại từ bước 4, lặp tới
khi sạch. Giới hạn lúc đó là giới hạn thật của nhân vật: mọi pose và clip phải nằm trong nó (gate LIMIT). Ở chibi ví
dụ, ROM hạ cổ, đầu, cánh tay, đùi và `limb_grow` (`reference/failure-catalog.md` mục A). Giao ROM kèm rig
(quy chuẩn 5.3.5).

**7. Blocking** — viết key pose trong một file Python (`POSES = {...}`, reference/specs.md), rồi
`-- check_anim --poses poses.py --out p.json --png p.png --views front34,side,game`. **Phải 0 lỗi** trước khi làm clip:
lần đầu thử trên chibi ví dụ, gate bắt 23 lỗi trong 10 pose mà ảnh nhỏ nhìn vẫn "ổn".

**8. Clip** — viết `clips.py` bằng API `Clip` (reference/specs.md), `-- anim --clips clips.py --out anim.blend`,
rồi `-- check_anim --out a.json`. **Phải 0 lỗi.** Gate kiểm từng frame: giới hạn khớp, lún đất (da mềm: GROUND; vật
cứng như vũ khí, khiên, mũ, mặt: PROP_UNDER_FLOOR, chạm sàn được, xuyên sàn thì không), trượt chân lúc chạm
đất, đạo cụ xuyên người và xuyên nhau, tay chân xuyên thân, rách/lật da, loop hở, giật (pop), Root dời, scale
không đều trên xương có con, đạo cụ rơi quá xa thân (DROP_FAR), pose đầu / cuối lệch pose trung tâm (HUB_START,
HUB_END: frame 0 của Idle), biên độ quá nhỏ (LOW_AMPLITUDE). Hai gate sau hiệu chỉnh trên pack ExplosiveLLC (team
khen mượt) và một bộ clip chibi sinh bằng code (team chê "chán, tệ"), đo bằng `scripts/tests/measure_clips.py`: pack qua sạch; idle chibi
bị chê (~5°) ra cảnh báo, đòn lệch 35° ra lỗi.

**9. Review** — `-- review --action A --frames ... --views front34,side,game --textured --trail Prop_Axe --out r.png
--video r.mp4 --video-view front34`. Duyệt như mục 9.3 quy chuẩn: silhouette ở camera game, đường cung (trail),
tính cách trên ảnh; nhịp, spacing, giật chỉ thấy trên video (loop phát 3 vòng). Khung hình lấy theo mọi frame nên
đạo cụ văng xa vẫn nằm trong ảnh. Gate sạch chưa chắc anim đẹp: bản Move đầu tiên của chibi ví dụ 0 lỗi mà trông như
đứng nhún. Gửi ảnh và video cho người dùng.

**10. export + verify_fbx** — `-- export --out Model/<Subject>_Rig.fbx [--mode split] [--names mixamo]`, rồi
`blender -b --factory-startup --python rk.py -- verify_fbx --fbx ... --source anim.blend --out v.json`. Một
SkinnedMeshRenderer, không xương `_end`, độ lệch sau vòng xuất–nhập ≤ 2 mm. `<out>.events.json` chứa
AnimationEvent cho Unity (FBX không mang). verify_fbx gỡ nối mọi xương sau khi import rồi mới đo: importer của Blender
nối con duy nhất vào đuôi xương cha và bỏ qua location của nó (prop rơi xa đọc lệch cả mét), Unity thì không có "nối".

## Luật cứng (vì sao: reference/rules.md, reference/failure-catalog.md)

**Rig**
1. Khớp nằm giữa tiết diện, ở điểm xoay thật; roll tính từ `bend` để X là trục gập (không đặt roll 0 cho mọi xương).
2. Tên theo quy chuẩn 5.2 và hậu tố `_L/_R` theo bên nhân vật; cần Mixamo thì đổi lúc xuất (`--names mixamo`).
3. Không xương lá `_end`; Root không deform, không có key; xương Hips mang cả thân (clip tại chỗ).
4. Chibi, thân tròn: xương vai (Shoulder) để không deform, tay mọc thẳng từ thân.

**Weight**
5. Không bao giờ tính weight bằng ngưỡng toạ độ (z, x ≥ 0): thân thành sọc ngang, gập là gãy. Dùng bone heat.
6. Mỗi chi chỉ có weight trong vùng của nó cộng dải blend; tay không được kéo mặt (đuôi bone heat), trái không
   theo phải.
7. Da dưới phụ kiện cứng (mũ, giáp) 100% theo xương mang phụ kiện, không thì chúng trượt lên nhau và xuyên.
8. 4 influence chọn ổn định giữa các đỉnh kề nhau; weight < 0,02 bỏ; tổng bằng 1.

**Pose**
9. Pose bằng góc giải phẫu (`flex`, `side`, `twist`) hoặc mục tiêu IK (`reach`, `aim`), không bằng Euler thô.
10. Chân luôn IK, cắm đất; hông nhún thì gối tự gập. Dời hông mà chân FK là chân lún và trượt (lỗi của bản script tay).
11. Mọi pose trong giới hạn khớp đã hiệu chỉnh bằng ROM; tay cầm vũ khí: giải ở key pose (swivel khuỷu + xoay cẳng
    tay), cổ tay gần thẳng.
12. Đo tỉ lệ trước khi pose (reference/proportions.md): tầm với của chân, độ hạ hông tối đa, tay chạm thân ở góc nào,
    mũ chắn tay ở đâu, vũ khí dài bao nhiêu so với người.

**Clip**
13. Timing theo quy chuẩn 3.x; key pose → breakdown → spline. Ease theo mục đích: `impact` ở va chạm (tới nhanh,
    dừng gắt), `stop` ở pose giữ, `burst` khi người chơi bấm, `auto` (clamped, không vọt quá) cho phần còn lại.
14. Rơi nhanh hơn lên; tiếp đất có nén và lắng; hold không bao giờ đứng im tuyệt đối (`wave`).
15. Overlap bằng `drag` cho bộ phận lỏng; phụ kiện ôm sát (mũ) không được lắc riêng. Theo đà sau cú dừng gắt
    (follow-through: đi tiếp, vọt quá, lắng) bằng lò xo: vật cầm hay vật lơ lửng dùng **một** `spring(bone, "turn"
    | "grip")` trên hướng của xương mang nó, không lò xo từng khớp của tay IK; khớp FK đơn (đầu, ngực) dùng
    `spring_channel`. Đặt `windows` để pose trung tâm và khung chạm đúng key; `max_deg` cho vũ khí vung nhanh; không
    `spring` lên Hips hay chân. Lò xo đẩy vật xuống thấp: xem PROP_UNDER_FLOOR.
16. Mỗi clip key đủ location/rotation/scale của mọi xương (clip thiếu kênh giữ giá trị clip trước).
17. Loop: key cuối = key đầu, tiếp tuyến vòng qua chỗ nối (API tự làm); tốc độ gốc ghi vào clip.
18. Đạo cụ rơi (`drop`) bay ra xa chỗ thân sẽ ngã xuống nhưng nằm lại gần thân (dưới 0,6 chiều cao), thả đúng lúc
    (bị đánh văng khi trúng đòn, mũ bật khi đầu chạm đất); thả ở key `impact` thì hạ `inherit`.
19. Chân đã chạm giới hạn thì lực của clip đặt vào thân trên (`wave` nén giãn lệch nhau từng frame, tay, ngực xoắn
    ngược), không ép chân quá giới hạn.
20. Mọi clip hành động bắt đầu và kết thúc ở pose trung tâm (frame 0 của Idle cùng bộ = READY): chuyển state mới không
    giật. Hit được lệch frame đầu (`hub_start=False`, quy chuẩn A-22); Die kết thúc chỗ khác (`hub_end=False`).
21. Idle phải sống: xương thân trên xoay trung bình ≥ 8° (pack 15–16°); đòn ≥ 20°. Code sinh clip dễ ra "đúng mà
    chán": đo biên độ trước khi gửi.

## Báo cáo cho người dùng

Gửi: bảng gate trước / sau (số lỗi theo mã), ảnh review và video mỗi clip, đường dẫn file. Không gọi là xong khi còn lỗi;
cảnh báo còn lại (ví dụ `IK_REACH` vài frame lúc ngã) phải nói rõ và vì sao chấp nhận. Chép vào project Unity chỉ khi
người dùng đồng ý; import theo quy chuẩn 8.1 (Generic, Root node None, Optimal 0,5°).
