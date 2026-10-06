# Định dạng spec và API

Spec là file của từng nhân vật, để trong project game cạnh model (SKILL.md, "File của nhân vật"), không để trong
skill. Toạ độ: Blender, +Z lên, nhân vật nhìn **-Y**, bên **trái** nhân vật
là **+X**, mét.

## 1. Rig spec (`rig`)

```json
{
 "armature": "Rig_Hero", "mirror_x": true, "limits": "humanoid",
 "skin_meshes": ["body"],
 "bones": [
  {"name": "Root", "head": [0,0,0], "tail": [0,0,0.25], "deform": false, "roll_z": [0,-1,0]},
  {"name": "Hips", "head": [0,-0.02,0.95], "tail": [0,-0.02,1.05], "parent": "Root", "bend": [0,-1,0]},
  {"name": "UpperArm_L", "head": [...], "tail": [...], "parent": "Shoulder_L", "bend": [0,-1,0], "connect": true},
  {"name": "LowerArm_L", "head": [...], "tail": [...], "parent": "UpperArm_L", "bend": [0,-1,0], "connect": true,
   "prebend": 3},
  {"name": "LowerLeg_L", "...": "...", "bend": [0,1,0], "prebend": 3}
 ],
 "props": [{"object": "axe", "bone": "Prop_Axe", "hand": "Hand_R", "deform": true, "bone_length": 0.35,
            "grip": {"point": {"box": [[x0,y0,z0],[x1,y1,z1]]}, "axis": [0,0,1], "front": [1,0,0]},
            "hold": {"pose": {"UpperArm_R": {"side": -35, "flex": 22}, "LowerArm_R": {"flex": 50}},
                     "fist": 0.45, "axis": [-0.15,-0.35,1.0], "front": [0,-1,0]}}],
 "static_props": [{"object": "hat", "bone": "Prop_Hat", "parent": "Head", "head": [...], "tail": [...]}]
}
```

- `bend`: hướng đầu xương đi tới khi khớp gập tự nhiên. Đặt: Spine / Chest / Neck / Head / Hips / UpperArm /
  LowerArm / UpperLeg `[0,-1,0]` (ra trước); LowerLeg `[0,1,0]` (gối gập ra sau); Foot, Toes, Shoulder `[0,0,1]`;
  Hand `[0,0,-1]` (T-pose, lòng bàn tay úp). Tool đặt roll để trục X local là trục gập.
- `prebend` (độ): dời khớp giữa (gối ra trước, khuỷu ra sau) để mặt phẳng gập xác định. 2–3° là đủ.
- `limits`: `humanoid` | `chibi` | bảng riêng `{"flex": [lo,hi], "side": [...], "twist": [...]}` trên từng xương.
- `props`: vũ khí đặt vào tay theo pose cầm (`hold`), rồi đưa về T-pose. `grip.point` là điểm nắm (hoặc tâm các đỉnh
  trong `box`), `axis` hướng cán về phía đầu, `front` hướng lưỡi / mặt khiên, trong toạ độ của mesh đạo cụ. Xương
  đạo cụ deform (mesh gộp chung một SkinnedMeshRenderer); đạo cụ là object riêng trong Unity thì dùng
  `Socket_Hand_R` không deform (quy chuẩn 5.5).
- Chỉ khai xương giữa và `_L`; `_R` sinh bằng mirror X.

## 2. Skin spec (`skin`)

```json
{
 "armature": "Rig_Hero", "symmetric": true, "max_influences": 4, "min_weight": 0.02,
 "soft": [{"object": "body", "method": "heat", "small_islands": "follow_surface", "small_island_max_frac": 0.08,
           "exclude_bones": ["Prop_*"], "side_band": 0.02, "smooth": 3, "limb_regions": true, "limb_grow": 0.05,
           "locks": [{"bone": "Head", "z_min": 0.97, "falloff": 0.07}]}],
 "rigid": [{"object": "axe", "bone": "Prop_Axe"}, {"object": "hat", "bone": "Prop_Hat"}]
}
```

- `method`: `heat` (tự rơi về `proxy` khi heat phủ < 99,9%) hoặc `proxy` (voxel, cho mesh hở nặng, nhiều lớp chồng).
- `small_islands`: `follow_surface` (mỗi đỉnh chép weight của bề mặt dưới nó: thắt lưng, cổ áo) hoặc
  `rigid_nearest` (cả mảnh đi theo một điểm: mắt, nút).
- `side_band`: phần chiều cao quanh X = 0 được lẫn trái / phải.
- `limb_grow`: bề rộng dải blend của mỗi chi vào thân, theo phần chiều cao, đo dọc bề mặt.
- `locks`: ép một vùng về một xương (`z_min` + `falloff`, hoặc `box`). Luôn khoá vùng da dưới phụ kiện cứng.

## 3. Pose (dict Python)

```python
READY = {
    "Hips": {"loc": [0, 0, -0.012]},              # dời hông, mét, không gian armature
    "Spine": {"flex": 2}, "Head": {"flex": -3},   # góc giải phẫu, độ
    "UpperArm_R": {"side": -35, "flex": 22}, "LowerArm_R": {"flex": 50},
}
STRIKE = merge(READY, {
    "arm_R": {"reach": [30, -15, 0.95], "elbow": [0.6, 0.4, -0.7]},   # IK: azimuth, elevation, độ duỗi
    "Prop_Axe": {"aim": [-0.15, -0.97, -0.2], "front": [0, -0.2, -0.98]},  # hướng cán rìu, hướng lưỡi
    "leg_L": {"offset": [0, -0.03, 0], "roll": 0, "pivot": 0},          # chân: IK, mặc định cắm đất tại chỗ
})
```

| Khoá | Nghĩa |
|---|---|
| `flex` | gập tự nhiên (> 0): gối, khuỷu gập; lưng, đầu cúi; tay, đùi đưa ra trước; cổ chân hất mũi lên |
| `side` | dạng ra (> 0): tay, chân ra xa thân; lưng, đầu nghiêng sang phải nhân vật |
| `twist` | xoắn quanh xương (> 0 với lưng, đầu: xoay sang trái nhân vật); bên `_R` tự lật dấu, cùng số là đối xứng |
| `arm_X.reach` | `[azimuth, elevation, ext]` quanh vai, trong khung của ngực: az 0 trước, +90 ra ngang, -90 chéo qua thân, 180 sau; el +90 lên; ext = khoảng cổ tay / chiều dài tay |
| `arm_X.elbow` | hướng khuỷu ưu tiên (trái làm chuẩn: ra ngoài, ra sau, xuống) |
| `Prop_*.aim/front` | hướng trục đạo cụ và mặt trước; tool giải swivel khuỷu và xoay cẳng tay, cổ tay chỉ gánh phần còn lại |
| `leg_X.offset` | cổ chân dời bao nhiêu so với rest (0 = cắm đất tại chỗ) |
| `leg_X.roll / pivot` | nghiêng bàn chân (> 0 mũi lên) quanh gót (-1), cổ chân (0), mũi (+1) |
| `leg_X.follow` | 0 bàn chân phẳng theo mặt đất, 1 buông theo ống chân (chân đang nhấc) |
| `leg_X.fk` | 0..1 trộn sang góc FK của UpperLeg / LowerLeg / Foot (thân ngã, chân rời đất) |
| `leg_X.auto_heel` | mặc định true: cổ chân quá giới hạn thì thử nhấc gót |

`merge(a, b)` gộp sâu. `mirror(pose)` đổi trái phải. Kiểm pose: `check_anim --poses file.py --names A,B`.

## 4. Clip (`anim`)

```python
from rigkit.anim import Clip, gait
atk = Clip("Hero_Atk_Light_01", frames=30, loop=False, base=READY)
atk.key(0, {}, ease="stop")
atk.key(7, ANTIC, ease="stop")          # lấy đà, giữ một nhịp
atk.key(9, OVER)                        # breakdown: cung của vũ khí
atk.key(11, STRIKE, ease="impact")      # va chạm: tới nhanh, dừng gắt
atk.key(14, FOLLOW)
atk.key(30, {}, ease="stop")
atk.drag("Head", parent="Chest", delay=1, amount=0.25)   # overlap: đầu trễ theo ngực 1 frame
atk.spring("Prop_Sword", kind="turn", gain=0.6, max_deg=40, windows=[(11, 30)])   # lưỡi theo đà sau va chạm
atk.spring_channel(("Head", "flex"), gain=0.4, hz=2.4)                            # đầu gật theo đà rồi lắng
atk.event(8, "AE_Sfx", "anim.hero.swing")                # event quy chuẩn 7.4
CLIPS = [atk]
```

| API | Làm gì |
|---|---|
| `Clip(name, frames, loop, base, fps=30, hub_end=True, hub_start=None)` | clip; `base` là pose nền, mỗi key = base + phần đè; `hub_end=False` cho clip cố ý kết thúc ở pose khác (Die); `hub_start=False` cho clip được lệch frame đầu (Hit, quy chuẩn A-22; tên có `_Hit` thì mặc định vậy). Pose trung tâm là frame 0 của Idle cùng bộ: clip `Hero_Sword_Atk…` so với `Hero_Sword_Idle` |
| `key(frame, pose, ease)` | key pose đủ; ease: `auto` `stop` `impact` `burst` `linear` `hold` |
| `wave(target, amp, period, phase, frames)` | lớp sóng cộng thêm trên một kênh, ví dụ `("Hips","side")` hay `("Hips","loc",2)`; `frames=(f0,f1)` cho moving hold |
| `drag(bone, parent, delay, amount)` | overlap: xương giữ một phần hướng của cha từ `delay` frame trước (trễ cố định, không vọt quá) |
| `spring(bone, kind, gain, hz, zeta, max_deg, windows, fade, grip)` | theo đà (follow-through, `rigkit/follow.py`): xương treo trên lò xo tắt dần, cú dừng gắt thì đi tiếp, vọt quá rồi lắng. Chạy trong bake sau pose và IK, trước drag / drop, trên hướng của xương trong armature space. `kind="turn"`: theo hướng của chính xương (lưỡi đi tiếp sau cú chém); `"grip"`: con lắc quanh đầu xương, kéo bởi chuyển động thế giới của đầu xương `grip` (`spring("UpperArm_R", "grip", grip="Hand_R")`: vũ khí trễ theo cả cú nảy của thân). Ghi ngược thành góc giải phẫu trong giới hạn khớp (mềm), `max_deg` là trần mềm của góc. Mặc định `hz` 2,8 (một nhịp ~11 frame), `zeta` 0,3 (hai nhịp thấy rõ, lắng trong ~0,5 s), `gain` 1 = giữ nguyên vận tốc như khớp tự do. Nhiều lò xo chạy theo thứ tự thêm vào (grip trên tay rồi turn trên bàn tay). Không đặt lên Hips hay chân (chạy sau IK chân) |
| `spring_channel(target, gain, hz, zeta, room, windows, fade)` | lò xo trên kênh của key, trước bake: một xương (flex, side, twist), một kênh `("Head","flex")` hay danh sách. Theo đà của chính kênh đó (cái gật dừng gắt); kênh giải phẫu nằm trong giới hạn khớp (mềm), `room=(lo, hi)` giới hạn lệch của kênh không có giới hạn (kênh ảo) |
| `windows=[(start, end[, fade[, gain]])]` | (cả hai kiểu lò xo) lò xo bắt đầu từ trạng thái nghỉ ở `start` (cú dừng rơi đúng `start` vẫn đá), mờ về 0 trong `fade` frame trước `end`. Mặc định cả clip, mờ trước frame cuối trừ khi `hub_end=False`: frame đầu, cuối giữ đúng key (pose trung tâm A-22). Cửa sổ kết thúc trước khung chạm giữ cú đánh đúng key; cửa sổ bắt đầu ở khung chạm cho cú dừng lăn ra. Loop: chạy 3 vòng lấy vòng cuối, không có cửa sổ |
| `drop(bone, frame, velocity, spin, restitution, friction, mesh, inherit)` | đạo cụ rời tay, rơi theo trọng lực, nảy trên mặt đất; bắt đầu với `inherit` × vận tốc lúc thả + `velocity`. Thả ở key `impact` thì tay có thể đang đi 5–10 m/s: hạ `inherit` (chibi ví dụ trong failure-catalog.md: 0,1) hoặc thả sau frame dừng |
| `contact(foot, f0, f1)` | khai frame bàn chân phải chạm đất (gate trượt chân) |
| `event(frame, function, param)` | AnimationEvent, xuất ra `<fbx>.events.json` |
| `gait(clip, stride, lift, duty, bob, bob_phase, sway, hips_twist, arm_swing, arm_bones, heel_roll, toe_roll, follow_max)` | chu kỳ đi / chạy tại chỗ: chân trụ trượt lùi đúng tốc độ gốc (ghi vào clip) |

Key tay (`reach`, `aim`) được giải thành góc FK **một lần ở mỗi key**; giữa các key nội suy góc FK, nên tay đi cung
và không nhảy nghiệm. Chân giải IK mỗi frame (cắm đất, hở đất). Mọi frame được bake và key tuyến tính cho đủ xương.

Kênh vắng ở một key lấy giá trị của `base` ở key đó; kênh không có trong `base` thì nhận 0. Kênh ảo (một điều khiển do
file clip tự đọc, ví dụ mức nén) mà chỉ vài key dùng thì khai trong `base`, không các key còn lại kéo nó về 0.

Vật cầm hay vật lơ lửng: **một** `spring` trên hướng của xương mang vật, không đặt lò xo lên từng khớp của tay giải bằng
IK (các khớp dư bù trừ nhau, lò xo từng khớp phá thế bù: vũ khí lắc 95° lúc bật nhảy dù gần như không xoay).

## 5. Bảng mã gate

| Bước | Lỗi (chặn) | Cảnh báo |
|---|---|---|
| rig | ZERO_BONE, PAIR, NAME, LEAF, ROOT_DEFORM, MIRROR, PARENT, BEND | OUTSIDE, MIRROR_ROLL |
| skin | UNWEIGHTED, RIGID_BONE, RIGID_NODEFORM | HEAT_PARTIAL |
| check_weights | UNWEIGHTED, NOT_NORMALIZED, TOO_MANY_INFLUENCES, NONDEFORM_WEIGHT, CROSS_SIDE, STRAY_WEIGHT, TEAR, COLLAPSE, FLIP | TINY_WEIGHTS, DISCONNECTED, ACCESSORY_SKIN, PRESERVE_VOLUME, ENVELOPES |
| anim | | IK_REACH, ARM_LIMIT |
| check_anim (ngưỡng tính theo chiều cao nhân vật: `--height`, không có thì `prepare --height`, không có nữa thì tư thế nghỉ của da mềm và phần cứng trừ vũ khí / khiên cầm tay; báo cáo ghi `height_m`) | LIMIT, GROUND (da mềm), PROP_UNDER_FLOOR (mesh cứng: đạo cụ, vũ khí, mũ, mặt thấp hơn sàn > 2% chiều cao; chạm sàn được, xuyên thì không), FOOT_SLIDE, PROP_PENETRATION (sâu > 1% chiều cao, hoặc tỉ lệ điểm của đạo cụ nằm trong thân tăng > 4% so với rest), LIMB_PENETRATION, TEAR, FLIP, LOOP_SEAM, POP, ROOT_MOTION, SCALE_CHILDREN, DROP_FAR (đạo cụ rơi cách thân > 2 lần chiều cao), HUB_START / HUB_END (pose đầu / cuối lệch Idle f0 trung bình > 20° hoặc một xương > 90°) | FOOT_FLOAT, LOOP_VELOCITY, DROP_FAR (> 0,6 chiều cao), HUB_START / HUB_END (> 10° hoặc > 45°), LOW_AMPLITUDE (Idle < 8°, đòn < 20° trung bình), HUB_MISSING (không có Idle nền để so) |
| verify_fbx | ARMATURES, BONES_MISSING, LEAF_BONES, TAKE_MISSING, DEVIATION (đo sau khi gỡ nối mọi xương đã import: Unity không có "connect") | BONES_EXTRA, MESHES, RANGE |
