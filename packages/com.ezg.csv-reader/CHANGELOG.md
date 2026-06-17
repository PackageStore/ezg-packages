## [0.2.1] - 2026-06-18

### Changed
- Default `CsvReaderConfig.generatedClassDirectory` now points to
  `/Assets/_Project/Features/_Shared/GameData/` so the generated `CsvAssetDir.cs` constant class
  lands alongside the generated `GenDataManager.cs`/`DataManager.Generated.cs` (same GameData folder)
  instead of `_Shared/`. Projects with an existing `CsvReaderConfig` asset must update that asset's
  field to take effect (the asset overrides this default).

## [0.2.0] - 2026-06-18

### Added
- `Ezg/CsvReader/Project setup` editor menu (`ProjectSetup`) that bootstraps a `CsvReaderConfig`
  asset (lets the user pick its location) and generates `GenDataManager.cs` into the configured
  directory from a bundled `Editor/Templates/GenDataManager.cs.txt` template, prompting to confirm
  before overwriting an existing file.
- `CsvReaderConfig.dataManagerDirectory` and `CsvReaderConfig.dataManagerNamespace` fields so the
  generated DataManager output path and namespace are externalized (config-driven).

### Removed
- The old `[CreateAssetMenu] Ezg/CsvReader/Config` create-asset menu on `CsvReaderConfig`, replaced
  by the `Project setup` menu above.

## [0.1.1] - 2026-06-14

- Add missing .meta files for README.md, CHANGELOG.md, package.json to fix Unity "immutable folder" import errors


# Changelog

## [0.1.0] - 2026-06-10

- Initial release: reflection-based `CsvReader` deserializer (typed arrays, nested objects/arrays,
  enums, ID-value sheets), `CSVReaderManager` grid reader, `ICsvCustomData` hook, and the editor
  import pipeline (`BasePostProcessor`, `CsvImportManager`, `AssetPathGenerate`) with MD5-cached
  change detection. Project-specific paths/suffixes externalized to `CsvReaderConfig`. No
  third-party dependencies (Unity built-ins only).