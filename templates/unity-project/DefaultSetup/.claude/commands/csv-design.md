---
description: Rules for designing CSV files and their corresponding C# models/collections.
---
# CSV Design Workflow

Follow these rules when creating or modifying CSV files and their data models.

## CSV File Rules
- **Column Names**: Use `snake_case` (e.g., `tokens_required`).
- **File Naming**: The CSV filename MUST match the Collection class name without the "Collection" suffix.
  - *Example*: `SpeedFeastRaceRoundCollection` class -> `SpeedFeastRaceRound.csv`.
- **No Comments**: Do NOT include comments or metadata rows in the CSV file itself.
- **Location**: Place CSV files co-located with the feature, in `Assets/_Project/Features/<Domain>/<FeatureName>/CsvConfig/` (e.g. `Features/Gameplay/CsvConfig/`, `Features/Meta/Inventory/CsvConfig/`). Domains: `Gameplay, Meta, Monetization, Onboarding, Social, System, Events, _Shared`.

## C# Model & Collection Rules
- **Naming Convention**: 
  - Use `CamelCase` for both Collection and Model classes.
- **Data Structure**:
  -	Inherit from `ScriptableObject`
  - Use the field name `dataGroups` for the root data in the Collection class.
  - If a field contains multiple elements, use an **array**.
  - If a field contains only one element, do **not** use an array.
- **Automatic Loading**: Do NOT write custom load functions. The `com.ezg.csv-reader` package loads CSV automatically.
- **DataManager access**: Expose each collection as a property in `DataManager` and read it via `DataManager.<CollectionName>` (e.g. `DataManager.CookingRecipes`). There is no `DataManager.GetConfig<T>()`.
- **No CreateAssetMenu**: Do NOT use the `[CreateAssetMenu]` attribute on these data classes.

## Workflow Steps
1. Define the CSV structure with `snake_case` headers.
2. Create the C# Data Model class and Collection class using `CamelCase`.
3. Name the root data field `dataGroups` (array for multiple elements, single object otherwise).
4. Place the CSV in `Assets/_Project/Features/<Domain>/<FeatureName>/CsvConfig/` with the correct name.
5. Let `com.ezg.csv-reader` handle the instantiation; expose the collection on `DataManager`.
