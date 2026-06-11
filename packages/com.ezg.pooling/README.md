# EZG Pooling

Game-agnostic GameObject pooling for Unity. Spawn and despawn pooled prefabs by reference or by
resource path, with generic component pools, delayed despawn, and a static `PoolService` facade.

## Package ↔ source mapping

Extracted from `Assets/_Project/Core/Adapter/PoolAdapter/PoolingModule` in the game project.

| Type | Role |
|------|------|
| `PoolService` | Static facade — `Spawn`/`Despawn`/`OnDespawn` + transform helper extensions |
| `PoolingManager` | Pool registry — `Show` / `ShowWithModel` / `PreInit` / `ClearPool` (singleton) |
| `SpawnerManager` | Persistent (`DontDestroyOnLoad`) parking parent + delayed-callback helper (singleton) |
| `PoolingComponent` | Per-instance hook that returns the object to its pool on `OnDisable` |
| `PoolingDataModel<T>` | Wrapper holding a pooled GameObject + its controller component |
| `IPoolingModule` | Return-to-pool callback contract |
| `Singleton<T>` | Vendored MonoBehaviour singleton base (keeps the package self-contained) |
| `PoolResources` | Prefab resolver hook — defaults to `Resources`, overridable by the host |

## Dependencies

This package declares **no** `com.ezg.*` package dependencies — it is self-contained.

## Peer requirements

The consuming project must already provide:

- **UniTask** (`com.cysharp.unitask`) — assembly `UniTask`. Used by `SpawnerManager` for delayed despawn.

## Resolving prefabs by path

The path-based overloads (`PoolService.SpawnGameObject(path)`, `Spawn<T>(string path)`) resolve prefabs
through `PoolResources.Loader`, which defaults to `Resources.Load`. To route through a custom asset
pipeline (AssetBundles, Addressables, or the host game's own loader) set it once at startup:

```csharp
PoolResources.Loader = (path, type) => MyAssetPipeline.Load(path, type);
```

## Usage

```csharp
// Spawn / despawn by prefab reference
var fx = PoolService.Spawn(prefab, parent);
PoolService.Despawn(fx, time: 1.5f);

// Spawn a typed component pool
MyView view = PoolService.Spawn<MyView>(viewPrefab);

// Spawn by resource path
GameObject go = PoolService.SpawnGameObject("Prefabs/Coin");
```

## Notes

- `PoolingManager` / `SpawnerManager` are `DontDestroyOnLoad` singletons; pooled objects parked under
  `SpawnerManager` survive scene loads, while `PoolingManager` clears its pools on scene change.
- Vietnamese comments from the original source are intentionally preserved.
