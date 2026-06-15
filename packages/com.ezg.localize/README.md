# com.ezg.localize

Lightweight localization system for Unity: singleton `Localization` service, `LanguageData` ScriptableObjects loaded from `Resources`, Google Sheets CSV downloader, and editor tools that generate localization assets owned by the consuming project.

## Source folder mapping

| Package path | Source (game project) |
|---|---|
| `Runtime/` | `Assets/_Project/Core/Localize/` (runtime files only) |
| `Editor/` | `Assets/_Project/Core/Localize/Editor/` + `GenAsset.cs` |

## What the package owns

The package owns the runtime/editor code:

- `Localization`, `Locale`, `LocalizeProvider`
- `LanguageData` ScriptableObject
- `LocalizeCategory`
- `ThaiChecker`
- `LocalizeDownloader` editor config and inspector
- CSV-to-`LanguageData` generation helpers

The package does not ship game localization data. Each consuming project should keep its own CSV files and generated `LanguageData` assets under `Assets/`.

## Runtime API

```csharp
// Get active language
Localization.Current.localCultureInfo

// Look up a localized string
string text = Localization.Current.Get("Common", "ok_button");

// React to language changes
Localization.Current.CultureInfoChanged += (s, e) => Refresh();
```

`LocalizeProvider` loads data with:

```csharp
Resources.Load<LanguageData>($"LocalizationData/{lang}/{category}");
```

`lang` is the active `CultureInfo.Name` converted to lowercase with hyphens removed. Examples: `en`, `vi`, `zhcn`.

## Consumer data layout

Use the default project-owned layout:

```text
Assets/_Project/Localize/LocalizeDownloader.asset
Assets/_Project/Localize/LocalizationData/{lang}/{category}.csv
Assets/_Project/Localize/Resources/LocalizationData/{lang}/{category}.asset
```

Runtime only needs the generated `.asset` files under a `Resources` folder:

```
Assets/.../Resources/LocalizationData/{lang}/{category}.asset
```

The CSV files are editor inputs. The generated `LanguageData` assets are runtime inputs.

## Create the downloader config

After installing the package, create a default config from Unity:

```text
Tools > Localization > Create Downloader Config
```

This creates:

```text
Assets/_Project/Localize/LocalizeDownloader.asset
```

with default values:

| Field | Default |
|---|---|
| `downloadPath` | Merge Two Google Sheet URL |
| `saveFilePath` | `Assets/_Project/Localize/LocalizationData` |
| `codeList` | `en`, `vi`, `pt`, `id`, `ru`, `th`, `es`, `ko`, `ja`, `zhcn`, `zhtw`, `fr`, `de`, `it`, `pl`, `nl`, `tr` |
| `itemList` | `common`, `item`, `shop`, `tutorial`, `scene`, `settings`, `email`, `event`, `notification` |

You can also create a config manually:

```text
Create > Localization > Google Sheet
```

Manual creation now uses the same default values. Existing or empty configs can be reset from the inspector with `Reset Default Values`.

## Download and generate localization data

1. Select `Assets/_Project/Localize/LocalizeDownloader.asset`.
2. Edit `downloadPath` if the project uses a different Google Sheet.
3. Keep only the needed language codes in `codeList`.
4. Enable `download` only for the sheets/categories you want to regenerate.
5. Click `Download Data Language`.

The downloader will:

1. Delete and recreate `saveFilePath`.
2. Download each enabled Google Sheet tab as CSV.
3. Write CSV files under:

```text
Assets/_Project/Localize/LocalizationData/{lang}/{category}.csv
```

4. Generate `LanguageData` assets under the sibling `Resources` folder:

```text
Assets/_Project/Localize/Resources/LocalizationData/{lang}/{category}.asset
```

If CSV files already exist and you only want to rebuild assets, click `Generate Assets from CSVs` in the inspector.

## Runtime lookup

Use `Localization.Current.Get(category, key)` directly:

```csharp
var text = Localization.Current.Get("common", "ok");
```

Or use the generated/category enum pattern from the consuming project:

```csharp
var text = GameSystems.Localize("ok", LocalizeCategory.Common);
```

`LocalizeCategory.Common` maps to `common`, `LocalizeCategory.Shop` maps to `shop`, and so on in projects that use the existing `GameSystems.Localize` wrapper.

## Important packaging notes

- Do not keep both the source folder under `Assets/_Project/Core/Localize` and this package dependency in the same project. The scripts share GUIDs and types, so Unity will report duplicate scripts or duplicate package/type conflicts.
- The package is code-only. Localization CSVs and generated `.asset` files belong to the consuming project.
- If using `ThaiChecker`, provide fonts at `Resources/NotoSans/NotoSans_Thai` and `Resources/PoetsenOne-Regular`, or customize the component for your project.
- Installed UPM packages are immutable. Generate CSV and `Resources/LocalizationData` assets under `Assets/`, not under `Packages/`.

## Dependencies (package.json)

| Package | Version |
|---|---|
| `com.ezg.csv-reader` | 0.1.1 |
| `com.ezg.dictionary` | 0.1.2 |
| `com.ezg.easy-event-manager` | 2.3.1 |

## Peer requirements

The consuming project must also have:

| Assembly | Package / Source |
|---|---|
| `UniTask` | `com.cysharp.unitask` (GitHub or registry) |

## Known debt

- `LocalizeCategory` enum contains game-shaped category names (`Equipment`, `Hero`, `Item`, etc.). Consuming projects may extend or replace this enum with their own localization categories.
- `GenAsset.GenConfig()` assumes a `Csv -> Resources` directory mapping. This is a source-project convention; use `LocalizeDownloader.GenerateLanguageAssets()` or `GenAsset.Gen()` for custom paths.
- The default `LocalizeDownloader` Google Sheet URL is project-specific and should be changed by consumers that do not use the Merge Two sheet.
