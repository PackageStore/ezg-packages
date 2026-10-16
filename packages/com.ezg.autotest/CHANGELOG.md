# Changelog

Mọi thay đổi đáng chú ý của `com.ezg.autotest`. Định dạng theo [Keep a Changelog](https://keepachangelog.com/),
đánh số theo [Semantic Versioning](https://semver.org/).

## [0.2.0] - 2026-10-16

Bớt nhiễu cho repo kế thừa asset / màn cũ chưa dọn, và mở report cho tool QA bên ngoài đọc.

### Thêm

- **Liveness feature** (`liveness` trong Settings): quét tĩnh code + prefab/scene/asset xem feature nào không ai
  tham chiếu (ngoài map bundle `AssetBundleName.cs` / `AssetRefs.cs`, ngoài prefab màn của chính nó, có tính chuỗi
  `"TênFeature"`); feature chết ⇒ Smoke / UI Audit / Button Sweep / Visual bỏ qua. `forceLive` / `forceDead` /
  `observedLive` (tool QA ghi màn đã thấy mở thật) / `deadFeaturePatterns`. Cache theo hash project ở
  `Library/EzgAutoTest/liveness-cache.json`; kết quả chỉ nằm trong bản snapshot của lượt chạy (và config bake lên
  device), không bao giờ ghi vào `EZGAutoTestSettings.json`.
- Luật tĩnh **`Feature.Dead`** (Info) liệt kê feature chết kèm lý do; `Feature.LiveObservedOnly` cho feature chỉ sống
  nhờ tool QA / `forceLive`.
- **`report.json` schema 0.2**: enum dạng chuỗi, `issues[]` phẳng ở gốc (có `screen`), `summary.issuesTotal /
  bySeverity / bySuite`, `generatedAt`, `version`; `AutoTestReportJson.Serialize / Deserialize` đọc được cả 0.1.
  Report trên device cũng theo schema này.
- UI Audit: `UI.Occluded` (Info) — nút bị **lớp khác** (popup / overlay / toast phủ ≥ 35 % màn) che, khớp cách EZG
  QA Probe báo "occluded".
- Visual Regression: số đo "Vùng động bị mask", "Text động bị mask"; issue `Visual.Diff` ghi % đã mask + độ phân giải.

### Sửa

- **Missing reference** chấm severity theo **phạm vi load** (`LoadScopeIndex`): asset trong Resources / AssetBundle /
  closure scene build ⇒ Major (giữ nguyên); asset chỉ được cụm không load được tham chiếu ⇒ Minor; asset không ai tham
  chiếu ⇒ Info nhóm `Asset.Unreferenced`. Mọi GUID gặp khi quét được gom để biết ai tham chiếu ai.
- UI Audit `UI.Blocked`: thay nhịp chờ cố định 0,4 s bằng **chờ UI đứng yên** (blocker / rect / alpha không đổi 0,3 s
  liên tục, poll 0,1 s, tối đa 2 s) — hết bắt nhầm popup đang trượt vào.
- Visual Regression: 2 khung chụp cách **1 s** (đồng hồ đổi mỗi giây), vùng động nở theo ô 8 px, text có pixel đổi
  mask trọn khung; mô tả nói rõ baseline theo độ phân giải chụp.

### Lưu ý nâng cấp

- Editor đọc report cũ bằng `AutoTestReportJson.Deserialize` — code ngoài package đang `JsonUtility.FromJson<TestRunReport>`
  trên `report.json` 0.2 sẽ mất `severity` / `status` (chuỗi) → đổi sang API mới.
- Thêm khối `liveness` vào `ProjectSettings/EZGAutoTestSettings.json` (thiếu = mặc định bật, `_Retired$`).

## [0.1.1] - 2026-09-29

### Sửa

- Menu theo đúng quy ước chung của các package EZG (gốc `Ezg`, không tạo thêm menu gốc `EZG` riêng):
  cửa sổ mở ở **`Ezg > Auto test system`** (phím tắt giữ nguyên Ctrl/Cmd+Shift+T), tạo kịch bản ở
  `Assets/Create/Ezg/Auto Test Scenario`, Settings ở **Project Settings > Ezg > Auto Test**.

## [0.1.0] - 2026-09-29

Bản đầu tiên.

### Thêm

- **Lõi**: `AutoTestSuite` / `AutoTestCase` / `AutoTestContext` (bước, Check/Assert, issue theo severity
  Blocker → Info, số đo có ngưỡng, ảnh chụp, file đính kèm, chờ có timeout + huỷ), bộ đếm frame riêng chạy
  được ở Edit mode, Play mode và device (không phụ thuộc UniTask), bắt log Unity theo từng case, fingerprint
  issue ổn định giữa các lượt chạy.
- **Adapter game**: `EzgTemplateAdapter` (UIManager + GameEnums.Features, PlayerResource + EnumBase.MoneyTypes,
  DataPlayer, RewardsService, PurchaseManager, DataManager — phản chiếu theo tên type, tắt đúng capability
  thiếu) và `GenericGameAdapter` (scene + EventSystem) làm lưới an toàn; interface `IGameAdapter` cho project
  kiến trúc khác.
- **`UiDriver`**: tìm object theo path/tên/text, kiểm tra hiển thị / interactable / bị che, bấm - giữ - kéo -
  nhập text qua EventSystem như ngón tay thật.
- **`GameFlow`**: chờ boot, đóng popup đầu game, ghi "màn nền", mở/đóng màn an toàn, quay về màn nền.
- **Suite**: Kiểm tra tĩnh, Smoke PlayMode, UI Audit, Button Sweep, Monkey / Stress, Logic / Economy,
  Performance, Visual Regression (baseline trong `AutoTestBaselines/`), Device / E2E qua adb (APK test chạy
  được cả trên Firebase Test Lab chế độ Game Loop), Kịch bản riêng.
- **Cửa sổ** `EZG > Auto Test System` (Ctrl/Cmd+Shift+T), Settings trong Project Settings > EZG > Auto Test
  (`ProjectSettings/EZGAutoTestSettings.json`).
- **Báo cáo** mỗi lượt: `report.html` (điểm sức khoẻ, so sánh lượt trước với nhãn MỚI, nút Sao chép bug),
  `report.json`, `junit.xml`, `issues.csv`, `summary.md`, ảnh chụp và file đính kèm.
- **Sandbox dữ liệu người chơi**: sao lưu + khôi phục PlayerPrefs quanh suite có sửa dữ liệu, tự khôi phục
  sau khi Editor crash.
- **CLI** `Ezg.AutoTest.Editor.AutoTestCli.Run` cho CI (exit code 0 / 2 / 3 / 4).
- **Kịch bản riêng + AI**: `AutoTestScenario` + `[AutoTestScenario]`, `IAutoTestProjectHooks` /
  `AutoTestProjectHooks`; `AutoTestScaffolder` + menu `Assets/Create/EZG/Auto Test Scenario` sinh thư mục
  AutoTests + asmdef, file kịch bản, hooks, adapter template, prompt AI; skill Claude Code
  `ezg-autotest-scenario`; hướng dẫn `Documentation~/AI-SCENARIO-GUIDE.md`.
- **Sample** "Example Scenarios": mở màn + kiểm tra nút, tiền tệ khứ hồi, khung project hooks.
