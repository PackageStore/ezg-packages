---
name: ui-cooldown-time-view
description: Display countdown timers (cooldowns) on the UI via UI_CooldownTimeView. Use for event end-times, IAP pack timers, or fixed daily/weekly/monthly rollovers shown on a UI Text.
---

# UI_CooldownTimeView

`UI_CooldownTimeView` (`Assets/_Project/Features/_Shared/UI/UI_CooldownTimeView.cs`, namespace `Ezg.Core.UI`) is a Unity component that renders a countdown onto a legacy UI `Text`. Common in event screens and IAP store popups to show when a feature/pack ends.

Two usage modes (set via the `_cooldownType` enum in the Inspector, **Cấu hình** tab):
1. **Automatic** rollover — `NextDay`, `NextWeek`, `NextMonth`. No code needed.
2. **Custom** — `Custom`, driven by a target time passed from code.

## Inspector Setup

The script grabs a `Text` via `GetComponentInChildren<Text>(true)` in `Awake()`, so put this component on the parent and a `Text` anywhere beneath it.

Inspector fields (Cấu hình tab):
- `_cooldownType` — `NextDay` / `NextWeek` / `NextMonth` / `Custom`.
- `_showCustom` — when `true`, formats as `dd\d hh\h` / `hh\h mm\m` instead of `TimeManager.GetRemainingTimeToString`.

## 1. Automatic (NextDay / NextWeek / NextMonth)

No initialization code. Set `_cooldownType` to a non-`Custom` value. On `OnEnable` the component runs a coroutine that refreshes the text every second using:
- `TimeManager.GetRemainingTimeToNextDay()`
- `TimeManager.GetRemainingTimeToNextWeek()`
- `TimeManager.GetRemainingTimeToNextMonth()`

## 2. Custom target time (features/packs)

Set `_cooldownType` to `Custom`, reference the component, and call `InitCustomCooldown`:

```csharp
using Ezg.Core.UI;
using Sirenix.OdinInspector;
using UnityEngine;
using System;

public class MyFeatureController : FeatureBaseController
{
    [SerializeField] [TabGroup("Cấu hình")] [Required]
    private UI_CooldownTimeView _cooldownTime;

    protected override void LoadData()
    {
        base.LoadData();

        // End time as Unix seconds (online clock)
        long endTime = MyFeatureManager.GetEndTime();

        // Auto-close this popup when the countdown reaches 0
        _cooldownTime.InitCustomCooldown(endTime, () => CloseMe());
    }
}
```

### `InitCustomCooldown(long endTime, UnityAction callbackEndAction = null)`
- `endTime` — target time on the **online** clock. The component computes the remaining time as `endTime - TimeManager.GetOnlineNow()` and stops at 0.
- `callbackEndAction` — invoked once when the countdown hits 0. Ideal for auto-closing a popup or reloading the next data loop.

## Notes

- No manual `HH:mm:ss` formatting needed — the component uses `TimeManager.GetRemainingTimeToString` (or the `dd/hh/mm` short form when `_showCustom` is on).
- Bound to legacy UI `Text` only. If a screen migrates to TextMeshPro, `Awake()` in `UI_CooldownTimeView` would need updating.
- Time comes from the synced online clock (`TimeManager.GetOnlineNow()`), not `DateTime.Now` — consistent with the project's time rules.
