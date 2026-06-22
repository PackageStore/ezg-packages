---
description: Master workflow for developing a complete feature from start to finish.
---
# Feature Development Workflow

This is the **master workflow** for creating a new feature. Follow these steps in order.

---

## Step 1: Create Folder Structure
**Reference**: `/ui-features-rule`

Create the feature folder in `Assets/_Project/Features/<Domain>/` (Domain ∈ `Meta, Monetization, Onboarding, Social, System, Events, Gameplay`; most UI screens go under `Meta`) with this structure:
```
FeatureName/
├── Scripts/
│   ├── Controller/   # FeatureBaseController + static Service classes
│   └── Data/         # PlayerData, ScriptableObject configs
├── CsvConfig/        # Feature CSV configs (if any)
├── Visuals/          # Art assets
└── Resources/        # Prefabs, screens
```

---

## Step 2: Design CSV Data
**Reference**: `/csv-design`

1. Define CSV with `snake_case` headers.
2. Place CSV in `Assets/_Project/Features/<Domain>/<FeatureName>/CsvConfig/`.
3. Create C# Model & Collection classes (`camelCase`).
4. Use arrays for multi-element fields, single fields otherwise.
5. Do NOT write custom load functions or use `[CreateAssetMenu]`.

---

## Step 3: Create User Data Class
**Reference**: `/user-data-rule`

1. Create PlayerData class in `Scripts/Data/`.
2. Inherit from `DataPlayerBaseGeneric<T>`.
3. Only implement **CRUD validation logic** — no business logic.
4. **Register** the new data class in `PlayerDataManager.cs`.

```csharp
public class YourPlayerData : DataPlayerBaseGeneric<YourDataModel>
{
    // CRUD validation only
}
```

---

## Step 4: Review Dependency Rules
**Reference**: `/follow-code-rule`

Before writing Service logic, remember:
```
Service → PlayerData ✅
Service → DataManager ✅
PlayerData → Service ❌ (NOT ALLOWED)
```

---

## Step 5: Design Patterns & De-coupling
**Reference**: `/design-pattern-rule`

Before implementing Service or UI logic, review performance and coupling (core patterns are now `com.ezg.*` UPM packages):
1. **Pooling**: Use `com.ezg.pooling` — `PoolingManager.Instance`.
2. **Factory**: Use `com.ezg.factory` / `com.ezg.instance-factory`.
3. **Singleton**: Inherit `Singleton<T>` from `com.ezg.singleton`.
4. **Communication**: Use the TigerForge `EventManager` (`com.ezg.easy-event-manager`) instead of Singletons where possible to reduce coupling.

---

## Step 6: Create Service Class
**Reference**: `/service-rule`

1. Create Service class in `Scripts/Controller/`.
2. Service must be **static**.
3. Access data via `PlayerDataManager.<Module>` and `DataManager.<CollectionName>`.

```csharp
public static class YourFeatureService
{
    public static void DoSomething()
    {
        var playerData = PlayerDataManager.YourPlayerData;
        var configData = DataManager.YourCollection;
    }
}
```

---

## Step 7: Use Utils & Constants
**Reference**: `/utils-constants-rule`

When implementing Service logic:
- Store resource paths in `PathUtils` (`_Shared/Systems/PathUtils.cs`).
- Define game constants feature-local (`SCREAMING_SNAKE_CASE`); there is no central `GameConstants` class.

```csharp
var path = PathUtils.PathPrefabVFX + "MyPrefab";
if (level >= MAX_LEVEL) { }
```

---

## Checklist Summary

| Step | Action | Reference |
|------|--------|-----------|
| 1 | Create folder structure | `/ui-features-rule` |
| 2 | Design CSV & C# models | `/csv-design` |
| 3 | Create PlayerData & Register | `/user-data-rule` |
| 4 | Review dependency rules | `/follow-code-rule` |
| 5 | Apply Design Patterns | `/design-pattern-rule` |
| 6 | Create Service class | `/service-rule` |
| 7 | Use Utils & Constants | `/utils-constants-rule` |
