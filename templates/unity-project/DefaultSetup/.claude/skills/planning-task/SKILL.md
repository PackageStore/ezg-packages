---
name: planning-task
description: Capture a new task into backlog/planning/ with full triage + spec (DO NOT touch BACKLOG.md). Used when the user says "create planning task" / "draft task" / "create task X". Multiple agents can run in parallel — the filename uses a timestamp, so it is unique. To PICK a planning task into BACKLOG.md, use /add-to-backlog. When intent is unclear between the two skills, confirm with the user first.
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
[0] TRIAGE       → classify XS / S / M / L (≤500 tokens)
[1] EXTRACT      → parse user intent + clarify until contract is clear
[2] DRAFT        → tier-specific (skip Plan subagent for XS/S)
[3] FILENAME     → timestamp + tier + slug (no NNN, no race check)
[4] WRITE        → fill tier-specific template + required skills + conditional guardrails
[5] CHECK        → tier-aware quality check
[6] REPORT       → summarize for user, point to /add-to-backlog
```

---

## STEP 0 — Triage (always perform first)

Classify the task into one of the four tiers using **concrete signals**, not gut feeling. When in doubt, choose a smaller tier — `run-backlog` can escalate later if scope creep occurs during implementation.

| Tier | Signals (any single match) | Pipeline cost |
|---|---|---|
| **XS** | CSV tweak / constant adjust / dead-code removal / rename in 1 file / **add an `EventName` constant**. No new logic. | ~1K tokens, no subagent |
| **S** | Single-file logic tweak, OR a **small self-contained save field/module** (≤2 files, `SetupDefaultData()` fallback, no reshaping of existing saved data). No new UI screen. | ~3K tokens, no subagent |
| **M** | Multi-file feature: new UI screen/popup, new controller, OR a save change that **migrates/reshapes existing player data** or spans 3–8 files. | ~10K tokens, Plan subagent only if complex (see STEP 2) |
| **L** | Cross-cutting: new IAP/purchase flow, save-data **migration across modules**, auth/session, new system integration, or 9+ files. | ~25K tokens, Plan subagent + risk pass |

> **Tier = implementation scope, NOT risk.** Tier drives the template + model/effort. Risk (save / security / hot-path) drives which quality gates run, and that is decided at `run-backlog` time from the actual diff — not by inflating the tier. Do not bump a small task to M just because it touches a save or event file; bump only when the *scope* (file count) or *migration risk* is real (see auto-bump below).

**UI skill routing rule** (applies before drafting):
- A task is UI-scoped if it creates or edits a Unity feature screen, popup, persistent HUD widget, reusable UI child prefab, prefab variant, serialized UI references, tab/list/slider/resource preview composition, or screen registration.
- UI-scoped tasks are at least **M**, except a pure code-only tweak to an existing controller in <=2 files with no prefab or serialized-reference work.
- Every UI-scoped task must include `**Required skills:** /create-ui` near the title. Add `/compile-check` when that UI task creates or edits `.cs` files.
- UI-scoped acceptance criteria must explicitly require the `/create-ui` workflow: read `.claude/skills/create-ui/SKILL.md`, follow `references/prefab-templates.md` and `references/mcp-playbook.md`, reuse shared prefab templates, screenshot-verify, and self-correct layout issues before done.
- For a root feature screen or popup, criteria must require a `Popup_Template/screen_template` prefab variant, the correct `FeatureBaseController` subclass on the root, preserved root child order (`child[0]` background, `child[1]` MainUI), wired serialized references, and `UIManager.Show(...)` verification.
- If the requested work is mostly service/gameplay code and UI prefab authoring should happen after the controller/service exists, keep prefab authoring out of scope and create a separate UI follow-up planning task when requested. Do not modify existing planning files just to add the split unless the user explicitly asks.

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

### Tier XS — no exploration needed

Write the task directly from the user's message + your knowledge of the repo. No Plan subagent, no Grep.

### Tier S — light exploration in the main context

Use `codegraph_explore` or `codegraph_search` to locate symbols and confirm file paths — one call replaces multiple Grep + Read calls. Only fall back to Grep/Read for string literals or details codegraph didn't cover. **DO NOT** spawn a Plan subagent. Then draft directly.

### Tier M (simple) — main-context draft, NO Plan subagent

Spawn the opus Plan subagent **only when the M task is genuinely complex**. Many M tasks are bumped up purely for scope (3–8 files) but are mechanically simple — a Plan subagent (opus) is wasted tokens for those.

Draft in the **main context** (1–2 `codegraph_explore` calls, then write the spec yourself) when the M task is **simple**, i.e. ALL of:
- A single new save field/module, OR a single new controller/screen, OR a localized set of edits in 3–8 files following one obvious existing pattern.
- No cross-module runtime flow being newly wired.
- No migration/reshaping of existing saved data.
- No open questions affecting the contract.

Escalate to the **Plan subagent** (next section) when the M task is **complex**: multiple subsystems interact, a non-obvious pattern decision is needed, the dependency graph is unclear, or you cannot confidently list `files_to_touch` after 1–2 codegraph calls.

When drafting in the main context, produce the same JSON fields the Plan subagent would (see below) so STEP 4 can fill the template identically.

### Tier M (complex) / L — Plan subagent

Spawn a Plan subagent with `subagent_type: "Plan"`. Brief as follows:

> You are drafting a backlog task spec for the **[Project Name]** project (Unity mobile merge-grid game, C#, primary target is Android).
>
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
> Read the codebase sufficiently to produce a draft spec. DO NOT implement. DO NOT modify files. Terse — JSON only, no chain-of-thought prose, ≤2000 tokens.
>
> Steps:
> 1. Read `CLAUDE.md` and files in `.agents/rules/` (core-system, code-style, data-persistence, third-party). Read relevant SKILL.md files in `.agents/skills/` if the task touches those systems.
> 2. Use `codegraph_explore` with the key symbols/files from the user intent to locate and understand what will be modified. Fall back to Grep/Glob only for paths or string literals codegraph doesn't cover. Locate files likely to be modified (controllers, prefabs, CSV configs, save data, events, scenes) under `Assets/_Project/`.
> 2a. **No-collision / no-phantom checks (mandatory):**
>    - **De-dup:** before declaring a NEW file/class/CSV as a deliverable, confirm it does not already exist (search the codebase) AND is not already owned by another task — skim `backlog/done/`, `backlog/todo/`, and other `backlog/planning/` files for the same artifact. If it already exists or another task owns it, this task must REFERENCE it, not recreate it (recreating a class = CS0101 duplicate-type → `run-backlog` blocks). Put any overlap in `open_questions`.
>    - **Real paths only:** every path in `files_to_touch` / `related_files` must either already exist in the repo, or follow the project's established folder convention for that epic (match where sibling/dependency code actually lives — do NOT invent a parallel tree). If the dependency code does not exist yet, mark the path `[ASSUMED]` and add an `open_question`.
>    - **No phantom references:** every config/class/accessor the spec tells the implementer to READ (e.g. `DataManager.X`, a CSV, a helper method) must actually exist or be created by a named upstream task. Never reference an artifact that no task produces. Verify method/accessor NAMES against the real API (don't invent `GetFoo()` that isn't there).
> 3. Identify existing patterns that the implementer must follow (`FeatureBaseController`, `RedDotBadge`, `UIManager`, `TigerForge`, DOTween, `UniTask`, `PlayerDataManager.[Module]`, `DataManager` read-only).
> 3a. If the task is UI-scoped, set `required_skills` to include `/create-ui` (and `/compile-check` if that UI task touches `.cs` files). Include the exact `/create-ui` workflow constraints in completion criteria and verify steps. If UI should be split from a non-UI implementation, document that split in `ui_task_split` and keep the current task's out-of-scope clear.
> 4. Surface risks → acceptance criteria.
> 5. Apply the scope-control gate: if proposing broad changes, explain why/impact/migration/tests/checkpoints/rollback; if you cannot explain, narrow the scope or put it under `open_questions`.
> 6. Decide which guardrails apply (see `applicable_guardrails` below). For each guardrail you exclude, provide a concrete reason of ≥10 chars.
>
> Return ONE JSON object as the final message:
> ```json
> {
>   "summary": "one-sentence restatement",
>   "required_skills": [],
>   "ui_task_split": {
>     "needed": false,
>     "reason": "none | UI prefab authoring depends on controller/service from this task",
>     "suggested_followup_title": "none | Build <feature> screen prefab"
>   },
>   "files_to_touch": [{ "path": "Assets/_Project/...", "why": "..." }],
>   "pattern_to_follow": "...",
>   "scope_control": {
>     "is_broad_change": false,
>     "why_broad_change_is_needed": "none | required because ...",
>     "affected_areas": ["module/feature/system names"],
>     "migration_plan": "none | data/schema/config/save migration steps",
>     "test_regression_plan": ["specific regression/test checkpoint"],
>     "checkpoints": ["observable implementation checkpoint"],
>     "rollback_or_fallback": "none | rollback/fallback path",
>     "out_of_scope": ["things the implementer must not touch"]
>   },
>   "completion_criteria": ["observable criterion 1 (observable in Editor/build)", "..."],
>   "verify_steps": ["happy path — open scene X, do Y, confirm Z", "edge case", "regression check"],
>   "risks": ["commonly forgotten guards"],
>   "applicable_guardrails": ["pattern", "ui", "time", "save", "async", "localize", "event", "dotween", "double_submit", "loading_cooldown", "boundary", "persist_restart", "mobile_perf", "csv_config"],
>   "not_applicable": {
>     "csv_config": "no balance numbers in this task"
>   },
>   "mobile_impact": {
>     "gc_alloc": "none | hot-path-risk — mitigation: ...",
>     "apk_size": "none | new-assets — mitigation: ...",
>     "draw_call": "none | new-ui-or-vfx — mitigation: ...",
>     "save_data": "none | adds-field — mitigation: SetupDefaultData() fallback",
>     "localize": "none | new-strings — mitigation: add key via localize system",
>     "csv_config": "none | new-balance-values — mitigation: place in appropriate CSV"
>   },
>   "open_questions": []
> }
> ```
>
> Concrete details: real file paths from the repo, real class names, observable criteria. 3–7 items per list. Keep `open_questions: []` unless the intent is truly ambiguous. If there are `open_questions` affecting behavior, acceptance criteria, verification steps, save/IAP/economy/UX flow, the task is not yet permitted to be written into `backlog/planning/`.

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

Accept user edits on file lists / criteria / verify steps. Update the draft in place. **DO NOT re-spawn the Plan subagent** unless the user explicitly rejects the entire approach — and even then, only once.

---

## STEP 3 — Filename (timestamp + tier + slug)

1. Generate a UTC timestamp with millisecond precision: `YYYYMMDDTHHmmssSSS`.
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

### XS — minimal
- [ ] Title describes the specific change (not "improve X").
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
- [ ] If `scope_control.is_broad_change = true`, there must be a compelling reason, a migration plan if touching data/schema/config/save, and a specific rollback/fallback.
- [ ] UI-scoped task includes `**Required skills:** /create-ui` and concrete `/create-ui` acceptance criteria.
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
1. **Selected tier** + reason (which signal triggered it).
2. Task title, priority, and created file path (in `backlog/planning/`).
3. Required skills (if any), calling out `/create-ui` for UI-scoped tasks.
4. UI split decision (if relevant): whether this task owns prefab authoring or a separate follow-up UI task should be created.
5. **Pointer**: *"This task is in planning. When you want to queue it for `run-backlog` to run, use `/add-to-backlog` (or say 'add task to backlog')."*
6. **Guardrails skipped** (if any) + reason.
7. **Assumptions made** (if any) so the user can correct them now.
8. **Scope-control summary**: broad/not broad, affected areas, out_of_scope, rollback/fallback if any.
9. Top 3 acceptance criteria so the user can sanity-check the scope.

DO NOT commit. DO NOT modify `BACKLOG.md`. DO NOT create anything in `backlog/todo/`.
