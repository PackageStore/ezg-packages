---
name: figma-to-unity
description: Import Figma screens into Unity as UGUI prefabs with the UnityFigmaBridge (com.simonoliver.unityfigma). Use when asked to "import a screen from Figma", "bring the Figma UI into Unity", "re-import a screen after the designer changed Figma", or when given a figma.com/design link to a screen frame.
argument-hint: [Figma link or frame name]
---

# Figma to Unity screens

**The rule:** a Figma COMPONENT is a Unity prefab; a Figma INSTANCE is a prefab
instance (`PrefabUtility.InstantiatePrefab`); a COMPONENT_SET is a folder of
prefabs, one per variant. Screens contain prefab instances, not baked copies of
component internals.

| Piece | Where |
|---|---|
| Bridge (private copy of simonoliver/UnityFigmaBridge) | sibling repo `../UnityFigmaBridge`, consumed via a `file:` path in `Packages/manifest.json` |
| Divergence log | `FORK_NOTES.md` in the bridge repo |
| Upstream remote (read-only) | `upstream` -> `https://github.com/simonoliver/UnityFigmaBridge.git` |
| Settings asset | `Assets/UnityFigmaBridgeSettings.asset` |
| Screen prefabs | `ScreenPrefabFolder` on the settings asset |
| Component prefabs | `ComponentPrefabFolder` on the settings asset |
| Image fills | `ImageFillFolder` on the settings asset |

## Main checkout only

The `file:` path in `Packages/manifest.json` resolves relative to `Packages/`,
which is gitignored and exists only in the main checkout (the project repo
root). It does not resolve from a sibling worktree checkout. Run imports from
whichever branch is checked out in the main checkout — that is the only place
`Packages/` exists.

## Never restart Unity

Killing the editor takes down the MCP server at `http://127.0.0.1:8080` and you
lose all control until the user reopens it by hand. Everything below is reachable
over MCP:

- Picked up a changed `Packages/manifest.json` or a new embedded package:
  `UnityEditor.PackageManager.Client.Resolve()` through `execute_code`
- Recompile: `refresh_unity`, or `CompilationPipeline.RequestScriptCompilation()`
- Stale build artifacts: `RequestScriptCompilationOptions.CleanBuildCache`

## The token

A Figma personal access token is resolved in this order by `FigmaAccessToken.Read()`
(`Editor/Utils/FigmaAccessToken.cs`):

1. `$FIGMA_TOKEN` environment variable
2. `.figma_token` file at the Unity project root (beside `Assets/`), gitignored
3. `PlayerPrefs` (legacy fallback)

The token is never logged. Do not put it in the settings asset — that is a
ScriptableObject under version control.

## Run it

Menu path: **Figma Bridge / Sync Document**

Drive with `mcp__UnityMCP__execute_menu_item`, then read `[FIGMA]` and
`[NineSlicePass]` lines from `~/Library/Logs/Unity/Editor.log`. The MCP
`read_console` also works and is easier to filter.

**First run, or after pages change in Figma:** the import aborts with
"The pages found in the Figma document have changed". It writes the new page
list into the settings asset and selects it in the Inspector. Tick the pages you
want, then Sync again.

**Do not edit any script while a download is running.** The download is async;
saving a `.cs` triggers a domain reload that silently kills it — the only symptom
is a log that stops at "Connecting 0 %".

### BuildPrototypeFlow = false

The settings asset sets `BuildPrototypeFlow = false`. The import still creates a
temporary Canvas in the active scene (`CreateCanvas(false)` at
`UnityFigmaBridgeImporter.cs:438`) and destroys it in `CleanUpPostGeneration`.
This is expected — the Canvas is a build surface, not a scene object. If you see
an unexplained Canvas appear and disappear during import, that is this path.

### Page prefabs

`FigmaAssetGenerator.cs:42-46` saves one prefab per selected page into
`FigmaPaths.FigmaPagePrefabFolder`. Nothing reads these prefabs; they are a
side-effect of the upstream design. They are harmless and are cleaned up on
re-import (only `.prefab` files carrying `FigmaNodeObject` are deleted).

## Adding a screen

Add a row to the `ScreenNameOverrides` list on the settings asset: `FrameName`
is the Figma frame name (e.g. `<FrameName>`), `PrefabName` is the output prefab
filename (e.g. `Screen_<FrameName>`).

When `OnlyImportListedScreens` is true (the current setting), only frames with a
matching row are imported. A FRAME whose parent is a CANVAS or SECTION and has no
matching row is skipped — `FigmaPaths.GetPathForScreenPrefab` returns `null` and
`FigmaAssetGenerator.BuildFigmaNode:229-230` guards on that.

No code change needed. It is a data change on the settings asset.

## Finding a component prefab

See [reference/prefab-contract.md](reference/prefab-contract.md) for the full
node-type-to-asset table.

Summary: standalone components land at
`<ComponentPrefabFolder>/<SafeName>.prefab`. Variants of a COMPONENT_SET land at
`<ComponentPrefabFolder>/<SetName>/<NormalisedVariant>.prefab`, where `=` becomes
`-` and `, ` becomes `_`. Each set folder also gets an `axis-intent.json`.

## 9-slice

The bridge already renders each cell of a `slice_ROW_COL` grid correctly via
`FigmaImage.ImageTransform` and constraint-derived anchors. Nine-slice collapse
is an **optimisation**, not a correctness fix: it replaces nine `FigmaImage`
components (each with its own dynamic material) with one plain `Image` and a
shared sliced sprite.

**Toggle:** `CollapseSliceGrids` on the settings asset. Currently **off**.
Turning it on enables the `NineSlicePass` that runs after component
instantiation.

**A wrong border after collapse is a regression.** The uncollapsed rendering is
already correct. Validate borders against the project's known-good nine-slice
border reference before shipping with the toggle on.

The pass operates on component and screen prefabs. It detects grids by the
`slice_ROW_COL` naming convention, measures borders from parent-local position
(never `anchoredPosition`), and writes the border into the sprite's
TextureImporter. Cells with stroke or corner radius are skipped (a plain `Image`
cannot reproduce those).

## What the importer does well

Worth knowing so you do not rebuild it:

- **Constraints become anchors.** `NodeTransformManager.ApplyFigmaConstraints`
  (`:79`) reads the Figma `constraints` node and sets `anchorMin`/`anchorMax`
  accordingly, with position offsets for CENTER/RIGHT/BOTTOM and size-delta
  adjustments for LEFT_RIGHT/TOP_BOTTOM/SCALE. `AnchorPositionsForFigmaConstraints`
  (`:190`) is the mapping table.
- **`imageTransform` crops are honoured.** `FigmaNodeManager.SetupImageFill`
  (`:311`) reads `fill.imageTransform` for STRETCH-mode fills and passes it to
  `FigmaImage.ImageTransform`. `FigmaImage.OnPopulateMesh` (`:306-316`) uses it
  to calculate UV coordinates. The previous vendored importer ignored
  `imageTransform` entirely, drawing the whole source image in every slice cell;
  this was the main reason it was replaced.
- **Image fills come back as the original uploaded PNG.** The API's
  `GetDocumentImageFillData` returns per-`imageRef` assets at full resolution.
  Sprite assignment comes from the node's own fill, so cross-assignment is
  impossible by construction.
- **Server-rendered vectors.** Nodes that cannot be reproduced with UGUI primitives
  (complex vectors, boolean operations) are rendered server-side as PNGs and
  imported as simple `Image` sprites.
- **Text becomes TextMeshPro.** Font mapping uses `FontManager`. Google Fonts
  download is configurable (`EnableGoogleFontsDownloads` on the settings asset;
  currently off for this project).

## What it does not do

- **No state code.** Variants are separate prefabs. There is no
  `Selectable`, no `SpriteSwap`, no runtime variant-switching logic.
- **Axis intent is recorded but unread.** `ComponentAxisIntent.WriteAxisIntent`
  parses the `UNITY:` directive from each component set's Figma description and
  writes `axis-intent.json` beside the variant prefabs. No runtime code
  reads this file yet. Most component set descriptions are currently empty — the
  data will arrive when `figma-tokens` fills them in.

## Verifying

Offscreen capture of a UGUI canvas does not work in this project (URP;
`Camera.Render()` is a no-op). Check by:

- The import log: node count, component prefabs written, screens saved
- `[NineSlicePass]` log line: prefabs touched, grids collapsed, sprites written
  (only when `CollapseSliceGrids = true`)
- Reading back sprite borders and asserting `left + right < width` and
  `top + bottom < height`
- Opening a prefab in the editor — looking is a human step

See the `psd-to-figma` skill for the Figma file's own conventions and the
**Never restart Unity** section above.
