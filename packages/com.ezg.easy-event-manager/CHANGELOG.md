## [2.3.1] - 2026-06-14

- Add missing .meta files for README.md, CHANGELOG.md, package.json to fix Unity "immutable folder" import errors


# Changelog

## [2.3.0] - 2026-06-09

- Added `EventManagerExt`: `MonoBehaviour.StartListening()` extension that auto-unsubscribes on `OnDisable` and fully cleans up on `OnDestroy`.
- Added `EventListenerComponent`: internal `MonoBehaviour` that tracks registered listeners and re-subscribes on `OnEnable`.