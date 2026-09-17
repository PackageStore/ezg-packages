---
name: figma-hygiene
description: Pre-flight and post-flight contract gate for Figma design files. Checks structure (no flat screens, naming, grouping) and visual integrity (9-slice, component reuse, text style binding, layout grid). Enforces layout-grid and reuse design rules. Runs automatically before and after any Figma workflow, blocks on failure. Also invocable manually via /figma-hygiene.
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
- Safe zones: no interactive element in the top 100 px or the bottom 60 px.
- Figma: every screen-size frame must have an applied layout grid style. Apply it when you create the frame (this is what `V-4` checks).
- Spanning: full-width panels span 6 columns, cards span 2, item-grid slots span 1.5–2.
- Unity: anchor screens to the same column math. Nudge elements onto the nearest column edge and out of the safe zones.

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

Only two kinds of frame may clip, and each must be named for it: a scroll list (`Scroll View`, `Container-*Scroll*`) and banner/pattern art that must be masked inside a popup. Screen-size frames (the device viewport) are the implicit third. This is what `S-7` checks.

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
  edges (normally the full content span between the side margins), top and
  bottom inside the safe zones. The plate inside it also spans whole columns,
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
| S-7 | **No clip content** | Zero nodes with `clipsContent = true` in the subtree, except the screen frame itself, scroll lists and popup-masked banner art (see *Clip content* above) |
| S-8 | **Popup containment** | On a popup screen, the plate instance, its close button, its content containers and any stacked panels share one root-level `Container-*` frame whose left/right edges sit on column edges inside the safe zones, constraints CENTER/CENTER, `clipsContent = false` (see *Popup composition* above) |
| S-9 | **No underscores** | Zero names containing `_` in the subtree — nodes, instances, screen frame, and the styles they bind — except `slice_ROW_COL` cells (see *Naming* above) |

### Tier 2 — Visual integrity (post-flight only)

| # | Contract | Pass condition |
|---|---|---|
| V-1 | **9-slice usage** | Every button plate and frame background listed in the project's nine-slice registry is built as 9-slice, not image fill |
| V-3 | **Text style binding** | Every TEXT node has a non-empty `textStyleId` bound to a shared text style defined by the project |
| V-4 | **Grid style presence** | Screen frame has an applied grid style (see *Layout grid* above) |

## Running the checks

### Automated gate (S-1, S-2, S-3, S-7, S-9, V-3, V-4)

Extract the screen, then run the hygiene gate:

```
python3 <psd-to-figma scripts>/figma_extract_rest.py --data-dir <data> --keys <key>
python3 <psd-to-figma scripts>/verify_figma_vs_psd.py --data-dir <data> --screen <key> --json --hygiene-strict
```

Pass condition: both commands exit 0. The extract writes
`figma_extract_<key>.json` with a `hygiene` block; the gate reads it and
reports per-rule results in `verify_report.json` under
`screens.<key>.hygiene`.

Without `FIGMA_TOKEN`, use the Plugin-based extract path: run
`figma_extract.js` (gen, paste, save), then the same gate command.

A screen not in `screens.json` (a component-only check) has no extract key;
add a temporary key with `figma_extract_save.py --allow-unknown` or run the
Plugin walk on the frame id directly.

### Pre-flight (structure only)

Run the extract + gate above. A failure in any S-rule blocks the workflow
from proceeding to Figma writes.

### Post-flight (full gate)

Run the same extract + gate. Post-flight adds the numeric tier (art/text
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
