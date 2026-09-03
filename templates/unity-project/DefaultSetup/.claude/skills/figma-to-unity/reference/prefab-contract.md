# Prefab contract

Node-type-to-asset mapping for the UnityFigmaBridge import pipeline. Every claim
cites the source file and line so a future reader can verify it against the code
in the bridge repo (`../UnityFigmaBridge`, sources under `UnityFigmaBridge/`).

## Node type table

| Figma node type | Unity output | Source |
|---|---|---|
| FRAME (parent is CANVAS or SECTION) | Screen prefab at `FigmaPaths.FigmaScreenPrefabFolder` | `FigmaAssetGenerator.cs:226-231` via `FigmaDataUtils.IsScreenNode` (`:532-537`) |
| COMPONENT (standalone) | Component prefab at `<ComponentPrefabFolder>/<SafeName>.prefab` | `ComponentManager.GenerateComponentAssetFromNode` (`:68-76`) |
| COMPONENT (child of COMPONENT_SET) | Variant prefab at `<ComponentPrefabFolder>/<SetName>/<NormalisedVariant>.prefab` | `ComponentManager.cs:70-73`, `FigmaPaths.GetPathForComponentPrefab` (`:103-120`) |
| COMPONENT_SET | Folder `<ComponentPrefabFolder>/<SetName>/` containing variant prefabs + `axis-intent.json` | `FigmaPaths.cs:106-115`, `ComponentAxisIntent.WriteAxisIntent` (`:25-39`) |
| INSTANCE (definition found) | `FigmaComponentNodeMarker` placeholder, later replaced with `PrefabUtility.InstantiatePrefab` of the component prefab | `FigmaAssetGenerator.cs:147-153`, `ComponentManager.InstantiateComponentPrefabs` (`:125-216`) |
| INSTANCE (definition missing) | Built inline as a regular node — no prefab link | `FigmaAssetGenerator.cs:155-156` |
| CANVAS (Figma page) | Page prefab at `FigmaPaths.FigmaPagePrefabFolder` — written but nothing reads it | `FigmaAssetGenerator.cs:42-47` |
| SECTION | Registered with `PrototypeFlowController` if `BuildPrototypeFlow` is on | `FigmaAssetGenerator.cs:238-239` |
| All other (RECTANGLE, ELLIPSE, TEXT, GROUP, etc.) | GameObject with UGUI components under the parent; no separate prefab | `FigmaNodeManager.CreateUnityComponentsForNode` / `ApplyUnityComponentPropertiesForNode` |

## Screen name table

`FigmaPaths.GetPathForScreenPrefab` (`FigmaPaths.cs:83-96`) resolves the output
path for a screen FRAME:

1. If `ScreenNameOverrides` contains a row where `FrameName` matches `node.name`,
   the prefab is saved as `<ScreenPrefabFolder>/<PrefabName>.prefab`.
2. If no match and `OnlyImportListedScreens` is **true**, returns `null` — the
   screen is skipped entirely (`FigmaAssetGenerator.cs:229-230` guards on null).
3. If no match and `OnlyImportListedScreens` is **false**, falls through to the
   raw frame name.

Duplicate-name collisions append `_N` (`FigmaPaths.cs:88-89`).

## Component prefab filenames

`FigmaPaths.GetPathForComponentPrefab` (`FigmaPaths.cs:103-120`):

- **Standalone component:** `<ComponentPrefabFolder>/<MakeValidFileName(name)>.prefab`
- **Variant (child of COMPONENT_SET):**
  `<ComponentPrefabFolder>/<MakeValidFileName(setName)>/<NormalisedVariant>.prefab`

`NormaliseVariantName` (`FigmaPaths.cs:126-130`) turns Figma's `State=Normal, Color=Green`
into `State-Normal_Color-Green`, then `MakeValidFileName` strips filesystem-unsafe
characters.

`MakeValidFileName` (`FigmaPaths.cs:145-151`) replaces characters in
`Path.GetInvalidFileNameChars()` plus `.` with `_`.

## Axis intent

For each COMPONENT_SET, `ComponentAxisIntent.WriteAxisIntent`
(`ComponentAxisIntent.cs:25-39`) writes `axis-intent.json` in the set's folder.
It reads the `UNITY:` directive from the set's Figma description
(`figmaFile.componentSets[parentNode.id].description`). Format:

```
UNITY: runtime-axis=State; design-axis=Color
```

Axes not classified by the directive appear in `Variants`. Sets with no
`UNITY:` directive produce empty `RuntimeAxes` and `DesignAxes`; all axes go to
`Variants`. No runtime code reads this file yet.

## 9-slice collapse

When `CollapseSliceGrids` is on, `NineSlicePass.Run` (`NineSlicePass.cs:14`)
iterates component and screen prefabs after component instantiation.

`FigmaNineSlice.Apply` (`FigmaNineSlice.cs:35`) walks each prefab depth-first:

1. **Detect:** every child matches `slice_<row>_<col>` regex and all cell sprites
   share the same texture dimensions (`FigmaNineSlice.cs:59-81`).
2. **Guard:** cells with `FigmaImage` stroke or corner radius are skipped — a
   plain `Image` cannot reproduce those shader features (`:69-75`).
3. **Border:** measured from parent-local position (`localPosition + rect.xMin`),
   never `anchoredPosition`. A grid with fewer than 3 columns gets no horizontal
   border; likewise rows (`:127-130`).
4. **Sprite:** uses the existing image fill asset when it has an asset path,
   setting the border on its `TextureImporter`. Falls back to a RenderTexture
   read-back and MD5-named PNG when the sprite has no asset path (`:144-152`).
5. **Replace:** destroys all slice children, ensures the parent has a plain
   `Image` (not `FigmaImage`), and sets `Image.Type.Sliced` (`:87-104`).

The collapse is an optimisation. A wrong border is a regression — the uncollapsed
rendering (nine `FigmaImage` components each using `ImageTransform` to crop
correctly) is already correct.

## Output folder defaults

All configurable on the settings asset (`UnityFigmaBridgeSettings.cs`).
`FigmaPaths.Configure` reads them at import start. Each field's live value is
whatever the settings asset holds; read it there rather than assuming a folder.

| Setting field | Default (blank = this) |
|---|---|
| `AssetsRootFolder` | `Assets/_Project/UI` |
| `ScreenPrefabFolder` | `<root>/Screens` |
| `ComponentPrefabFolder` | `<root>/Components` |
| `PagePrefabFolder` | `<root>/Pages` |
| `ImageFillFolder` | `<root>/Sprites`; image fills always land in `<ImageFillFolder>/<Figma document name>` |

Non-configurable folders derived from root: `ServerRenderedImages`,
`FontMaterialPresets`, `Fonts`.
