# Changelog

All notable changes to `com.ezg.core` will be documented in this file.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/). Versioning follows [Semantic Versioning](https://semver.org/).

## [0.1.0] - 2026-06-16

### Added
- Initial release extracted from `Assets/_Project/Core`.
- Asset-loading adapter: `ResLoader`, `AssetRef`, `AssetBundleManager` (Resources / AssetBundle / Play Asset Delivery, gated behind `USE_PAD` / `USE_BUNDLE`).
- UI helper components: `SafeArea`, `UILayoutAdjuster`, `UI_GridLayoutGroup`, `UI_CircleLayoutGroup`, `UI_ScrollViewSnap`, `UI_TweenMove`, `UI_TransformRandom`, `UI_CanvasGroupFade`, `UI_CloseWithFade`, `UI_FadeImageOnScroll`, `CurvedTextLegacy`, `CinemachineLockCamera`, `DragDropController`, `PropogateDrag`, button extensions, and the `JumpInJumpOut` animation component.
- Extensions/utilities: `MonoBehaviourHelper`, `BezierMove`, `AutoHideGameObject`, `RebuildUILayoutHelper`, `ClearTrailRedererWhenDisable`, `ColorUtils`, `CoreUtils`, `UpdateManager`/`IUpdateManager`, `BundleNameAttribute`, plus editor-only helpers guarded by `#if UNITY_EDITOR`.
- Device/security helpers: `SecuritySystems`, `SecuredServiceBase`, `DetectDevice`.
- Shared `EnumBase` enum collection and `Resource` model.
- `Ezg.Core` runtime asmdef (+ `Ezg.Core.JumpInJumpOut.Editor`) targeting Unity 6000.2+.
- All Odin Inspector attributes guarded behind `#if ODIN_INSPECTOR` so the package compiles without Odin installed.
