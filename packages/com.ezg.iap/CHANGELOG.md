# Changelog

## [0.3.4] - 2026-10-08

Drop-in update from 0.3.3: no public API was removed or changed. Adds one opt-out property
(`InAppManager.LogStoreKit2PurchasesToFirebase`, default `true`). Android behaviour is unchanged.

### Fixed
- **iOS purchases missing from Firebase / GA4 revenue.** Unity IAP 5 buys through StoreKit 2 on iOS 15+, and Firebase Analytics
  only collects `in_app_purchase` automatically for StoreKit 1. The module now logs every purchase it grants through the native
  `Analytics.logTransaction` API (FirebaseAnalytics iOS 10.17.0+; Firebase Unity SDK 13.3.0 already ships 12.2.0), so no Firebase
  Unity SDK upgrade is needed. A bundled Swift plugin (`Plugins/iOS/EzgIapFirebaseStoreKit2.swift`) looks the transaction up by
  `Transaction.id` (= `order.Info.TransactionID`) and hands it to Firebase, which logs a verified `in_app_purchase` with price,
  currency and product id, exactly like its automatic StoreKit 1 event.
  - `ConfirmPurchase` of a newly granted iOS StoreKit 2 order now runs after Firebase has logged it: confirming finishes the
    transaction and a finished consumable can no longer be found. The reward is granted first, so the player never waits; the
    confirm runs on the first frame after Firebase replies (typically about 1 s — a freshly bought transaction only becomes visible
    in the StoreKit history after a short delay) or once 7 s have passed (wall clock: an app that comes back from the background
    after longer confirms on its first frame, and that order may not be logged). If the `InAppManager` object is disabled or
    destroyed while waiting, the pending confirms run in `OnDisable`. If iOS kills the suspended app while waiting, the store
    re-delivers the order on the next launch and `IIapOrderLedger` prevents a second grant (that order is then not logged).
  - **Requires an `IIapOrderLedger`.** Unity IAP records a confirmed transaction in its own transaction log only at
    `ConfirmPurchase`; deferring the confirm without a ledger would let a re-delivered order be granted twice after a crash.
    Hosts that call `Configure` without a ledger — or an order whose `MarkGranted` threw — keep the 0.3.3 behaviour (immediate
    confirm, no Firebase logging). The generated
    `IapBootstrap` template now carries a TODO for it.
  - Logged once per granted order: re-delivered orders, orders already granted in an earlier session, rejected orders and
    StoreKit 1 devices (iOS < 15, or `StoreKitSelector.forceStoreKit1`) are not logged. Android is not affected.
  - A store re-delivery of the same transaction while its confirm is waiting is not confirmed a second time.
  - `Buy()` of a product whose previous order is still waiting returns at once through `unSuccess`, exactly like a tap while
    another purchase is running (no `OnPurchaseFailed`, the running purchase's state is left alone). StoreKit would otherwise hand
    back the same unfinished transaction — no charge, no new order — and the purchase would hang. The window is typically about
    1 s after the previous purchase and at most 7 s. If the store still hands that waiting transaction back to a pending `Buy()` of
    the same product (e.g. `Buy()` called synchronously from the success callback), `IPurchasing.OnPurchaseFailed` is raised with
    the new reason `ExistingPurchasePending` — games that map failure reasons may want a friendlier message for it.
  - When Firebase is not configured yet (an order re-delivered right at app start), the lookup is retried after 0.5 s, 1 s, 2 s,
    plus a last attempt that still leaves 0.5 s of lookup before the cap.
  - Apps without FirebaseAnalytics build and behave as before (the plugin compiles to a no-op; only the first purchase of a session
    is confirmed one frame later, until the plugin reports that Firebase is missing). Apps on FirebaseAnalytics iOS older
    than 10.17.0 must add `EZG_IAP_DISABLE_FIREBASE_SK2` to the UnityFramework target's Swift Active Compilation Conditions from a
    `PostProcessBuild` script (Unity regenerates the Xcode project on every build). Removing the `.swift` file is not an option:
    IL2CPP links the plugin symbol directly.
  - Set `LogStoreKit2PurchasesToFirebase = false` if the game already calls `FirebaseAnalytics.LogAppleTransactionAsync`
    (Firebase Unity SDK 13.12.0+), otherwise purchases are logged twice.
  - Known limitation (Firebase, not this module): Firebase's StoreKit 1 fallback observer still captures a small share of StoreKit 2
    purchases automatically, so a few percent of `in_app_purchase` events may appear twice with the same `ga_dedupe_id`
    (firebase-ios-sdk#16686). Deduplicate on `ga_dedupe_id` in BigQuery.

### Changed
- The runtime asmdef now references `Unity.Purchasing.Utilities` (for `StoreKitSelector`, guarded by a version define for
  `com.unity.purchasing` 5.1.0+).

## [0.3.3] - 2026-10-05

Drop-in update from 0.3.2: no public API was removed or changed. Existing `IPurchasing` / `IIapProfile` / `IIapReporter` /
`IIapOrderLedger` implementations compile unchanged (verified against the five consuming projects, Editor + Android + iOS player
compiles).

### Fixed
- **Buy button stuck for the rest of the session.** On device, `Buy()` set the "purchase in progress" flag and then, when the store
  was not ready or the product was not purchasable, only logged. The flag was never released and no callback ran, so every later
  tap was silently rejected with "purchase in progress". The flag is now released and the failure is reported. The same applies to
  an unexpected exception after the flag was set (e.g. from `PurchaseProduct`) and to a host `OnPurchaseFailed` handler that throws.
  A failure in the "another purchase is running" branch never touches the running purchase's flag or callback.
- **Silent purchase failures.** A purchase rejected before it reaches the store now raises `IPurchasing.OnPurchaseFailed` with a
  `PurchaseFailureReason` name (`PurchasingUnavailable`, `ProductUnavailable`, `Unknown`) in addition to the caller's `unSuccess`.
  Causes: store not connected, products not fetched yet, SKU missing from the store, unexpected error. A tap while another
  purchase is still running keeps the old behaviour (`unSuccess` only), so the running purchase's UI is not released.
- **Store never reconnected after a failed or dropped connection.** Unity Purchasing reports a failed connect through
  `OnStoreDisconnected` and completes the `Connect()` task normally. The internal "connecting" flag therefore stayed set, and
  `Init()` could not connect again in that session (e.g. when the app was opened offline). The flag is now cleared on connect and
  on disconnect. Once `Init()` has been called, the module reconnects or re-fetches products when the app returns to the
  foreground, and when a purchase or restore is attempted while the store is not ready.
- **Paid but not granted when purchases arrive before the product catalog.** At app start the purchase list can be fetched before
  the products; Google Play then builds the product as `Unknown`. The order was validated and confirmed while the grant returned
  `false`. The order is now kept pending. Once the catalog arrives the module fetches purchases again and grants the order with the
  correct product type.
- **UI callback exceptions skipped the grant.** If `OnPurchaseCompleteBeforeCallback` or the `Buy()` success callback threw (for
  example by touching a destroyed UI object), `OnPurchaseComplete` was skipped but the order was still confirmed. Both callbacks
  are now isolated, and the grant always runs.
- **`unSuccess` was invoked on a successful cheat/test-IAP purchase** together with the success callback. It is now invoked only on
  failure.
- **Restore reported before the restored purchases arrived.** On Apple, `RestoreItem()` and `OnTransactionRestored(true)` ran in the
  `RestoreTransactions` callback. Unity invokes that callback before the restored orders are fetched, so the game read stale
  ownership. The result is now reported once, after the purchase list has been fetched, on both Apple and Google Play. Further
  changes:
  - `RestorePurchases()` while the store is not ready reports `false` instead of doing nothing.
  - A second call while a restore is running is ignored.
  - Callbacks from an older restore are ignored.
  - If the purchase list never arrives, the module fetches again after 10 s and reports `false` after 20 s instead of hanging.
    This happens on Google Play when the app regains focus, because Unity re-fetches into the same callback slot.
  - While Apple is waiting for the player to confirm the restore, pending-order recovery keeps running.
- **Pending-order recovery stopped for the rest of the session** after its fetch result was diverted (Google Play focus re-fetch,
  see above). The recovery fetch now expires after 30 s and can run again.
- **Fetched pending orders were processed twice per fetch.** Unity routes each fetched pending order to `OnPurchasePending`, and the
  module then forwarded the same order again from `OnPurchasesFetched`. Only the host ledger prevented a second grant, and a
  rejected order raised `OnPurchaseFailed` twice. Orders settled in this session (granted, already granted per the ledger, or
  rejected) are now skipped (same instance) or only re-confirmed (same transaction id, new delivery). An order that could not be
  granted is not settled and is processed again on its next delivery, as before. The ledger check still runs before
  `ConfirmPurchase` and outside any `try/catch`, so a ledger that throws keeps the order pending exactly as before.

### Added
- `IIapRestoreListener` (optional). Implement it on the `IPurchasing` class to receive `OnRestoreCompleted(success,
  restoredProductIds)` **instead of** `OnTransactionRestored(bool)`. The list contains the non-consumables and subscriptions the store
  reports as owned, plus pending orders granted during the restore. An empty list with `success = true` means there was nothing to
  restore. Hosts that do not implement it keep receiving `OnTransactionRestored(bool)`.

### Behaviour changes to check when updating
- Where a purchase used to fail silently (store not ready, SKU missing on the store), players now see the host's purchase-failed
  message, and hosts that track `OnPurchaseFailed` record an event.
- Tapping Restore while the store is not ready now shows the host's restore-failed message.
- On iOS the restore result arrives slightly later: after the purchase list is fetched, not right after the StoreKit callback.
- A pending order whose product is not in the fetched catalog is no longer confirmed without a grant. It stays pending until the
  catalog contains it. For a SKU the game no longer declares (or that permanently fails to fetch), the order therefore stays
  unconfirmed: Google Play refunds it after 3 days instead of charging with nothing granted, and Apple re-delivers it on every
  launch (logged, ignored). Keep retired SKUs in the catalog until their pending orders are settled.

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
