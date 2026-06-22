---
name: item-preview
description: Guide for using ItemPreviewController module to display resource items in UI
---

# ItemPreviewController Module

## Overview

`ItemPreviewController` is a Unity component that dynamically creates and manages a list of item UI elements from `Resource` data. It handles instantiation, caching, and cleanup of item preview elements.

**Location**: `Assets/_Project/Features/_Shared/UI/Items/ItemPreviewController.cs`

## Dependencies

- `ItemElementController` - Template prefab for individual item display
- `ShowingObjectController` - Optional animation controller for showing items
- `Resource` - Data model representing game resources

## Core Features

1. **Multiple Overloads** - Supports `List<Resource>`, `Resource[]`, or single `Resource`
2. **Caching** - Maintains a list of created item controllers for later access
3. **Animations** - Optional showing animation via `ShowingObjectController`
4. **Ignore List** - Excludes specified transforms from being destroyed on clear

## Usage Examples

### Basic Usage - Display List of Items

```csharp
// Get reference to ItemPreviewController (usually via SerializeField)
[SerializeField] private ItemPreviewController _itemPreview;

// Initialize with list of resources
List<Resource> rewards = new List<Resource>
{
    new Resource(EnumBase.ResourceTypes.Money, 1, 100),
    new Resource(EnumBase.ResourceTypes.Item, 5, 1)
};
_itemPreview.InitData(rewards);
```

### Display with Options

```csharp
// View-only mode (no interaction)
_itemPreview.InitData(items, isViewOnly: true);

// Show remaining quantity
_itemPreview.InitData(items, showRemaining: true);

// Display full quantity without abbreviation
_itemPreview.InitData(items, showFullQuantity: true);

// Disable showing animation
_itemPreview.InitData(items, useAnim: false);

// Show check marks on owned items
Resource[] itemArray = GetRewardArray();
_itemPreview.InitData(itemArray, isShowChecked: true);
```

### Single Item Display

```csharp
Resource singleReward = new Resource(EnumBase.ResourceTypes.Money, 1, 500);
_itemPreview.InitData(singleReward, isViewOnly: true);
```

### Access Created Items

```csharp
// Get all created item controllers for further manipulation
List<ItemElementController> items = _itemPreview.GetItems();
foreach (var item in items)
{
    // Custom logic per item
}
```

### Clear Items

```csharp
// Remove all items from preview
_itemPreview.ClearItems();
```

## Method Reference

| Method | Parameters | Description |
|--------|------------|-------------|
| `InitData(List<Resource>)` | items, isViewOnly, showRemaining, useAnim, showFullQuantity | Initialize with list of resources |
| `InitData(Resource[])` | items, isViewOnly, showRemaining, useAnim, isShowChecked | Initialize with array of resources |
| `InitData(Resource)` | item, isViewOnly, showRemaining, useAnim | Initialize with single resource |
| `ClearItems()` | - | Destroy all item elements except ignored ones |
| `GetItems()` | - | Returns cached list of ItemElementController |

## Configuration

### Inspector Setup

1. **Item Template** (`_itemTemplate`): Assign the `ItemElementController` prefab
2. **List Ignore** (`_listIgnore`): Add transforms that should not be destroyed on clear

### Common Patterns

```csharp
// Pattern 1: Reward popup
public void ShowRewards(List<Resource> rewards)
{
    _itemPreview.ClearItems();
    _itemPreview.InitData(rewards, isViewOnly: true, useAnim: true);
}

// Pattern 2: Shop item display
public void DisplayShopItems(Resource[] items)
{
    _itemPreview.InitData(items, isViewOnly: false, showRemaining: true);
}

// Pattern 3: Inventory check
public void ShowCollectionStatus(Resource[] items)
{
    _itemPreview.InitData(items, isShowChecked: true);
}
```

## Notes

- Always call `ClearItems()` before re-initializing if you want to refresh the display
- The `_itemCached` list is only populated when using `List<Resource>` overload
- Animation is controlled by `ShowingObjectController` component on the same GameObject
- Items in `_listIgnore` will not be destroyed when calling `ClearItems()`
