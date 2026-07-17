---
description: Generate + review + APPROVE spec-first UI mockups for /new-ui tasks. New drafts use one authoritative .ui-spec.json rendered deterministically to 1080×2400 HTML; approval remains HUMAN-ONLY. Existing embedded-spec HTML remains compatible.
---

# /ui-mockup — Mockup review & approval

The mockup pipeline has two halves with different owners:

| Half | Owner | Where it runs |
|------|-------|---------------|
| **Generate draft** | `mockup-drafter` subagent | Inline in `/planning-task` / `/planning-system` (N parallel sessions), or STEP 2 here as fallback |
| **Approve** | **Human** (visual taste = 5th forbidden-to-invent group) | HERE, one interactive session, any time |

`groundTruth` states carried in a `/new-ui` task's `**Workflow args:**`:

```
groundTruth=PENDING-MOCKUP                                → no draft yet (drafter failed/skipped)
groundTruth=PENDING-APPROVAL:TechSpec/Mockups/<F>/<S>.html → draft exists, waiting for human
groundTruth=TechSpec/Mockups/<F>/<S>.png                   → APPROVED (PNG exists ⇔ approved)
groundTruth=clone:<ExistingPrefab>                         → escape hatch: no mockup needed,
                                                             /new-ui copies that prefab's layout
                                                             (resolve via ui-catalog/ui-tokens.json)
```

**Prerequisite for generating a draft:** the current project has exported `ui-catalog/ui-tokens.json`, and `ui-catalog/ui-kit.json` exists and is fresh. Run `python3 .claude/scripts/ui-kit-sync.py` after a catalog export or prefab change. If the catalog itself is absent, stop with an actionable prerequisite; never seed it from another game. A user-supplied visual contract or an unambiguous `clone:<ExistingPrefab>` path may bypass draft generation. New drafts also produce sibling `<Screen>.ui-spec.json`; the task contract deliberately remains the `.html`/`.png` `groundTruth` states above.

## Invocation

- `/ui-mockup` (no args) — review mode: sweep `backlog/planning/` for every `PENDING-*` task (plus M/L tasks flagged `**Needs mockup:** yes`).
- `/ui-mockup <task-file.md>` — one specific task.
- `/ui-mockup <FeatureName>: <description>` — standalone (no backlog task yet): draft → review → approve → report the PNG path for a later manual `/new-ui`.

## STEP 1 — Collect

```bash
grep -l "PENDING-MOCKUP\|PENDING-APPROVAL\|\*\*Needs mockup:\*\* yes" backlog/planning/*.md
```

Parse each hit: FeatureName + groundTruth state from `**Workflow args:**` (or the `**Needs mockup:**` flag), lane from `**Mockup lane:**`, and context docs from `**Context docs:**`. A `PENDING-MOCKUP` task missing lane must rerun the planning-task fast-lane classifier against ui-catalog and persist the result before spawning. Nothing pending and no args → report "no mockups waiting" and stop.

## STEP 2 — Draft the missing ones (`PENDING-MOCKUP` only)

Spawn `mockup-drafter` per screen — one parallel tool-use block, ≤10/wave (same cap as planning-system fan-out):

```
Agent({ subagent_type: "mockup-drafter",
        description: "Mockup draft — <Feature>/<Screen>",
        prompt: featureName/screenName/branch/lane + outputPath TechSpec/Mockups/<F>/<S>.html
                + task-file path + its **Context docs:** paths })
```

Per result: `created`/`recovered`/`exists` means a validated v1 pair; `legacy-exists` means a validated legacy HTML. Only these statuses may flip `PENDING-MOCKUP` → `PENDING-APPROVAL:<path.html>`, and only after confirming the returned HTML exists. `error` → keep `PENDING-MOCKUP`. Surface every `questions[]` entry during STEP 4.

## STEP 3 — UI Review Dashboard (assembled at review time, NEVER during planning)

Run `python3 .claude/scripts/ui-review.py serve` (starts a token-protected loopback service on `127.0.0.1:4176` and opens the dashboard over `http://`; occupied port → automatically use an ephemeral port). Closing the browser does not lose state: reopen the printed URL while the process lives, or run `serve` again to rescan the filesystem queue. `serve` runs in the foreground; Ctrl-C stops only the service, never the pending state.

The dashboard discovers pending v1 sidecars plus legacy HTML referenced by `groundTruth=PENDING-APPROVAL:*`, surfaces each screen's `questions[]` + `assumptions[]`, opens previews at true 1080×2400, and provides Refresh / per-screen / selected / approve-all controls. **Approval is script-only:** the dashboard calls the local API, which serially applies the existing spec-hash guard + validator, captures PNG, writes `<Screen>.ui-approval.json`, flips every matching planning task to `groundTruth=<png>`, stages only that screen/task, and returns the refreshed queue. No AI agent, custom protocol, Terminal, or browser reload is involved.

Structured options marked ⚡ carry restricted JSON patches. Selecting them calls the local service, which hash-checks, patches the authoritative spec, removes answered questions, renders, and validates immediately — no AI session. If the same submission also has free-form input, deterministic patches apply first and AI receives only the remaining custom answers/notes against the new hash. Only that remainder, legacy string options, or bespoke visual edits use **AI Regenerate**. The service durably records those requests and launches a bounded `claude -p` edit session with `acceptEdits` (never `bypassPermissions`) and a renderer/validator allowlist. `generate --open` plus the OS URL handlers remain static compatibility fallbacks only. `_ui-review.html` is generated session state; do not commit it.

Parallel-safety rule: planning sessions only write their own screen pair + task; filesystem is the persistent queue. The local HTTP server processes approval requests sequentially, and rejects any item whose submitted hash is stale. AI regenerate sessions never approve.

## STEP 4 — Review loop (human)

Tell the user to review the dashboard in the browser. Per screen:

- **Fast lane:** `clone:<Prefab>` screens never enter the dashboard. `mockupLane=kit-composition` should normally finish as Review → optional ⚡ Apply choices → Approve. `mockupLane=custom` keeps the same gate but may require AI Regenerate.
- **v1 sidecar exists:** edit only `<Screen>.ui-spec.json`, then run `python3 .claude/scripts/ui-spec-render.py <spec> --output <html>`. Never hand-edit generated HTML.
- **Legacy HTML only:** it remains editable/approvable for backward compatibility. Optionally extract a v0 sidecar with `ui-spec-extract.py`; do not pretend it is strict v1.

Iterate until the user says approve or park. **Never self-approve; never treat silence as approval.**

Resolve all structured questions in one dashboard pass. Patched choices apply instantly; natural-language edit requests carry the exact HTML path, expected `specHash`, and human instructions. The AI must verify the hash, edit only the authoritative spec, then render/validate. The local dashboard observes the filesystem change; do not regenerate the dashboard and never approve a regenerated result automatically.

## STEP 5 — Approve → export PNG (dashboard executes this gate)

Before export, run:

```bash
python3 .claude/scripts/ui-spec-validator.py TechSpec/Mockups/<F>/<S>.html --mode approve
python3 .claude/scripts/ui-spec-render.py TechSpec/Mockups/<F>/<S>.ui-spec.json \
  --output TechSpec/Mockups/<F>/<S>.html --check   # v1 only
```

For `specVersion: 1`, either failure blocks approval. Legacy HTML emits `legacy_spec` warnings but remains approvable so existing planning/backlog tasks are not broken.

macOS:
```bash
"/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" --headless=new --disable-gpu \
  --hide-scrollbars --window-size=1080,2400 \
  --screenshot="$(pwd)/TechSpec/Mockups/<F>/<S>.png" "file://$(pwd)/TechSpec/Mockups/<F>/<S>.html"
sips -g pixelWidth -g pixelHeight TechSpec/Mockups/<F>/<S>.png   # expect 1080 × 2400
```
Windows:
```powershell
& "C:\Program Files\Google\Chrome\Application\chrome.exe" --headless=new --disable-gpu `
  --hide-scrollbars --window-size=1080,2400 --screenshot="<abs>.png" "file:///<abs>.html"
```
No Chrome on the machine → ask the user to open the HTML at 100% zoom and screenshot to the sibling `.png`, then run `python3 .claude/scripts/ui-review.py approve --existing-png --item TechSpec/Mockups/<F>/<S>.html=<SPEC_HASH>`. This validates the 1080×2400 PNG and completes the same evidence/task/staging gate.

## STEP 6 — Flip groundTruth + stage

1. Task file `**Workflow args:**`: `PENDING-APPROVAL:<...>.html` → `TechSpec/Mockups/<F>/<S>.png`.
2. `git add` the screen's `.ui-spec.json` (when present), `.html`, `.png`, `.ui-approval.json`, and the task file — **no commit**. `_ui-review.html` stays unstaged.

For v1, `.ui-spec.json` = editable source, HTML = generated review, PNG = frozen contract. Re-render and re-export to the same paths after changes; tasks keep pointing at the same PNG. Legacy HTML keeps its old behavior.

## STEP 7 — Report

Table: screen · state (approved / still pending / draft failed) · path, plus unanswered drafter `questions[]`. Remind: `/add-to-backlog` blocks on `mockup_warnings` while any task remains `PENDING-*` or a `clone:<Prefab>` target does not resolve through the current project's catalog or to one unambiguous prefab under `Assets/` — approve here first, or choose a real clone target.
