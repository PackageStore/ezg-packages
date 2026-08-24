# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-08-24

### Added
- Initial extraction from source project as a standalone UPM package.
- Namespace `EZG.TechnicalArt.VisualRoadBuilder`; assembly `EZG.TechnicalArt.VisualRoadBuilder.Editor`.
- Menu path: **Tools > EZG Technical Art > Visual Road Builder**.
- Asset menus: **Create > EZG Technical Art > Road Part Library** and **> Decor Library**.
- Configurable `roadPlanAtlas` field on `RoadPartLibrary` (replaces former hard-coded sprite-atlas GUID).
- Configurable `Save Folder` field on the editor window (replaces former hard-coded save directory).
- Default Libraries sample containing pre-configured `RoadPartLibrary` and `DecorLibrary` assets.
