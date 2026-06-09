# EZG Taptic Feedback (com.ezg.tapticfeedback)

Cross-platform haptic / taptic feedback for iOS and Android.

Packaged from the source module `Assets/_Project/3rdParty/TapticFeedback`.

## Usage

The API is static and lives in the **global namespace** (no `using` required):

```csharp
Taptic.Light();
Taptic.Medium();
Taptic.Heavy();
Taptic.Success();
Taptic.Warning();
Taptic.Failure();
Taptic.Selection();
Taptic.Vibrate();
Taptic.Default();

Taptic.tapticOn = false; // global mute
```

- **iOS:** routed through the native Taptic Engine (`Plugins/iOS/TapticFeedback.mm`); falls back to `Handheld.Vibrate()` on pre-6s devices.
- **Android:** routed through `AndroidTaptic` using the system `Vibrator` (`VibrationEffect` on API 26+, with amplitude patterns). `Plugins/Android/AndroidManifest.xml` declares the `VIBRATE` permission.
- Feedback is suppressed in the Editor (`Application.isEditor`).

## Contents

- `Runtime/Taptic.cs` — public static API.
- `Runtime/AndroidTaptic.cs` — Android vibration implementation + `HapticTypes` enum.
- `Runtime/Plugins/iOS/TapticFeedback.mm` — native iOS plugin.
- `Runtime/Plugins/Android/AndroidManifest.xml` — VIBRATE permission.

## Dependencies

- Scoped registry: **none**.
- Peer requirements: **none** (UnityEngine only).
