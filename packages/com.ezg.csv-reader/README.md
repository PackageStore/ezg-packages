# EZG CSV Reader (`com.ezg.csv-reader`)

Reflection-based CSV → C# / ScriptableObject deserializer plus an editor import pipeline
for Unity. Turns CSV text into typed model arrays (supports nested objects, arrays, enums
and ID-value sheets), and — in the editor — auto-imports feature-local `CsvConfig` CSVs into
ScriptableObject collection assets via an `AssetPostprocessor`.

## Package ↔ source

Vendored from `Assets/_Project/Core/Data/CsvReader` of the Merge Two project.
Assemblies: **`Ezg.Package.CsvReader`** (Runtime) · **`Ezg.Package.CsvReader.Editor`** (Editor) ·
Namespace: `Ezg.Package.CsvReader`.

## Peer requirements

**None.** The package uses only Unity built-ins (`UnityEngine`, `UnityEditor`) and the BCL.
There are no `package.json` dependencies and no third-party libraries to install.

## Contents

**Runtime (`Ezg.Package.CsvReader`)**
- `CsvReader` — static deserializer: `Deserialize<T>(...)`, `DeserializeIdValue<T>(...)`, `ParseCsv(...)`.
- `CSVReaderManager` — alternative grid/dictionary CSV reader (regex-based row splitting, grouping helpers).
- `ICsvCustomData` — implement on a collection ScriptableObject to receive raw CSV text directly.
- `CsvReaderConfig` — `ScriptableObject` holding project-specific import settings (see below).
- `CsvReaderSettings` — resolver that finds the project's `CsvReaderConfig` asset (editor) and falls
  back to an in-memory default, so the module runs even before a config asset exists.
- `CsvPathUtility` — path helpers for the feature-local `CsvConfig` → `Resources` mapping.

**Editor (`Ezg.Package.CsvReader.Editor`)**
- `BasePostProcessor` — `AssetPostprocessor` that re-imports changed managed CSVs, deletes orphaned
  generated assets, and regenerates the asset-name constant class.
- `CsvImportManager` — manual/forced import entry point (`ImportAllData`), MD5-cached change detection.
- `AssetPathGenerate` — generates the `CsvAssetDir` constant class from discovered `CsvConfig` CSVs.

## Configuration (`CsvReaderConfig`)

Create one asset per project via the Project window right-click menu
**Create → Ezg/Csv Reader/Project config** (bootstraps the `CsvReaderConfig` asset and generates
`GenDataManager.cs`). The resolver (`CsvReaderSettings.Current`) auto-discovers it; call
`CsvReaderSettings.Invalidate()` after creating or editing it.

| Field | Default | Purpose |
|-------|---------|---------|
| `csvConfigFolderName` | `CsvConfig` | Folder name marking a feature-local CSV. |
| `resourcesFolderName` | `Resources` | Sibling folder that receives the generated `.asset`. |
| `collectionSuffix` | `Collection` | `Foo` → `FooCollection` class lookup. |
| `dataSuffix` | `Model` | `Foo` → `FooModel` class lookup. |
| `cachedFileName` | `FileInfo.txt` | MD5 cache file (under `Assets/`). |
| `sharedModelPrefixes` | `Skill_`, `Passive_`, … | Collections sharing one model class. |
| `generatedClassDirectory` | `/Assets/_Project/Features/_Shared/` | Where `CsvAssetDir.cs` is written. |
| `generatedClassName` | `CsvAssetDir` | Generated constant class name. |

> **Known debt:** the default values for `sharedModelPrefixes`, `generatedClassDirectory` and
> `generatedClassName` are carried over from the Merge Two project. They are **overridable** on the
> config asset — set them to match your own project layout. No game-specific types are referenced
> in code; only these default strings reflect the original host project.

## Usage

```csharp
using Ezg.Package.CsvReader;

// CSV text -> typed array (header row maps to public fields by name)
MyRowModel[] rows = CsvReader.Deserialize<MyRowModel>(csvText, assetFile: "MyData.csv");

// ID-value sheet (column 0 = field name, column 1 = value) -> single object
MySettings s = CsvReader.DeserializeIdValue<MySettings>(csvText);
```

In the editor, drop a `*.csv` inside any `CsvConfig/` folder that has a matching
`<Name>Collection` + `<Name>Model` class; `BasePostProcessor` imports it into
`../Resources/<Name>.asset` automatically on asset change.
