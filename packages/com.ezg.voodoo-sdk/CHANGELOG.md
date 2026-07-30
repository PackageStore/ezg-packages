# Changelog

## [0.1.2] - 2026-07-30
### Fixed
- Apple Unity Plug-ins priming no longer misses the environment type. `Type.GetType` with a short assembly name only searches assemblies that are already loaded, so in batchmode it intermittently reported "not in project" even though the package was installed — and when that happened no native library was copied and the iOS link failed with hundreds of undefined `_NSString_*` / `_NSObject_*` symbols. Resolution now falls back to Unity's `TypeCache` and an assembly scan.

## [0.1.1] - 2026-07-30
### Added
- Inject the Android manifest declarations that `com.unity.mobile.notifications` fails to emit in batchmode builds: the `UnityNotificationManager` receiver, `custom_notification_android_activity` and `exact_scheduling` meta-data, `POST_NOTIFICATIONS`, and — when enabled in the notification settings — the boot receiver and exact-alarm permissions. Values are read from `ProjectSettings/NotificationsSettings.asset`, never hardcoded.
- `VoodooSdkXml` helper that preserves the UTF-8 BOM and inserts XML at line boundaries with matching indentation, so manifest edits produce clean diffs.

### Fixed
- Firebase Messaging restore and `tools:replace` qualification no longer strip the manifest's UTF-8 BOM or break indentation of the tag they insert before.

## [0.1.0] - 2026-07-30
### Added
- Initial release extracted from `Assets/_Project/Features/_Shared/VoodooSdk`.
- Config schema + commented template written to `ProjectSettings/voodoo-sdk.config.json`.
- Generator for `Assets/Resources/TinySauce/Settings.asset`, plus Facebook app id / client token wiring.
- Self-healing patch for GameAnalytics' `GAMaxIntegration.cs`, re-applied whenever TinySauce is re-imported (AppLovin MAX v13 removed `CrossPromo` and `RewardedInterstitial`).
- Build hook that restores Firebase Messaging entries and qualifies `tools:replace` names in `AndroidManifest.xml` after TinySauce overwrites it.
- Primer that initialises Apple Unity Plug-ins during batchmode iOS builds, so native libraries are copied and the Xcode project links.
- Preflight validation that fails the build early with an actionable message instead of dying at link time.
- Menu items under `Tools/Voodoo SDK/` and a batchmode entry point `VoodooSdkInstaller.InstallFromCommandLine`.
