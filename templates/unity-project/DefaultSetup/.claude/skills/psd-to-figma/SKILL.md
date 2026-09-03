---
name: psd-to-figma
description: Import a Photoshop screen into the project's Figma design file as editable layers — art as uploaded PNGs, text as real Figma text — then prove it matches the PSD numerically. Use when asked to "import a PSD to Figma", "bring this screen into Figma", "add a new screen to the design file", or whenever a .psd under the project's PSD source directory must become a Figma frame. Also use before editing an already-imported screen, because it defines the node naming, 9-slice, component-reuse and verify contracts that edit must not break.
---

# PSD → Figma import

Every project value — target file key, fonts, frame size, grid style, page ids,
source paths — is read from the **settings file**, never written into this
skill. See [Project settings](#project-settings).

> Generic structural and visual contracts (no flat screens, naming,
> `Container_` grouping, auto-layout, component reuse, 9-slice, grid-style
> presence) live in the `figma-hygiene` skill. This skill supplies the settings
> contract, the concrete verify commands, and the numeric-accuracy tier that
> `figma-hygiene` does not carry.

**These contracts already exist. Extend them; do not restate or replace them.**
Adding a screen means adding an entry to `screens.json` in `<data>`, not
writing a new manifest generator.

## Project settings

One JSON file — `<data>/psd2figma.json` — holds every project-specific value.
The scripts find it on their own: the `--data-dir` flag names its directory, or
the `PSD2FIGMA_DATA_DIR` env var, or a walk-up from the current directory. Read
it before stage 1.

Schema:

| Key | Holds |
|---|---|
| `paths.projectRoot` | consuming project root, relative to the data dir |
| `paths.psdDir` | PSD source directory, relative to the project root |
| `paths.preflight` | optional legacy file the gate reads `fonts.body` from when `figma.fonts.body` is absent |
| `frame.w` / `frame.h` | the one fixed frame size every screen uses |
| `figma.fileKey` | target Figma file, read by every `use_figma` call |
| `figma.pages` | page id → name, used by build and extract |
| `figma.fonts.body` | `{family, style}` — the one font every TEXT node must carry |
| `figma.fonts.condensed` | the accepted substitute when the PSD's condensed family is absent |
| `figma.condensedIsRealFamily` | `false` when the substitute above is in force |
| `figma.gridStyleId` | the file-local layout-grid style id every screen frame applies |
| `tables.screens` | filename of the per-screen geometry table (default `screens.json`) |
| `tables.nodeNames` | filename of the node-name / vocabulary table (default `node_names.json`) |
| `tables.diffRegions` | filename of the visual-diff region table (default `diff_regions.json`) |
| `tables.extract` | filename of the extract config (default `extract_config.json`) |
| `styleIdFiles` | list of text-style-id sidecar filenames the font SSOT gate merges, in order |
| `icons` | icon stage: `subdir`, `skip`, `variantOverrides`, `setPrefixes` |
| `export` | art stage: `skipAssets`, `tieBreak`, `plates` |

Every `tables.*` entry and every `styleIdFiles` name is a file **in `<data>`**,
never in this skill. Each stage reads the sub-section it owns — `export` for the art exporter,
`icons` for the icon exporter — and no other.

A value this skill needs that is not yet in `psd2figma.json` gets **added
there**, not pasted into a step. Per-screen geometry (PSD path, source size,
`dx`/`dy` shift) lives in `screens.json` (the file named by `tables.screens`);
the frame size and the layout-grid style id are settings values too — read them
from `psd2figma.json`, or add them if the file predates this contract. The verify gate reads the expected font from `figma.fonts.body`, falling back to
`paths.preflight` only when that key is absent; with neither present it fails
with a message naming the key to add, and never assumes a font.

A new project creates its own `<data>/psd2figma.json` with this schema, points
`paths` at its own root and PSD directory, and fills the `tables` files with its
own screens and node names. Nothing project-specific is ever written into the
skill.

Placeholders below: `<scripts>` is this skill's `scripts/` directory, which
ships with the skill; `<data>` is the project's data directory, which holds all
generated output and project JSON. `<screen>` is a screen key from
`screens.json`. Every script runs as `python3 <scripts>/<name> --data-dir <data>`.

## Scripts here, data there

The pipeline scripts ship inside this skill, under `<scripts>`. Every generated
file and every project table — `psd2figma.json`, the `tables.*` JSON, `assets/`,
`diff/`, the manifests and the reports — lives in the project's data directory,
`<data>`. `--data-dir` is the only thing that joins the two.

The skill directory must never accumulate generated output. No manifest, no
`assets/`, no `diff/`, no report is ever written under `scripts/`. If a stage
would write into the skill, its `--data-dir` is wrong.

## Before anything

1. `.venv/bin/python3` — create with `python3 -m venv .venv && .venv/bin/pip install -r <scripts>/requirements.txt` if missing. `psd-tools` is not in the system Python.
2. Load the `figma-use` skill before **every** `use_figma` call.
3. A full seat on the Figma team. View-only seats fail every write silently.

## Pipeline

Each stage is idempotent and reads the stage before it. Run every script as
`python3 <scripts>/<name> --data-dir <data>`; each `Script` below lives in
`<scripts>`, and every `Produces` path is under `<data>`.

| # | Stage | Script | Produces |
|---|---|---|---|
| 1 | Parse PSDs | `psd_manifest.py` | `psd_manifest.json` — the single source of truth |
| 2 | Export art PNGs | `psd_export_pngs.py` | `assets/`, `assets_index.json` |
| 3 | Export icon PNGs | `psd_export_icons.py` | `icons/`, `icons_index.json` |
| 4 | Upload to Figma | `figma_upload.py` / `upload_assets` | `image_hashes.json` |
| 5 | Detect 9-slice borders | `nine_slice_detect.py` | `nine_slice.json` |
| 6 | Build in Figma | `use_figma` scripts | components, then screens |
| 7 | Extract what was built | `figma_extract.js` via `use_figma` | `figma_extract_<screen>.json` |
| 8 | Numeric gate | `verify_figma_vs_psd.py` | `verify_report.md` |
| 9 | Visual gate | `visual_diff.py` | region deltas as JSON on stdout, `diff/*.png` |

Stage 6 is the only manual one. Build **components first, screens second** — a
screen is assembled from instances, never from loose art.

## Locked decisions

- **Hybrid PNG-art + real text.** Art layers upload as trimmed RGBA PNGs. Text
  is live Figma `TEXT` with the PSD's font, size, colour and effects. Never
  bake text into art.
- **One fixed frame size per project.** Every screen frame is exactly the
  project's frame size. A PSD wider than the frame carries a `dx` shift in its
  `screens.json` entry; never resize the frame to fit the PSD.
- **Hidden PSD layers are skipped**, and so are effects with `enabled: false`.
  psd-tools reports both. Log what was skipped; do not silently drop it.
- **Node names come from `node_names.json`** in `<data>`, not from raw PSD
  layer names. Add new mappings there.
- **Vocabulary is fixed per project.** Each concept has one agreed term used in
  both node names and copy, recorded in `node_names.json`. Never substitute a
  synonym, however natural it reads.
- **One font family per file**, `fonts.body` in settings. When the PSD names a
  family variant Figma does not carry, the accepted substitute is recorded as
  `fonts.condensed` with `condensedIsRealFamily: false` — never chosen ad hoc
  at build time.
- **The layout grid style is local to the target file.** It cannot be imported
  from a library. Every screen-size frame must have it applied.
- **Font SSOT rule.** Every TEXT node must carry `fontName` = `fonts.body` AND
  be bound to a shared text style whose id appears in one of the sidecar files
  listed in `styleIdFiles`, merged in that order. This includes text inside clip frames,
  which `figma_extract.js` returns in a separate `clippedText` array and the
  gate checks alongside normal leaves. The gate
  (`verify_figma_vs_psd.py`) enforces both; a violation is a bug, never
  accepted debt — font/style violations cannot be pinned via
  `accepted_debt.json`.

## Reuse before you build

Check `<data>/component_ids.json` first. It maps every built component and
variant to its node id. If the PSD art matches an existing component, instance
it — do not re-upload the PNG.

A **size difference is not a reason to build a new component**: every plate is
9-sliced, so the same component renders correctly at any size. One plate node
routinely serves several sizes.

Art that repeats three or more times across screens gets promoted to a
component (or a variant set) before any screen uses it.

See `reference/component-registry.md`.

## Grouping — no flat screens

A screen where every element is a direct child of the screen frame is a flat
screen. **Flat screens are forbidden.** Every screen must have at least one
level of structural grouping. The grouping step runs AFTER art and text are
placed and verified, BEFORE the screen is considered done.

### Naming convention

| Purpose | Name pattern | Example |
|---|---|---|
| UI zone (top bar, bottom bar, scroll area) | Descriptive name | `Top_Bar`, `Bottom`, `Scroll_View` |
| Layout/structural container | `Container_<Content>` | `Container_Stats` |
| Existing component names | Keep as-is | as recorded in `component_ids.json` |

Never leave a frame named `Frame`, `Frame N`, or `Group N`. Every frame must
have a semantic name.

### When to group

| Condition | Action |
|---|---|
| 2+ elements form a logical UI section | Wrap in a named frame (`Top_Bar`, `Bottom`) |
| 2+ instances of the same component with uniform spacing | Wrap in auto-layout frame with gap |
| Instances form an NxM grid | Wrap in grid frame |
| A visual unit has bg + icon + text (composite widget) | Wrap in positioned frame |
| A background rect + content text belong together | Wrap in frame |

### Reference and counter-example screens

Do not carry a canonical screen list in this skill — it goes stale. Instead,
before restructuring a screen, read a screen in the same file that already
passes the structure contract and copy its grouping shape; its
`figma_extract_<screen>.json` shows that shape verbatim.

`verify_figma_vs_psd.py --data-dir <data>` reports every screen against the
contract in one pass. Screens it fails are counter-examples, never templates.

## 9-slice

Every button plate and frame is a 9-slice, never a stretched image fill.
Figma has no native 9-slice and third-party plugins cannot be driven over
MCP — build it directly. See `reference/nine-slice.md` for the geometry, the
`imageTransform` formula and the border table.

## Verify contract

The gate is **0.00 px for art, 2.00 px for text ink**, plus zero unmapped
nodes. `verify_figma_vs_psd.py` compares `psd_manifest.json` against
`figma_extract_<screen>.json`.

Text is compared on its **ink** box, art on its **layout** box. A text node's
`absoluteRenderBounds` includes stroke and shadow; the PSD manifest is
glyph-ink only, so the script subtracts the outside stroke weight.

A deviation that cannot be fixed goes in `accepted_debt.json` as a pinned
exception — pinned at its measured value, with a 0.5 px drift guard. **An
exception is a pin, not an amnesty.** Never widen a tolerance to pass.

See `reference/contracts.md` for the extract leaf rule, the exception
mechanism, and the full numeric-accuracy tier (N-1 through N-4).

## Verify commands

Order: extract → verify → visual diff.

Both gates take no per-screen flag. Each one processes **every** screen in
`screens.json` on each run, so read the report for your screen rather than
filtering on the command line.

```bash
# Numeric gate — all screens; exits non-zero if any screen fails
python3 <scripts>/verify_figma_vs_psd.py --data-dir <data>

# Visual gate — all screens; region deltas on stdout, diff PNGs into <data>/diff/
python3 <scripts>/visual_diff.py --data-dir <data>
```

Extract step: run the `use_figma` extract script for the target screen before
verify. The verify script reads `figma_extract_<screen>.json`; it never talks
to Figma directly. `visual_diff.py` needs `<data>/diff/figma_<screen>.png` and
`<data>/diff/psd_<screen>.png` to already exist for every screen in the region
table; it compares images, it does not render them.

## Definition of done

- `verify_figma_vs_psd.py` exits 0, `art max: 0.00px`, `unmapped: 0`.
- `visual_diff.py` produces no unexplained region.
- `component_ids.json` and `nine_slice.json` record every node created.
- Every new deviation is either fixed or written into `accepted_debt.json`
  with a reason.
- No probe, test or placement frames left on any page.
