---
name: time-manager
description: Guide for using TimeManager module for time operations, period checks, and online time synchronization.
---

# TimeManager Module

`TimeManager` is a static utility class for all time-related operations. It provides local time management, period transition detection, and online time synchronization.

## Quick Reference

| Method | Purpose |
|--------|---------|
| `GetNow()` | Get current Unix timestamp (seconds) |
| `GetMillisecondsNow()` | Get current Unix timestamp (milliseconds) |
| `GetOnlineNow()` | Get synchronized server time |
| `IsNextDay(long)` | Check if day has changed |
| `IsNextWeek(long)` | Check if week has changed |
| `IsNextMonth(long)` | Check if month has changed |

---

## 1. Getting Current Time

```csharp
// Get current Unix timestamp (seconds)
long now = TimeManager.GetNow();

// Get current Unix timestamp (milliseconds)
long nowMs = TimeManager.GetMillisecondsNow();

// Convert Unix to DateTimeOffset
DateTimeOffset dateTime = TimeManager.GetTimeFromUnix(now);
```

> [!IMPORTANT]
> Always use `TimeManager.GetNow()` instead of `DateTime.Now` for consistency.

---

## 2. Period Transition Checks

Check if a time period has passed since a saved timestamp:

```csharp
long lastLogin = PlayerDataManager.PlayerData.LastLoginTime;

// Check day change
if (TimeManager.IsNextDay(lastLogin))
{
    // Reset daily rewards, quests, etc.
}

// Check week change  
if (TimeManager.IsNextWeek(lastLogin))
{
    // Reset weekly content
}

// Check month change
if (TimeManager.IsNextMonth(lastLogin))
{
    // Reset monthly content
}
```

---

## 3. Get Remaining Time

```csharp
// Seconds until next day/week/month
int toNextDay = TimeManager.GetRemainingTimeToNextDay();
int toNextWeek = TimeManager.GetRemainingTimeToNextWeek();
int toNextMonth = TimeManager.GetRemainingTimeToNextMonth();

// Format countdown string
string countdown = TimeManager.GetRemainingTimeToString(seconds);
// Output: "12:34:56" or "2d 05h 30m"
```

---

## 4. Event Registration

Register callbacks for automatic period transitions:

```csharp
void OnEnable()
{
    TimeManager.RegEventNextDay(OnNextDay);
    TimeManager.RegEventNextWeek(OnNextWeek);
    TimeManager.RegEventMonthWeek(OnNextMonth);
}

void OnNextDay()
{
    // Handle daily reset
}
```

You can also listen via EventManager:
```csharp
EventManager.StartListening(nameof(EventName.NextDayEvent), OnNextDay);
EventManager.StartListening(nameof(EventName.NextWeekEvent), OnNextWeek);
EventManager.StartListening(nameof(EventName.NextMonthEvent), OnNextMonth);
```

---

## 5. Hour-Based Operations

```csharp
// Get seconds until specific hour (e.g., 12:00 UTC)
long secondsToNoon = TimeManager.GetNextHour(12);

// Check if hour has passed today
bool isAfternoon = TimeManager.IsPassHour(12);
```

---

## 6. Online Time Synchronization

For anti-cheat and server-verified operations:

```csharp
// Sync with server time
await TimeManager.GetOnlineTime(
    onSuccess: () => Debug.Log("Time synced!"),
    onFailed: () => Debug.LogWarning("Sync failed")
);

// Check sync status
if (TimeManager.IsOnline == true)
{
    // Use synchronized time
    long serverTime = TimeManager.GetOnlineNow();
    DayOfWeek serverDay = TimeManager.GetOnlineDayOfWeek();
    int dayOfMonth = TimeManager.GetOnlineDayOfMonth();
}
```

### Target Day Calculations (Online)

```csharp
// Get timestamp for next Monday (end of day)
long nextMonday = TimeManager.GetOnlineTimeToTargetDayOfWeek(
    DayOfWeek.Monday, 
    isGetEndDay: true
);

// Get timestamp for 15th of current month
long day15 = TimeManager.GetOnlineTimeToTargetDayOfMonth(15, isGetEndDay: false);
```

---

## 7. Bonus Time

For testing or special events:

```csharp
// Add 1 hour bonus to current time
TimeManager.BonusTimeNow = 3600;

// Reset bonus
TimeManager.BonusTimeNow = 0;
```

---

## Common Patterns

### Save & Check Daily Reset

```csharp
// On action completion
PlayerData.LastActionTime = TimeManager.GetNow();

// On game load
if (TimeManager.IsNextDay(PlayerData.LastActionTime))
{
    ResetDailyData();
}
```

### Cooldown Timer

```csharp
// Set cooldown end time
long cooldownEnd = TimeManager.GetNow() + COOLDOWN_SECONDS;

// Check remaining cooldown
long remaining = TimeManager.GetTotalSecondRemain(cooldownEnd);
if (remaining <= 0)
{
    // Cooldown finished
}
```

### Event Duration Check

```csharp
// Check if event is still active
long eventEnd = eventConfig.EndTime;
if (TimeManager.GetNow() < eventEnd)
{
    // Event is active
    string timeLeft = TimeManager.GetRemainingTimeToString(eventEnd - TimeManager.GetNow());
}
```
