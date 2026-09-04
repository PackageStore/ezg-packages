# Plugin API helpers

`scripts/figma_helpers.js` is tested Plugin API code for `use_figma` builds:
paste the whole file ahead of your build script in one payload, then call the
functions. Nothing project-specific lives in it — ids, hashes, borders and
recipes are arguments. Every helper is `async`; run the self-test
(`scripts/figma_helpers_selftest.js`) once against any page id to confirm the
file behaves in the current sandbox before trusting it.

Trap ids below point at `reference/figma-traps.md`.

## `nineSliceFrame(parent, name, hash, w, h, border, {srcW, srcH, opacity})`

Builds a `clipsContent` FRAME (no fill of its own) holding the slice cells for
one plate: 3×3 cells for a border with all sides non-zero, 1×3 or 3×1 when one
axis has both sides zero (that axis collapses to a single full-span STRETCH
band). `border` is `[left, top, right, bottom]` or `{left,top,right,bottom}`.
Rows tile exactly `0..top / top..h-bottom / h-bottom..h` and columns likewise;
each cell is a RECTANGLE with the same image `hash`, `CROP` scaleMode and the
per-cell `imageTransform` from the formula in `nine-slice.md` (normalised to
`srcW×srcH`, defaulting to the plate's native `w×h`). Constraints are
MIN/STRETCH/MAX per band so corners stay fixed and the middle stretches.
Children are named `slice_<row>_<col>` so the extract folds the frame to one
leaf. Throws when `top+bottom >= h` or `left+right >= w` (prevents **P-8**, the
collapsed middle band) and when `getImageByHash(hash)` is null (fail loud rather
than render an empty plate). Returns the frame. See `nine-slice.md` for the
measurement contract that produces `border`.

## `rectHash(parent, name, hash, x, y, w, h, {scaleMode})`

Places a single RECTANGLE with an image fill at `x,y` sized `w×h`. With
`scaleMode: 'CROP'` the fill gets the identity `imageTransform`
`[[1,0,0],[0,1,0]]` — a free non-uniform stretch of the whole image, which is
how a widget or vector component used as a bare plate is placed as flat art
instead of an instance (**P-6**). Throws when `getImageByHash(hash)` is null.
Returns the rectangle.

## `instanceAt(parent, componentId, name, x, y, w, h)`

Creates an instance of the COMPONENT at `componentId`, names it, and resizes it
**at the instance level** only when both `w` and `h` are passed (instance
children are immutable — **P-2** — so per-instance art that must differ in size
belongs in a variant, not an overlay). Pass `w,h` null/undefined to keep the
component's natural size. Throws if `componentId` is a COMPONENT_SET (asks for a
variant id) or not a component, and throws when `w,h` are passed to a component
whose `description` contains `natural-size-only` (a component whose art breaks
on resize is flagged there). The caller sizes STRETCH-margin instances to
`target + 2×margin` and offsets by `−margin` (**P-13**); this helper only sets
the instance box. Returns the instance.

## `textByInk(parent, name, chars, styleId, recipe, inkBox, {align, font})`

Loads the font (once per family/style, cached for the run), creates a TEXT node,
binds `styleId` when given, applies `fill`, stroke and `effects` from `recipe`
(same shape as `text_styles.json` `styles[]`: hex colours converted to 0–1,
`strokeAlign` default OUTSIDE, disabled effects skipped), sets
`textAutoResize = WIDTH_AND_HEIGHT`, and positions the node so
`absoluteRenderBounds` minus the outside stroke equals `inkBox` (**P-12**: the
render bounds include outside strokes and shadows, ink coordinates do not). It
iterates the placement until the residual is ≤0.005px because the first
`absoluteRenderBounds` read lags layout, and returns `{node, dx, dy}` — the
measured residual, ≤0.01px. The font comes from `opts.font` or `recipe.font`
(a `{family, style}` resolved by the caller from `psd2figma.json`'s
`figma.fonts`); it is never hardcoded. Recipe construction is the caller's
responsibility and carries its own traps: a zero-radius offset shadow that
matches the gate's `max(stroke, shadow)` edge model (**P-14**), an OuterGlow
substituted as a zero-offset DROP_SHADOW (**P-11**), and Photoshop choke that
does not map to Figma spread (**P-24**).

## `reparentKeepWorld(node, parent)`

Captures `node.absoluteTransform` before `appendChild`, then restores the node's
world position by setting local `x/y = worldTx − parentTx` — because
`appendChild` keeps the child's local coordinates and would otherwise offset
every reparented leaf by the container's origin (**P-1**). Returns the node.
Assumes the parent is unrotated and unscaled, which every layout container in
this pipeline is.

## `deleteByIds(ids)`

Removes each node whose id is given (skipping ids already gone) and returns the
list of ids not found. Use it to clear the placement frames `upload_assets`
drops on the current page, deleting by recorded id rather than by clearing the
page (**P-4**).

## `readGeometry(node)`

Returns `{id, name, x, y, w, h, renderBounds}` with `renderBounds` (from
`absoluteRenderBounds`) made frame-relative to the node's parent — the same
leaf shape `figma_extract.js` emits. Use it to read a built node back and
compare against the manifest without re-deriving the coordinate conversion.
Returns `renderBounds: null` when the node has no rendered bounds.
