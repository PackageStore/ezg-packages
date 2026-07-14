# Changelog

## [0.1.1] - 2026-07-14
### Changed
- Moved menus under **EZG Technical Art**: window is now `Tools ▸ EZG Technical Art ▸ Icon CSV Generator`; settings asset created via `Assets ▸ Create ▸ EZG Technical Art ▸ Icon CSV Generator ▸ Settings`.

## [0.1.0] - 2026-07-14
### Added
- Initial release. Extracted from `Assets/_Game/StickmanForge/Scripts/Editor/IconGenerator` in the StickmanForge project into a standalone UPM package.
- `Tools ▸ Ezg ▸ Icon CSV Generator` editor window: CSV-driven batch icon generation via Google Gemini, per-group prompt templates and reference images, cost estimation, parallel generation, on-disk cache, per-row review/approve workflow, and flat white-background PSD export.
- `IconGeneratorSettings` ScriptableObject (**Assets ▸ Create ▸ Ezg ▸ Icon CSV Generator ▸ Settings**) now holds the two project paths — `incomingRoot` (PSD staging) and `referenceImagesRoot` — so the package carries no hardcoded project layout.
- Bundled Lucide toolbar icons under `Editor/ButtonIcons/` (fail-soft: buttons fall back to text if the PNGs are unavailable).
- EditMode tests for the PSD encoder, filename builder, and CSV row filter.
