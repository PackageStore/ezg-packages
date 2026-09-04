---
name: psd-to-figma
description: Import a Photoshop screen into the project's Figma design file as editable layers — art as uploaded PNGs, text as real Figma text — then prove it matches the PSD numerically. Use when asked to "import a PSD to Figma", "bring this screen into Figma", "add a new screen to the design file", or whenever a .psd under the project's PSD source directory must become a Figma frame. Also use before editing an already-imported screen, because it defines the node naming, 9-slice, component-reuse and verify contracts that edit must not break.
---

# PSD → Figma import

Import a PSD screen into the project's Figma file as editable layers — art as
uploaded PNGs, text as live `TEXT` — then prove it matches the PSD numerically.
The skill is project-agnostic: every project value is read from
`<data>/psd2figma.json` and the `tables.*` files, never written into the skill.

> Generic structural and visual contracts (no flat screens, `Container_`
> grouping, component reuse, 9-slice, grid style) live in the `figma-hygiene`
> skill; this skill adds the settings contract, the commands, and the numeric tier.

Adding a screen means adding an entry to `screens.json`, not writing a new
generator. `<scripts>` is this skill's `scripts/` dir; `<data>` is the project's
data dir; `<key>` is a screen key from `screens.json`. Run each script as
`python3 <scripts>/<name> --data-dir <data>` (`python3` = the skill's `.venv`);
the project root comes from `paths.projectRoot` in `psd2figma.json`. The first
run writes `<data>/.gitignore`, so generated artifacts never enter the project's git.

## Before anything

1. `.venv/bin/python3` — create with `python3 -m venv .venv && .venv/bin/pip
   install -r <scripts>/requirements.txt` if missing. `psd-tools` is not in the
   system Python.
2. Load the `figma-use` skill before **every** `use_figma` call.
3. A full Figma seat. View-only seats fail every write silently (P-7).
4. Read `reference/figma-traps.md` before writing any `use_figma` build or
   grouping code — every Plugin API, MCP and PSD-import trap this pipeline has
   already paid for (P-1..P-24), one row each: symptom, cause, rule, evidence.

## Project settings

One JSON file — `<data>/psd2figma.json` — holds every project value; the scripts
find it from `--data-dir`, `PSD2FIGMA_DATA_DIR`, or a walk-up. Each stage reads
only the sub-section it owns. Keys the stages read:

| Key | Holds |
|---|---|
| `paths.projectRoot` / `paths.psdDir` | consuming root, then PSD source dir |
| `frame.w` / `frame.h` | the one fixed frame size every screen uses |
| `figma.fileKey` / `figma.pages` | target file; page id → name |
| `figma.fonts.body` / `.condensed` / `condensedIsRealFamily` | the one font, its accepted substitute |
| `figma.gridStyleId` | the file-local layout-grid style every screen frame applies |
| `tables.screens` / `.nodeNames` / `.diffRegions` / `.extract` | the per-project data files, all in `<data>` |
| `styleIdFiles` | list of text-style-id filenames the font SSOT gate merges, in order |
| `icons` / `export` | icon stage; art stage (`plates`, `tieBreak`, `allowShared`, `skipAssets`) |

A value the skill needs that is not there gets added there, never pasted into a
step. The gate reads the expected font from `figma.fonts.body`, falling back to
`paths.preflight`; with neither it exits naming the key to add.

## Pipeline

Each stage is idempotent and reads the stage before it; each links a one-page
brief carrying its inputs, exact commands, acceptance, traps and hand-off.

| Stage | Brief | What it runs |
|---|---|---|
| Data | `reference/briefs/data.md` | `psd_manifest.py [--strict]` → `psd_manifest.json` |
| Art | `reference/briefs/art.md` | `psd_export_pngs.py`, `psd_export_icons.py`, upload, `nine_slice_detect.py` |
| Components | `reference/briefs/components.md` | `figma_helpers.js` via `use_figma`; record with `registry_add.py` |
| Screen build | `reference/briefs/screen-build.md` | build; `figma_extract_gen/save.py`; `verify_figma_vs_psd.py --screen <key>` |
| Screen verify | `reference/briefs/screen-verify.md` | re-extract; gate `--json`; `visual_diff.py --screen <key>` |
| Gate | `reference/briefs/gate.md` | full extract; `verify_figma_vs_psd.py --json`; pins |

Run every local stage as one idempotent command:
`python3 <scripts>/pipeline.py --data-dir <data> run` executes manifest, export,
icons, borders, then the gate in order, skips any stage whose inputs are
unchanged, and stops on a collision (exit 3, the stem named). `status` lists
stale stages without running; `run --stages a,b` limits the set, `--screen <key>`
filters the gate, `--force` reruns regardless. Upload and Figma stages are never
run by the runner — they need the MCP. Runner state lives under
`<data>/.pipeline/` and checkpoints under `<data>/.progress/` (both covered by
`<data>/.gitignore`) — see `reference/checkpoints.md`.

Build **components first, screens second** — a screen is assembled from
instances, never loose art. Record every component, 9-slice application and text
style through `registry_add.py`, which locks and atomically deep-merges into
`component_ids.json`, `nine_slice.json` and the style-id files — parallel builders
write straight into the current files, never per-plan snapshots. See
`reference/component-registry.md`.

## Locked decisions

- **Hybrid PNG-art + real text.** Art uploads as trimmed RGBA PNGs; text is live
  `TEXT` with the PSD's font, size, colour and effects. Never bake text into art.
- **One fixed frame size per project.** A PSD wider than the frame carries a `dx`
  shift in `screens.json`; never resize the frame.
- **Hidden PSD layers and disabled effects are skipped** and logged, never
  silently dropped (P-17).
- **Node names come from `node_names.json`**, and vocabulary is fixed per
  project — one agreed term per concept, never a synonym.
- **One font family per file** (`figma.fonts.body`); the accepted substitute is
  `figma.fonts.condensed` with `condensedIsRealFamily:false`, never ad hoc.
- **The layout grid style is local to the target file** and applies to every
  screen-size frame.
- **Manifest is the single source of truth.** The gate takes each text node's
  style and effects from the manifest layer it matched (`type.style`,
  `type.effects`); `text_styles.json.layerMap` is only an override. A phantom
  drop shadow the style declares but neither node nor PSD carries is not
  subtracted. See `reference/contracts.md` → Text recipe.
- **Collisions fail loud.** A stem reused for different pixels (exporter, exit 3)
  or a node name reused with a different style (`psd_manifest.py --strict`) stops
  the pipeline naming both. Escapes: `export.tieBreak`, `export.allowShared`
  (mandatory reason). See `reference/contracts.md` → Collision contract.
- **Fill opacity is data, not eyeball.** The manifest records `fillOpacity` /
  `layerOpacity` when below 100%; the exporter records `bakedOpacity` per stem.
  A builder always sets node opacity = manifest `opacity`; for a text layer fill
  opacity is the glyph fill alpha, not the node opacity. See
  `reference/contracts.md` → Opacity contract.
- **Build with tested helpers**, never hand-written Plugin API —
  `scripts/figma_helpers.js` (`reference/plugin-helpers.md`), self-tested once
  per session with `figma_helpers_selftest.js`.
- **Font SSOT.** Every TEXT node — including text inside clip frames — must carry
  `fontName` = `figma.fonts.body` and bind a shared text style whose id appears
  in one of the style-id files listed in `styleIdFiles`, merged in that order.
  The gate enforces both; a font/style violation is a bug, never accepted debt.

## Verify

The bar is **0.00 px art, 2.00 px text ink, 0 unmapped**, plus zero font/style
violations. Gate one screen with `verify_figma_vs_psd.py --screen <key>`
(repeatable); the summary, the Failures block and the exit code then cover only
the named screens, and an unknown key exits 2 listing the known keys. A screen
whose `figma_extract_<key>.json` is absent is tolerated — listed under `Missing
extracts:`, its rows skipped, exit 1 — so no scratch `screens.json` is needed.
`--json` also writes `verify_report.json` (markdown unchanged) with per-screen
numbers and a `rows` array — read numbers from it, not the prose. An irreducible
deviation is pinned in `accepted_debt.json` per `node`+`screen` with a 0.5 px
drift guard — a pin, never a widened tolerance. `verify_figma_vs_psd.py
--selftest` checks the recipe resolver. Pin schema, the leaf rule and the full
numeric tier live in `reference/contracts.md`.

## References

- `reference/briefs/` — the six stage briefs above.
- `reference/checkpoints.md` — resume-after-kill convention.
- `reference/figma-traps.md` — P-1..P-24.
- `reference/contracts.md` — extract, verify, recipe, opacity, collision contracts.
- `reference/component-registry.md`, `reference/nine-slice.md`, `reference/plugin-helpers.md`.

## Definition of done

- `verify_figma_vs_psd.py` meets the bar for the new screens (`art max 0.00`, `unmapped 0`, zero font/style violations).
- `visual_diff.py` leaves no unexplained region.
- `component_ids.json` and `nine_slice.json` record every node created.
- Every deviation is fixed or pinned with a reason; no probe frame remains.
