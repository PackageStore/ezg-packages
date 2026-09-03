# Changelog

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
