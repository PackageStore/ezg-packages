# EZG Curve Master

Editor tooling for advanced `AnimationCurve` manipulation in Unity.

Provides a dedicated curve-editing window, cubic-bezier editing, a curve
preset/library system, and the `EditWithCurveMaster` property attribute for
opening any serialized `AnimationCurve` field in the Curve Master window.

## Package ↔ source folder

Extracted from the game repo `m1`:

```
Assets/_Project/Visual/ArtAsset/Scripts/CurveMaster/   →   com.ezg.curve-master
  Runtime/  (EditWithCurveMasterAttribute)              →   Runtime/
  (everything else — the editor tool)                   →   Editor/
```

## Assemblies

- `AnimationCurveManipulationRuntime` (Runtime) — the `EditWithCurveMaster`
  property attribute. This is the only part referenced from runtime code.
- `AnimationCurveManipulationTool` (Editor) — the curve-editing window, bezier
  editor, preset library, and internal Unity editor bindings. Editor-only.

## Dependencies

None. Pure Unity / BCL + `UnityEditor`.

## Peer requirements

None.

## Notes

- The editor tool ships bundled icons and built-in presets under
  `Editor/Resources/` and loads them via `Resources.Load`. This is
  editor-only tooling, so the load happens in the Editor.
- Uses internal `UnityEditor` reflection bindings (`EditorInternalBindings/`)
  to integrate with the Animation/Curve/ParticleSystem windows; these may
  need maintenance across major Unity Editor versions.
