# Integration Demo

Starter **tối giản, self-contained** cho `Ezg.Tracking`. Mọi thứ ở đây chạy được ngay sau khi import mà **không** phụ thuộc symbol game nào (không `PlayerDataManager`, không `GameConstant`) — mục đích là dạy *cách cắm*, rồi bạn đổi nội dung theo game của mình.

## Chạy thử

1. Mở scene **`DemoTracking.unity`** → bấm **Play**.
2. Console hiện ngay vài block `-----FIREBASE TRACKING-----` (event bắn lúc `Start`).
3. Bấm nút **"Click me → track event"** giữa màn hình → thêm 1 event `button_click` được log (qua `TrackingButtonController`).

> Demo bật `TrackingService.IsInitFirebase = true` để thấy log ngay. Ngoài editor, không có Firebase thật nên chỉ là log — đó là chủ đích.

## Các file

| File | Vai trò | Bạn sửa gì |
|---|---|---|
| `DemoEvents.cs` | Enum event mẫu + `DemoEventConfig` (typed payload) + `.Send()` extension. | Đổi tên event/field thành của game bạn. |
| `DemoUserProperties.cs` | Snapshot user-property mẫu. | Đổi field thành thứ bạn phân khúc người chơi. |
| `DemoBootstrap.cs` | Bật engine, đăng ký provider, bắn event demo, dựng UI nút. | Copy *pattern*, gọi từ bootstrap thật của bạn rồi xoá file. |
| `DemoUsage.cs` | Call-site mẫu (3 kiểu gọi). | Tham khảo rồi copy vào feature code. |
| `DemoTracking.unity` | Scene 1 GameObject chạy `DemoBootstrap`. | Giữ để thử, hoặc xoá. |

## Đưa vào project thật — 3 bước

1. **Bật engine sau khi Firebase init:** `TrackingService.IsInitFirebase = true;` (xem `DemoBootstrap.EnableTrackingForDemo`).
2. **Cắm user-property provider** đọc từ player-data của bạn (xem `DemoBootstrap.RegisterUserProperties`). Bỏ qua nếu không cần user-property.
3. **Gọi tại feature code** theo 1 trong 3 kiểu trong `DemoUsage.cs`: typed `.Send()`, dictionary, hoặc `TrackingService.SendFirebase/SendAppsFlyer` trực tiếp.

Sau khi nắm pattern, có thể xoá `DemoBootstrap.cs` / `DemoUsage.cs` / scene và chỉ giữ lại cấu trúc enum + provider của riêng bạn.
