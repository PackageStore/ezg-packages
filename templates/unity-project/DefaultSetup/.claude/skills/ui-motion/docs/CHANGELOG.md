# CHANGELOG — UI Motion

Phiên bản ghi ở `UIMotionDefaults.Version`, hiện trên Inspector của UIMotionSettings.

## 0.4.1 — 2026-09-30

Skill `ui-motion` mang theo bản chép tài liệu của module trong `docs/` (quy chuẩn, README, CHANGELOG, `Samples/`). Trước
đây quy chuẩn chỉ có trong folder module: skill cài một mình (Feature Hub, `~/.claude/skills`) ở project chưa có module
không có gì để đọc và dừng lại. Luật, code module không đổi (chỉ số bản).

- Skill đọc quy chuẩn theo thứ tự: `Docs` của module trong project (đúng bản module của project), rồi `<skill>/docs/`
  (cùng bản với skill), không có thì dừng và báo user. Mẫu sink sfx cũng lấy được từ `<skill>/docs/Samples/`.
- `docs/` là bản chép, không sửa tay: repo phát triển module chép và kiểm bằng `Docs/tools/sync_skill_docs.mjs`
  (`--check` trước khi commit, cài skill, đẩy lên Feature Hub).
- `selftest` của skill không còn để lại folder tạm `uimotion-selftest-*` sau mỗi lần chạy (thoát bằng `process.exitCode`,
  nên phần dọn trong `finally` được chạy).

## 0.4.0 — 2026-09-28

Dùng chung cho mọi project: module và quy chuẩn chỉ chứa luật chung; điều riêng của từng project nằm trong file của project
đó. 191 test (EditMode + Play mode) xanh trên Unity 6000.3.16f1 (bản chép của project phát triển module).

**Chép bản này vào project đã có module 0.3**: trong Unity bấm **Tools/UI Motion/Dời asset của project ra khỏi folder module**
TRƯỚC khi thay folder (asset nằm trong folder module sẽ mất khi chép đè), xoá folder stub Odin riêng bản cũ dặn chép kèm
(stub đã vào module), rồi RoleMap → **Cập nhật theo bản module mới** → **Áp luật riêng của project**.

### Luật riêng của project
- File mới `ProjectSettings/UIMotionProject.json`: class tự có motion (`componentRules`, `componentRulesRemove`), keyword
  (`keywords.add` với `before`, `keywords.remove`), từ nhận prefab item / khuôn (`prefabWords`), folder quét, chuỗi loại
  trừ, sink sfx (`settings`), folder dữ liệu (`dataFolder`). Đọc bằng parser riêng: báo lỗi kèm dòng / cột, cảnh báo khoá lạ.
- **Tools/UI Motion/Áp luật riêng của project** (+ nút trên Inspector RoleMap, batch `UIMotionBatch.ApplyProjectRulesBatch`,
  `-uimotionProjectRules <file>`): áp idempotent, có Undo, Console liệt kê thay đổi. Luật component của project đứng trên
  bảng mặc định. RoleMap nhớ dấu file đã áp; file đổi mà chưa áp thì Inspector và Console nhắc.
- **Tools/UI Motion/Tạo file luật riêng mẫu**.
- Ghi chú của project `ProjectSettings/UIMotionProject.md` (skill sinh; module không đọc).

### Bảng mặc định không còn class của project nào
- Bỏ 4 dòng Component mặc định là class của một project cũ (screen controller, list animator, nút, chấm đỏ). Cập nhật RoleMap
  KHÔNG bỏ dòng nào: project đang có các dòng đó vẫn giữ, coi như luật riêng.
- Ghi chú dòng Role mặc định, comment, test không nhắc project nào. Test đổi tên (`Retrofit_NoFalsePositives`,
  `Retrofit_Phase5Rules`, `Keywords_MatchRealObjectNames`…), số giữ nguyên. Test mới: bảng mặc định chỉ có kiểu của module /
  Unity / TMP / DOTween.
- Từ nhận prefab item (`item, cell, slot, row, entry, element, card`) và prefab khuôn (`template, layout, tpl`) chuyển từ code
  vào RoleMap (field mới, asset cũ tự nhận mặc định), sửa được trong tab Keyword hoặc file luật riêng.
- Settings mới: folder quét mặc định `Assets` (trước là folder theo bố cục của một project); chuỗi loại trừ mặc định bỏ tên
  folder riêng, thêm /Sample/, /Demo/, /Demos/, /Examples/.
- Profile Team: số giữ nguyên, mô tả là cảm giác chuẩn của module (quy chuẩn 2.1).

### Asset của project ra khỏi folder module
- RoleMap, Settings, Profile tạo trong folder dữ liệu của project: folder asset đang có, không thì `dataFolder` của file luật,
  không thì `Assets/Resources/UIMotion` (`UIMotionDefaults.DefaultDataFolder`). Module tìm asset theo kiểu, không theo đường dẫn
  cứng.
- **Tools/UI Motion/Dời asset của project ra khỏi folder module** (+ nút trên Inspector RoleMap / Settings, batch
  `UIMotionBatch.MoveDataOutOfModuleBatch`): MoveAsset giữ GUID, bỏ folder Resources rỗng. Mở Editor mà asset còn trong module
  thì Console nhắc.
- `UIMotionBatch.SetupBatch` áp file luật riêng nếu có. Báo cáo quét ghi folder quét và trạng thái luật riêng.

### Một folder là đủ
- Stub Odin (`OdinStubs/`) nằm trong module, tự tắt khi có Odin. Bộ stub cũ ở folder riêng có tham chiếu cứng tới class của
  một project nên không chép được sang project khác; editor fallback cho class của project giờ đặt trong code của project.
- Mẫu sink sfx chung `Docs/Samples/UIMotionSfxSink.cs.txt` (thay mẫu viết cho một project).
- `UIMotion_Plan.md` (lộ trình, nhật ký pilot) không còn đi kèm module: là tài liệu của project phát triển module.

### Skill `ui-motion` (folder `Skill~/ui-motion`, Unity bỏ qua)
- SKILL.md dùng cho mọi project: luật lấy từ quy chuẩn của module, điều riêng lấy từ file của project, không dùng trí nhớ
  về project khác.
- `scripts/uimotion.mjs` (Node 18+, không cần cài gì): `survey` khảo sát project chỉ bằng đọc file (class tự có motion,
  class bấm được, badge, audio manager + enum / hằng tên tiếng, chỗ đóng screen, list pool, folder UI, thói quen đặt tên,
  bẫy môi trường); `init` sinh file luật (chỉ ứng viên độ tin cao) + ghi chú (mục cần xác nhận); `check-rules`;
  `install-module` (dời asset cũ ra trước khi thay folder); `install-skill` (cài vào `~/.claude/skills`); `selftest` trên
  project giả.
- Reference: file riêng của project, đưa module vào project, đọc khảo sát, sink sfx, chạy batch.
- Evals trên project giả, không dùng dữ liệu của project thật.

## 0.3.0 — 2026-09-25

Phase 4 → 6: pilot trên bản chép một project cũ, đủ component cho mọi role, Role Sandbox có bảng role. Test chạy xanh trên
Unity 6000.3.16f1; bộ test module chạy được cả trong project pilot (6000.2.6f2, Odin).

**Chép bản này vào project đã có module 0.2**: mở RoleMap, bấm **Cập nhật theo bản module mới** (hoặc menu
Tools/UI Motion/Cập nhật RoleMap theo bản module mới). Phần đã chỉnh tay giữ nguyên.

### Phase 4 — pilot project cũ (379 prefab UI)
Luật gắn rút ra từ pilot:
- **Họ nút chỉ gắn lên object TỰ bấm được.** Gắn lên phần hình con (btn_rim, btn_shadow…) giành mất pointer, OnClick của nút cha không chạy.
- Prefab **Variant** được quét; gốc variant nhận component dạng added-component override, phần thừa kế gắn ở prefab gốc.
- Prefab **dùng chung** (lồng trong prefab khác, làm gốc variant, tên chứa template / layout / tpl) không gắn hiện / ẩn cấp screen.
- Prefab **item** (tên chứa item, cell, slot, row, entry, element, card) coi như nằm trong list (list pool tái dùng item): không gắn Counter / Icon / Badge / Slider.
- Screen / Popup / Sheet / Toast / Tooltip không phải gốc chỉ gắn khi là lớp phủ thật (tắt sẵn trong prefab hoặc có nền dim riêng). Backdrop chỉ gắn khi cha là screen.
- List chỉ gắn khi có ScrollRect / LayoutGroup và không nằm trong item; list suy theo cấu trúc cần item cùng tên hoặc 3 item cùng bộ component. Screen suy theo cấu trúc phải có nút hoặc chữ bên trong.
- Counter chờ 0,25 s sau khi bật (screen nạp số lúc mở không bị đếm từ 0) và không gắn trong badge.
- Keyword: notification / notify → Badge (trước là Toast); bỏ price (Counter) và dot (Badge); thêm fullscreen → Decoration; nhóm Badge đứng trước Counter / Timer, Spinner trước Icon. Button có keyword card / tab đổi thành Card / Tab.
- **Prefab hỏng** (script mất, script còn file nhưng không còn class, prefab lồng / gốc variant mất): Unity không cho lưu, nên tool báo "Prefab hỏng" trong báo cáo và bỏ qua; gate đánh dấu không sửa bằng nút được. Prefab lưu lỗi trong phiên cũng được nhớ là hỏng.
- UIEffect và UI Extensions thành **tuỳ chọn** (define `UIMOTION_UIEFFECT`, `UIMOTION_UIEXTENSIONS` tự bật). Thiếu package thì chỉ mất `MorphSquircle` / `Dissolve` và sandbox cũ; module vẫn compile.
- Batch `UIMotionBatch.ScanReportBatch` (`-uimotionReport <file.md>`) ghi báo cáo quét + gate dạng Markdown và TSV để đọc trước khi gắn.
- Mẫu sink sfx cho project có audio manager riêng (0.4.0 thay bằng mẫu chung `Docs/Samples/UIMotionSfxSink.cs.txt`).

### Phase 5 — component cho mọi role
- `UIMotionToggle`: nhấn, dấu check pop, knob trượt + nền đổi màu (gán trong Inspector).
- `UIMotionTabs` (trên thanh tab): indicator trượt tới tab chọn, tab vừa chọn nảy, nội dung tab trượt vào theo hướng đổi tab. Tab là Toggle hay Button đều được.
- `UIMotionCard`: nổi lên khi được chọn (Toggle), `Flip()` lật đổi mặt trước / sau.
- `UIMotionDropdown`: danh sách mở giãn dọc + item lần lượt, đóng co lại (Dropdown và TMP_Dropdown).
- `UIMotionSlider`: code đổi giá trị thì thanh đuổi theo (giá trị thật không đổi), handle nhún khi kéo, tick khi qua bước.
- `UIMotionScrollbar`: hiện khi cuộn, mờ khi nghỉ.
- `UIMotionSheet` (Panel role Sheet): tuỳ chọn kéo xuống để đóng.
- `UIMotionToast`: trượt vào / ra; tuỳ chọn tự ẩn sau thời gian giữ, chờ lượt (một cái một lúc), chạm để tắt.
- `UIMotionTooltip`: mọc ra từ phía nút neo, tuỳ chọn giữ trong màn hình.
- `UIMotionBadge`: pop khi hiện, punch khi số tăng, tuỳ chọn ẩn khi về 0.
- `UIMotionTimer`: còn ≤ 10 s thì nhịp + đổi màu + sfx low mỗi giây; về 0 bắn complete; cộng giờ thì thoát.
- `UIMotionIcon`: code đổi sprite là tự Pop / Flip / Fade; loop Spin / Pulse / Float; đổi dồn dập (ảnh động bằng code) thì tự thôi.
- `UIMotionSpinner`: xoay liên tục hoặc theo nấc; reduce motion thì thở alpha.
- `UIMotionDecoration`: loop Float / Pulse / Spin / Glow / Sway, lệch pha ngẫu nhiên.
- `UIMotionTitle`: chữ hiện lần lượt (Text và TMP), sóng / nhảy, `Replay()`.
- `UIMotionInputField`: chọn ô thì nổi nhẹ + gạch chân, `ShowError()` rung + đỏ.
- `UIMotionScreenFx`: `Shake` / `Flash` / `Hit` / `Crit` toàn màn, gọi qua `UIMotionScreenFx.Main`.
- `UIMotionFlyTo`: coin bay theo đường cong về đích, mỗi đồng tới bắn `OnArrive` + sfx `ui.counter.arrive`.
- `UIMotionRecipes`: `OpenChest`, `LevelUp`, `PurchaseSuccess`, `Error` — chuỗi dùng lại motion của từng role.
- Sự kiện mới cuối `UIMotionEvent`: Focus, Arrive. Tham số mới cuối `UIMotionParam`: SpinPeriod, FlyDuration, FlyStagger, FlyArc (Profile tự suy ra).
- Binder gắn các component trên theo role; phạm vi mới trong Settings: Slider, Badge, Timer, Icon, Ô nhập, Loading (bật sẵn); Scrollbar, Tiêu đề, Trang trí (tắt sẵn — bật là screen cũ đổi cảm giác).
- Gate: M-5 đòi đúng component (UIMotionToggle / Tabs / Card / Dropdown), Sheet / Toast / Tooltip tính vào M-1; rule mới **M-12** (slider, badge, timer, icon, ô nhập, loading… có motion, theo phạm vi trong Settings); M-8 tính cả loop của Spinner / Decoration / Icon / Title.
- Nút thử trong Play mode trên Inspector (Card lật, ScreenFx, InputField báo lỗi, FlyTo bay, Title chạy lại).
- Mỗi component có test chạy thật trong Play mode: kịch bản chính, ngắt giữa chừng, reduce motion.

### Phase 6 — hoàn thiện
- **Role Sandbox** có 2 trang, chuyển bằng thanh tab trên cùng (chính là UIMotionTabs): *Screen giả* như cũ, và **Bảng role**: 32 ô, mỗi role một ô có vật mẫu thật, dòng RoleMap đang dùng (đọc lại liên tục — sửa RoleMap lúc Play là ô đổi theo) và nút Hiện / Ẩn / Thử nối bằng OnClick. Có cả FlyTo, Recipes, None, Chưa rõ.
- RoleMap nhớ bản module đã gộp (`DefaultsVersion`). Có nút **Cập nhật theo bản module mới** trên Inspector, menu cùng tên, batch `UIMotionBatch.UpgradeRoleMapBatch`. Mở Editor mà RoleMap cũ thì Console nhắc một lần mỗi phiên. Cập nhật thêm role / sự kiện sfx / luật / keyword còn thiếu, không đụng clip, kiểu hiện / ẩn, feedback, keyword riêng.
- Cửa sổ Quét hiện đủ các phạm vi gắn.
- Test Play mode chờ theo frame thật của game (`Time.frameCount`), không tin một lần `yield null`. Game giới hạn FPS (đặt `targetFrameRate`) thì một lần yield có thể chưa qua frame nào.

### Tương thích
- Không đổi field / số enum đã lưu. Enum, `UIMotionParam`, `UIMotionBindKind` chỉ thêm ở cuối.
- Project cũ chép module mới: Settings giữ nguyên; phạm vi mới lấy mặc định; RoleMap cần bấm Cập nhật (xem trên).
- Keyword "price" không còn là Counter: label giá tiền tĩnh không bị gắn đếm. Project nào muốn giá tiền đếm thì thêm lại keyword.

## 0.2.0 — 2026-09-25

Phase 0 → 3. 102 test (EditMode + Play mode) chạy xanh trên Unity 6000.3.16f1.

### Phase 0 — nền và feeling
- **Profile Team** (preset mới, mặc định cả game): số calibrate trên UI của một project đang chạy — screen mở 0.3 s scale 0.8 → 1 OutExpo, đóng 0.2 s InQuad; PopIn từ 0 OutBack, slide 240 px 0.29 s OutCubic, stagger 0.06 s, rơi nảy, nổi lên + fade + pop; nhấn 0.9, nhả 0.2 s. Áp preset giờ thay cả 5 trục lẫn danh sách ghi đè.
- Template mới: `PopSoftIn/Out`, `ZoomIn/Out`, `DropIn/Out`, `RiseIn/Out`; cửa chung `UIMotion.Show(kind…)` / `UIMotion.Hide(kind…)` / `ReverseOf`; `UIMotion.SetActiveAnimated(go, active)`.
- Tham số mới cuối `UIMotionParam`: SoftPopFromScale, ZoomFromScale, DropDistance, DropDuration, RiseDistance, RiseFromScale. Ease slot mới: ShowSoft, HideSoft, Drop.
- **Role**: enum `UIRole` (30 role, số cố định), component `UIMotionRole` (gắn tay, None phải có lý do, bỏ qua cả nhánh).
- **UIMotionRoleMap**: bảng Role (hiện / ẩn / feedback / loop / sfx theo sự kiện), bảng Component (component nào báo role gì, cái nào đã tự có motion), bảng Keyword (seed từ tên object thật của project UI cũ). Inspector 3 tab, ô gõ thử tên.
- **UIMotionSettings**: phạm vi tự gắn, folder quét, gate, reduce motion, hệ số tốc độ, âm thanh.
- **Sfx**: hub `UIMotionFeedback` (key `ui.<role>.<event>`), player kèm sẵn `UIMotionSfxPlayer` (kéo clip vào RoleMap là kêu), sink riêng qua `IUIMotionSfxSink`.
- TextMeshPro: overload `CountUp`, `AnimateNumber`, `Typewriter` (dùng maxVisibleCharacters), `SwapText`, `PopText`, `DamageText`. Define `UIMOTION_TMP` / `UIMOTION_UNITASK` tự bật theo package (`UIMotionDefineBootstrap`).
- Xem thử trong Scene View không cần Play (`UIMotionEditorPreview`), cả nút Play của `UIMotionPlayer`.

### Phase 1 — component lõi
- `UIMotionPanel` (screen, popup, panel, sheet, toast): tự hiện khi bật, `Hide()` ẩn xong mới tắt, ngắt giữa chừng được, khoá bấm khi đang đóng, khung popup (Content) + nền dim (Backdrop) chạy cùng, cảnh báo một lần khi bị SetActive(false) thẳng.
- `UIMotionBackdrop`: fade tới alpha đặt trên Image, tuỳ chọn chạm nền để đóng.
- `UIMotionButton`: tự bắt pointer (không cần Button), nhấn theo scale nghỉ thật, nút khoá không nhún, sfx press / click.
- `UIMotionCounter`: code cũ gán `text = "1,500"` là tự đếm từ số đang hiện, giữ định dạng (`$1,250`, `x5`, `80/100`, `1.2K`), punch khi tăng. Text và TMP.
- `UIMotionLayoutTransition` (List): hiện lần lượt, item thêm vào tự pop đúng chỗ layout, item còn lại trượt về chỗ khi thêm / bớt, `RemoveItem`.
- Game Destroy object giữa lúc motion chạy (dọn list để dựng lại, huỷ screen, counter đang flash): tween của `UIMotion.Show` / `Hide` và feedback (punch, shake, nudge, press, flash) tự dừng theo object, DOTween không báo lỗi. Counter hoàn tất punch / flash khi bị tắt. API mới `UIMotion.CompleteFlash(graphic)`.
- Scene **Role Sandbox** (`Tools/UI Motion/Dựng scene Role Sandbox`): screen giả nối bằng OnClick, không code.

### Phase 2 — tự gắn
- `UIMotionRoleResolver`: suy role theo thứ tự gắn tay → component → keyword → cấu trúc (gốc screen kéo giãn, nền dim con đầu, LayoutGroup nhiều item giống nhau).
- `UIMotionBinder`: lập / gắn component còn thiếu; không gắn chồng lên motion sẵn có (class tự có motion của project, Animator…); không đụng object của prefab lồng; không tự gắn Counter trong list (item tái dùng).
- **Tools/UI Motion/Quét và gắn motion…**: quét không đổi gì → tick → gắn; gỡ những gì tool đã gắn.
- Save prefab trong Prefab Mode là tự gắn (Ctrl+Z được).
- `UIMotionAutoBind`: lưới an toàn lúc chạy cho screen chưa quét / sinh lúc chạy (component không lưu vào prefab).
- Interop cho list animator có sẵn của project: `IUIMotionHideable` để `UIMotion.SetActiveAnimated` chạy ngược xong mới tắt; class của project bắn show / hide qua hub.

### Phase 3 — gate
- `UIMotionLint`: rule M-1 … M-10; vi phạm thiếu component sửa bằng một nút.
- Sổ nợ `ProjectSettings/UIMotionDebt.json` (lý do, người, ngày; quá 30 ngày tô vàng).
- **Tools/UI Motion/Kiểm tra (gate)…**; build gate 3 chế độ (Tắt / Cảnh báo / Chặn build); CLI `UIMotionLint.RunCli` (+ `-uimotionStrict`).

### Tương thích
- Không đổi field / số enum đã lưu trong prefab cũ. Enum chỉ thêm ở cuối.
- `UIMotionDefaults.FallbackResourcePath` đổi sang `UIMotionProfile_Team` (thiếu asset thì dựng Team trong bộ nhớ).
- Class của project nối vào module (`IUIMotionHideable`, hub) phụ thuộc module.
