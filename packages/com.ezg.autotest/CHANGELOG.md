# Changelog

Mọi thay đổi đáng chú ý của `com.ezg.autotest`. Định dạng theo [Keep a Changelog](https://keepachangelog.com/),
đánh số theo [Semantic Versioning](https://semver.org/).

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
