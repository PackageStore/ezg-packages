# Changelog

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
