# Changelog

## [0.1.0] - 2026-06-16
### Added
- Initial release extracted from `Assets/Plugins/FastScriptReload`.
- Runtime assembly `FastScriptReload.Runtime` (assembly change loader, detour crash handler, scoped logger, temporary new-field values).
- Editor assembly `FastScriptReload.Editor` (hot-reload manager, file watcher setup, Roslyn-based dynamic compilation and code rewriting, welcome screen).
- Bundled Harmony, Roslyn and ImmersiveVRTools.Common DLLs under `Plugins/`, referenced via `overrideReferences` / `precompiledReferences`.
- Vendor documentation and third-party license notices.
