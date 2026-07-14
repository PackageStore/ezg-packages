# Changelog

## [0.1.1] - 2026-07-14
### Added
- Operates on the current **Project-window selection** — selected textures and/or folders — instead of a single folder field.
- **Recursive** checkbox: when a folder is selected, include textures in its subfolders (on) or only those directly inside it (off).
- Power-Rename-style target indicator (green when textures are targeted, amber when the selection is empty) with folder/file counts; the Apply button is disabled when nothing is targeted.
### Changed
- Accepts **any** `TextureImporter` asset (png, psd, jpg, tga, exr…), not just PNG/PSD.
### Removed
- The `Target Folder` object field and its folder scan (superseded by the Project selection).

## [0.1.0] - 2026-07-14
### Added
- Initial release extracted from `Assets/_Project/Editor/Shared/TextureFormatOverrideWindow.cs`.
- `Tools ▸ EZG Technical Art ▸ Texture Format Override` editor window: batch-applies texture import settings to every PNG/PSD in a selected folder.
- Two mutually-exclusive modes — **Override for Android and iOS** (per-platform format, compression quality, optional forced max size, PSD remove-matte) and **Custom Max Size** (Default-tab Max Texture Size slider 32–4096 + NPOT scale None).
