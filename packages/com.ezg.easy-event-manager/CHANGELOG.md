# Changelog

## [2.3.0] - 2026-06-09

- Added `EventManagerExt`: `MonoBehaviour.StartListening()` extension that auto-unsubscribes on `OnDisable` and fully cleans up on `OnDestroy`.
- Added `EventListenerComponent`: internal `MonoBehaviour` that tracks registered listeners and re-subscribes on `OnEnable`.
