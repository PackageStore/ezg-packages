# Changelog

## [0.1.0] - 2026-06-20
### Added
- Initial release extracted from `Assets/_Project/3rdParty/QuickOutline`.
- `QuickOutline` component rendering a two-pass mask + fill outline for mesh and
  skinned-mesh renderers.
- Five outline modes: OutlineAll, OutlineVisible, OutlineHidden,
  OutlineAndSilhouette, SilhouetteOnly.
- Adjustable outline color and width, with optional precomputed (baked) smooth
  normals for large meshes.
- Bundled `OutlineMask` / `OutlineFill` materials and shaders under
  `Runtime/Resources`.
