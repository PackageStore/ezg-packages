---
name: planning-task
description: Capture a new task into backlog/planning/ with full triage + spec (DO NOT touch BACKLOG.md). Used when the user says "planning task" / "plan task" / "create planning task" / "draft task" / "create task X". Multiple agents can run in parallel — the filename uses a timestamp, so it is unique. To PICK a planning task into BACKLOG.md, use /add-to-backlog. When intent is unclear between the two skills, confirm with the user first.
---

# Planning Task — Capture Agent

Turn a user request into a fully specified task file in `backlog/planning/`, allowing multiple Claude windows to capture tasks in parallel without overwriting each other. The task is NOT yet queued for `run-backlog` — that only happens when the user selects the task via `/add-to-backlog`.


> **Where the backlog lives.** Every `backlog/...` path in this document is relative to
> the backlog root, which is **NOT in the worktree**:
>
> ```bash
> BACKLOG_ROOT="$(git rev-parse --git-common-dir)/backlog"   # i.e. <repo>/.git/backlog
> ```
>
> It is per-developer bookkeeping — never committed, never merged (tracking it made every
> dev branch carry its own index and collide on merge), and shared automatically by every
> `git worktree` of the clone. So: **write task files to `$BACKLOG_ROOT/planning/`, glob
> from there, and NEVER `git add` / `git mv` / commit anything under it.** If the
> directory does not exist yet, run `python3 .claude/scripts/backlog-ops.py init` first.

The backlog uses a **split-file layout**:
- `backlog/planning/<timestamp>-<TIER>-<slug>.md` = drafted, not yet queued (this skill writes here)
- `BACKLOG.md` = index of queued tasks (only `/add-to-backlog` modifies this)
- `backlog/todo/NNN-<TIER>-<slug>.md` = queued task (created by `/add-to-backlog` when picking from planning)
- `backlog/in-progress/`, `backlog/done/` = managed by `run-backlog`

You create **one new file** in `backlog/planning/` — with **one exception**: a task that builds a **NEW UI screen** is split into a sibling `/new-ui` screen task + the logic task (STEP 2 HYBRID/M-L block), so the planning session writes **two** files. You **DO NOT** touch `BACKLOG.md` and **DO NOT** create files in `backlog/todo/`.

> **Drafting ahead of dependencies?** If this task (or a wave of tasks) is being spec'd against code that does not exist yet, mark any not-yet-real path/class with `[ASSUMED]` and raise it as an `open_question`. Before such specs are promoted via `/add-to-backlog`, run them through `.claude/backlog-templates/_REVALIDATION-PLAYBOOK.md` (the 4 checks: duplicate / stale-path / phantom / deps) so `/run-backlog` does not hit a hard blocker. Prefer spec'ing just-in-time (after dependencies ship) to keep this light.

---

## Core principle: clarity-first, right-size the pipeline

**A planning task is an implementation contract, not a rough idea.** Prioritize understanding the correct intent over saving tokens. Do not run a full M/L pipeline for a CSV tweak, but also do not guess decisions that could lead `run-backlog` to implement in the wrong direction.

```
[0] TRIAGE       → (0b) NEW-SYSTEM? dispatch /planning-system → (0a) workflow-backed? → classify XS / S / M / L (≤500 tokens)
[1] EXTRACT      → parse user intent + clarify until contract is clear
[2] DRAFT        → tier-specific (skip task-planner for XS/S, simple-M, AND workflow-backed scaffolds)
[3] FILENAME     → timestamp + tier + slug (no NNN, no race check)
[4] WRITE        → fill tier-specific OR _TEMPLATE_WF template + conditional guardrails
[5] CHECK        → tier-aware quality check
[6] REPORT       → summarize for user, point to /add-to-backlog
```

---

## STEP 0b — New-system detection (chạy TRƯỚC 0a, nhưng 0a là tie-break)

Một số intent không phải là "một task" mà là **cả một hệ thống mới** (GDD/design doc nhiều phần). Viết một task M/L duy nhất cho nó = mất kiểm soát scope; đúng đường là **pipeline thiết kế** `/planning-system` (design-validate → mapping → batch-ground thành N task planning). Skill này chỉ **phát hiện và dispatch** — mọi cơ chế nằm trong [.claude/skills/planning-system/SKILL.md](../planning-system/SKILL.md).

**0b CHỈ fire khi thỏa CẢ HAI:**

1. **Quy mô doc/hệ thống** — ít nhất một trong:
   - Input là cả một GDD/design doc nhiều section (không phải một thay đổi scoped), hoặc user ném file doc và bảo "làm feature này".
   - Intent tạo **≥2 feature module MỚI tương tác với nhau** (không phải sửa module có sẵn).
2. **KHÔNG diễn đạt được thành MỘT task workflow-backed** — chạy registry match của STEP 0a trước như tie-break: nếu intent khớp trọn một dòng registry (vd. một package IAP lẻ → `/new-package`, một màn UI lẻ → `/new-ui`) thì **0b NHƯỜNG 0a**, kể cả khi intent chạm economy/IAP. Chạm economy đơn thuần KHÔNG đủ để kích 0b — nó chỉ là tín hiệu tier (auto-bump M) như hiện tại.

**Idempotency (bắt buộc probe trước khi dispatch):** Glob `TechSpec/<FeatureName>-*.md`. Nếu artifact đã tồn tại → hệ thống này từng qua pipeline; KHÔNG chạy lại từ đầu — dispatch `/planning-system` ở **chế độ resume** (`--from-mapping TechSpec/<FeatureName>-Implementation.md` nếu mapping đã có, hoặc từ stage đầu tiên còn thiếu). Nói rõ với user đang resume từ stage nào.

**Chống đệ quy (bắt buộc):** nếu prompt/ngữ cảnh hiện tại có flag `origin: planning-system` (batch mode — orchestrator đang tái sử dụng drafting path của skill này), **BỎ QUA HOÀN TOÀN 0b và 0a-dispatch**, vào thẳng STEP 2. `/planning-system` không bao giờ được dispatch chính nó (depth cap = 1).

**Khi 0b fire:** KHÔNG viết task nào, KHÔNG hỏi tier. Thông báo user một câu ("Intent này là hệ thống mới lớn → chuyển qua pipeline thiết kế /planning-system") rồi thực thi `/planning-system` với input doc. Khi 0b không fire → tiếp tục STEP 0a bên dưới như bình thường.

---

## STEP 0a — Workflow-backed detection (run FIRST, before tier triage)

Some `/new-*` workflows in `.claude/commands/` already specify a scaffold **deterministically** (which files, which registrations, which conventions). For those, the workflow IS the plan — spending a `task-planner` subagent (~15–25K tokens) to re-discover what the workflow states verbatim is pure waste. Detect these first and route around `task-planner`.

**Workflow registry** (match the user's intent against these):

| Intent signal | Workflow | Exec tier (for review-gating) | Sensitive? |
|---|---|---|---|
| Create a new **feature module** (controller + manager, optional save/CSV) | `/new-feature` | M (L if it adds a save field + cross-system events) | yes if it adds a `DataPlayer` field |
| Create a new **IAP package** (PascalCase, ends with `Pack`) | `/new-package` | M | **yes** (IAP/purchase) |
| Create a new **UI prefab** from `FeatureTemplate.prefab` | `/new-ui` | S–M | no |
| Create a new **class** following `FeatureBaseController` conventions | `/new-class` | S | no |

> `/add-localize` is **not** a standalone WF task: it writes to a Google Sheet and produces **no local git diff**, so `run-backlog` would stop with `NO_CHANGES`. Treat localize as a sub-step folded into the parent feature task (the workflow / custom delta references it), never as its own backlog task.

> `/new-ui` tasks carry a **mockup ground truth** in `**Workflow args:**` (`FeatureName | groundTruth=...`) — resolved in STEP 2 item 2b via the mockup pipeline (`mockup-drafter` subagent + automatic `ui-review.py auto-approve`). Building UI from a text description alone is the documented main visual-failure mode (new-ui-guide.md §0a); the mockup is the cheap-medium design pass the Unity build later copies.

**Decision:**

1. **No registry match** → not workflow-backed. Go to STEP 0 (normal tier triage).
2. **Match + PURE scaffold** (the deliverable is exactly what the workflow generates, no extra logic, no cross-system wiring beyond what the workflow documents):
   - **Skip `task-planner`.** Assign the **exec tier** from the registry column (this only drives review-gating in `run-backlog`; it is NOT discovered by an agent).
   - Read **only the one matched workflow file** (~1–3K tokens) to lift its checklist into acceptance criteria. **Do NOT grep the codebase, do NOT spawn any subagent.**
   - Draft with `_TEMPLATE_WF.md` (STEP 2 → "Workflow-backed path"). `**Custom delta:**` = `none`.
3. **Match + HYBRID** (scaffold **plus** custom logic the workflow does not cover — e.g. "new skill AND wire it into evolution pool X and rebalance CSV Y"):
   - Run normal tier triage (STEP 0) → almost always M or L.
   - Spawn `task-planner` (STEP 2 M/L) but **scope it to the delta only**: tell it the scaffold is handled by `/new-xxx` and it must NOT re-plan the workflow's files — only plan the extra wiring/logic/balance.
   - The resulting M/L draft MUST carry a `**Backed by workflow:** /new-xxx` + `**Workflow args:** ...` line so `run-backlog` still loads the workflow first.
   - **If the HYBRID also builds a NEW UI screen** (authors a new `Resources/*.prefab` popup/panel) → **split the screen into its own `/new-ui` task** (STEP 2 HYBRID/M-L block). Do NOT bundle screen authoring into the logic task — the split is what routes the screen through the mockup draft+approval pass.

In both WF cases, record the matched workflow + args so STEP 4 can write them into the task file.

---

## STEP 0 — Triage (always perform first)

Classify the task into one of the four tiers using **concrete signals**, not gut feeling. **When in doubt, choose the LARGER tier** — review gates in `run-backlog` are keyed to the tier and are never escalated automatically at execution time, so an under-tiered task silently skips reviewers, while an over-tiered one only costs a little more review.

| Tier | Signals (any single match) | Pipeline cost |
|---|---|---|
| **XS** | CSV tweak / constant adjust / dead-code removal / rename variable in 1 file. No new logic. | ~1K tokens, no subagent |
| **S** | Single-file logic tweak. No new UI screen / new save field / new event. ≤2 files. | ~3K tokens, no subagent |
| **M** | Multi-file feature. New UI screen/popup, new controller, new save field, new TigerForge event. 3–8 files. | ~15K tokens, task-planner subagent **only if complex** (simple M drafts in main context — see STEP 2) |
| **L** | Cross-cutting: new IAP/purchase flow, new backend surface, save data migration, skill system integration, or 9+ files. | ~25K tokens, task-planner subagent + risk pass |

**Auto-bump rules** (override to a higher tier if any signal matches):
- Touches `Purchase*`, `IAP*`, `Receipt*`, `Payment*` → at least M.
- Adds new `DataPlayer` field or save module → at least M.
- Adds new TigerForge event cross-system → at least M.
- Adds new Cloudflare Worker endpoint or Supabase table → at least M.
- Touches `Backend*`, `Auth*`, `Token*`, `Session*` → at least M.
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
- Backend, auth, security, leaderboard, social, IAP, purchase, and receipt.
- Acceptance criteria or manual verification steps.

You may only assume low-risk details that do not change the outcome (e.g., slug name, title phrasing, expected file paths after grep/read). All other assumptions must be documented clearly in the task, but **assumptions must not be used to replace questions** when ambiguity affects behavior or completion criteria.

For **XS/S**, you can stop clarifying once the small change is clear and the rest is implementation detail. For **M/L**, do not create a planning task if there are remaining `open_questions` affecting the contract; continue asking the user or state clearly that the task is blocked due to missing decisions.

Do not ask questions that can be answered by grepping/reading the codebase.

---

## STEP 2 — Draft (tier-specific)

### Workflow-backed path (PURE scaffold from STEP 0a) — no subagent, no codebase grep

This is the token-saving path. Do **NOT** spawn `task-planner`. Do **NOT** grep/read feature code.

1. Read **only** the matched workflow file in `.claude/commands/` (e.g. `new-package.md`). Every row in the registry above must resolve to a real file there — if it does not, the row is stale: report it instead of routing to it.
2. Build the `**Workflow args:**` string in the exact format that workflow's arg parser expects (e.g. `/new-package` wants `PackageName: Description`, PascalCase ending in `Pack`; `/new-feature` wants `FeatureName: Description`).

   **2b — `/new-ui` only: resolve `groundTruth` (mockup pipeline).** Append ` | groundTruth=<value>`:
   - User supplied a reference image path → that path (`Read` once to confirm it exists).
   - The screen clones an existing prefab's layout → `clone:<ExistingPrefab>`.
   - Otherwise → spawn [`mockup-drafter`](../../agents/mockup-drafter.md) (featureName / branch / outputPath `TechSpec/Mockups/<F>/<S>.html` + intent/context docs). `created`/`recovered`/`exists` (validated v1 pair) or `legacy-exists` (validated legacy HTML), with returned HTML confirmed present ⇒ `PENDING-APPROVAL:<path.html>`; `error` ⇒ `PENDING-MOCKUP`. **Never block planning on drafting.**
   - **Generate = autonomous, approve = AUTOMATIC:** after the task file is written (STEP 4), run `python3 .claude/scripts/ui-review.py auto-approve --task backlog/planning/<task>.md` — it freezes the draft to its PNG contract and flips `groundTruth` → `.png` with no human round. Screens left `pending` (open drafter `questions[]` / `[?]` placeholders / no Chrome) stay `PENDING-APPROVAL`; report them so the dev can settle them via `/ui-mockup` (dashboard). The drafter writes ONLY its own screen pair (parallel-safe); `backlog-ops.py promote` blocks while the value is still `PENDING-*`.
3. Lift the workflow's `CHECKLIST` / `FINAL CHECKLIST` into acceptance criteria.
3b. **Cheat decision (`/new-feature`, `/new-package`, `/new-ui` only).** These scaffolds produce a prefab that already inherits `ButtonCheatMenu` from `FeatureTemplate`/`PackageTemplate`, so a dev cheat costs only the `Cheat_*` methods + 2–5 buttons. If the feature has state a tester cannot reach in a few taps (time gate / daily reset / cooldown, progression or unlock threshold, resource requirement, rare or one-shot flow, anything needing a reset) → add `[CHEAT]` to `**Guardrails:**`, name the concrete cheats in `**Custom delta:**` (this is a real delta — the workflow does not invent them), and add an acceptance criterion. Otherwise write the reason into `**Guardrails skipped:**` (e.g. `cheat (static info popup, drivable from its own UI)`). Design + code pattern: [.claude/skills/feature-cheat/SKILL.md](../feature-cheat/SKILL.md) — mirror `DailyLoginV2.prefab` / `Equipment.prefab`.
4. Draft with `_TEMPLATE_WF.md` (STEP 4). `**Custom delta:**` = `none — pure scaffold` (or the cheat list from 3b).
5. Still resolve any contract-affecting `open_questions` with the user first (e.g. the exact skill ID, the IAP product-id suffix, the gameplay rule the scaffold must satisfy) — a deterministic workflow still needs correct inputs.

Then go straight to STEP 3. Skip the M/L subagent flow below.

### Tier XS — no exploration needed

Write the task directly from the user's message + your knowledge of the repo. No task-planner subagent, no Grep. The task is so small that scoping it is faster than analyzing it.

### Tier S — light exploration in the main context

Use Grep + Read on 1–3 files to confirm file paths and patterns. **DO NOT** spawn a task-planner subagent. Then draft directly.

### Tier M (simple) — main-context draft, NO task-planner subagent

Spawn the opus `task-planner` subagent **only when the M task is genuinely complex**. Many M tasks are bumped to M purely for scope (3–8 files) but are mechanically simple — an opus subagent is wasted tokens for those.

Draft in the **main context** (1–2 Grep/Read passes to confirm paths and patterns, then write the spec yourself) when the M task is **simple**, i.e. ALL of:
- A single new save field/module, OR a single new controller/screen, OR a localized set of edits in 3–8 files following one obvious existing pattern.
- No cross-module runtime flow being newly wired.
- No migration/reshaping of existing saved data.
- No open questions affecting the contract.

Escalate to the **task-planner subagent** (next section) when the M task is **complex**: multiple subsystems interact, a non-obvious pattern decision is needed, the dependency graph is unclear, or you cannot confidently list the files to touch after 1–2 Grep/Read passes.

When drafting in the main context, produce the same JSON fields the `task-planner` subagent would (see the schema in [.claude/agents/task-planner.md](../../agents/task-planner.md)) so STEP 4 can fill the template identically.

### Tier M (complex) / L — task-planner subagent

> **HYBRID (scaffold + custom logic, from STEP 0a case 3):** if the task is partly a `/new-*` scaffold, scope the subagent to the **delta only**. Add to the prompt body: *"The scaffold (files, registrations, conventions) is handled by `/new-xxx` — do NOT plan or list those files; plan ONLY the custom logic/wiring/balance beyond the workflow."* Then add a `**Backed by workflow:** /new-xxx` + `**Workflow args:** ...` line into the final M/L draft so `run-backlog` loads the workflow before applying the delta. Pure scaffolds never reach here — they use the Workflow-backed path above.
>
> **A HYBRID/M/L task that builds a NEW UI screen** (authors a new `Resources/*.prefab` popup/panel, e.g. a `/new-feature` scaffold with a custom popup) — **SPLIT the screen into its own `/new-ui` task.** Do NOT bundle screen authoring into the logic task: a bundled screen never spawns `mockup-drafter`, so `/planning-task` produces a task with no draft to approve (the exact bug this fixes). The split mirrors `/planning-system`'s UI handling and routes the screen through **item 2b** above.
>
> Concretely, the planning session writes **two** files:
> 1. **Screen task** — a `/new-ui` task (`_TEMPLATE_WF.md`, `**Backed by workflow:** /new-ui`, `**Workflow args:** <Screen> | groundTruth=<value>`). Draft this one FIRST so its timestamp sorts ahead. Because it is `/new-ui`, STEP 2 **item 2b runs**: `mockup-drafter` is spawned **at planning time** → the draft is then auto-approved to its PNG contract (item 2b), so the task is promote-ready without a human round.
> 2. **Logic task** — the HYBRID/M/L task with the custom logic/wiring, carrying `**Backed by workflow:** /new-xxx` (if any) + `**Depends on:** <screen-task planning filename>` so `promote` queues the screen ahead of it.
>
> **Escape hatch** (screen genuinely inseparable from the logic prefab, e.g. you only re-wire an existing screen — no new visual): keep one task, list `/new-ui` in `**Required skills:**`, and carry a wired marker line `**Mockup:** groundTruth=PENDING-MOCKUP (screen=<Feature>/<Screen>)`, or `groundTruth=none — <reason>` when there is truly no new visual to design. The `groundTruth=` token works on ANY real line (not just `**Workflow args:**`); `/ui-mockup` greps it and `promote` blocks on it.
>
> **Do NOT use the legacy `Needs mockup: yes` string — nothing sweeps it** (that was the silent-skip bug). **Backstop (both paths):** `backlog-ops.py promote` hard-blocks any task that references `/new-ui` but carries no `groundTruth=` token at all, so a forgotten split or marker fails loudly at `/add-to-backlog` instead of silently skipping the gate.

Spawn the **`task-planner`** subagent. Its full brief — steps, the JSON schema it must return, and project conventions — lives in [.claude/agents/task-planner.md](../../agents/task-planner.md) so it can be edited independently of this skill. You pass **only the dynamic context** in the prompt:

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

The agent returns ONE JSON object (schema defined in [.claude/agents/task-planner.md](../../agents/task-planner.md)). If it returns `open_questions` affecting behavior, acceptance criteria, verification steps, or save/backend/IAP/security/economy/UX flow, the task is **not yet permitted** to be written into `backlog/planning/` — resolve them with the user first (see 2b).

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

Accept user edits on file lists / criteria / verify steps. Update the draft in place. **DO NOT re-spawn the task-planner subagent** unless the user explicitly rejects the entire approach — and even then, only once.

---

## STEP 3 — Filename (timestamp + tier + slug)

1. Get the UTC millisecond timestamp from the ops script — deterministic, **never hand-generate or guess it**:
   ```bash
   python3 .claude/scripts/backlog-ops.py timestamp   # → YYYYMMDDTHHmmssSSS
   ```
   Example output: `20260523T142301456`
2. Tier from STEP 0: `XS` | `S` | `M` | `L`.
3. Slug: 2–5 kebab-case words from the task title.
4. Final filename:
   ```
   <timestamp>-<TIER>-<slug>.md
   ```
   Example: `20260523T142301456-M-glory-pass-sprint-offer.md`

**No NNN. No folder scanning. No race check.** Timestamp + millisecond is unique per agent instance.

**Edge case** (clock skew or agent retry in the same ms): if the filename already exists, append `-r1`, `-r2`, etc.

The file goes into `backlog/planning/`, **never** `backlog/todo/`.

---

## STEP 4 — Write task file

Pick template based on tier — **unless the task is a PURE workflow-backed scaffold** (STEP 0a case 2), in which case use `_TEMPLATE_WF.md` regardless of tier:

| Case | Template file |
|---|---|
| PURE workflow-backed scaffold | `.claude/backlog-templates/_TEMPLATE_WF.md` |
| XS | `.claude/backlog-templates/_TEMPLATE_XS.md` |
| S  | `.claude/backlog-templates/_TEMPLATE_S.md` |
| M (incl. HYBRID workflow-backed) | `.claude/backlog-templates/_TEMPLATE_M.md` |
| L (incl. HYBRID workflow-backed) | `.claude/backlog-templates/_TEMPLATE_L.md` |

**Filename tier:** the `<TIER>` in the filename is always the **exec tier** (`XS`/`S`/`M`/`L`) — for WF tasks use the exec tier from the STEP 0a registry. Never put `WF` in the filename; WF-ness lives in the `**Backed by workflow:**` body field so `run-backlog` review-gating stays tier-driven.

**Body tier (source of truth):** every template has a `**Tier:** X` line right under the title. Fill it with the exec tier you chose (must match the `<TIER>` in the filename). This is what `run-backlog` reads first to gate its quality gates — the BACKLOG.md bullet `[Tier]` added later by `/add-to-backlog` is only a mirror. Do not omit this line.

For workflow-backed tasks (pure or hybrid), fill `**Backed by workflow:**` and `**Workflow args:**` with the values resolved in STEP 0a/2.

**Conditional guardrail rule** (tag-based — definitions live in `.claude/backlog-templates/_GUARDRAILS.md`, NOT pasted into the task):
- Write a single `**Guardrails:**` line listing ONLY the applicable tags (uppercase, bracketed, space-separated), derived from the task-planner's `applicable_guardrails`. Example: `**Guardrails:** [SAVE] [ASYNC] [LOCALIZE]`. Map each `applicable_guardrails` value to its tag (`backend_security` → `[BACKEND-SECURITY]`, `android_build` → `[ANDROID-BUILD]`, `double_submit` → `[DOUBLE-SUBMIT]`, `loading_cooldown` → `[LOADING/COOLDOWN]`, `persist_restart` → `[PERSIST-RESTART]`, `mobile_perf` → `[MOBILE-PERF]`, `csv_config` → `[CSV-CONFIG]`, `cheat` → `[CHEAT]`, etc.).
- DO NOT paste the full guardrail blocks/verify recipes into the task file — they are duplicated in every reviewer prompt and bloat tokens. The tag is enough; reviewers + qa-verifier look it up in `.claude/backlog-templates/_GUARDRAILS.md`.
- `**Guardrails skipped:**` should only call out a guardrail a reader might *expect* to apply but you deliberately excluded, with a `not_applicable` reason of ≥10 chars (e.g. `backend_security (no backend write)`). If nothing is surprising, write `none`. Do NOT enumerate every unused tag.
- If `applicable_guardrails` is missing or a reason is "n/a" / empty / <10 chars → include that tag by default. Safer to over-include.

Write the file to `$BACKLOG_ROOT/planning/<filename-from-STEP-3>.md` (resolve `$BACKLOG_ROOT` with `git rev-parse --git-common-dir` — the path is outside the worktree, so a plain relative `backlog/planning/...` write would silently create a stray directory).

---

## STEP 5 — Quality check (tier-aware)

Run only the checks for the current tier:

### WF — workflow-backed (pure scaffold)
- [ ] `**Backed by workflow:**` names a real file in `.claude/commands/`.
- [ ] `**Workflow args:**` is in the exact format that workflow's arg parser expects (e.g. `Id: Description`, PascalCase + `Pack` for packages, ID in the correct range for skills).
- [ ] Optional batch fields (`**Context docs:**` / `**Depends on:**` / `**Requires:**`) are either filled with REAL values or DELETED — never left as template placeholders (a leftover `**Requires:** unity-editor` placeholder makes run-backlog defer a fully headless task).
- [ ] Acceptance criteria include "workflow checklist fully satisfied" + any delta criteria.
- [ ] No contract-affecting `open_questions` remain (skill ID, IAP product-id, gameplay rule the scaffold must satisfy).
- [ ] `**Custom delta:**` is `none` — if it is NOT, this should have been a HYBRID M/L task, not a pure WF task. Re-route. **One exception:** a cheat list from STEP 2 item 3b may sit in `**Custom delta:**` and still be a pure WF task — cheats are a dev affordance on the scaffold's own prefab/controller, not cross-system logic.
- [ ] `/new-feature` / `/new-package` / `/new-ui` task: the **cheat decision** from STEP 2 item 3b is visible — either `[CHEAT]` on `**Guardrails:**` + the cheats named in `**Custom delta:**`, or `**Guardrails skipped:** cheat (<reason ≥10 chars>)`.
- [ ] `/new-ui` task: `**Workflow args:**` carries `groundTruth=` with one of the four legal values (approved `.png` / `PENDING-APPROVAL:<...>.html` / `PENDING-MOCKUP` / `clone:<Prefab>`), resolved per STEP 2 item 2b.
- (HYBRID tasks run the M/L checks below **plus** the first two checks here.)

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
- [ ] A single `**Guardrails:**` tag line exists in the draft (tags only, no pasted block text) and matches the task-planner's `applicable_guardrails`.
- [ ] `**Guardrails skipped:**` lists only deliberately-excluded tags a reader might expect, each with a reason ≥10 chars (or `none`).
- [ ] Mobile impact — GC alloc / APK size / draw call / save data / localize / backend write / CSV: each axis is evaluated, included, or justified.
- [ ] Verify steps cover (1) happy path, (2) edge case, (3) regression check on the related feature.
- [ ] At least 3 manual verification steps in the `**Required verification steps**` section.
- [ ] **Cheat decision made explicitly** — either `[CHEAT]` is on the `**Guardrails:**` line WITH an acceptance criterion naming the concrete cheat buttons, or `**Guardrails skipped:**` carries `cheat (<reason ≥10 chars>)`. Silence is a fail. A verify step containing "wait until tomorrow" / "reach level N" / "after the reset" with no cheat is also a fail — that is exactly the state a cheat exists to reach ([.claude/skills/feature-cheat/SKILL.md](../feature-cheat/SKILL.md)).
- [ ] **If the task builds a NEW UI screen** (authors a new `Resources/*.prefab` popup/panel): the screen was **split into its own `/new-ui` task** (which carries `groundTruth=` and got a `mockup-drafter` draft at planning time), and the logic task `**Depends on:**` it. Only if the screen is genuinely inseparable does it stay bundled with a `**Mockup:** groundTruth=...` marker (escape hatch). Either way — a task referencing `/new-ui` with no `groundTruth=` token is a hard `promote` blocker.

### L — full + phases
- [ ] All M checks pass.
- [ ] Task has a `**Phases:**` section dividing work into ≤4 sequential sub-steps with explicit checkpoints.
- [ ] Risk section details cross-cutting impacts and what could break.
- [ ] Controlled broad changes: each affected area has a checkpoint/regression check; rollback/fallback is clear enough for the user to decide whether to queue the task.

If any check fails, fix the draft before STEP 6.

---

## STEP 6 — Report

Report to the user, in order:
1. **Selected tier** + reason (which signal triggered it). If workflow-backed, also state **Backed by workflow** (`/new-xxx`) + the resolved **Workflow args**, and whether it is **pure** (task-planner skipped — note the token saving) or **hybrid** (task-planner planned the delta only).
2. Task title, priority, and created file path(s) (in `backlog/planning/`). **If a UI screen was split off**, report BOTH files — the `/new-ui` screen task and the logic task — note the `Depends on` link, and the mockup state: **approved** (auto-approve froze the PNG), `PENDING-APPROVAL` (auto-approve skipped — say why: open questions / `[?]` / no Chrome — and point to `/ui-mockup`), or `PENDING-MOCKUP` (drafter failed — say so).
3. **Pointer**: *"This task is in planning. Use `/add-to-backlog` to queue it."* Only mention `/ui-mockup` when a mockup is still `PENDING-*`.
4. **Guardrails skipped** (if any) + reason.
5. **Assumptions made** (if any) so the user can correct them now.
6. **Scope-control summary**: broad/not broad, affected areas, out_of_scope, rollback/fallback if any.
7. Top 3 acceptance criteria so the user can sanity-check the scope.

DO NOT commit. DO NOT modify `BACKLOG.md`. DO NOT create anything in `backlog/todo/`.
