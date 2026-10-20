---
description: Decision + code-gen layer for /new-package (IAP shop pack feature) — args, references, directory, Model/Collection/Manager/Controller, DataManager/IAP registration, CSV, UI handoff, checklist. Loaded by .claude/commands/new-package.md. UI MCP details live in new-ui-guide.md + ui-mcp-playbook.md. NOT about UPM packages — that is /package-module.
---

# New Package Guide (IAP package feature)

This is the **what-to-build** layer for [`/new-package`](../commands/new-package.md). UI prefab work is delegated to `/new-ui` ([new-ui-guide.md](new-ui-guide.md) + [ui-mcp-playbook.md](ui-mcp-playbook.md)).

> "Package" here means an **IAP shop offer pack** — a feature inside this game. Extracting C# out of
> `Assets/` into a reusable **UPM package** is a different workflow: [`/package-module`](../commands/package-module.md).

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

The template's own pack code is the reference — read it before writing anything, and when a step
below names a class that `Glob` cannot find in this repo, follow the shipped code instead of the
step (the generation steps below predate parts of the current template; record the deviation):

| # | Path | Purpose |
|---|------|---------|
| 1 | `<featuresRoot>/Monetization/Shop/Store/Scripts/Data/PackTemplateModel.cs` | Base pack model (`id`, `rewards`, `purchaseList`, `iapCost`, `costRequire`, `sale`, product ids, `packName`, `isBestSeller`, `ProductId`) |
| 2 | `<featuresRoot>/Monetization/Shop/Store/Scripts/Data/CoinPackModel.cs` + `CoinPackCollection.cs` | Minimal pack model + collection (`dataGroups`) pair |
| 3 | `<featuresRoot>/_Shared/UI/Templates/purchase_template/PurchaseTemplate.cs` | The pack card: `InitButtons`, `SetPurchaseType`, `SetRewards`, `SetSale`, `SetProductId`, `SetCurrencyRequire`, `SetFirstTimeBonus` |
| 4 | `<featuresRoot>/Monetization/Shop/Store/Scripts/Controller/ShopItemRawPack.cs` + `ShopPackageLogic.cs` + `ShopService.cs` | How a card is driven from a `PackTemplateModel` row, listed in the shop, and how purchases are tracked (`ShopService.IsBuyPack`) |
| 5 | `<featuresRoot>/_Shared/Purchase/` (`PurchaseManager`, `IAPIntegration/`) + `RewardsService` | Purchase entry points and reward granting — every buy goes through them |

---

## 4. Directory structure

Create under the `Monetization` bucket, `<featuresRoot>/Monetization/` (rule `project-structure`; a time-boxed event pack goes under `Events/<Event>/` instead):

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
- **Inheritance:** `PackTemplateModel`
- **Rules:** no namespace; `[Serializable]`; add type-specific fields as needed:
  - `duration` (int) — availability seconds
  - `comebackAfter` (int) — cooldown before reappear (seconds)
  - custom trigger fields if needed

```csharp
using System;

[Serializable]
public class [PackageName]Model : PackTemplateModel
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
- **Inheritance:** `FeatureBaseController` (carries `FeatureType` for open/close events) — `Features/_Shared/UI/Framework/FeatureBaseController.cs`
- **Namespace:** `Ezg.Feature.[PackageName]`
- **Rules:**
  - `PurchaseTemplate` reference (the `purchase_template` card in the popup — new-ui-guide §1 Package branch)
  - `UI_CooldownTimeView` for time-limited packages (from the instantiated `cooldown_timer_template`)
  - Override `LoadData()` to init package data; drive the card like `ShopItemRawPack.UpdateView`
  - Purchases go through `PurchaseManager` / rewards through `RewardsService` before any animation
  - Override `CloseMe()` to emit close event
  - `FeatureType` (from the base class) is wired on the prefab during `/new-ui` (§10), not set in this class

```csharp
using System;
using Ezg.Core.UI;
using Ezg.Feature.Shared.GameData;
using Ezg.Feature.Shared.UI;
using Ezg.Feature.Shared.UI.Framework;
using Sirenix.OdinInspector;
using TigerForge;
using UnityEngine;

namespace Ezg.Feature.[PackageName]
{
    internal class [PackageName]Controller : FeatureBaseController
    {
        [SerializeField]
        [TabGroup("Cấu hình")]
        [Required]
        private PurchaseTemplate _purchase;

        [SerializeField]
        [TabGroup("Cấu hình")]
        private int _packIndex;

        [SerializeField]
        [TabGroup("Cấu hình")]
        [Required]
        private UI_CooldownTimeView _cooldownTime;

        protected override void Awake()
        {
            base.Awake();
            // Đăng ký nút một lần (ShopItemRawPack làm ở Start) — LoadData có thể chạy nhiều lần.
            _purchase.InitButtons(OnPurchaseAds, OnPurchaseCurrency, OnPurchaseIap, OnPurchaseFree);
        }

        protected override void LoadData()
        {
            base.LoadData();

            var pack = DataManager.[PackageName].GetPack(_packIndex);
            // Đổ dữ liệu như ShopItemRawPack.UpdateView: HideAll → SetSale → SetRewards → SetPurchaseType
            // → SetProductId (IAP) / SetCurrencyRequire (Currency) → SetFirstTimeBonus nếu có.
            _purchase.HideAll();
            _purchase.SetSale(pack.sale)
                .SetRewards(pack.rewards, false)
                .SetPurchaseType(pack.purchaseList[0].purchaseType, true);
            _cooldownTime.InitCustomCooldown([PackageName]Manager.GetEndTime(_packIndex), () => CloseMe());
        }

        // Mỗi handler mua qua PurchaseManager rồi cấp thưởng qua RewardsService — chép đúng luồng
        // OnPurchase* của ShopItemRawPack, không tự cộng tiền / item.
        private void OnPurchaseAds() { }
        private void OnPurchaseCurrency() { }
        private void OnPurchaseIap() { }
        private void OnPurchaseFree() { }

        public override void CloseMe(Action completeAction = null)
        {
            base.CloseMe(() =>
            {
                completeAction?.Invoke();
                EventManager.EmitEvent(nameof([PackageName]Controller));
            });
        }
    }
}
```

---

## 6. DataManager / IAP registration

1. **Generated accessors — never hand-edit.** `CsvAssetDir.cs` (CSV → asset name) and
   `DataManager.Generated.cs` (`DataManager.[PackageName]`) are produced by the `com.ezg.csv-reader`
   import tool from the CSV in the pack's `CsvConfig/` (skill `csv-config`). Add the CSV, run the import,
   and check the new `public static [PackageName]Collection [PackageName] => Get<...>()` line appeared.

2. **IAP product ids** — `InAppPurchase` registers the store catalog from `ShopService.GetAllProductId()`
   and grants every completed order through `ShopService.GrantIapByProductId()` →
   `GetPackByProductId()` (no UI controller involved, so recovered / deferred orders are granted too).
   A pack sold for real money adds its collection to **both**, the same way `CoinPack` / `GemPack` /
   `RemoveAdsPack` are listed:

```csharp
// ShopService.GetAllProductId()
list.AddRange(DataManager.[PackageName].dataGroups.Select(x => x.ProductId));

// ShopService.GetPackByProductId() — add to the ?? chain
?? Find(DataManager.[PackageName].dataGroups)
```

   Missing from the first → the store never offers the SKU; missing from the second → the order
   charges and grants nothing (PolishRules §12 / P35).

---

## 7. CSV data file

**Path:** `<featuresRoot>/Monetization/[PackageName]/CsvConfig/[PackageName].csv`

> Do **NOT** place CSV at `Assets/Csv/Collection/Packages/...` — that legacy path is obsolete (see csv-config skill).

### Columns

Start from the header the template's own packs use (`Monetization/Shop/CsvConfig/CoinPack.csv` /
`GemPack.csv`) — it maps 1:1 onto `PackTemplateModel` — then add the pack's own columns (e.g.
`duration`, `comeback_after`) at the end:

| Column | Type | Description |
|--------|------|-------------|
| `id` | int | Pack id (row key) |
| `pack_name` | string | Localization key of the pack name (lowercase, e.g. `[package_name]_1`) |
| `res_type` / `res_id` / `res_number` | int / int / long | One reward (`EnumBase.ResourceTypes`, resource id, amount) |
| `bonus` / `stage_bonus` | float / int | Reward bonus (optional) |
| `first_time_purchase_bonus` | int | Extra amount on the first purchase (0 = none) |
| `purchase_type` / `purchase_count` | `PurchaseType` / int | `IAP` / `Ads` / `Free` / `Currency` and how many times |
| `iap_cost` | float | Display price fallback (the store price wins at runtime) |
| `cost_type` / `cost_id` / `cost_number` | | Soft-currency cost when `purchase_type = Currency` |
| `sale` | float | Sale % shown on the card (empty = none) |
| `google_product_id` / `google_product_id_premium` / `apple_product_id` | string | Store SKUs (`com.company.game.[package_name]_N`) |

### Example

```csv
id,pack_name,res_type,res_id,res_number,bonus,stage_bonus,first_time_purchase_bonus,purchase_type,purchase_count,iap_cost,cost_type,cost_id,cost_number,sale,google_product_id,google_product_id_premium,apple_product_id
1,[package_name]_1,1,2,100,0,0,0,IAP,1,3.99,,,,,com.company.game.[package_name]_1,,com.company.game.[package_name]_1
```

Multi-reward rows follow the csv-config skill's continuation-row rule — read it before writing one.

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

From the `PurchaseType` enum (`Features/_Shared/ResourceSystem/Services/RewardsService.cs`):

| Type | Description |
|------|-------------|
| `Free` | No cost, direct claim (`button_purchase_free`) |
| `Ads` | Watch ad to purchase (`button_purchase_ads`, through `AdsController`) |
| `Currency` | In-game currency (`button_purchase_currency`, cost via `PurchaseManager.PurchaseOffline`) |
| `IAP` | Real-money IAP — default for packages (`button_purchase_iap`, `PurchaseManager.PurchaseOnline`) |

---

## 10. UI prefab (required for every package)

Every package needs a UI prefab, but **the build is not described here** — invoke `/new-ui` with the same `[PackageName]`:

> `/new-ui [PackageName]`

Because the name ends with `Pack`, `/new-ui` auto-routes to its **Package branch**, which is the single source of truth for the prefab build — a `screen_template` Variant on the popup layout holding a `purchase_template` card (or a plain shop-cell prefab when the pack only lives in the shop list), `_purchase` / `_packIndex` / timer wiring, `ClickBackgroundToExit`, `FeatureType`, and Variant verification. See [new-ui-guide.md](new-ui-guide.md) §0b, §1, §4 (Package branch) + §5 (including the Phase D designer pass — [ui-designer-pass.md](ui-designer-pass.md)), with tool-call recipes in [ui-mcp-playbook.md](ui-mcp-playbook.md) §4.

Only package-specific fact to carry across: the controller to bind is `[PackageName]Controller.cs` from §5.E above.

---

## 10b. Cheat support

📖 **MUST READ:** [`.claude/skills/feature-cheat/SKILL.md`](../skills/feature-cheat/SKILL.md) — prefab structure, sizes, labels, code pattern.

The base screen prefab ships **no** cheat chrome, so instantiate `Templates/CheatMenu.prefab` into the pack prefab root yourself (`CheatMenuController` + `GameCheatObjectController` come on it). Packs are almost always **worth cheating**: they are time-limited, purchase-gated, and one-shot — exactly the state a tester cannot reach in a few taps.

Typical package cheats (mirror `Features/System/GameCheat` in style):

- `ResetBuy` "Reset buy" → clear the purchased/claimed flag in `Player[PackageName]Data` so the pack can be bought again.
- `Expire` "Expire now" / `+1 day` → move the cooldown/limited window (route through the manager's single time accessor, never `DateTime.Now`).
- `Grant` "Grant pack" → run the reward path **through the production entry point** (`RewardsService` / `ShopService.GrantIapByProductId` — the same method the real purchase calls), never a parallel grant.

`GameSystems.Cheat.IAP` already exists for simulating the purchase itself — prefer it over faking a receipt. Cheat labels get **no** `LocalizeHelper`. Do not wrap cheat code in `#if UNITY_EDITOR`; the chrome's `GameCheatObjectController` gates visibility.

---

## 11. Final checklist

- [ ] All names end with `Pack` suffix
- [ ] `[PackageName]Model.cs` inherits `PackTemplateModel`
- [ ] `[PackageName]Collection.cs` has `dataGroups` array
- [ ] `[PackageName]Manager.cs` registered in `PlayerDataManager.cs`
- [ ] `CsvAssetDir.cs` + `DataManager.Generated.cs` regenerated by the csv-reader import (not hand-edited)
- [ ] `ShopService.GetAllProductId()` and `ShopService.GetPackByProductId()` include `DataManager.[PackageName]`
- [ ] CSV at `<featuresRoot>/Monetization/[PackageName]/CsvConfig/[PackageName].csv`
- [ ] IAP product IDs: `com.company.game.[package_name]_N`
- [ ] Localization keys for package names
- [ ] `/new-ui [PackageName]` → `screen_<snake_case>` Variant of `screen_template` in `<featuresRoot>/<Domain>/[PackageName]/Resources/` (Package branch), or a shop-cell prefab when the pack has no popup
- [ ] Controller `_purchase` (the `purchase_template` card), `_packIndex`, `_cooldownTime` (if time-limited) bound
- [ ] Cheat decision made explicitly (§10b): `Cheat_*` methods + buttons under `CheatMenu/Menu` (reset-buy / expire-window at minimum), or a stated reason why none are needed
