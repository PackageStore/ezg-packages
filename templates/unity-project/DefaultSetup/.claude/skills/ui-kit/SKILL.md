---
name: ui-kit
description: Own the UI-kit contract that every mockup and /new-ui build reads — regenerate it from the screen template prefabs (ui-kit-sync.py), check whether it is stale (--check), and record composition rules a prefab cannot express in ui-kit-usage.json. Use when a template prefab under uiTemplatesRoot is added/renamed/edited, when the ui-spec validator reports kit_stale or unknown_template, when a review catches a mockup composing templates wrongly, or when the user says "sync ui kit" / "update ui kit" / "kit stale".
---

# UI Kit — the contract mockups are drafted against

`.claude/ui-kit/` is the machine-readable description of this project's screen
templates. Everything downstream reads it and nothing else: `mockup-drafter`
picks template names from it, `ui-spec-validator.py` rejects any name that is
not in it, `/new-ui` copies its numbers into the real prefab, and the review
dashboard renders from its CSS.

So the kit is only as true as its last regeneration. **It has no watcher.**
Editing a template prefab does not update it, and the failure is silent: the
kit keeps describing the prefabs as they were, mockups get drafted against a
version of the UI that no longer exists, and the whole UI test suite quietly
skips. That already happened here — the kit described 46 templates while the
folder held 48, and 22 tests stood down for weeks with nobody noticing.

## Files: two halves, different owners

| File | Owner | Rule |
|------|-------|------|
| `ui-kit.json` | generated | **Never hand-edit.** Public contract: per-template size, anchors, pivot, text nodes, children. |
| `ui-kit.css` | generated | **Never hand-edit.** `.tpl-<Name>` classes inlined into every mockup HTML. |
| `kit-preview.html` | generated | **Never hand-edit.** Gallery for humans; open it to see what a template actually looks like. |
| `ui-kit-usage.json` | **hand-written** | Composition rules the prefab YAML cannot express. See below. |
| `*.template.html` | hand-written | Page shells for the gallery and the review dashboard. |

`ui-kit.json` is **project-specific** — `_meta.sourceHash` is a hash of *these*
prefabs. Copying it into another project hands that project a kit describing a
different game, and its first `/ui-mockup` fails as `kit_stale`. A new project
generates its own (`bootstrap.sh` does it, or run the command below).

Where the prefabs are read from is the `uiTemplatesRoot` key of
`.claude/project-profile.json` (`python3 .claude/scripts/project_profile.py uiTemplatesRoot`).

## Commands

```bash
python3 .claude/scripts/ui-kit-sync.py            # regenerate all three artifacts
python3 .claude/scripts/ui-kit-sync.py --check    # exit 1 if stale; changes nothing
```

`--check` states: `fresh` · `stale` (prefabs or usage notes moved — regenerate)
· `missing` (never generated) · `usage-invalid` (fix `ui-kit-usage.json` first;
regenerating would drop every note) · `no-templates` (no UI prefabs in this
project — not an error, exit 0).

Regeneration is deterministic — same prefabs in, same bytes out — so running it
when nothing changed produces an empty diff. When in doubt, run it.

## When to regenerate — this is the part that gets skipped

Any of these, before you do anything else with the UI:

- A prefab under `uiTemplatesRoot` was **added, deleted, renamed, or edited** —
  including a size/anchor tweak. This is the trigger that actually gets missed:
  the change looks like a Unity edit, not a toolchain one.
- You edited `ui-kit-usage.json`.
- The validator or a mockup run reported `kit_stale`, `kit_missing`, or
  `unknown_template` for a template you know exists.
- Fresh clone / freshly generated project, or after merging a branch that
  touched the templates folder (a merge can bring in prefabs without the kit).
- The UI tests report skips: `python3 -m unittest discover -s .claude/scripts/tests`
  standing down with "UI kit not generated yet" means the kit is missing, not
  that the tests passed.

Commit the regenerated artifacts together with the prefab change, in the same
commit. `backlog-preflight.py` flags a staged template-prefab edit whose kit was
not regenerated (`ui-kit-stale`) — that gate exists because documentation alone
did not hold.

## `ui-kit-usage.json` — rules a prefab cannot state about itself

The extractor reads a prefab's own geometry. It cannot read how templates
compose with **each other**: nothing inside `TabBottomTemplate.prefab` says a
tab toggle must live inside it, so a drafter reading `ui-kit.json` alone built
a hand-made tab row in the content area and passed every gate.

Those rules live in `ui-kit-usage.json` as one note per template name (the
prefab file stem). `ui-kit-sync.py` copies each note into that template's
`usage` field, where the drafter reads it as part of the contract.

```json
{
  "templates": {
    "ScrollViewTemplate": "Vùng cuộn — instantiate template, nội dung vào Viewport/Content. CẤM tự gắn ScrollRect/RectMask2D."
  }
}
```

Write a note when a review catches a mockup composing templates wrongly — the
rule is the fix, not the one-off correction. Keep the wording aligned with
`.claude/docs/new-ui-guide.md` §3d, which is the human-facing half of the same
contract; a rule that exists in only one of the two will be followed only half
the time. A note naming a template this project does not have is reported as
`_meta.usageUnknown` in `ui-kit.json` (and by the sync command) rather than
silently dropped — that is how a rename gets caught.

## Verify

```bash
python3 .claude/scripts/ui-kit-sync.py --check                    # expect state: fresh
python3 -m unittest discover -s .claude/scripts/tests             # UI tests must RUN, not skip
```

Then open `.claude/ui-kit/kit-preview.html` if a template's appearance is what
you were changing — the JSON says how big it is, the gallery says what it looks
like.
