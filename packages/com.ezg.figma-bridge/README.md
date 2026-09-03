# EZG Unity Figma Bridge

Imports a Figma document into Unity as native UGUI prefabs: screens, components, variants,
image fills and fonts, driven from an editor window and a settings asset.

## Install

Feature Hub (**Ezg > Feature Hub**, UPM Packages tab), or add it directly:

```json
"scopedRegistries": [
  { "name": "Easygoing code base", "url": "https://upm-registry-worker.developer-a1f.workers.dev", "scopes": ["com.ezg"] }
],
"dependencies": { "com.ezg.figma-bridge": "0.1.0" }
```

Requires Unity 6000.3 or newer. Dependencies (`com.unity.ugui`, `com.unity.nuget.newtonsoft-json`)
resolve automatically from the Unity registry.

## Setup

1. Open **Tools > EZG Technical Art > Figma Bridge**.
2. Paste the Figma document URL.
3. Set a [Figma personal access token](https://www.figma.com/developers/api#authentication).
   It is stored per machine in `PlayerPrefs`, never in the project or in source control.
4. Choose which pages and screens to import, then sync.

## Settings

The settings asset drives every path and toggle. The fields worth knowing:

| Field | Controls |
|---|---|
| `AssetsRootFolder` | root for every generated asset; blank = `Assets/_Project/UI` |
| `ScreenPrefabFolder` | where screen prefabs are written; blank = `<root>/Screens` |
| `ComponentPrefabFolder` | where component prefabs are written; blank = `<root>/Components` |
| `PagePrefabFolder` | where page prefabs are written; blank = `<root>/Pages` |
| `ImageFillFolder` | parent of the sprite folders; image fills go in `<folder>/<Figma document name>`; blank = `<root>/Sprites` |
| `OnlyImportListedScreens` | import just the named screens instead of every frame |
| `CollapseSliceGrids` | collapse a 9-slice plate into one sprite with borders |

## Behaviour worth knowing

These are on by default and shape the output:

- **9-slice collapse.** A plate built as a grid of `slice_*` cells is imported as a single
  sprite carrying `Sprite.border`, not as nine child images.
- **Node-named image fills.** Sprites are named after the node and its owner rather than the
  Figma `imageRef` hash, so a re-import produces stable, readable asset names.
- **Axis-intent output.** A component set writes `axis-intent.json` beside its variant prefabs
  recording each variant axis. Variants are separate prefabs; there is no runtime state code.
- **Fonts.** Font families named by the document are fetched live from Google Fonts and placed
  in `Assets/TextMesh Pro/Fonts`. A family Figma names but Google does not serve needs a
  substitute set in the settings asset.

## Companion skill

`figma-to-unity` in Feature Hub's AI Feature tab drives this package from Claude Code, and
`psd-to-figma`, `figma-components`, `figma-tokens` and `figma-hygiene` cover the
design-file work that feeds it.

## Licence

MIT. See [LICENSE](LICENSE) for the full notice and copyright holders.
