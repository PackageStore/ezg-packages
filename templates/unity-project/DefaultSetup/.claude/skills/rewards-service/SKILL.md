---
name: rewards-service
description: Guide for using RewardsService module to handle reward processing, distribution, and analytics tracking.
---

# RewardsService Module

## Overview
`RewardsService` is a static utility class that handles all reward-related operations including:
- Receiving and processing rewards
- Resource extraction and exchange
- Analytics tracking
- UI popup notifications
- Reward compilation and deduplication

## Location
`Assets/_Project/Features/_Shared/ResourceSystem/Services/RewardsService.cs`

## Key Concepts

### PurchaseType Enum
Defines how a reward was acquired:
- `None` (-1): Undefined
- `Free` (0): Free reward
- `Ads` (1): Reward from watching ads
- `Currency` (2): Purchased with in-game currency
- `IAP` (3): In-app purchase
- `Event` (4): Event reward

### Resource Types
Uses `EnumBase.ResourceTypes`:
- `Money`: Currency (Gold, Gems, etc.)
- `Skill`: Player skills
- `Book`: Passive abilities
- `Package`: Bundled items
- `Item`: General items
- `Pet`: Companions
- `Hero`: Characters

## Common Usage Patterns

### 1. Grant a Single Reward
```csharp
var reward = new Resource
{
    resType = EnumBase.ResourceTypes.Money,
    resId = (int)EnumBase.MoneyTypes.Gold,
    resNumber = 100
};

RewardsService.ReceiveReward(
    reward,
    purchasedType: PurchaseType.Free,
    source: "DailyLogin",
    sourceId: "Day1",
    isShowPopup: true
);
```

### 2. Grant Multiple Rewards
```csharp
var rewards = new Resource[]
{
    new Resource { resType = EnumBase.ResourceTypes.Money, resId = 0, resNumber = 100 },
    new Resource { resType = EnumBase.ResourceTypes.Money, resId = 1, resNumber = 50 }
};

RewardsService.ReceiveRewards(
    rewards,
    purchasedType: PurchaseType.IAP,
    source: "Shop",
    productId: "com.game.starter_pack",
    isShowPopup: true,
    onClose: () => Debug.Log("Popup closed")
);
```

### 3. Grant Reward Without Popup
```csharp
RewardsService.ReceiveReward(reward, isShowPopup: false);
```

### 4. Grant Reward Without Updating Resource Event
```csharp
// Useful when batching multiple operations
RewardsService.ReceiveReward(reward, updateResource: false);
// Manually emit event when done
EventManager.EmitEvent(nameof(EventName.UpdateResource));
```

### 5. Validate Currency Display
```csharp
long cost = 1000;
// Returns colored string: green if player has enough, red if not
string displayText = cost.ValidResource(EnumBase.MoneyTypes.Gold);
// With abbreviated format (1K, 1M)
string shortText = cost.ValidResource(EnumBase.MoneyTypes.Gold, useMoneyConvert: true);
```

### 6. Merge Duplicate Rewards
```csharp
var rewards = new List<Resource> { /* multiple items */ };
var merged = rewards.CompileRewards(); // Combines same resType + resId
```

### 7. Calculate Pack Bonus Rewards
```csharp
PackRewards[] packRewards = GetPackData();
Resource[] finalRewards = packRewards.GetFinalRewardsBonus();
// Includes stage-based bonus calculation
```

## Cost Requirements

### Define Cost Requirements
```csharp
var costs = new RewardsService.CostRequire[]
{
    new() { costType = EnumBase.ResourceTypes.Money, costId = 0, costNumber = 100 }
};

// Convert to Resource list for validation
List<Resource> resourceCosts = RewardsService.GetRequireCost(costs);
```

## Method Reference

| Method | Description |
|--------|-------------|
| `ReceiveReward(Resource)` | Process single reward |
| `ReceiveRewards(Resource[])` | Process multiple rewards |
| `ReceiveReward(List<Resource>)` | Process list of rewards |
| `ShowToast(Resource[])` | Display reward popup |
| `Tracking(Resource[])` | Send analytics events |
| `CompileRewards()` | Merge duplicate rewards |
| `FixDuplicateRewards()` | Remove and merge duplicates |
| `GenerateReward()` | Filter valid reward types |
| `ValidResource()` | Format currency with color |
| `GetFinalRewardsBonus()` | Calculate stage-based bonuses |
| `GetRequireCost()` | Convert CostRequire to Resource list |

## Analytics Tracking
Tracking is automatic when using `ReceiveReward`/`ReceiveRewards`. Events sent:
- `buy_resource`: For IAP purchases
- `earn_resource`: For free/currency rewards
- Includes source, placement, item details, and remaining balance

## Best Practices
1. Always provide `source` and `sourceId` for analytics clarity
2. Use `updateResource: false` when batching multiple reward operations
3. Use `CompileRewards()` before displaying to merge duplicates
4. Handle `onClose` callback for sequential UI flows
