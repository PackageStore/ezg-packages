## [0.1.1] - 2026-06-14

- Add missing .meta files for README.md, CHANGELOG.md, package.json to fix Unity "immutable folder" import errors


# Changelog

## [0.1.0] - 2026-06-10

- Initial release: reflection-based `CsvReader` deserializer (typed arrays, nested objects/arrays,
  enums, ID-value sheets), `CSVReaderManager` grid reader, `ICsvCustomData` hook, and the editor
  import pipeline (`BasePostProcessor`, `CsvImportManager`, `AssetPathGenerate`) with MD5-cached
  change detection. Project-specific paths/suffixes externalized to `CsvReaderConfig`. No
  third-party dependencies (Unity built-ins only).