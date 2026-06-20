# EZG UI Particle System

Renders Unity's built-in **Particle Systems inside a uGUI Canvas** with correct UI
sorting, soft-particle **depth masking**, **mask culling**, RectTransform
**size-following**, and an optional **distortion** shader. URP-compatible.

Extracted from the game source folder `Assets/_Project/3rdParty/UIParticleSystem`
(commercial asset "UI Particle System" by ABcat).

## Package ↔ source mapping

| Package path | Source |
|---|---|
| `Runtime/Scripts/` | `Assets/_Project/3rdParty/UIParticleSystem/Scripts/` |
| `Runtime/Shaders/` | `Assets/_Project/3rdParty/UIParticleSystem/Shaders/` |
| `Samples~/Example/` | `Assets/_Project/3rdParty/UIParticleSystem/Example/` (demo scene) |
| `Documentation~/` | vendor PDF, URP upgrade readme, vendor changelog |

## Assembly

- `Ezg.UIParticleSystem` (Runtime) — references `UnityEngine.UI`.

Editor-only behavior lives inside `#if UNITY_EDITOR` guards within the runtime
scripts; there is no separate editor assembly.

## Dependencies

None on the scoped registry.

## Peer requirements

The consuming project must already provide:

- **`com.unity.ugui`** (UnityEngine.UI) — referenced by the runtime assembly.

## Key components

- `UIParticleCanvas` — drives the per-canvas depth mask render.
- `UIParticleDepthObject` / `UIParticleDepthObjectInfo` — particle renderer that
  participates in UI sorting and depth masking.
- `UIParticleRectTransSizeFollower` — keeps a particle system sized to a RectTransform.

## URP

See `Documentation~/UniversalRP(URP)_UpgradeReadme.txt` for the render-pipeline
upgrade steps shipped by the original vendor.

## License

This package wraps a third-party commercial Unity Asset Store asset. Use is subject
to the original asset's Asset Store EULA; distribute only within projects/teams
licensed to use it.
