# com.ezg.localize

Lightweight localization system for Unity: singleton `Localization` service, `LanguageData` ScriptableObjects loaded from `Resources`, Google Sheets CSV downloader, and asset generation editor tools.

## Source folder mapping

| Package path | Source (game project) |
|---|---|
| `Runtime/` | `Assets/_Project/Core/Localize/` (runtime files only) |
| `Editor/` | `Assets/_Project/Core/Localize/Editor/` + `GenAsset.cs` |

## Runtime API

```csharp
// Get active language
Localization.Current.localCultureInfo

// Look up a localized string
string text = Localization.Current.Get("Common", "ok_button");

// React to language changes
Localization.Current.CultureInfoChanged += (s, e) => Refresh();
```

## Data layout (consumer must provide)

Place compiled `LanguageData` assets at:
```
Assets/Resources/LocalizationData/{lang}/{category}.asset
```
Where `{lang}` is a lowercase, hyphen-stripped CultureInfo name (e.g. `en`, `vi`, `zhcn`).

Use `LocalizeDownloader` ScriptableObject (Create → Localization → Google Sheet) to download CSVs from Google Sheets and generate assets automatically.

## Dependencies (package.json)

| Package | Version |
|---|---|
| `com.ezg.csv-reader` | 0.1.0 |
| `com.ezg.dictionary` | 0.1.2 |
| `com.ezg.easy-event-manager` | 2.3.0 |

## Peer requirements

The consuming project must also have:

| Assembly | Package / Source |
|---|---|
| `UniTask` | `com.cysharp.unitask` (GitHub or registry) |

## Known debt

- `LocalizeCategory` enum contains game-specific category names (`Equipment`, `Hero`, `Item`, etc.). Consuming projects may extend or replace this enum with their own localization categories.
- `GenAsset.GenConfig()` assumes a `Csv → Resources` directory mapping — this is a convention from the source project; override via `GenAsset.Gen()` for custom paths.
