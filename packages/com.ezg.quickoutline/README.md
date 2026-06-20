# EZG Quick Outline

Per-object outline / silhouette effect for 3D meshes. Add the `QuickOutline`
component to any GameObject to render a colored outline around its mesh (and the
meshes of its children) using a two-pass mask + fill material technique.

Based on **Quick Outline** by Chris Nolet (Unity Asset Store, asset id 115488).

## Package ↔ source folder

| Package path | Origin |
|---|---|
| `Runtime/Scripts/QuickOutline.cs` | `Assets/_Project/3rdParty/QuickOutline/Scripts/QuickOutline.cs` |
| `Runtime/Resources/Materials/OutlineMask.mat`, `OutlineFill.mat` | `.../Resources/Materials/` |
| `Runtime/Resources/Shaders/OutlineMask.shader`, `OutlineFill.shader` | `.../Resources/Shaders/` |

The two outline materials are loaded at runtime via `Resources.Load`, so the
`Runtime/Resources/` folder must ship with the package (it does).

## Usage

```csharp
var outline = gameObject.AddComponent<QuickOutline>();
outline.OutlineMode  = QuickOutline.Mode.OutlineAll;
outline.OutlineColor = Color.yellow;
outline.OutlineWidth = 5f;
```

For best results toggle `outline.enabled` rather than adding/removing the
component. For large meshes, enable **Precompute Outline** in the inspector to
move the per-vertex smooth-normal work into the editor.

## Dependencies

- `com.ezg.*` registry dependencies: **none**

## Peer requirements

- **none** — uses only `UnityEngine` and `System.*`.

## Notes

- The `QuickOutline` MonoBehaviour lives in the **global namespace**. When a
  project consumes this package from the registry, remove any in-project copy of
  the script first to avoid a duplicate-type compile conflict.
- If the outline appears off-center, enable *Read/Write* on the model import
  settings and disable *Optimize Mesh Data* in Player Settings.
