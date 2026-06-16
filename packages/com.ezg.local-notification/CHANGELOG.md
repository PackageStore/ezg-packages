# Changelog

## [0.1.0] - 2026-06-16
### Added
- Initial release extracted from `Assets/_Project/Features/_Shared/LocalNotification`.
- `LocalNotificationService` — register/refresh/cancel local notifications with permission handling, TigerForge event triggers, an `AppPaused` hook, and launch-payload capture.
- Android/iOS platform providers and permission services (`LocalNotificationPlatform`), runtime models (`LocalNotificationModels`), manager facade, and native bridge.
- Editor menu (`LocalNotificationSetupMenu`) that scaffolds the project-side `LocalNotificationScriptInfo.cs` rule template into a consuming project.
### Changed
- Localized notification text now resolves directly through the `com.ezg.localize` package instead of the host game's `GameSystems`, keeping the package self-contained.
