# Changelog

## [0.1.1] - 2026-10-14
### Fixed
- Namespace is now `Ezg.Package.CheatGate` (was `Ezg.CheatGate`): from game code in any `Ezg.*` namespace, `CheatGate.X` resolved to the namespace `Ezg.CheatGate` instead of the class and did not compile. 0.1.0 is deprecated.

## [0.1.0] - 2026-10-14
### Added
- `CheatGate`: `Initialize(projectCode)`, `TryUnlock(callback)` (10th tap on the version label), `IsUnlocked`, `Changed`, `DeviceIdDisplay`.
- Checks the phone against the `ezg-cheat-gate` Worker only on the 10th tap, and at launch / return to the foreground only on phones that hold a grant (at most every 30 minutes). A listed phone keeps an RSA-signed grant for 7 days offline; a phone taken off the list loses it at its next online re-check.
- Every check carries a random nonce that the grant must echo: a recorded answer replayed by a proxy is refused (`InvalidGrant`).
- A grant received during a run lasts its own duration from arrival on the phone's clock (skewed test clocks work, no endless session).
- `CheatGateCore`: the state machine without UnityEngine (storage, transport and clock injected).
- Device id: first 12 hex digits of SHA-256("ezg-cheat-gate:" + `SystemInfo.deviceUniqueIdentifier`), shown as XXXX-XXXX-XXXX.
- EditMode tests with grants signed by the Worker's own code.
