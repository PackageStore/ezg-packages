# Marketing config — 1 click từ Google Sheet vào project

Tool dùng chung cho MỌI dự án (package `com.ezg.ezgkit`). **Dữ liệu nằm** ở `ProjectSettings/` — merge template giữa các dự án không làm lẫn số của nhau.

| File | Vai trò | Đi theo template? |
|---|---|---|
| `Packages/com.ezg.ezgkit/Editor/Marketing/*.cs` | Code tool | Có (package) |
| `ProjectSettings/MarketingSource.json` | URL sheet + prefix cột của dự án | Không |
| `ProjectSettings/MarketingConfig.json` | Bản chép sheet, do tool ghi lại mỗi lần fetch | Không |

## Dựng cho một dự án mới

1. Mở `Ezg > EzgKit` → mục **Marketing & AppSecrets** → dán link tab chứa bảng thông số (link phải có
   `#gid=...`), điền mã dự án trong sheet (`I001`, `D001`, `R003`… — để trống thì tự dò cột).
2. Sheet phải ở chế độ **Share > Anyone with the link (Viewer)**. Để private thì Google trả về trang
   đăng nhập HTML, tool báo lỗi rõ chứ không parse ra config rỗng.
3. Bấm **Tải sheet** (chỉ ghi `MarketingConfig.json`) → **Xem thay đổi** (bảng cũ → mới theo từng nơi ghi) →
   **Áp dụng** (hỏi lại rồi mới ghi).
4. Card **AppSecrets** cùng trang: key riêng của app mà sheet không có (webhook / bot Discord, endpoint backend,
   form feedback, cờ sandbox). Ô có giá trị thắng sheet; "Lấy từ sheet" điền sẵn AppsFlyer key, App Store ID,
   Privacy / Terms.

Package name / tên game trong sheet mặc định **không** ghi vào PlayerSettings — chúng là gợi ý ở mục
"Thông tin dự án". Bật "Ghi cả bundle id / tên game" ở card sheet nếu muốn sheet quyết.

Mở cửa sổ chỉ đọc; trạng thái mục (Xong / Còn việc …) tính bằng dry-run.

CI/batchmode: `-executeMethod Ezg.Editor.Shared.Marketing.MarketingConfigApplier.ApplyFromCli`
(dùng URL đã lưu, không mở dialog).

## Tool ghi vào đâu

Không hardcode đường dẫn — dò theo tên file trong `Assets/` (xem `MarketingConfigApplier.FindAsset`),
nên dự án đặt file ở nhánh khác vẫn chạy. Dự án chưa tích hợp SDK nào thì sink đó vào mục
"Sink khong co trong du an nay", **không** tính là lỗi.

| Sink | Nội dung |
|---|---|
| `AdsConfig.asset` | MAX sdk key + rewarded/interstitial/banner id 2 nền tảng — thứ duy nhất runtime đọc |
| `AppLovinSettings.asset` | sdk key + Admob app id (post-process của MAX nhét vào manifest/plist) |
| `ProjectSettings/AppLovinInternalSettings.json` | consent flow: privacy policy, ToS, chuỗi ATT |
| `FacebookSettings.asset` | app id + client token + app label |
| `Assets/Plugins/Android/AndroidManifest.xml` | FB app id / client token / ContentProvider / package |
| PlayerSettings | applicationIdentifier (Android + iOS), productName — chỉ khi bật "Ghi cả bundle id / tên game" (batchmode `ApplyFromCli` luôn ghi) |
| `AppSecretsConfig.asset` (template mới) | AppsFlyer dev key, App Store ID, link privacy / ToS (+ các key gõ ở card AppSecrets) |
| `GameConstant.cs` | package name, link store / fanpage; template cũ (không có AppSecretsConfig) thêm AppsFlyer dev key, Apple ID, link policy / ToS |

Ô rỗng trong JSON = "chưa có" → tool **giữ nguyên** giá trị đang dùng, không ghi đè bằng chuỗi rỗng.

## Tool KHÔNG làm được

- Tạo ad unit bên dashboard AppLovin MAX và gán Admob/Unity/FB placement vào từng unit — cần
  Management API của AppLovin, không phải việc Unity Editor. Các số đó vẫn được in ra cuối báo cáo
  để đối chiếu tay.
- Tải `google-services.json` / `GoogleService-Info.plist` từ Firebase console — việc đó là của mục
  **Firebase** (nhóm Nâng cao) trong cùng cửa sổ. Chạy mục "Thông tin dự án" trước, vì Firebase tạo app theo
  package name / bundle id trong PlayerSettings.

## Quy ước sheet

Dò theo **nhãn ở cột đầu**, không theo số thứ tự dòng — marketing thêm/bớt dòng không làm vỡ mapping.
Cột nền tảng nhận diện qua hàng tiêu đề: `<prefix>a` / `<prefix>i` (I001a / I001i), hoặc chữ
"Android" / "iOS". Ô gộp (SDK key, package name, AF key, FB app id) chỉ điền ở cột trái — cột iOS
trống sẽ tự lấy theo cột Android.

Nhãn đang đọc: `Package name`, `Game Name`, `Max SDK Key`, `Max Rewarded`, `Max Inter`, `Max Banner`,
`Admob`, `Admob Reward`, `Admob inter`, `Unity`, `Unity Reward`, `Unity Inter`, `Facebook app ID`,
`FB Rewarded`, `FB Inter`, `Facebook Client Token`, `AF key`, cột `Apple ID`.
Thêm nhãn mới thì sửa `MarketingSheetFetcher.BuildConfig`.
