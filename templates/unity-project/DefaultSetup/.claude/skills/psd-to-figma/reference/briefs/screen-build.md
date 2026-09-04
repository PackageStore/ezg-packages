# Brief — Screen build (place art + text, extract, gate one screen, group)

Read your checkpoint at `<data>/.progress/screen-build-<key>.md` if it exists and
continue from `next` (`<key>` = the screen key; see `reference/checkpoints.md`).
Write it again after each step; drop a probe id when you delete the node.

`python3` in every command below is the skill's `.venv/bin/python3` (SKILL.md,
"Before anything").

Assembles one screen frame from component instances and live text so it matches
the numeric gate, then groups it (no flat screens).

## Inputs

- The screen's layer resolution table (its plan) — frame position, per-layer
  size/kind/resolution, grouping list.
- `<data>/psd_manifest.json`, `component_ids.json`, `image_hashes.json`,
  `nine_slice.json`, `text_styles.json`.
- `<data>/psd2figma.json` — `frame.w/h`, `figma.gridStyleId`, `figma.fonts`,
  `figma.pages` (screens page id).
- `scripts/figma_helpers.js`, `reference/plugin-helpers.md`.

## Build (Figma-side, small `use_figma` calls)

1. Create the frame at its position; apply `figma.gridStyleId`; `clipsContent`.
2. Place art top-down in PSD z-order with the helpers: `rectHash` for hash-reuse
   and single-cell CROP plates, `instanceAt` for component instances (resize the
   instance, never a child), `nineSliceFrame` for plates with `applied` borders.
   Set node opacity = the manifest `opacity`.
3. Create text with `textByInk`: font = `figma.fonts.body`, bind the
   `text_styles` style id, apply the recipe (per node from the manifest's
   `type.style`/`type.effects`), position by ink box.

## Extract this frame, then gate this screen

```bash
python3 <scripts>/figma_extract_gen.py --data-dir <data> --keys <key>
# paste the printed script into use_figma, then save what it returns:
python3 <scripts>/figma_extract_save.py --data-dir <data> --in <result.json>
python3 <scripts>/verify_figma_vs_psd.py --data-dir <data> --screen <key> --json
```

`figma_extract_gen.py` prints a runnable script (never paste `figma_extract.js`
directly — it is a template) and asserts every configured frame exists.
`figma_extract_save.py` writes `figma_extract_<key>.json` (refuses a key absent
from `screens.json` unless `--allow-unknown`; `--dry-run` shows a diff). Fix the
build until the screen shows `unmapped: 0` and art `0.00`; read the numbers from
`verify_report.json`, not the prose. Then group per the plan's list
(`Container_<Content>` or a section name, never `Frame N`) and re-run
extract + gate — grouping must not move a leaf.

## Acceptance

- Frame at its position, `frame.w×frame.h`, grid style bound, `clipsContent`.
- `unmapped: 0`; art `0.00`; text `≤2.00` or a pin candidate with measured
  values for the gate stage; zero font/style violations.
- Every instance target is a live component; no re-uploaded art for a reuse row.

## Traps

- P-1 grouping reparent keeps local coords — use `reparentKeepWorld`.
- P-6 a widget/vector instance descends and emits inner leaves; place a bare
  plate as a flat CROP rect. P-12 ink box = render bounds − outside stroke.
- P-11 OuterGlow → zero-offset DROP_SHADOW. P-14 build a text shadow as radius 0.
- P-15 apply a PSD type-layer rotation. P-18 colours are 0–1. P-19 build
  incrementally. P-23 wrap cropped text in a `clipsContent` frame. P-24
  choke ≠ spread. See `reference/contracts.md` → Opacity contract.

## Hand-off

`figma_extract_<key>.json` feeds `briefs/screen-verify.md` and `briefs/gate.md`.
Record the frame id and any pin candidates in the checkpoint.
