# Build plan contract

A build plan is one JSON object that describes one frame. `figma_build_gen.py`
injects it as `PLAN` into `figma_build.js`; the script creates the frame and
every op in order. The plan is the whole interface: the builder reads no
project settings, and any producer (a PSD parser, a code exporter, a hand-written
file) that writes this shape gets the same frame.

## Top level

| Field | Type | Meaning |
|---|---|---|
| `key` | string | Producer's id for this frame; echoed in the result. |
| `pageName` | string | Page to build on. Must exist; the script throws otherwise. |
| `frameName` | string | Name of the new top-level frame. |
| `frame` | `{x, y, w, h}` | Size is required. `x`/`y` may be `null` (Figma default). |
| `replace` | bool | `true` deletes an existing frame of the same name first. Otherwise a name clash throws. |
| `gridStyleId` | string or null | Layout grid style bound to the frame. |
| `font` | `{family, style}` or absent | Default font for `text` ops without `recipe.font`. |
| `ops` | array | Ordered op list, parents before children. |
| `warnings` | array | Producer warnings `{code, ...}`. `NO_STYLE` and `NO_HASH` block the build unless listed in `allowWarnings`. |
| `allowWarnings` | array of codes | Blocking codes to let through. |

The frame is created with `clipsContent: true` and no fill.

## Ops

Every op has `op`, `id` (unique in the plan), `name`, `parent` (an earlier op
id, or `null` for the frame), `x`, `y` relative to the parent, `w`, `h`.
Optional on every op: `opacity` (0–1, applied when ≠ 1) and `layerKey`, an
opaque producer string returned in `result.layerIds`.

| `op` | Extra fields | Builds |
|---|---|---|
| `container` | `clip` (bool) | Empty FRAME, no fill. `clip` sets `clipsContent`. |
| `rect` | `hash` | RECTANGLE with an IMAGE fill (`FILL` scale mode). Throws when the hash is unknown to the file. |
| `nineSlice` | `hash`, `border` `[l,t,r,b]` or `{left,top,right,bottom}`, `srcW`, `srcH` | Clipping FRAME of `slice_<row>_<col>` cells. Throws when a border pair ≥ its axis. |
| `instance` | `componentId` | Instance of that COMPONENT (never a COMPONENT_SET), resized to `w×h` unless the component description contains `natural-size-only`. |
| `text` | `chars`, `styleId`, `recipe`, `ink` `{x, y, w, h}` | TEXT node positioned so its ink box lands on `ink` (absolute frame coordinates). Empty `chars` skips the op with an `EMPTY_TEXT` warning. |

`recipe` is `{fontSize, fill, strokeWeight, strokeAlign, strokes[{color}],
effects[], font}`; colours are hex strings, every field optional. `styleId`
may be `null` when a recipe carries the whole look.

## Result

`figma_build.js` returns

```json
{
  "key": "...",
  "frameId": "12:34",
  "replacedId": null,
  "ops": { "<op id>": "<node id>" },
  "layerIds": { "<layerKey>": "<node id>" },
  "textDeltas": { "<op id>": { "dx": 0, "dy": 0 } },
  "warnings": ["EMPTY_TEXT Name", "RENDER_BOUNDS_FALLBACK Name"]
}
```

A throw anywhere rolls back every write of the payload; fix the plan and run
again.

## Minimal example

```json
{
  "key": "demo",
  "pageName": "Screens",
  "frameName": "Demo",
  "frame": { "x": 0, "y": 0, "w": 1080, "h": 2400 },
  "replace": true,
  "gridStyleId": null,
  "font": { "family": "Inter", "style": "Bold" },
  "ops": [
    { "op": "container", "id": "o1", "name": "Container-Header", "parent": null,
      "x": 0, "y": 0, "w": 1080, "h": 200 },
    { "op": "text", "id": "o2", "name": "Text-Title", "parent": "o1",
      "x": 40, "y": 60, "w": 400, "h": 80, "chars": "Hello",
      "styleId": null, "recipe": { "fontSize": 64, "fill": "#FFFFFF" },
      "ink": { "x": 40, "y": 60, "w": 400, "h": 80 } }
  ],
  "warnings": []
}
```
