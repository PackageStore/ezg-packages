# Changelog

## [0.1.1] - 2026-06-16
### Fixed
- Added asmdef references `Google.Play.Games` and `Apple.GameKit` so the platform-guarded login providers compile in consumer projects. Without them, a project that defines `GPG_LOGIN` failed with `CS0246: 'GooglePlayGames' could not be found` because the standalone `Ezg.Core.Firebase` assembly did not reference the Google Play Games assembly (previously resolved implicitly when the code lived in a host assembly).

### Notes
- The Sign in with Apple provider (`FirebaseAppleLoginProvider`) still requires the **AppleAuth** plugin (lupidan/apple-signin-unity) to expose an assembly the package can reference; it is not referenced here because the plugin is not installed and ships without an asmdef. Only relevant for iOS builds.

## [0.1.0] - 2026-06-16
### Added
- Initial release extracted from `Assets/_Project/Core/Infrastructure/Firebase`.
- Authentication facade `FirebaseLoginManager` (Google Play Games, Sign in with Apple, Game Center) with session validation.
- `FirebaseFirestoreManager`, `FirebaseFunctionManager`, `FirebaseStorageManager` (`ISyncData` save-data sync) and secure `Nonce` generator.
- Generic `FirebaseRemoteManager` Remote Config wrapper (no game-specific keys) with an `OnRemoteConfigApplied` hook.
- `FirebaseConfig` ScriptableObject for per-project tuning (storage bucket, timeouts, Game Center settings) loaded from `Resources/FirebaseConfig`.
- Editor tool **Create > Ezg > Firebase > Firebase Config** (`FirebaseConfigCreator`) that creates the config asset and scaffolds a game-side `GameRemoteConfig`.

### Removed
- Unused `using Ezg.Core.Extensions` / `using Ezg.Core.Utils` in `FirebaseStorageManager`, eliminating the `com.ezg.core` dependency so the package has no `com.ezg.*` dependencies.
