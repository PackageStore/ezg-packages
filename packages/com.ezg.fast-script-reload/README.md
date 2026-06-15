# EZG Fast Script Reload

`com.ezg.fast-script-reload`

Hot-reloads C# script edits into the running Unity Editor (and optionally into
builds) without exiting Play mode or triggering a full domain reload. This is a
vendored distribution of [Immersive VR Tools' **Fast Script Reload**](https://immersivevrtools.com/),
packaged for the EZG scoped registry. It bundles its own Harmony and Roslyn DLLs,
so no additional packages are required.

## Package ↔ source folder

This package mirrors `Assets/Plugins/FastScriptReload` from the game project:

| Package folder | Source folder | Assembly |
|---|---|---|
| `Runtime/` | `Scripts/Runtime/` | `FastScriptReload.Runtime` |
| `Editor/` | `Scripts/Editor/` | `FastScriptReload.Editor` |
| `Plugins/` | `Plugins/` | Bundled DLLs (Harmony, Roslyn, ImmersiveVRTools.Common) |
| `Documentation/` | `Documentation/` | Vendor manual + release notes |

The two assembly definitions keep their original vendor names
(`FastScriptReload.Runtime`, `FastScriptReload.Editor`) so existing references
and the vendor's own DLL bindings remain valid.

## Dependencies

- **Registry (`package.json`):** none.
- **Peer requirements:** none. The package ships the Harmony, Roslyn and
  ImmersiveVRTools.Common DLLs it needs via `overrideReferences` /
  `precompiledReferences`.

## Notes

- `FastScriptReload.Runtime` is gated by the define constraint
  `UNITY_EDITOR || LiveScriptReload_IncludeInBuild_Enabled`, matching the
  vendor's original build behaviour.
- Examples shipped with the original asset are intentionally excluded from this
  package.
- See `Third-Party Notices.txt` and the per-library license files under
  `Plugins/` for licensing terms.

## Installation

```json
"scopedRegistries": [
  {
    "name": "Easygoing code base",
    "url": "https://upm-registry-worker.developer-a1f.workers.dev",
    "scopes": ["com.ezg"]
  }
],
"dependencies": {
  "com.ezg.fast-script-reload": "0.1.0"
}
```
