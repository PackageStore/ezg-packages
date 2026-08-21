# Marketing config — 1 click từ Google Sheet vào project

Tool dùng chung cho MỌI dự án (đi qua branch `code-template`). Code nằm trong `Assets/`, **dữ liệu nằm
ngoài** ở `ProjectSettings/` — merge template giữa các dự án không làm lẫn số của nhau.

| File | Vai trò | Đi theo template? |
|---|---|---|
| `Assets/_Project/Editor/Shared/Marketing/*.cs` | Code tool | Có |
| `ProjectSettings/MarketingSource.json` | URL sheet + prefix cột của dự án | Không |
| `ProjectSettings/MarketingConfig.json` | Bản chép sheet, do tool ghi lại mỗi lần fetch | Không |

## Dựng cho một dự án mới

1. Mở `Ezg > EzgKit` → tab **Marketing** (hoặc vào thẳng
   `Ezg > Marketing > Bang thong so (Marketing Dashboard)`) → dán link tab chứa bảng
   thông số (link phải có `#gid=...`), điền prefix cột (`I001`, `D001`, `R003`… — để trống thì tự dò).
2. Sheet phải ở chế độ **Share > Anyone with the link (Viewer)**. Để private thì Google trả về trang
   đăng nhập HTML, tool báo lỗi rõ chứ không parse ra config rỗng.
3. Bấm **Tải sheet + Setup (1 click)** — từ lần này về sau chỉ cần bấm đúng nút đó.

Tab chỉ có hai nút: **Tải sheet + Setup (1 click)** để chạy, **Làm mới** để chụp lại trạng thái.
Các biến thể cho dev (chỉ đối chiếu, hoặc áp lại từ JSON cũ không tải sheet) nằm ở menu bên dưới.

## Đọc tab này thế nào

Bố cục đi theo thứ tự **việc trước, thông tin sau** — cuộn càng xuống càng ít cấp bách:

| Vùng | Nội dung |
|---|---|
| ngoài scroll | nguồn dữ liệu (link sheet + prefix), hai nút chạy, dải chip tóm tắt |
| dải chip | `N/M ô khớp sheet`, `K việc làm tay`, số lỗi — kèm công tắc **Chỉ hiện ô đang lệch** |
| *Đã ghi vào đâu trong project* | mỗi sink một khối gấp được; **sink còn ô lệch mở sẵn, sink khớp hết gấp lại** |
| *Còn phải làm tay* | việc PM phải tự làm ngoài Unity |
| *Tham khảo* | nhận dạng app, 22 số khai bên dashboard MAX, sink không có trong dự án — **gấp lại hết** |

Công tắc **Chỉ hiện ô đang lệch** là thứ dùng nhiều nhất: lệch 3 ô trong 60 ô thì bật nó lên là ra
đúng 3 ô đó, không phải cuộn đi tìm dấu hiệu.

Ô lệch hiện thành hai dòng — giá trị **đang nằm trong project** rồi tới **giá trị sheet sẽ ghi đè**.
Ô khớp chỉ một dòng, dấu xanh.

Mở cửa sổ chỉ chạy dry-run, không ghi gì.

Toàn bộ luồng dựng dự án mới (marketing → Firebase) nằm ở tab **Tổng quan** — xem
`../EzgKit/README.md`.

Menu tương đương, không cần mở cửa sổ:

| Menu | Việc |
|---|---|
| `Ezg/Marketing/Setup All (1 Click)` | tải sheet + ghi vào project |
| `Ezg/Marketing/Check Config (Dry Run)` | chỉ đối chiếu, không ghi |
| `Ezg/Marketing/Apply Config (khong tai sheet)` | ghi từ JSON hiện có |

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
| PlayerSettings | applicationIdentifier (Android + iOS), productName |
| `GameConstant.cs` | AppsFlyer dev key, Apple ID, package name, link store/policy/ToS/fanpage |

Ô rỗng trong JSON = "chưa có" → tool **giữ nguyên** giá trị đang dùng, không ghi đè bằng chuỗi rỗng.

## Tool KHÔNG làm được

- Tạo ad unit bên dashboard AppLovin MAX và gán Admob/Unity/FB placement vào từng unit — cần
  Management API của AppLovin, không phải việc Unity Editor. Các số đó vẫn được in ra cuối báo cáo
  để đối chiếu tay.
- Tải `google-services.json` / `GoogleService-Info.plist` từ Firebase console — việc đó là của tab
  **Firebase** trong cùng cửa sổ. Chạy tab này xong mới chạy tab đó, vì Firebase đọc package name /
  bundle id mà tab này vừa ghi vào PlayerSettings.

## Quy ước sheet

Dò theo **nhãn ở cột đầu**, không theo số thứ tự dòng — marketing thêm/bớt dòng không làm vỡ mapping.
Cột nền tảng nhận diện qua hàng tiêu đề: `<prefix>a` / `<prefix>i` (I001a / I001i), hoặc chữ
"Android" / "iOS". Ô gộp (SDK key, package name, AF key, FB app id) chỉ điền ở cột trái — cột iOS
trống sẽ tự lấy theo cột Android.

Nhãn đang đọc: `Package name`, `Game Name`, `Max SDK Key`, `Max Rewarded`, `Max Inter`, `Max Banner`,
`Admob`, `Admob Reward`, `Admob inter`, `Unity`, `Unity Reward`, `Unity Inter`, `Facebook app ID`,
`FB Rewarded`, `FB Inter`, `Facebook Client Token`, `AF key`, cột `Apple ID`.
Thêm nhãn mới thì sửa `MarketingSheetFetcher.BuildConfig`.
