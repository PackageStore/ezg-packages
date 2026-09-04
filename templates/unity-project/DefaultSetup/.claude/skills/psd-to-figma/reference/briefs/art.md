# Brief — Art stage (export, icons, upload, borders)

Read your checkpoint at `<data>/.progress/art-<key>.md` if it exists and
continue from `next` (`<key>` = `all` for this whole-run stage; see
`reference/checkpoints.md`). Write it again after each step.

`python3` in every command below is the skill's `.venv/bin/python3` (SKILL.md,
"Before anything").

Renders every art and icon layer to PNGs, uploads them to Figma, and measures
9-slice borders — the pixels and hashes the build stages instance.

## Inputs

- `<data>/psd_manifest.json` (from `briefs/data.md`).
- `<data>/psd2figma.json` — `export.*` (`plates`, `tieBreak`, `allowShared`,
  `skipAssets`), `icons.*` (`subdir`, `skip`, `variantOverrides`, `setPrefixes`).
- `<data>/image_hashes.json` — existing asset name → uploaded hash (reuse, never
  re-upload a name already listed).

## Commands

```bash
python3 <scripts>/psd_export_pngs.py --data-dir <data>
python3 <scripts>/psd_export_icons.py --data-dir <data>
python3 <scripts>/nine_slice_detect.py --data-dir <data>
```

Upload is Figma-side (MCP): call `upload_assets` through `use_figma` to mint one
single-use submit URL per asset, save them to a JSON list, then merge hashes:

```bash
python3 <scripts>/figma_upload.py <urls.json> --data-dir <data>   # add --new-only to skip already-hashed names
```

- Exporter: writes `assets/` + `assets_index.json`, recording `bakedOpacity` per
  stem — `"none"` (`topil()`, raw pixels) or `"fill"` (`composite()`, fill
  opacity baked in). A stem shared by two same-size layers with different pixels,
  or shared across the art and icon namespaces, prints `STEM COLLISION <stem>:
  ...` and exits 3 (naming the stems). Exit precedence: 3 collision, else 1
  error, else 0. Declare a genuine share in `export.allowShared`.
- Icons: writes `icons/` + `icons_index.json` (multi-source, per-source size).
- Borders: refreshes only the measured fields (`size`, `border`, `band`, `tol`,
  `sliceable`) and preserves every hand key (`applied`, `axisNote`, notes); a
  stem no longer in `export.plates` is kept and marked `"stale": true`, never
  deleted, and the run prints `stale (not in export.plates): <stems>` when any
  exist.

## Acceptance

- `assets_index.json` carries `bakedOpacity` on every present-PSD stem.
- Exporter exits 0 (no undeclared `STEM COLLISION`).
- `image_hashes.json` only grows; no name is uploaded twice.
- Re-running the detector leaves `applied`/`axisNote` intact.

## Traps

- P-4 delete the placement frames `upload_assets` drops (by recorded id, via
  `deleteByIds`); remove those ids from the checkpoint after deletion.
- P-8 a 9-slice border that exceeds its axis collapses the middle band — the
  detector records `sliceable:false`; place such art unsliced.

## Hand-off

`assets_index.json` (`bakedOpacity`), `icons_index.json`, `image_hashes.json`
(name → hash), `nine_slice.json` (`applied` borders per stem) feed the component
and screen-build stages. Record the new stems and their hashes in the checkpoint.
