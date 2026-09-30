# Pack ExplosiveLLC: pack làm animation thế nào, quy chuẩn học gì

Phiên bản 0.1.1 · 2026-09-28 · Đọc và đo *RPG Character Mecanim Animation Pack FREE* 2.5.2 của Explosive (Asset Store, id
65284) trong `Assets/ExplosiveLLC`, trên Unity 6000.3.16f1. Luật rút ra đã vào `GameAnimation_QuyChuan.md` (cột "Mục" của
bảng ở mục 6). Tài liệu này giữ bằng chứng: pack làm gì, số đo được, cái gì không nên chép.

Cách đo: đọc file (`.controller`, `.meta`, code C#), rồi chạy Unity batch trên một bản chép của project: phát clip bằng
`PlayableGraph` trên nhân vật của pack, cộng root motion, dò độ cao và vị trí bàn chân, chạy controller của pack từng frame
ở 30 fps. Số đo trong editor, không phải trên máy thật. Pack chưa nằm trong git (asset Asset Store, 61 MB, trong đó 45 MB
FBX); ai cần mở thì tải bản FREE trên Asset Store.

---

## 0. Tóm tắt

Pack gồm một nhân vật Humanoid (rig 3ds Max), 89 clip cho hai bộ vũ khí (tay không, kiếm hai tay), một Animator
Controller, và code mẫu điều khiển nhân vật: di chuyển, nhảy, đánh, trúng đòn, rút / cất vũ khí, IK tay, NPC đi NavMesh.

Đáng học:

- Controller chia sub-state machine theo bộ vũ khí, trong mỗi bộ chia theo nhóm (Idle, Movement, Attacks, Damage,
  Jumping…). Nhóm nào cũng ra bằng Exit, rồi state machine cha chọn Idle hay Movement theo `Moving`.
- Any State cho mọi thứ ngắt được (trúng đòn, knockback, knockdown, nhảy, rơi): điều kiện có trigger, Can Transition To
  Self bật để trúng đòn liên tiếp vẫn chơi lại được.
- Layer thân trên (Override + Avatar Mask) để vừa chạy vừa đánh, vừa chạy vừa rút kiếm. Khi rảnh, layer đứng ở một state
  trống.
- Moveset combat đủ: trúng đòn theo hướng (trước ×2, sau, trái, phải), knockback có quãng lùi, knockdown nối getup, lăn,
  nhảy / rơi / tiếp đất / nhảy lần hai, đánh khi chạy.
- Event đặt ở frame va chạm (`Hit`), chạm chân (`FootL`, `FootR`), tiếp đất (`Land`), tay nắm vũ khí (`WeaponSwitch`).
- IK tay trái bám điểm cầm trên kiếm hai tay; Foot IK bật ở mọi state chạm đất.

Không chép:

- Một trigger chung + số `TriggerNumber` để chọn hành động, cộng một state trống `Attacks` để rẽ nhánh: trễ một frame (5.4).
- Root motion qua `OnAnimatorMove` cộng thêm vận tốc của code. Quy chuẩn chọn clip tại chỗ, code dời (7.2).
- Khoá di chuyển và hành động bằng số giây gõ tay trong code, không lấy từ clip.
- Blend tree 2D Freeform Cartesian với vị trí gõ tay theo input chuẩn hoá, trong khi tốc độ thật của các hướng lệch nhau 14%.
- Controller 4,2 MB mà 95% là object mồ côi, điều kiện trỏ tới tham số không có.
- Tên `RPG-Character@Unarmed-Attack-L1`, event không tiền tố, nén clip lẫn lộn Off và Keyframe Reduction.

---

## 1. Trong pack có gì

| Phần | Nội dung |
|---|---|
| Model | `RPG-Character.FBX` (nhân vật mà prefab dùng), `RPG-Character-Bones.FBX` (một bản rig khác, vài xương mang tên lỗi như `B_L_Forearm 1` thay cho bàn tay trái), `2Hand-Sword.FBX` |
| Clip | 89 clip trong 88 FBX: `Animations/Unarmed` 40 file (file Jump chứa cả Jump và Land), `Animations/2Hand-Sword` 48 file |
| Avatar | `RPG-Character-Avatar.ht` (Human Template: bảng map xương sang Humanoid), `RPG-Character-Upperbody-AvatarMask.mask` |
| Controller | `RPG-Character-Animation-Controller.controller` |
| Code | `RPGCharacterController` (điểm vào, Actions API), `RPGCharacterMovementController` (di chuyển, dựa trên SuperCharacterController), `RPGCharacterWeaponController`, `RPGCharacterInputController` (Input Manager cũ), `RPGCharacterNavigationController` (NavMeshAgent), `RPGCharacterAnimatorEvents` (nhận event, chuyển root motion), `IKHands`, `AnimatorExtensions`, các enum trong `Lookups/` |
| Demo | scene `RPG-Character.unity`, prefab người chơi và NPC, camera, GUI bấm thử mọi hành động |
| Kèm theo | SuperCharacterController 2.0 (va chạm và bám đất tự viết), preset Input Manager và Tags & Layers, gói `InputSystem - Requires InputSystem Package.unitypackage`, ảnh thiết lập import FBX vào Blender, một script Editor hiện hộp nhắc |

Tài liệu gốc (link trong `Documentation/`): ReadMe, Component API, Actions API. ReadMe dạy thay model bằng cách kéo một model
Humanoid khác vào dưới prefab rồi gán controller của pack: pack dựa hẳn vào retarget Humanoid lúc chạy.

---

## 2. Rig và import

| Mục | Pack | So với quy chuẩn |
|---|---|---|
| Cây xương | 3ds Max: `Motion` (gốc) → `B_Pelvis` → `B_Spine`, `B_Spine1`, `B_Spine2`; `B_L_Thigh`…; ngón `B_L_Finger0` … `B_L_Finger42`. 55 transform, 53 xương có skin | có gốc tách khỏi hông như `Root` / `Hips` của 5.2; tên kiểu `B_L_UpperArm` phải đổi thành `UpperArm_L` nếu dùng Generic |
| Kiểu rig | Humanoid. Model: Create From This Model; 89 clip: Copy From Other Avatar (avatar của `RPG-Character.FBX`); map lưu thành `.ht` | lúc chạy dùng Generic (5.4); lưu `.ht` giống 8.1 khuyên |
| Avatar | twist của cánh tay, cẳng tay, đùi, cẳng chân 100% (mặc định 50%); Arm Stretch 1 (mặc định 0,05), Leg Stretch 0 | |
| Đơn vị | Scale Factor 0,01, Convert Units tắt; kiếm cũng 0,01; riêng bản `-Bones` thì Scale Factor 1, Convert Units bật | Scale Factor 1, Convert Units bật (8.1), gate A-2 |
| Kích thước | cao khoảng 2,6 m, hông cao khoảng 1,3 m, `humanScale` 1,47; capsule cao 2,5 m | người tả thực 1,7–1,8 m (5.1). Root motion Humanoid nhân theo `humanScale` (cỡ avatar so với avatar chuẩn của Unity), nên cùng clip đi xa hơn trên nhân vật to |
| Mesh | 1 SkinnedMeshRenderer, 2512 đỉnh, 1256 tam giác, 1 material; 99,7% đỉnh chỉ 1 influence (skin cứng, nhân vật khối) | trong ngân sách NPC (5.6) |
| fps | 30 fps mọi clip; cycle chạy 24 frame | 30 fps (3.1) |
| Nén | 41 file FBX để Off, 47 file Keyframe Reduction and Compression, sai số 0,5 | Optimal (8.1), gate A-6 |
| Root Transform | Based Upon: Original ở mọi clip (riêng Fall: XZ theo Center of Mass). Bake Into Pose XZ tắt ở locomotion, rơi, một số đòn kiếm; bật ở idle, đòn tay không, knockback, getup. Cùng một hành động mà hai bộ đặt khác nhau (lăn, tiếp đất) | 7.2; cùng hành động ở mọi bộ phải cùng thiết lập (A-6) |
| Mask của clip | None (cả thân) | |
| Event | lưu trong import settings, thời gian chuẩn hoá 0–1 | 7.4 |

---

## 3. Animator Controller

### 3.1 Tham số (16)

| Tham số | Kiểu | Dùng để |
|---|---|---|
| `Trigger` | trigger | trigger duy nhất cho mọi hành động |
| `TriggerNumber` | int | chọn nhóm hành động, theo enum `AnimatorTrigger`: 4 đánh, 12 trúng đòn, 15 cất vũ khí, 16 rút, 18 nhảy / rơi / tiếp đất, 25 đổi bộ ngay, 26 knockback, 27 knockdown, 28 lăn… |
| `Action` | int | chọn biến thể trong nhóm: đòn 1–11, hướng trúng đòn 1–5 |
| `Weapon` | int | bộ vũ khí: 0 tay không, 1 kiếm hai tay |
| `Side` | int | tay: 0 không, 1 trái, 2 phải |
| `Jumping` | int | 0 trên đất, 1 đang lên, 2 đang rơi, 3 nhảy lần hai |
| `Moving` | bool | có di chuyển không; state machine cha dựa vào đây để chọn Idle hay Movement |
| `Velocity X`, `Velocity Z` | float | vận tốc cục bộ theo tỉ lệ (chạy 1, đi 0,5) cho blend tree |
| `Idle` | float | 0 idle có thở, 1 idle tĩnh |
| `AnimationSpeed` | float | nhân vào tốc độ của 56 / 59 state |
| `WeaponSwitch`, `LeftWeapon`, `RightWeapon`, `SheathLocation`, `Aiming` | int / bool | phục vụ đổi vũ khí; `Aiming` bản FREE không dùng |

Code đặt tham số chọn trước, bắn trigger sau (`AnimatorExtensions.SetActionTrigger`):

```csharp
animator.SetInteger(Action, actionNumber);        // biến thể
animator.SetInteger(TriggerNumber, (int)trigger); // nhóm
animator.SetTrigger(Trigger);
```

### 3.2 Layer

| Layer | Blend | Weight | Mask | IK Pass | Làm gì |
|---|---|---|---|---|---|
| Base Layer | Override | 1 | không | bật | mọi thứ; IK Pass cho IK tay trái (4.6) |
| Upperbody | Override | 1 | thân trên: Body, Head, hai tay, ngón, IK tay; tắt Root, chân, IK chân | tắt | đánh khi chạy, rút / cất kiếm khi chạy; rảnh thì đứng ở state trống `Null` |

### 3.3 Cây state machine

```
Base Layer
├─ Any State → Jump, Fall, Land, GetHit F1/F2/B1/L1/R1, Knockback 1/2, Knockdown, DiveRoll, Sheath, Unsheath,
│              Attacks (trống), Idle của bộ (đổi bộ ngay)     điều kiện: Trigger + TriggerNumber + Weapon (+ Action, Moving)
├─ Unarmed                                sub-state machine của bộ tay không
│  ├─ Unarmed-Idle          blend tree 1D theo Idle: Idle ↔ Idle-Static
│  ├─ Unarmed-Movement      blend tree 2D Freeform Cartesian theo Velocity X / Z, 17 clip
│  ├─ Unarmed-Attacks       Attacks (trống) → Attack-L1…L3, R1…R3 theo Action; mỗi đòn → Exit
│  ├─ Unarmed-Damage        GetHit ×5, Knockback ×2, Knockdown → Getup; → Exit
│  ├─ Unarmed-Jumping       Jump, Fall (loop) ↔ Jump-Flip, Land → Exit
│  └─ Unarmed-Rolls-Dodges  DiveRoll → Exit
│     Exit của mọi nhóm → cha chọn: !Moving → Unarmed-Idle, Moving → Unarmed-Movement
└─ 2Hand-Sword                            cùng khung, thêm nhóm 2Hand-Sword-WeaponSwitch (Sheath, Unsheath)
Upperbody
├─ Any State → Run-Forward-Attack (theo Side, Weapon), Sheath, Unsheath      điều kiện: Moving + Trigger + TriggerNumber
└─ Null (trống, mặc định) ← mọi hành động xong ra Exit về đây
```

Đang dùng: 59 state, 18 state machine, 148 transition. Mọi state bật Write Defaults. 39 state bật Foot IK (các state chạm
đất; tắt ở nhảy, rơi, lăn, knockdown).

### 3.4 Transition

| Chuyển | Pack | Ghi chú |
|---|---|---|
| Any State → đòn, nhảy | 0 s, không Exit Time | vào thẳng |
| Any State → trúng đòn, knockback, knockdown, lăn, tiếp đất | 0,05 s | |
| Any State → rơi | 0,2 s | |
| Any State → rút / cất vũ khí | 0,1 s | |
| Đòn → Exit | Exit Time 0,8, blend 0,1 s | 20% cuối clip không bao giờ chạy |
| Trúng đòn → Exit | Exit Time 0,75 (tay không), 0,9 (kiếm), blend 0,1 s | |
| Lăn, knockback, getup → Exit | Exit Time 0,8–0,9, blend 0,1 s | |
| Idle → Exit khi `Moving` | 0,05 s (tay không), 0,1 s (kiếm), không Exit Time | |
| Movement → Exit khi `!Moving` | 0,1 s, không Exit Time | |
| Movement → Exit khi rút / cất kiếm | 0,5 s | sang bộ kia |
| Đòn thân trên khi chạy → `Null` | Exit Time 0,69, blend 0,25 s (tay không); 0,9 và 0,1 s (kiếm) | |
| Knockdown → Getup | Exit Time 1 (tay không), 0 (kiếm), 0 s | đo: cả hai đều chạy hết clip ngã rồi mới sang getup |
| Any State | Can Transition To Self bật ở mọi transition trừ bốn cái vào Fall, Land | có trigger nên không bị gọi lại mỗi frame |

Transition có thời lượng đều bật Fixed Duration. Thứ tự: Unity xét transition của Any State trước transition của state
hiện tại, theo thứ tự trong danh sách (manual Unity, Transitions).

### 3.5 Blend tree

Locomotion của cả hai bộ: 2D Freeform Cartesian, tham số `Velocity X`, `Velocity Z`.

| Vị trí | Clip | Tốc độ thật (5.2) |
|---|---|---|
| (0, 0) | Idle | 0 |
| (0, 1) · (0, −1), (±1, 0) | Run F · Run B, L, R | 6,2 · 5,4 m/s |
| (±0,75, ±0,75) | Run FL, FR, BL, BR | 5,3 m/s |
| (0, ±0,5), (±0,5, 0) | Strafe F, B, L, R | 2,9–3,1 m/s |
| (±0,35, ±0,35) | Strafe chéo | 3,1 m/s |

Vị trí gõ tay theo input chuẩn hoá. Ô chéo xa tâm hơn ô thẳng (1,06 so với 1) nhưng clip chéo chạy chậm hơn 14%. Manual
Unity dành Freeform Cartesian cho trục không phải hướng (tốc độ thẳng + tốc độ xoay); clip theo hướng có nhiều tốc độ thì dùng
Freeform Directional. Bộ kiếm có clip Walk, Walk-Slow nhưng không nằm trong blend tree nào.

Idle: blend tree 1D theo `Idle`: 0 = Idle (thở), 1 = Idle-Static.

### 3.6 Rác trong file

File 4,2 MB chứa 3715 state machine, 182 state, 2768 transition; phần đang dùng là 18, 59, 148. Còn lại là object mồ côi,
có lẽ sót từ lúc cắt bản trả phí xuống bản FREE. Sáu điều kiện trỏ tới tham số không có trong danh sách (`Injured` ×4,
`Crouch` ×2). Unity không báo gì cả. Quy chuẩn thêm gate A-15.

---

## 4. Code điều khiển animation thế nào

### 4.1 Actions API

- `RPGCharacterController` giữ các action handler theo tên (`"Attack"`, `"GetHit"`, `"Jump"`…). Input gọi `CanStartAction`,
  `StartAction(tên, context)`; handler đặt tham số Animator (3.1) và khoá di chuyển / hành động (4.2).
- Ba kiểu handler: `SimpleActionHandler` (bật / tắt như công tắc), `InstantActionHandler` (bắt đầu rồi kết thúc ngay),
  `MovementActionHandler` (gắn với state của movement controller).
- Chọn đòn: `AnimationData.RandomAttackNumber` bốc ngẫu nhiên trong danh sách biến thể (`AnimationVariations`). Trúng đòn:
  bốc 1 trong 5 clip rồi suy ra hướng bị đẩy (`AnimationData.HitDirection`).

### 4.2 Khoá theo số giây

| Hành động | Code khoá | Clip thật (30 fps) |
|---|---|---|
| Đòn tay không | 0,75 s (`AttackDuration`) | 0,8 s, ra ở 0,64 s + 0,1 s blend |
| Đòn kiếm | 1,1 s | 1,2 s, ra ở 0,96 s + 0,1 s |
| Trúng đòn | chờ 0,1 s rồi khoá 0,4 s | 0,5 s, ra ở 0,375 s + 0,1 s |
| Knockdown | 5,25 s | ngã 1,53 s + getup 4 s; đo: về Idle ở frame 159 (5,3 s) |
| Lăn | 1 s | 1,2 s, ra ở 0,96 s (tay không) hoặc 1,08 s (kiếm) |
| Rút / cất kiếm | khoá 1 s; đợi 0,75 s (rút), 0,55 s (cất) rồi mới đặt `Weapon` | 1 s; event `WeaponSwitch` ở 0,36 s (rút), 0,42 s (cất) |

Số trong code do người gõ cho gần với clip: sửa clip là phải nhớ sửa code. IK tay cũng tắt theo số giây (0,6 s khi trúng
đòn, 1,05 s khi lăn, 5,25 s khi knockdown). Quy chuẩn lấy các mốc này từ bảng frame data (3.7).

### 4.3 Di chuyển: code và root motion cùng lúc

- `RPGCharacterMovementController` kế thừa `SuperStateMachine` của SuperCharacterController, có state Idle, Move, Jump,
  DoubleJump, Fall, DiveRoll, Knockback, Knockdown; mỗi state có `_EnterState`, `_SuperUpdate`, `_ExitState`.
- Mỗi frame code tự dời `transform.position += currentVelocity * dt`, với `runSpeed = 1`, `walkSpeed = 0,5`, và ghi vận
  tốc cục bộ vào `Velocity X`, `Velocity Z`.
- Cùng lúc, `RPGCharacterAnimatorEvents` có `OnAnimatorMove`, chuyển `deltaPosition` của Animator lên object cha. Clip chạy
  có root motion 6,2 m/s, nên nhân vật đi khoảng 7,2 m/s (1 của code cộng 6,2 của clip). Comment trong
  `RPGCharacterNavigationController` ghi tỉ lệ 7 : 1.
- `LockMovement` / `UnlockMovement` bật / tắt `applyRootMotion` nhưng không đổi được gì: khi có `OnAnimatorMove`, Animator
  vẫn đưa root motion cho hàm đó cả lúc `applyRootMotion` tắt (đo, 5.6).
- Nhảy do code tính: `jumpSpeed` 12, trọng lực lúc lên 24, lúc rơi 32 (rơi nhanh hơn lên), nhả nút sớm thì vận tốc lên bị hạ
  xuống 1/4. Clip chỉ là pose: Jump lúc lên, Fall (loop) khi vận tốc dọc âm, Land khi chạm đất, Jump-Flip khi nhảy lần
  hai; tham số `Jumping` báo đang ở pha nào.
- NPC: `NavMeshAgent` dời nhân vật ở 7 m/s, code đặt `Velocity Z = 1` (Run F, 6,2 m/s); `OnAnimatorMove` bỏ qua lúc đang
  đi NavMesh. Chân trượt khoảng 12%.

### 4.4 Đánh, trúng đòn

- Đứng yên: Any State → `Attacks` (trống) → đòn theo `Action`, khoá theo `AttackDuration`.
- Đang chạy: layer Upperbody chơi `Run-Forward-Attack` (clip toàn thân vừa chạy vừa đấm); mask chỉ lấy thân trên, chân vẫn
  theo blend tree chạy ở Base; không khoá di chuyển.
- Trúng đòn: code bốc biến thể và hướng, đẩy bằng lực Rigidbody (8 ± 4, nhân 10, trong 0,1 s), cộng thêm root motion của
  clip knockback (lùi 2,2 m hoặc 1,3 m).
- Event `Hit` chỉ gọi UnityEvent `OnHit` cho hiệu ứng, không mở hitbox.

### 4.5 Rút / cất vũ khí

- Action `SwitchWeapon` → `RPGCharacterWeaponController` xếp coroutine vào hàng đợi: đặt `WeaponSwitch`, `Weapon`, bắn
  trigger 16 (rút) hoặc 15 (cất), đợi số giây cố định, rồi đặt `Weapon`, `LeftWeapon`, `RightWeapon`.
- Model kiếm nằm sẵn dưới `B_R_Hand` trong prefab; rút / cất chỉ bật / tắt model (`SetActive`) khi event `WeaponSwitch` bắn.
  Không có kiếm đeo sau lưng: tham số `SheathLocation` có nhưng bản FREE chỉ dùng lưng và không hiện kiếm ở đó.
- Đang chạy thì rút / cất ở layer Upperbody; đứng yên thì ở Base, xong Exit sang Idle của bộ mới.

### 4.6 IK tay trái

- `IKHands` nằm trên object có Animator; Base Layer bật IK Pass; `OnAnimatorIK` kéo `AvatarIKGoal.LeftHand` về `AttachPoint`
  (con đầu tiên của model kiếm). ReadMe dặn chỉnh chỗ cầm lúc đang chạy game rồi chép transform ra.
- Weight 0 → 1 trong 1 s sau khi rút kiếm (trễ 0,75 s), 1 → 0 trong 0,2 s khi cất. Khi trúng đòn, lăn, knockback, knockdown
  thì tạm tắt: 0,1 s xuống, giữ theo số giây của từng hành động, 0,1 s lên.

### 4.7 Event

| Event | Có trong | Code dùng |
|---|---|---|
| `FootL`, `FootR` | 40 / 41 clip: locomotion, lăn, knockback | UnityEvent, pack không nối vào đâu |
| `Hit` | 18 đòn, ở frame va chạm | UnityEvent `OnHit` |
| `Land` | 2 clip tiếp đất, frame 0 | UnityEvent `OnLand` |
| `WeaponSwitch` | rút, cất | bật / tắt model kiếm |
| `Shoot` | không clip nào (bản FREE) | |

Receiver `RPGCharacterAnimatorEvents` được `AddComponent` lúc Awake. Thiếu receiver thì Unity báo `AnimationEvent 'FootR'
on animation 'Unarmed-Run-Forward' has no receiver!` (ReadMe). Cả 8 hướng chạy dùng chung một mốc chân (phải 0,35, trái
0,84), trong khi điểm thấp nhất của bàn chân đổi theo hướng (5.2): tiếng bước chân lệch ở các hướng không phải phía trước.

### 4.8 Tốc độ, làm chậm, input

- `animationSpeed` của controller được ghi vào tham số `AnimationSpeed` mỗi `LateUpdate`. Ba state không gắn tham số này
  (`Null` và hai đòn tay không khi chạy) nên không chậm theo.
- `SlowTime` đổi `Time.timeScale` (phím T: 0,0125; phím P: 0), dừng cả game chứ không riêng nhân vật.
- `RPGCharacterInputController` đọc Input Manager cũ (`Input.GetButtonDown("AttackL")`…) trong try / catch, cần preset
  Input của pack. `AnimatorExtensions` và `RPGCharacterWeaponController` ghi `Debug.Log` ở mỗi lần bắn trigger, đổi vũ khí
  (82 lệnh log trong `Code/`).

---

## 5. Số đo

### 5.1 Hành động (30 fps)

| Clip | Frame | Frame va chạm (`Hit`) | Tới va chạm | So với quy chuẩn |
|---|---|---|---|---|
| Unarmed-Attack L1–L3, R1–R3 | 24 (0,8 s) | 9,4–11,2 | 314–373 ms | người chơi: đòn nhẹ ≤ 100 ms (3.2), chậm gấp ba. Quái: dưới 400 ms là đòn nhanh, phải có dấu hiệu từ trước (3.3) |
| 2Hand-Sword-Attack 1–10 | 36 (1,2 s) | 13,8–15,2 | 461–506 ms | người chơi: đòn nặng ≤ 300 ms, chậm. Quái thường ≥ 500 ms: vừa chạm ngưỡng |
| 2Hand-Sword-Attack11 | 36 | 21,6 | 721 ms | hợp đòn mạnh của quái, boss (600–1000 ms) |
| 2Hand-Sword-Run-Forward-Attack1 (khi chạy) | 24 | 9,3 | 309 ms | |
| GetHit | 15 (tay không); 20 trước / sau, 15 trái / phải (kiếm) | | | ra ở Exit Time 0,75 / 0,9 |
| Knockback-Back1, Back2 | 36, 26 | | | lùi 2,2 m, 1,3 m bằng root motion (5.3) |
| Knockdown1 → Getup1 | 46 + 120 (1,5 s + 4 s) | | | getup 4 s: dài với game mobile |
| DiveRoll-Forward1 | 36 (1,2 s) | | | lăn 6,7 m |
| Jump, Fall (loop), Land, Jump-Flip | 19, 32, 13, 14 | | | Land cắt từ cùng take với Jump (frame 21–34) |
| Sheath, Unsheath | 30 (1 s) | `WeaponSwitch` ở 12,7 / 10,9 | | |
| Idle, Idle-Static | 50 (tay không), 60 (kiếm) | | | |

### 5.2 Locomotion (trên nhân vật của pack, `humanScale` 1,47)

Cycle nào cũng 24 frame (0,8 s). Mốc chân lấy từ event của pack; điểm thấp nhất của bàn chân là số đo (thời gian chuẩn hoá).

| Clip (tay không) | Tốc độ | Mỗi bước | Mốc chân theo event | Điểm thấp nhất của bàn chân |
|---|---|---|---|---|
| Run F | 6,2 m/s | 2,47 m | phải 0,35 · trái 0,84 | phải 0,35 · trái 0,98 |
| Run FL, FR | 5,3 m/s | 2,12 m | như Run F | phải 0,40 · trái 0,94 / 0,85 |
| Run L, R | 5,4 m/s | 2,17 m | như Run F | L: phải 0,06 · trái 0,56; R: phải 0,94 · trái 0,40 |
| Run B, BL, BR | 5,3–5,4 m/s | 2,13–2,17 m | như Run F | phải quanh 0 (0,90–1,00) · trái 0,52–0,60 |
| Strafe F, FL, FR, L | 3,0–3,1 m/s | 1,19–1,25 m | trái 0 · phải 0,50–0,52 | |
| Strafe B, BL, BR, R | 2,9–3,1 m/s | 1,15–1,23 m | phải 0 · trái 0,45–0,54 | |

Bộ kiếm tương tự: Run F 6,2 m/s, mốc chân phải 0,22 · trái 0,73 (khớp điểm thấp nhất đo được 0,23 · 0,73); Walk 3,1 m/s,
Walk-Slow 1,6 m/s (48 frame, cùng bước 1,25 m).

- Pack chép một mốc chân cho cả 8 hướng chạy, trong khi điểm thấp nhất của bàn chân ở Run L, R, B lệch Run F 0,3–0,4 chu
  kỳ: tiếng bước chân lệch, và blend giữa hướng thẳng với hướng ngang không cùng pha.
- So cùng một chân, vòng Strafe lệch vòng Run 0,15–0,4 chu kỳ tuỳ hướng: chân xoắn khi tốc độ nằm giữa đi và chạy. Quy chuẩn
  7.1 mục 4.
- `AnimationClip.averageSpeed` của clip Humanoid tính theo tỉ lệ (Run F báo 4,20); nhân `humanScale` 1,47 mới ra 6,2 m/s
  trên nhân vật này.

### 5.3 Quãng dời của hành động (root motion)

| Clip | Quãng | `averageSpeed` báo |
|---|---|---|
| DiveRoll-Forward1 (tay không, Bake Into Pose XZ bật) | tới 6,69 m | 0 |
| 2Hand-Sword-DiveRoll-Forward1 (Bake Into Pose XZ tắt) | tới 6,67 m | 3,78 |
| Knockback-Back1 / Back2 (Bake Into Pose XZ bật) | lùi 2,17 m / 1,26 m | 0 |
| Mọi đòn đánh đứng tại chỗ, trúng đòn, getup, tiếp đất | 0 | 0 |

Clip Based Upon = Original mà dời bằng node gốc của file vẫn nhả root motion dù đã tick Bake Into Pose, và `averageSpeed`
báo 0. Muốn biết quãng dời phải phát clip rồi cộng root motion, không đọc `averageSpeed` (quy chuẩn 7.2).

### 5.4 Độ trễ trong controller của pack (30 fps)

| Đường đi | Frame mà state đích bắt đầu | Ghi chú |
|---|---|---|
| Idle → đòn: Any State → `Attacks` (trống) → đòn | 2 | frame 1 đứng ở state trống, pose chưa đổi |
| Idle → trúng đòn: Any State → clip | 1 | |
| Idle → chạy: Exit → state machine cha → Entry của Movement | 1 | chuỗi Exit / Entry không tốn frame |
| Chạy → Idle | 1 | blend 0,1 s |
| Đòn thân trên khi chạy (layer Upperbody, Any State) | 1 | |

State trống rẽ nhánh tốn đúng một frame (33 ms ở 30 fps), ăn vào ngân sách phản hồi 100 ms của 3.2. Quy chuẩn 8.3, gate A-18.

### 5.5 Layer phụ và state chờ

Base chạy Run F; layer Override mask thân trên, weight 1. Đo góc lệch lớn nhất của xương thân trên (Spine1, Neck, Head,
cánh tay, cẳng tay) so với chỉ có Base, sau 0,3 s (layer đang chờ) và sau khi một đòn chạy xong rồi về lại state chờ.
Humanoid dùng rig và mask của pack; Generic dùng bản chép import lại thành Generic, mask transform từ `B_Spine1` trở lên.
Chân luôn lệch 0°.

| Rig | Write Defaults | State chờ | Lúc chờ | Sau đòn, về state chờ |
|---|---|---|---|---|
| Humanoid | bật | Motion = None | 0° | 0° |
| Humanoid | bật | clip rỗng (không curve) | 0° | 0° |
| Humanoid | tắt | Motion = None | **59°** | **74°** |
| Humanoid | tắt | clip rỗng | 0° | 0° |
| Generic | bật | Motion = None | 0° | 0° |
| Generic | bật | clip rỗng | 0° | 0° |
| Generic | tắt | Motion = None | 0° | **62°** (thân trên kẹt ở pose của đòn) |
| Generic | tắt | clip rỗng | 0° | 0° |

Pack dùng Motion = None + Write Defaults bật nên không lỗi. Quy chuẩn 8.3, gate A-17.

### 5.6 Root motion khi tắt Apply Root Motion

| Clip Run F | Transform dời trong một cycle | `deltaPosition` |
|---|---|---|
| Apply Root Motion bật, không có `OnAnimatorMove` | 4,93 m | 4,93 m |
| Apply Root Motion tắt, không có `OnAnimatorMove` | 0 | 0 |
| Có `OnAnimatorMove` (không tự dời), Apply Root Motion tắt | 0 | hàm nhận 4,93 m |
| Có `OnAnimatorMove` (không tự dời), Apply Root Motion bật | 0 | hàm nhận 4,93 m |

Có `OnAnimatorMove` thì Unity không tự dời nữa và đưa root motion cho hàm đó, bất kể Apply Root Motion bật hay tắt. Clip
tại chỗ đúng nghĩa khi Apply Root Motion tắt **và** không component nào trên object có Animator viết hàm này. Quy chuẩn 7.2,
gate A-16.

---

## 6. Quy chuẩn học gì, sửa gì, bỏ gì

| Kỹ thuật | Pack | Quy chuẩn | Mục |
|---|---|---|---|
| Sub-state machine theo bộ vũ khí, trong mỗi bộ theo nhóm | có | học | 8.3 |
| Nhóm ra bằng Exit, cha chọn Idle / Loco theo cờ di chuyển | có | học (`IsMoving`) | 8.3 |
| Any State cho hành động ngắt được, lọc theo bộ, Can Transition To Self + trigger | có | học, thêm thứ tự ưu tiên | 8.3 |
| Một trigger + `TriggerNumber` + `Action` | có | sửa: một trigger mỗi nhóm + `Variant` + `Stance` | 8.3 |
| State trống `Attacks` để rẽ nhánh | có | bỏ: trễ một frame | 8.3, A-18 |
| Layer thân trên + mask cho đánh / rút kiếm khi chạy | có | học | 8.3 |
| State chờ `Null`: Motion None + Write Defaults bật | có | học, ghi rõ điều kiện | 8.3, A-17 |
| Blend tree 2D Freeform Cartesian, vị trí gõ tay | có | sửa: Freeform Directional, vị trí = vận tốc thật | 8.3 |
| Idle 1D thở ↔ tĩnh | có | học | 8.3 |
| Root motion cho di chuyển qua `OnAnimatorMove`, cộng vận tốc code | có | bỏ: clip tại chỗ, một nguồn dời | 7.2, A-16 |
| Khoá theo số giây gõ tay | có | bỏ: lấy từ bảng frame data | 3.7 |
| Exit Time 0,75–0,9 che đuôi clip | có | sửa: cắt đuôi ở import | 3.5 |
| Event `Hit`, `FootL`, `FootR`, `Land`, `WeaponSwitch` | có | đổi sang `AE_` | 7.4 |
| Receiver `AddComponent` lúc chạy | có | sửa: có sẵn trong prefab | 7.4 |
| IK tay trái trên vũ khí hai tay, tắt IK theo số giây | có | học ý; weight IK theo curve của clip | 8.6 |
| Foot IK trên state chạm đất | có (Humanoid) | ghi nhận; Generic không có | 8.6 |
| Tham số `AnimationSpeed` nhân vào từng state | có | sửa: `Animator.speed` | 8.3 |
| `Time.timeScale` để làm chậm | có | chỉ cho làm chậm cả cảnh; hitstop dùng `Animator.speed` | 7.5 |
| Knockback bằng lực ngẫu nhiên cộng root motion | có | bỏ: code dời theo frame data | 7.5 |
| Trúng đòn theo hướng, knockback, knockdown → getup, lăn, nhảy lần hai | có | học vào moveset | 7.3 |
| Gốc logic (collider, controller) tách khỏi object model có Animator | có | học | 7.4 |
| Human Template `.ht`, Copy From Other Avatar | có | đã có (8.1) | 8.1 |
| Tên `Model@Clip` có dấu `-` | có | đổi theo 4.2 | 4.2 |
| Nén lẫn Off / Keyframe Reduction | có | Optimal | 8.1, A-6 |
| Controller đầy object mồ côi, điều kiện trỏ tham số không có | có | bỏ, thêm gate | 8.3, A-15 |

---

## 7. Chuyển clip của pack sang quy chuẩn

1. Để pack nguyên chỗ Asset Store đặt (`Assets/ExplosiveLLC`), không sửa file của pack, để còn cập nhật được. Coi clip của
   pack như clip mocap thô (5.8): không vào gameplay trực tiếp.
2. Chọn clip cần dùng, ghi vào brief.
3. Đo trên nhân vật đích: độ dài, fps, tốc độ gốc, quãng dời, frame của event (tool "Đo clip", 12.3; lượt đọc pack này đo
   bằng script batch).
4. Retarget sang cây chuẩn và lưu Generic (5.8), đặt tên theo 4.2 (bảng dưới). Bộ `Unarmed` / `Sword2H` thành đoạn `<Bộ>`.
5. Quãng dời vào bảng frame data (3.7), tốc độ gốc ghi vào clip locomotion (7.2); clip chạy tại chỗ.
6. Đổi event (bảng dưới). Frame `Hit` là frame active đầu tiên trong bảng frame data.
7. Người chơi: cắt bớt lấy đà cho vừa 3.2. Quái: kiểm telegraph theo 3.3.
8. Chỉnh pha chân bằng Cycle Offset cho cả blend tree cùng pha (7.1).
9. Dựng controller theo 8.3, không dùng controller của pack.
10. Duyệt như clip mocap: làm sạch, cường điệu, chỉnh theo mood (2.1).

| Pack | Theo quy chuẩn (archetype `HumanM`) |
|---|---|
| `RPG-Character@Unarmed-Idle`, `…-Idle-Static` | `HumanM_Unarmed_Idle`, `HumanM_Unarmed_Idle_Static` |
| `…@Unarmed-Run-Forward`, `…-Run-Backward-Left` | `HumanM_Unarmed_Loco_Run_F`, `HumanM_Unarmed_Loco_Run_BL` |
| `…@Unarmed-Strafe-Left` | `HumanM_Unarmed_Loco_Strafe_L` |
| `…@Unarmed-Attack-L1`, `…-Attack-R2` | `HumanM_Unarmed_Atk_PunchL_01`, `HumanM_Unarmed_Atk_PunchR_02` |
| `…@Unarmed-Run-Forward-Attack1-Left` | `HumanM_Unarmed_Atk_RunPunchL` (layer UpperBody) |
| `…@2Hand-Sword-Attack1` | `HumanM_Sword2H_Atk_01` |
| `…@Unarmed-GetHit-F1`, `…-GetHit-B1` | `HumanM_Unarmed_Hit_Light_01_F`, `HumanM_Unarmed_Hit_Light_01_B` |
| `…@Unarmed-Knockback-Back1` | `HumanM_Unarmed_Hit_Knockback_01_B` |
| `…@Unarmed-Knockdown1`, `…-Getup1` | `HumanM_Unarmed_Hit_Knockdown`, `HumanM_Unarmed_Hit_Getup` |
| `…@Unarmed-Stunned` | `HumanM_Unarmed_Hit_Stun_Loop` |
| `…@Unarmed-Jump`, `Fall`, `Land`, `Jump-Flip` | `HumanM_Unarmed_Jump_Start`, `_Jump_Loop`, `_Jump_End`, `_Jump_Double` |
| `…@Unarmed-DiveRoll-Forward1` | `HumanM_Unarmed_Def_Roll_F` |
| `…@2Hand-Sword-Unsheath-Back-Unarmed`, `…-Sheath-…` | `HumanM_Sword2H_Equip_Draw_Back`, `HumanM_Sword2H_Equip_Sheath_Back` |

| Event của pack | Theo quy chuẩn (7.4) |
|---|---|
| `FootL`, `FootR` | `AE_Footstep` (0 trái, 1 phải); đặt lại theo từng clip, không chép một mốc cho mọi hướng |
| `Hit`, `Shoot` | không thành event: là frame active đầu tiên trong bảng frame data, code mở hitbox (7.4); tiếng vung, vệt kiếm bằng `AE_Sfx`, `AE_Vfx` |
| `Land` | `AE_Sfx` (`anim.<subject>.land`), thêm `AE_Shake` nếu tiếp đất nặng |
| `WeaponSwitch` | `AE_Prop` (`Sword:Hand_R` khi rút, `Sword:Back` khi cất) |

---

## 8. Mở pack trong project này

- Project đặt Active Input Handling = Input System Package. Code demo của pack đọc `UnityEngine.Input` (input controller,
  camera, GUI demo), nên scene demo báo lỗi mỗi frame và nhân vật không nhận phím. Muốn bấm thử: giải nén gói
  `InputSystem - Requires InputSystem Package.unitypackage` rồi thay `RPGCharacterInputController` bằng
  `RPGCharacterInputSystemController`; hoặc đặt Active Input Handling = Both (sửa thiết lập chung, hỏi team trước).
- `Assets/ExplosiveLLC/Editor/SetupInputLayers.cs` là `AssetPostprocessor` mở cửa sổ nhắc mỗi lần có asset import xong. Xoá
  file này (pack tự dặn vậy trong cửa sổ).
- Không load preset `Inputs.preset`, `TagLayers.preset` vào project chung: ghi đè Input Manager và thêm layer `Walkable`,
  `TempCast`.
- SuperCharacterController là hệ va chạm riêng: cần layer `Walkable`, `BSPTree` trên mọi Mesh Collider. Không dùng cho game
  của team.
- Pack chưa vào git (61 MB). Muốn đưa lên repo chung thì đọc điều khoản Asset Store trước; không thì ai cần tự tải bản FREE
  về.
