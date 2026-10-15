# Changelog

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
