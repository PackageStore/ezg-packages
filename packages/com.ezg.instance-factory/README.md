# com.ezg.instance-factory

High-performance object instantiation utilities using compiled expression trees. Pure C# — no Unity engine dependency.

## Package ↔ Source folder

| Package path | Source path |
|---|---|
| `Runtime/InstanceFactory.cs` | `Assets/_Project/Core/Patterns/InstanceFactory/InstanceFactory.cs` |
| `Runtime/InstanceManager.cs` | `Assets/_Project/Core/Patterns/InstanceFactory/InstanceManager.cs` |

## API

### `InstanceFactory` (static)

Caches compiled constructor delegates — faster than `Activator.CreateInstance` after the first call.

```csharp
using Ezg.Packages.InstanceFactory;

// No args
object obj = InstanceFactory.CreateInstance(typeof(MyClass));

// Typed args (0–3)
object obj = InstanceFactory.CreateInstance<string>(typeof(MyClass), "hello");
object obj = InstanceFactory.CreateInstance<int, string>(typeof(MyClass), 42, "world");

// Params overload (falls back to Activator for >3 args or null args)
object obj = InstanceFactory.CreateInstance(typeof(MyClass), arg1, arg2);
```

### `InstanceManager` (static)

Enumerate and instantiate all concrete subclasses of a type within its assembly.

```csharp
IEnumerable<MyBase> instances = InstanceManager.GetEnumerableOfType<MyBase>(/* optional ctor args */);
```

## Assembly

| Name | `noEngineReferences` |
|---|---|
| `Ezg.InstanceFactory` | `true` (pure BCL) |

## Dependencies

None — no `com.ezg.*` or external peer requirements.

## Phase 2 consumers (switch to registry after publish)

- `Assets/_Project/Core/Modules/Stats/RPGStatCollection.cs`
