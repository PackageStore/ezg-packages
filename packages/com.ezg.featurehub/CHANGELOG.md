# Changelog

## [0.1.5] - 2026-06-23
### Fixed
- Installing Feature Hub on a machine that did not already have `com.gindemit.rlottie` failed to compile with `CS0246: The type or namespace name 'LottiePlugin' could not be found`. UPM cannot resolve a git dependency declared transitively in a package's `package.json`, so the rlottie runtime that powers the in-editor Lottie icons was never pulled in automatically.
  - The only file touching `LottiePlugin` (`LottieElement.cs`) is now guarded by the `EZG_HAS_RLOTTIE` define (added via asmdef `versionDefines` keyed on `com.gindemit.rlottie`). Feature Hub therefore **compiles whether or not rlottie is present** — when absent, Lottie elements degrade to an empty box instead of breaking the build.
  - A new `[InitializeOnLoad]` bootstrap (`FeatureHubRuntimeDependency`) **self-heals** the project: on editor load it ensures `com.gindemit.rlottie` (git url) is present in `Packages/manifest.json`, adding it once if missing and resolving. After Unity resolves, `EZG_HAS_RLOTTIE` switches on and the animated icons light up automatically — no manual manifest editing required. It never overwrites an existing entry.

## [0.1.3] - 2026-06-21
### Fixed
- Packages that are already present in the project are no longer reported as "not installed".
  - **UPM tab:** status is now computed from `PackageManager.Client.List` (offline, including indirect dependencies) instead of only reading direct dependencies in `Packages/manifest.json`. This detects packages that are resolved transitively, embedded, local, or are built-in modules — i.e. installed without being a literal `dependencies` entry. Version is compared against the actually resolved version; "different version" is only flagged when both the template target and the resolved version are concrete semver (so `file:`/git/range targets no longer show a false mismatch).
  - **Unity Packages tab:** a `.unitypackage` whose assets were imported manually / before the Hub existed / on another machine (no install record) is now detected via optional `markerPaths` / `markerGuids` declared per catalog entry. If any marker path/GUID resolves to an existing asset, the entry is treated as installed. Detection is evaluated live each refresh, so deleting the assets reverts the status.
### Added
- `CatalogAsset.markerPaths` and `CatalogAsset.markerGuids` (optional) in the asset catalog schema, used to recognize already-present `.unitypackage` content. Entries without markers keep the previous record-only behavior.

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
