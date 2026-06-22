---
name: core-systems
description: Detailed reference for core project systems like Utils, TimeManager, and UIManager.
---

# Core Systems Reference

## When to Use This Skill
- **Delay Execution**: Use `Utils.Delay()` or `Utils.DelayRealTime()` instead of Coroutines.
- **Formatting**: Use `Utils.MoneyConvert()` for currency display (1.5k, 1M).
- **Probability**: Use `Utils.IsSuccessWithRate()` or `Utils.GetIndexInRate()`.
- **Targeting**: Use `Utils.GetNearestTarget()` or `Utils.GetRandomTarget()`.
- **Time/Dates**: Use `TimeManager` for all time checks (daily/weekly resets).
- **UI Management**: Use `UIManager` to Show/Close features.

## 1. Utils - Extension & Helper Methods
**Location**: `Assets\_Project\Features\_Shared\Systems\Utils.cs`

### Common Use Cases

#### Delay Execution
Prefer `Utils.Delay` over `StartCoroutine` for simple delays.
```csharp
// Delay 1.5 seconds (scaled time)
Utils.Delay(1.5f, () => {
    Debug.Log("Executed after 1.5s");
});

// Delay realtime (unaffected by timeScale)
Utils.DelayRealTime(1.5f, () => {
    Debug.Log("Executed even if game paused");
});
```

#### Currency Formatting
```csharp
long gold = 1500000;
string text = gold.MoneyConvert(); // Returns "1.5M"
```

#### Probability & Random
```csharp
// 30% chance success
if (0.3f.IsSuccessWithRate()) { /* Success */ }

// Weighted random selection
var weights = new List<float> { 50f, 30f, 20f }; // 50%, 30%, 20%
int index = weights.GetIndexInRate();
```

#### Target Selection
Efficiently find targets using `Physics2D.OverlapCircleNonAlloc` under the hood.
```csharp
// Cache buffer to avoid GC
Collider2D[] _hitBuffer = new Collider2D[20];

// Get nearest enemy
Transform nearest = transform.GetNearestTarget(
    radius: 10f, 
    layer: LayerMask.NameToLayer("Enemy"), 
    hitColliders: _hitBuffer
);
```

#### Circular Positioning
```csharp
// Get position on a circle boundary
Vector3 pos = Utils.GetPosInCircle(centerPos, radius: 5f, angle: 45f);
```

#### Deep Copy
```csharp
// Deep copy any object using JSON serialization
var newData = oldData.CloneJson();
```

## 2. TimeManager - Time & Date Operations
**Location**: `Assets\_Project\Features\_Shared\Systems\TimeManager.cs`

### Common Use Cases

#### Get Current Time
Always use `TimeManager.GetNow()` instead of `DateTime.Now` to sync with server time.
```csharp
long currentUnixTime = TimeManager.GetNow();
```

#### Check Daily Reset
Check if the current time has passed the daily reset time compared to a saved timestamp.
```csharp
if (TimeManager.IsNextDay(lastClaimTime)) {
    // Reset daily reward
    lastClaimTime = TimeManager.GetNow();
}
```

#### Countdown Timer
```csharp
int secondsLeft = TimeManager.GetRemainingTimeToNextDay();
string display = TimeManager.GetRemainingTimeToString(secondsLeft); // "hh:mm:ss"
```

#### Events
```csharp
// Subscribe to new day event
TimeManager.RegEventNextDay(() => {
    RefreshShop();
});
```

## 3. UIManager - UI System Management
**Location**: `Assets\_Project\Features\_Shared\UI\Framework\UIManager.cs`

### Common Use Cases

#### Show Feature
```csharp
// Simple show
UIManager.Instance.Show(GameEnums.Features.Shop);

// Show with data (passed to LoadData method of controller)
UIManager.Instance.Show(GameEnums.Features.ItemDetail, data: itemInfo);

// Async show
await UIManager.Instance.Show(GameEnums.Features.Leaderboard, isAsync: true);
```

#### Close Feature
```csharp
// Close specific feature
UIManager.Instance.CloseFeature(GameEnums.Features.Shop);

// Close the top-most feature (often used for Back button)
UIManager.Instance.CloseLastestUI();
```

#### Feature Sequence
Queue features to show one after another (e.g., login popups).
```csharp
UIManager.Instance.AddFeatureSequense(GameEnums.Scenes.HomeScene, GameEnums.Features.DailyLogin);
UIManager.Instance.AddFeatureSequense(GameEnums.Scenes.HomeScene, GameEnums.Features.Offer);
```

#### Interaction Control
```csharp
// Block user input
UIManager.EnableTouch(false);
```
