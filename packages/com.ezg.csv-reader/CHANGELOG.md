## [0.3.0] - 2026-10-14

### Changed
- **`CsvReader.Deserialize(text, Type, file)` always returns an array** (0, 1 or N elements). Before, a CSV
  with a single data group returned a lone object and `Deserialize<T>` threw `InvalidCastException`.
  Callers that set a field use the new `CsvReader.AdaptToFieldType(value, fieldType)` (array field → array,
  single field → first element, null for none). `BasePostProcessor` and com.ezg.localize `GenAsset` do.

### Fixed
- Empty CSV returns an empty array instead of throwing.
- Invalid (non-lowercase) or duplicate header columns are skipped with a warning instead of aborting every
  later column; rows shorter than the header no longer index out of range.
- An array field with no matching columns gets an empty array plus a warning.
- On a parse exception the type and file are logged and an empty array is returned.
- `AssetPathGenerate` groups duplicate collection names in one warning listing every path.

## [0.2.6] - 2026-07-30

### Fixed
- **`BasePostProcessor.GenAsset` no longer silently keeps stale data.** When a CSV collapses to
  exactly one group, `CsvReader.Deserialize` returns a single object instead of an array while the
  target field is `Model[]`, so `field.SetValue` threw. The old code caught it, logged a vague
  warning ("Chỉ có một phần tử...") and `return`ed — leaving the `.asset` with its **previous**
  contents. Designers saw "import succeeded" while the game kept running old numbers. The single
  object is now wrapped into a 1-element array, and any remaining failure is a `LogError` that says
  explicitly the asset was left untouched. (This fix already existed in the game project the module
  was extracted from; it was lost during packaging.)
- `GenAsset` now guards against a null target field (collection with neither a `Model[]` field nor
  `dataGroups`) instead of throwing a `NullReferenceException`.
- `CsvImportManager.ImportAllData` uses `ImportAssetOptions.ForceUpdate` instead of `Default`.
  `Default` is skipped by Unity when the content hash is unchanged, so "Force Reload all data CSV"
  could silently do nothing in exactly the case it exists for — a stale `.asset` next to an
  unchanged `.csv`.

### Changed
- CSVs whose collection implements `ICsvCustomData` no longer require a matching `<Name>Model`
  class. The collection parses the raw text itself (e.g. `LanguageData` in the Localize module),
  so demanding a model class only produced a spurious "Data class not found" skip.
- `AssetPathGenerate.Generate()` sorts entries ordinally. The order used to follow
  `Directory.EnumerateFiles`, which is filesystem-dependent — the generated `CsvAssetDir.cs`
  reshuffled between macOS and Windows, causing git churn and needless recompiles.
  **One-off effect:** the first run after upgrading rewrites `CsvAssetDir.cs` (and therefore any
  file generated from it) in sorted order. Content is equivalent; only ordering changes.
- `ImportAllData` reports what actually happened (`Reimport N/M CSV có thay đổi` /
  `Không CSV nào thay đổi`) instead of an unconditional "Load success", and drops cache entries
  whose CSV no longer exists so the MD5 cache file stops accumulating dead paths.

## [0.2.5] - 2026-07-03

### Fixed
- `CsvImportManager.ImportAllData()` no longer shows a blocking `EditorUtility.DisplayDialog`
  ("Load success") after importing CSVs — replaced with `Debug.Log`. The modal dialog stalled
  the Unity Editor main thread waiting for a manual "OK" click, which hung any automated/MCP-driven
  session (e.g. an agent invoking `CustomEditor/BlackFace/Reload all data CSV`) until a human
  dismissed it.

## [0.2.3] - 2026-06-20

### Added
- `Ezg/Csv Manager` editor window (`CsvManagerWindow`) that browses the project's CSV
  files grouped by feature/category, with search, category filter and Ping/Open/Copy
  actions. Extracted from the Merge Two game project into this package.

### Changed
- The CSV Manager window now lives under namespace `Ezg.Package.CsvReader.Editor`.
- Added a scoped dependency on `com.ezg.editor-ui@0.1.0` for the shared editor styling
  (`EditorBaseStyle`) used by the CSV Manager window.

### Notes
- Known debt: `CsvManagerWindow` scans the host project's `Assets/_Project` tree directly
  (game-specific) instead of `CsvPathUtility`. In a project without that folder it simply
  lists nothing.

## [0.2.2] - 2026-06-19

### Changed
- Moved the project bootstrap menu from the top menu bar `Ezg/CsvReader/Project setup` to the
  Project window right-click menu **Create/Ezg/Csv Reader/Project config** (same `ProjectSetup` action,
  new `MenuItem` path). No behavior change — only the menu location/label.

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