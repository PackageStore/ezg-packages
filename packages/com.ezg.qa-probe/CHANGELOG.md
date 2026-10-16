# Changelog

## [1.3.0] - 2026-10-16
### Added
- `Resources/EzgQaProbeConfig.json` thêm 5 mục (thiếu = hành vi cũ): `screenBaseTypes` (mảng tên lớp cơ sở màn hình, ngắn hoặc
  đầy đủ, lớp generic ghi không có `` `1 ``), `cheatTypeName`, `cheatFlagMember`, `cheatEnableMethod`, `cheatDisableMethod` (type
  / cờ / hàm cheat của game không theo quy ước `GameSystems.isCheat` …). Skill `/auto-test-device` sinh file này bằng
  `qadevice.py profile sync` từ block `qa` của `.claude/project-profile.json`; `/ping` trả `config.screenBaseTypes` đang dùng.
- Nhận ra màn hình theo `screenBaseTypes` thay vì danh sách viết cứng (`/ui.screens`, `layer` của từng phần tử, `/context.screen`).
- `/pseudoloc?on=1|0&mode=expand|accent|both`: biến mọi chữ đang hiện (UI.Text, TMP) thành chữ giả localize — bọc `[ ]`, chữ
  cái có dấu, đệm `~` ≈ 30% — để bắt khung chữ hẹp / chữ bị cắt / font thiếu glyph trước khi có bản dịch; `on=0` trả lại chữ
  gốc; probe tắt (`/stop`) cũng tự trả lại. Chữ game set lại SAU khi bật không được theo dõi — gọi lại `on=1` để đổi tiếp
  (trả `count` = số phần tử vừa đổi). `/ping` và `/ui` có `pseudoloc: true` khi đang bật.
- `/ui` và `/context` trả `probeVersion`.
- `/ui`: flag mới trên từng phần tử (port từ UiAuditSuite của com.ezg.autotest — assembly đó không có trong bản release nên
  không link được, copy logic) kèm `flagDetail {flag: "chi tiết"}`. Toàn bộ là phép đọc thuộc tính / hình học, không raycast
  thêm, không ép layout, nên chạy luôn trong `/ui` (không cần tham số). Flag cũ giữ nguyên tên (camelCase như trước):

  | Flag | Ý nghĩa | Severity gợi ý |
  |---|---|---|
  | `deadButton` | Button bấm được nhưng `onClick` rỗng (Inspector lẫn `AddListener`) và không có handler chạm nào khác trên object | medium |
  | `noRaycastTarget` | Phần tử tương tác không có Graphic nào (chính nó hoặc con) bật Raycast Target → chạm không tới | high |
  | `overlapButton` | Hai nút cùng cha chồng nhau > 50% diện tích nút nhỏ; flag gắn trên nút vẽ trước (bị đè), detail ghi tên + id nút đè | medium |
  | `missingSprite` | Image (đúng type Image) không sprite, hiện thành khối màu (không trắng); detail `#RRGGBBAA W×Hpx` | low |
  | `solidBackground` | Image không sprite phủ ≥ 50% màn — nền phẳng, thường cố ý | info |
  | `noSprite` | (mở rộng) ô trắng đục ≥ 6 px không sprite, nay bắt cả ô có con (nền nút trắng thiếu art) | medium |

### Changed
- `ProbeAutoConfig.Detect` có overload nhận tên type / cờ / hàm từ config; hàm bật do dev chỉ đích danh được nhận cả bản không
  tham số (quy ước template vẫn chỉ nhận `TryEnableCheat(Action<bool>)`). Cache lớp màn hình được xoá mỗi lần kích hoạt.
- Test PlayMode thêm: parse config mới, dò cheat type riêng, lớp màn hình từ config (ngắn / đầy đủ / generic), pseudoloc
  đổi–trả qua endpoint + tự trả khi tắt, `/ui` có `probeVersion` và các flag mới.

## [1.2.1] - 2026-10-15
### Fixed
- Ảnh chụp (`/shot`, ảnh tự chụp của `/bug`) dính nút nổi báo lỗi của Bug Logger → ảnh gửi BugHub có cả nút. Giờ probe
  gọi `BugLoggerUI.HideForCapture` (com.ezg.buglogger ≥ 0.2.4, qua reflection) để ẩn UI Bug Logger đúng frame chụp rồi
  hiện lại. Game không có Bug Logger / bản cũ hơn thì chụp như trước.
### Added
- `/capturehidden?sec=N`: ẩn UI Bug Logger N giây (tối đa 10, 0 = hiện lại) cho tool chụp từ ngoài app (adb screencap,
  simctl); trả về sau 2 frame để frame trên màn đã sạch nút. `qadevice.py` gọi quanh mỗi lần chụp.

## [1.2.0] - 2026-10-15
### Added
- Package hoá EZG QA Probe (trước đây skill `/auto-test-device` chép `EzgQaProbe.cs` vào `Assets/EzgQaProbe/` của từng
  project — v1.0.0 / v1.1.0). Project chỉ cần một dòng trong `Packages/manifest.json`; `qadevice.py start` tự thêm dòng
  đó và gỡ bản chép cũ.
- Không cần cấu hình: lúc kích hoạt, probe tự dò `GameSystems.isCheat` (cổng release), `GameSystems.TryEnableCheat(Action<bool>)`
  (bật cheat qua CheatGate) và `GameSystems.DisableCheat` trong code game. `TryEnableCheat()` không tham số (gate Remote
  Config đời cũ) không được dùng vì nó có thể tắt cheat đang bật. `Resources/EzgQaProbeConfig.json` chỉ còn để ghi đè
  từng mục (`"none"` = tắt mục đó). `/ping` trả config đang dùng.
- `com.ezg.cheat-gate` ≥ 0.1.1 (tuỳ chọn, `versionDefines`): probe vừa được kích hoạt là hỏi CheatGate một lần như QA tap
  version 10 lần. Máy có trong danh sách máy test → mở cổng probe ở bản release + bật cheat của game; máy khác → cổng
  đóng, `/ping` trả Device ID. `/unlock` hỏi lại (được gọi khi cổng đóng — server quyết), `/cheat?on=1|0` bật/tắt cheat game.
- `link.xml` (giữ nguyên assembly khi Managed Stripping cao) đưa vào build qua `IUnityLinkerProcessor` vì UnityLinker
  không đọc link.xml nằm trong package.
- `/stop`: tool kết thúc phiên thì probe thôi nghe; token của phiên đã dừng (hoặc bị phiên mới thay) không bao giờ được
  dùng lại, kể cả khi intent lúc mở game vẫn còn mang nó.
### Fixed
- Nối lại vào game đang chạy (`start --attach`) trên Android 11+: app không đọc được file trigger do `adb shell` ghi vào
  `Android/data/<pkg>/files` (Access denied) → lấy lại focus thì probe đọc cả intent mới (Unity `setIntent` trong
  `onNewIntent`) và đổi sang token mới khi đang nghe với token phiên trước.
- Test PlayMode (`Tests/Runtime`): tự dò config, bật/tắt cheat, cổng release, server từ chối khi thiếu/sai token (401) và
  khi cổng đóng (423), máy có quyền CheatGate mở cổng + bật cheat đúng một lần, đổi token khi lấy lại focus, `/stop`
  và không dùng lại token đã dừng.
