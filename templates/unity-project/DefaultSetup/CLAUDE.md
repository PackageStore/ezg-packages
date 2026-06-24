# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Merge Two** — Unity mobile game with merge-grid gameplay, cooking recipes, shop/IAP, events, and progression systems. C# codebase, ~11,000+ scripts. Primary working directory: `Assets/_Project/`.

## Architecture

### Two-Tier Data System

**Runtime config** (read-only game data, CSV-sourced ScriptableObjects):
- Loaded via `DataManager.<CollectionName>` — e.g. `DataManager.ItemMerge`, `DataManager.CookingRecipes`
- Backed by ScriptableObjects loaded from per-feature `Resources/` folders (e.g. `Features/Gameplay/Resources/ItemMerge.asset`); CSV sources live in per-feature `CsvConfig/` folders (e.g. `Features/Gameplay/CsvConfig/`)

**Player data** (persisted, JSON in PlayerPrefs):
- Accessed via `PlayerDataManager.<Module>` — e.g. `PlayerDataManager.Gameplay`, `PlayerDataManager.Inventory`
- Each module extends `DataPlayerBase` and is auto-discovered at runtime via reflection
- Save: `PlayerDataManager.<Module>.Save()` or `PlayerDataManager.SaveAll()`

### ItemMerge Data Model (recently refactored)

`ItemMergeModel` is a **flat struct** — each entry represents one individual item:
```csharp
public struct ItemMergeModel {
    public string id;       // format: ItemKey.ToKeyString() e.g. "700101" (type 7001, sequential id 1)
    public string nameItem;
    public bool canMerge;   // true = has a merge target; false = max level
    public int sellPrice;
    public int sumMerge;
}
```

**ID format:** `MergeEnum.ItemKey.ToKeyString()` = `$"{(int)type:D4}{idItem:D2}"`. For example, `CoffeeIngredient` (7001) item 1 → `"700101"`. First 4 digits = type enum value, remaining digits = sequential item id within type.

`ItemMergeCollection` key methods:
- `GetById(string id)` — look up one item by its string id
- `GetAllByType(MergeEnum.MergeItemTypes type)` — all items of a type, ordered by sequential id
- `GetMergeTargetId(string id)` — returns the next id in the chain (sequential id +1), or `null` if `!canMerge`
- `IsMax(string id)` — `!canMerge`
- `GetItemHighestByType(type)` — returns `ItemSave` for the max-level item (last `!canMerge`)
- `ParseNumericId(string id)` — static helper, extracts the sequential `idItem` part: `"700101"` → `1`

**Bridge to `ItemSave`** (which still uses enum + int for save data):
```csharp
// ItemSave → string id (for ItemMergeCollection lookups)
string id = new MergeEnum.ItemKey(itemSave.itemSaveType, itemSave.idItem).ToKeyString();

// string id → ItemSave
var key = MergeEnum.ItemKey.FromKeyString(id);
new ItemSave { itemSaveType = key.itemSaveType, idItem = key.idItem }
```

### Item Role System

Items on the grid each have a **role** that defines their behavior. Role types: `Generator`, `Tool`, `Disposable`, `Currency`, `Expand`, `Booster`, `Bubble`, etc.

- `ItemMerge` (MonoBehaviour) — the grid item component; holds `SlotBoardData itemData` and `ItemRoleManager role`
- `ItemRoleBase` — abstract base for all roles; subclasses override `OnTap`, `OnSelect`, `OnDropOnTarget`, `CanMerge`, etc.
- `ItemRoleFactory.Get(roleType)` — creates role instances
- Role data comes from `DataItemCacheManager.*Cache` (e.g. `ItemGeneratorCache`, `ItemToolCache`) — keyed by `MergeEnum.ItemKey.ToKeyString()`

### UI Feature System

All screens extend `FeatureBaseController`. Register in `GameEnums.Features` enum. Open via:
```csharp
UIManager.Instance.Show(GameEnums.Features.MyFeature, data: someData).Forget();
```

Six UI layers (lowest to highest): `Main_Container → Modal_Container → CurrencyBar_Container → Overlay_Container → Tutorial_Container → Toast_Container`

### Event Bus

Uses TigerForge `EventManager` (static, string-keyed):
```csharp
EventManager.EmitEvent(EventName.SomeName);
EventManager.EmitEventData(EventName.SomeName, data: myData);
EventManager.StartListening(EventName.SomeName, handler);
EventManager.StopListening(EventName.SomeName, handler);
```

### Async

Use **UniTask** (Cysharp) throughout — never raw coroutines for new code:
```csharp
async UniTask DoSomethingAsync() { ... }
someAsyncTask.Forget(); // fire-and-forget
```

### Grid Service

- `GridService.GetGrid(GridType.Gameplay)` — access the active grid context
- Grid stores items as `Dictionary<Vector2Int, ItemMerge>`
- `ItemPos` ↔ `Vector2Int` via `Utils.ToCellPos(itemPos)` / `Utils.ToItemPos(cellPos)`

## Key Lookup Patterns

```csharp
// Convert ItemSave to string id
string id = new MergeEnum.ItemKey(type, numericId).ToKeyString();  // e.g. "700101"

// Get item data
var item = DataManager.ItemMerge.GetById(id);

// Check if item can merge
bool atMax = DataManager.ItemMerge.IsMax(id);

// Get merge target
string nextId = DataManager.ItemMerge.GetMergeTargetId(id);
int nextNumericId = ItemMergeCollection.ParseNumericId(nextId);  // sequential part only

// Get all items of a type
ItemMergeModel[] items = DataManager.ItemMerge.GetAllByType(MergeEnum.MergeItemTypes.CoffeeIngredient);

// Get item index within its type (used for level display / pricing)
int level = DataManager.ItemMerge.GetIndex(id);

// Item name localization
string name = Utils.GetNameItems(type, numericId);        // no level suffix
string name = Utils.GetNameItems(type, numericId, true);  // with "Lv X" suffix
```

## Important Locations

| What | Where |
|------|-------|
| Item type enum (148+ types) | `Features/_Shared/GameData/Core/MergeEnum.cs` |
| Item merge rules | `Features/_Shared/GameData/Core/ItemMergeCollection.cs` |
| Item model struct | `Features/_Shared/GameData/Core/ItemMergeModel.cs` |
| Player gameplay state | `Features/Gameplay/Scripts/Data/PlayerGameplayData.cs` |
| Player data facade | `Features/_Shared/GameData/PlayerDataManager.cs` |
| Config data facade | `Features/_Shared/GameData/DataManager.cs` |
| Grid item component | `Features/Gameplay/Scripts/Controller/Controller/ItemMerge.cs` |
| Merge execution | `Features/Gameplay/Scripts/Controller/Controller/Item/Services/ItemMergeService.cs` |
| Merge compatibility check | `Features/Gameplay/Scripts/Controller/Controller/Item/Services/ItemPolicy.cs` |
| Role base class | `Features/Gameplay/Scripts/Controller/Controller/Item/Core/ItemRoleBase.cs` |
| Role data caches | `Features/Gameplay/Scripts/Controller/Controller/DataItemCacheManager.cs` |
| UI base class | `Features/_Shared/UI/Framework/FeatureBaseController.cs` |
| UI manager | `Features/_Shared/UI/Framework/UIManager.cs` |
| Utility helpers | `Features/_Shared/Systems/Utils.cs` |
| Editor export tools | `Editor/ProjectSpecific/ExportForBalancingWindow.cs` |
| Editor recipe designer | `Editor/ProjectSpecific/IngredientAndRecipeWindow/` |
| Editor order designer | `Editor/ProjectSpecific/OrderDesigner/` |

## Branches

- `main` — production
- `develop` — integration branch
- `AnhNT/RemakeBase` — current active branch (gameplay remake)

## Autonomous Backlog System

Cơ chế thực thi task tự động dựa trên backlog split-file (token usage phẳng dù backlog lớn cỡ nào). Index `BACKLOG.md` ở repo root; chi tiết từng task nằm trong `backlog/{planning,todo,in-progress,done}/`. Format task: dùng tier-specific template (`backlog/_TEMPLATE_XS/S/M/L.md`) — xem index tại `backlog/_TEMPLATE.md`.

**Task lifecycle:** `planning → todo → in-progress → done`

| Command | Skill File | Description |
|---------|-----------|-------------|
| `/planning-task [intent]` | [.agents/skills/planning-task/SKILL.md](.agents/skills/planning-task/SKILL.md) | Triage XS/S/M/L → spawn Plan subagent (M/L only) → ghi `backlog/planning/<timestamp>-<TIER>-slug.md`. Parallel-safe, KHÔNG touch BACKLOG.md. |
| `/add-to-backlog` | [.agents/skills/add-to-backlog/SKILL.md](.agents/skills/add-to-backlog/SKILL.md) | List planning tasks → user pick 1/nhiều/all → git mv planning→todo, assign NNN, update BACKLOG.md. Serial operation. |
| `/run-backlog` | [.agents/skills/run-backlog/SKILL.md](.agents/skills/run-backlog/SKILL.md) | Pick task TODO đầu → branch `agent/dev` (từ `develop`) → implement → deterministic preflight → quality gates (code review + performance review song song, + security khi sensitive; verify) với auto-fix max 2 rounds mỗi gate → mark DONE → commit + push (KHÔNG tạo PR). Khi TODO rỗng → ghi `PAUSED` vào `.agents/state`. |

**Subagents dùng cho `/run-backlog`:**

| Agent | File | Role | Model | Spawn khi |
|-------|------|------|-------|-----------|
| `code-reviewer` | [.agents/agents/code-reviewer.md](.agents/agents/code-reviewer.md) | Review diff theo conventions Merge Two (FeatureBaseController, UIManager, UniTask, TigerForge, DOTween, localize, magic number). JSON verdict pass/warn/block. | opus | Mọi task |
| `performance-reviewer` | [.agents/agents/performance-reviewer.md](.agents/agents/performance-reviewer.md) | Audit mobile-perf của diff: GC alloc trên hot path, LINQ/string trong loop, Find/GetComponent không cache, thiếu pooling, canvas/layout rebuild mỗi frame, thuật toán O(n²). JSON verdict pass/warn/block. | opus | Mọi task (song song với code-reviewer) |
| `security-auditor` | [.agents/agents/security-auditor.md](.agents/agents/security-auditor.md) | Audit threat model: credential leak, IAP integrity, save tampering, input validation. JSON verdict. | opus | Khi diff touches `Purchase*`, `IAP*`, `Receipt*`, `DataPlayer*`, `SaveData*`, `Auth*`, `Token*`, hoặc file có credential pattern |
| `qa-verifier` | [.agents/agents/qa-verifier.md](.agents/agents/qa-verifier.md) | Cross-check từng item trong "Acceptance criteria" của task spec với diff. Output `manual_verify_steps` cho user. | sonnet | Mọi task (sau khi review pass) |

**Loop chạy thủ công khi muốn (Windows):**
```powershell
powershell -ExecutionPolicy Bypass -File .agents/scripts/run-backlog-loop.ps1
```
> **macOS / Linux:** chưa có loop runner `.sh`. Chạy trực tiếp skill `/run-backlog` trong Claude Code, lặp lại khi cần — pipeline tự pause (`PAUSED`) khi TODO rỗng.

**Deterministic preflight:**
```powershell
# Windows
powershell -ExecutionPolicy Bypass -File .agents/scripts/backlog-preflight.ps1 -Pretty
```
```bash
# macOS / Linux — bản port Python, JSON output giống hệt bản .ps1
python3 .agents/scripts/backlog-preflight.py -Pretty
```

**Sync `.claude/` → `.agents/`** (tạo junction/symlink một lần sau khi clone; `.agents/` chỉ là link views nên không cần chạy lại sau mỗi lần sửa file):
```powershell
# Windows (junction)
powershell -ExecutionPolicy Bypass -File .claude/scripts/sync-to-agents.ps1
```
```bash
# macOS / Linux (symlink)
bash .claude/scripts/sync-to-agents.sh
```

**Branch model:**
- `agent/dev` luôn branch từ `develop`.
- `/run-backlog` chỉ push lên `agent/dev`, **không** tạo PR.
- User merge tay `agent/dev → develop` sau khi chạy manual verify steps.

**Source of truth:** `.claude/` là canonical source (file thật, được track trong git); `.agents/` chỉ là link views trỏ ngược về `.claude/` (cho Codex/Gemini/Cline đọc) — gitignore, không track. Chỉ edit trong `.claude/`. Sau clone chạy `sync-to-agents` một lần để tạo link.

---

## Coding Notes

- **Vietnamese comments** are common and intentional — leave them in place.
- **Odin Inspector** attributes (`[TabGroup]`, `[SerializeField]`, `[ShowIf]`) are used throughout MonoBehaviours.
- **`ItemSave`** (type enum + int id) is the **save/runtime identity** format. **`ItemMergeModel.id`** (string) is the **config lookup** format. Always convert at boundaries.
- **Editor tools** under `Assets/_Project/Editor/` are `#if UNITY_EDITOR` only and use `ItemMergeCollection` directly.
- `MergeEnum.ItemKey` (struct with `itemSaveType` + `idItem`) is used as dictionary keys for generator/tool/booster caches — its `ToKeyString()` output is those caches' key format.
