# Ezg Tracking

Engine analytics **game-agnostic**. Đẩy event + user-property sang **Firebase Analytics** và **AppsFlyer**, nhưng bản thân package **không biết gì** về game cụ thể: mọi event, field, và nguồn dữ liệu người chơi đều do project tiêu thụ tự cắm vào qua các *extension point*.

---

## 1. Trong package có gì (Runtime)

| File | Vai trò |
|---|---|
| `TrackingService` | Engine tĩnh. Gửi Firebase/AppsFlyer event + set user-property. Nhận payload theo 3 kiểu: **typed object** (public field → param), **`IDictionary<string,object>`** (zero boilerplate), hoặc **enum bất kỳ** (`ToString()` làm tên event). |
| `TrackingButtonController` (`Ezg.Tracking.UI`) | Component drop-in: gắn lên `Button`, click là bắn 1 Firebase event. Đặt tên event trong Inspector, không cần code. |

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
