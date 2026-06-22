---
name: csv-config
description: Guide for configuring CSV files and mapping them to C# models
---

# CSV Configuration Guide

This skill provides detailed instructions on how to configure CSV files for game data and how the system reads and maps CSV data to C# models.

## Core Concepts

### CSV Naming Convention
- **Header row**: Use `snake_case` for column names (e.g., `group_id`, `res_type`)
- **CsvReader** auto-converts `snake_case` → `camelCase` to match C# field names
- Example: `group_id` → `groupId`, `res_type` → `resType`

### Primitive Types Supported
| Type | CSV Example | Notes |
|------|------------|-------|
| `int` | `100` | Integer values |
| `float` | `1.5` | Decimal values |
| `string` | `text` | Text values |
| `bool` | `TRUE` / `FALSE` | Case-insensitive |
| `enum` | `Money` | Must match enum value name exactly |
| `int[]` | `1,2,3` or `1\|2\|3` | Comma or pipe separated |

---

## Resource Configuration

### Resource Class Structure
File: [Resource.cs](Packages/com.ezg.core/Runtime/Models/Resource.cs)

```csharp
public class Resource
{
    public int resType;     // Resource category (int const, see ResourceTypes)
    public int resId;       // Specific ID within the type
    public long resNumber;  // Quantity
}
```

### ResourceTypes (int constants)
File: [EnumBase.Shared.cs](Packages/com.ezg.core/Runtime/Utils/EnumBase.Shared.cs#L454-L489)

`ResourceTypes` is a `static class` of `const int` values (no longer an enum):

| Value | ID | Description |
|-------|-----|-------------|
| `None` | 0 | No resource |
| `Money` | 1 | Currency (see MoneyTypes) |
| `Item` | 2 | Game items |
| `Feature` | 6 | Features |
| `Package` | 7 | Packages |
| `Level` | 10 | Level |

### MoneyTypes Enum (when `resType` = `Money`)
File: [EnumBase.Shared.cs](Packages/com.ezg.core/Runtime/Utils/EnumBase.Shared.cs#L346-L372)

| Value | ID | Description |
|-------|-----|-------------|
| `None` | 0 | No money type |
| `Gold` | 1 | Gold coins |
| `Diamonds` | 2 | Premium diamonds |
| `Energy` | 3 | Energy/stamina |
| `InfinityEnergy` | 4 | Infinite energy |
| `Exp` | 5 | Experience |
| `SkipTime` | 6 | Skip time |
| `Star` | 7 | Stars |
| `Exchange` | 8 | Exchange |
| `AddSlotInventory` | 9 | Add inventory slot |
| `CurrencyInfinityPack` | 10 | Infinity pack currency |
| `Ads` | 1000 | Watch ads |
| `Cash` | 1001 | Real money (IAP) |

### CSV Resource Columns
When configuring rewards/resources, use these columns:

```csv
res_type,res_id,res_number
1,1,100
1,2,50
2,1,1
```

- `res_type`: Use the `ResourceTypes` int value (e.g., `1` for Money, `2` for Item)
- `res_id`: For `Money` type, use `MoneyTypes` ID. For others, use specific item ID
- `res_number`: Quantity to give

---

## Nested Array Pattern

### How CsvReader Handles Nested Arrays
File: [CsvReader.cs](Packages/com.ezg.csv-reader/Runtime/CsvReader.cs)

The system identifies array grouping by checking the **first column of the parent object**:
- When the parent column is **empty**, the row belongs to the previous parent
- When the parent column has a **value**, it starts a new parent object

### Example: LuckySpin
**Model Structure:**
```csharp
public struct LuckySpinModel
{
    public int groupId;                    // Parent ID (first column)
    public LuckySpinDetailModel[] details; // Nested array
}

public class LuckySpinDetailModel : PackRewards
{
    public float rate;
    // Inherits: resType, resId, resNumber, bonus, stageBonus
}
```

**CSV Structure:**
```csv
group_id,rate,res_type,res_id,res_number,bonus,stage_bonus
0,300,1,1,50,0,0
,150,1,5,1,0,0
,250,1,1,100,0,0
1,300,1,1,50,0,0
,150,1,5,1,0,0
```

**Key Points:**
- Row 2: `group_id=0` starts first group, contains first detail
- Rows 3-4: Empty `group_id` = these details belong to group 0
- Row 5: `group_id=1` starts a new group
- Rows 6+: Empty `group_id` = belong to group 1

---

## Multi-Level Nesting

For models with multiple nested arrays, the same pattern applies:

```csharp
public struct ParentModel
{
    public int id;                    // First column (Level 1)
    public ChildModel[] children;     // Nested array
}

public struct ChildModel
{
    public int childId;               // First column of child (Level 2)
    public GrandchildModel[] items;   // Deeper nesting
}
```

**CSV Pattern:**
```csv
id,child_id,item_id,item_value
1,100,1001,50
,,1002,75
,101,1003,25
2,200,2001,100
```

- `id=1` starts parent 1
- `child_id=100` starts first child of parent 1
- Empty `id` + empty `child_id` = items continue for child 100
- Empty `id` + `child_id=101` = new child under same parent 1
- `id=2` = new parent

---

## Best Practices

### 1. Column Order
- Place parent ID column **first**
- Place nested object columns after parent fields
- Order nested fields by their depth level

### 2. Empty Values
- Use empty string for missing values (not `NULL`)
- System auto-fills default values:
  - `int/float/double/long` → `0`
  - `string` → empty string
  - `bool` → `FALSE`
  - `enum` → first enum value

### 3. Comments
Lines starting with `/` or `//` are ignored by CsvReader.

### 4. Inheritance Pattern
When inheriting from `Resource` or `PackRewards`:

```csharp
public class MyRewardModel : PackRewards
{
    public int customField;
    // Inherits: resType, resId, resNumber, bonus, stageBonus
}
```

CSV must include all inherited columns:
```csv
custom_field,res_type,res_id,res_number,bonus,stage_bonus
10,1,1,100,0,0
```

---

## Quick Reference

### Resource Reward CSV Template
```csv
res_type,res_id,res_number
1,1,100
1,2,50
2,1,1
```

### PackRewards CSV Template (with bonus)
```csv
res_type,res_id,res_number,bonus,stage_bonus
1,1,100,0.5,10
```

### Common Resource Examples
| Description | res_type | res_id | Example |
|-------------|----------|--------|---------|
| 100 Gold | `1` | `1` | `1,1,100` |
| 50 Diamonds | `1` | `2` | `1,2,50` |
| 1 SkipTime | `1` | `6` | `1,6,1` |
| Item ID 101 x3 | `2` | `101` | `2,101,3` |
