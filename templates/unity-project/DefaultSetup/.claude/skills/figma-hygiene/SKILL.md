---
name: figma-hygiene
description: Pre-flight and post-flight contract gate for Figma design files. Checks structure (no flat screens, naming, grouping, a tidy canvas with nodes at most 100 px apart) and visual integrity (9-slice, component reuse, text style binding, layout grid). Enforces layout-grid and reuse design rules. Runs automatically before and after any Figma workflow, blocks on failure. Also invocable manually via /figma-hygiene.
---

# Figma Hygiene Gate

Enforces structural and visual contracts on the target Figma design file.
Runs as a pre+post agent on every workflow that writes to Figma. Blocks the
workflow if any contract fails.

## When this skill fires

| Trigger | What runs |
|---|---|
| **Pre-flight** (before any Figma write workflow) | Structure + naming checks on the TARGET screen |
| **Post-flight** (after Figma write completes) | Full contract gate: structure + visual |
| **Manual** (`/figma-hygiene`) | Full contract gate on a specified screen or all screens |

## Design rules

### Layout grid — the backbone

Every screen is composed on a 6-column grid. It is the backbone of the whole UI system, in Figma and in Unity alike — never optional.

- Design frame is 1080×2400 portrait. 6 columns of 150 px, 24 px gutters, 30 px side margins. Column left edges are at x = 30, 204, 378, 552, 726, 900.
- Safe zone: no interactive element in the bottom 60 px. The top edge has no safe zone.
- Figma: every screen-size frame must have an applied layout grid style. Apply it when you create the frame (this is what `V-4` checks).
- Spanning: full-width panels span 6 columns, cards span 2, item-grid slots span 1.5–2.
- Unity: anchor screens to the same column math. Nudge elements onto the nearest column edge and out of the bottom safe zone.

### Reuse rule — components and instances, never copies

Anything used more than 2 times MUST be a reusable definition with instances: a Figma component (plus variants) in the design file, which the bridge imports as a prefab (see `figma-to-unity/reference/prefab-contract.md`). A Figma INSTANCE becomes a `PrefabUtility.InstantiatePrefab` link in Unity, so a baked copy in Figma is a broken prefab link in Unity. Never place a third duplicated copy of a widget — create the master first, or extend an existing one with a variant, then instance it everywhere and swap the existing copies over. This is what `S-6` checks.

- Screens must maximize instance usage. A raw image rect is acceptable only for one-off art, or where no master can render pixel-identically.
- When an instance's content differs from the master's, use the master's own slot. NEVER hide a master layer and stack a duplicate on top — that defeats the component exactly like disabling a prefab's TMP component and adding a second Text object beside it.
- Text: override `characters` on the instance's own text node (load its font first). If a slot cannot fit real content because the box is too narrow, fix the MASTER's box or alignment once — not the instance.
- Icon or image: override the fill on the instance's icon node when the render size matches. Only a size-mismatched sprite may be an overlaid sibling, because Figma cannot resize instance children — and prefer adding a variant instead.
- Hiding an instance child is only for elements genuinely absent in that usage (an unused badge, for example), never as a step toward overlaying a replacement.
- Figma gotcha: setting `visible=false` on an instance child records a *removed* override — the node vanishes from the instance's tree. `instance.resetOverrides()` restores all slots.
- Compositions nest like prefabs: a widget built from other components (a slot = item frame + corner badge + type icon) becomes its own component containing instances of its parts.

### Clip content — off by default

Every frame, component and instance has **Clip content OFF** (`clipsContent = false`). A clipping frame silently cuts outside strokes and overhanging art (an icon's outer stroke inside an 80×80 slot, a badge that overhangs its card) and hides overflow that should be fixed at the source. When art looks cut, uncheck clip on the parent first — never move the stroke to INSIDE or shrink the art to fit.

Only two kinds of frame may clip, and each must be named for it: a scroll list (`Scroll View`, `Container-*Scroll*`) and a mask frame (`Mask-*`) that holds art to be cut to a shape, such as a pattern or banner inside a popup. Screen-size frames (the device viewport) are the implicit third. This is what `S-7` checks.

A mask frame is the parent of what it masks, like a UGUI `Mask`: Clip content on, the corner radius is the mask shape, no fill (a fill shows as the mask graphic), constraints STRETCH/STRETCH when its parent resizes. Its children carry no radius of their own. The bridge imports it as `RectMask2D` (radius 0) or `Mask` with a generated 9-sliced rounded sprite (radius > 0). Do not use "Use as mask" layers for this: they mask their siblings, and UGUI has no sibling mask.

```
Mask-Pattern      FRAME, Clip content ON, radius 84, no fill
  pattern         RECTANGLE, PATTERN fill, radius 0
```

### Corner radius — concentric

A rounded shape inside a rounded shape follows the outer curve. When an inner
shape sits in an outer shape's corner with the same inset on both edges, its
radius is:

```
r_inner = max(0, r_outer − d)
```

`d` is the inset between the outer box and the inner box. A plate inset 8 px in
a frame with a 20 px radius has a 12 px radius. An equal radius makes the gap
thicker at the corner than along the edges; a zero radius points the inner
corner into the curve.

- The outer shape is the smallest shape drawn below the inner one whose box
  holds it: its parent, or an earlier sibling such as the `Bg` plate instance
  under a button's content.
- The rule applies per corner, and only where the two edge insets match (±1 px)
  and the inset is smaller than the outer radius. Content away from a corner is
  not bound by it.
- Figma clamps a radius to half the short side, so a pill inside a pill follows
  the rule on its own.
- Exempt: image-fill art and 9-slice plates (the radius is in the PNG), children
  of a `Mask-*` frame (they carry radius 0; the mask radius is the shape), and
  imported masters that are pinned debt. Pin those with a `V5` entry in
  `hygiene_allow`; never resize or re-radius them.
- Tokens: Figma variables cannot calculate, so a concentric pair is two literal
  tokens, `radius/<owner>/outer` and `radius/<owner>/inner`. V-5 checks that
  their values agree.

This is what `V-5` checks. V-5 is warn-only for now: it is reported and never
blocks.

### Popup composition — one container holds the popup

A popup is one object. In Figma it is one frame; in Unity it becomes one prefab
root that is shown, hidden and animated as a unit. Popup parts left loose on the
screen root can never be that object.

- **One root-level container per popup.** Everything that belongs to the popup
  — plate, title, close button, content, stacked secondary panels, decoration
  that overhangs the plate — lives inside one `Container-<Feature>Popup` frame
  directly under the screen frame. The screen root keeps only what sits behind
  the popup: backdrop, dim layer, the chrome still visible around it.
- **The container is the footprint, not the plate.** Its bounds enclose every
  child, overhang included. Size it to the grid: left and right edges on column
  edges (normally the full content span between the side margins), bottom
  above the bottom safe zone. The plate inside it also spans whole columns,
  centred in the container. A plate whose width is pinned debt is centred,
  never resized.
- **One sub-container per panel.** The main plate and its content form one
  `Container-<Feature>`; each stacked card or secondary panel is its own child
  of the popup container. Uniform stacks sit in a vertical auto-layout with a
  gap token (S-4). Inside a panel, content groups by region or state
  (`Container-<State>`), and each group holds its own text, readouts and
  button. Content is positioned relative to its panel, never to the screen.
- **Anchoring follows containment.** The popup container is constrained
  CENTER/CENTER to the screen frame. Each panel is constrained centre to the
  popup container. Leaves are constrained to their own panel. An overlay that
  hangs off a corner (close button, badge) is an absolute-positioned child of
  the panel it overhangs, constrained to that corner.
- **Reuse inside.** Plate = popup component instance. Buttons = button
  component instances, resized at the instance root only. Stretchable pills and
  plates = 9-slice frames whose slices carry stretch constraints.
- **Nothing clips.** Overhang is the container's job to enclose, not the
  plate's job to cut (S-7).

This is what `S-8` checks.

### Naming — hyphens, never underscores

Every node, component, screen frame and style name is made of PascalCase
segments joined by a hyphen: `Container-Popup`, `Btn-Buy`, `Card-Offer`,
`Text-Title`, `Icon-Close`. `Card_Offer` is a violation; `Card-Offer` is the
form. The hyphen is the one separator; a space means the layer was never named
(S-2), and an underscore is reserved for machine-read contracts.

The single exemption is the 9-slice cell grid `slice_ROW_COL`
(`slice_0_0` … `slice_2_2`). The bridge and the extractor read that name with a
regex; it is a contract, not a layer name, and it stays as is.

This is what `S-9` checks.

### Canvas layout — tidy rows, 100 px apart

The designer arranges the file by eye, so every page stays compact and tidy.
The top-level nodes of a page — screen frames on `Screens`, masters and sets on
`Components` and `Icons` — sit in rows, and no two neighbours are more than
100 px apart.

- **Rows, left to right.** A row is a band of nodes whose top edges start above
  the band's bottom edge. Nodes in a row are top-aligned.
- **100 px maximum.** The gap between row neighbours and between consecutive
  rows is at most 100 px. No two top-level nodes overlap.
- **A new node goes at the end of the last row**, 100 px right of its rightmost
  node, top-aligned with that row. A rebuilt screen keeps the position of the
  frame it replaces. Never leave a new node at (0,0) on top of another node, or
  far from the rest.
- **Tidy after every write.** The last step of any workflow that adds, removes
  or resizes a top-level node is `scripts/tidyCanvas.js` with `MODE: 'tidy'` on
  that page. Tidy keeps each node's row and order and sets every gap to exactly
  100 px. It moves top-level nodes only, so no screen or set content changes.
- **Sections are blocks.** A `SECTION` moves as one node; its children are not
  tidied.

This is what `S-10` checks.

## Contract tiers

### Tier 1 — Structure (pre-flight + post-flight)

These checks use `get_metadata` only (no pixel comparison).

| # | Contract | Pass condition |
|---|---|---|
| S-1 | **No flat screens** | Every screen frame has ≥1 child that is a FRAME (not RECTANGLE/TEXT/INSTANCE at root) |
| S-2 | **No generic frame names** | Zero nodes named `Frame`, `Frame N`, `Group`, or `Group N` anywhere in the screen subtree |
| S-3 | **Container- naming** | Every grouping frame (non-component FRAME with ≥2 children, not a section like `Top-Bar`/`Bottom`/`Scroll View`) uses `Container-<Content>` or a semantic section name |
| S-4 | **Auto-layout where uniform** | When ≥2 sibling instances of the same component have equal spacing, their parent is an auto-layout frame |
| S-5 | **Grid where grid** | When instances form an NxM pattern (N≥2, M≥2), their parent is a single container |
| S-6 | **Component reuse** | Art/structure repeating ≥3 times across screens is a component, not loose nodes (see *Reuse rule* above) |
| S-7 | **No clip content** | Zero nodes with `clipsContent = true` in the subtree, except the screen frame itself, scroll lists and `Mask-*` frames (see *Clip content* above) |
| S-8 | **Popup containment** | On a popup screen, the plate instance, its close button, its content containers and any stacked panels share one root-level `Container-*` frame whose left/right edges sit on column edges and whose bottom edge sits above the bottom safe zone, constraints CENTER/CENTER, `clipsContent = false` (see *Popup composition* above) |
| S-9 | **No underscores** | Zero names containing `_` in the subtree — nodes, instances, screen frame, and the styles they bind — except `slice_ROW_COL` cells (see *Naming* above) |
| S-10 | **Tidy canvas** | On the page the workflow writes to, no two top-level nodes overlap and every gap between row neighbours and between rows is ≤ 100 px (see *Canvas layout* above) |

### Tier 2 — Visual integrity (post-flight only)

| # | Contract | Pass condition |
|---|---|---|
| V-1 | **9-slice usage** | Every button plate and frame background listed in the project's nine-slice registry is built as 9-slice, not image fill |
| V-3 | **Text style binding** | Every TEXT node has a non-empty `textStyleId` bound to a shared text style defined by the project |
| V-4 | **Grid style presence** | Screen frame has an applied grid style (see *Layout grid* above) |
| V-5 | **Concentric radius** | Every inner shape in an outer shape's corner has radius `max(0, r_outer − d)` ±1 px (see *Corner radius* above). **Warn-only**: reported, never blocks |

## Running the checks

### Automated gate (S-1, S-2, S-3, S-7, S-9, V-3, V-4, V-5)

Extract the screen, then run the hygiene gate:

```
python3 <psd-to-figma scripts>/figma_extract_rest.py --data-dir <data> --keys <key>
python3 <psd-to-figma scripts>/verify_figma_vs_psd.py --data-dir <data> --screen <key> --json --hygiene-strict
```

Pass condition: both commands exit 0. The extract writes
`figma_extract_<key>.json` with a `hygiene` block; the gate reads it and
reports per-rule results in `verify_report.json` under
`screens.<key>.hygiene`. V-5 shows as `V5 warn` in the report line and as
`hygiene.V5` in the JSON; it never changes the exit code.

S-10 is page-level, so it has its own check. Run `scripts/tidyCanvas.js` via
`use_figma` with `PAGE_ID` and `MODE: 'check'`; the pass condition is
`pass: true`. `MODE: 'tidy'` fixes a failure.

Without `FIGMA_TOKEN`, use the Plugin-based extract path: run
`figma_extract.js` (gen, paste, save), then the same gate command.

A screen not in `screens.json` (a component-only check) has no extract key;
add a temporary key with `figma_extract_save.py --allow-unknown` or run the
Plugin walk on the frame id directly.

### Pre-flight (structure only)

Run the extract + gate above, and the S-10 check on the page the workflow
writes to. A failure in any S-rule blocks the workflow from proceeding to Figma
writes.

### Post-flight (full gate)

Run `tidyCanvas.js` with `MODE: 'tidy'` on every page the workflow wrote to,
then the same extract + gate. Post-flight adds the numeric tier (art/text
tolerance, unmapped nodes) to the report. A failure in either tier blocks
the workflow from marking the screen as done.

### What stays manual

| Rule | What to check | Helper |
|---|---|---|
| S-4 | Auto-layout where ≥2 same-component siblings have equal spacing | Visual inspection of the node tree |
| S-5 | Grid container where instances form N×M (N≥2, M≥2) | Visual inspection of the node tree |
| S-6 | Component reuse for art/structure repeating ≥3× | `figma-components/scripts/auditComponentCoverage.js` |
| S-8 | Popup container left/right edges on column edges | Extract reports `rootContainers` and centering; column-edge alignment is visual |
| V-1 | 9-slice usage per the project's registry | `nine_slice.json` in the data dir |

## Failure handling

| Failure tier | Action |
|---|---|
| Tier 1 (Structure) | Fix the violating nodes. The gate names each node and rule. |
| S-10 (Tidy canvas) | Run `tidyCanvas.js` with `MODE: 'tidy'` on the page, then check again. |
| Tier 2 (Visual) | Fix or add to `accepted_debt.json` with reason. |
| Hygiene allow-list | Add `{id, rule, reason}` entries to `accepted_debt.json` under `hygiene_allow`. The gate subtracts allowed ids before checking. |

<!-- evidence: the remote icon_close Vector (a library component) is the
canonical allow-list example: {"id": "<node-id>", "rule": "S2", "reason":
"remote library component, name not ours to change"} -->

## Agent integration

When this skill runs as a workflow agent:

1. **Pre-flight** runs the extract + gate commands, returns `{pass, violations}`
   built from `verify_report.json.screens[key].hygiene`.
2. The main workflow proceeds only if pre-flight passes.
3. **Post-flight** runs the same commands (extract + full gate), returns the
   same shape plus the numeric tier results.
4. The workflow marks the screen done only if post-flight passes.
