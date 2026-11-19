# Changelog

## [0.3.2] - 2026-11-19

### Fixed
- **A caller that passed only `onClose` was never answered when no ad could play.** `ShowRewardedVideo(id, onSuccess, onFail)` — positional, so the game's fail handler lands in `onClose` and `onFail` is null — is what the EZG template's `PurchaseManager` shipped; with no ad loaded (or a display failure) nothing ran and the game's "watching…" button stayed locked (Loot Hero). `AdsManager.ShowRewardedVideo` now uses `onClose` in place of a missing `onFail`: closing early and failing are exclusive outcomes of one show, so the caller still gets exactly one callback. Running games fixed by upgrading only this package.
- **Rewarded `onClose` never ran on device.** `OnRewardedAdHiddenEvent` only stamped the interstitial spacing and reloaded; the `onClose` stored by `ShowRewardVideo` was never invoked, so every game's "closed before the reward" branch was dead with real MAX ads, and `IsShowReward` stayed `true` (it was only reset on reward / display failure). Hidden now resets `IsShowReward` and, when that show earned no reward, invokes `onClose`. Found by S011.
- **Reward vs hidden order is not fixed, so `onClose` waits 2 s.** The MAX Unity plugin forwards every native callback through a thread pool (Android: a 3-thread `ScheduledThreadPoolExecutor`; iOS: `dispatch_async` on a global concurrent queue), so `OnAdReceivedRewardEvent` can reach Unity after `OnAdHiddenEvent` even when the SDK fired them in order — in a test player, 10 of 20 concurrent deliveries arrived hidden-first. Deciding `onClose` at hidden would make a player who watched the whole ad lose the reward. The decision runs in a coroutine on `AdsManager` after `LATE_REWARD_GRACE_SECONDS` (2 s realtime, unaffected by `Time.timeScale`; the same window S004 / S001 already wait in `LilyAdService`), and a reward inside that window ends the show with `onFinish` only. A reward later than that still invokes `onFinish` (after `onClose`), as 0.3.1 would have, instead of being dropped.
- **`ShowRewardVideo` with no ad loaded returned silently on device.** It only logged, never invoked `onFail`, and left `IsShowReward` stuck `true`. It now kicks a reload and invokes `onFail` synchronously — the same as the existing "format disabled" branch.
- **Double tap no longer asks MAX to show twice.** A second `ShowRewardVideo` while the first is still pending (sent to MAX, not displayed / failed yet, at most 3 s) takes over the ad being opened — its callbacks replace the first call's, exactly as in 0.3.1 — but no longer calls `MaxSdk.ShowRewardedAd` again (which only produced `FullscreenAdAlreadyShowing`, firing `onFail` on the caller that then received the reward).
- A duplicate `OnAdReceivedRewardEvent` for the same ad no longer reports `OnRewardCompleted` twice.
- State is reset before the game's callback runs, so a game that shows again from inside `onFail` / `onFinish` is no longer clobbered afterwards.

### Changed
- **Behavioural:** every rewarded show ends with exactly one of `onFinish` (reward earned), `onClose` (closed before the reward, ~2 s after the ad closes) or `onFail` (not ready / failed to display; may run inside the `ShowRewardedVideo` call). Only exception: a reward arriving more than 2 s after the close gives `onClose` then `onFinish`. **Migration:** shared work such as unlocking UI or resuming the game belongs in all three callbacks.
- `onFail` defaults to `onClose` when the caller passes none (see Fixed).
- `DebugAdsProvider` used to call `onClose` right after `onFinish`, which no device path ever did; it now calls only `onFinish`, so debug builds behave like real ads.
- `MaxAdsvertising` takes an optional `MonoBehaviour coroutineHost` constructor argument (`AdsManager` passes itself). Without one, `onClose` is decided at hidden with no grace window.

## [0.3.1] - 2026-10-08

### Fixed
- **Interstitial placement never reached MAX.** `ShowInterstitial(..., source)` stored the placement for internal tracking but called `MaxSdk.ShowInterstitial(id)` without it, so MAX revenue reports and `AdInfo.Placement` were empty for every interstitial — and any tracker forwarding `AdRevenueInfo.Placement` (e.g. AppsFlyer ad revenue) sent a blank placement too. It is now passed as `MaxSdk.ShowInterstitial(id, source)`, matching rewarded.
- **Revenue `Source` leaked across formats.** Rewarded and interstitial shared one placement field, and banner revenue read it as well, so banner impressions were attributed to whichever fullscreen placement ran last. Each format now carries its own placement; banner revenue reports `Source = null` (banners take no placement), the same value it already had before the first fullscreen ad of a session.

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
