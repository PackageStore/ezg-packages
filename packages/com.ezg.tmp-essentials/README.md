# EZG TMP Essentials (`com.ezg.tmp-essentials`)

Unity **TextMesh Pro Essential Resources** packaged as a UPM package so projects can
pull the shared TMP resource set from the registry instead of re-importing
*Window → TextMeshPro → Import TMP Essential Resources* into every project.

This is **asset-only** — there is no C# code or assembly in this package.

## Contents

Mirrors the standard `Assets/TextMesh Pro/` essential-resources folder, with the
**original `.meta` GUIDs preserved verbatim** (so existing scenes/prefabs/materials
that reference these assets keep resolving):

| Folder | What |
|--------|------|
| `Resources/` | `TMP Settings.asset` (loaded via `Resources.Load("TMP Settings")`), default `LiberationSans SDF` font asset + Drop-Shadow/Outline materials, `EmojiOne` sprite asset, `Default Style Sheet`, line-breaking character tables |
| `Shaders/` | TMP SDF / Bitmap / Sprite shaders, mobile + masking + overlay/SSD variants, URP & HDRP shader graphs, and the `.cginc` / `.hlsl` includes |
| `Fonts/` | `LiberationSans.ttf` source font |
| `Sprites/` | `EmojiOne.png` atlas + `EmojiOne.json` |
| `Documentation/` | TextMesh Pro user guide (PDF) |

## Package ↔ source folder

Extracted from the game repo at:

```
Assets/_Project/Visual/ArtAsset/Shared/3rdParty/TextMesh Pro
```

## Dependencies

- **`package.json` dependencies:** none.

## Peer requirements (the consuming project must already provide these)

- **`com.unity.ugui` ≥ 2.0.0** — in Unity 6 the TextMeshPro runtime ships inside the
  Unity UI (uGUI) package. `TMP Settings.asset` and the font assets reference
  TextMeshPro scripts (e.g. `TMP_Settings`, script GUID
  `2705215ac5b84b70bacc50632be6e391`) that come from this package. Without it the
  assets will show as missing-script.

> Note: this package targets **Unity 6000.2+**. The shader set and URP/HDRP shader
> graphs come from Unity 6.2's TextMeshPro; UPM hides the package in older editors.

## ⚠️ Duplicate-resource conflict

These assets carry the **same GUIDs** Unity writes into `Assets/TextMesh Pro/` when you
import TMP Essential Resources normally. A project that consumes this package **and**
still has an in-`Assets/` copy of TMP Essentials will hit duplicate-GUID conflicts.
When switching a project to consume this package, remove its local
`Assets/.../TextMesh Pro` copy in the same change.

## Licensing / attribution

This package redistributes Unity's TextMesh Pro essential resources; `author` in
`package.json` (EZG Studio) is the packager, not the original author.

- **TextMesh Pro resources** — © Unity Technologies, under the **Unity Companion License**.
- **Liberation Sans** (`Fonts/LiberationSans.ttf`) — **SIL Open Font License 1.1**
  (© Google, © Red Hat). See `Fonts/LiberationSans - OFL.txt`.
- **EmojiOne** sprites (`Sprites/EmojiOne.*`) — provided by EmojiOne under their own
  licensing terms. See `Sprites/EmojiOne Attribution.txt`.
