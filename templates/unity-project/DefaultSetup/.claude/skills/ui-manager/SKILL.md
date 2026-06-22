---
name: ui-manager
description: UI system management - show/hide/layer control via UIManager. Use when opening/closing feature screens or popups, controlling UI layers/containers, queueing UI sequences, or locking touch input. Never use gameObject.SetActive on feature UI.
---

# UIManager

**Location:** `Assets/_Project/Features/_Shared/UI/Framework/UIManager.cs`
**Namespace:** `Ezg.Feature.Shared` · Features enum: `Ezg.Feature.Shared.Config.GameEnums.Features`

Centralized UI management — handles layering, caching, and feature open/close logic. All feature screens must inherit `FeatureBaseController` (`Assets/_Project/Features/_Shared/UI/Framework/FeatureBaseController.cs`).

## 1. Open UI (Show Feature)

`Show` is `async UniTask<GameObject>`; default group is `Overlay_Container`.

```csharp
// Open with default group (Overlay_Container)
UIManager.Instance.Show(GameEnums.Features.Shop).Forget();

// Open and pass data (loaded via the Feature's LoadData)
UIManager.Instance.Show(GameEnums.Features.HeroDetail, data: heroInfo).Forget();

// Specify a container explicitly
UIManager.Instance.Show(
    GameEnums.Features.BattleHUD,
    grp: UIManager.UIGroupName.Main_Container,
    isAsync: true
).Forget();
```

## 2. Close UI

```csharp
// Close a specific feature
UIManager.Instance.CloseFeature(GameEnums.Features.Shop);

// Close the last opened UI (Back button / Android Escape)
UIManager.Instance.CloseLastestUI(closeWithBackKey: true);
```

## 3. Close-event callbacks

```csharp
// Run an action right after a specific feature closes
UIManager.Instance.AddActionCloseFeature(GameEnums.Features.ChestOpen, () =>
{
    Debug.Log("Chest UI closed!");
});

// Run for ANY feature closing (e.g. refresh red dots at Home)
UIManager.RegisterCloseAnyFeature(() => RefreshNotifications());
```

## 4. UI Sequence

Show UIs one after another (e.g. after Level Up, then Reward).

```csharp
UIManager.Instance.AddFeatureSequense(GameEnums.Scenes.HomeScene, GameEnums.Features.DailyReward);
```

## 5. Input control (touch)

```csharp
UIManager.EnableTouch(false); // lock all UI interaction
UIManager.EnableTouch(true);  // unlock
```

## 6. Containers & Layering

Lowest → highest. Default `Show` group is `Overlay_Container`.

| Container | Purpose |
|-----------|---------|
| `Main_Container` | Main screens (Home, Battle/Gameplay) |
| `Modal_Container` | Popups, dialogs |
| `CurrencyBar_Container` | Shared currency bar, sits above modals |
| `Overlay_Container` | Sales, events, mini-games — above currency bar (**default**) |
| `Tutorial_Container` | Tutorial |
| `Toast_Container` | System alerts (lost connection…), floating messages — top-most |

```csharp
// Current sorting layer order — use to set sorting layer for particles/effects on UI
int currentLayer = UIManager.Instance.GetCurrentLayer();

// Move an already-open feature to another container
UIManager.Instance.MoveToGroup(GameEnums.Features.Sale, UIManager.UIGroupName.Modal_Container);
```

## Warnings

- Always use `UIManager.Instance.Show()` — never `gameObject.SetActive(true)` on feature UI.
- Feature UI classes must inherit `FeatureBaseController` to work with the data + open/close lifecycle.
- `Show` returns `UniTask<GameObject>`; use `.Forget()` when you don't need the handle.
