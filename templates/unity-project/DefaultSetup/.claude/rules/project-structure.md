# PROJECT STRUCTURE & PLACEMENT

Tree is regular — learn the rule, not the tree. Root: `Assets/_Project/`.
**Procedure:** 1) pick domain bucket → 2) `Features/{Domain}/{Feature}/` → 3) place by role → 4) unsure? `codegraph_explore` an existing sibling feature & mirror it exactly. New feature/screen → `/new-feature`,`/new-ui` (never hand-author the tree).

**Buckets `Features/{Domain}/`:**
- **Framework-standard (shipped by this template):** `_Shared` = cross-cutting frameworks ONLY (UI, GameData, Networking, Purchase, Systems/Utils/TimeManager) — NEVER single-feature code. `System` = utility screens (Settings, Admin, Cheat, Tooltip, RewardPopup…). `Monetization` = Shop/IAP/offers. `Onboarding` = Splash/loading/language. `Social` = Account/Avatar/Name/GiftCode. `Meta` = out-of-run progression + home hub (currently `HomeScene`).
- **`Events` = live-ops / time-boxed content** (seasonal event, limited-time challenge, race & leaderboard, login/milestone campaign) — anything that OPENS AND CLOSES on a schedule instead of shipping permanently. One sub-folder per event (`Events/BattleRoyale`, `Events/SpeedFeastRace`…), each a normal feature layout. Cross-event plumbing (base event controller, countdown, shared reward/leaderboard) → `Events/_Shared`, NEVER `Features/_Shared`. Template ships none: install ready-made ones from the EZG Feature Hub catalog, or `/new-feature` with Domain `Events`. Schedule/duration comes from `CsvConfig/` + `TimeManager` (never `DateTime.Now`); an expired event must close cleanly on stale save data, not crash.
- **Gameplay buckets are per-project — this template ships NONE.** Create one bucket per major gameplay domain (e.g. `Gameplay`, `Combat`, `Puzzle`…) and default new gameplay code into the project's primary gameplay bucket.

**Feature layout `{Feature}/` (mirror any existing feature) — `Scripts/` has exactly TWO sub-folders:**
- `Scripts/Controller/` → `Screen{X}Controller.cs`,`{X}View.cs` (extend `FeatureBaseController`) **+ static `{X}Service.cs`** (business logic)
- `Scripts/Data/` → `Player{X}.cs`+`Player{X}Data.cs` (extend `DataPlayerBase`, via `PlayerDataManager`) · `{X}ConfigModel.cs`+`{X}ConfigCollection.cs` (CSV→model, via `DataManager`) · `{X}EventName.cs` (partial `EventName`)
- `CsvConfig/`={X}Config.csv · `Resources/`=prefab+SO · `Visuals/`=art. Create only slots you need; keep the names.

**Lookup:** UI→`/new-ui`(Controller+Resources) · save field→`Data/`(+`SetupDefaultData()`) · logic→`Controller/`(static Service) · table→`CsvConfig/`+`Data/` · event name constant→`Data/`(`{X}EventName.cs`) · seasonal/limited-time content→`Features/Events/{Event}/` · helper→check `_Shared/Systems/Utils.cs` first · editor tool→`Assets/_Project/Editor/`(`#if UNITY_EDITOR`) · whole feature→`/new-feature`.

**Never:** feature code in `_Shared/` (cross-event code → `Events/_Shared/`, not `Features/_Shared/`) · invent a new `Scripts/` sub-folder — `Controller/`+`Data/` is the WHOLE vocabulary, there is no `Scripts/Service/`, `Scripts/Config/` or `Scripts/Events/` (the `Events` **domain bucket** is a different thing — a sibling of `Meta`/`Social`, not a `Scripts/` sub-folder) · hand-make a tree `/new-feature`/`/new-ui` would scaffold.
