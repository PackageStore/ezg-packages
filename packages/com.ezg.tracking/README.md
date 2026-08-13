# Ezg Tracking

Engine analytics **game-agnostic**. Đẩy event + user-property sang **Firebase Analytics**, **AppsFlyer** và **GameAnalytics**, nhưng bản thân package **không biết gì** về game cụ thể: mọi event, field, và nguồn dữ liệu người chơi đều do project tiêu thụ tự cắm vào qua các *extension point*.

> GameAnalytics là **optional**: nằm ở assembly riêng, chỉ compile khi có define `EZG_GAMEANALYTICS`. Project không cài SDK vẫn build bình thường, mọi call GameAnalytics thành no-op. Xem [mục 5](#5-gameanalytics-sink-voodoo).

---

## 1. Trong package có gì (Runtime)

| File | Vai trò |
|---|---|
| `TrackingService` | Engine tĩnh. Gửi Firebase/AppsFlyer event + set user-property. Nhận payload theo 3 kiểu: **typed object** (public field → param), **`IDictionary<string,object>`** (zero boilerplate), hoặc **enum bất kỳ** (`ToString()` làm tên event). |
| `TrackingButtonController` (`Ezg.Tracking.UI`) | Component drop-in: gắn lên `Button`, click là bắn 1 Firebase event. Đặt tên event trong Inspector, không cần code. |
| `TrackingService` (nửa GameAnalytics) | Các hàm `SendGameAnalytics*` gửi event **typed** của GameAnalytics: business / resource / progression / ad / design / error. Tự queue khi SDK chưa init, tự sửa event id sai luật. |
| `GameAnalyticsEventId` | Sửa chuỗi cho hợp luật GameAnalytics (ký tự lạ → `_`, cắt 64 ký tự/phần, tối đa 5 phần ngăn bởi `:`). |
| `IGameAnalyticsSink` | Cổng giao tiếp với SDK. Implement thật nằm ở assembly optional; thay được bằng fake để test. |

Engine **không** tham chiếu player-data, không hardcode tên event của game. Ba điểm cắm để host project điền vào:

- `TrackingService.UserPropertyProvider` — `Func<object>` trả snapshot user-property; engine gọi trước mỗi Firebase event.
- `TrackingService.IsInitFirebase` — bật `true` **sau khi** Firebase init xong; trước đó mọi call là no-op an toàn.
- Các overload `SendFirebase<TEnum>` / `SendAppsFlyer<TEnum>` — gọi bằng enum riêng của game để có type-safety mà engine vẫn agnostic.

---

## 2. Peer requirements (project phải tự cài, package KHÔNG bundle)

| Dependency | Vì sao |
|---|---|
| **Firebase Analytics SDK** | `TrackingService` log qua `FirebaseAnalytics` (`Firebase.Analytics.dll`). Thiếu → không compile. |
| **AppsFlyer Unity SDK** | Dùng cho `SendAppsFlyer` / `SetUAProperties`. |
| **UniTask** (`com.cysharp.unitask`) | API `SendFirebase` trả `UniTask`. |

> Đây là plugin/asset import, không phải package trên registry, nên không khai báo trong `dependencies` — cài sẵn trong project trước khi dùng.

---

## 3. Quick start

```csharp
// 1) Sau khi Firebase init xong:
TrackingService.IsInitFirebase = true;

// 2) (Tuỳ chọn) cắm user-property provider:
TrackingService.UserPropertyProvider = () => new Dictionary<string, object>
{
    ["player_id"]     = PlayerDataManager.Account.AccountId,
    ["current_level"] = PlayerDataManager.Progress.Level,
};

// 3) Gửi event — chọn 1 trong 3 kiểu:
TrackingService.SendFirebase("level_start", new Dictionary<string, object> { ["level_id"] = 7 }).Forget();
MyEnum.level_start.Send(new MyConfig { level_id = 7 }); // qua extension tự định nghĩa (xem sample)
```

---

## 4. Sample

Package Manager → chọn **Ezg Tracking** → tab **Samples** → **Import** mục *Integration Demo*. Sample là bộ starter **tối giản, self-contained** (không phụ thuộc symbol game nào) gồm event enum mẫu, user-property provider mẫu, `.Send()` extension, call-site mẫu và 1 **demo scene** bấm-thử-thấy-log. Copy xong thì đổi tên event/field và thay thân provider bằng player-data của bạn.

---

## 5. GameAnalytics sink (Voodoo)

GameAnalytics là **sink product-analytics thứ 2**, chạy song song Firebase — khác hẳn AppsFlyer/Adjust (attribution). Voodoo bắt buộc có nó khi publish.

### 5.1. Bật

| Bước | Việc |
|---|---|
| 1 | Cài SDK GameAnalytics (thường đi kèm TinySauce của Voodoo, qua `com.ezg.voodoo-sdk`). |
| 2 | Đảm bảo script SDK nằm trong 1 assembly có tên **`GameAnalytics.Scripts`**. Bản vendor của TinySauce **không có sẵn asmdef** — `com.ezg.voodoo-sdk` tự tạo giúp. |
| 3 | Thêm scripting define **`EZG_GAMEANALYTICS`** (`com.ezg.voodoo-sdk` cũng tự thêm). |

Xong. Sink **tự đăng ký** lúc `BeforeSceneLoad`, không cần viết dòng wiring nào. Thiếu bất kỳ bước nào ở trên thì assembly optional không compile, `GameAnalyticsSink` = null, mọi call là no-op — **không lỗi build**.

### 5.2. Map khái niệm game → event GameAnalytics

| Việc trong game | Gọi | Loại event GA |
|---|---|---|
| Mua IAP thành công | `SendGameAnalyticsBusiness(currency, amountInCents, itemType, itemId, cartType)` | Business (doanh thu) |
| Nhận / tiêu tiền ảo | `SendGameAnalyticsResource(flow, currency, amount, itemType, itemId)` | Resource |
| Vào màn / thắng / thua | `SendGameAnalyticsProgression(status, p01, p02, p03, score)` | Progression |
| Quảng cáo hiển thị / click | `SendGameAnalyticsAd(action, adType, sdkName, placement)` | Ad |
| Mọi thứ còn lại | `SendGameAnalyticsDesign(eventId, value)` | Design (catch-all) |

`amountInCents` là **đơn vị nhỏ nhất** của tiền tệ: $2.99 → `299`, không phải `2.99`.

### 5.3. Hai cái bẫy package đã xử lý sẵn

- **Event gửi trước khi SDK init bị GA vứt** (chỉ log 1 dòng). Engine **queue lại** (tối đa 128) rồi replay khi SDK ready. Không cần tự canh thời điểm init.
- **Event id sai luật cũng bị vứt im lặng.** GA chỉ nhận `A-Z a-z 0-9`, khoảng trắng và `- _ . ( ) ! ?`; mỗi phần ≤ 64 ký tự; tối đa 5 phần ngăn bởi `:`. Tên item có dấu tiếng Việt hay id quá dài là mất event. Engine tự sửa qua `GameAnalyticsEventId` trước khi gửi.

### 5.4. Bẫy còn lại — phải tự cấu hình

> **Resource event bắt buộc khai báo trước.** GA loại bỏ resource event nếu `currency` hoặc `itemType` chưa được khai trong `Assets/Resources/GameAnalytics/Settings.asset` (`ResourceCurrencies`, `ResourceItemTypes`) **trước lúc SDK init**. Danh sách rỗng = **rớt 100% resource event**. Khai bằng `com.ezg.voodoo-sdk` (field `gameAnalyticsResourceCurrencies` / `gameAnalyticsResourceItemTypes` trong config) hoặc sửa tay asset đó.

> **User id phải đặt sớm.** GA gắn custom id lúc init và bỏ qua nếu gọi sau. `SetGameAnalyticsUserId` vì thế **không** đi qua queue — gọi ngay khi biết player id, càng sớm càng tốt.
