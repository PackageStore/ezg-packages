# Changelog

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
