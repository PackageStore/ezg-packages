# Brief — Components stage (build masters, record the registry)

Read your checkpoint at `<data>/.progress/components-<key>.md` if it exists and
continue from `next` (`<key>` = the component set you are building; see
`reference/checkpoints.md`). Write it again after each master and each record.

`python3` in every command below is the skill's `.venv/bin/python3` (SKILL.md,
"Before anything").

Build components first, screens second — a screen is assembled from instances,
never loose art. Art that repeats three or more times across screens is promoted
to a component (or a variant of an existing set) before any screen uses it.

## Inputs

- `<data>/component_ids.json` — existing components/variants → node id. Check it
  first; if the art is already covered, instance it, do not rebuild.
- `<data>/image_hashes.json`, `<data>/nine_slice.json` (from `briefs/art.md`).
- `<data>/psd2figma.json` — `figma.pages` (build on the components page id),
  `figma.fonts`.
- `scripts/figma_helpers.js`, `reference/plugin-helpers.md`, `reference/nine-slice.md`,
  `reference/component-registry.md`.

## Read the registry

```bash
python3 -c "import json,sys; print(json.dumps(json.load(open(sys.argv[1])),indent=2)[:4000])" <data>/component_ids.json
```

## Build (Figma-side, one `use_figma` payload at a time)

Paste `scripts/figma_helpers.js` ahead of the build, then call `nineSliceFrame`
(plate masters, border `[L,T,R,B]` from `nine_slice.json`, hash from
`image_hashes.json`), `rectHash`, `instanceAt`. Combine masters with the Plugin
API's `figma.combineAsVariants`, then set a unique `Type=` per variant. Verify the helpers once per session by pasting
`figma_helpers.js` then `figma_helpers_selftest.js` (`PAGE_ID` a page id, `FONT`
= `figma.fonts.body`); require `{pass:true}` and no leftover node.

## Record — never a per-plan snapshot

```bash
python3 <scripts>/registry_add.py --data-dir <data> component --entry <file> [--replace]
python3 <scripts>/registry_add.py --data-dir <data> nine-slice-applied --stem <stem> --entry <file> [--replace]
python3 <scripts>/registry_add.py --data-dir <data> style --entry <file> --sidecar <name> [--replace]
python3 <scripts>/registry_add.py --self-test
```

Locks, deep-merges and atomically rewrites `component_ids.json` /
`nine_slice.json` / the style-id files, so parallel builders write straight into
the current file. Exit 0 ok, 2 usage, 3 validation, 4 scalar conflict (file left
untouched — pass `--replace` only to overwrite a scalar deliberately). Every id
must match `\d+:\d+`; a body with `variants` needs a `setId`. See
`reference/component-registry.md`.

If this component needs a new stem uploaded, run `briefs/art.md` first.

## Acceptance

- The set exists; `componentPropertyDefinitions` reads without an error state.
- Each variant's size matches its source; `applied` borders recorded.
- `registry_add.py --self-test` passes; ids land in `component_ids.json`.

## Traps

- P-2 instance children are immutable — differing per-instance art is a variant,
  not an overlay. P-5 give every variant a unique `Type=` or the set errors.
- P-8 border-vs-axis guard. P-13 size a STRETCH-margin instance to `target+2×margin`,
  offset `−margin`. P-21 per-variant icons use a nested exposed instance, not an
  INSTANCE_SWAP. P-20 an unpublished file's components are local copies.

## Hand-off

`component_ids.json` (ids, variants, asset stems), `nine_slice.json` (`applied`)
feed `briefs/screen-build.md`. Record every id created in the checkpoint; drop an
id when you delete its node.
