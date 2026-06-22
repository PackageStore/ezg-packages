---
description: Create a new Feature structure in the Merge Two project
---

// turbo-all
# Create New Feature Workflow
As a Unity Senior Developer, create a new feature structure following Merge Two conventions. This workflow aligns with `/feature-development` and the `.agents/rules/` — read those for the full rules.

## 1. ARGUMENT PARSING
Parse `{{args}}` with format: `FeatureName: Description`
- If no colon → treat entire `{{args}}` as FeatureName, empty Description.
- FeatureName MUST be **PascalCase**. Description optional.

## 2. DESCRIPTION ANALYSIS
| Priority | Source | Action |
|---|---|---|
| 0th | Implementation Mapping (section `# 10. Implementation Mapping` from `/03-implementation-mapping`) | Parse 10.1–10.7 as structured input: Sub-Features, Player Save Data, Collections, UI Screens, Events, Dependency Graph, Registration Points. |
| 1st | Attached file (image/.md/.txt/.pdf) | Read and analyze as full requirements. |
| 2nd | Text from `{{args}}` | Use as feature spec. |
| 3rd | No description | Create minimal empty structure only. |

## 3. REFERENCE STUDY (before implementing)
- **Base controller:** `Assets/_Project/Features/_Shared/UI/Framework/FeatureBaseController.cs` — coding style, naming, base structure.
- **Conventions:** `.agents/rules/code-style.md`, `core-system.md`, `data-persistence.md`, `third-party.md`.
- **Skill discovery (MANDATORY):** list `.agents/skills/` and, for EVERY technical requirement, read the matching `SKILL.md` before generating code (e.g. `event-manager`, `csv-config`, `pooling-manager`, `currency-preview`, `rewards-service`, `purchase-manager`).

## 4. DIRECTORY STRUCTURE
Create under `Assets/_Project/Features/<Domain>/` (Domain ∈ `Meta, Monetization, Onboarding, Social, System, Events, Gameplay`; most UI screens → `Meta`):
```
[FeatureName]/
├── Scripts/
│   ├── Controller/   # [FeatureName]Controller (FeatureBaseController) + static [FeatureName]Service
│   └── Data/         # [FeatureName]PlayerData, [FeatureName]Model, [FeatureName]Collection
├── CsvConfig/        # [FeatureName].csv (if Model/Collection needed)
├── Visuals/          # Art assets
└── Resources/        # Prefabs / screens
```

## 5. CODE GENERATION
### A. [FeatureName]Controller.cs (Controller/)
- Inherit `FeatureBaseController`. Do NOT call `gameObject.SetActive` for UI — use `UIManager.Instance.Show/Hide`.
- Keep `base.Awake/OnEnable/OnDisable()` in lifecycle overrides. XML `///` docs on public API.
- Match requirements to existing skills; only use `// TODO: [Name] - Desc` for logic with no matching skill.

### B. [FeatureName]Service.cs (Controller/)
- **Static class.** Holds business logic. Access data via `PlayerDataManager.<Module>` and `DataManager.<CollectionName>`.
- Dependency rule: `Service → PlayerData ✅`, `Service → DataManager ✅`, `PlayerData → Service ❌`.

### C. [FeatureName]PlayerData.cs (Data/) — only if persisting player data
- Data model: `[Serializable] class [FeatureName]DataModel { ... }`.
- `public class [FeatureName]PlayerData : DataPlayerBaseGeneric<[FeatureName]DataModel>` — CRUD/validation only, no business logic.
- **Register** in `Assets/_Project/Features/_Shared/GameData/PlayerDataManager.cs`:
  ```csharp
  public static [FeatureName]PlayerData _[featureName]PlayerData;
  public static [FeatureName]PlayerData [FeatureName]PlayerData
  {
      get { return _[featureName]PlayerData ??= DataPlayer.GetModule<[FeatureName]PlayerData>(); }
      set => _[featureName]PlayerData = value;
  }
  ```
- Set new-field defaults in `SetupDefaultData()` so existing users don't crash after update.

### D. [FeatureName]Model.cs + [FeatureName]Collection.cs (Data/) — only if config data needed
- `[Serializable]` Model. Collection : `ScriptableObject` with `public [FeatureName]Model[] dataGroups;`.
- NO `[CreateAssetMenu]`, NO custom load function (auto-loaded by `com.ezg.csv-reader`).
- Expose on the `DataManager` facade (`_Shared/GameData/DataManager.cs`); read via `DataManager.[FeatureName]`.

### E. Events — if the feature emits/listens cross-system
- Use TigerForge `EventManager` with `EventName` constants (read `.agents/skills/event-manager/SKILL.md`). No hardcoded event strings.

## 6. CSV DATA FILE — if Model/Collection exists
📖 MUST READ `.agents/skills/csv-config/SKILL.md`.
- Path: `Assets/_Project/Features/<Domain>/[FeatureName]/CsvConfig/[FeatureName].csv` (file name = Collection name without `Collection`).
- Columns `snake_case`, match Model field order. If reward/cost/resource → include the 6 fields `res_type, res_id, res_number, bonus, stage_bonus, custom_value`.
- Data tables → add all rows; otherwise header + 1 example row.

## 7. UI PREFAB — if the feature needs a screen
Delegate to `/new-ui [FeatureName]` (or the `create-ui` skill). It creates a prefab variant of `screen_template`, attaches the controller, and registers `GameEnums.Features`. Skip for pure data/service features.

## 8. COMPILE CHECK
If any `.cs` file was created/edited, run `/compile-check` before reporting done (see `.agents/rules/compile-validation.md`).

## 9. FINAL CHECKLIST
- [ ] Controller inherits `FeatureBaseController`; no `SetActive` for UI.
- [ ] Service is static; dependency direction respected.
- [ ] (If persisting) PlayerData inherits `DataPlayerBaseGeneric<T>`, registered in `PlayerDataManager.cs`, defaults in `SetupDefaultData()`.
- [ ] (If config) Collection has `dataGroups`, exposed on `DataManager`, CSV in `CsvConfig/`.
- [ ] (If cross-system) events via TigerForge + `EventName` constant.
- [ ] (If UI) `/new-ui [FeatureName]` invoked → screen prefab + `GameEnums.Features` registered.
- [ ] Compile check passed (or skipped with reason).
- [ ] List all `TODO` comments + summarize created files.
