# Brief — Screen verify (independent re-extract, gate, hygiene, visual diff)

Read your checkpoint at `<data>/.progress/screen-verify-<key>.md` if it exists
and continue from `next` (`<key>` = the screen key; see
`reference/checkpoints.md`). Write it again after each step.

Proves one already-built screen matches its PSD, independently of the build. Do
not trust the build's own extract — re-extract the frame yourself.
`python3` here is the skill's `.venv/bin/python3` (Pillow/numpy needed for the
visual diff).

## Inputs

- The screen's plan table (the only outside document you open) — frame position
  and `dx`, grouping list, expected leaves.
- The built frame in the Figma file (`figma.fileKey`, screens page from
  `figma.pages` in `<data>/psd2figma.json`).
- `<data>/psd_manifest.json`, `<data>/diff_regions.json`.

## Steps

1. **Re-extract.** Generate the extract script for this screen and paste it into
   `use_figma` (load `figma-use` first; `whoami` must show a full seat), then
   save what it returns. A large `use_figma` return may arrive stringified —
   `JSON.parse` before indexing (P-9).
2. **Gate this screen** and read the numbers from `verify_report.json` (`art_max`,
   `text_max`, `unmapped`, `font_violations`, `style_violations`, `rows`).
3. **Hygiene.** Confirm no flat screen (at least one `Container_`/section frame,
   never `Frame N`); copy the grouping shape from a sibling screen that already
   passes, read from its `figma_extract_<sibling>.json`.
4. **Visual diff.** Render both sides into `<data>/diff/`: the Figma frame via
   `exportAsync {format:'PNG', constraint:{type:'SCALE', value:1}}` →
   `diff/figma_<key>.png`; the PSD composite cropped to `[−dx, 0, frame.w, frame.h]`
   → `diff/psd_<key>.png`. Both must be `frame.w×frame.h`. Then run the diff.

## Commands

```bash
python3 <scripts>/figma_extract_gen.py --data-dir <data> --keys <key>
python3 <scripts>/figma_extract_save.py --data-dir <data> --in <result.json>
python3 <scripts>/verify_figma_vs_psd.py --data-dir <data> --screen <key> --json
python3 <scripts>/visual_diff.py --data-dir <data> --screen <key>
```

`--screen` (repeatable) restricts the gate and the diff to this key; an unknown
key exits 2 and lists the known keys. In `--screen` mode `visual_diff.py` prints
one JSON-lines object per screen (`{"screen":KEY,"regions":[{"region":NAME,
"mean":M,"max":X}, ..., {"region":"_overall",...}]}`), reads the pair rendered
in step 4 and writes only `diff/diff_<key>.png`; a known screen with no `diff_regions.json` entry prints
`{"screen":KEY,"regions":[]}`. Add the screen's regions (frame-relative
`[x,y,w,h]`, one per grouping container) to `diff_regions.json` first.

## Acceptance

- `unmapped: 0`, `art_max: 0.00`, `text_max ≤ 2.00`, `font_violations: 0`,
  `style_violations: 0` for this screen.
- Every visual-diff region with `max > 40` or `mean > 4` has a named cause
  (font raster, glow substitution, band stretch); none is "unknown". Text
  anti-aliasing alone can read `max` up to ~120 with low `mean` — explain it.
- A screen whose PSD is wider than `frame.w` carries a negative `dx` and crops
  the PSD at `x = −dx`; an off-by-`dx` fails every region.

## Traps

- P-3 address the page by id; `setCurrentPageAsync` at most once. P-7 a View seat
  fails writes silently — `whoami` first. P-9 parse a stringified return.
- P-6 an instance that descends changes the leaf count.

## Hand-off

`figma_extract_<key>.json`, `verify_report.json`, `diff/*.png` and the region
causes. A screen that fails on a build error, not a pin, goes back to
`briefs/screen-build.md`. Record the pass state in the checkpoint.
