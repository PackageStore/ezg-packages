# Changelog

## [0.1.0] - 2026-09-16
### Added
- `SdkOptions.Limits` (`SdkLimits`): `RewardAmountMax` (default 1000), `DifficultyDeltaMax` (default 2), `MaxCustomEvents` (default 10). Values below 1 fall back to the defaults.
- `manifest.limits` in `ExportManifestJson()` (`reward_amount_max`, `difficulty_delta_max`, `max_custom_events`) so the config CLI / Worker validator uses each game's declared ranges instead of hard-coded constants. Contract written in Phụ lục C §C.1.5 / §C.6.3 / §C.6.7 / §C.9.4 / §C.10.5 (`Documentation~/`).
- `ConfigParser.Parse(json | JObject, SdkLimits)` overloads; the engine parses fetched and cached configs with `Manifest.Limits`.
- Test vector `23_limits_from_manifest` and unit tests for widened / default / sanitized limits; vector harness reads `manifest.limits`.
### Changed
- `amount` / `delta` ranges and the custom-event cap are no longer fixed in the parser; defaults are unchanged, so existing configs behave exactly as before.

## [0.0.3] - 2026-09-16
### Fixed
- `SegDebugOverlay` read the F9 shortcut through `UnityEngine.Input`, which throws `InvalidOperationException` every frame in dev builds of projects whose Active Input Handling is "Input System Package" only. The shortcut now reads IMGUI `Event.current` inside `OnGUI`, so it works with either input backend and the package no longer depends on the legacy Input Manager.

## [0.0.2] - 2026-09-16
### Changed
- Menu **Ezg > User Segment > Init** no longer blocks when the project lacks assemblies the Integration layer references (`Ezg.Features`, `Ezg.Tracking`, `Ezg.LocalNotification`…); it now warns (log + dialog with "Vẫn copy" / "Huỷ") and lets the developer copy anyway, since those assemblies are the template's own code, not something to install.
### Added
- Init also checks the template core for the 6 `EventName` hooks the bootstrap listens to (`PlayerDataLoaded`, `OnShowFeature`, `PurchaseOnlineRequested`, `IapTransactionGranted`, `AdRewardedCompleted`, `AdInterstitialShown`) and warns which ones an older template is missing.

## [0.0.1] - 2026-09-16
### Added
- Initial release extracted from `Assets/_Project/Features/System/UserSegment/Package` (Unity Game Template).
- Engine core (C# thuần, `Ezg.Package.UserSegment.Engine`): config parser/validator, evaluator, reducer, resolver (cooldown / cap / one-shot / group conflict / daily cap), experiment assignment, action history, tracking emitter, clock drift detection.
- Unity SDK (`Ezg.Package.UserSegment`): `SegmentationSdk` public API, session lifecycle, default adapters (file storage, UnityWebRequest fetcher, time source, logger), IMGUI debug overlay (dev build).
- Editor menu **Ezg > User Segment > Init** copying the hidden `Samples~/Integration` layer into `Assets/_Project/Features/System/UserSegment/Integration` with `.meta` preserved.
- EditMode tests with 22 spec vectors (`Ezg.Package.UserSegment.Tests`).
- `Documentation~/`: spec v0.4, Phụ lục C (Interface & Schema Contract), data-flow diagram.
