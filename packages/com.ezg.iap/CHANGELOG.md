# Changelog

## [0.3.2] - 2026-08-26

> 0.3.1 was published with a compile error on device (`MiniJson` is not visible outside Unity Purchasing) — use 0.3.2.

### Fixed
- **iOS never granted rewards with Unity Purchasing 5 / StoreKit 2.** `CrossPlatformValidator.Validate` intentionally returns an empty array for Apple on StoreKit 2 (the native layer already verifies the transaction JWS), but `ValidatePurchase` required at least one receipt with a transaction id, so every iOS order was treated as forged: `ConfirmPurchase` ran (Apple charged the user) and no reward was granted. The StoreKit 2 path now validates the order through `IAppleOrderInfo` (transaction id, `jwsRepresentation` present, JWS payload `productId`/`bundleId` match, not revoked).
- `isTestIAP` alone could skip the store on a release build — any code calling `SetIsTestIAP(true)` (e.g. a cheat panel) bought every pack for free. The flag now only takes effect when `IIapProfile.IsCheatEnabled` is true.

### Changed
- Google Play / StoreKit 1 validation additionally requires the receipt to contain the product being purchased with a non-empty transaction id (a valid receipt for a different order is rejected). `MissingStoreSecretException` (tangle stripped or missing) stays fail-closed — hosts must preserve the generated Tangle classes from managed stripping (link.xml).
- A rejected receipt now raises `IPurchasing.OnPurchaseFailed("ValidationFailure")` so the UI can tell the player instead of failing silently.

## [0.3.0] - 2026-08-03

### Added
- `IIapOrderLedger` — optional persistent ledger injected through `Configure()`. Keyed by the transaction id captured at `PendingOrder` (the SDK returns an empty id after confirm), it makes granting idempotent: if the app dies after granting and saving but before `ConfirmPurchase`, the store re-delivers the order and the ledger stops a second grant. The host implements it with its own save system and must flush to disk inside `MarkGranted`.
- Pending/deferred order recovery: `ProcessPendingOrdersOnPurchasesFetched(true)` at connect, plus a `FetchPurchases()` on returning to foreground. Covers iOS Ask-to-Buy orders approved while backgrounded and purchases interrupted mid-flow, on both Android and iOS. Distinct from `RestorePurchases()`, which stays a user-initiated action for non-consumables; the two flows guard against overlapping.
- `AppsFlyerListener` implements `IAppsFlyerPurchaseValidation` and the Purchase Connector revenue data sources (StoreKit 1 + StoreKit 2), and exposes `PlayerIdProvider` so the host can attach `player_id` to auto-logged revenue events without the package referencing game code.

### Changed
- `Configure()` takes an optional fifth `IIapOrderLedger` argument. Existing four-argument calls still compile; without a ledger the idempotency guard is simply skipped.
- IAP revenue is now reported by the AppsFlyer Purchase Connector (ROI360), configured by the consumer project. The legacy `validateAndSendInAppPurchase` path and its `AppsFlyerPublicKey` config remain for reference but are no longer called — re-enabling one alongside the other double-counts revenue. See README.

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
