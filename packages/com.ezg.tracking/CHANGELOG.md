# Changelog

## [0.1.0] - 2026-06-15

Initial publish. Extracted the game-agnostic analytics engine from the game source.

- `TrackingService` — forwards events and user properties to Firebase Analytics and AppsFlyer via typed configs, plain dictionaries, or any enum. Knows nothing about any specific game.
- `TrackingButtonController` (`Ezg.Tracking.UI`) — drop-in component that sends a Firebase event on button click.
- Extension points: `UserPropertyProvider`, `IsInitFirebase`, and generic `SendFirebase<TEnum>` / `SendAppsFlyer<TEnum>` overloads — the host project supplies its own events and user-property source.
- Sample `IntegrationDemo` — self-contained starter (event enum, provider, `.Send()` extensions, demo scene).
- Firebase Analytics SDK, AppsFlyer Unity SDK and UniTask are peer requirements (not bundled).
