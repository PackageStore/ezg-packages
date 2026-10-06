# Changelog

## [0.8.1] - 2026-10-06
### Fixed
- **Lists from another Figma file are no longer reused.** The page selection, the screen rows and
  the component ticks are keyed by node id, and ids repeat across files (every file has a page
  `0:1`). After a switch of file (Bridge to another open file, or REST to Bridge), Sync could import
  the page that was ticked in the old file and find no screens. The settings now record the file
  the lists came from (`ListsFileKey`, hidden). When Sync or Refresh gets another file, the lists
  are rebuilt from it: every page and screen selected, no component ticked, and a warning is logged.
- **Refresh updates page names.** A renamed page kept its old name in the page list.

## [0.8.0] - 2026-10-06
### Added
- **Source panel with REST API / Bridge (EZG Tools) tabs.** Only the panel content switches; the
  settings and the lists below it stay in place. Rest shows the document URL, the token field
  with Save and status, and the FileID check. Bridge shows the port, the live hub status and a
  picker of connected files. Bridge needs no DocumentUrl and no token: the file is taken from the
  Figma file open in EZG Tools, and a picker appears when several files are open. Unsaved files
  (no key) cannot be imported.
- **The bound Bridge file is remembered.** It is stored in the new hidden `BridgeFileKey` and
  `BridgeFileName` fields, written after a successful import. A Sync that would import a different
  file than the bound one asks for confirmation; in batch mode it aborts with an error.
### Changed
- **The token tab is removed.** The token field lives in the Rest block. The token store is
  unchanged (`PlayerPrefs`, per machine).
- **Settings sit in closed foldouts** (Output Folders, Pages & Naming, Text, Sprites, Layout &
  Nine-Slice, Advanced) with conditional fields. Foldout state is per user, not in the asset. The
  window, the settings asset inspector and Project Settings use the same drawer.
- **Labels and error messages are source-specific.** Bridge texts never mention URL or token. The
  `[Header]` groups on the settings class are gone.
### Upgrade
- Existing settings assets need no migration. The two hidden fields appear on the next save. No
  existing field is renamed, retyped or reordered.

## [0.7.0] - 2026-10-06
### Added
- **Bridge source: import from the open Figma file through the EZG Tools plugin.** The settings asset
  gains `Source` (Rest | Bridge, default Rest) and `BridgePort` (default 39410). With Bridge, the
  importer reads the document, the server renders and the image fills from the Figma file open in
  Figma desktop, over the plugin's MCP tab connection. There is no REST quota and no token, and no
  Claude or MCP client is needed in the import path. A font missing in Figma is logged as a warning
  and the import goes on.
### Changed
- **The first Sync after a source switch renders every server render again.** The render-cache
  hashes differ between sources, so nothing is reused until the second Sync. `Source = Rest`
  behaves as in 0.6.11.
- **The visual check takes its reference from the selected source.** With Bridge it compares against
  the plugin's render, not a REST render.

### Requires
- EZG Tools plugin with `doc.snapshot` and `images.get`, released from `ezg-figma-tools` `main`.
  The MCP tab must be connected. Bridge is only usable with the plugin up to date.

## [0.6.11] - 2026-10-01
### Fixed
- **Sync froze at "Downloading file" 0% on a file with stroked text.** An outside stroke on a TEXT
  node makes Figma return megabytes of glyph outline in `strokeGeometry` per node (one 6,500-node
  file came back as 593 MB, 515 MB of it text outlines). The bridge read the whole response into
  one string and decoded it in one call, which froze the editor. The document now downloads to
  `Temp/FigmaBridge/`, the progress bar shows the megabytes received, the `fillGeometry` and
  `strokeGeometry` of TEXT nodes are dropped while the file is copied (TMP draws text from the
  characters and style), and the result is decoded as a stream. The same file now caches at 8.9 MB
  in `Assets/FigmaOutput.json`. The offline re-import also decodes the cache as a stream. Server
  render hashes change once, because text nodes in the cached JSON lose their outlines.
- **A large document download that lost its HTTP/2 stream part-way failed the Sync** ("Curl error 92",
  reported as "HTTP 200 Unknown Error"). A connection error on the document download now retries up
  to 3 attempts.

## [0.6.10] - 2026-10-01
### Fixed
- **A Figma code shader fill broke the whole document download.** Figma REST now returns such a
  fill as `"type":"CUSTOM"` (with `customEffectId` and `componentPropAssignments`), and Newtonsoft
  failed on it with "Error converting value CUSTOM to type Paint+PaintType". `Paint.PaintType` gains
  `CUSTOM` and `UNKNOWN`, read through a tolerant converter (any unrecognised type becomes `UNKNOWN`,
  with one warning per value), so unknown or `CUSTOM` paints no longer break document decode. The
  shader data is kept opaque. A childless node with such a paint is rendered server-side as an
  image (an instance that stretches it gets its own render); local drawing (flat Image, frame
  shape sprite, clip mask, text colour) skips the paint instead of drawing it as white. A frame
  with children keeps its other fills and logs a warning: put the shader on a childless rectangle
  behind the children.

## [0.6.9] - 2026-09-29
### Fixed
- **Re-import moved the object ids of same-named nodes, so another prefab's override landed on
  the wrong node.** A prefab is replaced by saving a new tree over it, and Unity carries object ids
  across by GameObject name over the whole tree: the first new `Rectangle` took the id of one old
  `Rectangle` (not the one at its place) and the other eight got new ids. The nine cells of a slice
  plate, or the text under each repeated row, came back with ids moved between nodes, and an
  override held on one of them - by a variant or screen the import did not rebuild, or set by hand -
  moved another node: the bottom-right cell's position put the top-left cell of a popup frame
  outside the frame. Component, screen and page prefabs are now saved with every repeated name
  suffixed on both sides by its path of `name#occurrence`, then renamed back, so each node keeps the
  id of the node at its place in the previous import. A prefab written by an earlier version keeps
  its current ids from the first import on.
- **Every child of a placed instance carried an override.** Re-applying the Figma properties to an
  instance lays its children out again from absolute coordinates, a few thousandths of a pixel off,
  and Unity recorded each value as an override (nine cell positions and sizes for one slice plate).
  Overrides on an instance's children that only repeat the component's own value (within 0.01 px
  for positions and sizes, 0.0001 for other floats) are dropped before the prefab is saved; the
  instance root's place and size and real differences stay. Values are compared after the
  RectTransforms update, since pivot centring moves a node through its world position and its
  serialized `anchoredPosition` lags until then. The count is logged once per import.

## [0.6.8] - 2026-09-28
### Fixed
- **New art in Figma renamed the fills already on disk and swapped the art behind their GUIDs.**
  Names are claimed in `imageRef` order, so one new fill whose hash sorts early took the first free
  name of its family (every 9-slice cell called `Rectangle`) and pushed each later fill of that
  family down one name. 0.6.5 kept the old files under the shifted names, so the cells of a plate
  showed each other's art; 0.6.7 downloaded them again, which fixed the prefabs the bridge writes
  but left every sprite GUID holding another cell's art. A fill whose own art is already in its
  folder under one of its candidate names (or the counter form of the last one) now keeps that
  file, and only fills with no file on disk claim new names. The match is by SHA-1, which is
  Figma's `imageRef`, so no record of earlier imports is needed.

### Added
- The offline re-import warns about image fills whose file holds other art than the document
  (SHA-1 differs from the `imageRef`), next to the files it could not find.

## [0.6.7] - 2026-09-28
### Fixed
- **Re-import kept old art when a node's image changed but its name did not.** With
  `NameImageFillsByNodePath` a fill's file is named after its node, and a fill was downloaded only
  when no file existed at that path. New art on the same node, a name moved to another fill, or a
  vector that became a bitmap under the same name kept the old PNG. A named fill is now downloaded
  again when the SHA-1 of its file differs from its `imageRef`.
- **A re-downloaded fill kept the sprite border of the art it replaced.** The border is cleared when
  a fill is written; the 9-slice pass sets it again for a slice grid.

### Changed
- A fill's Image is no longer `Sliced` because its sprite already has a border. Only the 9-slice
  pass slices a fill, so a border left by an earlier import (a slice grid replaced by one image of
  the same name) or by another slice grid that shares the fill no longer changes the result. A
  border set by hand on a fill sprite is ignored.

## [0.6.6] - 2026-09-28
### Added
- **Frame shape sprites.** A frame, component or instance with children whose own look a flat
  Image cannot draw (corner radius, gradient, stroke, inner shadow, or more than one visible fill)
  gets a locally drawn 9-sliced background sprite: its visible solid and gradient fills (linear,
  radial, diamond, angular) composited bottom to top, its inside stroke, and its inner shadows,
  clipped to its corner radii. Sprites are named by content hash under
  `<ImageFillFolder>/<document>/Shapes/`, so nodes that look the same share one file. A clipping
  frame keeps that sprite as its Mask graphic.
- **Per-instance renders for stretched shapes.** An instance that resizes a server-rendered vector
  shape, or a render with an image or pattern fill, gets a render at its own size: the component's
  render could not be sliced to that size without distortion.
- **Text line metrics.** Figma's `lineHeightPx` becomes TMP line spacing, and the half leading
  above the first and below the last line becomes the vertical margins, so the first baseline
  sits where Figma puts it. `leadingTrim: CAP_HEIGHT` boxes (first cap line to last baseline) use
  negative margins and keep their Figma height. Figma letter spacing is added to
  `CharacterSpacing`.
- **Gradient text fills** become a TMP vertex gradient, sampled at the cap line and baseline.

### Changed
- `CharacterSpacing` defaults to 0 and is now an offset on top of Figma's letter spacing. The old
  -0.7 only matched Figma while the design font was substituted. Existing settings assets keep
  their value; set it to 0.
- Auto-width text (`WIDTH_AND_HEIGHT`) no longer wraps.
- Render cache format version 2: every render is fetched and sliced again once.

### Fixed
- **Sliced renders bled their border into the centre.** The compacted centre kept only 2 texels,
  so bilinear filtering faded the stretched centre into the border colour over a quarter of its
  width. One band texel now stays on each side of the centre, inside the border.
- A Google Fonts family that Figma names with run-together words ("Saira ExtraCondensed") is
  looked up spaced as well ("Saira Extra Condensed"), instead of falling back to LiberationSans.
- Frames and texts took `fills[0]` even when it was hidden: a hidden first fill disabled the
  whole Image. The top visible fill is used now, and hidden text strokes and drop shadows are ignored.

## [0.6.5] - 2026-09-24
### Added
- **Import timing report.** Every Sync (online or offline) logs one console report when it ends:
  wall-clock seconds and share of the total for each phase (document download and decode,
  server render cache check and requests, image download, image import into Unity, node build,
  component and screen prefab saves, instance placement, 9-slice, behaviour binding, each
  post-processor, final refresh), with counts such as nodes, requests, files and MB. A stopped
  import reports the time until it stopped. The last report is also in `FigmaImportTimer.LastReport`.
- **Server render cache.** An online import only asks Figma to render, and only downloads, the
  render nodes whose subtree changed since the last import. Each node is hashed from the raw
  document JSON, leaving out its position on the page but including resolved instance children,
  so a master component edit re-renders every render that shows it. The hash also covers render
  type, scale, slicing, wrap and sprite import settings, plus a cache format version. The manifest
  lives in `Library/FigmaBridge/server-render-cache.json` and records each PNG's length and write
  time after slicing, so a deleted or edited PNG is fetched again. Delete the manifest to force
  every render again. The slicer only processes renders downloaded in that import.

### Changed
- **Server renders are always made at scale 1.** Removed the `ServerRenderImageScale`,
  `AutoServerRenderScale` and `NativeScreenLongSide` settings and their window fields, along with
  `FigmaDataUtils.GetEffectiveServerRenderScale`. Every server render is one texel per design unit
  and imports at 100 pixels per unit; the render failure hint no longer gives scale advice and
  lists only the frames that were requested. Old settings assets still load: Unity ignores the stale keys.
- **Faster image download and import.** Image fills and server renders download up to 6 at a time,
  and the files import in two AssetDatabase batches instead of one Refresh and reimport per file;
  a file whose settings are already right imports once. An HTTP error response is no longer
  written over the image, and each download item reports `Succeeded`.
- **Faster behaviour binding.** One MonoBehaviour type lookup per import (from `TypeCache`) instead
  of a scan of every loaded type for every node. Types from Unity's own assemblies are no longer
  bound, so a node named `Image` or `Button` does not gain a UGUI component. With
  `ScreenBindingNamespace` set, the first type whose name and namespace both match is picked; types
  sharing a name are chosen in a fixed order (global namespace, then full name, then assembly).
  Prefabs that binding did not change are not re-saved.

### Fixed
- `UnityWebRequestAwaiter` no longer throws when a request completes before its continuation is set.

## [0.6.4] - 2026-09-24
### Changed
- **One tab per page in the screen list.** The window shows the rows of one page at a time
  (`Screens (29)`, `Components (60)`, ...) instead of every page one under another, so no page
  has to be scrolled past to reach another. The `All` and `None` buttons act on the open tab.
- **Rows are sorted by name** (case-insensitive) in the window. The settings asset keeps them in
  document order.

## [0.6.3] - 2026-09-24
### Added
- **Fixed pages: `Screens` and `Components`** (settings `ScreensPageName`, `ComponentsPageName`).
  Every file imported with the bridge must have both; an import or a list refresh warns when one
  is missing. Screens come only from the screens page. A frame on any other page is a container
  for components, never a screen, so it gets no screen row, no screen prefab and owns no art.
  Component rows come from every page but the screens page, at any depth (a component set
  inside a frame on `Components` gets a row), never from inside another component or instance.
- **Component rows in the screen list.** Each page now lists its component sets, and its
  components outside a set, placed on the page or in a section (`ComponentSelections`, filled by
  **Refresh from Figma**). A row shows whether it is a set, how many screens use it, and when an
  unticked row still imports because a ticked screen or component uses it.
- **Import the selection only** (`ImportSelectionOnly`, on by default). An import builds the
  ticked screens and components, plus every component they reach through instances; a variant
  pulls in its whole set. Only their renders and image fills are downloaded. Other prefabs and
  sprites keep what the last import wrote, and page prefabs are not written. Duplicate-name
  suffixes on component prefabs are counted over the whole document, so a partial build writes
  every component to the path a full build would. A component on a page that is not imported is
  still built when the selection uses it, and its art is named on that page.

### Changed
- With `ImportSelectionOnly` on, a component that no ticked item uses is no longer imported. A
  new component row starts unticked. Turn the setting off to import every component as before.
- **Art names no longer follow the ticks.** Image fills and renders are named over every page,
  and every listed screen owns its art whether it is ticked or not; the ticks and the page
  selection decide only what is downloaded. Before, unticking a screen moved art it shared with
  a ticked one to a new name, and an offline import then found no file. Art a component on an
  unselected page uses is now downloaded too, so those components get their sprites. Names move
  once with this update: the first import after it must be online.

## [0.6.1] - 2026-09-24
### Changed
- **Server renders are filed and named like image fills** (setting `NameServerRendersByNodePath`,
  on by default). A render goes to `<image fills>/Components/<component>/`,
  `<image fills>/Screens/<screen>/` or `<image fills>/Shared/`, named by its node path, instead of
  `ServerRenderedImages/<node id>.png`. Fills are named first and both claim names from one set,
  so a render never takes a fill's file. A render outside the imported pages and a top-level
  export keep their old paths. The first import after the update must be online: the renders
  download to their new paths, and the old `<node id>.png` files are no longer used.

## [0.6.0] - 2026-09-24
### Added
- **Clip content becomes a mask on the same GameObject** (`ClipContentMask`, setting
  `ClipContentAsMask`, on by default). A frame, component or instance with Figma "Clip content"
  masks its children like it does in Figma: radius 0 gets `RectMask2D`; a corner radius gets a
  `Mask` whose graphic is a generated white 9-sliced rounded rectangle
  (`<image fills>/Masks/Mask-R<radius>.png`, 2 texels per unit, border = radius). The graphic shows
  only when the frame has a visible fill; otherwise it is hidden and not a raycast target. A frame
  with an image or pattern fill keeps that sprite as the mask graphic and logs that its radius is
  not applied. Screen frames and scroll frames are skipped. Instance re-application gives the same
  result, so instances carry no added overrides.

### Fixed
- **Only a "Use as mask" layer masks its siblings.** The sibling pass took any GameObject with a
  `Mask` as a Figma mask layer, so the siblings after a clipping frame moved under its mask.

## [0.5.2] - 2026-09-24
### Added
- **Android and iOS texture override.** Every texture the bridge writes (image fills, server
  renders, baked 9-slice sprites) gets the Android and iOS override tabs turned on with
  `MobileTextureFormat` (default `ASTC_4x4`) and `MobileCompressionQuality` (default `Normal`).
  New downloads get it before their first import; a pass after the 9-slice pass applies it to
  sprites already on disk and reimports only the ones that change. Max size is copied from the
  Default tab only when a tab is first turned on. `OverrideMobileFormat` (on by default) turns it off.

## [0.5.1] - 2026-09-24
### Added
- **Import state for automation.** `UnityFigmaBridgeImporter.ImportInProgress`, `LastImportError`,
  `LastImportStartedUtc` and `LastImportCompletedUtc` (ISO 8601 UTC; completed is set only when a
  Sync finishes without an error). A script that starts `SyncDocument` / `SyncDocumentOffline` over
  MCP polls them instead of guessing from file times. Start it from a one-shot
  `EditorApplication.update` handler: an import holds the main thread, so a direct call does not
  answer, and `delayCall` does not run while the editor is unfocused. Set `SuppressDialogs` first so
  an error cannot wait on a modal dialog.
- **Visual check crops.** Every container below its pass score gets `low/<nn>-<path>.png`, Figma,
  Unity and difference side by side, worst margin first. The report carries `checkedAtUtc`.

### Fixed
- **Render slicing is idempotent.** A render the slicer wrote carries `figma-bridge-sliced:<md5>`
  in its importer `userData` and is skipped while the file is unchanged. Re-slicing a compacted
  render picked a faint shadow tail as its band and cut rows off on an offline re-import; lines with
  nothing drawn on them are no longer accepted as a band either.

## [0.5.0] - 2026-09-24
### Added
- **Server renders get a sprite border** (`ServerRenderSlicer`, setting `SliceServerRenders`, on by
  default). A render made at the component's size keeps its corner radius, strokes and shadows where
  an instance draws it at another size. A band of identical columns (rows) in the render becomes the
  border centre and is cut down to 2 px, averaged so Figma's dither does not streak; comparison is
  premultiplied with a 4 level tolerance. An axis with a gradient along it takes its border from the
  node geometry (radius, inside stroke, shadow reach, what draws outside the box) and keeps its pixels.
  Renders import at 100 x the scale they were made at (`FigmaImportProcessData.ServerRenderScale`)
  pixels per unit, so borders draw at design size.
- **Instances that restyle a server-rendered sublayer get their own render.** Instance `overrides`
  are read from the API (`Node.overrides`); a sublayer whose fills, strokes, effects, radius,
  opacity or visibility an instance changes is rendered under its instance-side id and its sprite
  replaces the component's in that instance.
- **Nested variant and instance swaps.** A nested instance whose component differs from the one its
  parent component was built with is replaced by the prefab of the component the node names.
- **Visual check** (`Verify.FigmaVisualCheck`, button in the Figma Bridge window). Captures a screen
  prefab at frame size in a preview scene and scores each container (a visible node with children)
  by SSIM against Figma's 1x render of the frame. Pass: 0.90, or 0.85 for a container that draws
  only text. Captures, a difference map and `report.json` go to `Library/FigmaVisualCheck/<prefab>`.

### Fixed
- Server renders were requested with `use_absolute_bounds=true`, which crops to the layout box, and
  then stretched over the render-bounds rect: outside strokes survived only at the corners, shadows
  were cut. Renders now cover the render bounds; pattern tiles alone keep the layout box.
- Instance sublayer ids (`I1:2;3:4`) are URL-escaped in render requests.
- `slice_ROW_COL` collapse wrote a border measured in design units as texture pixels and left the
  sprite at 100 pixels per unit, so a plate at any density other than 1 texel per unit lost its
  corners. The border and pixels per unit now follow the texture's density.

## [0.4.1] - 2026-09-24
### Added
- **Server-render settings** on `UnityFigmaBridgeSettings`: `AutoServerRenderScale`,
  `NativeScreenLongSide`, `ServerRenderTopLevelExports` and `ServerRenderBatchSize` (shown in the
  Figma Bridge window with the other settings).
- **Automatic render scale (opt-in, `AutoServerRenderScale`, default off).** When on and the
  document has a top-level frame whose long side is at least `NativeScreenLongSide` (default 2400,
  i.e. a 1080×2400 canvas designed at device pixels), server renders use scale 1 instead of
  `ServerRenderImageScale` — rendering such a file at ×3 only grows every texture ×9 without adding
  detail. One log line names the frame that triggered it. PATTERN tile sizing now reads the scale
  actually used (`FigmaImportProcessData.ServerRenderScale`), so tiles stay at design size either way.
- **`ServerRenderTopLevelExports` (default on = previous behaviour).** Turn it off to stop rendering
  whole top-level frames that carry an Export setting — full screens at scale 3 can make the render
  request time out (HTTP 504). The screen still imports as a prefab (`GenerateNodesMarkedForExport`),
  and the vector/shape nodes inside it are then scanned for server rendering like any other frame.
- **Troubleshooting hints in the error dialog.** A server render that still fails with 5xx/timeout
  after the retries lists the settings to check (`Server Render Top Level Exports` with the names of
  the Export frames being rendered, `Server Render Image Scale` / `Auto Server Render Scale`,
  `Server Render Batch Size`, or the node id that keeps failing). A document that fails to parse on
  an unknown enum value names the value, its type and JSON path, and points to updating the bridge
  or `Re-import from cache (offline)`.
- `FigmaApiRequestException` carries the HTTP status of a failed server-render request.

### Changed
- Server renders are requested in batches of `ServerRenderBatchSize` (default 300, as before). A
  batch that fails with 5xx or a connection error is split in half and retried; a single node that
  keeps failing is retried twice, then reported by id. 4xx errors (429, 403…) still stop at once.
- Upgrading from 0.4.0 changes nothing by default: every new setting defaults to the old behaviour.

### Fixed
- **Documents using Figma's new effect types no longer fail to parse.** `Effect.EffectType` gains
  `TEXTURE`, `NOISE` and `GLASS`, and any unknown effect type now reads as `UNKNOWN` (warned once
  per type) instead of throwing `JsonSerializationException` for the whole document. These effects
  are skipped at import, like blurs.

## [0.4.0] - 2026-09-17
### Added
- **`[ignore]` marker.** A node whose name contains `[ignore]` (case-insensitive) is pruned from
  the parsed document with its subtree, right after the JSON is read (online and from cache).
  Nothing downstream sees it: no image fill or font character is collected, no server render is
  requested, no GameObject is built, and it is absent from the page and screen lists. Any node
  type qualifies, screens and component masters included. An ignored master stays in
  `file.components`, so its instances import as regular frames with their own children. One
  log line lists what was skipped.
- **Shape nodes are server-rendered.** A childless `RECTANGLE`, `ELLIPSE`, `STAR`, `FRAME` or
  `COMPONENT` with a visible stroke, corner radius, gradient fill, or an ellipse/star shape is
  queued as a `Substitution` render and imported as a plain `Image` with that sprite
  (`FigmaDataUtils.NeedsShapeRender`). A frame with children is not rendered (its children would
  draw twice); it keeps a flat coloured `Image` and is listed in `ShapeOnlyNodes` as before.
- `Node.absoluteRenderBounds` is read from the API. A server-rendered node is sized and placed
  from it when present, so outside strokes and shadows land where Figma drew them instead of
  being squashed into the layout box.

### Removed
- **`FigmaImage`**, its shader (`Runtime/Resources/Shaders/FigmaImageShader.shader`) and its
  custom inspector. Every fill is a stock `UnityEngine.UI.Image`; the former `PlainImages`
  path is the only path. Image fills: FIT sets `preserveAspect`, TILE sets `Image.Type.Tiled`,
  a sprite with a border sets `Sliced`, FILL and CROP become `Simple` (the crop is lost).
  Prefabs generated by 0.3.x hold a missing script where `FigmaImage` was until re-imported.
- The `PlainImages` setting.

## [0.3.2] - 2026-09-08
### Added
- **Pattern fills.** A node filled with Figma's *pattern* paint (`fills[].type == "PATTERN"`,
  another node repeated across the shape) imported as a flat white `Image` because a pattern
  carries no `imageRef`. The bridge now server-renders the fill's `sourceNodeId` once
  (`ServerRenderedImages/<id>.png`, wrap mode Repeat) and draws the fill as `Image.Type.Tiled`
  (or, before 0.4.0, `FigmaImage` Tile) with the tile shrunk by `ServerRenderImageScale / scalingFactor` so it
  lands at design size. Hexagonal `tileType`s are drawn as a rectangular grid with a warning. An
  offline re-import without the render on disk logs the missing file, like a missing image fill.

### Removed
- The three action menu items added in 0.3.0 (`Tools/EZG Technical Art/Figma Bridge - Sync Document`,
  `… - Re-import from cache (offline)`, `… - Run Post-Processors (no Sync)`). The bridge is back to
  one menu item, `Tools/EZG Technical Art/Figma Bridge`, which opens the window; the three actions
  are buttons there. Scripts and MCP call `UnityFigmaBridgeImporter.SyncDocument()`,
  `SyncDocumentOffline()` and `RunPostProcessorsOnly()` directly instead of the menu paths.

## [0.3.1] - 2026-09-07
### Fixed
- `[FontManager] '<font>' has no glyph for N character(s)` was logged as an error on every
  re-import for characters the font already had. `TMP_FontAsset.TryAddCharacters` returns `false`
  and echoes the whole request as "missing" when it has nothing left to add, and the bridge took
  that at face value. Characters are now checked against the font's character table before and
  after baking; only the ones still absent are reported.
- Re-importing with some frames excluded deleted the `*.instances.json` sidecar of **every**
  screen while keeping the excluded screens' prefabs, so `Run Post-Processors (no Sync)` lost the
  instance data for them. Only sidecars whose prefab no longer exists are removed now.

## [0.3.0] - 2026-09-07
### Changed (defaults)
- `BuildPrototypeFlow` now defaults to **off**. Building the prototype flow opens the runtime
  scene and adds a `PrototypeFlowController` to its canvas, which is too large a side effect for
  a default. Existing settings assets keep whatever value they have serialized.
- Imported sprites no longer enable mipmaps (`SpriteMipmaps` = off). UI sprites are drawn at
  or near native size; mipmaps only blurred them. Wrap mode is now `Clamp` for every fill except
  the ones some node draws with scale mode TILE, which keep `Repeat`.

### Added
- **Post-processor hook.** Implement `UnityFigmaBridge.Editor.PostProcess.IFigmaImportPostProcessor`
  in any Editor assembly and it runs at the end of every import (found via `TypeCache`, no
  registration). The `FigmaImportContext` carries the settings, the document, the font map, every
  screen prefab with the component instances placed in it (recorded before the bridge strips its
  markers, so the component origin of each instance is known, nested ones included), the
  component id → prefab path map and the shape-only node list below.
- **`Run Post-Processors (no Sync)`** (window button + menu) re-runs the processors on the
  prefabs already on disk. Each import writes a `<Screen>.instances.json` sidecar beside the
  screen prefab so this works offline with the same instance data.
- **`Re-import from cache (offline)`** (window button + menu) rebuilds every output from the
  document cached by the last online Sync (`Assets/FigmaOutput.json`) and the sprites already on
  disk, without one Figma API call. Fills or server renders never downloaded are listed in one
  warning, not fetched. Use it when the seat's API quota is spent.
- `PlainImages`: every fill becomes a stock `Image` instead of a `FigmaImage`. Sprite, colour,
  visibility, `Sliced` (when the sprite has a border), `Tiled` (TILE fills) and `preserveAspect`
  (FIT fills) carry over. Stroke, corner radius, gradient and ellipse/star shapes cannot be drawn
  by a plain Image; the node still gets a flat Image and is listed in
  `FigmaImportContext.ShapeOnlyNodes` with what was lost. Done at generation time, not as a pass
  over finished prefabs, so component-instance overrides (sprite, colour) survive.
- `AddLayoutElements`: `Always` (0.2 behaviour) or `OnlyUnderAutoLayout`, which gives a
  `LayoutElement` only to children of a Figma auto-layout frame. A component instance now keeps
  one `LayoutElement` instead of gaining a second on every instantiation.
- `FontOverride`: one `TMP_FontAsset` for every text in the document. No project font search,
  no Google Fonts download; the characters the document uses are still baked into it and every
  effect material preset is derived from it.
- `TextFitMode`: `ContentSizeFitter` (0.2 behaviour) or `FixedRectAutoSize`, where an
  auto-resized text keeps a fixed rect grown by `TextWidthPadding` / `TextHeightPadding`
  (aligned edge stays put), with TMP auto-size down to half the design size and an ellipsis
  beyond that. `CharacterSpacing` exposes the value that used to be a hard-coded -0.7.
- `SpriteCompression`: `Uncompressed` (default) or `Compressed` for imported sprites.
- `ButtonNamePattern`: the rule that turned any node whose name contains "button" into a
  `Button` is now a case-insensitive regex (default `button`). Blank switches name matching off.
- `NumberDuplicateSiblings`: siblings sharing one name become `Name`, `Name_1`, `Name_2` in
  sibling order, with one summary warning per import listing every rename.
- API errors name the HTTP status and, for 429, the `retry-after` and rate-limit tier headers,
  instead of every failure reading as "check your token and url".

## [0.2.1] - 2026-09-03
### Changed
- Folder fields lost the browse button: the field is Unity's own folder object field, picked with
  its own picker or by dropping a folder in.
- A folder field now always shows the path it writes to: the folder's own path when one is set,
  the default in grey when the field is blank, or the path plus a note when the folder is not in
  the project yet.
- Field tooltips are Vietnamese: one line of description, then one example line. The two inline
  hints the folder field draws are Vietnamese too.
- The window dropped the resolved-folder summary box under the output folders, because each
  field now shows its own path.
- The Pages and Screens lists no longer sit in their own scroll boxes that shrank with the
  window. The tab has one scroll bar, the lists draw every row, and Sync Document is pinned
  below the scroll area so a long list cannot push it out of reach.

## [0.2.0] - 2026-09-03
### Changed
- Output folder defaults moved to `Assets/_Project/UI`: screens in `Screens`, components in
  `Components`, pages in `Pages`, image fills in `Sprites/<Figma document name>`. Blank fields
  use these.
- `ImageFillFolder` is now the parent folder: image fills always go in a subfolder named after
  the Figma document, so two documents imported into one project never share sprites.
- The five output folder fields are folder pickers (drag a folder or browse) instead of text.
  They still serialize as project-relative paths, so a folder that does not exist yet is created
  on import and a wiped output folder does not break the settings asset.
- A blank folder field shows the default it falls back to, greyed out, instead of an empty
  object slot; the Figma Bridge window also lists the resolved output folders under the fields.
- `Assets` itself is accepted as a folder value.

## [0.1.0] - 2026-09-03
### Added
- Initial release of `com.ezg.figma-bridge`: Figma document import into Unity as native UGUI
  prefabs, with an editor window and a settings asset driving every path and toggle.
- Configurable output folders (`ScreenPrefabFolder`, `ComponentPrefabFolder`, `ImageFillFolder`)
  so no path is fixed to one project layout.
- Selective screen import via `OnlyImportListedScreens`.
- 9-slice collapse: a plate built from `slice_*` cells imports as one sprite carrying
  `Sprite.border` instead of nine child images.
- Node-named image fills, so sprite names are stable and readable across re-imports rather than
  keyed on the Figma `imageRef` hash.
- Component-set axis-intent output: `axis-intent.json` written beside the variant prefabs.
- Figma personal access token stored per machine in `PlayerPrefs`, never in the project.
- Live Google Fonts resolution, with imported families placed in `Assets/TextMesh Pro/Fonts`.
