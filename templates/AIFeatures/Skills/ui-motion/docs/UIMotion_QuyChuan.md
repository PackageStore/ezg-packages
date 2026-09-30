# Quy chuẩn UI Motion

Phiên bản 0.4 · 2026-09-28 · Khớp module `UIMotionDefaults.Version` = 0.4.1.
Áp dụng cho UI uGUI trong Unity (prefab, scene), ghép tay hay sinh bằng tool; mọi cơ chế chạy phía Unity. Dùng được cho mọi
project: quy chuẩn và module chỉ chứa luật chung, điều riêng của từng project nằm trong file riêng của project đó (mục 2.4).

Người đọc: người ghép UI, tech art giữ 3 asset kiểm soát, dev. Mục nào có chữ **[DEV]** là việc của dev, còn lại thao tác
trong Unity. Bắt đầu nhanh: `README.md`. Claude làm việc này qua skill `ui-motion` (folder `Skill~/ui-motion` của module).

**Đổi so với 0.3** (dùng chung cho mọi project): bảng Component mặc định chỉ còn component của Unity, TMP, DOTween và module
— class tự có motion của từng project khai trong file luật riêng `ProjectSettings/UIMotionProject.json` rồi áp vào RoleMap
(mục 2.4). Asset RoleMap / Settings / Profile là của project, nằm ngoài folder module (mục 2), có lệnh dời asset cũ ra. Từ
nhận prefab item / khuôn chuyển vào RoleMap. Folder quét mặc định `Assets`. Stub Odin nằm trong module. Skill `ui-motion`
khảo sát project và sinh file riêng của project.

Lịch sử các bản trước: `CHANGELOG.md`.

---

## 0. Năm nguyên tắc gốc

1. **Motion đi theo ROLE, không đi theo screen.** Một object là Button thì có motion của Button, ở screen nào cũng vậy.
2. **Có UI là có motion.** Tool gắn component, không phải người. Ba điểm gắn: Quét và gắn, lúc save prefab, lúc chạy game.
3. **Một chỗ kiểm soát.** Ba asset Profile, RoleMap, Settings. Không mở từng screen để chỉnh cảm giác.
4. **Không phá cái đang chạy.** Component tự có motion (screen controller, nút, list animator của project khai trong file luật
   riêng; Animator, DOTweenAnimation…) không bị gắn chồng. Tool chỉ thêm cái còn thiếu, không ghi đè chỉnh tay.
5. **Thiếu thì không qua gate.** Vi phạm phải sửa hoặc ghi nợ kèm lý do.

---

## 1. Thuật ngữ

| Từ | Nghĩa |
|---|---|
| Template | Một hàm motion dùng chung trong `UIMotion` (PopIn, PopSoftIn, SlideIn, RiseIn, Punch, CountUp…). |
| Component element | Component gắn lên object theo role (Panel, Backdrop, Button, Counter, List, Toggle, Tabs, Card, Slider… — bảng mục 3.1). Đọc RoleMap + Profile, có dropdown ghi đè. |
| Role | Vai trò của object: Screen, Popup, Button, List… (enum `UIRole`). Quyết định motion nào chạy. |
| Resolver | Bộ suy role: gắn tay → component → keyword tên → cấu trúc. Dùng chung cho tool, hook save, AutoBind, gate. |
| RoleMap | Asset: role nào chạy motion gì, sfx gì, có loop không; component nào báo role gì; keyword nào ra role gì; tên prefab nào là item / khuôn. |
| Profile | Asset 5 trục cảm giác sinh ra mọi thời lượng, ease, biên độ. Profile mặc định: **Team**. |
| Settings | Asset bật tắt cơ chế, phạm vi tự gắn, folder quét, gate, reduce motion, âm thanh. |
| AutoBind | Lưới an toàn lúc chạy: tự gắn motion cho object chưa có (không lưu prefab). |
| Gate | Rule M-1 … M-12 kiểm thiếu motion. Chạy ở cửa sổ Kiểm tra, lúc build, CLI. |
| Prefab hỏng | Prefab có script mất, script không còn class, prefab lồng / gốc variant mất. Unity không cho lưu nên tool không gắn được. |
| Nợ | Vi phạm được chấp nhận có lý do, gate bỏ qua. |
| File luật riêng | `ProjectSettings/UIMotionProject.json`: điều chỉ đúng cho một project (class tự có motion, keyword, folder quét, sink sfx). Mục 2.4. |
| Ghi chú của project | `ProjectSettings/UIMotionProject.md`: audio manager, chỗ đóng screen, bẫy môi trường, quyết định của project. |
| Feedback hub | `UIMotionFeedback`: kênh sự kiện motion (show, press, click…) để sfx / haptic bám vào. |

---

## 2. Ba asset kiểm soát

Tạo bằng **Tools/UI Motion/Tạo asset mẫu (Profile, RoleMap, Settings)**. Mỗi project một bộ, nằm trong một folder
`…/Resources/UIMotion/` của project, **ngoài folder module** (chỗ asset đang có; chưa có thì `dataFolder` của file luật riêng;
không ghi thì `Assets/Resources/UIMotion`). Folder module chỉ có code, tài liệu, skill: chép bản module mới đè nguyên folder
không mất tuning. Project cài bản 0.3 trở về trước (asset trong module): **Tools/UI Motion/Dời asset của project ra khỏi folder
module** (giữ GUID). Thiếu asset thì module dùng bản mặc định trong code.

### 2.1 Profile — cảm giác cả game

| Trục | 0 | 1 | Kéo theo |
|---|---|---|---|
| Tempo | chậm | nhanh | mọi thời lượng, stagger, chu kỳ pulse và float, tốc độ gõ chữ, nhịp thanh HP |
| Bounce | mềm | nảy | họ ease (Sine, Cubic, Back / Expo / Bounce), overshoot, scale bắt đầu của pop |
| Impact | nhẹ | mạnh | punch, shake, press, nudge, khoảng trượt / rơi / nổi, emphasize, bump thanh HP, haptic, sfx |
| Liveliness | tĩnh | sống | biên độ pulse, float, sóng chữ lúc nghỉ |
| Softness | sắc | mềm | fade nhiều hay ít, đóng nhanh hơn mở bao nhiêu, dim khi emphasize, ease morph |

Preset là điểm xuất phát. **Áp preset thay cả 5 trục lẫn danh sách ghi đè.**

| Preset | Tempo | Bounce | Impact | Liveliness | Softness | Ghi đè |
|---|---|---|---|---|---|---|
| Action | 0.90 | 0.85 | 0.90 | 0.50 | 0.10 | không |
| Midcore | 0.55 | 0.60 | 0.55 | 0.50 | 0.40 | không |
| Puzzle | 0.60 | 0.50 | 0.35 | 0.40 | 0.50 | không |
| Cozy | 0.20 | 0.10 | 0.10 | 0.80 | 0.90 | không |
| **Team** | 0.60 | 0.78 | 0.55 | 0.50 | 0.40 | 6 số + 2 ease, bảng dưới |

**Profile Team** là cảm giác chuẩn, mặc định của module (calibrate trên UI mobile thật đang chạy):

| Cảm giác | Team cho ra |
|---|---|
| Screen / popup mở ("jump in" mềm) | PopSoft: scale 0.80 → 1 + fade, OutExpo, 0.29 s |
| Screen / popup đóng | ngược lại, InQuad, 0.2 s (HideSpeedRatio 0.667, ghi đè) |
| Nền dim | Backdrop fade 0.29 s tới alpha đặt trên Image |
| Pop in | scale 0 → 1 (PopFromScale 0, ghi đè), OutBack overshoot 1.70 |
| Slide | 240 px, 0.29 s (ghi đè), Move = OutCubic (ghi đè) |
| Stagger list | 0.06 s |
| Rơi nảy | 318 px, OutBounce, 0.56 s |
| Nổi lên + fade + pop | 74 px, scale 0.90, 0.29 s |
| Nhấn nút | PressScale 0.9, nhấn 0.1 s, nhả 0.2 s (ghi đè) |

Nhấn 0.1 s là quyết định có chủ ý: chạm nhanh trên mobile vẫn thấy nút lún đủ. Khoảng cách px tính theo canvas tham chiếu
**1080 × 1920**. Test `UIMotionProfileTeamTests` giữ các số này.

**Project cũ có cảm giác riêng** (screen cũ tự jump in bằng số khác): chỉnh Profile của project (5 trục + ghi đè) cho screen
mới giống screen cũ; không sửa preset trong code. Skill `ui-motion` liệt kê số tween đang dùng trong code của project để làm
việc này; số đã chọn ghi vào ghi chú của project.

### 2.2 RoleMap — role nào chạy gì

Inspector 3 tab.

**Tab Role** — mỗi role một dòng:

| Cột | Ghi chú |
|---|---|
| Hiện | kiểu hiện (bảng mục 4) |
| Ẩn | Ngược của Hiện = đi ngược đường lúc hiện, nhanh hơn theo HideSpeedRatio |
| Feedback | Press, Punch, Shake, Nudge, Emphasize, Count, Không |
| Cho loop | chỉ role được phép loop |
| Thời lượng / Ease | 0 / Unset = theo Profile |
| Sfx theo sự kiện | mỗi sự kiện một dòng: kéo AudioClip (player kèm sẵn), Key (audio manager riêng), âm lượng |
| Ghi chú | |

**Tab Component** — component nào (hoặc lớp con) báo role gì; tick **Đã có motion** nghĩa là component đó tự lo motion, tool
không gắn chồng. Thứ tự = ưu tiên. Tên class không có trong project thì dòng đó vô hại. Bảng mặc định chỉ có component của
module, DOTween, Unity, TMP (trên xuống); class riêng của project (screen controller, nút, list animator, chấm đỏ…) vào từ
file luật riêng và đứng trên cùng (mục 2.4):

| Component | Role | Đã có motion |
|---|---|---|
| UIMotionBar | Bar | có |
| UIDamageTextSpawner | DamageText | có |
| UIMotionPlayer | (giữ role) | có |
| DG.Tweening.DOTweenAnimation | (giữ role) | có |
| UnityEngine.Animator | (giữ role) | có |
| UnityEngine.Animation | (giữ role) | có |
| Dropdown, TMP_Dropdown | Dropdown | không |
| InputField, TMP_InputField | InputField | không |
| Toggle | Toggle | không |
| ToggleGroup | Tab (thanh tab) | không |
| Slider | Slider | không |
| Scrollbar | Scrollbar | không |
| Button (gồm lớp con) | Button | không |
| ScrollRect | List (item trong Content) | không |
| Text, TMP_Text | Label (keyword đổi được) | không |

**Tab Keyword** — mục 3.3. Có ô **Gõ thử tên object** hiện cách tách từ và role ra được. Cuối tab: **Từ nhận prefab item**
(mặc định item, cell, slot, row, entry, element, card) và **Từ nhận prefab khuôn** (template, layout, tpl) — luật 3.4.

**Cập nhật theo bản module mới.** RoleMap nhớ bản module đã gộp bảng mặc định. Chép module mới vào project mà asset còn
cũ thì Inspector hiện nút **Cập nhật theo bản module mới** (cũng có ở menu Tools/UI Motion, Console nhắc một lần mỗi phiên).
Nút này thêm role, sự kiện sfx, luật component, keyword còn thiếu theo đúng thứ tự ưu tiên. Keyword bản mới đổi role hoặc
bỏ thì chỉ sửa khi dòng đó vẫn y như mặc định cũ. Không bỏ dòng Component nào: dòng bản cũ có mà bản mới không còn trong mặc
định (0.4 bỏ class của project khỏi mặc định) vẫn giữ, coi như luật riêng. Clip, kiểu hiện / ẩn, feedback, keyword riêng của
project giữ nguyên, có Undo. Console liệt kê từng thay đổi.

### 2.3 Settings — bật tắt cơ chế

| Mục | Mặc định | Ghi chú |
|---|---|---|
| Gắn khi save prefab | bật | chỉ khi save trong Prefab Mode |
| AutoBind lúc chạy | bật | lưới an toàn, không lưu prefab |
| Folder quét | `Assets` | nên thu về folder UI của project (file luật riêng, `settings.scanFolders`) |
| Bỏ qua path chứa | /3rdParty/, /ThirdParty/, /Plugins/, /Samples/, /Sample/, /Demo/, /Demos/, /Examples/, /Models/ | UI pack bên thứ ba, sample, model 3D; folder pack riêng của project thêm trong file luật riêng |
| Phạm vi: Button | bật | họ nút, chỉ object tự bấm được: Button, Toggle, Card, thanh tab, Dropdown |
| Phạm vi: Counter | bật | label số ngoài list |
| Phạm vi: Screen / Popup | bật | gốc screen / popup / sheet / toast / tooltip chưa có jump in/out, + nền dim |
| Phạm vi: List | bật | list chưa có motion |
| Phạm vi: Panel con | **tắt** | bật là đổi cảm giác mở của screen cũ |
| Phạm vi: Slider, Badge, Timer, Icon, Ô nhập, Loading | bật | ngoài list / prefab item |
| Phạm vi: Scrollbar, Tiêu đề, Trang trí | **tắt** | bật là screen cũ đổi cảm giác (thanh cuộn mờ đi, chữ chạy, trang trí chuyển động) |
| Gate | Cảnh báo | Tắt / Cảnh báo / Chặn build |
| Reduce motion | tắt | game đặt lúc chạy qua `UIMotionSettings.ReduceMotionOverride` **[DEV]** |
| Hệ số tốc độ | 1 | thử nhanh / chậm cả game |
| Cảnh báo SetActive thẳng | bật | Editor và bản development |
| Phát sfx, mixer group, âm lượng | bật, trống, 1 | player kèm sẵn |
| Sink sfx | player kèm sẵn | dropdown liệt kê class implement `IUIMotionSfxSink` **[DEV]** |

Phạm vi tự gắn đồng thời là chính sách của gate: thứ gì không nằm trong phạm vi thì không bị coi là thiếu.

### 2.4 Luật riêng của project — `ProjectSettings/UIMotionProject.json`

Module chỉ giữ luật chung. Điều chỉ đúng cho một project nằm trong file JSON của project đó (vào VCS, không vào build), áp
vào RoleMap / Settings bằng **Tools/UI Motion/Áp luật riêng của project** (nút cùng tên trên Inspector RoleMap; batch
`UIMotionBatch.ApplyProjectRulesBatch` **[DEV]**). Chưa có file: **Tools/UI Motion/Tạo file luật riêng mẫu**, hoặc để skill
`ui-motion` khảo sát project rồi sinh (kèm `ProjectSettings/UIMotionProject.md` ghi chú của project).

| Khoá | Nghĩa |
|---|---|
| `componentRules` | `type` (tên class, khớp cả lớp con), `role` (tên trong `UIRole`; `Unknown` = giữ role), `ownMotion` (class tự lo motion → không gắn chồng), `note`. Đứng trên bảng mặc định, theo thứ tự trong file |
| `componentRulesRemove` | bỏ dòng Component theo tên |
| `keywords.add` / `.remove` | `word`, `role`, `before` (tên role hoặc keyword; không có = cuối bảng); bỏ keyword |
| `prefabWords.item` / `.template` | thay danh sách từ nhận prefab item / khuôn (luật 3.4) |
| `settings.scanFolders` / `.excludePathContains` / `.sfxSinkType` | thay folder quét, chuỗi loại trừ; chọn sink sfx |
| `dataFolder` | folder asset của project khi chưa có asset nào |

Khoá thiếu = giữ nguyên asset. Áp lại không đổi gì thêm. RoleMap nhớ dấu file đã áp: file đổi mà chưa áp thì Inspector và
Console nhắc. **Khôi phục bảng mặc định** xoá luật của project: áp lại file. Chỉ ghi `ownMotion` cho class đã kiểm (tween
chạy trên chính object đó lúc hiện / ẩn / bấm): ghi sai là object đó mất motion của module mà gate vẫn pass.

---

## 3. Role

### 3.1 Danh sách role (`UIRole`, số cố định, chỉ thêm ở cuối)

| Số | Role | Là gì | Component motion (tool gắn) |
|---|---|---|---|
| 0 | Unknown | chưa rõ | — |
| 1 | None | cố ý không motion, phải có lý do | — |
| 2 | Screen | màn hình toàn phần | UIMotionPanel |
| 3 | Popup | hộp thoại nổi | UIMotionPanel (+ UIMotionBackdrop) |
| 4 | Backdrop | nền dim sau popup | UIMotionBackdrop |
| 5 | Panel | khối con hiện ẩn riêng | UIMotionPanel (khi bật phạm vi) |
| 6 | Button | nút bấm | UIMotionButton |
| 7 | List | chứa nhiều item giống nhau | UIMotionLayoutTransition |
| 8 | ListItem | item trong list | (List lo) |
| 9 | Counter | số đổi giá trị | UIMotionCounter; coin bay về: UIMotionFlyTo (gắn tay) |
| 10 | Bar | thanh HP / XP / tiến độ | UIMotionBar (gắn tay) |
| 11 | Label | chữ tĩnh | — (đi cùng cha) |
| 12 | Toggle | công tắc | UIMotionToggle |
| 13 | Slider | thanh kéo | UIMotionSlider |
| 14 | Tab | thanh tab | UIMotionTabs (trên thanh), từng tab: UIMotionToggle / UIMotionButton |
| 15 | Dropdown | menu thả | UIMotionDropdown |
| 16 | Scrollbar | thanh cuộn | UIMotionScrollbar (khi bật phạm vi) |
| 17 | Sheet | bottom sheet / drawer | UIMotionSheet |
| 18 | Toast | thông báo tự tắt | UIMotionToast |
| 19 | Tooltip | chú thích nổi | UIMotionTooltip |
| 20 | Card | thẻ chọn được | UIMotionCard |
| 21 | Title | tiêu đề | UIMotionTitle (khi bật phạm vi) |
| 22 | Icon | ảnh đổi / lật | UIMotionIcon |
| 23 | Badge | chấm đỏ | UIMotionBadge |
| 24 | Timer | đếm ngược | UIMotionTimer |
| 25 | Spinner | loading | UIMotionSpinner |
| 26 | DamageText | số bay | UIDamageTextSpawner (gắn tay) |
| 27 | Decoration | trang trí được loop | UIMotionDecoration (khi bật phạm vi) |
| 28 | ScreenFx | shake / flash toàn màn | UIMotionScreenFx (gắn tay, gọi từ code / OnClick) |
| 29 | InputField | ô nhập | UIMotionInputField |

Chuỗi nhiều element (mở rương, lên cấp, mua thành công, báo lỗi) không phải role: `UIMotionRecipes`, gọi từ code **[DEV]**.
Scene **Role Sandbox**, trang Bảng role, có một ô demo cho từng dòng bảng trên.

### 3.2 Thứ tự suy role

1. `UIMotionRole` gắn tay (khác Chưa rõ). Thắng tất cả. Tick **Bỏ qua cả con cháu** thì tool không đụng nhánh đó.
2. Bảng Component của RoleMap, theo thứ tự bảng.
3. Keyword trong tên. Chữ (Text / TMP) chỉ được đổi từ Label sang Counter / Title / Timer / Badge / DamageText. Nút (Button)
   chỉ được đổi sang Card / Tab.
4. Cấu trúc:
   - gốc prefab UI, hoặc con trực tiếp của canvas gốc, kéo giãn kín, bên trong có nút hoặc chữ → **Screen**; nếu con đầu là
     nền dim và có khung → **Popup**;
   - con đầu kéo giãn kín, Image tối hơi trong suốt, còn anh em phía sau → **Backdrop**;
   - LayoutGroup có item có nội dung, ít nhất 2 item cùng tên (bỏ số đuôi) hoặc 3 item cùng bộ component → **List**.
5. Bấm được (EventTrigger, handler tự viết) mà vẫn chưa ra role → **chưa rõ**: gate M-10, người ghép chọn role. Đây là việc tay duy nhất.

Keyword so nguyên từ, không phân biệt hoa thường; tên tách theo khoảng trắng, `_`, `-`, `.`, CamelCase và ranh giới chữ / số:
`BtnBuy`, `btn_buy`, `Btn-Buy`, `BTN_BUY` đều ra `btn`, `buy`. Hai từ liền nhau ghép lại cũng khớp (`TopBar` khớp `topbar`).

### 3.3 Keyword mặc định

Seed từ tên object thật của các project UI cũ (`button_close_sheet`, `BtnNext`, `text_value`, `scroll_view`,
`TxtValueTimeToUnlock`…). Thứ tự = ưu tiên; sửa trong RoleMap (thói quen đặt tên riêng của một project: file luật riêng,
mục 2.4), không sửa code.

| Thứ tự | Role | Keyword |
|---|---|---|
| 1 | Button | btn, button, nut |
| 2 | Panel | topbar, bottombar, sidebar, header, footer, panel |
| 3 | Popup | popup, dialog, modal, hopthoai |
| 4 | Backdrop | backdrop, dim, overlay, blocker |
| 5 | Decoration | fullscreen |
| 6 | Screen | screen |
| 7 | Sheet | sheet, drawer |
| 8 | Toast | toast, snackbar |
| 9 | Tooltip | tooltip, tip, hint |
| 10 | Tab | tab |
| 11 | List | list, scroll, grid |
| 12 | ListItem | item, cell, slot |
| 13 | Card | card |
| 14 | Badge | badge, reddot, noti, notification, notify |
| 15 | Timer | timer, countdown, clock, time |
| 16 | Counter | coin, gem, gold, diamond, cash, money, score, count, amount, value, num, profit |
| 17 | Bar | progress, hp, mp, xp, exp, energy, stamina, health, bar |
| 18 | Title | title, heading, head |
| 19 | Spinner | loading, loader, spinner |
| 20 | Icon | icon, ico, avatar |
| 21 | Decoration | deco, glow, sparkle, shine, fx, vfx |

Cố ý **không** có: `view`, `page`, `row` (project đặt cho item, cho chữ, cho khối tuỳ thói quen), `bg`, `content`, `fade`
(quá chung), `price` (giá đặt một lần lúc mở screen, không phải số đổi giá trị), `dot` (`star_1/dot`, `spacing_dot` hay là
chấm trang trí). Lý do thứ tự:

- Nút đứng đầu vì `button_close_sheet` là nút chứ không phải sheet.
- `fullscreen` đứng trước `screen` vì `_bg_Pattern_FullScreen_Dark` và `glow_fullScreen` là nền / trang trí.
- Badge đứng trước Timer / Counter vì `notification_count` là chấm đỏ có số.
- Timer đứng trước Counter vì `TxtValueTimeToUnlock` là đồng hồ.
- Spinner đứng trước Icon vì `loading_icon` là ảnh xoay loading.

### 3.4 Luật gắn (rút từ pilot trên project cũ)

Role đúng chưa đủ: binder còn xét chỗ đứng của object. Các luật dưới đây có test giữ.

| Luật | Vì sao |
|---|---|
| Họ nút (Button, Toggle, Card, Tab, Dropdown) chỉ gắn lên object **tự bấm được** (có Selectable hoặc handler pointer) | Gắn lên phần hình con (`btn_rim`, `btn_shadow`) giành mất pointer: OnClick của nút cha không chạy |
| Prefab Variant được quét; gốc variant nhận component dạng override, phần thừa kế gắn ở prefab gốc | Không tạo override chồng chéo |
| Prefab **dùng chung** (lồng trong prefab khác, làm gốc variant, tên có từ nhận prefab khuôn: template / layout / tpl): không gắn hiện / ẩn cấp screen | Screen dùng nó tự lo hiện / ẩn; gắn thêm là chạy hai lần |
| Prefab **item** (tên có từ nhận prefab item của RoleMap: item, cell, slot, row, entry, element, card): coi như trong list, không gắn Counter / Icon / Badge / Slider | List pool (scroll tái dùng dòng) dùng item cho dòng khác |
| Screen / Popup / Sheet / Toast / Tooltip không phải gốc: chỉ gắn khi là lớp phủ thật (tắt sẵn trong prefab hoặc có nền dim riêng) | Khối con luôn hiện không phải popup |
| Backdrop chỉ gắn khi cha là screen / lớp phủ | Nền tối trang trí giữa screen không phải nền dim |
| List chỉ gắn khi có ScrollRect / LayoutGroup và không nằm trong item | Tránh list lồng trong item |
| Counter chờ 0,25 s sau khi bật; không gắn trong badge | Screen nạp số lúc mở không bị đếm từ 0; số trên chấm đỏ do Badge lo |
| Icon chỉ gắn khi sprite trống trong prefab hoặc tên có avatar | Icon tĩnh vẽ sẵn không bao giờ đổi ảnh |
| Spinner chỉ gắn lên ảnh lá (không có con nào có hình) | Xoay cả khối loading là xoay luôn chữ |
| Keyword tab chỉ ra thanh tab khi bên trong có Toggle / con tên tab | `AchievementTab` lẻ chứa nút là một nút |
| Prefab hỏng: báo "Prefab hỏng", bỏ qua; gate đánh dấu không sửa bằng nút | Unity không cho lưu prefab có script / prefab lồng mất |

---

## 4. Motion mặc định theo role

Bảng Role mặc định của RoleMap. Số liệu lấy từ Profile. "—" = đi cùng cha, không có motion hiện / ẩn riêng.

| Role | Hiện | Ẩn | Feedback | Sfx | Loop |
|---|---|---|---|---|---|
| Screen | Pop mềm | ngược | — | show, hide | không |
| Popup | Pop mềm | ngược | — | show, hide | không |
| Backdrop | Fade | ngược | — | — | không |
| Panel | Trượt từ cạnh gần nhất | ngược | — | show, hide | không |
| Button | — | — | Press | press, click | không |
| List | Nổi lên + fade + pop, lần lượt | ngược, nhanh hơn | — | show | không |
| ListItem | Pop (item thêm lúc chạy) | ngược | Emphasize | change | **cấm** |
| Counter | — | — | Count | increase, decrease, arrive | không |
| Bar | — | — | (UIMotionBar) | increase, decrease, low | thở khi thấp |
| Label | — | — | — | — | không |
| Toggle | — | — | Press + check pop, knob trượt, nền đổi màu | change | không |
| Tab | — | — | Press; indicator trượt, tab chọn nảy, nội dung trượt vào | change | không |
| Dropdown | — | — | Press; danh sách giãn dọc + item lần lượt | show, hide | không |
| Card | — | — | Press; nổi lên khi chọn, Flip lật | click, change | không |
| Slider | — | — | handle nhún khi kéo, thanh đuổi theo giá trị code, tick | change | không |
| Scrollbar | — | — | hiện khi cuộn, mờ khi nghỉ | — | không |
| Sheet | Trượt từ dưới | ngược | kéo xuống để đóng (tuỳ chọn) | show, hide | không |
| Toast | Trượt từ trên | ngược | tự ẩn, chờ lượt, chạm để tắt (tuỳ chọn) | show | không |
| Tooltip | Pop (mọc từ phía nút neo) | ngược | — | show | không |
| Title | — | — | chữ hiện lần lượt; sóng / nhảy nếu chọn | — | được |
| Icon | — | — | đổi sprite: Pop / Flip / Fade; Spin / Pulse / Float nếu chọn | change | được |
| Badge | Pop | ngược | Punch khi số tăng; ẩn khi về 0 (tuỳ chọn) | show, increase | không |
| Timer | — | — | ≤ 10 s: nhịp + đổi màu mỗi giây | low, complete | được |
| Spinner | Fade | ngược | xoay (reduce motion: thở alpha) | — | được |
| DamageText | — | — | (UIDamageTextSpawner) | hit, crit | không |
| Decoration | — | — | Float / Pulse / Spin / Glow / Sway | — | được |
| ScreenFx | — | — | Shake / Flash / Hit / Crit theo lệnh | shake, flash, hit, crit | không |
| InputField | — | — | chọn ô nổi nhẹ + gạch chân; ShowError rung + đỏ | focus, error | không |

"Ngược" của từng kiểu: Fade → fade ra; Pop / Pop mềm / Zoom → thu về + fade; Trượt từ X → trượt ra X; Rơi → bay lên;
Nổi lên → chìm xuống.

Reduce motion: hiện / ẩn chỉ còn fade ngắn; knob, indicator, thanh, icon nhảy ngay tới trạng thái mới; loop đứng yên (spinner
thở alpha); thanh cuộn luôn hiện rõ; ScreenFx không loé (chống chói); coin không bay (xong ngay). Sự kiện sfx vẫn bắn.

---

## 5. Sfx và feedback

- Mọi component bắn sự kiện qua `UIMotionFeedback`: role, sự kiện, cường độ. Không gọi sfx UI bằng tay ở screen.
- Sự kiện: show, hide, press, release, click, change, increase, decrease, low, error, complete, hit, crit, shake, flash,
  focus (chọn ô nhập), arrive (mỗi coin bay tới đích).
- Key cho audio manager: `ui.<role>.<event>`, chữ thường (`ui.popup.show`, `ui.button.click`).
- **Player kèm sẵn**: kéo AudioClip vào RoleMap, chọn mixer group trong Settings. Không cần code. Cùng clip bắn dồn trong
  0.04 s chỉ kêu một lần.
- **Project có audio manager** **[DEV]**: class implement `IUIMotionSfxSink` (constructor rỗng), chọn trong Settings; hoặc gán
  `UIMotionFeedback.Sink` lúc boot. Haptic / analytics: nghe `UIMotionFeedback.Emitted`.
- Sfx riêng cho một object: danh sách Sfx riêng trên component đó, thắng RoleMap.
- **Cẩn thận tiếng kép**: project mà code đã tự phát tiếng nút (vd component nút của project tự gọi audio manager) thì đừng
  gán clip trùng vai trò trong RoleMap, hoặc tắt tiếng cũ. Mẫu sink: `Docs/Samples/UIMotionSfxSink.cs.txt` (chép vào code
  của project, không đặt trong module).

---

## 6. Quy tắc kỹ thuật **[DEV]**

1. **Đối xứng.** Có hiện thì có ẩn. Ẩn = hiện × HideSpeedRatio.
2. **Ngắt được giữa chừng.** Hiện khi đang ẩn thì tiếp từ trạng thái hiện tại; chỉ khi đứng yên mới đặt trạng thái đầu.
3. **Feedback hoàn tất, không kill.** Punch, Shake, Nudge, Press mang id riêng.
4. **`SetUpdate(true)`** trên mọi tween.
5. **Tắt giữa motion thì trả trạng thái nghỉ.** Ẩn bằng CanvasGroup; không SetActive(false) trong lúc tween.
6. **Ẩn rồi mới tắt.** Đóng screen qua `UIMotion.SetActiveAnimated(go, false)` hoặc `UIMotionPanel.Hide()`. SetActive(false)
   thẳng lên object có UIMotionPanel là vi phạm M-11, Console cảnh báo một lần kèm stack trace.
7. **Nối template bằng `OnComplete`**, không `Sequence.Append`. Dọn dẹp nội bộ đi qua `AppendCallback`.
8. **Loop tự dừng** khi object tắt; cấm loop trên ListItem (M-8).
9. **Không hardcode số cảm giác.** Tham số mới thêm cuối `UIMotionParam` + một dòng `Derive`.
10. **Enum lưu trong asset / prefab chỉ thêm ở cuối, không đổi số**: `UIRole`, `UIMotionShow`, `UIMotionHide`, `UIFeedbackKind`,
    `UIMotionEvent`, `UIMotionParam`, `UIMotionEase`, `UIMotionGenre`, `UIMotionGateMode`, `UIMotionPlayer.MotionType`,
    `UITextMotion.Mode`. Class của project nối vào module (`IUIMotionHideable`, hub feedback) giữ cùng luật cho enum của nó.
11. **Tool gắn idempotent.** Chỉ thêm khi thiếu; chạy lại không gắn trùng; chỉ sửa object thuộc chính prefab (không tạo override
    cho prefab lồng / phần của prefab gốc trong variant); component tool gắn mang dấu `AutoBound` để gỡ được.
12. **Không Instantiate / Destroy trong motion** (trừ ghost morph cũ); dùng pool.
13. **Odin không bắt buộc.** Component mới dùng Header / Tooltip của Unity; Inspector riêng của module vẽ giống nhau khi có và
    không có Odin.
14. **Tương thích ngược.** Không đổi field, enum, tên class đã có trong prefab.
15. **Test Play mode**: `yield return new EnterPlayMode()` đặt ngay trong hàm `[UnityTest]` (yield từ hàm con thì runner bỏ qua,
    Play mode không bật, test treo tới hết giờ). Phần sau `EnterPlayMode` chạy trong coroutine riêng (vào Play mode là reload
    domain; biến bị lambda bắt tạo closure từ đầu hàm sẽ mất → NullReference).
16. **Tween đi theo object.** Cửa `UIMotion.Show` / `Hide` và feedback (punch, shake, nudge, press, flash) gắn tween vào GameObject
    (`SetLink`): game Destroy object giữa chừng thì tween tự dừng, DOTween không báo lỗi. Element hoàn tất feedback / flash của
    mình trong OnDisable. Template mới chạy trên một object thì làm y vậy.
17. **Chỉ đổi phần hiển thị, không đổi logic game.** Slider không đổi `slider.value`, Tabs không tự chuyển tab, Dropdown không
    tự chọn, Toast không Destroy, Counter / Badge / Timer chỉ đọc text code đã gán. Listener gắn lúc chạy (`AddListener`), không
    lưu vào prefab.
18. **Không đặt tên hàm riêng trùng message của Unity** (`Start`, `Update`, `OnEnable`… có tham số): Unity báo lỗi hoặc gọi nhầm.
    Test `Components_DoNotMisuseUnityMessageNames` giữ.
19. **Test Play mode chờ frame thật.** Game giới hạn FPS (`targetFrameRate`) thì một lần `yield return null` có thể chưa qua
    frame nào: chờ `Time.frameCount` đổi (helper `Frame()`), chờ thời gian bằng đồng hồ thật.

---

## 7. Quy trình

### 7.1 Screen mới

1. Ghép UI như bình thường.
2. Save prefab trong Prefab Mode → component motion tự gắn theo role (Ctrl+Z được).
3. Nút đóng: Button OnClick → `UIMotionPanel.Hide`. Code đóng: `UIMotion.SetActiveAnimated` **[DEV]**.
4. Muốn khác mặc định: đổi dropdown trên component. **▶ Xem hiện / ▶ Xem ẩn** xem ngay trong Scene View.

### 7.2 Project cũ

1. **Khảo sát** (skill `ui-motion`: `uimotion.mjs survey` rồi `init`): class tự có motion, class bấm được, audio manager,
   chỗ đóng screen, thói quen đặt tên → `ProjectSettings/UIMotionProject.json` (luật đã chắc) + `.md` (ghi chú, mục cần xác
   nhận). Người giữ project xác nhận các mục chưa chắc. Không dùng skill: tự lập file luật (mục 2.4).
2. Chép **một folder module** vào project (đã có stub Odin bên trong, tự tắt khi có Odin). Đã có module bản cũ: dời asset ra
   khỏi folder module trước (mục 2), rồi thay nguyên folder; folder stub Odin riêng mà bản cũ dặn chép kèm thì xoá.
3. **Tools/UI Motion/Tạo asset mẫu** → **Áp luật riêng của project**. Đã có module bản cũ: bấm **Cập nhật theo bản module
   mới** trên RoleMap.
4. **Tools/UI Motion/Quét và gắn motion…** → Quét (không đổi gì) → đọc báo cáo, bỏ tick → Gắn. Sai nhiều ở một loại object
   thì sửa luật riêng, áp lại, quét lại. Dev có thể xuất báo cáo bằng batch `ScanReportBatch` trước **[DEV]**. Dòng "Prefab
   hỏng": sửa prefab (Remove Missing Scripts, nối lại prefab lồng) rồi quét lại.
5. Dev đổi chỗ đóng screen sang `SetActiveAnimated` **[DEV]**. Không có manager thì chạy game, đọc cảnh báo trong Console.
6. Sfx: kéo clip vào RoleMap, hoặc sink riêng trong code của project **[DEV]** (mục 5).
7. **Tools/UI Motion/Kiểm tra (gate)…**: sửa bằng Gắn, hoặc Bỏ qua kèm lý do. Sạch thì Settings > Gate = Chặn build.

### 7.3 Ba việc tay, không cần chuyên môn

| Tình huống | Làm gì |
|---|---|
| Gate báo chưa rõ role (M-10) | Gắn `UI Motion Role (gắn tay)`, chọn role |
| Object này muốn khác mặc định | Đổi dropdown trên component đã gắn |
| Cố ý không motion | `UI Motion Role` = Không motion, ghi lý do |

### 7.4 Người bảo trì

Sửa Profile, RoleMap và file luật riêng của project. Xem cửa sổ Kiểm tra. Role mới (sửa ở nguồn module, không sửa folder
module trong project game): thêm cuối `UIRole`, một dòng RoleMap, một luật binder, một rule gate, một ô trong Bảng role (mục
11). Không mở từng screen. Thử cảm giác: scene Role Sandbox (project phát triển module), trang Bảng role.

---

## 8. Gate

### 8.1 Rule

| # | Rule | Điều kiện pass | Sửa bằng một nút |
|---|---|---|---|
| M-1 | Screen / sheet / toast / tooltip có hiện / ẩn | có UIMotionPanel / UIMotionSheet / UIMotionToast / UIMotionTooltip, hoặc component tự có motion | có |
| M-2 | Popup có Panel và nền dim có Backdrop | như trên | có |
| M-3 | Button có press | có UIMotionButton, hoặc tự có motion (component nút của project khai "Đã có motion", Animator…) | có |
| M-4 | List có hiện lần lượt | có UIMotion List, hoặc list animator của project khai "Đã có motion" | có |
| M-5 | Toggle / Tab / Card / Dropdown có motion | có UIMotionToggle / UIMotionTabs (trên thanh tab) / UIMotionCard / UIMotionDropdown; tab lẻ là nút: UIMotionButton | có |
| M-6 | Label số (ngoài list) có Counter | có UIMotionCounter | có |
| M-7 | Sfx của role đã gán clip | mọi sự kiện sfx trong RoleMap có clip (khi dùng player kèm sẵn; có sink riêng thì bỏ qua) | không |
| M-8 | Không loop trong item của list | không UIMotionPlayer pulse / float, UITextMotion / Title lặp, Spinner, Decoration, Icon loop dưới list | không |
| M-9 | Role None có lý do | UIMotionRole = None có ghi lý do | không |
| M-10 | Không object bấm được mà chưa rõ role | resolver ra được role | không |
| M-11 | Ẩn đã route | lúc chạy: UIMotionPanel không bị SetActive(false) thẳng (Console) | không |
| M-12 | Slider / badge / timer / icon / ô nhập / loading / scrollbar / tiêu đề / trang trí có motion | có component tương ứng (mục 3.1), chỉ trong phạm vi đang bật | có |

Chỉ object nằm trong **phạm vi tự gắn** (mục 2.3) mới bị coi là thiếu. Vi phạm nằm trong **prefab hỏng** ghi thêm "prefab hỏng
(…): sửa prefab trước" và không sửa bằng nút Gắn được. Sửa prefab, hoặc ghi nợ kèm lý do.

### 8.2 Chạy ở đâu

| Nơi | Hành vi |
|---|---|
| **Tools/UI Motion/Kiểm tra (gate)…** | bảng vi phạm theo rule; Chọn → tới prefab; Gắn → sửa; Bỏ qua… → ghi nợ; tab Nợ đã ghi |
| Build | Tắt / Cảnh báo (build vẫn chạy) / Chặn build (còn vi phạm là fail) |
| CLI **[DEV]** | `-executeMethod Assets._Project.Core.Modules.UIMotion.Editor.UIMotionLint.RunCli`; mã 1 khi còn vi phạm và gate = Chặn build, hoặc có `-uimotionStrict` |

### 8.3 Nợ

`ProjectSettings/UIMotionDebt.json` (riêng từng project, vào VCS, không vào build): prefab / scene / RoleMap, đường dẫn object,
rule, lý do, người, ngày. Gate trừ nợ trước khi chấm. Nợ quá 30 ngày tô vàng trong cửa sổ Kiểm tra.

---

## 9. Hiệu năng

- Loop chỉ cho Decoration, Spinner, Title, Bar khi thấp, Timer khi thấp.
- Chữ chạy từng ký tự chỉ cho title, tên item, thông báo; không dùng trong list dài.
- List: tổng lệch không quá 0.6 s (chia đều khi list dài), stagger tối đa 40 item; trượt về chỗ chỉ khi list ≤ 50 item.
- AutoBind quét nửa giây một lần, chỉ canvas có object mới (`hierarchyCount` đổi), mỗi object xét một lần.
- Counter, Badge, Timer theo dõi chữ bằng so chuỗi mỗi frame; Icon so tham chiếu sprite; Slider, Scrollbar so giá trị (chỉ
  khi đang bật).
- Icon đổi sprite dồn dập (quá 3 lần trong 0,15 s — ảnh động bằng code) thì tự thôi motion đổi ảnh.
- Pool cho damage text, sfx voice, coin bay (tối đa 24 đồng một lượt).

---

## 10. Phân phối và phiên bản

- Module = **một folder** (đặt ở đâu trong `Assets` cũng được): code, `Docs/`, `OdinStubs/` (tự tắt khi có Odin), `Skill~/`
  (skill `ui-motion`, Unity bỏ qua). Không chứa asset hay luật của project nào: chép bản mới đè nguyên folder. Không sửa
  file trong folder module ở project game; cần khác thì sửa ở nguồn module và tăng bản.
- Của project, nằm ngoài folder module: asset RoleMap / Settings / Profile (mục 2), `ProjectSettings/UIMotionProject.json`
  (mục 2.4), `ProjectSettings/UIMotionProject.md`, `ProjectSettings/UIMotionDebt.json` (mục 8.3), sink sfx và code nối vào
  module (`IUIMotionHideable`…).
- Skill `ui-motion`: `node Skill~/ui-motion/scripts/uimotion.mjs install-skill` cài vào `~/.claude/skills/ui-motion` (mở
  project nào cũng có); `install-module` chép / nâng module vào project khác.
- Phiên bản: `UIMotionDefaults.Version` và `Docs/CHANGELOG.md`.
- Define tự bật khi mở Editor, ghi cho mọi nền tảng build; gỡ package thì define tự gỡ. Menu: **Tools/UI Motion/Đồng bộ
  define**.

  | Define | Bật khi có | Thiếu thì mất |
  |---|---|---|
  | `UIMOTION_TMP` | TextMeshPro | overload TMP (Counter, Title… vẫn chạy với Text) |
  | `UIMOTION_UNITASK` | UniTask | `ShowAsync` / `HideAsync` |
  | `UIMOTION_UIEFFECT` | UIEffect (assembly Coffee.UIEffect) | template `Dissolve`, sandbox cũ |
  | `UIMOTION_UIEXTENSIONS` | UI Extensions (assembly UnityUIExtensions) | template `MorphSquircle`, sandbox cũ |

- Yêu cầu: Unity 2021.3+, DOTween, uGUI. Tuỳ chọn: TextMeshPro, UniTask, Odin, UIEffect, UI Extensions, Test Framework.
  Đã chạy thật: Unity 6000.3; Unity 6000.2 + Odin.
- Chép bản mới vào project đã có module: RoleMap bấm Cập nhật theo bản module mới (mục 2.2), rồi Áp luật riêng của project;
  Settings giữ nguyên, phạm vi mới lấy mặc định.
- Không asmdef (DOTween Modules nằm ở firstpass). Test bọc `#if UNITY_INCLUDE_TESTS`.

---

## 11. Định nghĩa "xong" cho một role hoặc component mới

- [ ] Có dòng trong RoleMap mặc định (mục 4); đổi bảng mặc định thì `UpgradeDefaults` gộp được vào asset cũ (có test)
- [ ] Resolver nhận diện được (mục 3)
- [ ] Binder biết gắn gì, có phạm vi trong Settings, theo luật chỗ đứng (mục 3.4)
- [ ] Có rule gate nếu là role bắt buộc (mục 8)
- [ ] Có ô trong trang Bảng role của Role Sandbox (test `RoleGallery_HasEveryRole_AndEveryButtonRuns` đỏ nếu thiếu)
- [ ] Có test Play mode: kịch bản chính, ngắt giữa chừng, reduce motion
- [ ] Chỉ đổi phần hiển thị, OnDisable trả trạng thái nghỉ, tween `SetLink` theo object (mục 6)
- [ ] Có mục trong CHANGELOG
- [ ] Không thêm class / tên / số của một project cụ thể vào code hay bảng mặc định: thứ đó vào file luật riêng của project
