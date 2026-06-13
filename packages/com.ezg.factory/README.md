# com.ezg.factory

Generic factory and cache-factory patterns for enum/string-keyed module registries, plus DataPlayer base classes for PlayerPrefs-backed player data modules with JSON serialization.

## Source

Extracted from `Assets/_Project/Core/Patterns/Factory` in the Merge Two game project.

## Contents

| Class | Purpose |
|-------|---------|
| `FactoryGeneric<TKeyType, TData>` | Creates new instances of modules by enum key (reflection-based) |
| `CacheFactoryGeneric<TKeyType, TData>` | Caches and retrieves singleton-like modules by enum key |
| `CacheFactoryGenericString<TData>` | Same as above but with string keys |
| `DataWithOption<TKey>` / `DataWithOptionString` | Base data wrappers holding a type key |
| `DataPlayerBase` / `DataPlayerBaseGeneric<T>` | Abstract base for PlayerPrefs-backed player data modules |
| `DataPlayer` | Static facade for managing all `DataPlayerBase` modules (Init/Save/Load/Clear) |
| `CrossAssemblySerializationBinder` | Newtonsoft JSON binder that falls back across `Assembly-CSharp`, `Ezg.Core`, etc. |

## Peer Requirements

The consuming project must provide:

- **Newtonsoft.Json** — e.g. `com.unity.nuget.newtonsoft-json` ≥ 3.0.0, or a `Newtonsoft.Json.dll` in the project.

## Runtime Notes

- `CrossAssemblySerializationBinder.FallbackAssemblies` contains the hardcoded string `"Ezg.Core"`. If your project uses `CrossAssemblySerializationBinder` and has types in an assembly named `Ezg.Core`, they will resolve correctly. Otherwise this entry is a no-op.
- `DataPlayer.Init()` scans all loaded assemblies for concrete `DataPlayerBase` subclasses. Call it once at startup before accessing any module.

## Phase 2 (not done here)

To switch the game to consume this package from the registry, remove `Assets/_Project/Core/Patterns/Factory` and add `"com.ezg.factory": "0.1.0"` to `Packages/manifest.json`. Keeping both the in-Assets copy and the registry dependency causes a duplicate-assembly conflict.
