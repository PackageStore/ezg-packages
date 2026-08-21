# Changelog

## [0.1.0] - 2026-08-21
### Added
- Initial release extracted from `Assets/_Project/Editor/Shared/{EzgKit,Marketing,Firebase}`.
- `EzgKitWindow` — cửa sổ tool tổng dưới gốc menu `Ezg`, cột nav dọc, header bar hiện package name /
  bundle id / version ở mọi tab, tab Tổng quan chạy hết theo đúng thứ tự Marketing → Firebase.
- `EzgKitStyles` + `SetupGui` — bộ style và widget dùng chung (card, chip, foldout, `KeyValue`,
  `DiffRow`, ô nhập tay / mật khẩu / chọn file, bước làm tay).
- `IEzgKitPage` — contract để một tool tự đăng ký thành một tab.
- Tab **Marketing** — tải Google Sheet thông số rồi ghi vào PlayerSettings, AdsConfig,
  AppLovinSettings, FacebookSettings, AndroidManifest và GameConstant.cs; có chế độ dry-run.
- Tab **Firebase** — đọc service account `.json` (tự lấy `project_id`, dò project khả dụng), tạo app
  Android + iOS theo id trong PlayerSettings, tải `google-services.json` / `GoogleService-Info.plist`,
  cảnh báo đỏ khi config trong `Assets/` thuộc project khác.
