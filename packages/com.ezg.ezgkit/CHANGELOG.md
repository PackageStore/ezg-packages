# Changelog

## [0.3.0] - 2026-08-26
### Added
- Cột nav chia **hai nhóm**: **Setup Ezg** (Marketing / Firebase / Social / Readiness — giữ nguyên) và
  **Nhà phát hành** — một tab mỗi bộ SDK, sinh từ `PublisherRegistry` (`Editor/Publisher/`): **Ezg (trong
  nhà)** = bộ SDK mặc định của template, **Neptune** (CPI test), **SayGame** (placeholder). Header bar
  thêm ô *Phát hành* (bộ SDK đang áp); tab Tổng quan thêm thẻ trạng thái từng bộ.
- Mỗi tab là **bảng SDK** ba nhóm — *cần gắn thêm* (publisher đòi, project chưa có) · *đã gắn* (từng ID:
  hiện tại → phải là, *thay ở đâu*, nút mở file) · *thừa* (project có, publisher không đòi) — và nút
  **Chuyển sang {X}** (`SdkSwitcher`): lập kế hoạch → hiện đủ cài/gỡ/chặn/ID/define → xác nhận → thi hành:
  export SDK sắp gỡ ra `.unitypackage` trong cache theo máy (`~/.local/share/Ezg/SdkCache/{game}/`, spec
  UPM ghi `upm.json`) rồi xoá; cài SDK thiếu từ file người dùng kéo vào / cache / **tự tải**
  (`SdkDownloader`: GitHub Releases API cho Meta · GameAnalytics · MAX, zip Firebase theo version đang
  cài với đúng bộ product; async, progress bar, huỷ được) / spec UPM mặc định (`Client.AddAndRemove`); ghi ID publisher cấp vào `GameConstant` + `MarketingConfig.json`; gắn/gỡ
  scripting define `EZG_SDK_*` theo bộ SDK; lưu `activePublisher` (`ProjectSettings/PublisherConfig.json`).
  Mỗi card SDK thiếu có ô tick **Import**, SDK thừa có ô **Gỡ** (mặc định tick; bị chặn thì khoá) — kế
  hoạch dựng lại theo tick, mục bỏ tick liệt kê ở "Bỏ qua". Bấm về **Ezg** là cài lại đúng bản đã gỡ từ cache.
- **Chặn gỡ SDK mà code game còn gọi thẳng** (`SdkCatalog.CodeReferences`): gỡ là vỡ compile nên switcher
  để lại SDK, báo số file + tên file và define cần bọc (`#if EZG_SDK_FIREBASE` …). Trên template hiện
  tại Firebase / MAX / IAP / PAD / Meta / AppsFlyer đều còn tham chiếu thẳng → muốn switch sạch phải bọc
  code trước (task riêng); GameKit gỡ được ngay.
- SDK **nền tảng** (`SdkInstallSpec.IsPlatform`: Apple GameKit — Game Center/Sign in with Apple; Google Play
  plugins — Play Asset Delivery/In-App Review) mọi profile mặc nhiên giữ, hiện nhóm riêng "Nền tảng — luôn
  giữ", không bao giờ rơi vào "Thừa" hay bị switcher gỡ.
- `IPublisherProfile` = `SdkRequirement[]` (SDK + `SdkIdSlot.Given` publisher cấp / `.Own` game tự tạo,
  `RequiredEvent`). `SdkCatalog` dò 8 SDK, đọc ID (FacebookSettings / GameConstant / GA Settings), kiểm
  event AppsFlyer, mang `SdkInstallSpec` (UPM name/spec, thư mục Assets, trang release, regex tham chiếu
  code, define). Không tham chiếu assembly game.
- Profile **Neptune**: Meta (App ID/Client Token game tạo, Partner ID `3870082899724468`), AppsFlyer (dev
  key `NsZymPemYQycKKY8A826TU`, event `f_custom_playtime`, iOS App Store ID), GameAnalytics. Profile **Ezg**:
  Meta, AppsFlyer, Firebase, MAX, Unity IAP, Google Play plugins, Apple GameKit — ID lấy từ `MarketingConfig.json`.
- Menu `Ezg/Nha phat hanh/Ezg (mac dinh trong nha)`, `…/Neptune (CPI Test)`, `…/SayGame`.
- `SourceIndex` (`Editor/EzgKit/`): cache text mọi `.cs` dưới Assets trong RAM + cache kết quả dẫn xuất theo
  khoá, vô hiệu tự động khi có `.cs` import/xoá/đổi tên. Readiness (RestorePurchases), Social (link
  hardcode, webhook, bot token) và Publisher (tham chiếu SDK, custom event) quét trên đó thay vì mỗi bộ
  tự đọc 1600 file mỗi Reload — đổi tab / về Tổng quan không còn khựng.
- `SdkPostInstallFixer` (`AssetPostprocessor`): vá SDK vừa import để compile được trong project Ezg —
  GameAnalytics `GA_SettingsInspector.cs` dùng type `Game` bị `namespace Game` của Ezg.Features che
  (CS0118) → viết tên đầy đủ `GameAnalyticsSDK.Setup.Game`, idempotent, tự chạy mỗi lần GA được import.

## [0.2.0] - 2026-08-25
### Added
- Tab **Readiness** (`Ezg/Readiness (IAP - Firebase - SDK)`) — bảng Ready / Warning / Error chỉ đọc
  cho PM: SKU IAP từ `ShopPackCatalog` (prefix theo package name, Consumable một lần, Restore
  Purchases đã nối chưa, Play licence key, UGS link), Firebase (json/plist/xml cùng project và đúng
  id, `Resources/FirebaseConfig` + bucket Storage, phase Crashlytics iOS trong script build), SDK
  (debugAds, key/ad-unit MAX, AdMob app id, AppsFlyer dev key + App Store ID, Facebook), Store
  (keystore, version, link store). Nút **Tra App Store** xác minh `GameConstant.IOSAppId` là app nào;
  nút **Copy báo cáo cho PM** xuất text dán Discord/Slack kèm danh sách SKU phải có trên store.
- Mỗi mục Warning/Error có dòng "→ cách sửa" + nút hành động (`ReadinessActions`): chọn/ping asset,
  mở script đúng dòng, mở trang Project Settings, mở tab Marketing/Firebase; và nút **sửa luôn** có
  hỏi xác nhận — tạo `FirebaseConfig.asset` đúng bucket, sửa bucket, tắt `debugAds`, reimport
  `google-services.json`.
- Tab **Social** (`Ezg/Social (Discord - Support - Rating)`) — điền Discord invite / link support /
  email support → `ProjectSettings/SocialConfig.json` → ghi `GameConstant.LinkDiscord / LinkSupport /
  SupportEmail` (chưa có const thì tự chèn sau `LinkFacebook`). Bảng trạng thái mọi link đi vào build:
  fanpage / privacy / terms / store (đối chiếu sheet marketing, id trong link store iOS phải khớp
  `IOSAppId`), rating Android (plugin In-App Review) + iOS + `time_next_rating`, **link còn hardcode
  trong script ngoài GameConstant** (cách link của game khác đi theo template), webhook + **bot token
  Discord trong source** (đỏ). Nút *Kiểm Discord*: invite → tên server, webhook → tên webhook, link
  chết báo đỏ. Nhóm Social cũng xuất hiện trong tab Readiness.

### Fixed
- Ô chọn file của `SetupGui.ManualFilePathField` (tab Firebase: "File key (.json)") giờ **nhận
  kéo-thả** — thả file từ Finder/Explorer hoặc kéo asset từ Project window vào hàng ô nhập; sai đuôi
  thì con trỏ báo Rejected ngay lúc rê. Trước đó ô chỉ có TextField + nút Chọn…, thả file vào là Unity
  trả lại, nhìn như "không nhận". Nút Chọn… mở từ thư mục home thật thay vì chuỗi `~`.
- Tab Firebase hiện lý do file key bị từ chối (vd nằm trong repo, không phải service account key)
  NGAY dưới ô file, không chỉ ở card "Service account" phía dưới.

### Added (Readiness — tiếp)
- `ReadinessChecks` không tham chiếu assembly game — đọc catalog/asset qua `SerializedObject`, hằng
  số qua regex — nên dùng được cho mọi dự án trên code-template.

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
