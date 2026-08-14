---
description: Decision + code-gen layer for /new-package — args, references, directory, Model/Collection/Manager/Controller, DataManager/IAP registration, CSV, UI handoff, checklist. Loaded by .claude/commands/new-package.md. UI MCP details live in new-ui-guide.md + ui-mcp-playbook.md.
---

# New Package Guide (IAP package feature)

This is the **what-to-build** layer for [`new-package.md`](../workflows/new-package.md). UI prefab work is delegated to `/new-ui` ([new-ui-guide.md](new-ui-guide.md) + [ui-mcp-playbook.md](ui-mcp-playbook.md)).

CSV conventions: always read [`.claude/skills/csv-config/SKILL.md`](../skills/csv-config/SKILL.md) before writing package CSV.

---

## 1. Argument parsing

Parse `{{args}}` with format: `PackageName: Description`

**Examples:**
- `WeeklyGem: Weekly gem bundle package with bonus rewards`
- `VIPPack: VIP exclusive package with premium items`

**Rules:**
- If no colon found → treat entire `{{args}}` as PackageName with empty Description
- PackageName MUST be in **PascalCase** and end with `Pack` suffix
- Description is optional

---

## 2. Description analysis

Determine package requirements by priority:

| Priority | Source | Action |
|----------|--------|--------|
| 1st | User attached file (image, .md, .txt, .pdf) | Read and analyze as complete requirements |
| 2nd | Text description from `{{args}}` | Use as package specification |
| 3rd | No description provided | Create minimal structure only |

---

## 3. Reference study (required before implementation)

| # | Path | Purpose |
|---|------|---------|
| 1 | `PackageTemplateModel.cs` (`Glob **/PackageTemplateModel.cs`) | Base model fields (name, profit, rewards, packId) |
| 2 | `<featuresRoot>/Package/Scripts/Controller/PurchaseTemplateController.cs` | How UI handles IAP purchases |
| 3 | `<featuresRoot>/Package/Scripts/Controller/PackageManager.cs` | Purchase tracking (`IsPurchase`, `PurchasePack`, `PurchaseDailyPack`, `PurchaseWeeklyPack`) |
| 4 | Examples (pick relevant) | `StarterPack/` (duration/comebackAfter), `GemRefillPack/` (gem trigger), `JewelPack/` (simple time-limited) |

---

## 4. Directory structure

Create under `<featuresRoot>/`:

```
[PackageName]/
├── Resources/
├── CsvConfig/          # CSV lives here (not Assets/Csv/Collection/Packages/)
└── Scripts/
    ├── Controller/
    │   ├── [PackageName]Controller.cs
    │   └── [PackageName]Manager.cs
    └── Data/
        ├── [PackageName]Model.cs
        ├── [PackageName]Collection.cs
        └── Player[PackageName]Data.cs
```

---

## 5. Code generation

### A. `[PackageName]Model.cs`

- **Location:** `[PackageName]/Scripts/Data/`
- **Inheritance:** `PackageTemplateModel`
- **Rules:** no namespace; `[Serializable]`; add type-specific fields as needed:
  - `duration` (int) — availability seconds
  - `comebackAfter` (int) — cooldown before reappear (seconds)
  - custom trigger fields if needed

```csharp
using System;

[Serializable]
public class [PackageName]Model : PackageTemplateModel
{
    /// <summary>
    /// Thời gian tồn tại của gói (giây)
    /// </summary>
    public int duration;

    /// <summary>
    /// Thời gian quay trở lại sau khi gói hết hạn
    /// </summary>
    public int comebackAfter;
}
```

### B. `[PackageName]Collection.cs`

- **Location:** `[PackageName]/Scripts/Data/`
- **Rules:** inherit `ScriptableObject`; no namespace; main array field **MUST** be named `dataGroups`; helpers `GetRewards(int index)`, `GetPack(int index)`

```csharp
using System;
using UnityEngine;

[Serializable]
public class [PackageName]Collection : ScriptableObject
{
    public [PackageName]Model[] dataGroups;

    public Resource[] GetRewards(int packIndex) => dataGroups[packIndex].rewards;

    public [PackageName]Model GetPack(int packIndex) => dataGroups[packIndex];
}
```

### C. `Player[PackageName]Data.cs`

- **Location:** `[PackageName]/Scripts/Data/`
- **Rules:** no namespace; `[Serializable]`; time-tracking dictionary for package state

```csharp
using System;
using System.Collections.Generic;

[Serializable]
public class Player[PackageName]Data
{
    public Dictionary<int, long> PackStartTime = new();
}
```

### D. `[PackageName]Manager.cs`

- **Location:** `[PackageName]/Scripts/Controller/`
- **Inheritance:** `DataPlayerBaseGeneric<Player[PackageName]Data>`
- **Namespace:** `Ezg.Feature.[PackageName]`
- **Include:** `PlayerData` property
- **Key methods:**
  - `GetEndTime(int packIndex)` — when package expires
  - `CanShowPack(int packIndex)` — whether to display
  - `InitStartTime(int packIndex)` — init start time

**Required registration in `PlayerDataManager.cs`:**

```csharp
// using at top
using Ezg.Feature.[PackageName];

// field
private static [PackageName]Manager _[packageName];

// property
public static [PackageName]Manager [PackageName]
{
    get { return _[packageName] ??= DataPlayer.GetModule<[PackageName]Manager>(); }
    set => _[packageName] = value;
}
```

### E. `[PackageName]Controller.cs`

- **Location:** `[PackageName]/Scripts/Controller/`
- **Inheritance:** `GameFeatureBaseController` (adds `FeatureType` for open/close events) — locate with `Glob **/GameFeatureBaseController.cs`
- **Namespace:** `Ezg.Feature.[PackageName]`
- **Rules:**
  - `PurchaseTemplateController` reference for IAP
  - `UI_CooldownTimeView` for time-limited packages
  - Override `LoadData()` to init package data
  - Override `CloseMe()` to emit close event
  - `FeatureType` (TabGroup "Common", from the base class) is wired on the prefab during `/new-ui` (§10), not set in this class

```csharp
using Sirenix.OdinInspector;
using TigerForge;
using UnityEngine;
using System;

namespace Ezg.Feature.[PackageName]
{
    internal class [PackageName]Controller : GameFeatureBaseController
    {
        [SerializeField]
        [TabGroup("Cấu hình")]
        [Required]
        private PurchaseTemplateController _purchase;

        [SerializeField]
        [TabGroup("Cấu hình")]
        [Required]
        private int _packIndex;

        [SerializeField]
        [TabGroup("Cấu hình")]
        [Required]
        private UI_CooldownTimeView _cooldownTime;

        protected override void LoadData()
        {
            base.LoadData();

            _purchase.InitData(DataManager.[PackageName].GetPack(_packIndex));
            _cooldownTime.InitCustomCooldown([PackageName]Manager.GetEndTime(_packIndex), () => CloseMe());
        }

        public override void CloseMe(Action completeAction = null)
        {
            base.CloseMe(() =>
            {
                EventManager.EmitEvent(nameof([PackageName]Controller));
            });
        }
    }
}
```

---

## 6. DataManager / IAP registration

1. **`CsvAssetDir.cs`** — locate with `Glob **/CsvAssetDir.cs`  
   `public const string [PackageName] = "[PackageName]";`  
   Value is bare filename (no extension). `ResLoader` resolves CSV inside the package's `CsvConfig/` folder.

2. **`DataManagerAutoGenerate.cs`** — locate with `Glob **/DataManagerAutoGenerate.cs`  
   `public static [PackageName]Collection [PackageName] => Get<[PackageName]Collection>();`

3. **`GameSystems.cs`** — locate with `Glob **/GameSystems.cs`  
   Inside `InitAllPurchasePackage()`, add to `consumablePack` **before** the `Where` filter:

```csharp
consumablePack.AddRange(DataManager.[PackageName].dataGroups.Select(x => x.packId));
```

Pattern: `.Select(x => x.packId)` when each entry is a separate pack (same as `StarterPack`, `JewelPack`, `GloryPassPack`).

---

## 7. CSV data file

**Path:** `<featuresRoot>/[PackageName]/CsvConfig/[PackageName].csv`

> Do **NOT** place CSV at `Assets/Csv/Collection/Packages/...` — that legacy path is obsolete (see csv-config skill).

### Columns

| Column | Type | Description |
|--------|------|-------------|
| `pack_id` | string | IAP product ID (e.g. `com.company.game.[package_name]_1`) |
| `duration` | int | Availability seconds (86400 = 1 day) |
| `comeback_after` | int | Cooldown before reappear (seconds) |
| `profit` | int | Value multiplier shown to user (e.g. 1000 = x10) |
| `name` | string | Localization key (e.g. `#[package_name]_1`) |
| `res_type` | int | `EnumBase.ResourceTypes` int (Money=1, Hero=3, …) |
| `res_id` | int | Resource ID from corresponding enum |
| `res_number` | long | Amount |
| `bonus` | int | Bonus % (optional) |
| `stage_bonus` | int | Stage-based bonus (optional) |
| `custom_value` | string | Custom data (optional) |

### Multi-reward example

```csv
pack_id,duration,comeback_after,profit,name,res_type,res_id,res_number,bonus,stage_bonus,custom_value
com.company.game.[package]_1,86400,86400,1000,#[package]_1,1,22,1,,,
,,,,,1,2,200,,,
,,,,,1,16,10,,,
com.company.game.[package]_2,86400,86400,1500,#[package]_2,3,2,1,,,
,,,,,1,2,500,,,
```

**Important:** empty `pack_id` means the row belongs to the previous pack's rewards array.

---

## 8. Common usings

```csharp
// Controller
using System;
using Sirenix.OdinInspector;
using TigerForge;
using UnityEngine;

// Data files
using System;
using System.Collections.Generic;
using UnityEngine;
```

---

## 9. Purchase type options

From `PackageManager.PurchaseTypes`:

| Type | Description |
|------|-------------|
| `Free` | No cost, direct claim |
| `Ads` | Watch ad to purchase |
| `Currency` | In-game currency |
| `Iap` | Real-money IAP (default for packages) |

---

## 10. UI prefab (required for every package)

Every package needs a UI prefab, but **the build is not described here** — invoke `/new-ui` with the same `[PackageName]`:

> `/new-ui [PackageName]`

Because the name ends with `Pack`, `/new-ui` auto-routes to its **Package branch**, which is the single source of truth for the prefab build — base template `PackageTemplate.prefab`, Popup layout, `_purchase` / `_packIndex` / `_cooldownTime` wiring, `ClickBackgroundToExit`, `FeatureType`, and Variant verification. See [new-ui-guide.md](new-ui-guide.md) §0b, §1, §4 (Package branch) + §5, with tool-call recipes in [ui-mcp-playbook.md](ui-mcp-playbook.md) §4.

Only package-specific fact to carry across: the controller to bind is `[PackageName]Controller.cs` from §5.E above.

---

## 10b. Cheat support

📖 **MUST READ:** [`.claude/skills/feature-cheat/SKILL.md`](../skills/feature-cheat/SKILL.md) — prefab structure, sizes, labels, code pattern.

`PackageTemplate.prefab` ships the same `ButtonCheatMenu` chrome as `FeatureTemplate` (identical transform, `CheatMenuController` + `GameCheatObjectController`), so the package prefab inherits it. Packages are almost always **worth cheating**: they are time-limited, purchase-gated, and one-shot — exactly the state a tester cannot reach in a few taps.

Typical package cheats (mirror `DailyLoginV2` / `Equipment` in style):

- `ResetBuy` "Reset buy" → clear the purchased/claimed flag in `Player[PackageName]Data` so the pack can be bought again.
- `Expire` "Expire now" / `+1 day` → move the cooldown/limited window (route through the manager's single time accessor, never `DateTime.Now`).
- `Grant` "Grant pack" → run the reward path **through the production entry point** (`RewardManager` / the same method the real purchase calls), never a parallel grant.

`GameSystems.Cheat.IAP` already exists for simulating the purchase itself — prefer it over faking a receipt. Cheat labels get **no** `LocalizesUI`. Do not wrap cheat code in `#if UNITY_EDITOR`; the chrome's `GameCheatObjectController` gates visibility.

---

## 11. Final checklist

- [ ] All names end with `Pack` suffix
- [ ] `[PackageName]Model.cs` inherits `PackageTemplateModel`
- [ ] `[PackageName]Collection.cs` has `dataGroups` array
- [ ] `[PackageName]Manager.cs` registered in `PlayerDataManager.cs`
- [ ] `CsvAssetDir.cs` updated with path constant
- [ ] `DataManagerAutoGenerate.cs` updated with collection property
- [ ] `GameSystems.InitAllPurchasePackage()` has `DataManager.[PackageName].dataGroups.Select(x => x.packId)`
- [ ] CSV at `<featuresRoot>/[PackageName]/CsvConfig/[PackageName].csv`
- [ ] IAP product IDs: `com.company.game.[package_name]_N`
- [ ] Localization keys for package names
- [ ] `/new-ui [PackageName]` → prefab variant at `Features/[PackageName]/Resources/[PackageName].prefab` (Package branch)
- [ ] Controller `_purchase`, `_packIndex`, `_cooldownTime` (if time-limited) bound
- [ ] Cheat decision made explicitly (§10b): `Cheat_*` methods + buttons under `ButtonCheatMenu/MenuParent` (reset-buy / expire-window at minimum), or a stated reason why none are needed
