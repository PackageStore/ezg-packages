# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.1] - 2026-08-24

### Added
- Claude Code skill shipped in `ClaudeSkill~/VisualRoadBuilder/` (solver guide plus
  `canvas_decode.py`, `prefab_tiles.py`, `diff_tiles.py`, `solver_dump.cs`), installed into
  `<projectRoot>/.claude/skills/` by **Tools > EZG Technical Art > Install Claude Skill**.
- `RoadPartLibrary.ResolveRoadPlanAtlasPath()` — falls back to the `_road_plan.psd` shipped inside
  this package when `roadPlanAtlas` is empty, so the 2D preview works with no assignment.
- The editor window assigns the Part Library itself on open when the project holds exactly one
  `RoadPartLibrary`, matching how `DiscoverCanvasSaveDir` finds the save folder.
- `com.unity.2d.psdimporter` declared as a dependency — the shipped atlas is imported by the PSD
  Importer and its slices do not exist without it.

### Changed
- `RoadPartLibrary.roadPlanAtlas` widened from `Texture2D` to `Object`. A `.psd` imported by the PSD
  Importer has a `GameObject` main asset and the texture only as a sub-asset, so a `Texture2D` field
  could not hold it.
- Default Libraries sample leaves `roadPlanAtlas` empty and relies on the fallback.

### Fixed
- A window opened for the first time on another machine loaded no preview sprites, and every
  Setup-tab reload button did nothing: the atlas path resolved to empty and `EnsureRoadSprites`
  returned early. Both resolution sites — the window partial and `SpriteLoader` — now go through
  `ResolveRoadPlanAtlasPath()`.
- The Road 2 ramp preview never loaded: `EnsureRoadSprites` matched a slice named `hway_to_road2`
  while the shipped atlas names it `hway_to_road_type2`.
- The "assign Road Plan Atlas" HelpBox no longer shows when the fallback did resolve an atlas.

## [0.1.0] - 2026-08-24

### Added
- Initial extraction from source project as a standalone UPM package.
- Namespace `EZG.TechnicalArt.VisualRoadBuilder`; assembly `EZG.TechnicalArt.VisualRoadBuilder.Editor`.
- Menu path: **Tools > EZG Technical Art > Visual Road Builder**.
- Asset menus: **Create > EZG Technical Art > Road Part Library** and **> Decor Library**.
- Configurable `roadPlanAtlas` field on `RoadPartLibrary` (replaces former hard-coded sprite-atlas GUID).
- Configurable `Save Folder` field on the editor window (replaces former hard-coded save directory).
- Default Libraries sample containing pre-configured `RoadPartLibrary` and `DecorLibrary` assets.
