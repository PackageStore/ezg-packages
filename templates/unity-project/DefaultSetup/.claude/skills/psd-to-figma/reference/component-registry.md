# Component registry

`component_ids.json` in the project data directory (`<data>`) is the machine-readable source:
it maps every built component and variant to its node id. This file defines how
to read that inventory, what to record in it, and when a piece of art earns
promotion to a component — so you can tell whether new PSD art is already
covered without opening Figma.

Never restate the inventory here. Read it:

```bash
python3 -c "import json,sys; d=json.load(open(sys.argv[1])); print(json.dumps(d,indent=2)[:4000])" <data>/component_ids.json
```

## Machine-readable sources

| File | Maps | Answers |
|---|---|---|
| `component_ids.json` | component / variant name → node id | "does this component exist yet?" |
| `image_hashes.json` | asset name → uploaded image hash | "is this art already in Figma?" |
| `nine_slice.json` | asset name → detected and `applied` borders | "what borders does this plate use?" |
| `page_ids.json` | page name → page id | "where do I create this node?" |
| `style_ids.json` | text style name → style id | "which style do I bind this TEXT to?" |
| `accepted_debt.json` | node → pinned deviation and reason | "is this deviation known?" |

Sidecar files named `<stem>.<plan>.json` are per-plan snapshots. The unsuffixed
file is current; the gate merges the sidecars where it says it does.

## Page layout

Keep three pages and read their ids from `page_ids.json`: one for assembled
screens, one for component sets, one for icon sets. Components and icons never
live on the screens page.

## What to record per component

| Column | Content |
|---|---|
| Component | node name, matching `component_ids.json` |
| Set id | node id of the component set |
| Variants | the variant axis values, or `—` for a single component |
| Covers | what the art *is for*, and which asset stem backs it |

For a 9-sliced plate also record its asset stem and its `L/T/R/B` borders, and
every node size it is known to serve — that is the evidence the one component
covers all of them.

## Promotion rules

- **Art that repeats three or more times across screens gets promoted** to a
  component, or to a variant of an existing set, before any screen uses it.
- **A size difference is not a reason to build a new component.** Every plate is
  9-sliced, so one node renders correctly at any size. Record the extra sizes in
  the registry instead of forking the component.
- **Single-use sliceable plates stay flat.** `nine_slice_detect.py` will detect
  borders for art that appears once; place it as a flat `RECTANGLE` image fill
  and promote it only when a second screen needs it.
- **Loose 9-sliced plates** — sliced in place, inside one screen or component,
  not yet extracted — are promoted on the same three-use threshold. Record them
  with their owning node path so the next reuse can find them.
- Art sitting on the screens page that is not yet a component is still recorded,
  with its reuse count, so the threshold is visible rather than guessed.

## Matching new PSD art

Match on the **asset name in the PSD layer**, not on size. `image_hashes.json`
maps asset name → uploaded hash; if the name is already there, the art is
already in Figma and must not be re-uploaded.

Two nodes with the same hash are the same art. Two nodes with different hashes
are different art, even when they look alike at a glance.

## Deliberate de-duplication overrides

Some visually similar assets must stay separate by product decision, not by
accident. Record each one with its stem, the screen it appears on, its hash, and
the reason:

| Column | Content |
|---|---|
| Stem | asset name in the PSD |
| Screen | where the exception applies |
| Hash | the hash that must stay distinct |
| Reason | the decision, and who made it |

**Never "helpfully" de-duplicate a pair listed there**, even if the hashes turn
out identical. An override is a recorded decision; collapsing it silently
reverses a product call.
