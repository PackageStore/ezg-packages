# Prefab contract

Node-type-to-asset mapping for the EZG Figma Bridge import pipeline. Every claim
names the source file and symbol so a reader can verify it against the package
source (`packages/com.ezg.figma-bridge/UnityFigmaBridge/Editor/` in the
`ezg-packages` monorepo). Symbols are cited instead of line numbers because the
lines move with every release.

## Node type table

| Figma node type | Unity output | Source |
|---|---|---|
| FRAME (parent is CANVAS or SECTION) | Screen prefab at `FigmaPaths.FigmaScreenPrefabFolder` | `FigmaAssetGenerator.BuildFigmaNode`, `FigmaDataUtils.IsScreenNode` |
| COMPONENT (standalone) | Component prefab at `<ComponentPrefabFolder>/<SafeName>.prefab` | `ComponentManager.GenerateComponentAssetFromNode` |
| COMPONENT (child of COMPONENT_SET) | Variant prefab at `<ComponentPrefabFolder>/<SetName>/<NormalisedVariant>.prefab` | `ComponentManager.GenerateComponentAssetFromNode`, `FigmaPaths.GetPathForComponentPrefab` |
| COMPONENT_SET | Folder `<ComponentPrefabFolder>/<SetName>/` containing variant prefabs + `axis-intent.json` | `FigmaPaths.GetPathForComponentPrefab`, `ComponentAxisIntent.WriteAxisIntent` |
| INSTANCE (definition found) | `FigmaComponentNodeMarker` placeholder, later replaced with `PrefabUtility.InstantiatePrefab` of the component prefab; overrides applied to the nested nodes | `FigmaAssetGenerator.BuildFigmaNode`, `ComponentManager.InstantiateComponentPrefabs`, `ComponentManager.ApplyFigmaProperties` |
| INSTANCE nested in an instance, component changed (variant or instance swap) | The nested prefab instance is replaced by the prefab of the component the node names; place, transform and node id kept | `ComponentManager.SwapChangedComponent` |
| INSTANCE (definition missing) | Built inline as a regular node, no prefab link | `FigmaAssetGenerator.BuildFigmaNode` |
| CANVAS (Figma page) | Page prefab at `FigmaPaths.FigmaPagePrefabFolder`, written but nothing reads it | `FigmaAssetGenerator.SaveFigmaPageAsPrefab` |
| SECTION | Registered with `PrototypeFlowController` if `BuildPrototypeFlow` is on | `FigmaAssetGenerator.RegisterFigmaSection` |
| Node a plain `Image` cannot draw (vector, boolean, vector-only group, childless shape with stroke/radius/gradient, ellipse, star) | `Image` with a server-rendered sprite, sized to `absoluteRenderBounds`, `Sliced` when the sprite has a border | `FigmaDataUtils.GetNodeSubstitutionStatus`, `NeedsShapeRender`, `FigmaAssetGenerator.BuildFigmaNode` |
| Rendered sublayer that an instance restyles | Its own render, keyed by the instance-side id, assigned in that instance | `FigmaDataUtils.AddRestyledSublayerRenders`, `ComponentManager.ApplyInstanceRender` |
| All other (RECTANGLE, TEXT, GROUP, FRAME with children, etc.) | GameObject with UGUI components under the parent; no separate prefab | `FigmaNodeManager.CreateUnityComponentsForNode` / `ApplyUnityComponentPropertiesForNode` |

## Screen name table

`FigmaPaths.GetPathForScreenPrefab` resolves the output path for a screen FRAME:

1. If `ScreenNameOverrides` contains a row where `FrameName` matches `node.name`:
   `ExcludeFromImport` skips it; a blank `PrefabName` keeps the frame name;
   otherwise the prefab is saved as `<ScreenPrefabFolder>/<PrefabName>.prefab`.
2. If no match and `OnlyImportListedScreens` is **true**, returns `null` and the
   screen is skipped entirely.
3. If no match and `OnlyImportListedScreens` is **false**, falls through to the
   raw frame name.

Duplicate-name collisions append `_N`.

## Component prefab filenames

`FigmaPaths.GetPathForComponentPrefab`:

- **Standalone component:** `<ComponentPrefabFolder>/<MakeValidFileName(name)>.prefab`
- **Variant (child of COMPONENT_SET):**
  `<ComponentPrefabFolder>/<MakeValidFileName(setName)>/<NormalisedVariant>.prefab`

`NormaliseVariantName` turns Figma's `State=Normal, Color=Green` into
`State-Normal_Color-Green`, then `MakeValidFileName` replaces characters in
`Path.GetInvalidFileNameChars()` plus `.` with `_`.

## Axis intent

For each COMPONENT_SET, `ComponentAxisIntent.WriteAxisIntent` writes
`axis-intent.json` in the set's folder. It reads the `UNITY:` directive from the
set's Figma description (`figmaFile.componentSets[parentNode.id].description`).
Format:

```
UNITY: runtime-axis=State; design-axis=Color
```

Axes not classified by the directive appear in `Variants`. Sets with no
`UNITY:` directive produce empty `RuntimeAxes` and `DesignAxes`; all axes go to
`Variants`. No runtime code reads this file yet.

## Server render slicing

When `SliceServerRenders` is on, `ServerRenderSlicer.Run` processes every
`Substitution` render on disk after the downloads (online) or the missing-file
report (offline), before the prefabs are built:

1. **Axis band:** on each axis, the longest run of lines identical to its first
   line (premultiplied colour, tolerance 4 levels) that does not touch an image
   edge. The run that holds the centre line wins.
2. **Border:** a band at least half as long as the geometry centre becomes the
   border centre. Otherwise the border comes from `GeometryBorder`: what draws
   outside the layout box plus the widest of corner radius, inside stroke and
   shadow reach on that side (rectangle-like nodes only; other shapes get 0).
3. **Compaction:** a band longer than 2 lines is replaced by its 2-line
   alpha-weighted mean. The PNG is rewritten in place, so a second pass finds
   the same 2-line band and changes nothing.
4. **Importer:** `spriteBorder`, `spritePixelsPerUnit` = 100 × render scale,
   `FullRect` mesh when the border is not zero.

Pattern sources and Export renders are not sliced.

## Slice-grid collapse

When `CollapseSliceGrids` is on, `NineSlicePass.Run` iterates component and
screen prefabs after component instantiation. `FigmaNineSlice.Apply` walks each
prefab depth-first:

1. **Detect:** every child matches the `slice_<row>_<col>` regex, has an
   `Image` with a sprite, and all cell sprites share the same texture size.
2. **Border:** measured from parent-local position (`localPosition + rect.xMin`),
   never `anchoredPosition`, in design units. A grid with fewer than 3 columns
   gets no horizontal border; likewise rows. A border wider than the parent is
   dropped.
3. **Density:** the border is multiplied by the source texture's texels per
   design unit (`TexelsPerDesignUnit`, from the file's own width so an import
   size cap does not skew it), and the sprite's pixels per unit is set to
   100 × that density.
4. **Sprite:** uses the existing image fill asset when it has an asset path,
   setting border and pixels per unit on its `TextureImporter`. Falls back to a
   RenderTexture read-back and an MD5-named PNG when the sprite has no asset path.
5. **Replace:** destroys all slice children, ensures the parent has a plain
   `Image`, and sets `Image.Type.Sliced`.

Keep it on for files that use slice grids: CROP image fills draw the whole
image since 0.4.0, so uncollapsed cells each show the whole plate.

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

Non-configurable folders derived from root: `ServerRenderedImages` (only renders
outside the imported pages since 0.6.1; the others sit with the image fills),
`FontMaterialPresets`, `Fonts`.
