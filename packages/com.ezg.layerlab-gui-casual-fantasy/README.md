# Layer Lab GUI Casual Fantasy

`com.ezg.layerlab-gui-casual-fantasy`

Layer Lab's **Casual Fantasy** UI art pack, packaged for UPM. A pure art/asset
package — there are **no C# scripts** to compile.

## Contents

| Folder | What's inside |
|---|---|
| `Prefabs/` | 213 ready-to-use UGUI prefabs — buttons, labels, frames, popups, sliders, misc UI components, and demo-scene panels |
| `ResourcesData/Sprites/` | 710 component & demo sprites (icons, frames, buttons, sliders, backgrounds, characters) |
| `ResourcesData/Fonts/` | TextMeshPro font assets — `LINESeedSans` and `Tilt Warp` (+ outline variants) |
| `Scene/` | `DemoScene_CasualFantasy.unity` — showcase scene |
| `Preview/` | Preview / reference images |
| `+README+/` | Original Layer Lab user asset guide |

## Peer requirements

This package references these libraries by GUID but does **not** declare them as
UPM dependencies — the consuming project must already provide them:

- **TextMeshPro** — required by the UI prefabs and the bundled `TMP_FontAsset`s.
- **Unity UI (UGUI)** — engine module, normally already present.

## Fonts & licensing

Fonts ship under the SIL Open Font License (OFL). See:

- `Tilt Warp` — <https://fonts.google.com/specimen/Tilt+Warp/license>
- `LINESeedSans` — <https://scripts.sil.org/OFL>

Review and comply with each font's license before shipping.

## Source

Extracted from `Assets/Layer Lab/GUI-CasualFantasy` in the game project. All asset
GUIDs are preserved, so existing references resolve unchanged.
