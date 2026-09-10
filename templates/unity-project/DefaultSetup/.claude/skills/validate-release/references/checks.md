# Danh mục check

Mỗi `id` là một **dòng checklist** — nó luôn xuất hiện trong output, chỉ khác trạng thái
(`✓` đạt · `✗` hỏng · `!` cảnh báo · `·` thông tin · `–` không áp dụng).

Dùng id trong `ignore` / `severity` của `<project>/.claude/validate-release.json`. Cột **Hỏng khi**
mô tả điều kiện ra `✗`/`!`; ngoài điều kiện đó thì mục ra `✓`, trừ khi cột **Bỏ qua khi** thoả.

Nguồn: `PS` = `ProjectSettings/ProjectSettings.asset` · `SRC` = source `.cs` dưới sourceRoot (bỏ
`Editor/`) · `CFG` = settings asset của SDK.

## identity — định danh & ký build

| id | Mức | Hỏng khi | Bỏ qua khi | Nguồn |
|---|---|---|---|---|
| `identity.bundle-id` | ✗ | Thiếu entry, hoặc còn placeholder (`com.company.game`…). Một dòng cho mỗi platform | | PS |
| `identity.product-name` | ! | `productName` còn là tên template | | PS |
| `identity.keystore` | ✗ / ! | File keystore không tồn tại (✗) · chưa bật custom keystore (!) | Android ngoài scope | PS |
| `identity.path-foreign` | ! | Đường dẫn tuyệt đối máy khác (`D:\...`) trong settings | | PS, CFG |
| `identity.ios-signing` | ! | Không có Team ID và cũng không bật automatic signing | iOS ngoài scope | PS |
| `identity.version-code` | · | Version code Android ≠ build number iOS | | PS |

Keystore hỗ trợ prefix `{inproject}:` của Unity.

## firebase

| id | Mức | Hỏng khi | Bỏ qua khi |
|---|---|---|---|
| `firebase.android-config` | ✗ | Không có `google-services.json`, hoặc file không parse được | Android ngoài scope |
| `firebase.android-package-match` | ✗ | `package_name` ≠ bundle id Android → Firebase không init | như trên |
| `firebase.ios-config` | ✗ | Không có `GoogleService-Info.plist` | iOS ngoài scope |
| `firebase.ios-bundle-match` | ✗ | `BUNDLE_ID` trong plist ≠ bundle id iOS | như trên |
| `firebase.config` | – | Không bao giờ hỏng — dòng duy nhất thay cho cả nhóm khi source không có `using Firebase` | |

## ads — mediation

| id | Mức | Hỏng khi | Bỏ qua khi |
|---|---|---|---|
| `ads.applovin-key` | ✗ | `sdkKey` rỗng | Không có `AppLovinSettings` |
| `ads.admob-appid` | ✗ | Trống, hoặc đang là App ID test của Google. Một dòng mỗi platform | Không có `GoogleMobileAdsSettings` |
| `ads.admob-appid-match` | ✗ | App ID lệch giữa `AppLovinSettings` và `GoogleMobileAdsSettings` | Chỉ một SDK khai App ID |
| `ads.consent` | ✗ / ! | Bật consent nhưng thiếu Privacy Policy URL (✗) · consent flow đang tắt (!) | Không có `AppLovinInternalSettings.json` |
| `ads.ad-unit` | ✗ | Banner/Interstitial/Rewarded rỗng ở **mạng đang thật sự chạy** | Không dò được mediation |
| `ads.active-network` | · | Luôn là info — in ra mediation nào đang được `new` | |

AppLovin ghi đè AdMob App ID lúc build, nên `ads.admob-appid-match` là ✗ chứ không phải cảnh báo.

Mạng đang chạy dò bằng `ads.activeImplPattern` (mặc định bắt `advertising = new XxxAdvertising()`),
rồi cắt đúng nested class tương ứng trong file ad-unit bằng cách đếm ngoặc.

## tracking — attribution

| id | Mức | Hỏng khi | Bỏ qua khi |
|---|---|---|---|
| `tracking.appsflyer-key` | ✗ | Hằng `AppsFlyerId` / `IOSAppId` rỗng hoặc placeholder | Không thấy hằng đó |
| `tracking.facebook-appid` | ✗ | App ID lệch giữa `FacebookSettings` và `AndroidManifest` | Không có Facebook SDK |
| `tracking.facebook-provider` | ✗ | `FacebookContentProvider<id>` sai → crash lúc khởi động | như trên |
| `tracking.facebook-token` | ✗ | Client Token lệch giữa settings và manifest | như trên |

Ba dòng Facebook đọc list YAML kiểu Unity (phần tử thụt **ngang bằng** key, không sâu hơn) — sai chỗ
này thì cả ba ra `–` thay vì `✓`, và đó là dấu hiệu parser hỏng chứ không phải project sạch.

## flags — cờ debug còn bật

| id | Mức | Hỏng khi |
|---|---|---|
| `flags.debug-const` | ✗ | Có `const bool ...Sandbox/Debug/Cheat/TestMode/Mock... = true`. Một dòng mỗi cờ |

Pattern ở `debugResidue.flagPattern`. Chỉ quét code ngoài `Editor/`.

## remote — Remote Config

| id | Mức | Hỏng khi | Bỏ qua khi |
|---|---|---|---|
| `remote.default-file` | ! | Có file mà không key nào được code đọc (export của project khác), hoặc JSON hỏng | Không có file default |
| `remote.missing-default` | ! | Key được code đọc nhưng không có trong file default | như trên |
| `remote.unused-default` | · / ! | Key mồ côi, gộp theo file; >60% mồ côi thì lên `!` | như trên |

Key dùng trong code chỉ thu ở file có nhắc `RemoteConfig`/`FirebaseRemoteManager`, bắt cả literal
(`TryGetValue("x")`) lẫn gián tiếp (`readonly string xKey = "x"`). Giới hạn đó là cố ý: quét toàn bộ
source sẽ nuốt nhầm key của `PlayerPrefs`.

## build — build settings

| id | Mức | Hỏng khi | Bỏ qua khi |
|---|---|---|---|
| `build.scene-exists` | ✗ | Scene trong build script không có trên đĩa | Không tìm thấy build script |
| `build.development-option` | ✗ | Build script bật Development Build (đã bỏ comment trước khi so) | như trên |
| `build.editor-scene` | ! | Scene tên kiểu `LevelEditor`/`Demo`/`_old` nằm trong build | như trên |
| `build.il2cpp` | ✗ | Android còn Mono → không xuất được ARM64 | Rule tắt trong config |
| `build.arm64` | ✗ | `AndroidTargetArchitectures` thiếu bit ARM64 | Rule tắt trong config |
| `build.target-sdk` | ✗ / · | Dưới `android.minTargetSdk` (✗) · để Automatic (·) | Android ngoài scope |
| `build.link-xml` | ✗ | Bật stripping nhưng không có `link.xml` → reflection bị strip | Không bật stripping |
| `build.unity-splash` | ! | Splash Unity còn bật | |
| `build.scene-source` | · | Luôn là info — in nguồn scene list, và báo nếu lệch Editor Build Settings | |
| `build.stripping-profile` | · | Luôn là info — in mức stripping hiện tại | |

`build.scene-source` cố tình chỉ ở mức `·`: **build script mới là nguồn sự thật khi build CI**, lệch
với `EditorBuildSettings.asset` thường là chủ đích. Đừng "sửa" Build Settings vì dòng này.

## debug — dấu vết dev còn sót

| id | Mức | Hỏng khi |
|---|---|---|
| `debug.console-in-scene` | ! | `IngameDebugConsole`/`StatsOverlay`… nằm trong scene được ship |

Marker lồng nhau được gộp: `DebugConsole` là con của `IngameDebugConsole`, chỉ báo cái dài hơn.

## secrets

| id | Mức | Hỏng khi | Bỏ qua khi |
|---|---|---|---|
| `secrets.webhook` | ! | Webhook Discord/Slack hardcode trong `.cs` | |
| `secrets.credential-tracked` | ! | keystore / `googlekey.json` / `*Token.txt` / `.p12` / `.p8` đang được git track | Không đọc được git index |

Cả hai là `!` chứ không `✗`: nhiều team cố ý commit keystore vào repo nội bộ. Xác nhận rồi đưa vào
`ignore` thay vì để nó kêu mỗi lần.

## l10n — localization

| id | Mức | Hỏng khi | Bỏ qua khi |
|---|---|---|---|
| `l10n.files` | ! | Một ngôn ngữ thiếu hẳn file CSV mà ngôn ngữ gốc có | Không có thư mục dữ liệu |
| `l10n.keys` | ! | Thiếu key so với `localization.referenceLocale` (mặc định `en`) | như trên |

Thư mục dữ liệu chọn theo tiêu chí "có sẵn locale gốc bên trong" — nếu lấy bừa kết quả glob đầu tiên
sẽ trúng thư mục CODE tên na ná (`Core/Localize/Localization`) và cả nhóm im lặng ra `–`.

## hygiene — rác & sót từ project cũ

| id | Mức | Hỏng khi |
|---|---|---|
| `hygiene.stale-package-ref` | ! | Chuỗi dạng package id không khớp bundle id project và không thuộc prefix SDK đã biết |
| `hygiene.store-link` | ! | Link Google Play / App Store trỏ sang app khác |
| `hygiene.backup-clutter` | · | Nhiều file `*.backup*` trong `Assets/` (vẫn bị import) |

`hygiene.stale-package-ref` bắt được nhiều bug thật nhất khi project sinh từ template hoặc clone từ
game trước: rate popup, nút share, deep link trỏ về app cũ mà không ai để ý.
