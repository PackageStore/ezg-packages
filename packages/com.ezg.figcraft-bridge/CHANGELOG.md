# Changelog

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
