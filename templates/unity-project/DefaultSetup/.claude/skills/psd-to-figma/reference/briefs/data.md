# Brief — Data stage (sources → manifest)

Read your checkpoint at `<data>/.progress/data-<key>.md` if it exists and
continue from `next` (`<key>` = `all` for this whole-run stage; see
`reference/checkpoints.md`). Write it again after each step below.

`python3` in every command below is the skill's `.venv/bin/python3` (SKILL.md,
"Before anything").

Turns the PSD sources into `psd_manifest.json`, the single source of truth every
later stage reads (`type.style`, `type.effects`, `opacity`, borders, collisions).

## Inputs

- PSD files under `paths.psdDir` (from `<data>/psd2figma.json`).
- `<data>/screens.json` — per-screen PSD path, source size, `dx`/`dy` shift.
- `<data>/node_names.json` — raw PSD layer name → node name and `assetKeys`
  (asset stem) per screen.
- `<data>/psd2figma.json` — `export.*`, `icons.*`, `tables.*`.

## Steps

1. Add each new screen to `screens.json` (PSD path, source size, `dx`/`dy`); a
   PSD wider than `frame.w` carries the negative `dx`, never a resized frame.
2. Add node names and asset stems to `node_names.json`. A raw name that breaks a
   file path (space, `+`, `/`, `%`) needs an explicit `assetKeys` entry.
3. Build the manifest, then gate it on collisions.

## Commands

```bash
python3 <scripts>/psd_manifest.py --data-dir <data>
python3 <scripts>/psd_manifest.py --data-dir <data> --strict
```

- No flag: writes `psd_manifest.json`, prints `Collisions: N node-style, M
  stem-size`, exits 0, and records a top-level `collisions` key
  (`{nodeStyles, stemSizes}`). `nodeStyles` = a node name used with more than one
  text `type.style` across screens (`Unknown` is ignored); `stemSizes` = a stem
  mapped from `art` layers whose `w×h` differ, excluding stems listed under
  `export.plates`, `export.skipAssets`, `export.tieBreak` or `export.allowShared`.
- `--strict`: exits 3 while either list is non-empty — an opt-in stricter gate
  the runner never uses, so a project carrying known collisions still builds.
  Reach exit 0 by making node names unique or splitting the stem; the exporter's
  `tieBreak`/`allowShared` escapes do not suppress this report.

The exporter's escapes live under `export` in `psd2figma.json`: `tieBreak`
(`stem → {w,h}`, the size the exporter keeps) and `allowShared` (`stem → reason`,
a mandatory non-empty reason declaring shared pixels or a cross-namespace name;
a blank reason is itself an error; it suppresses only its own stem). See
`reference/contracts.md` → Collision contract.

## Opacity

When a layer's Photoshop fill opacity is below 100%, its entry gains
`fillOpacity` and `layerOpacity` (0–1); full-fill layers are unchanged. `opacity`
stays the value a builder sets on the node. See `reference/contracts.md` →
Opacity contract.

## Acceptance

- `psd_manifest.json` regenerates; every visible `art`/`text` layer has a node
  name; hidden layers and disabled effects are logged as skipped, not dropped.
- The default run exits 0 and prints the collision summary; `--strict` returns 3
  while any node-style or stem-size collision remains (0 once the data is clean).
- The two manifest runs are byte-identical on unchanged input.

## Traps

- P-16 effective font size = raw `fontSize` × transform scale — the manifest
  records the raw size; the builder scales it.
- P-17 psd-tools reports disabled effects; the manifest keeps the `enabled` flag.

## Hand-off

`psd_manifest.json` (per-layer `type.style`/`type.effects`/`opacity`/opacity
keys, `collisions`) feeds the art stage (`briefs/art.md`) and every gate.
Record the manifest path and the collision counts in the checkpoint.
