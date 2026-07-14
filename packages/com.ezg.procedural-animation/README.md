# EZG Procedural Animation

Editor tool that procedurally generates **in-between AnimationClips** from captured pose ScriptableObjects, driven by a pose-combination graph and per-bone "feel" easing presets.

Open via **Tools ▸ Ezg ▸ Procedural Animation ▸ Inbetween Generator**.

## Concept

- **PoseAsset** — a captured set of bone transforms (a single pose), captured from a scene rig.
- **PoseCombinationGraphAsset** — a graph defining valid pose-to-pose connections and the paths through them.
- **FeelPresetAsset** — an easing/"feel" curve applied to the interpolation, with per-bone timing rules. A built-in `DefaultFeelPresetFactory` generates a standard DOTween-style ease library (easeInOutQuad … easeOutBack, etc.) plus named feels.

The generator walks the graph, interpolates between connected poses using the assigned feel presets and timing, and bakes the result to standard Unity `AnimationClip`s.

## Features

- Graph-based authoring window with per-connection settings and live preview.
- **Pose capture** from scene rigs, with a capture filter and a pose renamer.
- **Feel presets** panel; generate the default ease library with one click.
- **Batch AnimationClip export** to a configurable output folder.
- Persists the working graph and settings between sessions.

## Assemblies

| Assembly | Platforms | Contents |
|---|---|---|
| `Ezg.ProceduralAnimation` | All | Runtime ScriptableObject data types (`FeelPresetAsset`, `PoseAsset`, `PoseCombinationGraphAsset`, generation settings) — usable in player builds |
| `Ezg.ProceduralAnimation.Editor` | Editor | The authoring window, panels, API, and pose-capture / clip-writing utilities |

## Configuration

Output and preset locations default to `Assets/ProceduralAnimation/…` and are overridable:

- `InbetweenGenerationSettings.outputFolder` / `PoseCombinationGenerationOptions.outputFolder` — where generated clips are written.
- `DefaultFeelPresetFactory.DefaultPresetFolder` — where the default feel presets are created.

Create the data assets via **Assets ▸ Create ▸ Ezg ▸ Procedural Animation ▸ {Feel Preset, Pose Asset, Pose Combination Graph}**.

## Requirements

- Unity 2022.3+
- Unity built-ins only (`UnityEditor.Animations`); no third-party libraries. The authoring tools are editor-only; the data types are runtime-safe.
