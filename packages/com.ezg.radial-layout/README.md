# EZG Radial Layout

A radial UI `LayoutGroup` that arranges child `RectTransform`s evenly around a circular arc. Drop it on a UI container, set the radius and angle range, and children are positioned (and optionally rotated) automatically as they are added, removed, or toggled.

## Component

`RadialLayout_v2` (namespace `UnityEngine.UI.Extensions`, menu **Layout/Extensions/Radial Layout v2**) — derives from `UnityEngine.UI.LayoutGroup`.

### Inspector fields

| Field | Description |
|-------|-------------|
| `fDistance` | Radius — distance of each child from the center. |
| `MinAngle` / `MaxAngle` | Arc range (degrees) over which children are distributed. |
| `StartAngle` | Angle (degrees) of the first child. |
| `OnlyLayoutVisible` | When `true`, inactive children are skipped when computing spacing. |
| `VectorRotation` | When `true`, each child is rotated to face away from the center; otherwise a fixed rotation is applied. |
| `ChildRotation` | Extra rotation (degrees) added to each child. |

## Package ↔ source mapping

| Package path | Origin |
|--------------|--------|
| `Runtime/RadialLayout_v2.cs` | `Assets/_Project/3rdParty/Radial Layout/RadialLayout_v2.cs` |

## Dependencies

None (no `com.ezg.*` registry dependencies).

## Peer requirements

The consuming project must already provide Unity's UI module:

- **Unity UI** (`com.unity.ugui`) — built-in; ships with the editor and is auto-referenced. Provides `LayoutGroup` / `LayoutRebuilder`.

## Unity version

Minimum **Unity 2022.3**.
