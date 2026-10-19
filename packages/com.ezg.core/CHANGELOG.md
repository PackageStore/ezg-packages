# Changelog

All notable changes to `com.ezg.core` will be documented in this file.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/). Versioning follows [Semantic Versioning](https://semver.org/).

## [0.1.4] - 2026-10-19

### Added
- `SceneBackupGuard` (editor-only assembly `Ezg.Core.SceneBackupGuard.Editor`): Unity's "Recovering Scene Backups" dialog never shows on Editor startup. Unity keeps `Temp/__Backupscenes` on disk during Play Mode and, after exiting Play, for as long as the scene stays dirty; if the Editor dies meanwhile (crash, force-quit of a hung game), the next start asks to copy it into `Assets/_Recovery/` — natively, before any C# loads, so it cannot be answered from code. The guard parks the backup under another name while playing, moves it back right before Unity restores the scene on exit (restore after Play is unchanged), and deletes it once back in Edit Mode. After a crash the result equals answering "No": unsaved scene edits are discarded, nothing is written to `Assets/_Recovery/`. Define `EZG_KEEP_SCENE_BACKUPS` to opt out.

## [0.1.3] - 2026-08-03

### Fixed
- `AssetBundleExtractor` and `PrePlaySceneReloader` closed their `#if UNITY_EDITOR` guard *before* the namespace's closing brace, so every non-Editor build stripped the `#endif` block and left a dangling `}` → the package failed to compile in player builds. Moved `#endif` after the namespace brace in both files.

## [0.1.2] - 2026-06-25

### Removed
- Moved the game-specific `EnumBase` enum collection out of the package into the consuming game project (`Assets/_Project/Features/_Shared/Systems/EnumBase.Shared.cs`, namespace `Ezg.Feature.Shared.Systems`). These enums were not referenced by any core package code. `Runtime/Utils/EnumBase.cs` remains as a comment-only placeholder to preserve its import path.

### Changed
- Dropped the now-removed `enum` utilities mention from the package description.

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
