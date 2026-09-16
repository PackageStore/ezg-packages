# EZG User Segment

Engine phân khúc người chơi và điều phối action (User Segmentation & Action Engine — spec v0.4 + Phụ lục C
Interface & Schema Contract). Engine đọc một **config JSON từ xa** (segment, tag, experiment A/B, rule với
cooldown / cap / one-shot), dựng **state theo từng player** từ các event game phát vào (progress, purchase,
ad, screen, custom), rồi chọn action cho rule khớp và giao cho **executor do game đăng ký**. SDK không biết
gì về game cụ thể: mọi thứ game-specific (tài nguyên nào, màn nào, lưu ở đâu, tracking gì) đi qua interface.

Spec đầy đủ nằm trong `Documentation~/` của package (2 file markdown + 1 sơ đồ data-flow HTML).

---

## 1. Trong package có gì

| Thư mục | Assembly | Nội dung |
|---|---|---|
| `Runtime/Engine/` | `Ezg.Package.UserSegment.Engine` (`noEngineReferences`, C# thuần) | Parser + validator config, evaluator AST, reducer state, resolver (cooldown / cap / one-shot / group conflict / daily cap), assignment experiment theo hash, action history, window counter, tracking emitter, clock drift (`clock_suspect`). Chạy được ngoài Unity, test bằng NUnit. |
| `Runtime/Unity/` | `Ezg.Package.UserSegment` | `SegmentationSdk` (public API static), `SegSdkBehaviour` (session start/end, resume, refetch), adapter mặc định (`FileStateStorage` ghi atomic vào `persistentDataPath/seg/`, `UnityConfigFetcher` qua UnityWebRequest, `UnityTimeSource`, `UnityLogger`), `SegDebugOverlay` (IMGUI, chỉ dev build). |
| `Editor/` | `Ezg.Package.UserSegment.Editor` | Menu **Ezg > User Segment > Init** — copy tầng Integration vào project (mục 4). |
| `Tests/Editor/` | `Ezg.Package.UserSegment.Tests` (EditMode, `UNITY_INCLUDE_TESTS`) | Unit test engine + `TestVectorRunner` chạy 22 vector JSON trong `Vectors/` (mỗi vector = một case của spec: edge fire, control group, one-shot, refund offline, cap, rollover, seed merge, kill switch…). `TestRunnerCli.Run(path)` để chạy từ code. |
| `Samples~/Integration/` | `Ezg.Package.UserSegment.Integration` (chỉ compile SAU khi Init) | Tầng nối SDK với **Unity Game Template**: bootstrap tự đăng ký hook core, catalog asset, 5 executor, player-data module lưu action history, tracking sink Firebase, seed provider, purchase bridge, map screen id. Xem mục 4. |
| `Documentation~/` | — | Spec v0.4, Phụ lục C, sơ đồ data-flow. |

Namespace runtime: `Ezg.UserSegment`. Namespace Integration: `Ezg.Feature.System.UserSegment`.

---

## 2. Cài đặt

Project sinh từ template đã có scoped registry **Easygoing**, chỉ cần thêm dependency:

```json
"dependencies": {
  "com.ezg.user-segment": "0.0.2"
}
```

Hoặc cài từ tab **UPM Packages** của Feature Hub. `com.unity.nuget.newtonsoft-json` được kéo tự động.

### Peer requirements (project phải tự có, package KHÔNG bundle)

**Runtime (`Ezg.Package.UserSegment`):**

| Thư viện | Vì sao |
|---|---|
| UniTask (`com.cysharp.unitask`, assembly `UniTask`) | `UnityConfigFetcher` và vòng đời `SegSdkBehaviour`. Template đã có. |

**Tầng Integration (chỉ khi dùng menu Init — mục 4):**

| Assembly | Nguồn |
|---|---|
| `Ezg.Features`, `Ezg.Core`, `Ezg.Core.Localize`, `Ezg.Singleton`, `Ezg.Dictionary`, `Ezg.InstanceFactory`, `Ezg.Package.Factory` | Unity Game Template (`Assets/_Project/`) và các package `com.ezg.*` template kéo sẵn |
| `Ezg.Tracking` | `com.ezg.tracking` (Firebase Analytics là peer của package đó) |
| `Ezg.LocalNotification` | `com.ezg.local-notification` |
| `TigerForge.EasyEventManager` | `com.ezg.easy-event-manager` |

`Ezg.Features` **không phải package để cài** — nó là asmdef của chính code project
(`Assets/_Project/Features/Ezg.Features.asmdef`), project nào sinh từ template đều có sẵn. Menu Init kiểm tra
danh sách này và 6 hook core (mục 4) trước khi copy; thiếu gì nó **chỉ cảnh báo** (log + dialog "Vẫn copy" /
"Huỷ"), dev vẫn được copy rồi tự bổ sung. Copy khi còn thiếu ⇒ assembly Integration báo lỗi compile cho tới
khi project có đủ.

---

## 3. Mô hình hoạt động (đọc trước khi tích hợp)

```
game events ──► SegmentationSdk.* ──► SegEngine.Enqueue ──► Reducer (state) ──► Evaluator (rules)
                                                                                    │
   IActionExecutor.Execute(ActionRequest) ◄── Resolver (cooldown/cap/one-shot) ◄────┘
        │  ReportPresented / ReportExecuted(result) / ReportFailed(reason)
        ▼
   ITrackingSink (seg_* events + user property)      IActionHistoryStore (blob one-shot / cooldown floor)
```

- **Config**: `GET {ConfigBaseUrl}?game={GameId}&env={Env}` (+ header `X-Config-Token` khi env ≠ prod, chỉ dev
  build). Envelope được validate nghiêm (unknown field, sai type, ref hỏng, `game_id`/`env` lệch ⇒ từ chối
  toàn bộ, giữ cache cũ). Cache tại `persistentDataPath/seg/config_cache.json`; state tại `seg/state.json`.
  Không có URL và không có cache ⇒ dùng `DevFallbackEnvelope` (dev only) hoặc engine đứng yên.
- **Manifest**: SDK tự dựng từ `SdkOptions` (rewards / screens / custom events / custom state) + action type
  của các executor đã đăng ký. Rule tham chiếu id hoặc action type không có trong manifest ⇒ rule bị disable
  với lý do `unsupported`, KHÔNG crash.
- **Thời gian**: `DateTimeOffset.UtcNow` thô, cố ý không qua time manager có cheat/bonus của game — lệch
  đồng hồ được engine tự phát hiện (`clock_suspect`) và là tín hiệu có nghĩa.
- **Session**: SDK tự emit `SESSION_START` / `SESSION_END`, xử lý resume + timeout; game không gọi.
- **Purchase**: chỉ emit cho luồng **mua mới** (bấm mua → transaction granted). Restore / recover order
  không tính là purchase mới.
- **Executor contract**: `Execute` chạy trên main thread ngay sau resolver; **phải** gọi `ReportExecuted`
  hoặc `ReportFailed` trong session. Terminal đầu tiên thắng. `FailReason.Offline` / `NoPermission` được
  hoàn cooldown / cap / one-shot; lý do khác thì không.

---

## 4. Quick start — project Unity Game Template

1. Cài package (mục 2).
2. Menu **Ezg > User Segment > Init**. Menu copy `Samples~/Integration` vào
   `Assets/_Project/Features/System/UserSegment/Integration/` **kèm `.meta`** nên GUID giữ nguyên, rồi
   `AssetDatabase.Refresh()`. Từ lúc này tầng Integration là **code của project** (không nằm trong package):
   sửa tự do, và **nâng version package không tự cập nhật nó** — chạy Init lại (menu hỏi ghi đè) khi muốn
   lấy bản mới, sau đó merge lại chỉnh sửa riêng.
3. Điền `Resources/UserSegmentCatalog.asset`:
   - **Settings**: `gameId` (khớp `config.game_id` phía Worker), `env` (`prod` / `dev` / `staging`),
     `configBaseUrl`, `configToken` (chỉ env ≠ prod, release bỏ qua).
   - **Rewards**: `reward_id` → `resType` / `resId` / `amountPerUnit` (chỉ whitelist reward giá trị thấp).
   - **Popups / Offers**: id → `GameEnums.Features` (+ `productId` cho offer để nhận diện `purchased`).
   - **Notifications**: `template_id` → localize key title/body (category `Notification`).
   - **Custom events / custom state**: whitelist tên (≤ 10 custom event) và kiểu.
4. `UserSegmentSeedProvider.cs` có 2 `TODO` phải sửa theo game: `TotalSpendUsd` (hiện cộng giá local) và
   `ProgressMax` (hiện = 0, template không có gameplay).
5. Game có knob độ khó runtime ⇒ implement `IDifficultyKnob` và gán `UserSegmentBootstrap.DifficultyKnob`
   **trước** khi core phát `PlayerDataLoaded`; không gán thì action `CHANGE_DIFFICULTY` không được đăng ký.
6. Dev build / Editor: `Resources/UserSegmentDevConfig.json` là envelope fallback khi chưa có URL. Sửa
   `game_id` / `env` trong file này cho khớp catalog.

### Tầng Integration làm gì, và cần gì từ core

`UserSegmentBootstrap` có `[RuntimeInitializeOnLoadMethod]` — **core không gọi gì vào module**, module tự
nghe 6 hook generic của template. Project template **cũ** chưa có hook nào thì thêm đúng chỗ dưới đây:

| Hook (`EventName`) | Core phát ở đâu | Module dùng để |
|---|---|---|
| `PlayerDataLoaded` | `SplashSceneController` sau khi load save | `SegmentationSdk.Initialize` + đăng ký executor |
| `OnShowFeature` (data = `GameEnums.Features`) | `FeatureBaseController.Show`, `ScreenDontDestroyController` | `ScreenOpen(snake_case(feature))` |
| `PurchaseOnlineRequested` (data = productId) | `PurchaseManager.PurchaseOnline` | đánh dấu "mua mới" đang chờ |
| `IapTransactionGranted` (data = `string[]{ txId, productId }`) | `GameIapHost` khi cấp quà | `Purchase(productId, usd, txId)` nếu khớp pending |
| `AdRewardedCompleted` (data = placement) | `GameAdsTracker` | `AdRewarded(placement)` |
| `AdInterstitialShown` (data = placement) | `GameAdsTracker` | `AdInterstitial(placement)` |

Các file còn lại:

| File | Vai trò |
|---|---|
| `UserSegmentCatalog.cs` + `Resources/UserSegmentCatalog.asset` | Settings + map id engine → tài nguyên / màn / product / localize key |
| `Executors/GiveRewardExecutor.cs` | `GIVE_REWARD` → `RewardsService.ReceiveReward` (source `user_segment`) |
| `Executors/ShowPopupExecutor.cs` | `SHOW_POPUP` → `UIManager.Show(feature, data: text_key)`, result `dismissed` khi đóng |
| `Executors/ShowOfferExecutor.cs` | `SHOW_OFFER` → mở màn shop, result `purchased` nếu có `PurchasedIapSuccess` lúc mở |
| `Executors/ScheduleNotificationExecutor.cs` | `SCHEDULE_LOCAL_NOTIFICATION` → `LocalNotificationManager.RegisterOrReplace`; chưa có permission ⇒ `no_permission` |
| `Executors/ChangeDifficultyExecutor.cs` + `IDifficultyKnob.cs` | `CHANGE_DIFFICULTY` → knob của project (tuỳ chọn) |
| `Data/PlayerUserSegment(.Data).cs` + `UserSegmentPlayerData.cs` | Module `DataPlayer` lưu blob action history (đi theo save + backup của game) |
| `UserSegmentTrackingSink.cs` | `ITrackingSink` → `TrackingService` (Firebase) |
| `UserSegmentSeedProvider.cs` | Seed state cho user hiện hữu từ `LoginActivity` / `PlayerShop` |
| `UserSegmentPurchaseBridge.cs` | Ghép `PurchaseOnlineRequested` + `IapTransactionGranted` thành một `PURCHASE` |
| `UserSegmentScreens.cs` | `screen_id` = tên `GameEnums.Features` dạng snake_case; nguồn duy nhất cho manifest + hook |

Gỡ thư mục Integration là game vẫn compile và chạy — SDK chỉ đứng yên.

---

## 5. Quick start — project KHÔNG dùng template

Tự viết phần Integration tương đương (tham khảo `Samples~/Integration` như mẫu). Tối thiểu:

```csharp
using Ezg.UserSegment;

// 1. Executor cho từng action type config sẽ dùng (không đăng ký = rule bị disable `unsupported`)
sealed class MyRewardExecutor : IActionExecutor
{
    public ActionType ActionType => ActionType.GIVE_REWARD;
    public void Execute(ActionRequest r)
    {
        r.ReportPresented();
        // ... cấp r.Params.RewardId × r.Params.Amount qua hệ thống reward của game
        r.ReportExecuted("granted");                 // hoặc r.ReportFailed(FailReason.Offline)
    }
}

// 2. Sau khi save game đã load
SegmentationSdk.RegisterExecutor(new MyRewardExecutor());
SegmentationSdk.Initialize(new SdkOptions
{
    GameId = "my_game", Env = "prod",
    ConfigBaseUrl = "https://<worker>/config",
    Tracking = new MyTrackingSink(),               // ITrackingSink → Firebase / AppsFlyer
    ActionHistoryStore = new MySaveBlobStore(),    // IActionHistoryStore → blob trong save game
    SeedProvider = () => new SeedData { InstallAt = ..., PurchaseCount = ..., ProgressMax = ... },
    Rewards = new[] { "coins_small" },
    Screens = new[] { "home", "shop" },
    DebugBuild = Debug.isDebugBuild,
});

// 3. Emit event ở đúng chỗ trong game
SegmentationSdk.ScreenOpen("shop");
SegmentationSdk.ProgressStart(levelId);
SegmentationSdk.ProgressFail(levelId, durationS);
SegmentationSdk.Purchase(productId, usd, transactionId);   // chỉ luồng mua mới
SegmentationSdk.AdRewarded(placement);
SegmentationSdk.CustomEvent("tutorial_done");
SegmentationSdk.SetCustomState("vip_level", 3);
```

Bỏ trống `Storage` / `Fetcher` / `Clock` / `Logger` để dùng adapter mặc định.

---

## 6. API `SegmentationSdk`

| Nhóm | API |
|---|---|
| Khởi tạo | `RegisterExecutor(IActionExecutor)` (gọi TRƯỚC `Initialize` để manifest đúng từ lần fetch đầu) · `Initialize(SdkOptions)` · `IsInitialized` · `SdkVersion` |
| Seed / history | `NeedsSeed` · `Seed(SeedData)` · `ImportActionHistory(blob)` · `MarkActionUsed(actionId, epoch)` |
| Progress | `ProgressStart(unitId)` · `ProgressComplete/Fail/Quit(unitId, durationS)` |
| Monetization | `Purchase(productId, usd, transactionId, executionId = null)` · `AdRewarded(placement)` · `AdInterstitial(placement)` |
| Khác | `ScreenOpen(screenId)` · `CustomEvent(name)` · `SetCustomState(key, double|bool|string)` |
| Debug | `Debug` (`ISdkDebug`, null ở release) · `Engine` · `ExportManifestJson()` · `LastOfferAttribution()` |

`ActionRequest` cho executor: `ActionId`, `ExecutionId`, `Type`, `Params` (typed: `RewardId`, `Amount`,
`PopupId`, `TextKey`, `OfferId`, `Delta`, `Scope`, `TemplateId`, `DelayH` + `Raw`), `RuleId`, `Group`,
`Nth`; kết quả hợp lệ theo type xem `ExecResult`, lý do fail xem `FailReason`.

---

## 7. Debug overlay (dev build / Editor)

`DebugBuild = true` ⇒ SDK gắn `SegDebugOverlay`: nút nổi **SEG** góc dưới-trái hoặc phím **F9** để mở.
Overlay hiện state, tag, assignment, rule cuối, action gần nhất, config version; cho phép override state /
tag / assignment / `now` để ép rule chạy. Release build trả `SegmentationSdk.Debug == null`, không tốn gì.

---

## 8. Tests

Assembly `Ezg.Package.UserSegment.Tests` chỉ compile khi package nằm trong `testables` của
`Packages/manifest.json`:

```json
"testables": ["com.ezg.user-segment"]
```

Chạy headless (khuyên dùng — một số plugin Editor như Ultimate Editor Enhancer làm Test Runner crash khi đổi scene):

```bash
Unity -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform EditMode \
  -assemblyNames Ezg.Package.UserSegment.Tests -testResults /tmp/seg_tests.xml -logFile /tmp/seg_batch.log
```

Hoặc từ code Editor: `Ezg.UserSegment.Tests.TestRunnerCli.Run("/tmp/seg_tests.txt")`.

---

## 9. Lưu ý / known debt

- `package.json` **cố ý không khai `samples`**: Package Manager không hiện nút Import, menu Init là cửa vào
  duy nhất. Import tay `Samples~` ra `Assets/Samples/` sẽ tạo bản thứ hai cùng asmdef ⇒ lỗi trùng class.
- Sau Init, Integration là code project sở hữu; đường dẫn đích cố định
  `Assets/_Project/Features/System/UserSegment/Integration` (hằng `UserSegmentInitMenu.TARGET_PATH`).
  Di chuyển sau đó vẫn chạy (asmdef resolve theo tên, `Resources.Load` tìm mọi thư mục Resources).
- `UserSegmentSeedProvider` còn 2 `TODO` (USD thật, ProgressMax) — bắt buộc sửa theo game.
- Token config chỉ có ý nghĩa với env ≠ prod và bị bỏ ở release build; không đặt token vào asset của
  build production.
- Newtonsoft được tham chiếu qua `precompiledReferences: Newtonsoft.Json.dll` (assembly Engine
  `overrideReferences: true`); dependency `com.unity.nuget.newtonsoft-json` đảm bảo DLL có mặt.
- Cloudflare Worker phục vụ config và CLI validate config **không** nằm trong package này.
