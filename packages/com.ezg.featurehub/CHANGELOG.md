# Changelog

## [0.1.2] - 2026-06-20
### Added
- On opening the Feature Hub, the window now validates that the project's `Packages/manifest.json` declares the required scoped registries; if any are missing it shows a confirm popup and, on accept, registers them (URL + union of scopes) and resolves. Falls back to the built-in EZG registry when the remote template declares none.
### Fixed
- A `.unitypackage` that contains scripts no longer shows as "not installed" after a successful import. Importing such a package triggers a domain reload that wiped the in-memory completion callback before the install record was written; the record is now persisted via a `SessionState`-backed pending marker and finalized by an `[InitializeOnLoad]` handler that survives the reload.

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
