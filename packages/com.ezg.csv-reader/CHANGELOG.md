# Changelog

## [0.1.0] - 2026-06-10

- Initial release: reflection-based `CsvReader` deserializer (typed arrays, nested objects/arrays,
  enums, ID-value sheets), `CSVReaderManager` grid reader, `ICsvCustomData` hook, and the editor
  import pipeline (`BasePostProcessor`, `CsvImportManager`, `AssetPathGenerate`) with MD5-cached
  change detection. Project-specific paths/suffixes externalized to `CsvReaderConfig`. No
  third-party dependencies (Unity built-ins only).
