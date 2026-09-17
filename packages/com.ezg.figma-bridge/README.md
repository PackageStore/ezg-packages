# EZG Unity Figma Bridge

Imports a Figma document into Unity as native UGUI prefabs: screens, components, variants,
image fills and fonts, driven from an editor window and a settings asset.

## Install

Feature Hub (**Ezg > Feature Hub**, UPM Packages tab), or add it directly:

```json
"scopedRegistries": [
  { "name": "Easygoing code base", "url": "https://upm-registry-worker.developer-a1f.workers.dev", "scopes": ["com.ezg"] }
],
"dependencies": { "com.ezg.figma-bridge": "0.1.0" }
```

Requires Unity 6000.3 or newer. Dependencies (`com.unity.ugui`, `com.unity.nuget.newtonsoft-json`)
resolve automatically from the Unity registry.

## Setup

1. Open **Tools > EZG Technical Art > Figma Bridge**.
2. Paste the Figma document URL.
3. Set a [Figma personal access token](https://www.figma.com/developers/api#authentication).
   It is stored per machine in `PlayerPrefs`, never in the project or in source control.
4. Choose which pages and screens to import, then sync.

## Settings

The settings asset drives every path and toggle. The fields worth knowing:

| Field | Controls |
|---|---|
| `AssetsRootFolder` | root for every generated asset; blank = `Assets/_Project/UI` |
| `ScreenPrefabFolder` | where screen prefabs are written; blank = `<root>/Screens` |
| `ComponentPrefabFolder` | where component prefabs are written; blank = `<root>/Components` |
| `PagePrefabFolder` | where page prefabs are written; blank = `<root>/Pages` |
| `ImageFillFolder` | parent of the sprite folders; image fills go in `<folder>/<Figma document name>`; blank = `<root>/Sprites` |
| `OnlyImportListedScreens` | import just the named screens instead of every frame |
| `CollapseSliceGrids` | collapse a 9-slice plate into one sprite with borders |

## Behaviour worth knowing

These are on by default and shape the output:

- **9-slice collapse.** A plate built as a grid of `slice_*` cells is imported as a single
  sprite carrying `Sprite.border`, not as nine child images.
- **Node-named image fills.** Sprites are named after the node and its owner rather than the
  Figma `imageRef` hash, so a re-import produces stable, readable asset names.
- **`[ignore]` nodes.** A node whose name contains `[ignore]` (any case) is dropped from the
  document with its whole subtree before anything else runs: no sprite, no server render, no
  GameObject, not in the screen list. An ignored component master leaves its instances in place;
  they import as regular frames with their own children.
- **Plain `Image` output.** Every fill is a stock `UnityEngine.UI.Image`; there is no bridge
  image component or shader. A childless node with a stroke, corner radius, gradient, ellipse or
  star is server-rendered once and imported as a sprite sized to its render bounds. A frame with
  children that carries one of those keeps a flat coloured Image and is listed in
  `FigmaImportContext.ShapeOnlyNodes` for a post-processor.
- **Pattern fills.** A Figma *pattern* paint (a node repeated across a shape) has no bitmap of
  its own; the bridge server-renders the source node once into `ServerRenderedImages/` and draws
  the fill as a tiled `Image` at design tile size. Costs one render request per distinct source.
- **Axis-intent output.** A component set writes `axis-intent.json` beside its variant prefabs
  recording each variant axis. Variants are separate prefabs; there is no runtime state code.
- **Fonts.** Font families named by the document are fetched live from Google Fonts and placed
  in `Assets/TextMesh Pro/Fonts`. A family Figma names but Google does not serve needs a
  substitute set in the settings asset.

## Post-processing and offline work (0.3.0)

The prefabs the bridge writes are raw output: no controller, no project templates, every fill
an `Image`. A project shapes them into its own screens with a **post-processor**:

```csharp
using UnityFigmaBridge.Editor.PostProcess;

public sealed class MyScreenBuilder : IFigmaImportPostProcessor
{
    public int Order => 100;
    public void OnDocumentImported(FigmaImportContext ctx)
    {
        foreach (var screen in ctx.Screens)           // raw screen prefab + its component instances
            foreach (var instance in screen.Instances) // NodeName, HierarchyPath, ComponentPrefabPath, SourcePathChain
                { /* map instance.ComponentPrefabPath to a project template, build a variant, ... */ }
    }
}
```

Drop the class in any Editor assembly; `TypeCache` finds it. It runs at the end of every
`Sync Document`, and again from **Run Post-Processors (no Sync)** without touching the network:
each import writes a `<Screen>.instances.json` sidecar beside the screen prefab carrying the
instance data the prefab itself no longer holds once the bridge markers are stripped.

**Re-import from cache (offline)** rebuilds the whole output from `Assets/FigmaOutput.json` (the
document the last online Sync cached) and the sprites already on disk. Fills that were never
downloaded are listed in one warning. This is the way to iterate on settings when the Figma seat's
API quota (20 Tier-1 requests a month on a View/Collab seat) is spent.

Settings added in 0.3.0 and what they are for:

| Setting | Use it when |
|---|---|
| `AddLayoutElements = OnlyUnderAutoLayout` | nothing reads the per-node `LayoutElement`; only children of auto-layout frames keep one. |
| `FontOverride` | the project has one font and Figma's family should never be downloaded. |
| `TextFitMode = FixedRectAutoSize` | the real font differs from Figma's, so auto-resized labels need a padded fixed rect and TMP auto-size instead of a `ContentSizeFitter`. |
| `ButtonNamePattern = ""` | the project adds its own Button stack; the bridge must not guess buttons from names. |
| `NumberDuplicateSiblings` | bindings resolve nodes by name and Figma has two siblings with one name. |

## Companion skill

`figma-to-unity` in Feature Hub's AI Feature tab drives this package from Claude Code, and
`psd-to-figma`, `figma-components`, `figma-tokens` and `figma-hygiene` cover the
design-file work that feeds it.

## Licence

MIT. See [LICENSE](LICENSE) for the full notice and copyright holders.
