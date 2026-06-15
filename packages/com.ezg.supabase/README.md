# EZG Supabase

`com.ezg.supabase` — the [Supabase C# client](https://github.com/supabase-community/supabase-csharp)
and its transitive dependencies, vendored for Unity as a **self-contained DLL bundle**.

There is **no first-party source** in this package: every assembly is the original
NuGet `netstandard2.0` build, carried over with its Unity `PluginImporter` `.meta`
(GUIDs and platform settings preserved). It is the same set of DLLs that used to live
under `Assets/Supabase/` (NuGetForUnity-managed) in the game project, now repackaged so
multiple projects can consume it from the scoped registry instead of vendoring it each.

## Bundled assemblies

| Area | DLLs |
|------|------|
| Supabase client | `Supabase`, `Supabase.Core`, `Supabase.Gotrue`, `Supabase.Postgrest`, `Supabase.Realtime`, `Supabase.Storage`, `Supabase.Functions` |
| Transport / util | `Websocket.Client`, `System.Reactive`, `JWTDecoder`, `MimeMapping` |
| BCL back-ports | `System.Threading`, `System.Threading.Channels`, `System.Threading.Tasks`, `System.Threading.Tasks.Extensions` |

Source versions (NuGet): supabase-csharp 0.16.2, supabase-core 0.0.3, gotrue-csharp 4.2.1,
postgrest-csharp 3.5.1, realtime-csharp 6.0.4, supabase-storage-csharp 1.4.0,
functions-csharp 1.3.2, Websocket.Client 4.6.1, System.Reactive 5.0.0, JWTDecoder 0.9.2,
MimeMapping 2.0.0.

## Peer requirements (NOT bundled — the consuming project must provide them)

- **Newtonsoft.Json 13.x** — required by `Supabase`, `Supabase.Postgrest` and
  `Supabase.Functions`. Install Unity's package `com.unity.nuget.newtonsoft-json`
  (3.2.x ships Newtonsoft.Json 13.x) or provide an equivalent `Newtonsoft.Json.dll`.
  This dependency is intentionally **not** declared in `package.json` (it resolves from
  Unity's registry, not the scoped EZG registry) and is **not** vendored here to avoid a
  duplicate-`Newtonsoft.Json.dll` conflict in projects that already have it.

## Notes

- **Marker assembly.** `Runtime/Ezg.Supabase.asmdef` is an intentionally empty assembly
  definition (it compiles no code). It exists only to satisfy the registry's validation
  gate, which requires every package to contain at least one `.asmdef`. The DLLs are
  plain Unity plugins (auto-referenced via their own `.meta`), so consuming code can
  `using Supabase;` directly without referencing `Ezg.Supabase`.
- **IL2CPP / mobile.** These libraries (Supabase, System.Reactive, Newtonsoft.Json) use
  reflection. If you ship an IL2CPP build (iOS/Android) and hit `ExecutionEngineException`
  / missing-type errors, add a `link.xml` in your project that preserves the affected
  assemblies (`Supabase.*`, `Newtonsoft.Json`, `System.Reactive`). None is bundled here
  because strip behavior is a consuming-project concern.
- **NuGet-restore artifacts removed.** The original `*.nupkg` and `*.signature.p7s` files
  (~8 MB, never used by Unity) were stripped; only the runtime DLLs + their `.meta`,
  nuspec/readme/icon/license files remain.

## Install

`Packages/manifest.json`:

```json
{
  "scopedRegistries": [
    {
      "name": "Easygoing code base",
      "url": "https://upm-registry-worker.developer-a1f.workers.dev",
      "scopes": ["com.ezg"]
    }
  ],
  "dependencies": {
    "com.ezg.supabase": "0.1.0",
    "com.unity.nuget.newtonsoft-json": "3.2.1"
  }
}
```
