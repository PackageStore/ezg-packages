# Changelog

## [0.1.2] - 2026-06-18
### Added
- `RpgStatsConfigBase<TKey>.ListVitalStats` (serialized `_listVitalStats`) and `StatConfigs<TKey>.ListVitalStats`, loaded in `StatConfigs<TKey>.Init`. Lets a project configure which stat keys are vitals (current/max) via the config asset instead of hardcoding it in per-project glue code.

## [0.1.1] - 2026-06-17
### Changed
- Project Setup scaffolder now generates the per-project files (`RPGStatType`, `RpgStatsConfig`, `RpgStatsBootstrap`) and the config asset into a dedicated `Assets/_Project/Features/_Shared/RpgStats` subfolder instead of `_Shared` directly, keeping the shared folder uncluttered.

## [0.1.0] - 2026-06-15
- Initial release: generic RPG stat system (`RPGStatCollection<TKey>`, attributes, vitals, modifiers, linkers), config ScriptableObject base (`RpgStatsConfigBase<TKey>`), level/exp system, and an Editor "Project Setup" scaffolder.
