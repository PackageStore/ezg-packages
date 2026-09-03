# Discovery

Forked from the Figma plugin skill `figma-generate-library` (v2.2.95)
`references/discovery-phase.md`. Upstream's Phase 0 reads design tokens out of a
web codebase — CSS custom properties, Tailwind config, DTCG JSON, CSS-in-JS
theme objects, iOS and Android token files — and reconciles them against Figma.

**None of that applies.** This project has no CSS, no Tailwind and no token
file. The Figma file is the source of truth for the design system, and Unity
consumes it through the UnityFigmaBridge. So discovery here reads Figma and the
project's own registries, not the codebase.

## 1. What to read instead of a codebase

| Source | What it tells you |
|---|---|
| `psd-to-figma/reference/component-registry.md` | What every existing set is for, and what art is not yet a component |
| `psd-to-figma/reference/nine-slice.md` | Which plates are 9-sliced and with what borders |
| `component_ids.json` (in the project data dir) | The machine-readable set id map |
| `image_hashes.json` (in the project data dir) | Whether art is already uploaded |
| `figma-tokens/reference/token-taxonomy.md` | Which values already have a token |
| the project PSD source directory | The design intent for screens not yet imported |

The registry and every id map are read from the project's data directory (the
`--data-dir` that `psd-to-figma` documents), never from this skill.

**The registry drifts.** It can list a component id that is no longer on the
Components page. Always confirm against the live file before trusting an id.

## 2. Inspect the file

Run `scripts/auditComponentCoverage.js`. It is read-only and returns:

- every component set on `Components` and `Icons`, with variant count, axes,
  component properties and description length
- per screen: total nodes, top-level instances, free non-instance nodes
- repeated free nodes grouped by name stem and size, split into cross-screen and
  same-screen repeats
- loose top-level frames on the `Components` page

The audit is the live source. Report its counts from the run, never from a
frozen table — the Components page can gain sets inside a single working
session. **Always re-measure.** A quoted baseline is a starting point for
reading the shape of the file, never a fact to report.

### Read the numbers this way

**Free-node count is the coverage signal.** A screen with many free nodes is
carrying structure that should be a master. Rank the screens by free-node count:

| Free nodes | Reading |
|---|---|
| lowest | The standard the other screens should reach |
| mid | Fine — mostly grouped structure and genuine one-offs |
| highest | The outlier — attack it first |

**Variant depth is the other signal.** Divide total variants by set count; a
ratio near 1 means most sets have a single axis. Interactive sets carrying no
`State` at all are the gap. That ratio, not the set count, is what makes a
component page thin.

## 3. Do not call `search_design_system` expecting a hit

Upstream requires `get_libraries` then `search_design_system` before creating
anything. **This file does not publish as a library and subscribes to none.**
Styles and components here are local; another file cannot reuse them without
copying.

Run `get_libraries` once if you want the confirmation, then build locally.
Record "no libraries; file is local" in the gap analysis and move on. Do not
import a community UI kit — the art is hand-painted and the Unity importer reads
this file's own nodes.

## 4. Build the plan

Produce three tables before writing anything.

**Promotion table** — one row per candidate:

| Candidate | Repeats | Screens | Decision | Target set |
|---|---|---|---|---|

**State table** — one row per interactive set:

| Set | Existing axis | States to add | New variant count |
|---|---|---|---|

**Token gap table** — every value a new master needs that has no token:

| Value | Where | Token needed | Owner |
|---|---|---|---|

The third table is a handoff to `figma-tokens`, not work for this skill. If it
is non-empty, the token job runs first.

## 5. Conflict resolution

| Conflict | Who wins | Why |
|---|---|---|
| Registry says a set exists, the file says it does not | **The file** | Update the registry in the same pass |
| The PSD and the Figma screen disagree on art | **The PSD** | It is the verified source; `psd-to-figma` tolerance is 0.00 px |
| A master's size is off the column grid | **The master** | Resizing breaks the verify pass. Pin it as debt |
| A new master's size is off the grid | **The grid** | Snap it to `span(n)` before building |
| Upstream convention disagrees with the file's names | **The file** | Renaming breaks `component_ids.json`, `style_ids.json` and the prefab names |
| Two sets could cover the same art | **Ask** | Merging sets is cheap now and expensive after screens instance both |

## 6. Stop and report

Present the three tables and stop. Do not start writing until the scope is
agreed. Upstream's rule holds here: creating a page, a component, a variable or
a style all count as mutation, and none of them is harmless setup.
