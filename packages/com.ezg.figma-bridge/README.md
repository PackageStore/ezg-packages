# EZG Unity Figma Bridge

Imports a Figma document into Unity as native UGUI prefabs: screens, components, variants,
image fills and fonts, driven from an editor window and a settings asset.

## Install

Feature Hub (**Ezg > Feature Hub**, UPM Packages tab), or add it directly:

```json
"scopedRegistries": [
  { "name": "Easygoing code base", "url": "https://upm-registry-worker.developer-a1f.workers.dev", "scopes": ["com.ezg"] }
],
"dependencies": { "com.ezg.figma-bridge": "0.8.5" }
```

Requires Unity 6000.3 or newer. Dependencies (`com.unity.ugui`, `com.unity.nuget.newtonsoft-json`)
resolve automatically from the Unity registry.

## Setup

**Rest**

1. Open **Tools > EZG Technical Art > Figma Bridge** and select Rest.
2. Paste the Figma document URL.
3. Enter a [Figma personal access token](https://www.figma.com/developers/api#authentication) in
   the Rest block and Save. It is stored per machine in `PlayerPrefs`, never in the project or in
   source control.
4. Pick pages and screens, then Sync.

**Bridge**

1. Open EZG Tools in Figma and connect the MCP tab.
2. Open **Tools > EZG Technical Art > Figma Bridge** and select Bridge.
3. Pick the file if several are listed.
4. Pick pages and screens, then Sync. No URL and no token.

## Settings

The settings asset drives every path and toggle. The fields below sit in closed foldouts of the window. The fields worth knowing:

| Field | Controls | Foldout |
|---|---|---|
| `AssetsRootFolder` | root for every generated asset; blank = `Assets/_Project/UI` | Output Folders |
| `ScreenPrefabFolder` | where screen prefabs are written; blank = `<root>/Screens` | Output Folders |
| `ComponentPrefabFolder` | where component prefabs are written; blank = `<root>/Components` | Output Folders |
| `PagePrefabFolder` | where page prefabs are written; blank = `<root>/Pages` | Output Folders |
| `ImageFillFolder` | parent of the sprite folders; image fills go in `<folder>/<Figma document name>`; blank = `<root>/Sprites` | Output Folders |
| `OnlyImportListedScreens` | import just the named screens instead of every frame | main view |
| `CollapseSliceGrids` | collapse a 9-slice plate into one sprite with borders | Layout & Nine-Slice |

## Behaviour worth knowing

These are on by default and shape the output:

- **9-slice collapse.** A plate built as a grid of `slice_*` cells is imported as a single
  sprite carrying `Sprite.border`, not as nine child images.
- **Node-named image fills.** Sprites are named after the node and its owner rather than the
  Figma `imageRef` hash, so a re-import produces stable, readable asset names. A fill whose art
  is already on disk under one of its names keeps that file, so new art in Figma never renames
  (or swaps the art behind) the sprites that were there before it.
- **Stable object ids on re-import.** A component, screen or page prefab keeps the object id of
  each node that stayed at its place (same name path, same order among same-named siblings), even
  when many nodes share one name, so overrides other prefabs hold on it stay on that node. Placed
  instances carry no override that only repeats the component's own value.
- **`[ignore]` nodes.** A node whose name contains `[ignore]` (any case) is dropped from the
  document with its whole subtree before anything else runs: no sprite, no server render, no
  GameObject, not in the screen list. An ignored component master leaves its instances in place;
  they import as regular frames with their own children.
- **Plain `Image` output.** Every fill is a stock `UnityEngine.UI.Image`; there is no bridge
  image component or shader. A childless node with a stroke, corner radius, gradient, ellipse or
  star is server-rendered once and imported as a sprite sized to its render bounds. A frame with
  children that carries one of those keeps a flat coloured Image and is listed in
  `FigmaImportContext.ShapeOnlyNodes` for a post-processor.
- **Tint.** A layer that paints in one RGB imports as a white sprite and its colour goes in
  `Image.color`, so recolouring it in Figma or by variant changes a colour override, not a sprite.
  A server-rendered layer is **not** tinted when anything in it has a gradient, an image, pattern,
  video or shader paint, more than one RGB across its paints, any visible effect, a mask, a
  non-normal node or paint blend mode, or text with per-range styles; nor when it has no visible
  paint at all, or the node is also exported or is a pattern source. A frame shape (a frame with
  children whose own fills and stroke the bridge draws) is **not** tinted when the fills and stroke
  it draws are not one SOLID RGB (a gradient, two colours), or when it has a visible inner shadow
  that is not black or whose blend mode is not normal: a shape with a coloured inner shadow keeps
  a baked sprite. For a frame shape, children, masks, node and paint blend modes, shader fills, an
  `OUTSIDE` stroke and other effects (drop shadow included) are not read. A layer that is not
  tinted keeps its coloured sprite.
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
