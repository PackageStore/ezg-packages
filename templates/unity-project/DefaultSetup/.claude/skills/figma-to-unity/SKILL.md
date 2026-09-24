---
name: figma-to-unity
description: Import Figma screens into Unity as UGUI prefabs with the EZG Figma Bridge (com.ezg.figma-bridge), and prove the result matches Figma with the bridge's visual check. Use when asked to "import a screen from Figma", "bring the Figma UI into Unity", "re-import a screen after the designer changed Figma", "the imported screen looks wrong", "check the import against Figma", or when given a figma.com/design link to a screen frame.
argument-hint: [Figma link or frame name]
---

# Figma to Unity screens

**The rule:** a Figma COMPONENT is a Unity prefab; a Figma INSTANCE is a prefab
instance (`PrefabUtility.InstantiatePrefab`); a COMPONENT_SET is a folder of
prefabs, one per variant. Screens contain prefab instances, not baked copies of
component internals.

| Piece | Where |
|---|---|
| Bridge | UPM package `com.ezg.figma-bridge` from the EZG scoped registry. Source: `packages/com.ezg.figma-bridge` in the `ezg-packages` monorepo; a push to its `main` publishes a new version |
| Window | **Tools > EZG Technical Art > Figma Bridge** (Setup and Token tabs, Sync buttons, Visual Check) |
| Settings asset | the project's `UnityFigmaBridgeSettings` asset (`t:UnityFigmaBridgeSettings`) |
| Screen / component / page prefabs | `ScreenPrefabFolder` / `ComponentPrefabFolder` / `PagePrefabFolder` on the settings asset |
| Image fills | `<ImageFillFolder>/<Figma document name>` |
| Server renders | `<AssetsRootFolder>/ServerRenderedImages/<node id>.png` (`:` becomes `_`) |
| Cached document | `Assets/FigmaOutput.json`, written by every online Sync |
| Visual check output | `Library/FigmaVisualCheck/<prefab name>/` |

Bridge changes belong in the package source (with a version bump), never in
`Assets/`. Generated prefabs and sprites are disposable output: delete and
re-import them freely.

## Main checkout only

While an unpublished bridge is tested, `Packages/manifest.json` points at the
package source with a `file:` path. That path resolves relative to `Packages/`,
which is gitignored and exists only in the main checkout (the project repo
root). Run imports and bridge tests from the main checkout.

## Never restart Unity

Killing the editor takes down the MCP server at `http://127.0.0.1:8080` and you
lose all control until the user reopens it by hand. Everything below is reachable
over MCP:

- Picked up a changed `Packages/manifest.json`: `UnityEditor.PackageManager.Client.Resolve()`
  through `execute_code`
- Recompile: `refresh_unity` with `compile: request`, or
  `CompilationPipeline.RequestScriptCompilation()`
- Stale build artifacts: `RequestScriptCompilationOptions.CleanBuildCache`

**The MCP connector can fail while the server is up.** If the session reports
UnityMCP as failed (ECONNREFUSED at session start), the server usually still
answers JSON-RPC at `http://127.0.0.1:8080/mcp`: POST `initialize`, keep the
`mcp-session-id` response header, send `notifications/initialized`, then
`tools/call`. Replies arrive as SSE `data:` lines.

`execute_code` compiles with CodeDom (C# 6): no local functions, no tuples,
no pattern matching. Editor state JSON has no space after colons
(`"ready_for_tools":true`).

## The token

`FigmaAccessToken.Read()` (`Editor/Utils/FigmaAccessToken.cs`) reads the
personal access token from Unity `PlayerPrefs` on this machine. Set it in the
window's **Token** tab. It is never written into the settings asset (a
ScriptableObject under version control) and never logged.

## Run it

Window buttons, or the same calls through `execute_code`:

| Button | Call | Figma API |
|---|---|---|
| Sync Document | `UnityFigmaBridgeImporter.SyncDocument()` | document, renders, image fills |
| Re-import from cache (offline) | `UnityFigmaBridgeImporter.SyncDocumentOffline()` | none: uses `Assets/FigmaOutput.json` and the files on disk |
| Run Post-Processors (no Sync) | `UnityFigmaBridgeImporter.RunPostProcessorsOnly()` | none |
| Visual Check (selected screen prefab) | `Verify.FigmaVisualCheck.Run(prefabPath)` | one frame render, then cached |

Both syncs are `async void`: the call returns at once. Wait for the screen
prefab file to be rewritten and stay unchanged, and for the editor to report
idle, before reading the output. Read results with `read_console` (the
`[NineSlicePass]`, `[ServerRenderSlicer]` and `[FigmaBridge]` lines).

Use the offline re-import whenever the change does not need new downloads (a
fix in prefab building, slicing or text). It costs no API quota. Changes to what
is rendered, or how, need an online Sync.

**First run, or after pages change in Figma:** the import aborts with
"The pages found in the Figma document have changed". It writes the new page
list into the settings asset and selects it in the Inspector. Tick the pages you
want, then Sync again.

**Do not edit any script while a download is running.** The download is async;
saving a `.cs` triggers a domain reload that silently kills it.

### BuildPrototypeFlow = false

The import still creates a temporary Canvas in the active scene
(`UnityFigmaBridgeImporter.CreateCanvas`) and destroys it in
`CleanUpPostGeneration`. The Canvas is a build surface, not a scene object.

### Page prefabs

One prefab per selected page goes into `PagePrefabFolder`. Nothing reads them.
Deleting a screen prefab before a re-import makes the page prefab log "Problem
detected while importing the Prefab file" once, because the screen it nests is
missing for a moment. A force reimport of the page prefab afterwards is clean.

## Adding a screen

Add a row to the `ScreenNameOverrides` list on the settings asset: `FrameName`
is the Figma frame name (e.g. `<FrameName>`), `PrefabName` is the output prefab
filename (e.g. `Screen_<FrameName>`), blank keeps the frame name.

When `OnlyImportListedScreens` is true, only frames with a matching row are
imported. A top-level FRAME with no matching row is skipped:
`FigmaPaths.GetPathForScreenPrefab` returns `null`.

No code change needed. It is a data change on the settings asset.

## Finding a component prefab

See [reference/prefab-contract.md](reference/prefab-contract.md) for the full
node-type-to-asset table.

Summary: standalone components land at
`<ComponentPrefabFolder>/<SafeName>.prefab`. Variants of a COMPONENT_SET land at
`<ComponentPrefabFolder>/<SetName>/<NormalisedVariant>.prefab`, where `=` becomes
`-` and `, ` becomes `_`. Each set folder also gets an `axis-intent.json`.

## Server renders

A node that a plain `Image` cannot draw is rendered by Figma and imported as a
sprite (`FigmaDataUtils.GetNodeSubstitutionStatus`): vectors, boolean
operations, vector-only groups, and childless shapes with a stroke, corner
radius, gradient, ellipse or star shape (`NeedsShapeRender`). A frame with
children is not rendered (its children would draw twice); it keeps a flat
`Image` and is listed in `ShapeOnlyNodes` for post-processors.

- **Render bounds.** Renders include what draws outside the layout box
  (outside strokes, shadows) and the RectTransform covers
  `absoluteRenderBounds`. Only pattern tiles are rendered at the layout box.
- **Master size.** A sublayer of a component is rendered once, at the
  component master's size. An instance resized in Figma draws that render at
  its own size, which is why every render is sliced (below).
- **Restyled sublayers.** When an instance overrides the fills, strokes,
  effects, radius, opacity or visibility of a rendered sublayer, that sublayer
  gets its own render under its instance-side id (`I<instance>;<node>`), and
  the instance's sprite replaces the component's.
- **Scale.** Renders are made at `ServerRenderImageScale` (or 1 when
  `AutoServerRenderScale` detects a file drawn at device pixels) and import at
  100 × that scale pixels per unit, so a border draws at design size.

## 9-slice

Two passes, both on by default.

**Render slicing** (`SliceServerRenders`, `ServerRenderSlicer`). Every
render gets a sprite border, so corners, strokes and shadows keep their size at
any instance size. On each axis, a band of identical lines (premultiplied
colour, tolerance 4 levels: Figma dithers gradients and shadows by up to 3)
becomes the border centre and is cut down to 2 px, averaged. An axis with a
gradient along it has no such band: its border comes from the node geometry
(corner radius, inside stroke, shadow reach, what draws outside the box) and
its pixels are kept. The render's Image is `Sliced` when the border is not zero.

**Slice-grid collapse** (`CollapseSliceGrids`, `NineSlicePass`). A parent
whose children are all `slice_<row>_<col>` cells sharing one image becomes one
`Image.Type.Sliced` with that image. The border is measured from the cells in
design units and scaled by the texture's texels per design unit; the sprite's
pixels per unit follow the same density. Keep it on: since 0.4.0 an image fill
in CROP mode draws the whole image, so an uncollapsed cell shows the whole
plate, not its slice.

## Instances and variants

Instances become prefab instances of the component prefab, and each nested
node gets the instance's overrides (text, fills, visibility, transform) through
`ComponentManager.ApplyFigmaProperties`. A nested instance whose component
differs from the one the parent component was built with (a variant change or
an instance swap) is replaced by the prefab of the component the Figma node
names (`SwapChangedComponent`), keeping its place, transform and node id.

## What the importer does well

- **Constraints become anchors.** `NodeTransformManager.ApplyFigmaConstraints`
  sets `anchorMin`/`anchorMax` from the Figma `constraints`
  (`AnchorPositionsForFigmaConstraints` is the table), with offsets for
  CENTER/RIGHT/BOTTOM and size deltas for LEFT_RIGHT/TOP_BOTTOM/SCALE. A child
  that sits wrong when its parent resizes is usually a constraint in Figma:
  read the node's `constraints` before changing the bridge.
- **Image fills come back as the original uploaded PNG**, one per `imageRef`,
  named by node path when `NameImageFillsByNodePath` is on.
- **Text becomes TextMeshPro.** Font mapping uses `FontManager`; Google Fonts
  download is `EnableGoogleFontsDownloads` on the settings asset.

## What it does not do

- **No state code.** Variants are separate prefabs. There is no `Selectable`,
  no `SpriteSwap`, no runtime variant-switching logic.
- **Axis intent is recorded but unread.** `ComponentAxisIntent.WriteAxisIntent`
  writes `axis-intent.json` from the `UNITY:` directive in each component set's
  description. No runtime code reads it yet.
- **FILL and CROP image fills draw as `Simple`**: the crop is lost.
- **Text strokes are approximate.** A Figma text stroke becomes a TMP outline
  of `strokeWeight × 0.1` with the same face dilate, whatever the font size; a
  3 px stroke on 40 px text draws about 1 px thinner than Figma.
- **Auto layout is experimental** (`EnableAutoLayout`, off by default): frames
  import at their Figma positions.

## Verifying: the round trip

`Verify.FigmaVisualCheck.Run(screenPrefabPath, passScore = 0.90,
textPassScore = 0.85, refreshReference = false)` captures the prefab at its
frame size in a preview scene (the open scene is not touched) and compares it
with Figma's 1x render of the frame. Each container (a visible node with
children) is scored by SSIM over its render bounds, so a small wrong part
cannot hide behind a correct background. A container that draws nothing but
text passes at `textPassScore`, since TMP rasterises and outlines glyphs its own
way. `Report.ToString()` lists every container, lowest margin first;
`report.json` holds the same with each box (top-left origin, frame pixels).
The Figma render is fetched once and cached as `figma.png`; pass
`refreshReference: true` after the design changes.

A fix is done only when the check passes. The loop:

1. **Baseline.** Run the check on the current import and keep the scores.
2. **Look.** For each LOW container, crop `figma.png`, `unity.png` and
   `diff.png` at its box and name the defect class: crop or stretch, lost
   corner radius, wrong colour or variant, position or anchor, text.
3. **Read the data first.** Check the node in `Assets/FigmaOutput.json` (or
   the live file) before touching the bridge. A wrong anchor that matches the
   node's `constraints` is a design fix, not a bridge fix.
4. **Fix the bridge**, bump its version, recompile over MCP, re-import
   (offline when no download changed), check again.
5. **At least 3 rounds.** The last one starts clean: delete the screen prefab
   and its sprite folder, run an online Sync, run the check. The result must
   match the previous round.

Other checks that need no capture:

- `[ServerRenderSlicer]` and `[NineSlicePass]` log lines: renders bordered and
  compacted, grids collapsed
- Sprite borders: `left + right < width` and `top + bottom < height`
- A render's pixels per unit is 100 × the render scale

See the `psd-to-figma` skill for the Figma file's own conventions and the
**Never restart Unity** section above.
