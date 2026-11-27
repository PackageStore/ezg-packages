# Changelog

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
