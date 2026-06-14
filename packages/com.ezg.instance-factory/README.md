# EZG Instance Factory

High-performance object instantiation utilities using compiled expression trees. Zero Unity dependency — works in any .NET/Mono context.

## Source folder

`Assets/_Project/Core/Patterns/InstanceFactory` in the Merge Two game project.

## Classes

- **`InstanceFactory`** — creates instances of a `Type` at runtime using cached compiled constructor delegates (0–3 typed args). Falls back to `Activator.CreateInstance` for >3 args or null args.
- **`InstanceFactoryGeneric<TArg1,TArg2,TArg3>`** — underlying generic helper; cached per-type delegate.
- **`InstanceManager`** — enumerates and instantiates all non-abstract subclasses of a given type via reflection.
- **`TypeToIgnore`** — internal placeholder for unused generic type slots.

## Namespace

`Ezg.Package.InstanceFactory`

## Installation

Add to `Packages/manifest.json`:
```json
"scopedRegistries": [{ "name": "Easygoing code base", "url": "https://upm-registry-worker.developer-a1f.workers.dev", "scopes": ["com.ezg"] }],
"dependencies": { "com.ezg.instance-factory": "0.2.0" }
```

## Dependencies

None — pure BCL (`System.*`) only.

## Peer requirements

None.

## Breaking changes

- **0.2.0** — Namespace renamed from `Ezg.Packages.InstanceFactory` to `Ezg.Package.InstanceFactory`. Update all `using` directives accordingly.
