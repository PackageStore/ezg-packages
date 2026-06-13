# com.ezg.singleton

Thread-safe Singleton base classes for Unity.

## Classes

- **`Singleton<T>`** — MonoBehaviour-based singleton. Auto-creates a DontDestroyOnLoad GameObject on first access.
- **`SingletonNormal<T>`** — Plain C# singleton using `Lazy<T>`. No Unity dependency.

Both classes live in the `Ezg.Package.Singleton` namespace.

## Source mapping

| Package path | Source repo path |
|---|---|
| `Runtime/` | `Assets/_Project/Core/Patterns/Singleton/` |

## Assembly

`Ezg.Singleton` (`Runtime/Ezg.Singleton.asmdef`)

## Dependencies

None — only `UnityEngine` (engine auto) and `System` BCL.

## Peer requirements

None.
