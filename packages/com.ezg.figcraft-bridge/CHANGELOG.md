# Changelog

## [0.1.5] - 2026-11-27
39 screens of a Linear project against 0.1.4: all 39 pass the visual check (was 38), none lower beyond noise;
FieldHud 0.757 → 0.911, WingsPanel-Wings 0.948 → 0.976, BossIntro 0.946 → 0.987, Vip 0.977 → 0.988. Import of the
39 screens 171 s → 111 s (linear alpha textures 52 s → 7 s, layer compensation 16 s → 10 s).
### Fixed
- **Very dark colours keep their channels in a Linear project.** A Canvas converts each vertex colour to linear
  and stores it in 8 bits, so #0E0A06 drew as (13, 13, 0) and #14100D as (22, 13, 13). Each screen prefab root
  now holds its own Canvas with `vertexColorAlwaysGammaSpace` (UI/Default and the TextMeshPro shaders convert
  the colour per pixel, exact to the 8-bit value; a nested Canvas keeps its own flag whatever its parent says),
  the TextMeshPro shader channels and a GraphicRaycaster. The import warns about any shader under a screen that
  ignores `_UIVertexColorAlwaysGammaSpace`. The bridge's own canvases (visual check, linear-alpha passes) set
  the flag too. Setting `GammaVertexColors` (on); a Gamma project is untouched.
- **The faint edge of a glow no longer shows a speckled band or a hard box.** Where the alpha curve lowers a
  faint alpha, the copy keeps the texel's alpha and darkens its colour in linear light instead: one alpha level
  near 0 is about 13 sRGB levels of white over black, so a dithered 0 / 1 alpha showed as specks. Copies are
  written again (marker version 3).
- **A flat translucent layer keeps its hue.** White at 0.18 over brown came out neutral grey: one alpha could
  match the brightness, not the colour. A layer of one flat colour (a solid fill, a text, or a frame at
  opacity < 1 holding one fill) now gets its colour scaled per channel in linear light as well, with the alpha
  that the most demanding channel needs.
- **Layer compensation measures each layer alone.** Graphics drawn over the layer are hidden while it is
  measured, as the texture pass already did, so a translucent rim or plate on top no longer counts as the
  layer's backdrop (a white slot over a dark panel was fitted over (105, 96, 91) instead of (22, 7, 0)).
- **Kerning warning only for kerned text.** `inspect_document` carries no OpenType features, so every text read
  as kerned. `features.kern` now comes from the archive's `document.json` (the layer's own, else its text
  style's `paragraph.features`), like the vertical alignment.
- **More uses of a soft texture are measured** (24 per screen, was 3), so a near-opaque plate over bright icons
  (FieldHud stash plate) weighs in its curve.
### Changed
- Faster linear-alpha passes: corrected copies are written with their `.meta` (the bridge file's settings, the
  copy's own GUID kept) and imported once, in one batch, instead of twice each; prefabs are repointed by
  rewriting their sprite references in YAML and imported in one batch, instead of opening and saving each one;
  layer compensation reads back only the layer's box. The report line gives the time split.

## [0.1.4] - 2026-11-27
39 screens of a Linear project against 0.1.3: 38 pass the visual check (was 28), none lower beyond noise;
Gear 0.781 → 0.953, WingsPanel-Wings 0.839 → 0.948, RewardPopup 0.712 → 0.944, RuneForge-Fuse
0.574 → 0.937, Revive 0.831 → 0.988.
### Fixed
- **Soft textures match FigCraft in a Linear colour space project.** A glow, a soft rim or a plate at
  texture alpha 240 (colour alpha 1, the softness in the texture) blended brighter than FigCraft's sRGB
  blend, and a colour alpha could not fix it: each texel needs its own correction. `LinearAlphaTextures`
  measures every soft texture the imported screens draw (drawn hidden, forced opaque, and as an alpha map,
  with every Graphic drawn over it hidden), fits an alpha curve `a → a'` per band of 8 alpha levels by
  least squares in linear light against the sRGB blend (non-decreasing, over every use of the texture on an
  opaque backdrop), bakes it into `<name>_linalpha.png` beside the bridge's file (ordered dither between the
  two nearest alpha levels, so the faint edge of a glow does not band) and points every Image of
  the imported prefabs at the copy. The bridge's own file is never changed, so crops and caches derived from
  it stay as they were. Runs before the per-layer alpha fit, under `CompensateLinearAlpha`; a Gamma project
  is untouched; no runtime cost.
- **A popup's dim counts as a backdrop.** Both linear-alpha passes skipped every pixel whose backdrop was
  not fully opaque, so a layer over a popup's dim (black at 0.86) kept its design alpha. A backdrop with
  alpha 200 or more now counts, its colour taken as drawn over black; a HUD over the game view (alpha 0)
  still keeps its design alpha.
- **Auto line height is the font's own.** FigCraft spaces an Auto line height by the font's line height
  (Cinzel Bold 40: 53.9 px); the import took 1.2 × size (48 px). Auto now leaves `lineHeightPx` 0 and the
  importer reads the line height off the TMP font asset for the line spacing, the half leading, the first
  baseline and the one-line test.
- **A slice grid shorter than its two corners keeps its corners.** Rows and columns of a 9-slice grid were
  ordered by their place on the frame; in a box shorter than both corners (Panel-Clean 1020 × 44, corners
  24) the middle row starts below the bottom row, the bottom edge was read off the middle row and the
  density came out 6× too high, so a 24 px corner shrank to 4 px. They are ordered by where each cell's
  crop sits in the image.
- **Font material presets no longer land in the game's font folder.** An empty `FontMaterialPresetsFolder`
  now means `<Assets Root>/FontMaterials` (was the fonts folder), so an import into another root writes no
  material beside the game's fonts.
### Added
- A warning, once per font, when a FigCraft text is kerned (the default) but the TMP font asset it maps to
  stores no kerning pairs, so Unity sets it wider. `TypeStyle.kern` carries the layer's `features.kern`.

## [0.1.3] - 2026-11-27
### Fixed
- **Translucent layers match FigCraft in a Linear colour space project.** FigCraft blends a layer below
  alpha 1 in sRGB, Unity UI in a Linear project blends it in linear light, so a light layer at low alpha
  over a dark one came out much brighter (a white slot at 0.18 over a dark panel). After each screen
  prefab is saved, `LinearAlphaCompensation` fits the alpha of every translucent Graphic and CanvasGroup
  on the screen itself: the screen is drawn with the layer hidden and at alpha 1, the sRGB blend of the
  two at the design alpha is the target, and the alpha is a least-squares fit in linear light over the
  pixels the layer changes over an opaque backdrop (parents first); a layer over a see-through part of
  the frame, such as a HUD over the game view, keeps its design alpha. Setting `CompensateLinearAlpha`
  (on); a Gamma project is untouched. Component prefabs have no backdrop and keep the design alpha; a
  screen writes the corrected alpha as an override on the instance it holds.
- **Vertical text alignment comes from the archive.** FigCraft's layer JSON leaves out a text box's
  vertical alignment; it is read from `align_v` in the saved archive's `document.json`, so a box centred
  in FigCraft no longer imports top-aligned.
- **A box one line tall does not wrap.** A fixed text box without a line break whose height holds less
  than two lines imports with no wrapping, as FigCraft shows it on one line; TMP's wider advances broke
  words ("PLA / Y").

## [0.1.2] - 2026-11-27
### Fixed
- **9-slice grids keep the art's own corners.** A `slice_ROW_COL` cell shows a crop of the shared image
  (FigCraft paint `crop`), but the crop was dropped when the file was converted, so the border was
  measured as the whole texture spread over the grid: a plate drawn larger than its art came out with
  thick, zoomed-in corners (Frame-Item, Panel-Clean, Btn-Raised bodies). `FigCraftToRest` now writes the
  crop as the paint's `imageTransform`, and the 9-slice pass reads the border in texels and the density
  off the corner cells' crops (`SliceGridCrop`, as in com.ezg.figma-bridge). A resized Btn-Raised
  instance therefore keeps a full body instead of a thin pill.
- **One image backing grids of different sizes.** The texture keeps the reference 100 pixels per unit
  and each sliced Image carries its own density in `pixelsPerUnitMultiplier`; before, the density was
  baked into the texture importer and the grid collapsed last won for every grid that shared the art.

## [0.1.1] - 2026-11-21
### Fixed
- **A frame's stroke draws above its children, as FigCraft draws it.** A node with children gets
  its whole stroke band (inside, center or outside) as the `[Stroke]` child, placed after the
  children, and its own shape sprite keeps only the fills. Before, the stroke was baked into the
  node's sprite under the children, so a child that crossed the frame's edge (a tag, a badge)
  hid the stroke that FigCraft shows on top. A node without children is drawn as before.
- **Per-side stroke weights are drawn.** A frame whose sides carry their own weights
  (`individualStrokeWeights`) was baked with its single `strokeWeight`: a card with 10 px sides
  and a base weight of 4 got a 4 px border. The shape sprite and the `[Stroke]` ring now draw each
  side at its own weight, inside, center or outside.

## [0.1.0] - 2026-11-21
### Added
- **First version, cloned from `com.ezg.figma-bridge` 0.8.19.** Imports a FigCraft file as UGUI
  prefabs with the figma-bridge pipeline (screens, components, variants, 9-slice, tint, fonts,
  post-processors, the visual check), reading FigCraft directly:
  - **File** source: a saved `.figcraft`, headless through `figcraft-cli mcp` (no app needed).
    **App** source: the file in front in the FigCraft app (`--control <port>`), unsaved changes
    included.
  - The document comes from one `inspect_document` call, converted to the importer's REST shape
    (`FigCraftToRest`): instance layers get `I<instance>;<master layer>` ids paired with the
    master by place, auto layout, sizing, constraints, corner radii, per-side strokes, gradients,
    effects and text styles are mapped.
  - Renders come from FigCraft's own `export`, shadows and outside strokes included; image fills
    are read straight from the `.figcraft` archive; the visual check's reference is FigCraft's
    render of the frame.
  - Own namespace (`FigCraftBridge`), assemblies, GUIDs, menu (**Tools > EZG Technical Art >
    FigCraft Bridge**), settings asset and cache paths, so it installs beside figma-bridge.
### Removed
- The Figma REST API (`api.figma.com`), the personal access token, the document URL, the EZG Tools
  hub (`ws://127.0.0.1:39410`) and the HTTP retry logic.
