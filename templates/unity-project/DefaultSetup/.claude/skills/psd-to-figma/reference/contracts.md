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

## Text recipe — manifest first, `layerMap` as override

A text node's expected style and effects come from the **manifest layer it
matched** (`type.style`, `type.effects`), not from a global node-name map. Two
screens may name a node the same and carry different styles; each gates on its
own recipe. `text_styles.json.layerMap` survives only as an override, resolved
in this order for a matched layer on screen `S` with node name `N`:

1. `layerMap["S/N"]` — an explicit per-screen override, if present.
2. `layerMap[N]` — used verbatim when `type.style` is `Unknown` or absent from
   `text_styles.styles` (the pre-manifest behaviour). If `type.style` is a valid
   style but `layerMap[N]` names a *different* one, the map entry is ignored and
   the manifest wins (`RECIPE_OVERRIDE_IGNORED`).
3. `text_styles.styles[type.style]` otherwise.

A style absent from `text_styles.styles` is a data gap: the gate reports
`RECIPE_MISSING`, falls back to `layerMap[N]`, and never crashes.

**Phantom shadow.** A drop shadow the shared style declares but neither the
rendered node (`effects`) nor the PSD layer (`type.effects`, with `DropShadow`
and `OuterGlow` mapped to `DROP_SHADOW`) actually carries is not subtracted from
the ink box. The rendered node is authoritative for render bounds, so a shadow
the manifest under-reports but the node renders is still subtracted — the
manifest only ever removes, never adds, a subtraction. This is the one default
change the manifest recipe makes to `verify_report.md`: rows in that class
shrink against the pre-recipe gate; every other row is byte-identical.

The recipe source per row (`manifest` / `override` / `ignored` / `missing` /
`legacy`) is summarised on stdout; `RECIPE_OVERRIDE_IGNORED`, `RECIPE_MISSING`
and `PIN_UNUSED` (a pinned row whose deviation now clears the bar, so the pin is
stale) go to stderr and, under `--json`, to `verify_report.json`. None of these
enter `verify_report.md`.

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

## Opacity contract

The manifest's `opacity` is the node opacity a builder sets on the Figma node.
Photoshop carries two opacities: layer opacity and fill opacity. Layer opacity
is always in `opacity`. Fill opacity is invisible to the numeric gate, so the
manifest records it when it matters and the exporter records who baked it.

- When a layer's fill opacity is below 100%, the manifest adds `fillOpacity` and
  `layerOpacity` (each 0–1). Layers whose fill opacity is 100% omit both keys and
  are byte-identical to before. The keys appear only on `art` and `text` layers.
- The exporter records `bakedOpacity` per stem in `assets_index.json`:
  `"none"` — `topil()` exported the raw pixels, neither fill nor layer opacity
  baked; `"fill"` — `composite()` baked fill opacity into the pixels (a layer with
  enabled effects, clip layers, or a fill kind), layer opacity still not baked.
- **A builder always sets node opacity = manifest `opacity`.** The manifest has
  already reconciled it with what the PNG baked: for `bakedOpacity: "none"`,
  `opacity` = layerOpacity × fillOpacity; for `bakedOpacity: "fill"`, `opacity` =
  layerOpacity (the PNG already carries the fill opacity).
- The manifest predicts the export path and the exporter takes it through one
  shared predicate (`bakes_fill_opacity`), so they cannot disagree. The exporter
  asserts the manifest's `opacity`/`fillOpacity`/`layerOpacity` match the path it
  took and fails naming the stem if a stale manifest disagrees.

**Text layers.** Fill opacity applies to the glyph fill only. Figma text has no
separate fill opacity, so a builder lowers the fill colour's alpha, not the node
opacity. The manifest's node `opacity` for a text layer stays its layer opacity;
`fillOpacity` is informational for the glyph alpha.

**Group opacity.** A group's opacity is carried on its group entry, not flattened
into its descendants. Nest the descendants inside the group's frame and set that
frame's opacity; the children inherit it. Flattening the product onto each
descendant as well would double-apply for a nesting builder.

## Collision contract

A **collision** is two layers that must not share an identity but do. Four kinds
are caught at the data stage, before any component or verify agent runs:

- **Stem pixel collision** (exporter). Two `art` layers map to one stem and, at
  the same pixel size, render different pixels. The exporter keeps the
  first-exported layer's RGBA digest and compares every same-size sibling to it;
  a mismatch prints `STEM COLLISION <stem>: <screen>/<psdName> <w>x<h> vs
  <screen>/<psdName> <w>x<h>`, finishes the other stems, and exits 3. Layers of a
  *different* size are a stem-size collision instead, not an exporter one.
- **Node-style collision** (manifest). A node name is used with more than one
  text `type.style` across the screens; layers whose style is `Unknown` (no
  `textStyles` mapping) are left out.
- **Stem-size collision** (manifest). A stem is mapped from `art` layers whose
  `w×h` differ. Stems listed under `export.plates` (9-sliced, so several sizes
  are their purpose), `export.skipAssets`, `export.tieBreak` or
  `export.allowShared` are left out.
- **Icons-namespace overlap** (exporter). A stem appears in both
  `assets_index.json` and `icons_index.json`; reported as a `STEM COLLISION`.

The manifest records the two manifest kinds under a top-level `collisions` key
(`{nodeStyles, stemSizes}`), prints them as a summary, and exits 0 by default;
`--strict` exits 3 when either list is non-empty. The exporter exits 3 on a stem
pixel collision or an icons-namespace overlap. Pixel-identical layers that share
a stem (genuine reuse) are never a collision and need no escape.

There are exactly two escapes, both in `psd2figma.json` under `export`:

- **`tieBreak`** (`stem → {w, h}`) names the size to keep when one stem is
  deliberately exported from instances of different sizes.
- **`allowShared`** (`stem → reason`) declares a stem's shared pixels or its
  cross-namespace name to be intentional. The reason string is mandatory — an
  `allowShared` entry with a blank reason is itself an error. `allowShared`
  suppresses only its own stem; every other collision still fails.

## Figma Plugin API traps

Every Plugin API, MCP and PSD-import trap lives once in `reference/figma-traps.md`
(P-1..P-N: instance-child immutability, set-level component properties, handle
invalidation, choke-vs-spread, text clipping, upload placement frames, shared
`currentPage`, duplicate-variant error state, unpublished-file locality, and
more). Read it before writing any `use_figma` build or grouping code.
