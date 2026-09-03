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

## Contract tiers

### Tier 1 — Structure (pre-flight + post-flight)

These checks use `get_metadata` only (no pixel comparison).

| # | Contract | Pass condition |
|---|---|---|
| S-1 | **No flat screens** | Every screen frame has ≥1 child that is a FRAME (not RECTANGLE/TEXT/INSTANCE at root) |
| S-2 | **No generic frame names** | Zero nodes named `Frame`, `Frame N`, `Group`, or `Group N` anywhere in the screen subtree |
| S-3 | **Container_ naming** | Every grouping frame (non-component FRAME with ≥2 children, not a section like `Top_Bar`/`Bottom`/`Scroll View`) uses `Container_<Content>` or a semantic section name |
| S-4 | **Auto-layout where uniform** | When ≥2 sibling instances of the same component have equal spacing, their parent is an auto-layout frame |
| S-5 | **Grid where grid** | When instances form an NxM pattern (N≥2, M≥2), their parent is a single container |
| S-6 | **Component reuse** | Art/structure repeating ≥3 times across screens is a component, not loose nodes (see *Reuse rule* above) |

### Tier 2 — Visual integrity (post-flight only)

| # | Contract | Pass condition |
|---|---|---|
| V-1 | **9-slice usage** | Every button plate and frame background listed in the project's nine-slice registry is built as 9-slice, not image fill |
| V-3 | **Text style binding** | Every TEXT node has a non-empty `textStyleId` bound to a shared text style defined by the project |
| V-4 | **Grid style presence** | Screen frame has an applied grid style (see *Layout grid* above) |

## Running the checks

### Pre-flight (structure only)

The pre-flight agent runs `get_metadata` on the target screen and checks
S-1 through S-6 by inspecting the node tree. No Figma writes happen until
all S-checks pass or the user explicitly overrides.

### Post-flight (full gate)

The post-flight agent runs both tiers:

1. **Structure** — `get_metadata` inspection (S-1 through S-6).
2. **Visual** — a visual extract of the target screen checked against V-1, V-3, V-4.

The project's own verify/diff tooling supplies the numeric pass separately
when applicable. A failure in either tier blocks the workflow from marking
the screen as done.

## Failure handling

| Failure tier | Action |
|---|---|
| Tier 1 (Structure) | Agent reports which contracts failed with node IDs. Workflow must fix before proceeding. |
| Tier 2 (Visual) | Agent reports violations with node IDs and expected values. Fix or add to `accepted_debt.json` with reason. |

## Agent integration

When this skill runs as a workflow agent:

1. **Pre-flight agent** receives the screen name and runs Tier 1 checks.
   Returns `{pass: true}` or `{pass: false, violations: [...]}`.
2. The main workflow proceeds only if pre-flight passes.
3. **Post-flight agent** receives the screen name and runs both tiers.
   Returns `{pass: true, report: {...}}` or `{pass: false, violations: [...]}`.
4. The workflow marks the screen done only if post-flight passes.
