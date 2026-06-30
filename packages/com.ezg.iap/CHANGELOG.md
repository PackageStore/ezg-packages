# Changelog

## [0.2.0] - 2026-06-30
### Changed
- Migrated to **Unity Purchasing v5** (`UnityIAPServices` / `StoreController`, event-driven `Order` flow). **Requires `com.unity.purchasing` 5.x** in the consuming project; no longer compatible with v4.
- `Ezg.Feature.IAP.asmdef` now references the v5 assemblies (`Unity.Purchasing*` instead of `UnityEngine.Purchasing*`).
- Restore is platform-aware: Apple via `RestoreTransactions`, Google Play via `FetchPurchases`.

### Fixed
- iOS purchases not completing: v5 transactions are now explicitly finalized via `ConfirmPurchase` (the legacy bridge left Apple transactions unfinished).
- `ConfirmPurchase` is always called in a `finally` so a granting exception cannot leave a transaction open and cause a re-delivery double-grant.
- `m_PurchaseInProgress` no longer gets stuck (which silently blocked all later purchases) when a product is unavailable or the store is not ready.

## [0.1.0] - 2026-06-16
### Added
- Initial release extracted from `Assets/_Project/Features/_Shared/InAppPurchase`.
- `InAppManager` singleton wrapping Unity IAP: initialize, buy, receipt validation (`CrossPlatformValidator`), restore, and localized pricing.
- Dependency-inversion seams `IPurchasing`, `IIapProfile`, `IIapReporter` plus injected `IapSecurityConfig` so the module carries no game code or secrets.
- `IapPurchaseInfo` DTO and `AppsFlyerListener` for forwarding AppsFlyer conversion data.
- Editor menu `Assets > Create > Ezg > IAP > Project Setup` generating the per-project integration template.
