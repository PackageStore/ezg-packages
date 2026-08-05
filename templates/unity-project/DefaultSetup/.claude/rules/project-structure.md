# PROJECT STRUCTURE & PLACEMENT

Tree is regular — learn the rule, not the tree. Root: `Assets/_Project/`.
**Procedure:** 1) pick domain bucket → 2) `Features/{Domain}/{Feature}/` → 3) place by role → 4) unsure? `codegraph_explore` an existing sibling feature & mirror it exactly. New feature/screen → `/new-feature`,`/new-ui` (never hand-author the tree).

**Buckets `Features/{Domain}/`:**
- **Framework-standard (shipped by this template):** `_Shared` = cross-cutting frameworks ONLY (UI, GameData, Networking, Purchase, Systems/Utils/TimeManager) — NEVER single-feature code. `System` = utility screens (Settings, Admin, Cheat, Tooltip, RewardPopup…). `Monetization` = Shop/IAP/offers. `Onboarding` = Splash/loading/language. `Social` = Account/Avatar/Name/GiftCode. `Meta` = out-of-run progression + home hub (currently `HomeScene`).
- **Gameplay buckets are per-project — this template ships NONE.** Create one bucket per major gameplay domain (e.g. `Gameplay`, `Combat`, `Puzzle`…) and default new gameplay code into the project's primary gameplay bucket.

**Feature layout `{Feature}/` (mirror any existing feature):**
- `Scripts/Controller/` → `Screen{X}Controller.cs`,`{X}View.cs` (extend `FeatureBaseController`)
- `Scripts/Data/` → `Player{X}.cs`+`Player{X}Data.cs` (extend `DataPlayerBase`, via `PlayerDataManager`)
- `Scripts/Service/` → `{X}Service.cs` (logic). `Scripts/Config/` → `{X}ConfigModel.cs`+`{X}ConfigCollection.cs` (CSV→model). `Scripts/Events/` → `{X}EventName.cs`.
- `CsvConfig/`={X}Config.csv · `Resources/`=prefab+SO · `Visuals/`=art. Create only slots you need; keep the names.

**Lookup:** UI→`/new-ui`(Controller+Resources) · save field→`Data/`(+`SetupDefaultData()`) · logic→`Service/` · table→`CsvConfig/`+`Config/` · event→`Events/` · helper→check `_Shared/Systems/Utils.cs` first · editor tool→`Assets/_Project/Editor/`(`#if UNITY_EDITOR`) · whole feature→`/new-feature`.

**Never:** feature code in `_Shared/` · invent a new `Scripts/` sub-folder when `Controller/Data/Service/Config/Events` fits · hand-make a tree `/new-feature`/`/new-ui` would scaffold.
