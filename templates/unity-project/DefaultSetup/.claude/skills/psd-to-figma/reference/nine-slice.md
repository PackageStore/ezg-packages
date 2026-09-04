# 9-slice in Figma

Figma has **no native 9-slice**. `scaleMode` is only `FILL | FIT | CROP | TILE`.
Community plugins exist but cannot be launched over MCP — the Figma MCP bridge
runs inside its own plugin sandbox, and Figma runs one plugin at a time. Build
the structure with `nineSliceFrame` (see `plugin-helpers.md`); this file is the
measurement contract that produces its `border` argument and the formula it
applies.

## Structure

`nineSliceFrame(parent, name, hash, w, h, border, opts)` builds it: a
`clipsContent` FRAME with no fill of its own, holding the slice cells (nine when
every border side is non-zero, or a single full-span band on an axis whose two
sides are both zero). Every cell is a rectangle carrying the **same image hash**
with a different `imageTransform`; constraints are MIN/STRETCH/MAX per band so
corners stay fixed and the middle stretches, and children are named
`slice_<row>_<col>` so the extract folds the frame to one leaf. Do not upload
one PNG per cell — that is what the plugin does, and it adds nine assets per
plate for no benefit. The helper throws when a border exceeds its axis (P-8).

## imageTransform

Verified empirically against the Plugin API:

```js
fills = [{
  type: 'IMAGE', imageHash, scaleMode: 'CROP',
  imageTransform: [[w, 0, x], [0, h, y]]   // normalized source region
}]
```

`x, y, w, h` are the cell's region in the source image, divided by the image's
pixel size. Identity `[[1,0,0],[0,1,0]]` with `CROP` gives a free non-uniform
stretch of the whole image — that is the single-cell case.

## Borders

`nine_slice_detect.py` measures them: the longest run of consecutive identical
columns (and rows) is the stretchable band, and everything outside it is fixed.
An axis with no uniform middle band is **not sliceable** — emit one full-span
cell for that axis instead of three.

Applied borders live in `nine_slice.json` under each asset's `applied` key, and
double as Unity `Sprite.border` values.

Art fails an axis for a predictable reason: a gradient or hand-painted shading
running the full length of that axis leaves no band uniform enough to stretch.
Such art is either sliced on the other axis only, or left unsliced. Record which,
and why, in `nine_slice.json` — a missing band is a measurement, not an
oversight to re-litigate later.

Re-running the detector refreshes only the measured fields (`size`, `border`,
`band`, `tol`, `sliceable`) and preserves every hand-added key on an entry
(`applied`, `axisNote`, `notes`, …), so those notes survive re-detection; a stem
dropped from `export.plates` is kept and marked `"stale": true` rather than
deleted.

## Rules

- **Whole pixels only.** Fractional cells put corners off-pixel and blur them.
- **Corners render 1:1.** The node's native size normally equals the image size.
- Prefer the pixel-exact (tolerance 0) border. Widen the tolerance only when
  tolerance 0 gives a degenerate band — for example when a gloss gradient makes
  the only exact pair so wide that the stretch band is off-centre — and record
  the chosen border and the reason in `nine_slice.json`.
- A 9-slice container is **one leaf** to the extract and the verify gate. Name
  its children `slice_<row>_<col>` so `figma_extract.js` can recognise and skip
  them.
