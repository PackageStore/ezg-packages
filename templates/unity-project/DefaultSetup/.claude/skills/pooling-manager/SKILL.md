---
name: pooling-manager
description: Guide for using PoolingManager to efficiently spawn and recycle GameObjects using object pooling pattern
---

# PoolingManager

Object Pooling system that reuses GameObjects instead of continuously creating/destroying them, improving game performance.

## Architecture

```
PoolingManager (Singleton)
├── _pools: Dictionary<string, Queue<GameObject>>     // Pool for plain GameObjects
└── _poolsT: Dictionary<string, Queue<object>>        // Pool for objects with Controller type T
```

## Usage

### 1. Spawn Plain GameObject (No Controller)

```csharp
// Get object from pool (or create new if pool is empty)
GameObject obj = PoolingManager.Instance.Show(prefab, position, parent);

// With rotation
GameObject obj = PoolingManager.Instance.Show(prefab, position, rotation, parent);

// Don't show immediately (for setup before displaying)
GameObject obj = PoolingManager.Instance.Show(prefab, position, parent, isShowObject: false);
```

### 2. Spawn GameObject With Controller (Generic)

When prefab has a controller component, use generic method to get controller directly:

```csharp
// Get controller from pool
ItemController item = PoolingManager.Instance.Show<ItemController>(prefab, position, parent);

// For UI elements (uses localPosition instead of position)
UIItem uiItem = PoolingManager.Instance.Show<UIItem>(uiPrefab, localPos, parent, isUI: true);

// Get full data model (know if object is newly created or from pool)
PoolingDataModel<ItemController> data = PoolingManager.Instance.ShowWithModel<ItemController>(prefab, position, parent);
if (data.IsNewCreate)
{
    // First-time setup for new object
}
```

### 3. Return Object To Pool

**Simple way**: Just `SetActive(false)` - PoolingComponent automatically returns to pool via `OnDisable()`:

```csharp
obj.SetActive(false);  // Auto return to pool
```

**Manual way** (if needed):

```csharp
// For plain GameObject
PoolingManager.Instance.ReturnPool(prefab, instance);

// For object with controller
PoolingManager.Instance.ReturnPool<ItemController>(prefab, poolingDataModel);
```

### 4. Pre-warm Pool

Create objects in pool beforehand to avoid lag on first spawn:

```csharp
// Pre-warm 10 objects
PoolingManager.Instance.PreInit(prefab, 10, Quaternion.identity, parent);

// Pre-warm with controller type
PoolingManager.Instance.PreInit<ItemController>(prefab, 10, Quaternion.identity, parent);
```

### 5. Clear Pool

```csharp
// Clear pool for 1 prefab
PoolingManager.Instance.ClearPool(prefab);
PoolingManager.Instance.ClearPool("PrefabName");

// Clear all cache (usually used when changing scenes)
PoolingManager.Instance.ClearCache();

// Re-enable after clear
PoolingManager.Instance.EnableReturnPool();
```

### 6. Instantiate Without Pooling (Static Helper)

When need to create object without using pool:

```csharp
ItemController item = PoolingManager.Instantiate<ItemController>(prefab, parent);
UIItem uiItem = PoolingManager.Instantiate<UIItem>(uiPrefab, localPos, parent, isUI: true);
```

## Data Model

```csharp
public class PoolingDataModel<T>
{
    public GameObject GObject;    // GameObject instance
    public T Controller;          // Component controller
    public bool IsNewCreate;      // true = newly created, false = from pool
}
```

## Important Notes

| Issue | Solution |
|-------|----------|
| Pool key based on `prefab.name` | Ensure prefab names are unique |
| Object auto-returns when disabled | Don't call `ReturnPool` manually after `SetActive(false)` |
| UI positioning | Use `isUI: true` to set `localPosition` instead of `position` |
| Scene change | Pool auto-clears when loading new scene |

## Practical Examples

### Spawn list of items

```csharp
[SerializeField] private GameObject _itemPrefab;
[SerializeField] private Transform _container;
private List<ItemController> _spawnedItems = new();

public void ShowItems(List<ItemData> dataList)
{
    // Clear old items
    foreach (var item in _spawnedItems)
        item.gameObject.SetActive(false);  // Auto return to pool
    _spawnedItems.Clear();
    
    // Spawn new items
    foreach (var data in dataList)
    {
        var item = PoolingManager.Instance.Show<ItemController>(_itemPrefab, Vector3.zero, _container);
        item.Setup(data);
        _spawnedItems.Add(item);
    }
}
```

### Spawn UI elements

```csharp
[SerializeField] private GameObject _slotPrefab;
[SerializeField] private Transform _slotsContainer;

public void CreateSlots(int count)
{
    for (int i = 0; i < count; i++)
    {
        var slot = PoolingManager.Instance.Show<SlotController>(
            _slotPrefab, 
            Vector3.zero, 
            _slotsContainer, 
            isUI: true
        );
        slot.Setup(i);
    }
}
```
