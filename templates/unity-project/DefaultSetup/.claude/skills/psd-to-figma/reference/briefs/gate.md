# Brief — Gate (full extract, numeric gate, pins, pre-existing failures)

Read your checkpoint at `<data>/.progress/gate-<key>.md` if it exists and
continue from `next` (`<key>` = `all` for this whole-run stage; see
`reference/checkpoints.md`). Write it again after each step.

`python3` in every command below is the skill's `.venv/bin/python3` (SKILL.md,
"Before anything").

Runs the numeric gate over every registered screen, pins the irreducible text
deviations, and reports pre-existing failures separately without absorbing them.

## Inputs

- All `<data>/figma_extract_<key>.json` (keys from `screens.json`).
- `<data>/psd_manifest.json`, `<data>/accepted_debt.json`, `<data>/text_styles.json`.

## Commands

```bash
python3 <scripts>/figma_extract_gen.py --data-dir <data>          # no filter = all frames
python3 <scripts>/figma_extract_save.py --data-dir <data> --in <result.json>
python3 <scripts>/verify_figma_vs_psd.py --data-dir <data> --json
python3 <scripts>/verify_figma_vs_psd.py --selftest
```

- No `--screen`: gates every screen; exits 0 only when the bar is met and no
  extract is missing, 1 when the bar is not met or any extract is absent, 2 on an
  unknown `--screen` key. A screen whose `figma_extract_<key>.json` is missing is
  tolerated — listed under `Missing extracts:` on stdout, its rows skipped, exit
  1 — so no need to trim `screens.json` into a scratch dir.
- `--json` also writes `verify_report.json` (markdown unchanged): per-screen
  `art_max`/`text_max`/`unmapped`/`font_violations`/`style_violations` and a
  `rows` array of `{node, role, status, dx, dy, dw, dh, pin}` (status
  PASS/FAIL/EXC_PASS/EXC_FAIL/UNMAPPED), plus top-level `missing` and `exit`.
- `--selftest` runs the recipe resolver's unit checks and exits without reading
  data.

## Recipe and pins

Each text node's expected style/effects come from the manifest layer it matched
(`type.style`, `type.effects`); `text_styles.json.layerMap` is only an override.
A phantom drop shadow the shared style declares but neither the node nor the PSD
carries is not subtracted. `RECIPE_OVERRIDE_IGNORED` / `RECIPE_MISSING` /
`PIN_UNUSED` print to stderr and, under `--json`, to `verify_report.json`; the
markdown carries none. See `reference/contracts.md` → Text recipe.

Fix a new-screen failure first: `unmapped`/`art` → fix the build; `text` → fix
size/position/effect, then pin only if irreducible. A pin is per `node` **and**
`screen` (names repeat across screens) with `verify_property` (`ink_centre` or
`position_only`), `measured`, and a `reason`; it never widens a tolerance. No pin
on an `art` row — that is a build error. See `reference/contracts.md` →
exception mechanism.

## Acceptance

- Zero failures whose screen is a new screen; `art_max: 0.00`, `unmapped: 0`,
  `font_violations: 0`, `style_violations: 0` there.
- `ART_TOL`, `TEXT_TOL`, `DRIFT_TOL` unchanged; two runs give an identical
  `verify_report.md`.
- Pre-existing failures reported under their own heading, never re-pinned. The
  gate may exit 1 on that pre-existing debt — that is not a regression.

## Traps

- P-10 phantom shadow over-subtraction (the manifest recipe fixes it).
- P-11 an OuterGlow inflates render bounds symmetrically — pin `ink_centre` when
  the label is visually centred. P-14 the gate models edge expansion as
  `max(stroke, shadow)`, not stacked.

## Hand-off

`verify_report.md`/`.json` and the new pins in `accepted_debt.json`. Record the
pass/fail split (new vs pre-existing) in the checkpoint.
