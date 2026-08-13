# Changelog

## [0.2.0] - 2026-08-13
### Added
- **Assembly definition for the bundled GameAnalytics SDK.** Voodoo ships GameAnalytics with no asmdef, so the whole `GameAnalyticsSDK` namespace lands in `Assembly-CSharp-firstpass` — which assembly definitions cannot reference. The result is that no asmdef code (`com.ezg.tracking`, or the game's own assemblies) can call GameAnalytics at all, failing with "type or namespace not found" while the SDK sits right there in the project. `VoodooSdkGaAsmdefPatcher` writes a `GameAnalytics.Scripts` asmdef, mirroring what AppLovin MAX already does for its own vendored scripts, and re-creates it whenever TinySauce is re-imported. The `MaxSdk.Scripts` reference is only added when MAX is actually present.
- **Up-front declaration of GameAnalytics resource currencies and item types**, via the new optional `gameAnalytics.resourceCurrencies` / `gameAnalytics.resourceItemTypes` config fields. GameAnalytics discards every resource event whose currency or item type was not registered in its settings asset before the SDK initializes, and the default lists are empty — so a fresh project silently drops 100% of its resource events. The patcher only adds missing entries, never removes what the project declared itself, and edits through `SerializedObject` so the package still compiles before TinySauce has been imported.
- **`EZG_GAMEANALYTICS` scripting define**, added for Android and iOS when the GameAnalytics SDK is present. This is what enables the optional GameAnalytics sink assembly in `com.ezg.tracking` 0.2.0+.

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
