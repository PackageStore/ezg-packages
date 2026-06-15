# Changelog

## [0.1.0] - 2026-06-16
### Added
- Initial release extracted from `Assets/_Project/Core/Infrastructure/RedDot`.
- `BaseRedDot` — abstract `MonoBehaviour` that subscribes to a TigerForge event key and toggles a target GameObject (the red dot) via `IsActive`.
- `Ezg.Core.RedDot` runtime assembly referencing `TigerForge.EasyEventManager`.
- Editor menu `Create ▸ Ezg ▸ RedDot ▸ Project setup` (`RedDotProjectSetup`) that scaffolds the project-side `RedDotId` enum and `RedDotBadge` base class with pinned `.meta` GUIDs so reused UI prefabs keep their script references.
