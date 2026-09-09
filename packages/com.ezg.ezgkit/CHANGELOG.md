# Changelog

## [0.4.0] - 2026-09-09
### Added
- Tab **IAP** biết hỏi **store thật**: nút *Kiểm tra trên store* gọi API chỉ-đọc của server nội bộ
  `project-ezg` (`GET https://project.easygoing.vn/api/v1/iap/products`, header
  `Authorization: Bearer`) và đối chiếu danh mục App Store Connect + Google Play với SKU client đăng
  ký. Trước bản này tab chỉ so được ý ĐỊNH bán (catalog ↔ bảng giá GD); "gói đã tạo trên store chưa"
  vẫn phải mở console đi soi tay.
  Từng SKU nhận thêm một chip: `trên store` · `store: chưa xong` · `store: đang tắt` ·
  `CHƯA có trên store`, kèm trạng thái thô của store (`MISSING_METADATA`, `APPROVED`, `ACTIVE`…) ở
  dòng ghi chú.
- Khối **"Có trên store mà client KHÔNG đăng ký"** — chiều NGƯỢC của lượt đối chiếu (gói template đã
  bỏ, bảng đang tắt trong catalog, id gõ sai, gói cũ của bản trước). Hiện **mọi lúc** khi đã xác
  minh, kể cả lúc sạch ("cả N dòng đều khớp"): ẩn đi thì không ai biết chiều này có được kiểm hay
  không, mà "không thấy gì" đọc y hệt "tool không kiểm".
- `IapStoreVerifier` — HTTP bất đồng bộ qua `EditorApplication.update` (không chặn Editor, không
  progress bar: lượt này chỉ đọc, không ghi file nào sau đó), parse bằng `JsonUtility`, và **các
  guardrail của API** được cài đúng: (1) nền tảng `ok: false` bị **bỏ qua** khi đối chiếu thay vì
  báo "chưa tạo" — Apple lỗi 5 phút mà kết luận sai là dev đi tạo lại và store sinh ra một loạt gói
  trùng không xoá được; (2) `stale: true` báo dữ liệu cũ kèm mốc đọc; (3) `keyExpiresAt` còn ≤ 14
  ngày thì cảnh báo. Không đọc logic từ `storeStatus` (chuỗi thô của store, đổi lúc nào không biết)
  — chỉ dùng `status`.
- Bắt ca **"dán nhầm key của dự án khác"** bằng **độ khớp danh mục**, KHÔNG bằng một ô "mã dự án" gõ
  tay: API key đã trỏ về đúng một dự án và response tự khai `project`, nên store trả về gói mà không
  trùng một id nào của client là gần như chắc chắn danh mục của dự án khác. Lượt kiểm này chạy mà
  không cần ai điền gì; ô gõ tay thì mỗi lần gõ sai là một báo động giả cho một key hoàn toàn đúng,
  và giá trị đoán mặc định theo tên thư mục gốc còn sai ngay khi agent chạy trong `git worktree`
  (`<repo>-agent-<base>`). Store rỗng không rơi vào lượt này (dự án mới chưa tạo gói nào là ca thật).
- `IapVerifyConfig` — **không có ô nhập nào ngoài API key.** Đường gọi là một hằng trong code
  (`https://project.easygoing.vn/api/v1/iap/products`): nó giống nhau ở mọi dự án và không đổi theo
  dự án nào, nên một ô cho nó chỉ thêm chỗ gõ sai rồi tab báo "không nối được server" cho một hạ
  tầng hoàn toàn bình thường. Trỏ sang server khác (dev tại máy `localhost:3001`, staging) thì đặt
  biến môi trường `EZG_IAP_API_URL` trước khi mở Unity — tab hiện rõ khi link đến từ env hoặc trỏ
  về localhost. **API key nhớ ở `EditorPrefs` theo từng project trên máy**, fallback biến môi trường
  `EZG_IAP_API_KEY` cho CI; key KHÔNG đi vào repo. Tab bày `ApiKeyHint` (14 ký tự đầu) để nhìn là
  biết đang cầm key nào mà không lộ cả chuỗi — câu hỏi đầu tiên khi gặp 401.
  Không còn `ProjectSettings/IapVerifySource.json`: khi mọi thứ cấu hình được đã biến mất thì file
  cấu hình cũng không còn lý do tồn tại.

### Changed
- **Bố cục tab IAP làm lại.** Bản trước là ba mục đánh số (1. file · 2. SKU · 3. dòng thừa) với
  MỘT card phẳng 24 dòng `KeyValue`, mỗi dòng nhồi `product id · $giá · loại` vào một chuỗi và mọi
  vấn đề nối bằng `·` — bảng đang TẮT nằm lẫn với bảng đang bán, và phải đọc hết 24 dòng mới thấy 2
  dòng thật sự có việc. Nay:
  - Khối **"Việc phải làm (N)"** đứng đầu, đỏ trước vàng sau, mỗi dòng một câu hành động — gom
    theo vấn đề (thiếu N dòng sheet, N dòng chưa có giá, N SKU chưa lên store…) kèm tên gói, thay
    vì bắt người đọc tự tổng hợp từ các dòng lẻ.
  - **Mỗi bảng pack là một khối gấp được** (`CardFoldout`) với icon trạng thái + chữ đếm
    `Consumable · N SKU · N cần xử lý`; bảng còn việc mở sẵn, bảng xanh và bảng tắt gấp lại. Loại
    SKU (Consumable/Non-Consumable) là thuộc tính của BẢNG nên chuyển lên hàng tiêu đề, không lặp
    lại ở 24 dòng nữa.
  - **Một SKU = ba tầng**: hàng tên + giá + hai chip (bảng giá / store) → hàng product id
    **copy được** (dev phải dán sang console) → mỗi vấn đề một dòng riêng.
  - Bỏ **banner lặp lại `Headline`** (cửa sổ đã vẽ chip trạng thái đó ở đầu trang); banner giờ mang
    thêm câu "nên làm gì tiếp".
  - Nhãn nút ghi nói rõ **đích và việc**: *Load lại 18 SKU vào Monitization.xlsx (thêm 4 dòng)* —
    bản trước chỉ ghi "Load lại N SKU đang dùng" trong khi ô chọn file nằm phía dưới nút.
  - Khối cấu hình (bảng giá `.xlsx`, server xác minh) chuyển xuống **cuối trang** theo đúng quy tắc
    "việc cần làm nằm trên, thông tin tham khảo nằm dưới".
- `IapSetupPage.Headline`/`Status` **dựng sẵn trong `Compare()`** thay vì nối chuỗi trong getter:
  cửa sổ đọc hai getter này mỗi lượt OnGUI của MỌI tab (icon cột nav, chip đầu trang, thẻ bước).
  Headline giờ nói cả ba nguồn: `18 SKU đang dùng · sheet còn 22 dòng chưa có giá · store: 10/18 SKU sẵn sàng.`
- Bước làm tay *"Tạo SKU trên console"* nói thêm: tạo xong bấm *Kiểm tra trên store*, server đệm 60
  giây nên đợi tối đa một phút. Thêm bước *Sandbox test trên máy thật* (store báo `ready` vẫn có thể
  fail vì chưa link tài khoản thanh toán / chưa ký thoả thuận).

## [0.3.3] - 2026-08-27
### Added
- Tab **IAP** (`Ezg/IAP (SKU - bang gia store)`, bước 4 trong Setup Ezg): đọc SKU client sẽ đăng ký với store
  từ `ShopPackCatalog` (đúng nguồn `ShopService.IapProductIds`) và đối chiếu với **bảng giá GD** — file `.xlsx`,
  sheet "List gói bán" (platform · product_id · reference_name · product_type · default_price · … ·
  store_product_id). Từng SKU báo thiếu dòng android/ios, product_type lệch cờ `isNonConsumable`,
  `store_product_id` android trống/lệch, ios lại có `store_product_id`, `default_price` chưa điền; liệt kê dòng
  trong sheet mà catalog không có (gói đã bỏ / bảng tắt / CSV chưa import).
- Nút **Load lại N SKU đang dùng** (một nút, chạy lại bao nhiêu lần cũng được): mỗi SKU đang bật có đúng hai dòng
  `android` + `ios` — chưa có thì thêm cuối vùng dữ liệu (reference_name/title/description từ pack_name,
  `net_pack_diamond` → "Net Pack Diamond", bỏ hậu tố `_iap`; product_type theo catalog; USD / base_plan_id 1 /
  en-US / family_sharable FALSE; android `store_product_id = product_id`, **iOS để trống**); `default_price` của
  dòng mới ghi chữ nhắc **"CẦN ĐIỀN GIÁ"**; dòng đã có mà chưa có giá chỉ điền chữ nhắc vào ô đó; **dòng GD đã điền
  giá không bị đụng**; bảng tắt trong catalog (gói template tạm bỏ) không đưa vào; dòng thừa trong sheet để nguyên
  và liệt kê. Dự án **chưa có file** → nút tự tạo `Assets/_Project/IapPrice/Monitization.xlsx` chỉ với hàng tiêu đề 16 cột rồi load.
- `IapPriceSheet`: đọc/ghi `.xlsx` bằng zip + XML OOXML thuần (System.IO.Compression + XDocument), không thư viện
  Excel — chỉ thay đúng entry `xl/worksheets/sheetN.xml` của sheet đích, 18 sheet còn lại + drawing/ảnh của GD
  giữ nguyên byte (lib load-save kiểu openpyxl/EPPlus làm rơi drawing). Mỗi lần ghi có bản sao ở
  `Library/EzgKit/IapPriceBackup/` và tự `ImportAsset` khi file nằm trong Assets. Đường dẫn file nhớ ở
  `ProjectSettings/IapPriceSource.json`; để trống thì tự dò `.xlsx` trong Assets (ưu tiên thư mục `IapPrice/`).
- `IapSkuCatalog`: đọc catalog qua SerializedObject (không tham chiếu assembly game) — bảng label/isEnabled/
  isNonConsumable, gói packName/googleProductId/appleProductId/iapCost, chỉ lấy `purchaseType = IAP`.

### Changed
- Tab enum: `Iap = 4`, Readiness/Ezg/Neptune/SayGame dời xuống một số (chỉ ảnh hưởng `Open(Tab)` nội bộ).

## [0.3.2] - 2026-08-27
### Changed
- Tab **Nhà phát hành** xếp lại theo việc thật: ngay dưới nút *Chuyển sang {X}* là khối **ID phải điền** —
  mỗi ID một ô nhập (icon trạng thái · SDK · nhãn · giá trị hiện tại; ID publisher cấp được điền sẵn giá trị
  phải có), một nút **Điền N ID vào project** ghi tất cả vào đúng chỗ trong một lần (xác nhận kèm danh sách
  đổi). Cuối mỗi hàng có nút mở thẳng chỗ setup (*Chọn FacebookSettings* / *Mở GameConstant.cs* đúng dòng / *Chọn GA
  Settings*) để soi giá trị đã ghi. ID ngoài Unity (Partner ID) hiện giá trị + link, không có ô. SDK chưa gắn thì ô khoá kèm ghi chú.
- Đoạn *Về {publisher}* và *Kế hoạch "Chuyển sang"* chuyển xuống **cuối trang** (vẫn gấp/mở được) — phần
  kiểm/điền ID không còn bị đẩy xuống dưới màn hình.
- Khối **Cần sửa (N)** đứng đầu trang: gom mọi mục đỏ/vàng của bảng SDK — SDK chưa gắn (→ Chuyển sang),
  ID sai/trống (→ ô ở khối ID phải điền), event thiếu/thiếu tham số (→ cách viết cụ thể) — đỏ trước vàng,
  mỗi dòng một câu cách sửa, **chỉ chữ không nút**. Không có gì thì không vẽ. Pill card SDK nói đúng thứ
  hỏng: *thiếu event* thay vì *ID sai* khi ID đã khớp.

### Added
- `PublisherIdWriter`: bộ ghi ID đối xứng với phần đọc của `SdkCatalog` — Meta `appId`/`clientToken` →
  `FacebookSettings.asset` + `AndroidManifest.xml` (ApplicationId / ClientToken / ContentProvider authorities)
  + `MarketingConfig.json facebook.*`; AppsFlyer `devKey`/`iosAppId` → `GameConstant` + `MarketingConfig.json`;
  GameAnalytics `gameKey`/`secretKey` → `Assets/Resources/GameAnalytics/Settings.asset` entry platform Android
  (chưa có Settings.asset / platform thì tạo qua `AddPlatform` của SDK bằng reflection).
- `PublisherSdkApplier` (nút *Chuyển sang*) dùng chung writer: ID publisher cấp sẵn của Meta (từ sheet
  marketing, profile Ezg) giờ cũng được ghi, không chỉ const trong `GameConstant`. Chỉ ghi trên SDK đã gắn.

### Fixed
- Đọc GA key lấy entry của platform **Android** trong `Settings.asset` (trước lấy entry đầu — lệch khi
  iOS đứng trước).

## [0.3.1] - 2026-08-26
### Fixed
- Cửa sổ EzgKit **không còn tự mở lúc khởi động project**. Unity lưu cửa sổ đang mở vào layout và khôi
  phục y nguyên khi mở lại project, kéo theo `ReloadAll()` đọc PlayerSettings/ProjectSettings ngay giữa lúc
  Editor boot. `EzgKitWindow.OnEnable` giờ nhận ra bản khôi phục từ layout (chưa ai bấm menu `Ezg/…` trong
  phiên Editor này — cờ `SessionState` sống qua domain reload, mất khi tắt Editor) và tự đóng, không
  Reload. Mở qua menu trong phiên rồi recompile / vào-ra Play vẫn giữ cửa sổ như cũ.

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
