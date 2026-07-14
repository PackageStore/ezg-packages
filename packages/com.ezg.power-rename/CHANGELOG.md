# Changelog

## [0.1.3] - 2026-07-14
### Changed
- Moved the editor window from `Tools ▸ Power Rename` to `Tools ▸ EZG Technical Art ▸ Power Rename`.

## [0.1.2] - 2026-07-14
### Added
- **Capitalize First Letter** and **Capitalize Each Word** options (Each Word wins when both are on; keeps `space / _ - .` delimiters).
- **Replace Content in File** option — runs the find/replace inside a Project text file's content. Project text files only: scene GameObjects are unaffected and binary files (images, prefabs, .asset…) are skipped to avoid corruption; requires a non-empty Find.

## [0.1.1] - 2026-07-14
### Changed
- Reorganized the editor window into grouped sections: Append (prefix/suffix), Trim, Find & Replace, Extension.
- Added Vietnamese tooltips to every input field.
### Added
- Green target indicator showing whether the rename targets scene GameObjects or project files, with counts.

## [0.1.0] - 2026-07-14
### Added
- Initial release extracted from `Assets/_Project/Visual/ArtAsset/Scripts/PowerRename.cs`.
- `Tools ▸ Power Rename` editor window: prefix/suffix, trim start/end, find & replace.
- **New Extension** field to change a project asset's file extension via `AssetDatabase.MoveAsset` (leave empty to keep the original).
