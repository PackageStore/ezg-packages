---
name: currency-preview
description: Guide for using CurrencyPreviewController to display currency/resource UI elements
---

# CurrencyPreviewController Module

## Overview
`CurrencyPreviewController` is a reusable UI component for displaying currency icons with formatted values. It's commonly used in shops, rewards popups, item tooltips, and anywhere costs or resources need to be shown.

**Location**: `Assets/_Project/Features/_Shared/UI/Items/CurrencyPreviewController.cs`

## Key Features
- Display single or multiple currencies
- Automatic icon loading based on currency type
- Number formatting with abbreviations (1K, 1M)
- Resource validation coloring (red if insufficient)
- Show remaining player resources alongside cost
- Support for free/ads display modes

## Currency Types
Uses `EnumBase.MoneyTypes`:
- `None` - Shows "Free" text, hides icon
- `Ads` - Shows ad icon with "Free" text  
- `Gold`, `Diamonds`, `Energy`, etc. - Shows corresponding icon and value

## Usage Patterns

### 1. Basic Currency Display
Show a simple currency with value:

```csharp
// Basic display
currencyPreview.InitData(EnumBase.MoneyTypes.Gold, 1000);

// With abbreviation (1000 -> 1K)
currencyPreview.InitData(EnumBase.MoneyTypes.Diamonds, 15000, useMoneyConvert: true);
```

### 2. Validation Display
Show colored text based on whether player has enough resources:

```csharp
// Red text if player doesn't have enough
currencyPreview.InitData(EnumBase.MoneyTypes.Gold, cost, isValidResource: true);
```

### 3. Show Remaining Resources
Display "owned/cost" format:

```csharp
// Shows like "500/100" (player has 500, cost is 100)
currencyPreview.InitData(EnumBase.MoneyTypes.Gold, 100, showRemaining: true);
```

### 4. With Click Action
Add button callback:

```csharp
currencyPreview.InitData(EnumBase.MoneyTypes.Diamonds, 50, action: () => {
    // Handle click - open shop, confirm purchase, etc.
});
```

### 5. Display Multiple Resources
Use template mode for multiple currencies:

```csharp
List<Resource> resources = new List<Resource>
{
    new Resource { resId = (int)EnumBase.MoneyTypes.Gold, resNumber = 1000 },
    new Resource { resId = (int)EnumBase.MoneyTypes.Diamonds, resNumber = 50 }
};

// Requires _currencyTemplate to be assigned in Inspector
currencyPreview.InitData(resources, useMoneyConvert: true);
```

### 6. Display Single Resource Object
```csharp
Resource cost = new Resource { resId = (int)EnumBase.MoneyTypes.Gold, resNumber = 500 };
currencyPreview.InitData(cost, isValidResource: true);
```

### 7. Custom Text Display
Show text only without icon:

```csharp
// Text only mode
currencyPreview.InitData("Custom Label");

// With click handler
currencyPreview.InitData("Buy Now", () => OpenPurchasePopup());
```

### 8. Pre-formatted String Value
When formatting is handled externally:

```csharp
currencyPreview.InitData(EnumBase.MoneyTypes.Gold, "1,234 / 5,000");
```

## Inspector Setup

| Field | Description |
|-------|-------------|
| `_icon` | Image component for currency icon |
| `_backgroundImage` | Optional background for rarity display |
| `_value` | Text component for value display |
| `_currencyTemplate` | Template prefab for multi-currency mode |

## Common Use Cases

### Shop Item Price
```csharp
var price = shopItem.GetPrice();
currencyPreview.InitData(price.type, price.amount, 
    useMoneyConvert: true, 
    isValidResource: true);
```

### Reward Preview
```csharp
currencyPreview.InitData(reward.type, reward.amount, useMoneyConvert: true);
```

### Cost with Remaining
```csharp
currencyPreview.InitData(EnumBase.MoneyTypes.Gold, buildingCost, 
    showRemaining: true, 
    isValidResource: true);
```

## Related Components
- `PlayerResource.GetCurrencyImage()` - Gets icon sprite for currency type
- `PlayerResource.GetMoneyQuantity()` - Gets player's current amount
- `ValidResource()` extension - Formats with validation color
- `MoneyConvert()` extension - Abbreviates large numbers
