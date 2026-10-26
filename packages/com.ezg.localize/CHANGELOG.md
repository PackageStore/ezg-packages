# Changelog

## [0.1.4] - 2026-10-14

### Changed
- `ThaiChecker` font is configurable: static `NormalFontResource` / `ThaiFontResource` (defaults unchanged)
  plus an optional per-component `normalFontOverride`. When the normal font resource is missing, the Text
  keeps its original font instead of rendering with `null`.
- `GenAsset` adapts CSV results with `CsvReader.AdaptToFieldType` (requires com.ezg.csv-reader 0.3.0).

## [0.1.2] - 2026-06-15

- Moved the manual LocalizeDownloader asset creation menu to `Create > Ezg > Localization > Config`.

## [0.1.1] - 2026-06-15

- Added default LocalizeDownloader config creation, documented the consumer-owned localization data workflow, and refreshed package metadata.

## [0.1.0] - 2026-06-13

Initial publish — extracted from `Assets/_Project/Core/Localize` of Merge Two game project.
