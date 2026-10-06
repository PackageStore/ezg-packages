# EZG EzgKit

`Ezg > EzgKit` — cửa sổ setup một dự án Unity vừa sinh từ template: mỗi việc phải điền theo dự án (bundle id,
id quảng cáo, key AppsFlyer, link pháp lý, localize, art style…) là một mục ở cột trái, có trạng thái, form bên
phải, và luôn hiện **bảng thay đổi** trước khi ghi.

Editor-only, không phụ thuộc package nào khác (Unity + BCL). Mọi SDK bên thứ ba (MAX, Facebook, com.ezg.ads,
com.ezg.localize, com.ezg.firebase) và type của game (`AppSecretsConfig`, `ShopService`) được dò bằng reflection —
dự án chưa có thứ đó thì mục tương ứng nói "không áp dụng", không vỡ compile.

## Cài đặt

```json
"scopedRegistries": [
  { "name": "Easygoing code base", "url": "https://upm-registry-worker.developer-a1f.workers.dev", "scopes": ["com.ezg"] }
],
"dependencies": { "com.ezg.ezgkit": "1.0.0" }
```

## Cửa sổ

```
┌ EZGKIT · PROJECT SETUP ─────┬─────────────────────────────────────────────────────────┐
│ <tên dự án>                 │ Thông tin dự án                              [Còn việc] │
│ Android … · iOS … · v1.0    │ Tên dự án, công ty, tên hiển thị và bundle id …          │
│ ▓▓▓▓░░░░  2/6 mục Setup xong├─────────────────────────────────────────────────────────┤
│ SETUP                       │ ┌ Còn việc ──────────────────────────────────────────┐  │
│ 1 Thông tin dự án  Còn việc●│ │ ● Bundle id Android: đang là id mẫu …              │  │
│ 2 Marketing & …    Chưa làm○│ └────────────────────────────────────────────────────┘  │
│ 3 Ads & Privacy    Chưa làm○│ ┌ Bundle id ─────────────────────────────────────────┐  │
│ 4 Gói bán (IAP)    Còn việc●│ │ Android   [com.studio.game          ]  ✓            │  │
│ 5 ArtStyle         Để sau  ●│ │ iOS       [☑ dùng chung id Android  ]               │  │
│ 6 Localization     Xong    ●│ └────────────────────────────────────────────────────┘  │
│ ▾ NÂNG CAO                  │ ┌ Xem thay đổi — 3 ô sẽ ghi ─────────────────────────┐  │
│   Firebase · Nhà phát hành  │ │ PlayerSettings  applicationIdentifier  cũ → mới    │  │
│   · Social                  │ └────────────────────────────────────────────────────┘  │
│ [Setup tất cả →]            ├─────────────────────────────────────────────────────────┤
│ [Làm mới trạng thái]        │ [Xem thay đổi] [Áp dụng]        Để sau  Không áp dụng   │
└─────────────────────────────┴─────────────────────────────────────────────────────────┘
```

- **Mở cửa sổ chỉ đọc.** Ghi chỉ xảy ra khi bấm **Áp dụng**: kit chạy dry-run, hỏi lại kèm danh sách ô sẽ đổi, rồi
  mới ghi. Asset ghi qua `SerializedObject` (có Undo); file text (GameConstant.cs, AndroidManifest.xml,
  ArtStyle.md, project-profile.json) được backup vào `Library/EzgKit/Backups/<giờ>/` trước khi ghi.
- **Trạng thái mỗi mục** do detector đọc thẳng từ project mỗi lần làm mới: `Xong`, `Còn việc`, `Chưa làm`,
  `Có lỗi`. Hai trạng thái còn lại là quyết định của người: **Để sau** và **Không áp dụng** — lưu lại và thắng
  detector (để sau mà project thực tế đã xong thì vẫn hiện Xong).
- **Setup tất cả** đi lần lượt qua các mục Setup còn `Chưa làm / Còn việc / Có lỗi`; mỗi trang có nút **Tiếp →**.
- Secret (key, token, webhook) hiện dạng ô mật khẩu có nút Hiện, bảng thay đổi che còn 4 ký tự cuối.

## Các mục

| Id | Mục | Ghi vào |
|---|---|---|
| `overview` | Tổng quan — thẻ trạng thái mọi mục + danh sách sẵn sàng phát hành (IAP / Firebase / SDK / Store / Social), nút copy báo cáo cho PM, tra App Store ID | — (chỉ đọc) |
| `project` | Thông tin dự án — tên dự án (agent system), company, product name, bundle id Android / iOS | PlayerSettings, `.claude/project-profile.json › projectName` |
| `marketing` | Marketing & AppSecrets — Google Sheet của PM → `ProjectSettings/MarketingConfig.json` → mọi nơi project đọc; key riêng của app | AdsConfig, AppLovinSettings, consent MAX, FacebookSettings, AndroidManifest, GameConstant (link store / package name), **`AppSecretsConfig.asset`** |
| `ads` | Ads & Privacy — debug ads, format, MAX sdk key + ad unit, define `MEDIATION_MAX`, AdMob app id, consent flow / ATT, đối tượng người chơi (COPPA), Facebook app id | AdsConfig, scripting define (Android + iOS), AppLovinSettings, AppLovinInternalSettings, FacebookSettings |
| `iap` | Gói bán (IAP) — **chỉ đọc**: SKU client đăng ký (`ShopService.GetAllProductId()`), loại, giá tham chiếu, trạng thái trên Google Play / App Store Connect (API project-ezg, có cache). Tạo / sửa gói bằng MCP gói bán | — |
| `artstyle` | ArtStyle — `.claude/docs/ArtStyle.md`: Status, mục nào còn khung trống, khối `art-style-boards`, dựng board (`art-style-board.py`), **Nhờ Claude soạn** | ArtStyle.md (chỉ khối boards) |
| `localize` | Localization — link file localize, service account, thư mục CSV dùng chung, tab cần tải, nút Tải localize | `LocalizeDownloader.asset`, `.claude/project-profile.json › localize` |
| `firebase` | *(Nâng cao)* Firebase — service account → tạo app Android + iOS → tải config, SHA-1, `FirebaseConfig.asset` đúng bucket | Assets/google-services.json, GoogleService-Info.plist, FirebaseConfig.asset |
| `publisher` | *(Nâng cao)* Nhà phát hành — bộ SDK theo publisher (Ezg / Neptune / SayGame), điền ID, chuyển bộ SDK | asset SDK, define `EZG_SDK_*`, `PublisherConfig.json` |
| `social` | *(Nâng cao)* Social — Discord invite / trang hỗ trợ / email, quét link hardcode + token Discord bị lộ | `SocialConfig.json`, GameConstant |

### AppSecretsConfig vs GameConstant

Template mới để AppsFlyer dev key, App Store ID, cờ sandbox, webhook / bot Discord, endpoint backend và link
privacy / terms trong ScriptableObject `AppSecretsConfig` (`Resources/AppSecretsConfig`), không còn là `const`
trong `GameConstant.cs`. Bản 0.x vẫn ghi các const đó → ghi vào hư không và báo lỗi sai. Từ 1.0 mọi đường đọc /
ghi các số này đi qua `AppSecretsConfig` khi dự án có type đó (tạo asset ở `Assets/_Project/Resources/` nếu
chưa có); dự án template cũ (không có type) vẫn ghi vào GameConstant như trước.

## File dữ liệu

| File | Nội dung | Git |
|---|---|---|
| `ProjectSettings/EzgKitSetup.json` | `{schema, pages:{<id>:{marker,at,note}}, answers:{<id>:{…}}, requests:[…]}` — quyết định Để sau / Không áp dụng / Xong, câu trả lời không bí mật, yêu cầu nhờ Claude | track |
| `ProjectSettings/MarketingSource.json`, `MarketingConfig.json` | link sheet + bản chép sheet marketing | track |
| `ProjectSettings/FirebaseSource.json`, `SocialConfig.json`, `PublisherConfig.json` | khai báo của từng mục nâng cao | track |
| EditorPrefs (theo máy) | đường dẫn key Firebase, API key IAP | không |
| `Library/EzgKit/` | cache store IAP, backup file trước khi ghi | không |

Secret không bao giờ nằm trong `EzgKitSetup.json`.

## API cho automation (Claude `/setup-project`)

`Ezg.EzgKit.EzgKitApi` — public static, trả về chuỗi JSON, không mở dialog. Gọi qua Unity MCP
(`unity_execute_code`), ví dụ `return Ezg.EzgKit.EzgKitApi.GetStatusJson();`.

| Hàm | Trả về / việc |
|---|---|
| `GetStatusJson()` | `{api, project, productName, androidId, iosId, version, progress:{done,total}, pages:[{id,title,group,canApply,status,marker,summary,todos:[{level,text,fix}]}], requests:[…]}` |
| `GetPageValuesJson(pageId)` | `{page, canApply, values:{…}}` — giá trị hiện tại, secret che bằng `•` |
| `Apply(pageId, valuesJson, dryRun = true)` | `{page, ok, dryRun, error, changed, rows:[{sink,field,old,new,changed}], notes:[…]}`; chỉ key có trong `valuesJson` mới được đụng; giá trị chứa `•` bị bỏ qua; ghi thật thành công → marker `done` |
| `SetMarker(pageId, marker)` | `done` \| `deferred` \| `na` \| `clear` |
| `ClearRequest(id)` | gỡ yêu cầu nhờ Claude (vd `artstyle`) sau khi xử lý |
| `Open(pageId = null)` | mở / focus cửa sổ ở trang đó |
| `SetupAll()` | mở cửa sổ + bắt đầu luồng Setup tất cả |

- `status`: `todo` · `partial` · `done` · `deferred` · `na` · `error` · `info` (Tổng quan). `group`: `overview` · `setup` · `advanced`.
- Ghi được bằng `Apply`: `project`, `marketing`, `ads`, `localize`, `artstyle`, `social`. `iap` chỉ đọc; `firebase` /
  `publisher` ghi qua nút trên cửa sổ (tạo app Firebase / chuyển bộ SDK không undo được nên luôn cần người bấm).
- `requests`: trang ArtStyle nút **Nhờ Claude soạn** thêm `artstyle` → skill `/setup-project artstyle` soạn
  `ArtStyle.md` theo § Bootstrap của template rồi `ClearRequest("artstyle")`.

Batchmode / CI vẫn có `Ezg.Editor.Shared.Marketing.MarketingConfigApplier.ApplyFromCli` (tải xong sheet thì ghi
mọi sink, kể cả PlayerSettings, như bản 0.x).

## Thư mục

| Thư mục | Vai trò |
|---|---|
| `Editor/Core/` | trạng thái (`EzgKitState`), JSON, đích ghi `AppSecretsConfig`, define, chạy tiến trình, HTTP bất đồng bộ |
| `Editor/UI/` | `EzgKitWindow`, bộ dựng UI (`Ui`), `EzgKit.uss`, khung trang (`SetupPage`) |
| `Editor/UI/Pages/` | một file mỗi mục — nửa Core (`Detect / GetValues / Apply`, không UI) + nửa UI (`Build / CollectUi`) |
| `Editor/Setup/` | cầu nối SDK (AdsConfig, AppLovin, Facebook), ArtStyle.md, LocalizeDownloader, kiểm giá trị nhập |
| `Editor/Marketing/`, `Firebase/`, `Iap/`, `Publisher/`, `Social/`, `Readiness/` | lõi nghiệp vụ (giữ từ 0.x, sửa đích ghi) |
| `Editor/Api/` | `EzgKitApi` |

Thêm một mục: viết class kế thừa `SetupPage` trong `Editor/UI/Pages/`, thêm một dòng vào `SetupPages.Create()`
và một id vào `PageIds` (id phải ổn định — skill gọi theo id).
