# Changelog

## [0.1.0] - 2026-06-16
### Added
- Initial release extracted from `Assets/BuildReport` (Build Report Tool v3.9.6).
- Build Report editor window: used/unused asset lists with per-asset imported sizes,
  size breakdown by category, script DLL list, scenes in build, and project settings.
- Public API `BuildReportTool.ReportGenerator.CreateReport(...)` for generating reports
  from custom build scripts.
- Editor-only assembly definition `BuildReportTool.Editor` added to scope all sources.
