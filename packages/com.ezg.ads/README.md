# EZG Ads (`com.ezg.ads`)

Module quảng cáo (ads mediation) độc lập, hiện chạy adapter **AppLovin MAX**. Đã được tách hoàn toàn khỏi gameplay/business-logic: không gọi thẳng analytics SDK, không đọc player-data, không hardcode SDK key. Mọi thứ game-specific được **inject từ host project**.

> Package ↔ source: trích từ `Assets/_Project/Features/_Shared/AdsManager` của game Merge Two. Toàn bộ runtime code nằm trong assembly `Ezg.Ads`.

---

## 0. Cài đặt

`Packages/manifest.json`:

```json
"scopedRegistries": [
  {
    "name": "Easygoing code base",
    "url": "https://upm-registry-worker.developer-a1f.workers.dev",
    "scopes": ["com.ezg"]
  }
],
"dependencies": {
  "com.ezg.ads": "0.1.0"
}
```

`com.ezg.singleton` được kéo tự động qua `dependencies`. AppLovin MAX SDK + define `MEDIATION_MAX` là **peer requirement** (xem mục 2).

---

## 1. Tính năng

- Rewarded video, Interstitial, Banner (MRec để chỗ trống, chưa hỗ trợ).
- Gating theo level + theo cờ remote-config (bật/tắt, capping interstitial).
- Analytics tách qua interface `IAdsTracker` — host tự map sang Firebase/AppsFlyer/GameAnalytics/...
- Config (SDK key, ad-unit-id) đọc từ asset `AdsConfig`, không nằm trong code.

---

## 2. Dependencies

### Registry (tự kéo qua `package.json`)

| Package | Vì sao |
|---|---|
| **`com.ezg.singleton`** | Base `Singleton<T>` cho `AdsManager` |

### Peer requirements (project phải tự cài, package KHÔNG bundle)

| Dependency | Vì sao | Ghi chú |
|---|---|---|
| **AppLovin MAX SDK** | Module là adapter cho MAX (`MaxSdk`, `MaxSdkCallbacks`...) | Asmdef `MaxSdk.Scripts`. Thiếu → code MAX bị compile-out. |
| **Define `MEDIATION_MAX`** | Bật toàn bộ tích hợp MAX | **Thiếu là ads im lặng.** Player Settings → Scripting Define Symbols. |

---

## 3. Cấu hình bắt buộc

### 3.1. Thêm define `MEDIATION_MAX`
`Edit > Project Settings > Player > Other Settings > Scripting Define Symbols` → thêm `MEDIATION_MAX`.

### 3.2. Tạo asset `AdsConfig`
1. `Create > Ezg > Ads > Config`.
2. Đặt asset trong **một thư mục `Resources`** bất kỳ (vd `Assets/_Project/Resources/`).
3. Đặt tên **chính xác** là `AdsConfig` (module load bằng `Resources.Load<AdsConfig>("AdsConfig")`).
4. Điền key/ad-unit-id của project:

| Field | Mô tả |
|---|---|
| `maxSdkKey` | SDK key AppLovin MAX |
| `maxBannerId` | Ad-unit-id banner |
| `maxInterstitialId` | Ad-unit-id interstitial |
| `maxRewardedAndroidId` / `maxRewardedIosId` | Ad-unit-id rewarded theo nền tảng |
| `ironSource*` | (Tùy chọn) dành cho adapter IronSource sau này |

> Module tự chọn id theo nền tảng build (`#if UNITY_IOS`). Có thể inject config khác bằng `MediationConstant.SetConfig(myConfig)` (test/đa môi trường) thay vì đọc Resources.

> **AdsConfig là dữ liệu app-specific (secret) → KHÔNG được ship trong package.** Mỗi project tự tạo asset của riêng mình.

---

## 4. Khởi tạo (bootstrap)

Gọi `Configure` **một lần lúc startup** để inject tracker analytics + nguồn lấy level. Cách gọn nhất là dùng `RuntimeInitializeOnLoadMethod`:

```csharp
using Ezg.Package.AdsManager;
using UnityEngine;

public static class AdsBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Register()
    {
        var ads = AdsManager.Instance;
        if (ads == null) return;

        // tracker: map sự kiện ad sang analytics của project bạn
        // levelProvider: trả về level hiện tại của người chơi (cho gating)
        ads.Configure(new MyAdsTracker(), () => MyPlayerData.CurrentLevel);

        // (tùy chọn) phản ứng trạng thái banner
        ads.OnBannerLoaded += () => { /* ... */ };
        ads.OnBannerFailed += () => { /* ... */ };
    }
}
```

> Nếu KHÔNG gọi `Configure`: rewarded vẫn chạy (dùng `NullAdsTracker` no-op, không gating level), nhưng **không có số liệu analytics**.

---

## 5. Implement `IAdsTracker`

Module bắn các sự kiện vòng đời ad; host tự gửi sang analytics. Ví dụ skeleton:

```csharp
using Ezg.Package.AdsManager;

public class MyAdsTracker : IAdsTracker
{
    public void OnAdsInitialized() { /* vd đăng ký auto-track impression */ }

    public void OnRewardClick(string source)    { /* user bấm xem rewarded */ }
    public void OnRewardShow(string source)     { /* bắt đầu show */ }
    public void OnRewardLoadFailed(string src)  { /* load fail */ }
    public void OnRewardDisplayed(string src)   { /* đang hiển thị (vd start timer) */ }
    public void OnRewardCompleted(string src)   { /* xem xong, đã trả thưởng */ }

    public void OnAdRevenuePaid(AdRevenueInfo info)
    {
        // info.Format: Rewarded/Interstitial/Banner
        // info.Revenue, info.NetworkName, info.AdUnitId, info.AdUnitIdentifier,
        // info.AdFormatLabel, info.Placement, info.Source, info.CountryCode,
        // info.Currency, info.AdPlatform
        // -> map sang AppsFlyer.logAdRevenue / Firebase ad_impression / ...
    }
}
```

`AdRevenueInfo` là POCO thuần (không chứa type của SDK) nên host map tùy ý.

---

## 6. Sử dụng API

```csharp
// Rewarded
if (AdsManager.Instance.IsVideoRewardReady())
    AdsManager.Instance.ShowRewardedVideo("daily_reward", onFinish: () => GrantReward());

// Rewarded có đủ callback
AdsManager.Instance.ShowRewardedVideo("shop_x2",
    onFinish: GrantReward, onClose: Resume, onFail: ShowError);

// Interstitial
AdsManager.Instance.ShowInterstitial(source: "level_complete");

// Banner
AdsManager.Instance.HideBanner();
```

---

## 7. Remote-config gating

Các cờ điều khiển hiển thị (mặc định **false/0** — phải bật mới hoạt động). Rewarded KHÔNG phụ thuộc các cờ này.

```csharp
var cfg = AdsManager.Instance.advertisingRemoteConfig;
cfg.IsShowInterstitialAds        = true;   // mặc định false -> interstitial bị chặn
cfg.ShowInterstitialAdsFromLevel = 3;      // chỉ show từ level 3
cfg.TimeDelayShowInterstitialAds = 30;     // capping thời gian
cfg.IsShowBannerAds              = true;   // mặc định false -> banner bị chặn
cfg.ShowBannerAdsFromLevel       = 1;
```

> Level so sánh lấy từ `Func<int>` đã truyền ở `Configure`. Nếu chưa truyền, level mặc định = `int.MaxValue` (không bao giờ bị chặn bởi level).

---

## 8. Kiến trúc

| File | Vai trò | Phụ thuộc |
|---|---|---|
| `AdsManager.cs` | Facade public (Singleton), điều phối + forward event | `Singleton<T>` |
| `IAdvertising.cs` | Contract của adapter mediation | — |
| `IRemoteConfigAdvertising.cs` | Contract cờ remote-config | — |
| `MaxAdsvertising.cs` | Adapter AppLovin MAX (guard `#if MEDIATION_MAX`) | MAX SDK |
| `MediationConstant.cs` | Facade đọc key/ad-unit từ `AdsConfig` | `AdsConfig` |
| `AdsConfig.cs` | ScriptableObject chứa config (asset) | — |
| `IAdsTracker.cs` | Cổng analytics (+ `NullAdsTracker`) | — |
| `AdRevenueInfo.cs` | POCO doanh thu + enum `AdFormat` | — |

**Hướng phụ thuộc:** host → `AdsManager` → `IAdvertising`/`IAdsTracker` (interface). Code game-specific (analytics, player-data, event-name) nằm **ngoài** module, được inject vào — nên module tái sử dụng được ở mọi project.

---

## 9. Checklist nhanh (copy khi onboard project mới)

- [ ] Thêm scoped registry + `com.ezg.ads` vào `Packages/manifest.json`
- [ ] Cài AppLovin MAX SDK
- [ ] Thêm define `MEDIATION_MAX`
- [ ] Tạo asset `AdsConfig` trong `Resources/`, điền key
- [ ] Viết class implement `IAdsTracker`
- [ ] Gọi `AdsManager.Instance.Configure(tracker, levelProvider)` lúc startup
- [ ] Bật cờ `IsShowInterstitialAds` / `IsShowBannerAds` nếu cần inter/banner

---

## 10. Troubleshooting

| Triệu chứng | Nguyên nhân thường gặp |
|---|---|
| Ads không hiện gì cả | Thiếu define `MEDIATION_MAX` |
| Log `[Ads] Không tìm thấy AdsConfig...` | Chưa tạo asset, sai tên (phải là `AdsConfig`), hoặc không nằm trong `Resources/` |
| Rewarded chạy nhưng không có số liệu | Chưa gọi `Configure` (đang dùng `NullAdsTracker`) |
| Interstitial/Banner không hiện | Chưa bật `IsShowInterstitialAds` / `IsShowBannerAds`, hoặc level < ngưỡng |
| Compile error `MaxSdk` not found khi bật `MEDIATION_MAX` | Chưa cài AppLovin MAX SDK (assembly `MaxSdk.Scripts`) |
