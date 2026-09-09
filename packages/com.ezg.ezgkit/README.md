# EZG EzgKit

`Ezg > EzgKit` — một cửa sổ Editor gom mọi bước setup của một dự án mới vào đúng thứ tự phải chạy.

Editor-only, **không phụ thuộc package nào khác**, không third-party lib. Chỉ Unity + BCL.

## Cài đặt

```json
"scopedRegistries": [
  {
    "name": "Easygoing code base",
    "url": "https://upm-registry-worker.developer-a1f.workers.dev",
    "scopes": ["com.ezg"]
  }
],
"dependencies": {
  "com.ezg.ezgkit": "0.1.0"
}
```

## Có gì trong này

| Thư mục | Vai trò |
|---|---|
| `Editor/EzgKit/` | khung cửa sổ + bộ style dùng chung (`EzgKitStyles`, `SetupGui`) + contract `IEzgKitPage` |
| `Editor/Marketing/` | tab Marketing: Google Sheet → PlayerSettings / AdsConfig / AppLovinSettings / FacebookSettings / AndroidManifest / GameConstant |
| `Editor/Firebase/` | tab Firebase: service account `.json` → tạo app Android + iOS → tải `google-services.json` / `GoogleService-Info.plist` |
| `Editor/Iap/` | tab IAP: SKU client đăng ký (`ShopService.GetAllProductId()` qua reflection) ↔ gói thật trên store (API chỉ-đọc của `project-ezg`, có cache) |

Chi tiết bố cục, quy tắc màu và cách thêm một tab mới: [`Editor/EzgKit/README.md`](Editor/EzgKit/README.md).
Chi tiết luồng Marketing: [`Editor/Marketing/README.md`](Editor/Marketing/README.md).

## Menu

| Menu | Việc |
|---|---|
| `Ezg/EzgKit` | mở tab Tổng quan |
| `Ezg/Marketing/Bang thong so (Marketing Dashboard)` | mở tab Marketing |
| `Ezg/Firebase/Cai dat...` | mở tab Firebase |
| `Ezg/IAP (SKU client - store)` | mở tab IAP |
| `Ezg/Marketing/Setup All (1 Click)` | tải sheet + ghi vào project, không mở cửa sổ |
| `Ezg/Marketing/Check Config (Dry Run)` | chỉ đối chiếu, không ghi |
| `Ezg/Marketing/Apply Config (khong tai sheet)` | ghi từ JSON hiện có |
| `Ezg/Firebase/Tao app + tai config (1 Click)` | tạo app + tải config, không mở cửa sổ |
| `Ezg/Firebase/Kiem tra (Dry Run)` | chỉ GET, không tạo gì |

## Yêu cầu

- Unity **2022.3** trở lên — tool dùng overload `PlayerSettings.GetApplicationIdentifier(NamedBuildTarget)`.
- **Không có peer requirement.** Không cần Odin, DOTween, UniTask hay Newtonsoft.
- Tab Firebase cần một file **service account `.json`** có quyền trên Firebase project. Đường dẫn file
  key nằm ở `EditorPrefs` theo máy, **không** đi vào repo; `FirebaseServiceAccount.TryLoad` từ chối
  thẳng file key nằm trong `Assets/`.

## Dữ liệu nằm ở đâu

Cấu hình của từng dự án nằm **ngoài `Assets/`**, trong `ProjectSettings/`:

- `ProjectSettings/marketing_config.json` — source of truth cho mọi số marketing/ads.
- `ProjectSettings/FirebaseSource.json` — project id / app name đang khai (KHÔNG chứa key).
- `ProjectSettings/SocialConfig.json` — Discord invite / support link / email.
- `ProjectSettings/AppLovinInternalSettings.json` — consent flow của MAX (do AppLovin quản).

Package chỉ chứa code; nó không mang theo dữ liệu của dự án nào.

Tab **IAP** không có file cấu hình nào trong `ProjectSettings/`: nguồn chuẩn của nó là một HÀM trong
code game (`ShopService.GetAllProductId()`, gọi qua reflection), không phải một đường dẫn ai đó phải
điền. Bản lưu của lượt xác minh store nằm ở `Library/EzgKit/IapStoreCache.json` — theo từng máy, không
vào git, xoá được ngay trong tab.

Phần **xác minh store** thì đường gọi là một hằng trong code
(`https://project.easygoing.vn/api/v1/iap/products` — giống nhau ở mọi dự án, và dự án nào là do chính
API key quyết định), nên chỉ còn API key, mà key là secret.

**Secret thì KHÔNG nằm trong `ProjectSettings/`** (thư mục đó được track git). Hai secret của kit
sống ở `EditorPrefs` theo từng project trên máy, đều có fallback biến môi trường cho CI:

| Secret | EditorPrefs | Biến môi trường |
|---|---|---|
| Firebase service account key (đường dẫn file) | `Ezg.Firebase.KeyPath:<dataPath>` | `GOOGLE_APPLICATION_CREDENTIALS` |
| API key xác minh IAP (`project-ezg`) | `Ezg.Iap.ApiKey:<dataPath>` | `EZG_IAP_API_KEY` |

Riêng **link server xác minh IAP** đổi được mà không phải sửa code: đặt biến môi trường
`EZG_IAP_API_URL` (ví dụ `http://localhost:3001/api/v1/iap/products` khi chạy `project-ezg` tại máy)
trước khi mở Unity — tab hiện rõ khi link đến từ env hoặc đang trỏ về localhost.

## Coupling đã biết (không phải lỗi)

Tab Marketing ghi vào các "sink" của EZG code-template. Nó **dò theo tên file** bằng `AssetDatabase`
chứ không hardcode đường dẫn, và **bỏ qua êm** nếu dự án không có sink đó:

| Sink | Cách tìm |
|---|---|
| `AdsConfig.asset` | tìm asset tên `AdsConfig` bất kỳ đâu dưới `Assets/` |
| `AppLovinSettings.asset` | tìm asset tên `AppLovinSettings` |
| `FacebookSettings.asset` | tìm asset tên `FacebookSettings` |
| `Assets/Plugins/Android/AndroidManifest.xml` | đường dẫn cố định (manifest chép tay) |
| `GameConstant.cs` | tìm file tên `GameConstant`, rồi thay giá trị bằng regex trên tên const: `AppsFlyerId`, `IOSAppId`, `PackNameAndroid*`, `LinkStore*`, `LinkFacebook`, `LinkPrivacyPolicy` |

Nghĩa là: dự án nào **không** theo quy ước đặt tên của code-template thì tab Marketing sẽ báo
"khong tim thay" ở sink đó và bỏ qua — phần còn lại vẫn chạy.

> Package này **khác** `com.ezg.firebase`. `com.ezg.firebase` là runtime SDK (auth / Firestore /
> Remote Config); tab Firebase ở đây là tool Editor để **tạo app trên Firebase console** lúc setup dự án.

## Thêm một tab setup mới

Implement `IEzgKitPage`, rồi thêm vào `EzgKitWindow.BuildPages()` + enum `EzgKitWindow.Tab`.
Ràng buộc quan trọng (`Status`/`Headline` phải rẻ, không chạy việc nặng lúc vẽ) ghi đầy đủ ở
[`Editor/EzgKit/README.md`](Editor/EzgKit/README.md).
