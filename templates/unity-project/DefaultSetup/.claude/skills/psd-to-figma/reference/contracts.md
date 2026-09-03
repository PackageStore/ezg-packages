# Contracts

## Extract contract

`figma_extract.js` runs through `use_figma` and its returned object is written
verbatim to `figma_extract_<screen>.json`. `verify_figma_vs_psd.py` reads those
files; it never talks to Figma.

**Leaf rule.** `RECTANGLE` and `TEXT` are always emitted. A node is a leaf when
it is a 9-slice container — a `FRAME`, `INSTANCE` or `COMPONENT` whose children
are all named `slice_*` — or when its name appears in the script's clip-leaf
list, the clipping boxes standing in for Photoshop paragraph boxes. A `FRAME`
with a visible fill is emitted and still descended into. Everything else is
descended into without being emitted.

**Clipped-text rule.** When the walk stops at a leaf, any TEXT nodes inside that
leaf's subtree are collected into a separate `clippedText` array (carrying `id`,
`name`, `fontName`, `fontSize`, `textStyleId`, and `parentLeaf`). These entries
do NOT enter the geometry comparison and do NOT count as leaves — they exist
solely so the font-identity and style-binding checks can see every TEXT node,
including those hidden behind clip frames. A font or style violation on a
clipped text node fails the gate identically to any other violation.

**Coordinates are relative to the screen frame.** Text nodes additionally carry
`inkX/inkY/inkW/inkH` from `absoluteRenderBounds`, plus `strokeWeight`,
`strokeAlign`, `hasVisibleStroke`, `effects`, `fontName`, `fontSize` and
`textStyleId`. Mixed-value properties emit the string `"MIXED"`.

**Repeated rows.** Only the first instance of a repeating row is extracted; the
manifest defines one row and the pitch is verified separately. `OTHER_ROWS` in
the script lists the ids to skip.

## Verify contract — numeric-accuracy tier

| # | Contract | Pass condition |
|---|---|---|
| N-1 | Art tolerance | `art max: 0.00 px` over non-exception nodes (layout box) |
| N-2 | Text tolerance | `text max: ≤2.00 px` (ink box) |
| N-3 | Unmapped nodes | `unmapped: 0` — every manifest layer has a Figma match and vice versa |
| N-4 | Exception drift | Pinned exceptions have not drifted >0.5 px from their recorded delta |

`unmapped` counts both manifest layers with no Figma match and Figma
`RECTANGLE`/`TEXT`/`FRAME`/`INSTANCE` nodes with no manifest match. A non-zero
count fails the gate — that is what stops stray nodes from accumulating.

Failure handling: run `verify_figma_vs_psd.py`; fix deviations or pin with a
drift guard. **Never widen a tolerance.** Deviations accepted permanently go in
the pipeline directory's `accepted_debt.json` with a reason.

## The exception mechanism is a pin, not an amnesty

`accepted_debt.json` holds `verification_exceptions`. An exception does not wave
a node through:

- `ink_centre` still enforces the 2 px bar, on the ink **centre** rather than
  the corner.
- `position_only` still enforces the normal bar on `dx`/`dy`.
- Both assert the recorded delta has not drifted more than **0.5 px**.

So a pinned node that moves still fails. Never raise `ART_TOL` or `TEXT_TOL` to
make something pass; pin it with a reason instead, or fix it.

Read the exceptions section of `verify_report.md`, not just the summary line —
the headline `art max` is measured over non-exception nodes only.

## Font identity and text-style binding

Every TEXT node — including text inside clip frames — must satisfy two conditions
or the gate fails:

1. `fontName` equals `figma.fonts.body` from the settings file. The expected font
   is always sourced from settings, never hardcoded in the gate or in a build
   script — with no font in settings the gate exits with a message naming the
   key to add, rather than assuming one.
2. `textStyleId` must be non-empty and match one of the ids in the sidecar files
   named by `styleIdFiles` in the settings file, merged in that order.

These checks run over both the normal `nodes` list and the `clippedText` list
from each extract file. They are reported as separate categories ("Font
violations", "Text Style Binding") and both make the gate exit non-zero.
**Font/style violations are bugs, not debt** — they cannot be pinned via
`accepted_debt.json`. This prevents a screen passing geometry verification while
silently carrying a wrong font — the failure mode where a fallback family is
substituted at build time and nothing downstream notices — even when that text
lives inside a geometry leaf like a clip frame.

## Layout grid style

The target file's grid style is local to that file. It cannot be imported from a
library. Read its id from the settings file and apply it to every screen-size
frame; never re-create the style per screen.

## Figma Plugin API traps

These cost real debugging time and apply to all Figma scripting.

1. **A child of an INSTANCE cannot be resized or repositioned.** `resize()` is
   ignored, `x`/`y` assignment throws. Only fills, characters, name, visible and
   variant properties override. The instance itself resizes fine. When
   per-instance art differs in size or position, the answer is a variant, not an
   overlay. Inside a master, everything is editable.
2. **Component properties are set-level, with one default — not one per
   variant.** After `combineAsVariants`, per-variant `BOOLEAN` / `INSTANCE_SWAP`
   properties merge into a single definition with a single `defaultValue`. When
   the value must follow the variant, give each variant its own nested instance
   and set `isExposedInstance = true` instead of using `INSTANCE_SWAP`.
3. **`node.remove()` invalidates the handle immediately.** Read every field you
   need *before* removing.
4. **Photoshop "choke" (%) and Figma "spread" (px) do not map.** Converting
   naively inflates text render bounds and can push a label out of view.
5. **Figma text does not clip like a Photoshop paragraph box.** Reproduce the
   clipping with a wrapper frame at `clipsContent: true`.
6. **`upload_assets` drops a placement frame per asset** on the current page.
   Delete them, or they litter the canvas.
7. **`figma.currentPage` resets between calls**, and `setCurrentPageAsync` may
   be called at most once per script. Fan multi-page work out into parallel
   calls, one page each.
8. **Duplicate variant names put a set into an error state.** Reading
   `componentPropertyDefinitions` then throws "Component set has existing
   errors". Give every variant a unique `Type=` value.
9. **An unpublished file's styles and components are local.** Unless the target
   file is published as a library, another file cannot reuse anything created
   there without copying it. Confirm the file's publish state before planning to
   share a component across files.
