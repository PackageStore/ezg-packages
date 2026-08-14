# Changelog

## [0.2.2] - 2026-08-14

### Fixed
- **The GameAnalytics sink never ran in a player build.** Nothing references `Ezg.Tracking.GameAnalytics` at compile time — game code calls `TrackingService` in the core assembly, which reaches the sink only through `IGameAnalyticsSink`, and the sink installs itself from a `RuntimeInitializeOnLoadMethod`. The managed linker therefore saw an assembly with no incoming references and dropped it from the build entirely, so nothing ever registered a sink and every GameAnalytics event was silently queued and lost. Confirmed by inspecting a built APK: `Ezg.Tracking.dll` and `Ezg.Tracking.UI.dll` were present in the IL2CPP metadata, `Ezg.Tracking.GameAnalytics.dll` was not. Fixed with `[assembly: AlwaysLinkAssembly]`, which Unity provides for exactly this case, plus `[Preserve]` on the registration method.

  This failed invisibly: the Editor behaved correctly, compilation was clean, and no warning or error appeared at runtime — the events simply never arrived. Only a stripped IL2CPP build on a device shows it, so verify GameAnalytics integration on a real build rather than in the Editor.

## [0.2.1] - 2026-08-14

### Fixed
- **A project without GameAnalytics now really does pay nothing.** 0.2.0 claimed the GameAnalytics calls were no-ops when the SDK is absent, but they still sanitized their arguments (building strings) and then queued a closure that nothing would ever drain — so a game with no GameAnalytics burned allocations on every resource/design event and held 128 dead closures forever. Every sender now checks for a registered sink before doing any work, and the replay queue is only used when a sink exists but has not finished initializing. No behaviour change for projects that do have the SDK.

## [0.2.0] - 2026-08-13

Adds GameAnalytics as a third sink, alongside Firebase and AppsFlyer. Purely additive — existing call sites and behaviour are untouched.

- `TrackingService` is now `partial`; the GameAnalytics half lives in `Runtime/GameAnalytics/`.
- New typed senders: `SendGameAnalyticsBusiness` / `SendGameAnalyticsResource` / `SendGameAnalyticsProgression` / `SendGameAnalyticsAd` / `SendGameAnalyticsDesign` / `SendGameAnalyticsDesignParts` / `SendGameAnalyticsError`, plus `SetGameAnalyticsUserId` and the `IsGameAnalyticsReady` flag.
- **Optional dependency.** The SDK call layer sits in a separate `Ezg.Tracking.GameAnalytics` assembly constrained to the `EZG_GAMEANALYTICS` define. Projects without GameAnalytics compile unchanged and every GameAnalytics call is a no-op — the core assembly still references only Firebase, AppsFlyer and UniTask.
- **Events raised before the SDK is ready are queued and replayed**, instead of being discarded by GameAnalytics with only a log line. The queue is bounded at 128 and is dropped if the SDK never initializes (usually declined tracking consent).
- **Event ids are repaired before they are sent.** GameAnalytics silently discards events whose ids break its character/length rules; `GameAnalyticsEventId` maps rejected characters to `_`, truncates parts to 64 characters and keeps at most 5 `:`-separated parts, so a name with accents or an over-long item id no longer costs the event.
- Guard rails that would otherwise fail validation silently: a score on a progression `Start` event is dropped, a progression dimension is ignored when the previous one is missing, and non-positive business/resource amounts are rejected with a warning naming the expected unit.
- `IsTracking` now gates the GameAnalytics sink (Firebase and AppsFlyer are unchanged).
- A sink can be replaced via `RegisterGameAnalyticsSink`, which makes the GameAnalytics path testable with a fake.

> **Resource events need setup.** GameAnalytics rejects a resource event whose currency or item type was not declared in `Assets/Resources/GameAnalytics/Settings.asset` **before** the SDK initializes. Declare them there (or through `com.ezg.voodoo-sdk`) or every resource event is dropped.

## [0.1.0] - 2026-06-15

Initial publish. Extracted the game-agnostic analytics engine from the game source.

- `TrackingService` — forwards events and user properties to Firebase Analytics and AppsFlyer via typed configs, plain dictionaries, or any enum. Knows nothing about any specific game.
- `TrackingButtonController` (`Ezg.Tracking.UI`) — drop-in component that sends a Firebase event on button click.
- Extension points: `UserPropertyProvider`, `IsInitFirebase`, and generic `SendFirebase<TEnum>` / `SendAppsFlyer<TEnum>` overloads — the host project supplies its own events and user-property source.
- Sample `IntegrationDemo` — self-contained starter (event enum, provider, `.Send()` extensions, demo scene).
- Firebase Analytics SDK, AppsFlyer Unity SDK and UniTask are peer requirements (not bundled).
