---
description: Step 2 - Design data layer (CSV config + PlayerData)
---

# Step 2: Design Data Layer

This step combines CSV Design and User Data rules.

---

## Part A: CSV Config Data

### CSV File Rules
- **Column Names**: Use `snake_case` (e.g., `tokens_required`)
- **File Naming**: Match Collection class name without "Collection" suffix
  - *Example*: `SpeedFeastRaceRoundCollection` → `SpeedFeastRaceRound.csv`
- **Location**: `Assets/_Project/Features/<Domain>/<FeatureName>/CsvConfig/` (co-located with the feature)

### C# Model & Collection Rules
- Inherit from `ScriptableObject`
- Use `dataGroups` for root data field
- Use **array** for multiple elements, single object otherwise
- Do NOT use `[CreateAssetMenu]` attribute (loaded automatically by `com.ezg.csv-reader`)
- Expose the collection on `DataManager` and read it via `DataManager.<CollectionName>` (no `GetConfig<T>()`)

### Example
```csharp
// Model
[Serializable]
public class YourFeatureModel
{
    public int id;
    public string name;
    public int cost;
}

// Collection
public class YourFeatureCollection : ScriptableObject
{
    public YourFeatureModel[] dataGroups;
}
```

---

## Part B: Player Data (User Progress)

### Implementation Rules
- Inherit from `DataPlayerBaseGeneric<T>`
- Only handle **validation** and basic property access
- Do NOT implement business logic

### Example
```csharp
// Data Model
[Serializable]
public class YourFeatureDataModel
{
    public int currentLevel;
    public int totalScore;
}

// Player Data Class
public class YourFeaturePlayerData : DataPlayerBaseGeneric<YourFeatureDataModel>
{
    // Validation logic only
}
```

### Registration in PlayerDataManager.cs
`Assets/_Project/Features/_Shared/GameData/PlayerDataManager.cs` — match the existing property style:
```csharp
public static YourFeaturePlayerData _yourFeaturePlayerData;
public static YourFeaturePlayerData YourFeaturePlayerData
{
    get { return _yourFeaturePlayerData ??= DataPlayer.GetModule<YourFeaturePlayerData>(); }
    set => _yourFeaturePlayerData = value;
}
```

### Access Pattern
```csharp
PlayerDataManager.YourFeaturePlayerData   // always use this from feature code
```

---

## Checklist
- [ ] Create CSV file with snake_case headers
- [ ] Create Model class (CamelCase)
- [ ] Create Collection class with `dataGroups`
- [ ] Create PlayerData model
- [ ] Create PlayerData class inheriting `DataPlayerBaseGeneric<T>`
- [ ] Register in `PlayerDataManager.cs`
