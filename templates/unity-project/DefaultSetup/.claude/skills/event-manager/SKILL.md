---
name: event-manager
description: Guide for using TigerForge EasyEventManager - the event system for loose coupling between components
---

# EasyEventManager Skill

## Overview
`TigerForge.EventManager` is the core event system used for loose coupling between components. Events are identified by string names defined in `EventName` partial class files.

## Event Naming Convention

Event names are defined in `EventName` partial class files:
- Location: `Assets/_Project/Features/**/EventName.cs`
- Use `const string` for events with data, `bool` for simple triggers

```csharp
// Example: Assets/_Project/Features/_Shared/Systems/SystemEventName.cs
public static partial class EventName
{
    public const string NextDayEvent = "NextDayEvent";  // For events with data
    public static bool OnChangeScene;                    // For simple trigger events
}
```

## Core Usage Patterns

### 1. Start Listening (OnEnable)
```csharp
private void OnEnable()
{
    EventManager.StartListening(nameof(EventName.OnChangeScene), OnSceneChanged);
    EventManager.StartListening(nameof(EventName.UpdateResource), OnResourceUpdated);
}
```

### 2. Stop Listening (OnDisable)
```csharp
private void OnDisable()
{
    EventManager.StopListening(nameof(EventName.OnChangeScene), OnSceneChanged);
    EventManager.StopListening(nameof(EventName.UpdateResource), OnResourceUpdated);
}
```

### 3. Emit Event (Simple)
```csharp
EventManager.EmitEvent(nameof(EventName.OnChangeScene));

// Emit after a delay (seconds)
EventManager.EmitEvent(nameof(EventName.OnChangeScene), 0.5f);
```

### 4. Emit Event with Data
```csharp
// Single data
EventManager.SetData(nameof(EventName.UpdateResource), 100);
EventManager.EmitEvent(nameof(EventName.UpdateResource));

// Or use shorthand (optional trailing delay in seconds)
EventManager.EmitEventData(nameof(EventName.UpdateResource), myData);
EventManager.EmitEventData(nameof(EventName.UpdateResource), myData, 0.5f);
```

### 5. Emit Event with Multiple Data (DataGroup)
```csharp
// Set and emit multiple values
EventManager.EmitEventDataGroup("MyEvent", value1, value2, value3);

// Retrieve in handler — each element exposes typed cast helpers
var data = EventManager.GetDataGroup("MyEvent");
int val1 = data[0].ToInt();
string val2 = data[1].ToString();
bool val3 = data[2].ToBool();
float val4 = data[3].ToFloat();
MyClass obj = data[0].ToData<MyClass>();   // custom type
```

## Data Retrieval Methods

| Method | Return Type | Default |
|--------|-------------|---------|
| `GetData(eventName)` | `object` | `null` |
| `GetData<T>(eventName)` | `T` | `default(T)` |
| `GetInt(eventName)` | `int` | `0` |
| `GetFloat(eventName)` | `float` | `0f` |
| `GetBool(eventName)` | `bool` | `false` |
| `GetString(eventName)` | `string` | `""` |
| `GetVector2(eventName)` | `Vector2` | `default` |
| `GetGameObject(eventName)` | `GameObject` | `null` |
| `GetSender(eventName)` | `object` | `null` |

## Utility Methods

```csharp
// Check if event exists
bool exists = EventManager.EventExists("MyEvent");

// Check if any listener active
bool listening = EventManager.IsListening();

// Pause/Resume specific event
EventManager.PauseListening("MyEvent");
EventManager.RestartListening("MyEvent");

// Clear event data (listeners continue)
EventManager.Dispose("MyEvent");
EventManager.DisposeAll();

// Stop all listeners
EventManager.StopAll();
```

## EventsGroup (Batch Management)

```csharp
private EventsGroup _eventGroup = new EventsGroup();

private void OnEnable()
{
    _eventGroup.Add("Event1", OnEvent1);
    _eventGroup.Add("Event2", OnEvent2);
    _eventGroup.StartListening();
}

private void OnDisable()
{
    _eventGroup.StopListening();
}
```

## Best Practices

1. **Always pair StartListening/StopListening** in `OnEnable`/`OnDisable`
2. **Use `nameof()` for bool EventNames** to avoid typos
3. **Use const string for events requiring data**
4. **Call `Dispose()` to clean up data** when no longer needed
5. **Define new events** in appropriate `EventName` partial class file
6. **Avoid event loops** — never let event A emit event B that re-emits A (infinite loop)
