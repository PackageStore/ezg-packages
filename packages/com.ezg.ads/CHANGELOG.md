# Changelog

## [0.2.0] - 2026-08-03

### Added
- `AdFormats` flags + `AdsConfig.EnabledFormats`: each project opts into the formats it uses. Calling a disabled format is a safe no-op (no throw, no error log), so code shared between projects needs no `#if` guards or deleted call sites. Branch on `AdsManager.HasBanner` / `HasInterstitial` / `HasRewarded` when behaviour must differ.
- `DebugAdsProvider` + `AdsConfig.debugAds`: skips mediation SDK init entirely, resolves every show call as an immediate success and swaps the tracker for `NullAdsTracker` so no ad events reach analytics. Enables every format so all ad buttons stay reachable.
- `Ezg.Ads.Editor` assembly with `AdsBuildGuard` — a build-time confirmation dialog when `debugAds` is still on, since the flag is honoured in release builds too. Editor builds only; CI/script builds bypass it.
- `MediationConstant.Current` exposes the resolved config.

### Changed
- **Breaking:** `IAdvertising` is replaced by the split `IAdProvider` / `IBannerAds` / `IInterstitialAds` / `IRewardedAds` / `IMRecAds` interfaces in `IAdFormatModules.cs`, and `AdsManager.advertising` is gone. Custom adapters must implement the new interfaces; consumers should call `AdsManager` instead of reaching for the adapter.
- **Breaking:** `AdsConfig` SDK keys and ad-unit-ids are now per platform (`maxAndroidSdkKey`/`maxAndroidBannerId`/… + `maxIos*`) instead of one shared set with a single split rewarded id. Existing `AdsConfig` assets must be refilled.
- `AdsManager` no longer initialises itself in `Awake` — the host calls `Configure()` + `Init()` explicitly so init order stays visible and movable in the game's boot flow.
- Banner only shows once remote config has been fetched, instead of racing the fetch on a cold start.

### Fixed
- iOS: the AppLovin consent flow (GDPR/CMP) kept reappearing after the player denied ATT. `MaxAdsvertising` now disables the consent flow and reports no consent when ATT status is `DENIED`/`RESTRICTED`. Requires the new `Unity.Advertisement.IosSupport` reference, so `com.unity.ads.ios-support` is now a package dependency.

## [0.1.0] - 2026-06-15

Initial publish. Extracted the AppLovin MAX ads mediation module from the game source.

- Rewarded, interstitial and banner ads via the AppLovin MAX adapter (`MaxAdsvertising`, guarded by `MEDIATION_MAX`).
- Level-based and remote-config gating for interstitial/banner.
- Analytics decoupled through the `IAdsTracker` interface (host maps to Firebase/AppsFlyer/GameAnalytics).
- SDK keys/ad-unit-ids read from an injectable `AdsConfig` ScriptableObject (`Resources/AdsConfig`) instead of hardcoded constants.
- Depends on `com.ezg.singleton`. AppLovin MAX SDK and the `MEDIATION_MAX` define are peer requirements.
