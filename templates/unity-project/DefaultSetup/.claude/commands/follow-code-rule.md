---
description: Dependency rules between Service and PlayerData classes.
---
# Follow Code Rule

## Dependency Direction
```
Service → PlayerData ✅
Service → DataManager ✅
PlayerData → Service ❌ (NOT ALLOWED)
```

## Rules
1. **Service CAN call**:
   - `PlayerDataManager.<Module>` to access player data (preferred over raw `DataPlayer.GetModule<T>()`).
   - `DataManager.<CollectionName>` to access config data.

2. **PlayerData CANNOT call**:
   - Any Service class.
   - PlayerData should only handle data validation/CRUD.

## Rationale
This keeps PlayerData as a pure data layer without business logic dependencies.
