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

```
[0] TRIAGE       → classify XS / S / M / L (≤500 tokens)
[1] EXTRACT      → parse user intent + clarify until contract is clear
[2] DRAFT        → tier-specific (skip Plan subagent for XS/S)
[3] FILENAME     → timestamp + tier + slug (no NNN, no race check)
[4] WRITE        → fill tier-specific template + conditional guardrails
[5] CHECK        → tier-aware quality check
[6] REPORT       → summarize for user, point to /add-to-backlog
```

---

## STEP 0 — Triage (always perform first)

Classify the task into one of the four tiers using **concrete signals**, not gut feeling. When in doubt, choose a smaller tier — `run-backlog` can escalate later if scope creep occurs during implementation.

| Tier | Signals (any single match) | Pipeline cost |
|---|---|---|
| **XS** | CSV tweak / constant adjust / dead-code removal / rename variable in 1 file. No new logic. | ~1K tokens, no subagent |
| **S** | Single-file logic tweak. No new UI screen / new save field / new event. ≤2 files. | ~3K tokens, no subagent |
| **M** | Multi-file feature. New UI screen/popup, new controller, new save field, new TigerForge event. 3–8 files. | ~15K tokens, Plan subagent |
| **L** | Cross-cutting: new IAP/purchase flow, save data migration, new system integration, or 9+ files. | ~25K tokens, Plan subagent + risk pass |

**Auto-bump rules** (override to a higher tier if any signal matches):
- Touches `Purchase*`, `IAP*`, `Receipt*`, `Payment*` → at least M.
- Adds new `DataPlayer` field or save module → at least M.
- Adds new TigerForge event cross-system → at least M.
- Touches `Auth*`, `Token*`, `Session*` → at least M.
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

Parse the user's message for: **What**, **Why** (if any), **Scope**, **Priority** (default `MEDIUM`), and **Constraints**.

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

### Tier M / L — Plan subagent

Spawn a Plan subagent with `subagent_type: "Plan"`. Brief as follows:

> You are drafting a backlog task spec for the **Merge Two** project (Unity mobile merge-grid game, C#, primary target is Android).
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
> 3. Identify existing patterns that the implementer must follow (`FeatureBaseController`, `RedDotBadge`, `UIManager`, `TigerForge`, DOTween, `UniTask`, `PlayerDataManager.[Module]`, `DataManager` read-only).
> 4. Surface risks → acceptance criteria.
> 5. Apply the scope-control gate: if proposing broad changes, explain why/impact/migration/tests/checkpoints/rollback; if you cannot explain, narrow the scope or put it under `open_questions`.
> 6. Decide which guardrails apply (see `applicable_guardrails` below). For each guardrail you exclude, provide a concrete reason of ≥10 chars.
>
> Return ONE JSON object as the final message:
> ```json
> {
>   "summary": "one-sentence restatement",
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

**Conditional guardrail rule** (applied to M/L using `applicable_guardrails` from Plan subagent):
- Include guardrail blocks ONLY WHEN the guardrail appears in `applicable_guardrails`.
- For each excluded guardrail, the Plan subagent must provide a `not_applicable` reason of ≥10 chars. Append these reasons at the end of the task file:
  ```
  **Guardrails skipped:** csv_config (no balance numbers), mobile_perf (no hot path changes).
  ```
- If `applicable_guardrails` is missing or the reason is empty / <10 chars → include that guardrail by default. Safer to over-include.

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
- [ ] If there is user input, include the [BOUNDARY] guardrail.
- [ ] If there is a user-facing mutation, include [DOUBLE-SUBMIT] + [LOADING/COOLDOWN].

### M — full
- [ ] All S checks pass.
- [ ] `open_questions` is empty or only contains low-risk implementation details that do not change the outcome.
- [ ] `scope_control` has all fields: broad/not broad, affected areas, out_of_scope, test/regression plan, checkpoints, rollback/fallback.
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
1. **Selected tier** + reason (which signal triggered it).
2. Task title, priority, and created file path (in `backlog/planning/`).
3. **Pointer**: *"This task is in planning. When you want to queue it for `run-backlog` to run, use `/add-to-backlog` (or say 'add task to backlog')."*
4. **Guardrails skipped** (if any) + reason.
5. **Assumptions made** (if any) so the user can correct them now.
6. **Scope-control summary**: broad/not broad, affected areas, out_of_scope, rollback/fallback if any.
7. Top 3 acceptance criteria so the user can sanity-check the scope.

DO NOT commit. DO NOT modify `BACKLOG.md`. DO NOT create anything in `backlog/todo/`.
