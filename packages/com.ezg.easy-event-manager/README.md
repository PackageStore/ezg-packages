# TigerForge Easy Event Manager

Lightweight static event bus for Unity (namespace `TigerForge`).

## Source mapping

| Package path | Source folder |
|---|---|
| `Runtime/` | `Assets/_Project/3rdParty/TigerForge/EasyEventManager/` |

## Assembly

`TigerForge.EasyEventManager` — no external dependencies; requires only `UnityEngine` and `UnityEngine.Events`.

## Usage

```csharp
// Emit
EventManager.EmitEvent("MyEvent");
EventManager.EmitEventData("MyEvent", myData);

// Listen
EventManager.StartListening("MyEvent", OnMyEvent);

// Auto-unsubscribe extension (added in v2.3.0)
this.StartListening("MyEvent", OnMyEvent);  // unregisters on OnDisable/OnDestroy
```

## Peer requirements

None — the package is self-contained.
