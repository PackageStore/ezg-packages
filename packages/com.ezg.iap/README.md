# EZG In-App Purchase (`com.ezg.iap`)

Standalone In-App Purchase module wrapping **Unity IAP** (`com.unity.purchasing`) + **AppsFlyer** receipt
validation. Everything game-specific (shop, player data, analytics, event names, secrets) is pushed behind
interfaces (the *seam*) and injected at startup — the package references **no game code**.

> Namespace: `Ezg.Feature.IAP`. Assembly: `Ezg.Feature.IAP` (runtime) + `Ezg.Feature.IAP.Editor` (editor).

---

## Package ↔ source

| In package | Role |
|------------|------|
| `Runtime/InAppManager.cs` | Singleton MonoBehaviour bọc Unity IAP: init, buy, validate receipt, restore, lấy giá |
| `Runtime/IPurchasing.cs` | Seam: danh mục product + các Action vòng đời mua |
| `Runtime/IIapProfile.cs` | Seam: `AccountId`, `IsCheatEnabled`, `RecordPurchase(price)` |
| `Runtime/IIapReporter.cs` | Seam: `OnPurchaseClick`, `OnPurchaseValidated`, `OnConversionData`, `RequestSync` |
| `Runtime/IIapOrderLedger.cs` | Seam (tuỳ chọn): sổ giao dịch bền vững chống cấp quà 2 lần khi store giao lại đơn |
| `Runtime/IIapRestoreListener.cs` | Seam (tuỳ chọn, 0.3.3): nhận kết quả Restore kèm danh sách product, thay cho `OnTransactionRestored` |
| `Runtime/IapSecurityConfig.cs` | Dữ liệu inject: tangle bytes, AppsFlyer public key, provider giá mặc định |
| `Runtime/IapPurchaseInfo.cs` | DTO truyền dữ liệu một giao dịch ra game (đã parse khỏi receipt) |
| `Runtime/AppsFlyerListener.cs` | `IAppsFlyerConversionData` → forward conversion data qua `IIapReporter` |
| `Runtime/StoreKit2ConfirmGate.cs` | (0.3.4) Giữ `ConfirmPurchase` của đơn iOS StoreKit 2 vừa cấp quà tới khi Firebase ghi xong, confirm đúng 1 lần |
| `Runtime/FirebaseStoreKit2Bridge.cs` | (0.3.4) P/Invoke tới plugin Swift, kết quả native đọc theo frame |
| `Plugins/iOS/EzgIapFirebaseStoreKit2.swift` | (0.3.4) Tra giao dịch StoreKit 2 theo `Transaction.id` → `Analytics.logTransaction` (Firebase iOS ≥ 10.17.0) |
| `Editor/IapProjectSetupGenerator.cs` | Menu **Assets > Create > Ezg > IAP > Project Setup** sinh 4 file tích hợp cho project mới |

The game-specific integration (impl of `IPurchasing` / `IIapProfile` / `IIapReporter` + bootstrap wiring) lives
in the consuming project, **not** in this package. The editor generator scaffolds those stubs for you.

---

## Dependencies

Registry (auto-resolved):

- `com.ezg.singleton` — `Singleton<T>` base class for `InAppManager`.

## Peer requirements (consumer project must already provide)

These are referenced by the asmdef but are **not** package dependencies — install them in the consuming project:

- **`com.unity.purchasing`** (Unity IAP) — assemblies `Unity.Purchasing`, `.Stores`, `.Security`,
  `.SecurityStub`, `.SecurityCore`, `.Utilities`.
- **`com.unity.services.core`** — assemblies `Unity.Services.Core`, `Unity.Services.Core.Environments`.
- **AppsFlyer SDK** — imported manually (not a UPM package); provides the `AppsFlyer` assembly. `AppsFlyerListener`
  routes conversion/attribution data back to the game via `IIapReporter`, and implements the Purchase Connector
  (ROI360) revenue data source. IAP revenue is reported by the AppsFlyer Purchase Connector, configured by the
  consumer project (see `GameInitialize.InitPurchaseConnector`), **not** by this package. The legacy
  `InAppManager.ValidateAndSend` / `validateAndSendInAppPurchase` path (and its `AppsFlyerPublicKey` config) is
  kept for reference but is no longer called — do not re-enable it alongside the Purchase Connector or IAP revenue
  is double-counted. A project that does not use AppsFlyer must abstract that path behind `IIapReporter`.
- **Firebase Analytics (optional, iOS)** — not referenced by any assembly. When the iOS app links the FirebaseAnalytics pod
  (10.17.0 or newer; Firebase Unity SDK 13.3.0 ships 12.2.0), StoreKit 2 purchases are logged to Firebase as `in_app_purchase`
  (see *iOS StoreKit 2 → Firebase* below). Without Firebase the plugin compiles to a no-op.

---

## Quick start

1. Install this package + the peer requirements above.
2. Right-click a folder → **Create > Ezg > IAP > Project Setup** → generates `InAppPurchase.cs`,
   `GameIapHost.cs`, `IapBootstrap.cs`, `IAPEventName.cs`. Fill in the `// TODO`s.
3. At splash: `IapBootstrap.Configure();` then `InAppManager.Instance.Init();` (Configure must run before Init/Buy).
4. Buy: `InAppManager.Instance.Buy(productId, onSuccess, "shop", "pack_name");`

See the generated `IapBootstrap` for wiring tangle bytes (Receipt Validation Obfuscator), AppsFlyer public key,
and the default-price fallback into `IapSecurityConfig`.

---

## Notes

- **Secrets stay out of the package** — tangle bytes + AppsFlyer public key are injected via `IapSecurityConfig`.
- **Init order:** `Configure()` must run before `Init()` / `Buy()` (guarded by `IsConfigured()`).
- **Failure reporting:** every purchase that does not reach the store raises `IPurchasing.OnPurchaseFailed(reason)`
  (`PurchasingUnavailable` / `ProductUnavailable` / `Unknown`) plus the caller's `unSuccess`. A tap while another purchase is
  running only gets `unSuccess`.
- **Reconnect:** after `Init()` has been called once, the module reconnects / re-fetches products by itself when the app returns
  to the foreground and when a purchase or restore is attempted while the store is not ready.
- **Restore:** the result (`RestoreItem()` then `OnTransactionRestored(bool)`, or `IIapRestoreListener.OnRestoreCompleted`) is
  reported once, after the store's purchase list has been fetched.
- **Order recovery:** a pending order whose product is not in the fetched catalog yet is kept pending (never confirmed without a
  grant); it is granted on the next purchase fetch once the catalog is there. Orders already settled in the session are not
  granted or reported twice.
- `k_Environment = "production"` (Unity Services environment) is currently fixed in `InAppManager`.
- **iOS StoreKit 2 → Firebase (0.3.4):** Firebase only auto-collects `in_app_purchase` for StoreKit 1, and Unity IAP 5 buys through
  StoreKit 2 on iOS 15+. For every order it grants, the module hands the transaction to Firebase through the bundled Swift plugin
  (`Analytics.logTransaction`) and confirms the order afterwards — usually ~1 s later, at most 7 s wall clock (an app paused longer
  confirms on its first frame back; if iOS kills the suspended app the order is re-delivered and the ledger blocks a second
  grant); the reward is granted first.
  While that confirm is waiting, `Buy()` of the same product returns at once through `unSuccess` (like a tap while another
  purchase is running). **Requires an `IIapOrderLedger`** passed to `Configure` (without one the order is confirmed immediately and not
  logged, because only the ledger protects a re-delivered order from a second grant while its confirm is deferred).
  Opt out with `InAppManager.Instance.LogStoreKit2PurchasesToFirebase = false` (do this if the game already calls
  `FirebaseAnalytics.LogAppleTransactionAsync`, or purchases are logged twice). FirebaseAnalytics iOS older than 10.17.0: add
  `EZG_IAP_DISABLE_FIREBASE_SK2` to the UnityFramework Swift Active Compilation Conditions from a `PostProcessBuild` script. Firebase may still auto-capture a few
  percent of StoreKit 2 purchases through its StoreKit 1 fallback, so dedupe `in_app_purchase` on `ga_dedupe_id` in BigQuery.
