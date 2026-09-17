---
name: psd-to-figma
description: Import a Photoshop screen into the project's Figma design file as editable layers — art as uploaded PNGs, text as real Figma text — then prove it matches the PSD numerically. Use when asked to "import a PSD to Figma", "bring this screen into Figma", "add a new screen to the design file", or whenever a .psd under the project's PSD source directory must become a Figma frame. Also use before editing an already-imported screen, because it defines the node naming, 9-slice, component-reuse and verify contracts that edit must not break.
---

# PSD → Figma import

Import a PSD screen into the project's Figma file as editable layers — art as
uploaded PNGs, text as live `TEXT` — then prove it matches the PSD numerically.
The skill is project-agnostic: every project value is read from
`<data>/psd2figma.json` and the `tables.*` files, never written into the skill.

> Structural and visual contracts live in the `figma-hygiene` skill; PSD
> authoring rules in `reference/psd-authoring-contract.md`. This skill adds the
> settings contract, the commands, and the numeric tier.

Adding a screen means adding an entry to `screens.json`, not writing a new
generator. `<scripts>` is this skill's `scripts/` dir; `<data>` is the project's
data dir; `<key>` is a screen key from `screens.json`. Run each script as
`python3 <scripts>/<name> --data-dir <data>` (`python3` = the skill's `.venv`);
the project root comes from `psd2figma.json`; the first run writes `<data>/.gitignore`.

## Before anything

1. `.venv/bin/python3` — create with `python3 -m venv .venv && .venv/bin/pip
   install -r <scripts>/requirements.txt` if missing. `psd-tools` is not in the
   system Python.
2. Load the `figma-use` skill before **every** `use_figma` call.
3. A full Figma seat. View-only seats fail every write silently (P-7).
4. Read `reference/figma-traps.md` before writing any `use_figma` build or
   grouping code — every Plugin API, MCP and PSD-import trap this pipeline has
   already paid for (P-1..P-30), one row each: symptom, cause, rule, evidence.

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
| `reuse.waive` | stems waived from component promotion, with a mandatory reason |

A value the skill needs that is not there gets added there, never pasted into a
step. The gate reads the expected font from `figma.fonts.body`, falling back to
`paths.preflight`; with neither it exits naming the key to add.

## Pipeline

Each stage is idempotent and reads the stage before it; each links a one-page
brief carrying its inputs, exact commands, acceptance, traps and hand-off.

| Stage | Brief | What it runs |
|---|---|---|
| Data | `briefs/data.md` | `psd_manifest.py [--strict]` → `psd_manifest.json` |
| Lint | `briefs/data.md` | `psd_lint.py` |
| Art | `briefs/art.md` | `psd_export_pngs.py`, `psd_export_icons.py`, upload (`scaleMode: "FIT"`), `nine_slice_detect.py` |
| Components | `briefs/components.md` | `figma-build/scripts/figma_helpers.js` via `use_figma`; record with `registry_add.py` |
| Plan | `briefs/screen-build.md` | `build_plan_gen.py --keys <key>` |
| Build | `briefs/screen-build.md` | `figma_build_gen.py --key <key>` (wraps the `figma-build` skill) → `use_figma` → `figma_build_save.py` |
| Extract | `briefs/screen-verify.md` | `figma_extract_rest.py` (`FIGMA_TOKEN`) or `figma_extract_gen/save` |
| Gate | `briefs/gate.md` | `verify_figma_vs_psd.py --json [--hygiene-strict] [--learn-ids]` |

Eight runner stages: `lint`, `manifest`, `export`, `icons`, `borders`, `plan`,
`extract`, `gate`; `extract` needs `FIGMA_TOKEN`; build is the one MCP step the
runner does not automate. `python3 <scripts>/pipeline.py --data-dir <data> run`
executes the eight in order, skips unchanged, stops on a collision (exit 3).
`status` lists stale stages; `run --stages a,b` limits, `--screen <key>` filters
the gate, `--force` reruns. State under `<data>/.pipeline/`, checkpoints under
`<data>/.progress/` — see `reference/checkpoints.md`.

Build **components first, screens second** — a screen is assembled from
instances, never loose art. Record every component, 9-slice and style through
`registry_add.py`; see `reference/component-registry.md`.

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
  `type.effects`); `text_styles.json.layerMap` is only an override. See
  `reference/contracts.md` → Text recipe.
- **Collisions fail loud.** A stem reused for different pixels (exporter, exit 3)
  or a node name with a different style (`--strict`) stops the pipeline.
  Escapes: `export.tieBreak`, `export.allowShared`. See `reference/contracts.md`.
- **Fill opacity is data, not eyeball.** The manifest records `fillOpacity` /
  `layerOpacity`; the exporter records `bakedOpacity` per stem. A builder sets
  node opacity = manifest `opacity`. See `reference/contracts.md`.
- **Build with tested helpers**, never hand-written Plugin API — the
  `figma-build` skill's `figma_helpers.js`, self-tested once per session with
  `figma_build_gen.py --helpers-selftest`.
- **Font SSOT.** Every TEXT node must carry `fontName` = `figma.fonts.body` and
  bind a style from `styleIdFiles`. A font/style violation is a bug, never debt.
- **Generated build, never hand-written.** `build_plan_gen.py` turns the
  manifest into a build plan (`figma-build/reference/build-plan.md`);
  `figma_build_gen.py` hands it to the `figma-build` skill. A hand edit goes
  into the plan tables or the helpers.
- **Reuse from data.** `components_plan.json` decides instance vs rect;
  `unpromoted` clusters block the build until promoted or waived in
  `psd2figma.json.reuse.waive` with a reason.
- **Ids over names.** Run `--learn-ids` once a screen passes; renames never
  break the gate.

## Verify

The bar is **0.00 px art, 2.00 px text ink, 0 unmapped**, plus zero font/style
violations and hygiene S-1/S-2/S-7/S-9 clean (`--hygiene-strict`). Gate one
screen with `verify_figma_vs_psd.py --screen <key>` (repeatable); `--json` writes
`verify_report.json` — read numbers from it, not the prose. An irreducible
deviation is pinned in `accepted_debt.json` per `node`+`screen` with a 0.5 px
drift guard — a pin, never a widened tolerance. `--selftest` checks the recipe
resolver. `PIN_STALE` rows (pins whose deviation now clears the bar) should be
removed from `accepted_debt.json`. When `name_map.json` exists, the gate resolves
migrated names through it so pre-migration manifests still match. Existing covered
screens keep `_` names by decision; new screens use hyphens (D-5). Pin schema,
the leaf rule and the full numeric tier live in `reference/contracts.md`.

## References

- `reference/briefs/` — the stage briefs above.
- `reference/checkpoints.md` — resume-after-kill convention.
- `reference/figma-traps.md` — P-1..P-30.
- `reference/contracts.md` — extract, verify, recipe, opacity, collision contracts.
- `reference/psd-authoring-contract.md` — PSD structuring rules for clean import.
- `reference/component-registry.md`, `reference/nine-slice.md`.
- Plan contract and Plugin API helpers → `figma-build` skill.
- Hygiene contracts → `figma-hygiene` skill.

## Definition of done

- `verify_figma_vs_psd.py` meets the bar (`art max 0.00`, `unmapped 0`, zero font/style violations).
- `visual_diff.py` leaves no unexplained region.
- `component_ids.json` and `nine_slice.json` record every node created.
- Every deviation is fixed or pinned with a reason; no probe frame remains.
