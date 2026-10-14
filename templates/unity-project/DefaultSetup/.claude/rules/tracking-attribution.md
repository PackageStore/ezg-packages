---
trigger: always_on
---

# TRACKING — AppsFlyer / Meta / GameAnalytics phải khớp install

Mỗi SDK chỉ ghi install khi nó **start**. Meta (Facebook SDK native, `AutoLogAppEventsEnabled`) tự start lúc
mở app, không qua code Unity. AppsFlyer / GameAnalytics start trong luồng boot của game — đứng sau một chuỗi
bước dài thì người chơi thoát app khi splash còn chạy sẽ có trên Meta mà không có trên AF/GA. Đã gặp thật
(i001, CPI test 10/2026): AF 240 install vs GA 315.

**Thứ tự boot (Splash) — bắt buộc:**
1. Nạp config + save (chỉ đọc local) → **AppsFlyer** ngay sau đó, trước asset bundle / ads / Firebase / IAP.
2. **GameAnalytics** ngay sau AF (GA bỏ hẳn mọi event bắn trước `Initialize`).
3. Rồi mới tới bundle, ads, Firebase, IAP store…

**AppsFlyer init — bắt buộc:**
- **Một bước boot riêng** (`RunStep` riêng): không chung try/catch với IAP hay module khác — lỗi bước khác
  không được kéo AF theo, lỗi AF không được chặn boot.
- Listener conversion data là **GameObject riêng của game** (`AppsFlyerHost`), không mượn listener của module
  IAP (mượn thì AF phải chờ IAP dựng xong).
- Chỉ `initSDK` + `startSDK` là bắt buộc. ATT, customer user id, Purchase Connector, event phụ: **mỗi bước
  try/catch riêng**; `startSDK()` đặt trong `finally`. Ngay sau đó `TrackingService.IsAppsFlyerStarted = true`.
- Gọi lại (soft-restart về Splash) chỉ cập nhật customer user id — không init lại SDK, không bắn lại
  event một-lần-mỗi-cài-đặt.

**Gửi event:**
- Event AppsFlyer **không chờ Firebase** — `com.ezg.tracking` ≥ 0.3.0 chỉ chờ `IsAppsFlyerStarted`. Đừng tự
  thêm gate `IsInitFirebase` cho đường AF (Firebase chậm/timeout/thiếu Play Services là mất event vĩnh viễn).
- Conversion data về trước Firebase: package giữ lại và tự áp khi `IsInitFirebase = true`;
  `SetUAProperties(null)` không đè dữ liệu đã về.
- Event một-lần (`af_first_open`…) chỉ gửi **sau** `startSDK`.

**Doanh thu quảng cáo (MAX → AppsFlyer):**
- `Revenue < 0` (MAX trả -1 khi không có số) → **không** `logAdRevenue` (gửi -1 là trừ doanh thu).
- `monetizationNetwork` = `adInfo.NetworkName`, không phải chuỗi mẫu của docs.
- Số gửi dạng string luôn `ToString(..., CultureInfo.InvariantCulture)` — máy locale VN/DE ra `0,0012`.
- Param rỗng/null thì bỏ khỏi dictionary, không gửi.

**Cấu hình (validate-release soát):** AppsFlyer dev key + `IOSAppId` **của chính app này** (copy từ project
khác là attribution iOS chạy sang app khác); GameAnalytics có game key cho **mọi** platform build (thiếu iOS
là GA không đếm iOS — check `tracking.gameanalytics-key`); Facebook App ID / Client Token khớp manifest.

**Kiểm trên device, không phải Editor:** AF (SDK debug log / dashboard), GA (Live feed), Meta (Events Manager
→ Test Events). Ba bên không bao giờ khớp 100% (định nghĩa install, cửa sổ attribution, SKAN khác nhau) —
lệch vài % là bình thường, lệch hàng chục % là boot order / init đang sai.
