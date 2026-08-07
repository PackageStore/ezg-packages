---
description: Generate + AUTO-APPROVE spec-first UI mockups for /new-ui tasks. Drafts are frozen to their 1080×1920 PNG contract automatically; the dev only steps in to answer forbidden-to-invent questions or request edits via the review dashboard. Runs automatically after planning and manually any time to re-generate.
---

# /ui-mockup — Mockup generate & auto-approve

**Approval is the DEFAULT.** A drafted mockup is automatically frozen to its PNG contract — no human round. The dev only interacts when (a) the drafter left a forbidden-to-invent question / `[?]` placeholder, or (b) they want to edit/re-generate a design.

| Half | Owner | Where it runs |
|------|-------|---------------|
| **Generate draft** | `mockup-drafter` subagent | Inline in `/planning-task` / `/planning-system` (N parallel sessions), or STEP 2 here |
| **Auto-approve** | `ui-review.py auto-approve` (deterministic script) | Right after drafting — planning calls it, and STEP 3 here (STEP 3 adds `--open`: a dev-typed `/ui-mockup` always ends with the approved HTML open in the browser) |
| **Edit / answer questions** | **Dev** via review dashboard (`ui-review.py serve`) | Only when auto-approve reports pending screens, or on demand |

`groundTruth` states — carried in a `/new-ui` task's `**Workflow args:**`, OR on a dedicated
`**Mockup:**` line for a HYBRID task whose `**Workflow args:**` belong to a non-`/new-ui`
workflow (e.g. `/new-feature` that also authors a popup). The token is location-agnostic:
`backlog-ops.py promote` and `ui-review.py` both match/flip it wherever it sits on a real line.

```
groundTruth=PENDING-MOCKUP                                → no draft yet (drafter failed/skipped)
groundTruth=PENDING-APPROVAL:TechSpec/Mockups/<F>/<S>.html → draft exists, auto-approve not yet run
                                                             or blocked by open questions/[?]
groundTruth=TechSpec/Mockups/<F>/<S>.png                   → APPROVED (PNG exists ⇔ approved)
groundTruth=clone:<ExistingPrefab>                         → escape hatch: no mockup needed,
                                                             /new-ui uses the spec-sheet path (§0a)
groundTruth=none                                          → HYBRID escape: task only wires an
                                                             existing screen, no new visual to design
```

A HYBRID task carrying the marker on a `**Mockup:**` line names its screen inline, e.g.
`**Mockup:** groundTruth=PENDING-MOCKUP (screen=<Feature>/<Screen>)` — STEP 1 reads `<Feature>/<Screen>`
from that `screen=` hint instead of from `/new-ui` Workflow args.

**Prerequisite:** `.claude/ui-kit/ui-kit.json` exists and is current — `python3 .claude/scripts/ui-kit-sync.py --check` (exit 1 = regenerate with the same script minus `--check`; lifecycle in skill `ui-kit`). New drafts also produce sibling `<Screen>.ui-spec.json`; the task contract deliberately remains the `.html`/`.png` `groundTruth` states above.

## Invocation

- **Automatic** — `/planning-task` / `/planning-system` run drafting + auto-approve inline after writing planning tasks. Nothing to do.
- `/ui-mockup` (no args) — sweep `backlog/planning/` for every `PENDING-*` task: draft the missing, auto-approve everything that validates, report the rest.
- `/ui-mockup <task-file.md>` — one specific task.
- `/ui-mockup <FeatureName>: <description>` — standalone (no backlog task yet): draft → auto-approve → open the HTML → report the PNG path for a later manual `/new-ui`.
- **Re-generate** — dev names a screen + what to change: run the edit path (STEP 4) for that screen, then auto-approve again. An already-approved screen can be re-generated freely; re-approval overwrites the same PNG path so tasks keep pointing at it.

## STEP 1 — Collect

```bash
grep -l "PENDING-MOCKUP\|PENDING-APPROVAL" backlog/planning/*.md
```

Parse each hit for FeatureName + groundTruth state: from `**Workflow args:**` for a `/new-ui` task, or from the `**Mockup:** groundTruth=… (screen=<Feature>/<Screen>)` line for a HYBRID task not backed by `/new-ui`. Context docs come from `**Context docs:**`. Nothing pending and no args → report "no mockups waiting" and stop.

## STEP 2 — Draft the missing ones (`PENDING-MOCKUP` only)

Spawn `mockup-drafter` per screen — one parallel tool-use block, ≤10/wave (same cap as planning-system fan-out):

```
Agent({ subagent_type: "mockup-drafter",
        description: "Mockup draft — <Feature>/<Screen>",
        prompt: featureName/screenName/branch + outputPath TechSpec/Mockups/<F>/<S>.html
                + task-file path + its **Context docs:** paths })
```

Per result: `created`/`recovered`/`exists` means a validated v1 pair; `legacy-exists` means a validated legacy HTML. Only these statuses may flip `PENDING-MOCKUP` → `PENDING-APPROVAL:<path.html>`, and only after confirming the returned HTML exists. `error` → keep `PENDING-MOCKUP`.

## STEP 3 — Auto-approve (deterministic, no AI, no human)

```bash
python3 .claude/scripts/ui-review.py auto-approve --open            # sweep all pending screens
python3 .claude/scripts/ui-review.py auto-approve --open --task backlog/planning/<task>.md   # scoped
```

**`--open` is MANDATORY when a dev typed `/ui-mockup`** (any invocation form in §Invocation, including the standalone and re-generate paths): show the design, don't just report a path. It costs **exactly one browser tab regardless of screen count** —

- **1 approved screen** → opens that screen's HTML (interactive, template-label toggle).
- **N approved screens** → generates `TechSpec/Mockups/_approved-gallery.html` (PNG grid + per-screen links to HTML/PNG) and opens only that. Generated session state like `_ui-review.html`, gitignored — never commit it.

The JSON gains an `"opened"` field naming what was launched. Omit `--open` **only** when auto-approve runs from `/planning-task` / `/planning-system` or any headless/batch loop — a browser popping up mid-planning is noise. If the browser cannot launch (headless machine, `webbrowser` no-op), the JSON still reports every path — say so and move on.

For every pending screen that passes approve-mode validation, the script: re-renders `--check`, exports the frozen 1080×1920 PNG (headless Chrome), writes the hash-bound `<Screen>.ui-approval.json`, flips the task `groundTruth` → `.png`, and stages only that screen/task. Screens are left `pending` with a reason when:

- the drafter left `questions[]` or literal `[?]` placeholders (forbidden-to-invent group), or
- validation/render/PNG export failed (e.g. no Chrome on the machine).

No Chrome → ask the dev to open the HTML at 100% zoom, screenshot to the sibling `.png`, then run `python3 .claude/scripts/ui-review.py approve --existing-png --item <html>=<SPEC_HASH>`.

## STEP 4 — Dev review / edits (only when needed)

Run `python3 .claude/scripts/ui-review.py serve` and hand the printed URL to the dev. The dashboard (styled per `.claude/docs/design-style/`) is token-protected, loopback-only, and shows ONLY screens that still need a human: it renders each 1080×1920 preview in-page, surfaces the drafter's `questions[]` as pickable options (⚡ options carry deterministic JSON patches applied server-side without AI), `assumptions[]` collapsible, and a free-text edit box per screen.

- **⚡ Apply choices** — hash-checked, patches the authoritative spec, re-renders, re-validates. No AI.
- **✦ AI Regenerate** — free-form visual requests spawn the bounded headless `claude -p` (scoped `Edit(TechSpec/Mockups/**)` under `--permission-mode default`, git-diff containment proof, live progress + ✕ Huỷ). Requests are durably queued in `TechSpec/Mockups/_regen-queue.jsonl` first.
- **✓ Duyệt** — same deterministic approve gate as auto-approve (validator + hash guard + PNG freeze).

Editing directly is also fine: edit only `<Screen>.ui-spec.json`, then `python3 .claude/scripts/ui-spec-render.py <spec> --output <html>`. Never hand-edit generated HTML. After edits, run STEP 3 again — auto-approve re-freezes the changed screens.

`serve` falls back to an ephemeral port when 8765 is busy; `--no-open` for headless, `--no-auto-regen` to queue edits without spawning the agent. `_ui-review.html` is generated session state; do not commit it.

Parallel-safety rule the whole pipeline obeys: planning sessions only ever write their OWN draft file + task file; filesystem remains the persistent queue. The loopback service processes approval requests serially, and every item is rejected if its submitted hash no longer matches disk. AI edit sessions never approve and only write the selected screen's authoritative spec/rendered HTML.

## STEP 5 — Report

Table: screen · state (approved / pending — reason / draft failed) · path. Approved screens have already been opened in the browser by STEP 3's `--open`; state in the report that they are open (or that the browser could not be launched). If a screen stayed `pending`, its HTML is NOT opened — run STEP 4's `serve` instead so the dev lands on the dashboard that can actually resolve it. For pending screens, tell the dev exactly what awaits them in the dashboard (which questions, which screens). Remind: `/add-to-backlog` blocks on `mockup_warnings` while any task remains `PENDING-*` — settle those screens here first, or set `groundTruth=clone:<Prefab>` for screens that just clone an existing layout.

For v1, `.ui-spec.json` = editable source, HTML = generated review, PNG = frozen contract. Re-render and re-approve to the same paths after changes; tasks keep pointing at the same PNG. Legacy embedded-spec HTML remains compatible (approvable, `legacy_spec` warnings only).
