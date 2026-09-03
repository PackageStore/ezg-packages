---
name: figma-components
description: Build and grow the component layer of the project's Figma file — component sets, variant axes, State ladders, component properties, promotion of repeated screen art into masters, and instance adoption. Use when asked to "add a component", "add states to a button", "make this reusable", "the Components page is thin", "extract this card into a component", "add a variant", "swap the copies for instances", or when a screen holds art that repeats. Does NOT create variables or bind tokens (use figma-tokens), does NOT import screens from PSD (use psd-to-figma), does NOT import to Unity (use figma-to-unity), and does NOT run the structure or 9-slice gate (use figma-hygiene).
---

# Components

Forked from two upstream sources and cut to this file:

- [`natdexterra/work-with-design-systems`](https://github.com/natdexterra/work-with-design-systems)
  `references/build/` — `component-spec.md`, `slots-guide.md`, `patterns-guide.md`.
- The Figma plugin skill `figma-generate-library` (v2.2.95) —
  `component-creation.md`, `discovery-phase.md`, `error-recovery.md` and the
  `scripts/` helpers.

Both upstreams target a themeable web design system that exports CSS and ships a
React component API. This project is a single-theme portrait game UI on one
fixed frame that exports Unity UGUI prefabs. What was changed and why is in
`PROVENANCE.md`; read it before re-importing anything from either upstream.

## Project settings

Every project-specific value this skill names — the target file key, the body
font, the frame size, the grid style and geometry, and the three page ids — is
a project value, not a skill value. It lives in the project's Figma settings,
the same `psd2figma.json` that `psd-to-figma` reads (`figma.fileKey`,
`figma.fonts.body`, `frame.w`/`frame.h`, `figma.gridStyleId`, `figma.pages`),
and never in this skill. Placeholders used below:

| Placeholder | Means |
|---|---|
| `<fileKey>` | the target Figma file, `figma.fileKey` in `psd2figma.json` |
| `<body-font>` | the project's one font `{family, style}`, `figma.fonts.body` |
| the frame size | the one fixed screen size, `frame.w` × `frame.h` |
| the grid style | the file-local layout-grid style, `figma.gridStyleId` |
| Screens / Components / Icons | the three page names (a fixed convention); their ids are in `page_ids.json` |

The scripts here take a `PAGE_ID` and node ids as inputs — they never hardcode a
file key or a page id. Component-set names, variant values and token names in
the examples below stand for whatever the project's own file uses; substitute
them.

## Prerequisites

1. **Invoke the `figma-use` skill before every `use_figma` call.** It carries the
   Plugin API rules and script templates. Never call `use_figma` without it.
2. `figma-hygiene` runs as the pre/post gate on any write. `S-6` (component
   reuse) is the rule this skill exists to satisfy; `S-2`/`S-3` govern the node
   names inside every master you build.
3. `figma-tokens` owns variables, scopes, text styles and all naming. This skill
   **binds** to tokens that already exist; it never mints one. If a component
   needs a colour, radius or spacing value that has no token, stop and hand the
   token job to `figma-tokens` first.
4. Read `reference/component-spec.md` before building any set, and
   `reference/component-creation.md` before writing the script.

## Ownership boundary

| Job | Skill |
|---|---|
| Create a variable, set scopes, mint a text style | `figma-tokens` |
| Bind an existing token to a component property | **this skill**, using `figma-tokens` names |
| Node naming, `Container_` prefix, generic-name violations | `figma-hygiene` (S-2, S-3) |
| Variant property names and the `State` ladder | `figma-tokens/reference/naming-conventions.md` |
| 9-slice construction and the numeric verify pass | `psd-to-figma` |
| Screen structure, auto-layout, grid style presence | `figma-hygiene` |
| Component set, variant axes, component properties, promotion | **this skill** |

## The grid contract

Every screen is the project frame size on the file-local grid style
(`figma.gridStyleId`): a fixed number of equal columns with a fixed gutter and
side margin, plus two row bands marking the top and bottom safe zones. Read the
concrete geometry from the grid style; never hardcode it here. The measures and
their tokens:

| Measure | Token |
|---|---|
| Column width | `space/column` |
| Gutter | `space/gutter` |
| Side margin | `space/margin` |
| Row pitch | `space/row-pitch` |

**Column spans.** A run of `n` columns is `span(n) = n × column + (n − 1) × gutter`.
Compute the span table once from the grid style and size new masters to it. Two
existing masters already sit exactly on a whole span — use whichever masters in
the file already snap to the grid as your sizing reference.

**A new master snaps to `span(n)`. An existing one does not get retro-fitted.**
An imported master whose width is already off the column grid is pinned debt:
the `psd-to-figma` art tolerance is 0.00 px (`N-1`), so resizing it to the
nearest span moves art and fails the verify pass on every screen that instances
it. Snap new work; leave imported work alone.

**Safe zones bind the master, not just the screen.** A component placed in a
screen's top or bottom safe zone must be designed to sit outside it — a bottom
navigation bar, for instance, must end above the bottom safe-zone line, not
overlap it.

**Minimum touch target is ≈44 pt.** Convert that to a px floor at the project's
design width (`frame.w`) and hold every tappable master to it. Anything smaller
needs a transparent hit frame around it, and the reason recorded in the
description.

## Critical rules

1. **Bind, never paste.** Every fill, stroke, radius and auto-layout gap in a
   master binds to a `Semantic` variable from `figma-tokens`. A raw value in a
   master is a defect. Primitives are for the Semantic tier to alias, not for a
   component to bind directly.
2. **Never detach a component.** Vary content with a variant, a boolean, an
   instance swap, or the master's own slot node. This holds even when the slot
   API would be the cleaner answer — see `reference/slots-guide.md`.
3. **Bind and edit inside the master, never on an instance.** A child of an
   `INSTANCE` cannot be resized or repositioned: `resize()` is silently ignored
   and `x`/`y` assignment throws. Only fills, `characters`, `name`, `visible`
   and variant properties override.
4. **`State` is `Normal` / `Pressed` / `Disabled` / `Active`.** No `Hover`, no
   `Focused`, no `Loading`, no `Error`. Touch game: no pointer, no keyboard
   focus ring, no form validation. Upstream's six-state web ladder does not
   apply and must not be re-imported.
5. **Component properties are set-level with a single default, not one per
   variant.** After `combineAsVariants`, per-variant `BOOLEAN` and
   `INSTANCE_SWAP` definitions merge into one with one `defaultValue`. When the
   value must follow the variant, give each variant its own nested instance and
   set `isExposedInstance = true`.
6. **Every variant name is unique within its set.** A duplicate puts the set
   into an error state, after which reading `componentPropertyDefinitions`
   throws `Component set has existing errors`.
7. **Cap the matrix at 30 combinations.** Past that, split by the primary axis,
   move a visual axis to `INSTANCE_SWAP`, or extract a `Building Blocks/`
   sub-component. See `reference/component-creation.md` §4.
8. **One `INSTANCE_SWAP`, never a variant per icon.** An icon set with many
   variants, folded into a parent as an axis, multiplies that set by its own
   variant count.
9. **Generic UI is real geometry; special assets stay PNG.** A component built
   from an image fill has no bindable surface. Genericize the chrome first —
   this is Phase 2b of `figma-tokens`, and it gates every colour binding in a
   master. The genericised button plates are the reference construction.
10. **Never touch font identity or `textStyleId`.** Every TEXT node stays the
    project body font (`<body-font>`) with a style id from `style_ids.json`.
    Load the node's *current* font via `getStyledTextSegments(['fontName'])`
    before any text write.
11. **New masters live on the `Components` page; icons on `Icons`.** Do not
    adopt upstream's one-page-per-component structure — it would add a page per
    component and break `figma-to-unity`, which reads three named pages.
12. **Work one component set per `use_figma` call, verify, then continue.**
    Never build on unverified work. Never run two `use_figma` calls in parallel
    against the same page.
13. **Match verification depth to the change.** Variant, property, binding and
    rename changes are deterministic — read them back inside the same script
    and return the result. Only new geometry needs `get_metadata` +
    `get_screenshot`. `get_screenshot` is the most rate-limited call available;
    run `whoami` if unsure of the tier.
14. **Promotion changes screens.** Replacing loose nodes with instances is a
    write to the Screens page and must pass the `psd-to-figma` verify contract
    (art tolerance 0.00 px, text ink 2 px) plus the `figma-hygiene` post-flight.
15. **Never renumber or re-case an existing component.** `component_ids.json`,
    `style_ids.json` and the Unity prefab names all key off the current names.

## Workflow

### Phase 0 — Discovery (read-only)

Run `scripts/auditComponentCoverage.js`. It returns, per screen, the node count,
the top-level instance count and the free (non-instance) node count, plus every
repeated free node grouped by name stem and size.

Read `reference/discovery-phase.md` for how to turn that into a plan. Present
the findings and **stop** — do not write until the scope is agreed.

The audit is the live source; the file is edited by hand between sessions, so
every count is a snapshot, never a fact to quote. Record one row per screen:

| Column | Content |
|---|---|
| Screen | screen frame name |
| Nodes | total node count |
| Instances | top-level instance count |
| Free nodes | non-instance nodes not inside an instance |
| Grid style | whether the grid style is applied |

The screen with the **lowest** free-node count is the standard to aim at; the
**highest** is the outlier to attack first.

### Phase 1 — Triage the candidates

For every repeated free node the audit returns, choose one:

- **promote** — it repeats 3+ times, or twice across two screens, and the
  repeats are structurally the same. Becomes a master.
- **variant** — a master already covers it and the difference is one axis. Add
  a variant to the existing set; do not create a second set.
- **leave** — genuinely single-use. A screen's one-off title, timer and reward
  containers are the clear cases.

`figma-hygiene` S-6 sets the floor: anything used more than twice **must** be a
component. Above that floor the call is judgement, and it is worth asking.

### Phase 2 — Build the master

Read `reference/component-spec.md` for the anatomy, then
`reference/component-creation.md` for the script. Order:

1. Build the base as an auto-layout frame with every visual property bound.
2. Clone per variant combination; change only the bindings that differ.
3. `figma.combineAsVariants`, then **position the variants** — they stack at
   (0,0) and stay there until you lay them out.
4. Add `TEXT` / `BOOLEAN` / `INSTANCE_SWAP` properties on the set.
5. Write the description using
   `figma-tokens/reference/component-description-template.md`.

### Phase 3 — States

The largest single gap in this file. Interactive sets that carry only a `Type`
or `Color` axis need a `State` axis added.

Use `scripts/addStateVariants.js`. It adds a `State` axis to an existing set
without touching the existing axis, and applies the state recipe from
`reference/component-spec.md`. No new art is needed: `Pressed` is a downward
offset plus a darkened rim, `Disabled` is a desaturated fill at reduced opacity.

### Phase 4 — Adopt

Replace the loose copies on the screens with instances of the new master, using
`scripts/promoteToComponent.js`. Override `characters` and fills on the
instance's own nodes. Never hide a master layer and stack a duplicate on top.

Then run the `psd-to-figma` verify pass on every screen touched.

### Phase 5 — Verify

- `scripts/validateComponent.js` per set: variant count, axis values, property
  definitions, unbound visual properties, generic child names.
- `figma-tokens/scripts/audit-tokens.js` per set: the unbound count must drop.
- `psd-to-figma` verify pass on every screen whose masters changed.
- `figma-hygiene` post-flight gate.

## Known live findings

The file drifts between sessions, so this skill carries no frozen list of live
defects. Run Phase 0 and record the recurring classes below as you find them:

| Finding class | How it shows | Action |
|---|---|---|
| Screens with no grid style | audit's grid-style flag is `no` | `figma-hygiene` V-4 violation; apply the project grid style |
| A generically named component (`Component 1`, `Frame N`) | audit's set list or `looseTopLevel` | Hygiene S-2. Name it or delete it |
| A lowercase or placeholder variant axis value | audit's `variants` per set | Axis values are PascalCase and name a real role; rename |
| Loose frames on the `Components` page | audit's `looseTopLevel` | Promote or delete |
| A registry entry not on the page | cross-check `component_ids.json` against the audit | `psd-to-figma/reference/component-registry.md` is stale; refresh it |
| Slot API unavailable | `figma.createSlot` does not exist in this environment | Use the boolean + instance-swap fallback in `reference/slots-guide.md` |

## Related skills

- `figma-tokens` — variables, scopes, text styles, naming, component
  descriptions. Owns everything this skill binds to.
- `figma-hygiene` — the structure and visual gate. S-6 is this skill's mandate.
- `psd-to-figma` — screen import, the component registry, the 9-slice registry,
  and the numeric verify contract that promotion must not break.
- `figma-to-unity` — the downstream import. Component names become prefab names.
