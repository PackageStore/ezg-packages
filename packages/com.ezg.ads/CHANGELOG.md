# Changelog

## [0.3.0] - 2026-09-10

### Fixed
- **Interstitials stopped showing after the first one in a session.** `CanShowInterstitial` was set `true` exactly once at init and cleared by every ad callback (`:561, :572, :599, :617`) with nothing ever restoring it, so `ShowInterstitial` short-circuited from the second call onwards. It is now derived from a timestamp of the last fullscreen ad and re-opens on its own after `TimeDelayShowInterstitialAds`.
- **"No interstitial right after a rewarded" was dead in practice.** The rule existed (`OnRewardedAdHiddenEvent` fires on any close, abandoned watches included) but hosts had to force `CanShowInterstitial = true` before every show to work around the bug above, which erased it. The guard now holds without host cooperation.
- `ShowInterstitial` no longer returns silently when a gate blocks it (level, remote config, spacing, no fill). Every non-showing branch invokes `onFail`, so callers that lock UI while waiting for a callback are released instead of hanging until their own watchdog fires.
- Gate checks moved ahead of storing `closeInter` / `failInter`, so a blocked call no longer leaves stale callbacks for a later ad event to pick up.

### Changed
- **Breaking:** `CountTimeShowInterstitialAds` is removed from `IRemoteConfigAdvertising`. Nothing ever incremented it — the counter and its delay were half of an unfinished scheme. Custom adapters must drop the member.
- **Breaking (behavioural):** assigning `CanShowInterstitial = true` is now a no-op. Setting `false` stamps "an ad just played"; the spacing re-opens on time. **Migration: remove every `CanShowInterstitial = true` from game code.** Leaving it in is harmless but no longer does anything, and any game relying on it to make interstitials work at all now gets correct spacing instead.
- `TimeDelayShowInterstitialAds` defaults to 30 seconds instead of 0, so a project with no remote config still has a floor against back-to-back fullscreen ads. Hosts that set it from remote config are unaffected.

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
