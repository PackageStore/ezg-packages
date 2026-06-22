---
name: purchase-manager
description: Guide for using PurchaseManager to handle in-game purchases with resources, currencies, ads, and IAP
---

# PurchaseManager Skill

## Overview
`PurchaseManager` is a static utility class that handles all purchase transactions in the game. It supports:
- **Offline purchases**: Using in-game resources or currencies (no network required)
- **Ads purchases**: Showing rewarded video ads as payment
- **Online purchases**: In-app purchases (IAP) through app stores

## File Location
`Assets/_Project/Features/_Shared/Purchase/PurchaseManager.cs`

## Namespace
```csharp
using Ezg.Feature.IAP;
```

## Key Concepts

### Payment Types
| Type | Description |
|------|-------------|
| `Resource` | Generic resource items (materials, ingredients, etc.) |
| `EnumBase.MoneyTypes` | Currency types: Gold, Gems, Energy, Ads, etc. |
| `productId` | Store product ID for IAP |

### Return Values
- `PurchaseOffline` returns `bool`: `true` if purchase succeeded, `false` if failed
- `PurchaseOnline` is `void`: uses callback for result

## Methods

### 1. PurchaseOffline with Single Resource
```csharp
PurchaseManager.PurchaseOffline(
    resource: new Resource(EnumBase.ResourceTypes.Item, itemId, quantity),
    successAction: () => { /* on success */ },
    unSuccessAction: () => { /* on fail */ },
    isShowMes: true,     // show "not enough" message
    source: "shop",      // analytics
    sourceId: "item_01",
    placement: "main"
);
```

### 2. PurchaseOffline with Multiple Resources
```csharp
var resources = new List<Resource>() {
    new Resource(EnumBase.ResourceTypes.Item, 1, 10),
    new Resource(EnumBase.ResourceTypes.Item, 2, 5)
};

PurchaseManager.PurchaseOffline(
    resource: resources,
    successAction: () => { /* on success */ }
);
```

### 3. PurchaseOffline with Currency
```csharp
PurchaseManager.PurchaseOffline(
    moneyType: EnumBase.MoneyTypes.Gold,
    price: 1000,
    successAction: () => { /* on success */ },
    unSuccessAction: () => { /* on fail */ },
    isShowMes: true,
    source: "upgrade",
    sourceId: "skill_01"
);
```

### 4. PurchaseOffline with Ads (Rewarded Video)
```csharp
var rewards = new List<Resource>() {
    new Resource(EnumBase.ResourceTypes.Money, (int)EnumBase.MoneyTypes.Gold, 500)
};

PurchaseManager.PurchaseOffline(
    moneyType: EnumBase.MoneyTypes.Ads,
    price: 0,                    // price is ignored for ads
    successAction: () => { 
        // Give rewards after watching ad
        RewardsService.Add(rewards);
    },
    unSuccessAction: () => { /* ad failed/skipped */ },
    location: "double_reward",   // ads location for analytics
    source: "daily_bonus",
    rewardsTracking: rewards     // track rewards in analytics
);
```

### 5. PurchaseOnline (In-App Purchase)
```csharp
PurchaseManager.PurchaseOnline(
    productId: "com.game.gems_pack_1",
    callback: () => {
        // Purchase completed - give items
        PlayerResource.AddCurrency(EnumBase.MoneyTypes.Diamonds, 100);
    },
    source: "shop",
    sourceId: "gems_pack_1"
);
```

## Common Usage Patterns

### Pattern 1: Buy Item with Gold
```csharp
public void BuyItem(int itemId, long price)
{
    bool success = PurchaseManager.PurchaseOffline(
        moneyType: EnumBase.MoneyTypes.Gold,
        price: price,
        successAction: () => {
            PlayerDataManager.Inventory.AddItem(itemId, 1);
            PlayerDataManager.Inventory.Save();
            Debug.Log("Item purchased!");
        },
        source: "shop",
        sourceId: $"item_{itemId}"
    );
}
```

### Pattern 2: Watch Ad for Reward
```csharp
public void WatchAdForReward(List<Resource> rewards)
{
    PurchaseManager.PurchaseOffline(
        moneyType: EnumBase.MoneyTypes.Ads,
        price: 0,
        successAction: () => {
            RewardsService.Add(rewards, source: "ads_reward");
        },
        location: "reward_popup",
        source: "daily_reward",
        rewardsTracking: rewards
    );
}
```

### Pattern 3: Upgrade with Multiple Resources
```csharp
public void UpgradeBuilding(int buildingId, List<Resource> cost)
{
    if (PurchaseManager.PurchaseOffline(cost, 
        successAction: () => {
            PlayerDataManager.Building.Upgrade(buildingId);
        },
        source: "building",
        sourceId: $"upgrade_{buildingId}"))
    {
        // Purchase started - UI feedback
        ShowUpgradeAnimation();
    }
}
```

## Analytics Parameters
All purchase methods support analytics tracking:

| Parameter | Description | Example |
|-----------|-------------|---------|
| `source` | Feature/screen where purchase happened | `"shop"`, `"upgrade"`, `"gacha"` |
| `sourceId` | Specific item/action identifier | `"item_001"`, `"skill_fire"` |
| `placement` | UI placement identifier | `"main_button"`, `"popup"` |
| `location` | Ads location (for Ads type only) | `"double_reward"`, `"revive"` |

## Important Notes

1. **Auto-save**: `PurchaseOffline` automatically saves `PlayerResource` after successful purchase
2. **Error message**: When `isShowMes: true`, shows localized "not_enough_resource" message on failure
3. **Ads handling**: For `MoneyTypes.Ads`, the method always returns `true` immediately (async video)
4. **Callbacks**: Always handle both `successAction` and `unSuccessAction` for proper UX

## Related Systems
- `PlayerResource` - Check and modify player resources
- `RewardsService` - Add rewards after purchase
- `AdsManager` - Handles rewarded video ads
- `InAppManager` - Handles store IAP
