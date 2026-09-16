# Changelog

## [0.0.1] - 2026-09-16
### Added
- Initial release extracted from `Assets/_Project/Features/System/UserSegment/Package` (Unity Game Template).
- Engine core (C# thuần, `Ezg.Package.UserSegment.Engine`): config parser/validator, evaluator, reducer, resolver (cooldown / cap / one-shot / group conflict / daily cap), experiment assignment, action history, tracking emitter, clock drift detection.
- Unity SDK (`Ezg.Package.UserSegment`): `SegmentationSdk` public API, session lifecycle, default adapters (file storage, UnityWebRequest fetcher, time source, logger), IMGUI debug overlay (dev build).
- Editor menu **Ezg > User Segment > Init** copying the hidden `Samples~/Integration` layer into `Assets/_Project/Features/System/UserSegment/Integration` with `.meta` preserved.
- EditMode tests with 22 spec vectors (`Ezg.Package.UserSegment.Tests`).
- `Documentation~/`: spec v0.4, Phụ lục C (Interface & Schema Contract), data-flow diagram.
