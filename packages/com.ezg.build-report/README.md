# EZG Build Report Tool

Editor tool that generates a detailed report of a Unity build: used vs unused assets,
per-asset imported sizes, a size breakdown by category, script DLLs, scenes in the build,
and project settings — to help track down what is bloating the build.

This is a vendored UPM distribution of **Anomalous Underdog's Build Report Tool (v3.9.6)**.

## Package ↔ source folder

This package mirrors the original asset folder layout:

| Package path | Original |
|---|---|
| `Scripts/Editor/` | `Assets/BuildReport/Scripts/Editor/` (all C#, compiled into `BuildReportTool.Editor`) |
| `GUI/` | `Assets/BuildReport/GUI/` (GUI skins + icons) |
| `README.txt`, `VERSION.txt`, `license.txt` | original vendor files (kept verbatim) |

## Usage

Open the window via the Unity menu **Window ▸ Build Report**. After a build, the report
window appears automatically (configurable in its Options screen). To generate a report
from a custom build script, call `BuildReportTool.ReportGenerator.CreateReport(...)` after
`BuildPipeline.BuildPlayer()` — see `CustomBuildScriptExample.txt`.

## Assembly

All code compiles into a single Editor-only assembly: **`BuildReportTool.Editor`**
(`includePlatforms: ["Editor"]`). Build scripts that call the public API and live in their
own asmdef must add `BuildReportTool.Editor` to their references.

## Dependencies

None — no scoped (`com.ezg.*`) dependencies and no third-party peer requirements. The tool
uses only `UnityEngine`, `UnityEditor`, and the .NET BCL.

## License

See `license.txt` (bundled). This is third-party software redistributed internally.
