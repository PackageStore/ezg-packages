# Changelog

## [0.1.1] - 2026-06-20
### Added
- Keyboard shortcut `Ctrl/Cmd + Shift + F` to open the Feature Hub window.
### Fixed
- Window no longer stays stuck "deactivated" after a `.unitypackage` import: teardown of the busy state no longer depends on the fragile package-name match, which fails in Dialog mode when Unity reports the package's embedded name instead of the downloaded temp file name.
- Cancelling/closing the native import dialog now reliably re-enables the window via a watchdog on the `PackageImport` window, covering the case where Unity does not fire `importPackageCancelled`.
### Added
- Initial release extracted from `Assets/_Project/Editor/FeatureHub`.
- Editor window (`Ezg > Feature Hub`) to install Unity packages from a remote catalog.
- Unity Packages tab: download + SHA-256 verify + import `.unitypackage`, with a local install record under `ProjectSettings/`.
- UPM Packages tab: write dependencies and scoped registries into `Packages/manifest.json`, download `file:` `.tgz` packages, and resolve.
- UI Toolkit interface with in-editor animated Lottie icons rendered via rlottie.
