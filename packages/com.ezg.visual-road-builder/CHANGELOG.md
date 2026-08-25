# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0] - 2026-08-25

### Added
- Station 2 block type (`BlockKind 3`): a second station that is placed as-is and deliberately does
  not participate in the road solver — no apron, no clearance, no fillet, no hook dots. Same 4×4
  footprint and pivot convention as Station 1.
- `RoadCanvasDoc.Stations2` / `RoadCanvasSave.stations2` — a separate persisted list for station-2
  blocks. Old canvases load unchanged (`stations2` defaults to empty); a canvas saved with station-2
  blocks is **not readable by 0.1.x** (the field is silently dropped on load, losing those blocks).
- `RoadPartLibrary.station2Prefab` field. Consumers must assign the station-2 prefab here; the bake
  pre-flight blocks when the canvas contains station-2 blocks and the slot is empty.
- Bake outputs station-2 blocks into a `RoadParent/Stations2` child group, parallel to `Stations`.
- Optional `station_area_2` atlas slice for the 2D canvas preview. When the slice is absent,
  station-2 blocks draw as a flat tinted rect (same fallback parking already uses).
- Station 2 brush in the tools panel with its own canvas colour (pink `0.85, 0.35, 0.65`), distinct
  from Station 1's blue.
- `_spStation2Area` sprite field on the editor window, with a reload row in the Setup tab's Building
  group.
- Eraser (E), Move All (G), Select & Move (Q), grid crop, and out-of-range prune all operate on
  `Stations2` alongside the existing block lists.
- Debug boundary overlay draws station-2 blocks with their prefab name.
- Status bar and Apply log include the station-2 count.

### Changed
- Tools-panel brush grid reordered so road brushes and block brushes each sit on their own rows:
  Row 1 `Road 1 / Road 2 / Lối đi bộ`, Row 2 `Highway / HW Decor / (empty)`,
  Row 3 `Station 1 / Station 2 / Park`. Previously Road 2 and Lối đi bộ sat at the end of the list.
- Ghost-block hover (`TryGhostBlock`) widened from a `bool isStation` out-parameter to a three-way
  `GhostBlockKind` enum (`Station`, `Station2`, `Parking`). Station-2 ghosts skip the solver's
  road-placement collectors, matching the placed block's behaviour.
- Undo/redo snapshot field set widened from 13 to 14 fields to include `stations2`. Canvas signature
  hash now covers `Stations2`; the first open of an existing canvas after upgrading reports it dirty
  (expected, one-time).

### Fixed
- `_spStation2Area` declared with `[SerializeField]` so the sprite reference survives domain reload.
  Without it the field reset to `null` after every script recompile and the Setup tab showed it as
  unloaded.
- Tools panel brush-switch and Move All toggle now clear `DraggingStation2` when changing mode. Two
  call sites were missing the reset, which could leave a stale drag index after switching away from
  the Station 2 brush mid-drag.

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
