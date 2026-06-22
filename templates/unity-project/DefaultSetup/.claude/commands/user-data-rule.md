---
description: Rules for designing user data classes that persist player progress.
---
# User Data Design Rule

## Overview
User data classes store information that needs to persist during gameplay.

## Implementation
- Inherit from `DataPlayerBaseGeneric<T>` where `T` is your data model.
- Provide a class data model to the generic `T`.
- Do NOT implement deep logic or business logic in this class.
- Code should only handle **validation** or basic property access of the data.
- After creating the data class, you **MUST** register and add it as a property in `PlayerDataManager.cs`.

## Example Structure
```csharp
// Data Model
[Serializable]
public class YourDataModel
{
    public int score;
}

// Player Data Class
public class YourPlayerData : DataPlayerBaseGeneric<YourDataModel>
{
    // Validation logic for data integrity only
}
```

## Registration Pattern
Update `PlayerDataManager.cs` (`Assets/_Project/Features/_Shared/GameData/PlayerDataManager.cs`) to expose the new module — match the existing property style there:
```csharp
public static YourPlayerData _yourPlayerData;
public static YourPlayerData YourPlayerData
{
    get { return _yourPlayerData ??= DataPlayer.GetModule<YourPlayerData>(); }
    set => _yourPlayerData = value;
}
```

## Access Pattern
- Always access via `PlayerDataManager.YourPlayerData` from feature code.
- `DataPlayer.GetModule<YourPlayerData>()` is the internal resolver used *inside* `PlayerDataManager` only — avoid calling it directly elsewhere.
