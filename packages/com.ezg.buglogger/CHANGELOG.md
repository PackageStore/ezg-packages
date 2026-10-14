# Changelog

## [0.2.3] - 2026-10-08

### Fixed
- Ô nhập trên Android vẫn không xoá / không chèn được giữa chữ (bản 0.2.2). Gốc lỗi: app chạy GameActivity thì bàn phím
  của Unity (`TouchScreenKeyboard`) gọi `restartInput` mỗi lần nhận vị trí con trỏ, mà InputField ẩn ô nhập (cách sửa
  của 0.2.2) đẩy con trỏ xuống bàn phím mỗi frame ⇒ bộ gõ bị khởi động lại liên tục, phím xoá / chữ chèn giữa / dấu
  tiếng Việt bị nuốt. Không ẩn ô nhập (InputField mặc định) thì không có con trỏ, chỉ gõ/xoá được ở cuối.
  Giờ chạm vào ô trên Android mở ô sửa native (EditText thật) nằm ngay trên bàn phím, có tên ô + nút Xong: đủ con trỏ,
  kéo tay cầm chọn chữ, dán, xoá/chèn giữa chữ, bộ gõ tiếng Việt; chữ đồng bộ vào ô của form theo từng phím, form tự
  co để ô đang gõ vẫn thấy được. Xong / Back / chạm ra ngoài đều đóng và giữ chữ. Dựng bằng JNI, không thêm file
  `.java` (không phụ thuộc Gradle/R8 của project). Lỗi JNI bất kỳ ⇒ tự quay về bàn phím Unity. Máy có bàn phím cứng
  vẫn sửa ngay trong ô như trên máy tính; iOS giữ nguyên ô nhập native trên bàn phím.

## [0.2.2] - 2026-10-08

### Fixed
- Ô nhập của form trên điện thoại: gõ được nhưng không sửa/xoá được giữa chữ. Android chạy GameActivity không có ô
  nhập nổi trên bàn phím nên InputField mặc định không vẽ con trỏ, chạm không đặt được con trỏ, chỉ xoá được từ cuối.
  Giờ Android sửa ngay trong ô (con trỏ, chạm đặt con trỏ, kéo chọn chữ, xoá/chèn giữa chữ); iOS giữ ô nhập native
  trên bàn phím, chạm vào chữ trong form thì dời con trỏ tới đó.
- Chạm vào ô chưa focus đặt con trỏ đúng chỗ chạm (trước nhảy về cuối chữ; ô nhiều dòng còn bị lệch dòng).
- Vuốt bắt đầu trên ô nhập giờ cuộn form thay vì mở bàn phím; chạm vào ô lúc form đang trôi thì dừng trôi.
- Đóng bàn phím bằng phím Back (Android báo Canceled) làm mất sạch chữ vừa gõ — InputField gốc trả chữ về như lúc mở
  ô. Giờ luôn giữ chữ.
- Chuyển từ ô này sang ô khác không còn làm card bật ra full rồi co lại (bàn phím tắt-bật trong lúc chuyển).
- Ô ở nửa dưới form bị bàn phím che: `TouchScreenKeyboard.area` trả 0 trên Android (GameActivity, và cả Activity khi
  ẩn ô nhập) ⇒ đọc inset IME của cửa sổ qua JNI; ô đang gõ được cuộn vào tầm nhìn khi bàn phím trồi xong, khi đổi ô
  và khi khung nhìn đổi cỡ.

### Changed
- Phụ thuộc thêm `com.unity.modules.androidjni`.

## [0.2.1] - 2026-10-08

### Fixed
- Một report server cứ trả lỗi tạm (5xx / 403) không còn chặn mọi report mới hơn trên máy tới khi bỏ cuộc (24 h):
  report đó tự chờ backoff riêng, uploader gửi tiếp report sau nó. Không có response (mất mạng / timeout) thì
  vẫn chờ chung cả hàng đợi. Lỗi gặp thật: feedback gửi vào project chỉ có forum bị 500 vĩnh viễn, bug QA gửi sau
  nó kẹt trong outbox kể cả khi cài bản build mới.

## [0.2.0] - 2026-10-07

### Added
- UI dùng chung `Ezg.BugLogger.UI` (`BugLoggerUI`): nút nổi kéo thả (dạt mép, nhớ vị trí, mờ khi rảnh, huy hiệu
  report chờ gửi), form báo lỗi (ảnh chụp + xem lớn + bật/tắt đính kèm, tiêu đề, mô tả, phân loại, tỉ lệ tái hiện,
  màn hình / tính năng, tag, người được tag), toast, animation vào/ra, né bàn phím ảo, phím Back.
- Toàn bộ sprite + font (Be Vietnam Pro, OFL) của UI nằm trong package; overlay canvas riêng tự co theo hướng màn
  hình + safe area ⇒ UI giống hệt nhau ở mọi project, không dựa vào template / art style / localize của game.
- Editor `BugLoggerUIPrefabBuilder` (Tools → EZG → Bug Logger → Rebuild UI Prefab) dựng lại prefab overlay.
- Test EditMode: prefab UI chỉ tham chiếu asset của package, không thiếu field.

### Changed
- Phụ thuộc thêm `com.unity.ugui`.

## [0.1.0] - 2026-10-06

### Added
- Lõi headless của Bug Logger, port từ package bug logger nội bộ và sửa theo BugHub contract.
- `BugLogger` facade: `Initialize`, `GetFormOptionsAsync`, `Submit`, `EncodeScreenshot`, `PendingCount`,
  event `ReportQueued` / `ReportSent` / `ReportFailed` (main thread).
- `BugLoggerSettings` (assembly riêng, không gate): `projectCode`, `endpointOverride`.
- `BugReportDraft` + `BugReportKind`; `report.json` schema 1 với `kind`, `category`, `reproduceRate`,
  `actual` / `expected` / `steps`, `tagIds`, `mentionIds`, `app.branch`, `device.id`; giới hạn từng trường
  theo contract (title 100, các trường dài 4000, reproduceRate 16, tag 4, mention 5, extra 32 × 256).
- `BugFormOptionsClient`: `GET /v1/projects/<code>/form`, cache disk, fallback cache → mặc định.
- Uploader bỏ cuộc một report sau 50 lần thử hoặc 24 h kể từ lần thất bại đầu (→ `failed/`), tôn trọng
  `Retry-After`.
- Baker build info thêm `branch`, đọc `CI_COMMIT_SHA` / `CI_COMMIT_REF_NAME`, bake mọi build player.

### Changed
- Bỏ cờ bật/tắt lúc chạy của package: host tự quyết lúc nào hiện UI báo lỗi (package có trong mọi build).
- Transport chỉ gửi header `X-BugHub-Project`; `403 intake_disabled` là lỗi tạm (giữ trong outbox).
- Endpoint + mã project lấy từ `BugLoggerSettings`, không còn gán qua code.

### Removed
- Mọi UI (form, icon nổi, sample prefab) và phụ thuộc UI framework / thư viện tween / thư viện inspector (UI quay
  lại ở 0.2.0, tự chứa trong package).
- Ước lượng độ dài issue body phía client (server tự cắt).
