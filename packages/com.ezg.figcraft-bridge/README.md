# EZG FigCraft Bridge

Imports a FigCraft design file into Unity as native UGUI prefabs:
screens, components, variants, image fills, renders and fonts, driven from an editor window and
a settings asset. Cloned from `com.ezg.figma-bridge` 0.8.19: everything after the document is
read (prefab building, 9-slice, instances, tint, fonts, post-processors, the visual check) is the
same code; the Figma REST API, the access token and the EZG Tools hub are gone.

## Install

Feature Hub (**Ezg > Feature Hub**, UPM Packages tab), or add it directly:

```json
"scopedRegistries": [
  { "name": "Easygoing code base", "url": "https://upm-registry-worker.developer-a1f.workers.dev", "scopes": ["com.ezg"] }
],
"dependencies": { "com.ezg.figcraft-bridge": "0.1.0" }
```

Requires Unity 6000.3 or newer and FigCraft's command-line tool `figcraft-cli` (found in
`~/.local/bin`, `/opt/homebrew/bin`, `/usr/local/bin` or `PATH`, or set **FigCraft CLI Path**).
It can sit beside `com.ezg.figma-bridge` in one project: its own namespace (`FigCraftBridge`),
assemblies, menu, settings asset (`Assets/FigCraftBridgeSettings.asset`) and cache paths.

## Setup

1. Open **Tools > EZG Technical Art > FigCraft Bridge**.
2. **File**: pick the `.figcraft` file (stored relative to the project, e.g. `Design/game.figcraft`).
   **FigCraft app**: start FigCraft with `--control 7979` and open the file; the import reads the
   file in front, unsaved changes included (image fills still come from the saved file).
3. Press **Refresh from file**, pick screens and components, then **Sync from .figcraft file**.

Screens come from the page named `Screens`, components from `Components` (both settable).

## How it reads FigCraft

| Step | FigCraft call |
|---|---|
| Document | one `inspect_document` (every page, full depth) through `figcraft-cli mcp`, converted to the importer's REST-shaped document (`Source/FigCraftToRest.cs`) and cached in `Library/FigCraftBridge/FigCraftOutput.json` |
| Renders (vectors, ellipses, booleans, shapes with stroke, radius, gradient or effect) | FigCraft's own `export` of each node to PNG at 1x, shadows and outside strokes included |
| Image fills | read straight from the `.figcraft` archive (`images/<sha256>`), no export |
| Visual check reference | `export` of the screen frame at its layout box |

Ids: a FigCraft layer keeps its id (`12`); a page becomes `0:<page id>`; a layer inside an
instance becomes `I<instance>;<master layer>`, paired with the master's layer at the same place,
so overrides and swaps resolve against the component prefab.

Known gaps (improve in later versions): rotated layers use FigCraft's x/y as the transform origin;
gradient handles come from FigCraft's `from`/`to`/`width`; per-character text styles are not read;
no prototype links are read from FigCraft interactions.

## Settings

The settings asset drives every path and toggle. The fields below sit in closed foldouts of the window. The fields worth knowing:

| Field | Controls | Foldout |
|---|---|---|
| `AssetsRootFolder` | root for every generated asset; blank = `Assets/_Project/UI` | Output Folders |
| `ScreenPrefabFolder` | where screen prefabs are written; blank = `<root>/Screens` | Output Folders |
| `ComponentPrefabFolder` | where component prefabs are written; blank = `<root>/Components` | Output Folders |
| `PagePrefabFolder` | where page prefabs are written; blank = `<root>/Pages` | Output Folders |
| `ImageFillFolder` | parent of the sprite folders; image fills go in `<folder>/<document name>`; blank = `<root>/Sprites` | Output Folders |
| `OnlyImportListedScreens` | import just the named screens instead of every frame | main view |
| `CollapseSliceGrids` | collapse a 9-slice plate into one sprite with borders | Layout & Nine-Slice |

## Behaviour worth knowing

These are on by default and shape the output:

- **9-slice collapse.** A plate built as a grid of `slice_*` cells is imported as a single
  sprite carrying `Sprite.border`, not as nine child images.
- **Node-named image fills.** Sprites are named after the node and its owner rather than the
  image hash, so a re-import produces stable, readable asset names. A fill whose art
  is already on disk under one of its names keeps that file, so new art in FigCraft never renames
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
  `Image.color`, so recolouring it in FigCraft or by variant changes a colour override, not a sprite.
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
- **Pattern fills.** A *pattern* paint (a node repeated across a shape) has no bitmap of
  its own; the bridge renders the source node once in FigCraft into `ServerRenderedImages/` and draws
  the fill as a tiled `Image` at design tile size. One render per distinct source.
- **Axis-intent output.** A component set writes `axis-intent.json` beside its variant prefabs
  recording each variant axis. Variants are separate prefabs; there is no runtime state code.
- **Fonts.** Font families named by the document are fetched from Google Fonts and placed
  in `Assets/TextMesh Pro/Fonts`. A family Google does not serve needs a substitute set in the
  settings asset (`FontOverride`).

## Post-processing and offline work

The prefabs the bridge writes are raw output: no controller, no project templates, every fill
an `Image`. A project shapes them into its own screens with a **post-processor**:

```csharp
using FigCraftBridge.Editor.PostProcess;

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
`Sync Document`, and again from **Run Post-Processors (no Sync)** without calling FigCraft:
each import writes a `<Screen>.instances.json` sidecar beside the screen prefab carrying the
instance data the prefab itself no longer holds once the bridge markers are stripped.

**Re-import from cache (offline)** rebuilds the whole output from
`Library/FigCraftBridge/FigCraftOutput.json` (the document the last Sync read) and the sprites
already on disk, without calling FigCraft. Use it to iterate on settings.

Settings worth knowing:

| Setting | Use it when |
|---|---|
| `AddLayoutElements = OnlyUnderAutoLayout` | nothing reads the per-node `LayoutElement`; only children of auto-layout frames keep one. |
| `FontOverride` | the project has one font and the design's family should never be downloaded. |
| `TextFitMode = FixedRectAutoSize` | the real font differs from the design's, so auto-resized labels need a padded fixed rect and TMP auto-size instead of a `ContentSizeFitter`. |
| `ButtonNamePattern = ""` | the project adds its own Button stack; the bridge must not guess buttons from names. |
| `NumberDuplicateSiblings` | bindings resolve nodes by name and the design has two siblings with one name. |

## Companion skill

`figcraft-to-unity` drives this package from Claude Code and checks the import against
FigCraft's render.

## Licence

MIT. See [LICENSE](LICENSE) for the full notice and copyright holders.
