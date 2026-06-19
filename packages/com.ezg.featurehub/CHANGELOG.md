# Changelog

## [0.1.0] - 2026-06-19
### Added
- Initial release extracted from `Assets/_Project/Editor/FeatureHub`.
- Editor window (`Ezg > Feature Hub`) to install Unity packages from a remote catalog.
- Unity Packages tab: download + SHA-256 verify + import `.unitypackage`, with a local install record under `ProjectSettings/`.
- UPM Packages tab: write dependencies and scoped registries into `Packages/manifest.json`, download `file:` `.tgz` packages, and resolve.
- UI Toolkit interface with in-editor animated Lottie icons rendered via rlottie.
