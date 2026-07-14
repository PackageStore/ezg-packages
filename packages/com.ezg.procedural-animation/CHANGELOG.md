# Changelog

## [0.1.1] - 2026-07-14
### Changed
- Moved menus under **EZG Technical Art**: window is now `Tools ▸ EZG Technical Art ▸ Procedural Animation ▸ Inbetween Generator`; data assets created via `Assets ▸ Create ▸ EZG Technical Art ▸ Procedural Animation ▸ {Feel Preset, Pose Asset, Pose Combination Graph}`.

## [0.1.0] - 2026-07-14
### Added
- Initial release. Extracted from `Assets/_Game/StickmanForge/Editor/ProceduralAnimation` in the StickmanForge project into a standalone UPM package.
- `Tools ▸ Ezg ▸ Procedural Animation ▸ Inbetween Generator` editor window: graph-based authoring, pose capture + filtering, pose renamer, per-connection settings, preview, and batch AnimationClip export.
- Runtime ScriptableObject data types (`FeelPresetAsset`, `PoseAsset`, `PoseCombinationGraphAsset`, `InbetweenGenerationSettings`, `PoseCombinationGenerationOptions`) in the `Ezg.ProceduralAnimation` assembly; authoring tools in the `Ezg.ProceduralAnimation.Editor` assembly.
- `DefaultFeelPresetFactory` for generating the default DOTween-style ease/feel preset library.
- Namespace changed to `Ezg.ProceduralAnimation[.Editor]`; menus moved under `Ezg ▸ Procedural Animation`; output/preset folders defaulted to `Assets/ProceduralAnimation` (configurable) so the package carries no project-specific paths.
