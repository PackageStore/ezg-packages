---
name: game-systems
description: Guide for using GameSystems module - utility functions for UI, messages, scene management, and common game operations.
---

# GameSystems Module

Static utility class providing common game operations including UI popups, notifications, scene transitions, and helper functions.

## File Location
`Assets/_Project/Features/_Shared/Systems/GameSystems.cs`

---

## Quick Reference

### Message & Notification

```csharp
// Simple floating notification (auto-localizes if key contains '_')
GameSystems.ShowSimpleMessage("purchase_success");
GameSystems.ShowSimpleMessage("Custom text without localization");

// Not enough resource notification
GameSystems.ShowMessageNotEnoughResource(resource);

// Simple OK dialog
GameSystems.ShowMessage("message_key", "Optional Title");

// Confirmation dialog with callbacks
GameSystems.ShowMessage(
    text: "confirm_delete",
    title: "Warning",
    acceptAction: () => DeleteItem(),
    cancelAction: () => Debug.Log("Cancelled"),
    focusYes: true,
    showCancelButton: true
);

// Editor-only debug message (yellow color)
GameSystems.ShowSimpleMessageEditor("Debug info");
```

### Reward Popup

```csharp
// Single reward
GameSystems.ShowRewardPopup(reward, onClose: () => Debug.Log("Closed"));

// Multiple rewards
GameSystems.ShowRewardPopup(rewardList, onClose: () => ContinueFlow());
```

### Scene Management

```csharp
// Change scene (auto-manages CancellationTokens)
GameSystems.ChangeScene(GameEnums.Scenes.HomeScene);
GameSystems.ChangeScene(GameEnums.Scenes.BattleScene);
```

### Loading Screen

```csharp
// Show/hide loading screen
GameSystems.ShowWaitingScreen(true);
GameSystems.ShowWaitingScreen(false);

// Check if loading screen is visible
if (GameSystems.IsShowWaitingScreen()) { }
```

### Floating Text (World Space)

```csharp
// Display floating text at world position
GameSystems.ShowFloatingText("+100", transform.position, Color.green);

// With position offset
GameSystems.ShowFloatingText("+50", pos, Color.yellow, Vector3.up * 2f);
```

### Localization

```csharp
// Get localized string
string text = GameSystems.Localize("button_ok");
string text = GameSystems.Localize("item_name", LocalizeCategory.Items);
```

### String Formatting

```csharp
// Format with float params (max 5)
string result = GameSystems.FormatString("Value: {0}", new float[] { 100f });

// Format with object params (max 5)
string result = GameSystems.FormatString("{0} x{1}", new object[] { "Gold", 50 });
```

### Input Control

```csharp
// Enable/disable all input
GameSystems.EnableTouch(false); // Disable
GameSystems.EnableTouch(true);  // Enable
```

### Internet Connectivity

```csharp
// Check internet connection
if (GameSystems.IsInternetConnection())
{
    // Online logic
}

// Start periodic internet check (shows popup if disconnected)
GameSystems.CheckInternet();
```

### Animation Helpers

```csharp
// Move transform with AnimationCurve
StartCoroutine(GameSystems.AnimMove(
    trans: transform,
    targetPos: targetPosition,
    duration: 0.5f,
    animCurve: curve,
    actionComplete: () => Debug.Log("Done"),
    isLocalMove: true
));

// Auto-hide object after delay
StartCoroutine(GameSystems.AutoHideObject(gameObject, 2f, isRealtime: false));
```

### Platform Utilities

```csharp
// Restart app (Android only)
GameSystems.RestartApplication();

// Check if installed from store
if (GameSystems.IsInstallFromStore()) { }

// Check if premium version
if (GameSystems.IsPremium) { }
```

---

## CancellationToken Management

For UniTask cancellation per scene:

```csharp
// Access scene-specific tokens
var battleToken = GameSystems.BattleCancelToken;
var homeToken = GameSystems.HomeCancelToken;

// Use in async operations
await SomeOperation().AttachExternalCancellation(GameSystems.BattleCancelToken.Token);

// Tokens auto-reset on scene change via ChangeScene()
// Manual init if needed:
GameSystems.InitCancelToken(GameEnums.Scenes.BattleScene);
```

---

## Best Practices

1. **Messages**: Use localization keys (contain `_`) for `ShowSimpleMessage`
2. **Scene Changes**: Always use `ChangeScene()` to properly manage tokens
3. **Loading Screen**: Pair `ShowWaitingScreen(true)` with `ShowWaitingScreen(false)`
4. **Confirmation Dialogs**: Set `focusYes: false` for destructive actions
5. **Debug Messages**: Use `ShowSimpleMessageEditor` - only shows in Unity Editor
