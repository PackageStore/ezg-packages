---
description: Rules for designing static Service classes that handle feature logic.
---
# Service Design Rule

## Overview
A Service is a **static class** that handles shared logic for an entire feature.

## Implementation
- Service classes must be **static**.
- To access player data: `PlayerDataManager.<Module>` (preferred — avoid raw `DataPlayer.GetModule<T>()` in feature code).
- To access config data from CSV: `DataManager.<CollectionName>` (property, e.g. `DataManager.CookingRecipes`).

## Example Structure
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
