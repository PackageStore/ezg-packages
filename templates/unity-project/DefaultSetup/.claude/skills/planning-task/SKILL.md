---
name: planning-task
description: Capture a new task into backlog/planning/ with full triage + spec (DO NOT touch BACKLOG.md). Used when the user says "create planning task" / "draft task" / "create task X". Multiple agents can run in parallel — the filename uses a timestamp, so it is unique. Large new-system intents (a whole GDD / multi-module design doc) are detected at STEP 0b and dispatched to /planning-system instead. To PICK a planning task into BACKLOG.md, use /add-to-backlog. When intent is unclear between the two skills, confirm with the user first.
---

# Planning Task — Capture Agent

Turn a user request into a fully specified task file in `backlog/planning/`, allowing multiple Claude windows to capture tasks in parallel without overwriting each other. The task is NOT yet queued for `run-backlog` — that only happens when the user selects the task via `/add-to-backlog`.

The backlog uses a **split-file layout**:
- `backlog/planning/<timestamp>-<TIER>-<slug>.md` = drafted, not yet queued (this skill writes here)
- `BACKLOG.md` = index of queued tasks (only `/add-to-backlog` modifies this)
- `backlog/todo/NNN-<slug>.md` = queued task (created by `/add-to-backlog` when picking from planning)
- `backlog/in-progress/`, `backlog/done/` = managed by `run-backlog`

You create **one new file** in `backlog/planning/`. You **DO NOT** touch `BACKLOG.md` and **DO NOT** create files in `backlog/todo/`.

---

## Core principle: clarity-first, right-size the pipeline

**A planning task is an implementation contract, not a rough idea.** Prioritize understanding the correct intent over saving tokens. Do not run a full M/L pipeline for a CSV tweak, but also do not guess decisions that could lead `run-backlog` to implement in the wrong direction.

**Spec against REAL code, not a void (just-in-time).** A spec is only as accurate as the code it references. Spec a task when its dependencies already exist as real code — so `codegraph` returns real paths/class names and there is nothing to guess. **Do NOT batch-spec a whole future phase on an empty/stub codebase**: parallel planners that can't see each other will independently invent paths, duplicate each other's deliverables, and reference configs/classes that nobody creates. Spec one phase at a time, only after the prior phase ships. If you must draft ahead of the code, mark assumed paths/classes explicitly as `[ASSUMED]` and add an `open_question` so they get re-validated before promotion. See the phase-revalidation playbook (`backlog/_REVALIDATION-PLAYBOOK.md`) for fixing specs that were drafted ahead of their dependencies.

```
[0b] NEW-SYSTEM? → whole GDD / ≥2 new interacting modules? dispatch /planning-system (0a match = tie-break)
[0] TRIAGE       → classify XS / S / M / L (≤500 tokens)
[0a] WF-DETECT   → pure scaffold matching a /new-* command? → skip task-planner, use _TEMPLATE_WF
[1] EXTRACT      → parse user intent + clarify until contract is clear
[2] DRAFT        → tier-specific (skip task-planner for XS/S, simple-M, and WF-backed scaffold)
[3] FILENAME     → timestamp + tier + slug (no NNN, no race check)
[4] WRITE        → fill tier-specific template + required skills + conditional guardrails
[5] CHECK        → tier-aware quality check
[6] REPORT       → summarize for user, point to /add-to-backlog
```

---

## STEP 0b — New-system detection (runs BEFORE everything, 0a is the tie-break)

Some intents are not "one task" but **a whole new system** (a GDD / multi-part design doc). Writing a single M/L task for it = losing scope control; batch-spec'ing it by hand = the documented phase-drift failure mode. The right route is the **design pipeline** `/planning-system` (design-validate → mapping → batch-ground into N planning tasks). This skill only **detects and dispatches** — all mechanics live in [.claude/skills/planning-system/SKILL.md](../planning-system/SKILL.md).

**0b fires ONLY when BOTH hold:**

1. **Doc/system scale** — at least one of:
   - The input is a whole GDD/design doc with multiple sections (not one scoped change), or the user drops a doc file and says "build this feature".
   - The intent creates **≥2 NEW feature modules that interact with each other** (not edits to existing modules).
2. **NOT expressible as ONE workflow-backed task** — run the STEP 0a registry match first as the tie-break: if the intent fits one registry row entirely (e.g. one new feature module → `/new-feature`, one new screen → `/new-ui`), **0b yields to 0a**, even when the intent touches economy/IAP. Merely touching economy does NOT trigger 0b — that is only a tier signal (auto-bump M), as today.

**Idempotency (mandatory probe before dispatch):** Glob `TechSpec/<FeatureName>-*.md`. If artifacts already exist → this system already went through the pipeline; do NOT rerun from scratch — dispatch `/planning-system` in **resume mode** (`--from-mapping TechSpec/<FeatureName>-Implementation.md` if the mapping exists, or from the first missing stage). Tell the user which stage you are resuming from.

**Anti-recursion (mandatory):** if the current prompt/context carries the flag `origin: planning-system` (batch mode — the orchestrator is reusing this skill's drafting path), **SKIP 0b and 0a-dispatch ENTIRELY** and go straight to STEP 2. `/planning-system` must never dispatch itself (depth cap = 1).

**When 0b fires:** do NOT write any task, do NOT ask for a tier. Tell the user in one sentence ("This intent is a large new system → routing to the /planning-system design pipeline") and execute `/planning-system` with the input doc. When 0b does not fire → continue to STEP 0 below as normal.

---

## STEP 0 — Triage (always perform first)

Classify the task into one of the four tiers using **concrete signals**, not gut feeling. **When in doubt, choose the LARGER tier** — review gates in `run-backlog` are keyed to the tier and are never escalated automatically at execution time, so an under-tiered task silently skips reviewers (and the M/L runtime-smoke gate), while an over-tiered one only costs a little more review.

| Tier | Signals (any single match) | Pipeline cost |
|---|---|---|
| **XS** | CSV tweak / constant adjust / dead-code removal / rename in 1 file / **add an `EventName` constant**. No new logic. | ~1K tokens, no subagent |
| **S** | Single-file logic tweak, OR a **small self-contained save field/module** (≤2 files, `SetupDefaultData()` fallback, no reshaping of existing saved data). No new UI screen. | ~3K tokens, no subagent |
| **M** | Multi-file feature: new UI screen/popup, new controller, OR a save change that **migrates/reshapes existing player data** or spans 3–8 files. | ~10K tokens, task-planner subagent only if complex (see STEP 2) |
| **L** | Cross-cutting: new IAP/purchase flow, save-data **migration across modules**, auth/session, new system integration, or 9+ files. | ~25K tokens, task-planner subagent + risk pass |

> **Tier = implementation scope, NOT risk.** Tier drives the template + model/effort. Risk (save / security / hot-path) drives which quality gates run, and that is decided at `run-backlog` time from the actual diff — not by inflating the tier. Do not bump a small task to M just because it touches a save or event file; bump only when the *scope* (file count) or *migration risk* is real (see auto-bump below).

**UI skill routing rule** (applies before drafting):
- A task is UI-scoped if it creates or edits a Unity feature screen, popup, persistent HUD widget, reusable UI child prefab, prefab variant, serialized UI references, tab/list/slider/resource preview composition, or screen registration.
- UI-scoped tasks are at least **M**, except a pure code-only tweak to an existing controller in <=2 files with no prefab or serialized-reference work.
- Every UI-scoped task must include `**Required skills:** /create-ui` near the title. Add `/compile-check` when that UI task creates or edits `.cs` files.
- UI-scoped acceptance criteria must explicitly require the `/create-ui` workflow: read `.claude/skills/create-ui/SKILL.md`, follow `references/prefab-templates.md` and `references/mcp-playbook.md`, reuse shared prefab templates, screenshot-verify, and self-correct layout issues before done.
- For a root feature screen or popup, criteria must require a `Popup_Template/screen_template` prefab variant, the correct `FeatureBaseController` subclass on the root, preserved root child order (`child[0]` background, `child[1]` MainUI), wired serialized references, and `UIManager.Show(...)` verification.
- If the requested work is mostly service/gameplay code and UI prefab authoring should happen after the controller/service exists, keep prefab authoring out of scope and create a separate UI follow-up planning task when requested. Do not modify existing planning files just to add the split unless the user explicitly asks.

**Screen redesign / rebuild routing rule** (prevents a "restyle" from silently keeping the old layout while passing every non-visual gate):
- A task that changes an **existing** screen's visual layout to match an approved mockup is NOT a free-form "restyle M task". It MUST carry `**Backed by workflow:** /new-ui` + `**Workflow args:** <Feature> | groundTruth=<approved .png>` + `**Requires:** unity-editor`, so `run-backlog`'s ground-truth gate (STEP 2c) and the `ui-visual-reviewer` visual-diff checkpoints engage. Referencing the mockup only in `**Context docs:**` or in prose acceptance criteria is **not** enough — that path skips the visual gate and is exactly how a wrong layout shipped.
- **Restyle vs rebuild — decide by a structural diff, not by wording:**
  - **Rebuild** (the default when the mockup changes structure): the mockup's `ui-spec.json` introduces, moves, or removes containers/elements the existing prefab lacks (a hero portal, a pity bar, a drop-rate table, a different grid). Recoloring the old prefab cannot produce a layout it does not have — the body must be reconstructed node-by-node from the ui-spec. Route as the `/new-ui` rebuild above.
  - **Restyle** (palette/color/spacing only): permitted ONLY when the existing prefab's hierarchy already matches the ui-spec's containers/elements 1:1 and nothing but styling changes. Still wire `groundTruth=` so the visual gate confirms it.
  - **When in doubt, choose rebuild** (the larger scope). An under-scoped restyle ships the old layout and passes code/perf/qa/runtime-smoke gates (none of them compare against the mockup).
- A rebuild task's acceptance criteria MUST state: "reconstruct the screen body from `<S>.ui-spec.json` **node-by-node**; do NOT recolor the existing prefab", "preserve the existing controller + every serialized reference (verify `unity_search_missing_references` before/after = 0)", and "`ui-visual-reviewer` passes against the approved `<S>.png`".

**Auto-bump rules** (risk-driven — bump only when the risk is real, not merely because a task "touches a save/event file"):
- Touches `Purchase*`, `IAP*`, `Receipt*`, `Payment*` → at least M (L if it is a NEW purchase/IAP flow).
- Touches `Auth*`, `Token*`, `Session*`, OR grants/spends currency, grants owned items, writes to the server, or writes leaderboard/competitive values → at least M.
- **New save field/module:** keep at **S** when self-contained (≤2 files, `SetupDefaultData()` fallback, no reshaping of existing saved data). Bump to **M** only when it spans 3–8 files OR migrates/reshapes existing player data. Bump to **L** only when that migration spans multiple modules.
- **New `EventName` constant only** → stays **XS/S**. Bump to **M** only when the event wires a genuinely NEW cross-system runtime flow (≥2 feature modules coordinating through it) — not for adding a constant that existing code happens to listen to.
- Touches >2 feature modules or >8 files → L.

**Scope-control gate** (prevent uncontrolled sprawling edits):
- If the task is small (`XS/S`) but the draft needs to touch modules outside the scope provided by the user, you must ask the user or split the task. Do not expand it on your own for refactoring/cleanup/pattern rewrite.
- Do not add abstractions, change patterns, change dependencies, change schema/save formats, or modify related registration/feature flows unless the user requests it or there is a compelling reason documented in the task.
- If broad changes are necessary, the task must explicitly document: why broad changes are needed, the affected areas, migration plan (if there is data/schema/config/save), test/regression plan, checkpoints, and rollback/fallback paths.
- If you cannot adequately explain the above points, do not create a broad planning task. Ask the user to narrow the scope or split it into multiple small tasks.
- Plans must prioritize the smallest change that correctly resolves the acceptance criteria. Do not pass the current task by breaking the contract of a future task or existing behavior.

Record your tier choice in your reasoning and explain it to the user in STEP 6. Do not skip this step.

---

## STEP 0a — Workflow-backed detection (deterministic routing)

Run this right after triage. If the task is **pure scaffold** that exactly matches a `/new-*` command in `.claude/commands/`, the command IS the plan — so **skip the task-planner subagent** in STEP 2 (saves ~15–25K tokens) and capture a thin spec with `_TEMPLATE_WF.md` instead.

**Command registry (default Unity template):**

| Intent signal | Command | Exec tier | `Workflow args` format | Sensitive? |
|---|---|---|---|---|
| New feature module (controller + structure under `Assets/_Project/Features/<Domain>/`) | `/new-feature` | M (L if it also reshapes/migrates save data or wires ≥2 modules) | `FeatureName: Description` (PascalCase) | yes if Domain = `Monetization` or it grants/spends value |
| New UI screen/popup prefab (delegates to the `create-ui` skill) | `/new-ui` | M (UI-scoped) | `FeatureName \| groundTruth=<value>` (mockup pipeline — see case 2 item 2b) | no |

> **Excluded by design:**
> - `/new-package` — extracts a UPM module and pushes to the `ezg-packages` monorepo `main` (out-of-band, no `agent/dev`/backlog lifecycle).
> - `/new-class` — S-tier (the task-planner subagent is already skipped for S, so WF saves no planner tokens) and has no deterministic checklist to lift; treat a new-class request as a normal S task.
> - There are no `/new-skill`/`/new-enemy-skill` commands in this project.
>
> **Packaging intent (`/new-package`):** do NOT create a backlog task and do NOT fall through to normal triage — module-packaging runs out-of-band on a different monorepo. Tell the user to invoke the `package-module` skill manually, and stop.

**Decision tree:**

1. **No match** → continue to STEP 1 (normal flow).
2. **Match + PURE scaffold** (the deliverable is exactly what the command generates, no extra logic):
   - **Skip the task-planner subagent.** Read only the matched command file (`.claude/commands/<name>.md`, ~1–3K tokens) — and, if it delegates to a skill (`/new-ui` → `create-ui`), skim that skill's checklist too.
   - Lift the command's / delegated skill's checklist into the task's **Acceptance criteria**.
   - Build `**Workflow args:**` in the EXACT format the command expects (see table).

     **2b — `/new-ui` only: resolve `groundTruth` (mockup pipeline).** A UI task must not be built from a text description alone — it needs a visual ground truth. Append ` | groundTruth=<value>` to the args:
     - User supplied a reference image path → that path (`Read` once to confirm it exists).
     - Probe `ui-catalog/ui-tokens.json` + `ui-catalog/ui-kit.json`. When both exist, run the **UI fast-lane classifier** before drafting:
       1. **Clone lane:** an existing prefab already matches the requested containers/hierarchy and the delta is text/data binding or minor styling → `clone:<ExistingPrefab>`; no mockup.
       2. **Kit-composition lane:** no single prefab matches, but every visible block maps to existing UI-kit tokens and there is no bespoke art/layout direction → spawn [`mockup-drafter`](../../agents/mockup-drafter.md) with `lane=kit-composition`.
       3. **Custom lane:** a new layout, bespoke composition, or visual direction is required → spawn it with `lane=custom`.
       If uncertain between clone and composition, use composition; if uncertain between composition and custom, use custom.
     - **Fresh project without an exported UI catalog:** supplied reference images remain valid. A clone lane is also valid when the prefab resolves uniquely under `Assets/`. Otherwise set `groundTruth=PENDING-MOCKUP`, preserve `**Mockup lane:** custom`, and report that the project must export `ui-catalog/ui-tokens.json` then run `ui-kit-sync.py`; never copy catalog data from another game.
     - Drafter input: featureName / screenName / lane / outputPath `TechSpec/Mockups/<F>/<S>.html` + intent/context docs. `created`/`recovered`/`exists` (validated spec+HTML pair), with the returned HTML confirmed present ⇒ `PENDING-APPROVAL:<path.html>`; `error` ⇒ `PENDING-MOCKUP`. HTML is an internal deterministic render artifact; never ask the user to author or edit it. **Never block planning on drafting.**
     - Persist non-clone classification in the task as `**Mockup lane:** kit-composition|custom` next to `**Workflow args:**`, even when drafting fails. This lets `/ui-mockup` retry without redoing classification. Supplied image and `clone:` paths delete/omit this field.
     - **Generate = autonomous, approve = human:** interactive single-task session → you MAY offer immediate review/approval (that is `/ui-mockup` STEP 4–6 inline). Batch (`origin: planning-system`) or any parallel run → NEVER wait for approval; the draft parks at `PENDING-APPROVAL` and `/ui-mockup` sweeps it later. The drafter writes ONLY its own screen pair (parallel-safe); `backlog-ops.py promote` blocks via `mockup_warnings` while the value is still `PENDING-*`.
   - Use the **exec tier** from the table (do not re-triage by file count) and draft with `_TEMPLATE_WF.md` (STEP 4), setting `**Custom delta:** none`.
   - For UI (`/new-ui`), still set `**Required skills:** /create-ui /compile-check`, add `**Requires:** unity-editor`, and keep the `/create-ui` acceptance criteria from the M template's UI block.
3. **Match + HYBRID** (scaffold + custom logic/wiring/balance beyond the scaffold):
   - Run the normal tier triage (almost always M, or L if cross-cutting) and draft with `_TEMPLATE_M.md` / `_TEMPLATE_L.md` (task-planner subagent only if genuinely complex per STEP 2).
   - Add the `**Backed by workflow:** /new-xxx` + `**Workflow args:** ...` fields to the M/L draft, and scope the task-planner subagent (if any) to the **delta only** — the scaffold is already specified by the command.

WF is **orthogonal to tier**: the filename + BACKLOG.md `[TIER]` bracket still carry the real exec tier (which drives review-gating); the WF marker only tells `run-backlog` to load the command first (its STEP 5.0).

---

## STEP 1 — Extract intent (compact)

Parse the user's message for: **What**, **Why** (if any), **Scope**, **Priority** (default `MEDIUM`), **Constraints**, and **Required skills**.

If the request mentions a screen, popup, HUD, prefab, UI controller, visual layout, UI list/tab/slider/resource preview, or manual Editor authoring for UI, mark the task UI-scoped and apply the UI skill routing rule from STEP 0.

If the intent is ambiguous regarding **what**, **scope**, **priority**, **constraints**, or any decision affecting **acceptance criteria / verify steps / product behavior**, you must ask for clarification before writing the file. Ask in small batches, maximum **3 questions per turn**, and continue clarifying until the task is clear enough to implement as a contract.

Do not guess decisions belonging to the following groups:
- Core product behavior or UX flow.
- Reward/economy/balance values.
- Save data, migration, and persist/restart behavior.
- IAP, purchase, and receipt validation.
- Acceptance criteria or manual verification steps.

You may only assume low-risk details that do not change the outcome (e.g., slug name, title phrasing, expected file paths after grep/read). All other assumptions must be documented clearly in the task, but **assumptions must not be used to replace questions** when ambiguity affects behavior or completion criteria.

For **XS/S**, you can stop clarifying once the small change is clear and the rest is implementation detail. For **M/L**, do not create a planning task if there are remaining `open_questions` affecting the contract; continue asking the user or state clearly that the task is blocked due to missing decisions.

Do not ask questions that can be answered by grepping/reading the codebase or querying codegraph.

---

## STEP 2 — Draft (tier-specific)

> **Workflow-backed pure scaffold (from STEP 0a):** do NOT spawn the task-planner subagent. The matched `/new-*` command is the plan — read it (and any skill it delegates to) to lift its checklist into Acceptance criteria, then jump straight to STEP 3/4 using `_TEMPLATE_WF.md`. A **hybrid** WF task (scaffold + custom logic) follows the M/L path below but carries the `**Backed by workflow:**` field; scope any task-planner subagent to the delta only.

### Tier XS — no exploration needed

Write the task directly from the user's message + your knowledge of the repo. No task-planner subagent, no Grep.

### Tier S — light exploration in the main context

Use `codegraph_explore` or `codegraph_search` to locate symbols and confirm file paths — one call replaces multiple Grep + Read calls. Only fall back to Grep/Read for string literals or details codegraph didn't cover. **DO NOT** spawn a task-planner subagent. Then draft directly.

### Tier M (simple) — main-context draft, NO task-planner subagent

Spawn the opus task-planner subagent **only when the M task is genuinely complex**. Many M tasks are bumped up purely for scope (3–8 files) but are mechanically simple — a task-planner subagent (opus) is wasted tokens for those.

Draft in the **main context** (1–2 `codegraph_explore` calls, then write the spec yourself) when the M task is **simple**, i.e. ALL of:
- A single new save field/module, OR a single new controller/screen, OR a localized set of edits in 3–8 files following one obvious existing pattern.
- No cross-module runtime flow being newly wired.
- No migration/reshaping of existing saved data.
- No open questions affecting the contract.

Escalate to the **task-planner subagent** (next section) when the M task is **complex**: multiple subsystems interact, a non-obvious pattern decision is needed, the dependency graph is unclear, or you cannot confidently list `files_to_touch` after 1–2 codegraph calls.

When drafting in the main context, produce the same JSON fields the task-planner subagent would (see the schema in [.claude/agents/task-planner.md](../../agents/task-planner.md)) so STEP 4 can fill the template identically.

### Tier M (complex) / L — task-planner subagent

> **HYBRID (scaffold + custom logic, from STEP 0a case 3):** if the task is partly a `/new-*` scaffold, scope the subagent to the **delta only**. Add to the prompt body: *"The scaffold (files, registrations, conventions) is handled by `/new-xxx` — do NOT plan or list those files; plan ONLY the custom logic/wiring/balance beyond the command."* Then add a `**Backed by workflow:** /new-xxx` + `**Workflow args:** ...` line into the final M/L draft so `run-backlog` loads the command before applying the delta. Pure scaffolds never reach here — they use the Workflow-backed path above.
>
> A HYBRID/M/L task that builds a **NEW screen not backed by `/new-ui`** additionally gets a real `**Needs mockup:** yes` line (the template comment documents it) — `/ui-mockup` sweeps that flag for drafting + approval, same as the `/new-ui` groundTruth path.

Spawn the **`task-planner`** subagent. Its full brief — steps, JSON schema, and repository-convention checks — lives in [.claude/agents/task-planner.md](../../agents/task-planner.md) so it can be edited independently of this skill. You pass **only the dynamic context** in the prompt:

```
Agent({
  description: "Draft backlog task spec (M/L tier)",
  subagent_type: "task-planner",
  prompt: <<dynamic context below>>
})
```

Prompt body (dynamic context only — the agent already knows the steps and the output schema):

> TIER: <M or L>
> USER INTENT:
> ```
> What: <what>
> Why: <why>
> Scope: <scope>
> Priority: <priority>
> Constraints: <constraints>
> ```
>
> Read the codebase sufficiently and return the single JSON spec object defined in your instructions. DO NOT implement, DO NOT modify files.

The agent returns ONE JSON object (schema defined in [.claude/agents/task-planner.md](../../agents/task-planner.md)). If it returns `open_questions` affecting behavior, acceptance criteria, verification steps, or save/IAP/security/economy/UX flow, the task is **not yet permitted** to be written into `backlog/planning/` — resolve them with the user first (see 2b).

**Re-spawn cap: max 1**. If the user rejects the first and second drafts, commit the second draft with the user's last refinements applied + assumption notes.

### 2b — Present draft to user (M/L only)

Show a **condensed** view, not raw JSON:
- One-line summary
- Required skills (especially `/create-ui` for UI-scoped tasks)
- UI split decision (same task vs separate follow-up, if relevant)
- File list (paths + one-line why)
- Scope-control summary (broad change? why, affected areas, rollback/fallback if any)
- Top 3–5 completion criteria
- Top 3 verify steps
- Any `open_questions`

If there are `open_questions` affecting the contract, ask those questions first (max 3 questions per turn) and **do not** proceed to STEP 3/4 until resolved. Once resolved, update the draft in place.

If there are no major open questions, ask once: *"Looks good, or do we need to tweak files / criteria / verify?"*

### 2c — Refinements

Accept user edits on file lists / criteria / verify steps. Update the draft in place. **DO NOT re-spawn the task-planner subagent** unless the user explicitly rejects the entire approach — and even then, only once.

---

## STEP 3 — Filename (timestamp + tier + slug)

1. Get the UTC millisecond timestamp from the ops script — deterministic, never hand-generate or guess it:
   ```bash
   python3 .claude/scripts/backlog-ops.py timestamp   # → YYYYMMDDTHHmmssSSS
   ```
2. Tier from STEP 0: `XS` | `S` | `M` | `L`.
3. Slug: 2–5 kebab-case words from the task title.
4. Final filename:
   ```
   <timestamp>-<TIER>-<slug>.md
   ```
   Example: `20260523T142301456-M-new-shop-popup.md`

**No NNN. No folder scanning. No race check.** Timestamp + millisecond is unique per agent instance.

**Edge case** (clock skew or agent retry in the same ms): if the filename already exists, append `-r1`, `-r2`, etc.

The file goes into `backlog/planning/`, **never** `backlog/todo/`.

---

## STEP 4 — Write task file

Pick template based on tier:

| Tier | Template file |
|---|---|
| XS | `backlog/_TEMPLATE_XS.md` |
| S  | `backlog/_TEMPLATE_S.md` |
| M  | `backlog/_TEMPLATE_M.md` |
| L  | `backlog/_TEMPLATE_L.md` |

**Body tier rule (all tiers):** every template has a `**Tier:** X` line right under the title — fill it with the exec tier from STEP 0/0a. It MUST match the `<TIER>` in the filename (`backlog-ops.py lint` enforces filename == body == bullet). This is what `run-backlog` reads first to gate its quality gates; the BACKLOG.md bullet `[Tier]` added later by `/add-to-backlog` is only a mirror. Do not omit this line.

**Optional batch fields rule:** `**Context docs:**` / `**Depends on:**` / `**Requires:**` / `**Needs mockup:**` are filled with REAL values or their line is DELETED — never left as a template placeholder (a leftover `**Requires:** unity-editor` placeholder makes `run-backlog` defer a fully headless task).

**Workflow-backed write rule (when STEP 0a flagged a pure scaffold):**
- Use `backlog/_TEMPLATE_WF.md` instead of the tier template. Fill `**Backed by workflow:** /new-xxx`, `**Workflow args:** ...` (exact format from the STEP 0a table), set `**Custom delta:** none`, and lift the command's / delegated skill's checklist into Acceptance criteria.
- The **filename tier still reflects the registry exec tier** (`M` for both `/new-feature` & `/new-ui`) so `run-backlog` review-gating is unchanged.
- A **hybrid** WF task (scaffold + custom logic) uses the normal `_TEMPLATE_M.md` / `_TEMPLATE_L.md` and just adds the `**Backed by workflow:**` + `**Workflow args:**` fields near the title.
- A `/new-ui` mockup path also writes the resolved `**Mockup lane:** kit-composition|custom` beside those fields; omit it for supplied-image and `clone:` paths.

**Required skills rule**:
- If the draft has `required_skills`, write them directly under the title as `**Required skills:** ...`.
- If the task is UI-scoped but `required_skills` omits `/create-ui`, fix the draft before writing.
- If a UI-scoped task creates or edits `.cs` files but `required_skills` omits `/compile-check`, either add `/compile-check` or document why no compile check is needed.

**Conditional guardrail rule** (tag-based — definitions live in `backlog/_GUARDRAILS.md`, NOT pasted into the task):
- Write a single `**Guardrails:**` line listing ONLY the applicable tags (uppercase, bracketed, space-separated), derived from `applicable_guardrails`. Example: `**Guardrails:** [SAVE] [ASYNC] [LOCALIZE]`.
- DO NOT paste the full guardrail blocks/verify recipes into the task file — they are duplicated in every reviewer prompt and bloat tokens. The tag is enough; reviewers look it up in `backlog/_GUARDRAILS.md`.
- `**Guardrails skipped:**` should only call out a guardrail a reader might *expect* to apply but you deliberately excluded, with a `not_applicable` reason of ≥10 chars. If nothing is surprising, write `none`. Do NOT enumerate every unused tag.
- When in doubt whether a tag applies, include the tag (cheap — one word) rather than over-explaining its exclusion.

Write the file to `backlog/planning/<filename-from-STEP-3>.md`.

---

## STEP 5 — Quality check (tier-aware)

Run only the checks for the current tier:

### WF — workflow-backed (pure scaffold)
- [ ] `**Backed by workflow:**` names a real command in `.claude/commands/`.
- [ ] `**Workflow args:**` is in the exact format that command expects (`FeatureName: Description`, PascalCase, for `/new-feature`; `FeatureName | groundTruth=<value>` for `/new-ui`).
- [ ] Optional batch fields (`**Context docs:**` / `**Depends on:**` / `**Requires:**` / `**Needs mockup:**`) are either filled with REAL values or DELETED — never left as template placeholders (a leftover `**Requires:** unity-editor` placeholder makes run-backlog defer a fully headless task).
- [ ] Acceptance criteria include "command checklist fully satisfied" + any delta criteria.
- [ ] No contract-affecting `open_questions` remain (feature name, domain bucket, the gameplay rule the scaffold must satisfy).
- [ ] `**Custom delta:**` is `none` — if it is NOT, this should have been a HYBRID M/L task, not a pure WF task. Re-route.
- [ ] `/new-ui` task: `**Workflow args:**` carries `groundTruth=` with one of the four legal values (approved `.png` / `PENDING-APPROVAL:<...>.html` / `PENDING-MOCKUP` / `clone:<Prefab>`), resolved per STEP 0a item 2b, and `**Requires:** unity-editor` is present.
- [ ] `/new-ui` without a supplied image records the chosen fast lane: `clone:<Prefab>`, or task field `**Mockup lane:** kit-composition|custom` + matching drafter `mockupLane`; kit-composition is used only when all blocks map to existing UI-kit tokens.
- (HYBRID tasks run the M/L checks below **plus** the first two checks here.)

### XS — minimal
- [ ] Title describes the specific change (not "improve X").
- [ ] `**Tier:**` body line present and matches the filename tier.
- [ ] Description is 1 sentence and not ambiguous.
- [ ] No guardrails section (XS cannot trigger any guard by definition).

### S — moderate
- [ ] All XS checks pass.
- [ ] File paths are real (verified via Grep or Glob).
- [ ] At least 1 regression criterion names the related feature.
- [ ] No remaining ambiguity affecting behavior, completion criteria, or verification steps.
- [ ] Does not expand beyond the scope provided by the user; if expansion is needed, the task must be bumped to M/L or ask the user.
- [ ] **No duplicate deliverable** — no NEW file/class/CSV that already exists or is owned by another task (else `run-backlog` blocks on a duplicate-type / re-author conflict).
- [ ] **No phantom reference** — every config/class/accessor the spec says to READ exists or is created by a named upstream task; referenced API method names are real.
- [ ] **Paths real or conventional** — every path exists or matches the epic's actual folder tree; anything assumed-ahead-of-code is marked `[ASSUMED]` + raised as an `open_question`.
- [ ] If there is user input, include the [BOUNDARY] guardrail.
- [ ] If there is a user-facing mutation, include [DOUBLE-SUBMIT] + [LOADING/COOLDOWN].

### M — full
- [ ] All S checks pass.
- [ ] `open_questions` is empty or only contains low-risk implementation details that do not change the outcome.
- [ ] `scope_control` has all fields: broad/not broad, affected areas, out_of_scope, test/regression plan, checkpoints, rollback/fallback.
- [ ] UI-scoped task includes `**Required skills:** /create-ui` and concrete `/create-ui` acceptance criteria.
- [ ] If `scope_control.is_broad_change = true`, there must be a compelling reason, a migration plan if touching data/schema/config/save, and a specific rollback/fallback.
- [ ] `applicable_guardrails` list exists and matches the included blocks.
- [ ] Each excluded guardrail has a `not_applicable` reason of ≥10 chars.
- [ ] Mobile impact — GC alloc / APK size / draw call / save data / localize / CSV: each axis is evaluated, included, or justified.
- [ ] Verify steps cover (1) happy path, (2) edge case, (3) regression check on the related feature.
- [ ] At least 3 manual verification steps.

### L — full + phases
- [ ] All M checks pass.
- [ ] Task has a `**Phases:**` section dividing work into ≤4 sequential sub-steps with explicit checkpoints.
- [ ] Risk section details cross-cutting impacts and what could break.
- [ ] Controlled broad changes: each affected area has a checkpoint/regression check; rollback/fallback is clear enough for the user to decide whether to queue the task.

If any check fails, fix the draft before STEP 6.

---

## STEP 6 — Report

Report to the user, in order:
1. **Selected tier** + reason (which signal triggered it). If workflow-backed, also state **Backed by workflow** (`/new-xxx`) + the resolved **Workflow args** (including the `groundTruth` state for `/new-ui`), and whether it is **pure** (task-planner skipped — note the token saving) or **hybrid** (task-planner planned the delta only).
2. Task title, priority, and created file path (in `backlog/planning/`).
3. Required skills (if any), calling out `/create-ui` for UI-scoped tasks.
4. UI split decision (if relevant): whether this task owns prefab authoring or a separate follow-up UI task should be created.
5. **Pointer**: *"This task is in planning. When you want to queue it for `run-backlog` to run, use `/add-to-backlog` (or say 'add task to backlog')."*
6. **Guardrails skipped** (if any) + reason.
7. **Assumptions made** (if any) so the user can correct them now.
8. **Scope-control summary**: broad/not broad, affected areas, out_of_scope, rollback/fallback if any.
9. Top 3 acceptance criteria so the user can sanity-check the scope.

DO NOT commit. DO NOT modify `BACKLOG.md`. DO NOT create anything in `backlog/todo/`.
