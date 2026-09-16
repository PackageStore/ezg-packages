# Changelog

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
