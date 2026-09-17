# Brief — Screen build (generate → paste → save → learn ids)

Read your checkpoint at `<data>/.progress/screen-build-<key>.md` if it exists and
continue from `next` (`<key>` = the screen key; see `reference/checkpoints.md`).
Write it again after each step; drop a probe id when you delete the node.

`python3` in every command below is the skill's `.venv/bin/python3` (SKILL.md,
"Before anything").

Assembles one screen frame from a generated build plan and live text so it
matches the numeric gate, then groups it (no flat screens).

## Inputs

- `<data>/build_plan_<key>.json` (from `build_plan_gen.py`).
- `<data>/psd_manifest.json`, `component_ids.json`, `image_hashes.json`,
  `nine_slice.json`, `text_styles.json`, `components_plan.json`.
- `<data>/psd2figma.json` — `frame.w/h`, `figma.gridStyleId`, `figma.fonts`,
  `figma.pages` (screens page id).
- `figma-build` skill: `scripts/figma_build.js`, `scripts/figma_helpers.js`,
  `reference/build-plan.md`.

## Generate the plan and the build payload

```bash
python3 <scripts>/build_plan_gen.py --data-dir <data> --keys <key>
python3 <scripts>/figma_build_gen.py --data-dir <data> --key <key> --out <payload.js>
```

`build_plan_gen.py` writes `build_plan_<key>.json` — the resolved op list for
this screen in the `figma-build` plan contract. `figma_build_gen.py` reads the
project font from `psd2figma.json` and calls the `figma-build` skill's generator,
which fills `figma_build.js` with that plan and writes a runnable JS payload. Paste the payload into `use_figma` (load
`figma-use` first; `whoami` must show a full seat).

## Save the result and gate

```bash
python3 <scripts>/figma_build_save.py --data-dir <data> --in <result.json>
python3 <scripts>/verify_figma_vs_psd.py --data-dir <data> --screen <key> --json
```

`figma_build_save.py` records node ids from the build result into
`node_ids_<key>.json` and the registries. Fix the build until the screen shows
`unmapped: 0` and art `0.00`; read the numbers from `verify_report.json`, not the
prose. Then group per the plan's grouping list (`Container-<Content>` or a section
name, never `Frame N`; hyphens, never `_`) and re-run extract + gate — grouping
must not move a leaf.

## Learn ids

Once the screen passes, record node ids so renames never break the gate:

```bash
python3 <scripts>/verify_figma_vs_psd.py --data-dir <data> --screen <key> --json --learn-ids
```

## Acceptance

- Frame at its position, `frame.w×frame.h`, grid style bound, `clipsContent`.
- `unmapped: 0`; art `0.00`; text `≤2.00` or a pin candidate with measured
  values for the gate stage; zero font/style violations.
- Hygiene S-1/S-2/S-7/S-9 clean in `verify_report.json`.
- Every instance target is a live component; no re-uploaded art for a reuse row.

## Traps

- P-1 grouping reparent keeps local coords — use `reparentKeepWorld`.
- P-6 a widget/vector instance descends and emits inner leaves; place a bare
  plate as a flat CROP rect. P-12 ink box = render bounds − outside stroke.
- P-11 OuterGlow → zero-offset DROP_SHADOW. P-14 build a text shadow as radius 0.
- P-15 apply a PSD type-layer rotation. P-18 colours are 0–1. P-19 build
  incrementally. P-23 wrap cropped text in a `clipsContent` frame. P-24
  choke ≠ spread. P-28 a throwing script rolls back all writes. See
  `reference/contracts.md` → Opacity contract.

## Hand-off

`figma_extract_<key>.json` feeds `briefs/screen-verify.md` and `briefs/gate.md`.
Record the frame id and any pin candidates in the checkpoint.
