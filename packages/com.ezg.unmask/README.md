# EZG Unmask

`com.ezg.unmask` — reverse-mask (unmask) UI component for Unity uGUI.

`Unmask` cuts a visible hole through a parent UI `Mask`, letting you spotlight a
target `RectTransform` (e.g. a tutorial highlight or focus overlay). It supports
edge smoothing, fit-to-target (optionally every `LateUpdate`), padding/offset, and
an optional `UnmaskRaycastFilter` so pointer input passes through the unmasked area.

Originally `Coffee.UIExtensions.Unmask`. The runtime namespace is kept as
`Coffee.UIExtensions` for drop-in compatibility.

## Package ↔ source mapping

| Package file | Source |
|---|---|
| `Runtime/Unmask.cs` | `Assets/_Project/3rdParty/Unmask/Unmask.cs` |
| `Runtime/UnmaskRaycastFilter.cs` | `Assets/_Project/3rdParty/Unmask/UnmaskRaycastFilter.cs` |

## Dependencies

None (`com.ezg.*`).

## Peer requirements

The consuming project must provide:

- **UnityEngine.UI** (`com.unity.ugui`) — a built-in Unity module, present in every
  project by default. Referenced by the `Ezg.Unmask` assembly for `MaskableGraphic`,
  `StencilMaterial`, and `MaskUtilities`.

## Usage

Add an `Unmask` component to a `Graphic` (e.g. an `Image`) that is a child of a UI
`Mask`. Set `fitTarget` to the RectTransform you want to reveal. Add an
`UnmaskRaycastFilter` and point its `targetUnmask` at the `Unmask` to let clicks pass
through the revealed area.
