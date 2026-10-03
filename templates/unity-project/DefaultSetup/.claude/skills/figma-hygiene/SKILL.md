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

This prose is the human contract; `rules.json` is its machine form. Where they disagree, the table wins.

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
- Icons: a hand-drawn icon that duplicates a library icon (find it with the bridge's `icons_search`) is a reuse finding. Advisory only: report a warning, never block the gate.

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
  imported masters that are pinned debt. Pin those with a `V-5` debt entry
  with code `pinned-geometry`; never resize or re-radius them.
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

One table, `rules.json`, holds every rule. One CLI, `scripts/audit.mjs`, runs
every row. `Blocks` is the row's `blocks` field: `yes` fails the run, `no` is
reported as a warning only.

### Tier 1 — Structure (pre-flight + post-flight)

| # | Contract | Pass condition | Blocks |
|---|---|---|---|
| S-1 | **No flat screens** | Every screen frame has at least one child that is a FRAME (not RECTANGLE/TEXT/INSTANCE at root) | yes |
| S-2 | **No generic frame names** | Zero nodes named `Frame`, `Frame N`, `Group`, or `Group N` anywhere in the screen subtree | yes |
| S-3 | **Container- naming** | Every non-component FRAME with `minChildren` (2) or more children has a name that starts with an allowed prefix (`Container-`, `Btn-`, `Header-`, `Row-`, `Slot-`, `Group-`) or is an exempt name (`Title`, `Top-Bar`, `Bottom`, or one containing `Scroll`). A frame with one child is not a grouping frame. | yes |
| S-4 | **Auto-layout where uniform** | When 2 or more sibling instances of the same component have equal spacing, their parent is an auto-layout frame | no |
| S-5 | **Grid where grid** | When instances form an NxM pattern (N 2 or more, M 2 or more), their parent is a single container | no |
| S-6 | **Component reuse** | Art/structure repeating 3 or more times across screens is a component, not loose nodes (see *Reuse rule* above) | no |
| S-7 | **No clip content** | Zero nodes with `clipsContent = true` in the subtree, except the screen frame itself, scroll lists and `Mask-*` frames (see *Clip content* above) | yes |
| S-8 | **Popup containment** | On a popup screen, the plate instance, its close button, its content containers and any stacked panels share one root-level `Container-*Popup` frame whose left/right edges sit on column edges and whose bottom edge sits above the bottom safe zone, constraints CENTER/CENTER, `clipsContent = false` (see *Popup composition* above) | no |
| S-9 | **No underscores** | Zero names containing `_` in the subtree (nodes, instances, screen frame, and the styles they bind) except `slice_ROW_COL` cells (see *Naming* above) | yes |
| S-10 | **Tidy canvas** | On the page the workflow writes to, no two top-level nodes overlap and every gap between row neighbours and between rows is at most 100 px (see *Canvas layout* above) | yes |

### Tier 2 — Visual integrity (post-flight only)

| # | Contract | Pass condition | Blocks |
|---|---|---|---|
| V-1 | **9-slice usage** | Every button plate and frame background listed in the project's nine-slice registry is built as 9-slice, not image fill | no |
| V-3 | **Text style binding** | Every TEXT node has a non-empty `textStyleId` bound to a shared text style defined by the project | yes |
| V-4 | **Grid style presence** | Screen frame has an applied grid style (see *Layout grid* above) | yes |
| V-5 | **Concentric radius** | Every inner shape in an outer shape's corner has radius `max(0, r_outer − d)` within 1 px (see *Corner radius* above) | no |

Rule parameters live in each row's `params`: S-3 `minChildren`,
`allowedPrefixes`, `exemptNames`; S-7 `allowPattern`; V-5 `exemptMaskedPrefix`.
The project overrides them in `ruleParams` of its `project.json`. The merge is
shallow, one level deep, and `blocks` cannot be overridden.

## Running the checks

The engine speaks to the live file through the EZG Figma Bridge, so the bridge
must be connected: in the EZG Tools MCP tab press "Kết nối tới MCP server"
before running anything.

```
node .claude/skills/figma-hygiene/scripts/audit.mjs --project <project data>/project.json \
  (--screens A,B | --page <name> | --nodes 1:2,3:4) \
  [--rules S-3,V-5] [--fail-on block|any|none] [--json <out>] [--port N]
```

| Use | Flags |
|---|---|
| Pre-flight | `--fail-on block --rules S-1,S-2,S-3,S-4,S-5,S-6,S-7,S-8,S-9,S-10` (the tier 1 rows) |
| Post-flight | `--fail-on block`, all rows |
| Audit | `--fail-on none`, all rows: report only |

`--screens` targets frames by name on the screens page; `--page` targets every
top-level frame on a page; `--nodes` targets node ids. Page-scope rows (S-10)
run once for each page that holds a target. The port comes from `--port`, then
env `EZG_FIGMA_BRIDGE_PORT`, then 39410.

Exit codes:

| Code | Meaning |
|---|---|
| 0 | The policy passed |
| 1 | Findings failed the `--fail-on` policy |
| 2 | Usage, config or debt-file error, including an unknown debt code |
| 3 | The bridge is not reachable, or no file matches |

Every finding has the shape `{rule, nodeId, name, path, detail}`, where `rule`
is the table id such as `S-3`.

## Debt

Known, accepted findings live in `tools/<project data>/debt/<fileKey>.json`
(the directory is `debtDir` in `project.json`). The engine subtracts them
before applying the policy.

An entry is `{rule, nodeId, code, note, issue?}`. `code` must be listed in
`debt-codes.json`:

| Code | Meaning |
|---|---|
| `remote-library` | Node is in a remote library component, so we cannot change it. |
| `art-group` | Imported art group kept as one unit. |
| `mixed-font` | Text with mixed styles cannot bind one text style. |
| `no-registry` | No registry entry exists yet. |
| `pinned-geometry` | Geometry is pinned to its source and must not move. |
| `designer-intent` | The designer confirmed the deviation. |
| `other` | Anything else. It needs a non-empty `issue`. |

An entry with no matching finding is reported as stale and does not fail the
run. An unknown code fails the run (exit 2).

## Failure handling

| Failure | Action |
|---|---|
| Blocking finding | Fix the node the finding names, then run the same command again. |
| S-10 (Tidy canvas) | Run `scripts/tidyCanvas.js` with `MODE: 'tidy'` on the page, then check again. |
| Warning (`blocks: no`) | Reported, never fails. Fix it when the workflow touches that node. |
| Accepted deviation | Add a debt entry with a code. Do not pad the entry's note to hide a fixable defect. |

## Adding a rule

1. Add a row to `rules.json` with `id`, `title`, `scope`, `engine`, `blocks`, `fix` and `params`.
2. For `engine: eval`, add `predicates/<file>.js` and a test `tests/<file>.test.mjs` with the harness in `tests/harness.mjs`.
3. Add the rule to the tier table above. `tests/check_skill_doc.py` fails until the row and its `Blocks` value match.

"New rule = new row" holds only for lint-backed rules and simple params. Any
other rule needs a predicate or a script.

## What stays manual

| Rule | What to check |
|---|---|
| S-8 | That the plate sits at the root of the popup container and the panels read as one popup |
| S-6 | Cross-screen judgement: whether repeated art across screens deserves a master (`figma-components/scripts/auditComponentCoverage.js` lists candidates) |

## Agent integration

When this skill runs as a workflow agent:

1. **Pre-flight** runs the audit command with the tier 1 rows and `--fail-on block`, and returns the exit code with the kept findings.
2. The main workflow proceeds only if pre-flight exits 0.
3. **Post-flight** runs the same command over all rows, then `tidyCanvas.js` with `MODE: 'tidy'` on every page the workflow wrote to.
4. The workflow marks the screen done only if post-flight exits 0.
