# Changelog

## [0.1.0] - 2026-09-18
### Added
- Bản đầu tiên, tách từ `Assets/_Project/Editor/ProjectSpecific/FigmaImport/` của project i001.
- **Hook vào bridge**: `FigmaFeatureImportHook` implement `IFigmaImportPostProcessor`, chạy sau mỗi
  `Sync Document` / `Run Post-Processors (no Sync)` của `com.ezg.figma-bridge`; mỗi frame bridge vừa ghi
  được chụp raw rồi dựng lại thành **variant của vỏ `screenTemplatePath`** (bản tại chỗ), và dựng thêm
  **bản ship** ở `outputPrefab` cho frame có entry `enabled`.
- **Pipeline dựng màn**: bật nhánh popup/full của vỏ, đặt thân đúng chỗ, unpack bản thô, đánh số node
  trùng tên, `renameNodes` / `dropNodes`, map component Figma → template project, `buttonNodes`,
  `FigmaImage` → `Image`, chuẩn hoá font + rect chữ, dời sprite khỏi thư mục của bridge (tái dùng ảnh
  trùng byte), dọn `LayoutElement`/`ContentSizeFitter`/marker bridge, neo thân, gắn + wire controller,
  codegen `<X>Controller.Figma.cs` với lượt chạy thứ hai sau khi compile.
- **Audit** (`FigmaImportAudit`): hard-check variant/nhánh/thân/controller/font/missing script + snapshot
  PNG 1080×2400 không cần Play.
- **Menu** `Tools/EZG Technical Art/Figma Feature Importer/…`: Rebuild Screens, Audit Screens,
  Select Import Settings.

### Changed (so với bản trong i001)
- **Không còn tham chiếu type của game.** Lớp cơ sở controller khai bằng tên (`baseControllerTypeName`),
  enum `FeatureType` đọc/ghi qua `SerializedProperty.enumNames` — enum đánh số thưa vẫn đúng giá trị.
- **Mọi đường dẫn và tên node của vỏ là data**: `screenTemplatePath`, `templatesRoot`, `rawScreensFolder`,
  `spriteReuseRoot`, `defaultFontPath`, `backgroundNode`, `popupNode`, `fullScreenNode`, `popupBodyPath`,
  `popupFrameNodes`, `fullScreenContentNode`, `fullScreenChromeNodes`, `buttonFaceSlot`, `buttonTextSlot`,
  `popupMarkerPattern`, `snapshotFolder`. Mặc định giữ đúng giá trị của template EZG.
- **Codegen không ép namespace của game**: `codegenUsings` + `localizeCallFormat` (`{0}` = const KEY,
  `{1}` = const FALLBACK; để trống thì gán thẳng FALLBACK).
- **Bỏ Odin**: settings dùng inspector IMGUI riêng, package compile được ở project không có Odin.
- Settings asset tìm theo type ở bất cứ đâu trong `Assets/`, chưa có thì tạo tại
  `Assets/FigmaFeatureImportSettings.asset`; seed mặc định chỉ còn bảng template map (không còn màn pilot
  của i001).
- Bỏ define `FIGMA_BRIDGE_POSTPROCESS`: package phụ thuộc cứng `com.ezg.figma-bridge` ≥ 0.4.0 nên luôn có
  API post-process.
