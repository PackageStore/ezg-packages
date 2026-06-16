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
| `Runtime/IapSecurityConfig.cs` | Dữ liệu inject: tangle bytes, AppsFlyer public key, provider giá mặc định |
| `Runtime/IapPurchaseInfo.cs` | DTO truyền dữ liệu một giao dịch ra game (đã parse khỏi receipt) |
| `Runtime/AppsFlyerListener.cs` | `IAppsFlyerConversionData` → forward conversion data qua `IIapReporter` |
| `Editor/IapProjectSetupGenerator.cs` | Menu **Assets > Create > Ezg > IAP > Project Setup** sinh 4 file tích hợp cho project mới |

The game-specific integration (impl of `IPurchasing` / `IIapProfile` / `IIapReporter` + bootstrap wiring) lives
in the consuming project, **not** in this package. The editor generator scaffolds those stubs for you.

---

## Dependencies

Registry (auto-resolved):

- `com.ezg.singleton` — `Singleton<T>` base class for `InAppManager`.

## Peer requirements (consumer project must already provide)

These are referenced by the asmdef but are **not** package dependencies — install them in the consuming project:

- **`com.unity.purchasing`** (Unity IAP) — assemblies `UnityEngine.Purchasing`, `.Stores`, `.Security`,
  `.SecurityStub`, `.SecurityCore`.
- **`com.unity.services.core`** — assemblies `Unity.Services.Core`, `Unity.Services.Core.Environments`.
- **AppsFlyer SDK** — imported manually (not a UPM package); provides the `AppsFlyer` assembly. Used directly in
  the validated-purchase flow (`InAppManager.ValidateAndSend` / `AppsFlyerListener`). A project that does not use
  AppsFlyer must remove/abstract that path behind `IIapReporter`.

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
- `k_Environment = "production"` (Unity Services environment) is currently fixed in `InAppManager`.
