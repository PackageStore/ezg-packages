---
description: Step 3 - Implement feature logic (Service, Utils, Constants)
---

# Step 3: Implement Feature Logic

This step combines Service, Follow Code, and Utils & Constants rules.

---

## Part A: Service Class

### Rules
- Service classes must be **static**
- Access player data via `PlayerDataManager.<Module>` (avoid raw `DataPlayer.GetModule<T>()`)
- Access config data via `DataManager.<CollectionName>` (property, not `GetConfig<T>()`)

### Example
```csharp
public static class YourFeatureService
{
    public static void DoSomething()
    {
        var playerData = PlayerDataManager.YourFeaturePlayerData;
        var configData = DataManager.YourFeatureCollection;
        // Logic here
    }
}
```

### Location
Place in `Scripts/Controller/` folder.

---

## Part B: Dependency Rules

### Allowed Dependencies
```
Service → PlayerData ✅
Service → DataManager ✅
PlayerData → Service ❌ (NOT ALLOWED)
```

### Summary
| Layer | Can Call | Cannot Call |
|-------|----------|-------------|
| Service | PlayerData, DataManager | - |
| PlayerData | - | Service |

**Rationale**: PlayerData stays as pure data layer without business logic dependencies.

---

## Part C: Utils & Constants

### PathUtils (`_Shared/Systems/PathUtils.cs`)
Store all **resource paths** here (a `partial class`):
```csharp
public const string PathPrefabFeature = "Prefabs/UI/FeatureName/";
```

### Constants
There is **no** central `GameConstants` class. Define constants feature-local in `SCREAMING_SNAKE_CASE` (no magic numbers):
```csharp
private const int FEATURE_MAX_VALUE = 100;
```
Check `Utils.cs` for existing helpers before adding new ones.

### Usage
```csharp
var path = PathUtils.PathPrefabFeature + "MyPrefab";

if (value >= FEATURE_MAX_VALUE) { }
```

---

## Checklist
- [ ] Create static Service class
- [ ] Implement core feature methods
- [ ] Verify dependency direction (Service → Data, not Data → Service)
- [ ] Add paths to PathUtils (if needed)
- [ ] Add feature-local constants (SCREAMING_SNAKE_CASE, if needed)
- [ ] Connect Service to Screen Controller
