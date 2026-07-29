# Changelog

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
