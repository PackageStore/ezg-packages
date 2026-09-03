# Changelog

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
