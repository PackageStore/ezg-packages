---
name: run-backlog
description: Autonomous backlog agent for a Unity project — pick the first task in TODO, implement it, run tiered quality gates with auto-fix, mark it DONE, and commit + push to agent/dev when a remote exists. DO NOT create PRs.
---

# Run Backlog — Autonomous Task Agent

You are the autonomous implementer and orchestrator for the current Unity project. Pick the first backlog task, implement it against the repository's real rules and code, pass the tiered gates, mark it DONE, and commit to `agent/dev` (pushing only when a remote exists).

Follow these steps **precisely**.

The backlog uses a **split-file layout** to keep token usage flat:
- `BACKLOG.md` = short index (the only file you read for the "directory")
- `backlog/planning/` = drafted-but-not-queued tasks; **ignore**
- `backlog/todo/NNN-slug.md` = one file per queued task (full details)
- `backlog/in-progress/NNN-slug.md` = task currently in progress
- `backlog/done/NNN-slug.md` = completed tasks (summary)

You read the index + **exactly one** task file — never scan all tasks.

Pipeline orchestration:
```
[1]   PICK     → backlog-ops pick: resolve the task from the index (todo | in-progress resume | empty → pause)
[1b]  REQUIRES → task declares **Requires:** unity-editor but no live Editor → backlog-ops defer (or EDITOR_REQUIRED pause)
[2]   BRANCH   → switch to agent/dev + merge $BASE_BRANCH into it (create from $BASE_BRANCH if it doesn't exist yet)
[3]   START    → backlog-ops start: todo → in-progress + bullet move (on agent/dev)
[4]   CONTEXT  → read .agents/rules/* + task file + relevant code
[5]   IMPLEMENT→ write code, git add + 3-tier compile check (DO NOT commit yet)
[6]   REVIEW   → deterministic preflight, then spawn code-reviewer + (performance-reviewer IF perf-sensitive) + (security-auditor IF sensitive files) in parallel
               → auto-fix max 2 rounds if blocked; fix rounds use preflight + delta diff when enough context is provided
[7]   VERIFY   → spawn qa-verifier; auto-fix max 2 rounds if failed; final preflight
[7.5] SMOKE    → runtime smoke gate (M/L, orchestrator-side, Unity MCP): play mode + console assert + screenshot
[8]   DONE     → backlog-ops done: in-progress → done + bullet removal, write summary with all gate verdicts
[9]   SHIP     → backlog-ops lint, then commit + push to agent/dev (DO NOT create a PR)
[10]  REPORT   → summarize for user, including manual verification steps
```

> **Deterministic bookkeeping:** every backlog state transition (pick / start / done / index edits) runs through `python3 .agents/scripts/backlog-ops.py` — NEVER hand-edit `BACKLOG.md` for a transition. Hand-edited bookkeeping has already corrupted the index (leaked tool-call markup, dual-state task files, forbidden DONE bullets); the script self-lints after every mutation.

---

## STEP 1 — Read index and pick task

Resolve the task deterministically (do NOT parse `BACKLOG.md` yourself):

```bash
python3 .agents/scripts/backlog-ops.py pick
# → JSON: {state, resume, nnn, tier, priority, title, path} — or {state: "empty"} (exit code 2)
```

- `state: "in-progress"` (`resume: true`) → **resume** that task. Read the file at `path`.
- `state: "todo"` → the first TODO entry. Note `path`, `nnn`, and `tier`.
- `state: "empty"` → backlog is empty. Run the **self-pause flow**:
  1. Write the local operational state `PAUSED` into `.agents/state` (do not commit or push it; no task branch has been selected yet).
  2. Stop and output: `TODO is empty — agent paused. Add tasks via /planning-task then /add-to-backlog, then re-run.`

Then read **exactly one** identified task file. DO NOT read other task files.

Extract from the task file:
- Task title and priority
- **Task tier** — the `tier` field from the `pick` JSON (sourced from the BACKLOG.md bullet, NOT the filename) — store as `$TASK_TIER`, used for the tier guard in STEP 5c.
- **Backed by workflow** — if the task body has a `**Backed by workflow:** /new-xxx` line, store `$WF_CMD = /new-xxx`, `$WF_ARGS` from the `**Workflow args:**` line, and `**Custom delta:**`. This routes implementation through STEP 5.0 (workflow-backed shortcut). If absent, `$WF_CMD = none` (normal free-form implement).
- **Context docs** — if the task body has a `**Context docs:**` line (batch tasks from `/planning-system`), store the paths as `$CONTEXT_DOCS`. These are design docs (typically `TechSpec/<Name>-Implementation.md` + `-TechSpec.md`) holding the concrete values (Domain bucket, Manager Type, CSV columns, economy numbers, event tables) the task was planned from — read them in STEP 5/5.0. If absent, `$CONTEXT_DOCS = none`.
- **Requires** — if the task body has a `**Requires:**` line, run the **requires gate** (1b below) BEFORE STEP 2 (the task is still in todo/ — defer is only possible pre-start). For this and the other optional fields (`**Context docs:**`, `**Depends on:**`): ignore occurrences inside HTML comments (`<!-- ... -->`) — those are template leftovers, not declarations.
- **Description** (what to do and why)
- **Context & Constraints**
- **Related files** (files to read first)
- **Acceptance criteria** (exit conditions)
- **Manual verify steps** — will be copied verbatim into the DONE summary for the user.

### 1b. Requires gate (only when the task declares `**Requires:**`)

Currently one requirement value exists: `unity-editor` — the task cannot run headless (e.g. `/new-ui` prefab authoring).

1. Probe Unity MCP: `mcp__unity__unity_list_instances`. A live Editor instance for THIS project → requirement satisfied; continue to STEP 2.
2. No live Editor (probe fails / times out / returns no instance):
   - Check whether any OTHER task remains that could run headless: `grep -L '\*\*Requires:\*\*' backlog/todo/*.md` (cheap, deterministic — reads no task body beyond the marker).
   - **Some headless task exists** → defer this one and let the loop continue:
     ```bash
     python3 .agents/scripts/backlog-ops.py defer <NNN>
     ```
     then output `DEFERRED — task <NNN> requires a live Unity Editor; moved to the tail of TODO.` and END this iteration normally (the next run picks the next task).
   - **Every remaining TODO task requires the editor** → pause exactly like the empty-backlog flow: write local operational state `EDITOR_REQUIRED` into `.agents/state` (do not commit/push it) and output: `EDITOR_REQUIRED — all remaining tasks need a live Unity Editor. Open the Editor, delete .agents/state, then re-run.` (Without the state write, a headless loop would defer-cycle forever.)

---

## STEP 2 — Switch to agent/dev branch

> **REMOTE DETECTION.** Detect once whether an `origin` remote exists:
> ```bash
> git remote get-url origin >/dev/null 2>&1 && HAS_REMOTE=1 || HAS_REMOTE=0
> ```
> When `HAS_REMOTE=1`: fetch/pull/push `origin` and push `agent/dev` after each completed task. When `HAS_REMOTE=0` (common for a freshly generated project): skip every network command and use local refs only.

Before touching code, record the current branch (this is the **base branch**: `agent/dev` is created from it if missing, and **merged from it** on every run so the base branch's work is available to the agent):

```bash
git rev-parse --abbrev-ref HEAD   # → $BASE_BRANCH
```

Then resolve whether `agent/dev` already exists:

```bash
if [ "$HAS_REMOTE" = "1" ]; then
  git fetch origin
  git show-ref --verify --quiet refs/remotes/origin/agent/dev && AGENT_EXISTS=1 || AGENT_EXISTS=0
else
  git show-ref --verify --quiet refs/heads/agent/dev && AGENT_EXISTS=1 || AGENT_EXISTS=0
fi
```

- If `AGENT_EXISTS=1`:
  ```bash
  # Update base only when origin actually has that branch.
  if [ "$HAS_REMOTE" = "1" ] && git show-ref --verify --quiet "refs/remotes/origin/$BASE_BRANCH"; then
    git pull --ff-only origin "$BASE_BRANCH"
  fi
  git checkout agent/dev
  [ "$HAS_REMOTE" = "1" ] && git pull --ff-only origin agent/dev
  [ "$BASE_BRANCH" = "agent/dev" ] || git merge "$BASE_BRANCH" --no-edit
  ```
  - **Skip the merge** when `$BASE_BRANCH` is already `agent/dev` (loop iterations after the first start here).
  - If the merge **conflicts**: run `git merge --abort`, then STOP the whole pipeline (move nothing, implement nothing) and output: `BRANCH_BLOCKED: agent/dev has merge conflicts with $BASE_BRANCH — resolve manually, then re-run.` NEVER auto-resolve conflicts.

- If `AGENT_EXISTS=0`:
  ```bash
  git checkout "$BASE_BRANCH"
  if [ "$HAS_REMOTE" = "1" ] && git show-ref --verify --quiet "refs/remotes/origin/$BASE_BRANCH"; then
    git pull --ff-only origin "$BASE_BRANCH"
  fi
  git checkout -b agent/dev
  ```

> Branch model: `agent/dev` always starts from `$BASE_BRANCH` — the branch active when the loop started: **created** from it on first run, **merged** from it on every later run. The user manually merges `agent/dev → $BASE_BRANCH` after running manual verification steps. Never assume `main` or `develop` is the base.

---

## STEP 3 — Mark IN PROGRESS

Run the deterministic transition — ONE call does the `git mv` todo → in-progress AND the BACKLOG.md bullet move (preserving the `[TIER]` bracket the loop runner reads), then self-lints:

```bash
python3 .agents/scripts/backlog-ops.py start <NNN>
```

- The JSON result echoes the new `path` plus a `lint` block. If `lint.ok = false`, the errors are pre-existing index damage (hand-edit or merge residue) — fix them before writing any code.
- DO NOT hand-edit `BACKLOG.md` for this transition.
- (Resume case: if `pick` returned `state: "in-progress"`, the transition already happened in a previous run — skip this step.)

Do this **before** writing any code.

---

## STEP 4 — Understand context

Before writing code:

### 4a. Resolve CodeGraph availability (ONCE — determines exploration method for the entire task)

First check whether `.codegraph/` exists at repository root. If it does not, set `CODEGRAPH_UP = false` and skip every CodeGraph probe. Indexing is the user's decision.

```
# Only when .codegraph/ exists:
mcp__codegraph__codegraph_search(query="FeatureBaseController", limit=1)
```

- **Success** → set `CODEGRAPH_UP = true`. ALL code exploration in this step MUST use CodeGraph (see table below). Grep/Read for symbol lookups when CodeGraph is available = **wasted tokens**.
- **Error / timeout / tool not found** → set `CODEGRAPH_UP = false`. Fall back to Grep/Read efficiently (precise patterns, minimal reads).

### 4b. Read project context

1. `CLAUDE.md` is already auto-injected into your context by the Claude Code CLI at session start — DO NOT Read it again (redundant read = wasted tokens). Just apply its rules.
2. Read the files in `.agents/rules/` — `code-style.md`, `core-system.md`, `data-persistence.md`, `third-party.md`. (`output-format.md` is only for text responses, do not apply it in this autonomous loop.)
3. Read `SKILL.md` files in `.agents/skills/` that correspond to the system being touched (see mapping in `.agents/agents/code-reviewer.md` under "Skill-specific conventions").
4. Read the files listed in the **Related files** of the task (for any detail `codegraph_explore` trimmed).
5. Read other necessary files to understand the surrounding context.

### 4c. CodeGraph exploration (when CODEGRAPH_UP = true)

| What you need | Tool |
|---|---|
| How does X work / survey an area / read several related files at once | `codegraph_explore` (primary — usually the only call needed) |
| Find where a class/method is defined (location only) | `codegraph_search` |
| What calls this method? | `codegraph_callers` |
| What does this method call? (detect missing dependency, wrong call) | `codegraph_node` with `includeCode=true` |
| What would break if I change X? | `codegraph_callers`, then `codegraph_explore` for the wider flow |
| Read an indexed source file | `codegraph_node` with `file=<path>` |
| List files under a directory | `Glob` (file listing is not a symbol lookup) |

**Rules (enforced — violations waste 40-60% more tokens):**
- NEVER Grep for a class/method name — `codegraph_explore` / `codegraph_search` is faster and returns kind + location + signature.
- NEVER chain multiple Read calls across different files when `codegraph_explore` returns them grouped.
- Use `codegraph_callers` before modifying a widely-used class/method to see direct call sites; use `codegraph_explore` on the returned names when the wider flow matters.
- Only fall back to Grep for **literal string content**: hardcoded text, localize key strings, CSV values, log messages.
- **New files** (created in this same implementation) are not yet indexed (~1s file-watcher lag) — Read them directly instead of querying CodeGraph.

### 4d. Grep/Read fallback (when CODEGRAPH_UP = false)

- Prefer `Grep` with precise patterns over blind reads.
- Read files only after Grep confirms the symbol exists there.
- Minimize Read calls — read only the relevant section.

DO NOT skip this step. Repository conventions are strict and are part of the review contract.

---

## STEP 5 — Implement task

### 5.0 — Workflow-backed shortcut (run FIRST if `$WF_CMD != none`)

If STEP 1 found a `**Backed by workflow:**` line, the scaffold is specified deterministically by a `/new-*` command — do NOT re-derive it free-form.

1. **Read the command file inline:** `.claude/commands/<name>.md` (e.g. `new-feature.md`). Follow its steps **inline as instructions** — do NOT invoke it as a slash command (you are already mid-orchestration; the Skill/command tool would fork the flow).
2. **Follow any delegated skill.** Some commands are thin entry points: `/new-ui` delegates to the `create-ui` skill (`.claude/skills/create-ui/SKILL.md` + its `references/prefab-templates.md` and `references/mcp-playbook.md`). When the command says "invoke the X skill", read that SKILL.md and follow its playbook/checklist — that checklist is the real authority. (`/new-package` is never workflow-backed — see `backlog/_TEMPLATE_WF.md`.)
2b. **Context docs as 0th-priority input:** if `$CONTEXT_DOCS` includes a `TechSpec/<Name>-Implementation.md`, treat it as the command's structured input — for `/new-feature` this IS the "Implementation Mapping" its step 2 gives 0th priority (§10.1–10.7 drive Sub-Features, Save Data, CSV Columns, Events, Registration Points). Use the mapping rows already pasted in the task body first; open the full doc when they lack a detail.
2c. **Ground-truth (mockup) gate — fires for ANY UI task that references an approved mockup, not only `/new-ui`.** The gate is ON when EITHER: (a) `$WF_ARGS` carries ` | groundTruth=<value>`, OR (b) the task has `**Required skills:** /create-ui` AND its body/Context docs reference a `TechSpec/Mockups/<F>/<S>.png` (+ sibling `.ui-spec.json`). When ON: follow the create-ui skill's "Ground truth" section — the approved PNG + ui-spec is the frozen visual contract. Build to match it (a **redesign of an existing screen** = rebuild the body node-by-node from the ui-spec when its containers/elements differ from the current prefab; a recolor-in-place that keeps the old layout is a defect), and the `ui-visual-reviewer` phase checkpoints (A skeleton / B elements / C wiring) are a **hard gate**: a `block` verdict that survives 2 fix rounds terminates the task with `VISUAL_BLOCKED` (do NOT fall through to runtime-smoke — the orchestrator's own screenshot is a smoke test, never a substitute for the independent visual-diff gate). `clone:<Prefab>` copies that prefab's layout. A `PENDING-*` value means promote's `mockup_warnings` blocker was bypassed — STOP with `MOCKUP_BLOCKED — groundTruth not approved; run /ui-mockup first.` Never build a screen from its text description alone.
3. **Execute** using `$WF_ARGS` as the command's `{{args}}` input. Generate exactly the files, registrations, and conventions the command prescribes (`FeatureBaseController` subclass, `GameEnums.Features` registration, `Assets/_Project/Features/<Domain>/` layout, prefab variant from `screen_template`, `PlayerDataManager`/CSV registrations, naming rules, etc.). Honor every "DO NOT" the command states. If the `**Custom delta:**` says a command step is deferred to another queued task (e.g. "SKIP workflow step 7 (UI prefab) — prefab is task NN"), skip that step and note it in the DONE summary instead of executing it.
4. **Apply the `**Custom delta:**`** from the task body (logic/wiring/balance beyond the scaffold). For a pure scaffold the delta is `none`.
5. Then continue with the normal rules below (conventions, no extra features) and proceed to staging + compile check (STEP 5b).

The command's / delegated skill's own checklist is part of the acceptance criteria — make sure every item is satisfied before staging. Quality gates (STEP 6/7) still run in full per `$TASK_TIER`.

If `$WF_CMD = none`, skip this section and implement free-form below.

---

Write code to fulfill the task. Rules:
- Follow exactly the conventions in `.agents/rules/`:
  - Inherit `FeatureBaseController` for UI features, `RedDotBadge` for notifications.
  - Use `UIManager.Show/Hide` instead of `SetActive`.
  - Use `TimeManager` instead of `DateTime.Now`.
  - Use `UniTask` instead of `Coroutine`/`Task`. NO `async void`.
  - Save data using `PlayerDataManager.[Module]`; include a `SetupDefaultData()` fallback when adding fields.
  - `DataManager` is read-only config — never write to it at runtime.
  - Use `TigerForge` + `EventName` constants for cross-system events.
  - DOTween: `OnComplete`/`Kill`; UI tweens must use `SetUpdate(true)`.
  - Localize all user-facing text.
  - Magic numbers → CSV config or `SCREAMING_CONST`.
- No new abstractions, no extra features beyond the task spec.
- No comments unless the WHY is non-obvious.
- Do not hardcode API keys, secrets, or IAP tokens.

**There is no `npm run lint` in a Unity project.** The 3-tier compile check below (STEP 5b) is the early gate: Unity Editor MCP → `dotnet build` → Unity batch mode.

When implementation is done, **stage** all changes:
```bash
git add -A
```

**Do not commit yet.** Quality gates run on the staged diff. Commit only after all gates pass.

### 5b. Unity compile check (3-tier, mandatory)

After staging, attempt compile verification in order. Stop at the first tier that **runs successfully** (regardless of whether it finds errors or not). Only skip if **all 3 tiers cannot run**.

For any tier that runs and finds errors, enter the fix loop before trying the next tier.

**Fix loop (shared across all tiers, max 2 rounds):**
1. Read the error output and fix the code.
2. `git add -A` to re-stage.
3. Re-run the same tier's compile check.
4. If errors remain after 2 rounds → output exactly:
   `COMPILE_BLOCKED — Unity compilation errors remain after 2 fix rounds. Manual intervention required.`
   DO NOT proceed. Stop.

---

**Tier 1 — Unity Editor MCP (instant)**

```
mcp__unity__unity_get_compilation_errors
```

- **No errors** → proceed to STEP 5c.
- **Errors returned** → enter fix loop. If COMPILE_BLOCKED → stop.
- **Tool unavailable (Editor not open)** → proceed to Tier 2.

---

**Tier 2 — dotnet build (~10–40 s)**

```powershell
dotnet build --nologo -v q 2>&1
```

Parse stdout/stderr for lines containing `error CS`.

- **No `error CS` lines** → proceed to STEP 5c.
- **`error CS` lines found** → enter fix loop. If COMPILE_BLOCKED → stop.
- **`dotnet` not found / non-compile exit (e.g., .sln stale, SDK mismatch)** → proceed to Tier 3.

---

**Tier 3 — Unity batch mode (~60–180 s)**

Get the Unity install path that matches this project's editor version (`ProjectSettings/ProjectVersion.txt`, currently `6000.2.6f2`):
```
mcp__unity__unity_hub_list_editors  →  pick version matching this project
```

Run:
```powershell
$unityExe = "<path from hub>/Editor/Unity.exe"
$logFile  = ".agents/tmp/backlog/unity-compile.log"
New-Item -ItemType Directory -Path .agents/tmp/backlog -Force | Out-Null
& $unityExe -batchmode -nographics -projectPath (Resolve-Path .) -logFile $logFile -quit
Get-Content $logFile | Select-String "error CS"
```

- **No `error CS` lines in log** → proceed to STEP 5c.
- **`error CS` lines found** → enter fix loop. If COMPILE_BLOCKED → stop.
- **Unity.exe not found / process fails for non-compile reason** → SKIP.

---

**SKIP** (only when all 3 tiers cannot run):
Note `compile-check: skipped (all 3 methods unavailable)` in the DONE summary Quality gates section. Proceed to STEP 5c.

> **Rule:** Skip only when the compile check *cannot run*. `COMPILE_BLOCKED` only when the check *runs and finds errors* that survive 2 fix rounds.

### 5c. Tier guard — short-circuit for XS and S tasks

Use `$TASK_TIER` extracted in STEP 1 from the BACKLOG.md bullet (the tier is `[XS]`/`[S]`/`[M]`/`[L]` in the bullet, not in the filename).

| Tier | Action |
|------|--------|
| **XS** | Run preflight (STEP 6b). If `has_blocking_definite = false` → skip STEP 6c/6d/6e, STEP 7, and STEP 7.5 entirely. Go directly to STEP 8. Manual verify steps = copy from task spec's "Acceptance criteria". DONE summary records all gates as `skipped (XS tier)`. |
| **S** | Run preflight (STEP 6b). Then spawn **`code-reviewer`** (always) + **`performance-reviewer`** (only if `$PERF_SENSITIVE = true`, see STEP 6c-bis) in parallel. Do NOT spawn security-auditor (unless `$SENSITIVE = true`). Do NOT spawn qa-verifier. Skip STEP 7.5. On `pass`/`warn` from the spawned reviewers → go directly to STEP 8. Manual verify steps = copy from task spec's "Acceptance criteria". |
| **M** / **L** | Full pipeline — proceed normally through STEP 6, STEP 7, and STEP 7.5 (runtime smoke). |

**XS preflight-blocked rule:** if preflight returns `has_blocking_definite = true` for an XS task, apply the same 2-round fix loop as M/L. If still blocked after 2 rounds → `PREFLIGHT_BLOCKED`. Do not skip to DONE with unresolved definite findings.

---

## STEP 6 — Quality Gate: Code Review + Performance Review + Security Review (parallel)

**Purpose:** Before committing, have independent reviewers check the diff against the task spec, audit it for mobile-performance regressions, and audit security if the task touches a sensitive surface.

### 6a. Capture staged diff

```bash
git diff --staged --name-only        # list changed files
git diff --staged                    # full diff
```

If the diff is empty:
- Output: `NO_CHANGES — implementation produced no diff. Task may already be complete or implementation skipped.`
- Stop. DO NOT commit.

### 6b. Run deterministic preflight before LLM reviewers

```bash
# macOS / Linux — use the Python port (identical JSON output)
python3 .agents/scripts/backlog-preflight.py -Pretty
```

The preflight output is a JSON containing:
- `summary.has_blocking_definite`: whether there is a critical finding based on hard rules.
- `summary.definite_critical_count`: number of critical findings.
- `findings[]`: each finding contains `rule`, `severity`, `confidence`, `file`, `line`, `evidence`, `suggestion`.
- `sensitive.value` + `sensitive.reasons[]`: used for the security-auditor decision.

Decision:
- If `summary.has_blocking_definite = true` and `summary.definite_critical_count <= 5`:
  1. Fix findings with `severity=critical` + `confidence=definite`. DO NOT blind grep-replace.
  2. `git add -A`
  3. Re-run preflight.
  4. Repeat for a maximum of 2 preflight-fix rounds before spawning reviewers.
- If `summary.definite_critical_count > 5` or after 2 preflight-fix rounds `has_blocking_definite` remains `true`:
  - Print all remaining definite critical findings.
  - Output exactly: `PREFLIGHT_BLOCKED — deterministic critical findings require manual intervention before LLM review.`
  - DO NOT commit. DO NOT proceed. Stop.
- Findings with `confidence=contextual` DO NOT automatically block reviewers. Paste the raw preflight JSON into the reviewer prompt.

After the preflight-fix loop, capture again:
```bash
git diff --staged --name-only
git diff --staged
```

### 6c. Detect sensitive files

Security review is for **value-bearing / trust-boundary** surfaces, NOT for plain progress save. Check if any file in the diff matches the patterns below (case-insensitive):

- `Assets/_Project/**/Purchase*`, `*IAP*`, `*Receipt*`, `*Payment*`
- `Assets/_Project/**/Auth*`, `*Login*`, `*Token*`, `*Session*`, `Assets/_Project/Features/Social/Account/**`
- New files containing credential patterns (regex `[A-Z0-9_]{3,}_(KEY|SECRET|TOKEN|PASSWORD)`)
- `*.env*`, `*.config`, `*Secrets*`, `*Credential*`
- **Value-bearing writes** (inspect the diff content, not just filename): code that **grants or spends currency**, **grants owned items**, **writes to the server** (Cloudflare Worker / Supabase upsert/delete, `GameNetworkManager`), or writes **leaderboard / competitive** values.

> **NOT security-sensitive by itself:** plain progress save (depth, level, unlock flags, settings) through `PlayerDataManager.[Module]`. Save-tampering of non-value progress data is low-impact and is already covered by the deterministic preflight save rules (`PlayerPrefs`, `Save()` in Update, `DataManager` write) + qa-verifier's `[PERSIST-RESTART]` check — it does NOT warrant spawning the security-auditor. Only escalate a save task to security review when it grants/spends a value-bearing resource per the list above.

Set `$SENSITIVE = true` if any pattern matches OR if the preflight JSON has `sensitive.value = true`, else `false`.

### 6c-bis. Detect performance-sensitive diff

The performance-reviewer only earns its tokens when the diff can actually regress mobile perf. Set `$PERF_SENSITIVE = true` if **any** of these hold (inspect the staged `*.cs` diff + preflight findings); else `false`:

- The diff adds/edits a per-frame method: `Update`, `FixedUpdate`, `LateUpdate`, or a `while`/`for` loop over a gameplay collection.
- Spawn/despawn or instantiation: `Instantiate(`, `Destroy(`, pooling calls (`PoolingManager`, `Spawn`/`Despawn`/`Get`/`Release`).
- List/scroll/UI binding or layout: `ScrollRect`, list/grid item binding, `LayoutRebuilder`, `Canvas`/`SetActive` churn, instantiating UI rows.
- Allocation on a hot path: `new List/Dictionary/StringBuilder`, LINQ (`.Where/.Select/.ToList`), or string concatenation inside any of the above.
- The preflight JSON already raised a `rule: "mobile-performance"` finding.

If the diff is **only** non-`.cs` (prefab/scene/CSV/`.md`/art) OR is pure data POCO / event-constant / read-only accessor code with none of the above → `$PERF_SENSITIVE = false`.

### 6d. Spawn reviewer subagent(s) — in parallel

Always spawn the **`code-reviewer`**. Additionally:
- Spawn the **`performance-reviewer`** ONLY when `$PERF_SENSITIVE = true`. When skipped, record `Performance review: skipped (no perf-sensitive change)` in the DONE summary.
- Spawn the **`security-auditor`** ONLY when `$SENSITIVE = true`.

Spawn all selected reviewers in the **same message** (one parallel tool-use block) so they run concurrently.

**Code Reviewer:**
```
Agent({
  description: "Code review backlog task",
  subagent_type: "code-reviewer",
  prompt: <<see below>>
})
```

**Performance Reviewer:**
```
Agent({
  description: "Perf review backlog task",
  subagent_type: "performance-reviewer",
  prompt: <<see below>>
})
```

**Security Auditor** (only when `$SENSITIVE = true`):
```
Agent({
  description: "Security audit backlog task",
  subagent_type: "security-auditor",
  prompt: <<see below>>
})
```

**Prompt packets — give each reviewer only what its lens needs (token discipline):**

Build the shared blocks ONCE:
- `TASK_PACKET` = the task's **title + Description + Context & Constraints + Acceptance criteria + Guardrails tag line** only. Do NOT paste the full task file boilerplate or the guardrail catalog text (tags resolve to `backlog/_GUARDRAILS.md`).
- `PREFLIGHT_PACKET` = the preflight `findings[]` array + `summary`. If `findings` is empty, write `preflight: clean (no findings)` instead of pasting the whole JSON.
- `FULL_DIFF` = `git diff --staged`. `SCOPED_DIFF(globs)` = `git diff --staged -- <globs>` for the files relevant to that reviewer.

Per-reviewer prompt body:

> TASK:
> ```
> <TASK_PACKET>
> ```
> PREFLIGHT:
> ```
> <PREFLIGHT_PACKET>
> ```
> DIFF:
> ```
> <diff per the table below>
> ```
> CODEGRAPH_UP=<true|false from STEP 4a>
>
> NOTES:
> - Guardrail tags in the task line (e.g. `[SAVE]`, `[ASYNC]`) are defined in `backlog/_GUARDRAILS.md` — read that file for the exact check + verify recipe before judging a tag.
> - Your DIFF may be scoped to your lens (see table below). If you need surrounding context, read it directly via Read/Grep/codegraph — do not treat the scoped diff as the entire change.
>
> Review according to the instructions in the agent definition and return a JSON verdict.

| Reviewer | Diff to pass |
|---|---|
| `code-reviewer` | `FULL_DIFF` (needs every changed file). |
| `performance-reviewer` | `SCOPED_DIFF` of `*.cs` only — skip prefab/scene/asset/CSV/`.md` files (no perf signal there). If only non-`.cs` files changed, you should not have spawned it (see STEP 6c-bis). |
| `security-auditor` | `SCOPED_DIFF` of the sensitive files that set `$SENSITIVE` + any `*.cs` that grants/spends value — skip prefab/scene/art diffs. |

**NOTE:** Spawn all reviewer subagents (`code-reviewer`, `performance-reviewer` when perf-sensitive, and `security-auditor` when `$SENSITIVE`) in **one tool-use block** to run them in parallel. DO NOT run sequentially.

### 6e. Read verdicts and decide

Record each reviewer's `tool_method` field from their verdict JSON (e.g. `code-reviewer: codegraph, perf-reviewer: grep-fallback`). `tool_method=codegraph` means CodeGraph was used for structural symbol/flow lookups; it may still include Grep for literal string scans. Include this in the DONE summary (STEP 8) so you can track token efficiency over time.

If `CODEGRAPH_UP=true` but any reviewer returns `tool_method="grep-fallback"` without reporting a CodeGraph tool error, re-spawn that reviewer once with this extra instruction: "CodeGraph is available. Re-run structural lookups with CodeGraph; use Grep only for literal text scans." Treat the second verdict as authoritative.

- **All are `pass` or `warn`** → proceed to STEP 7 (Verify).
- **Any is `block`** → enter the **auto-fix loop**:

**Auto-fix loop (max 2 rounds):**

- **Round 1**:
  1. Read all `block` and `critical` findings from every reviewer that returned a block.
  2. Capture staged diff snapshot before fixing:
     ```powershell
     New-Item -ItemType Directory -Path .agents/tmp/backlog -Force | Out-Null
     git diff --staged > .agents/tmp/backlog/review-before.diff
     ```
  3. Fix the code yourself (orchestrator = implementer).
  4. `git add -A` to re-stage.
  5. Re-run preflight. Fix definite critical findings before re-spawning reviewers.
  6. Capture delta prompt input:
     ```powershell
     git diff --staged > .agents/tmp/backlog/review-after.diff
     git diff --no-index -- .agents/tmp/backlog/review-before.diff .agents/tmp/backlog/review-after.diff
     ```
  7. Re-spawn the same reviewers (in parallel if both were spawned initially) with:
     - Previous blocking findings JSON.
     - Updated preflight JSON.
     - Delta diff between `review-before.diff` and `review-after.diff`.
     - Full staged diff only if the delta lacks enough context.

- **Round 2**: same as Round 1 if reviewers still return `block`.

- **After Round 2** if still `block`:
  - Print each remaining `block`/`critical` finding (file, line, issue, suggestion).
  - Output exactly: `REVIEW_BLOCKED — manual intervention required. Run /run-backlog again after fixing, or run python3 .agents/scripts/backlog-ops.py demote <NNN> to abandon (returns the task to the head of todo).`
  - DO NOT commit. DO NOT proceed. Stop.

---

## STEP 7 — Quality Gate: Verify (M / L only)

**Purpose:** Confirm that the code has resolved EVERY item in the "Acceptance criteria". Skip this entire step for XS and S tiers — they go directly to STEP 8 (see tier guard in STEP 5c).

### 7a. Spawn qa-verifier subagent

```
Agent({
  description: "Verify backlog implementation",
  subagent_type: "qa-verifier",
  prompt: <<see below>>
})
```

Prompt body (qa-verifier cross-checks every criterion, so it gets the full task spec + full diff — but still the trimmed preflight packet):
> TASK SPEC (focus especially on "Acceptance criteria" and "Manual verify steps"; guardrail tags resolve to `backlog/_GUARDRAILS.md`):
> ```
> <paste full content of backlog/in-progress/<NNN-slug>.md>
> ```
>
> PREFLIGHT:
> ```
> <PREFLIGHT_PACKET — findings[] + summary; if empty, `preflight: clean (no findings)`>
> ```
>
> STAGED DIFF (`git diff --staged`):
> ```
> <FULL_DIFF>
> ```
>
> CODEGRAPH_UP=<true|false from STEP 4a>
>
> Run verification according to the instructions in the agent definition and return a JSON verdict + criteria_check + manual_verify_steps.

### 7b. Read verdict and decide

Record qa-verifier's `tool_method` field for the DONE summary. If `CODEGRAPH_UP=true` but qa-verifier returns `tool_method="grep-fallback"` without reporting a CodeGraph tool error, re-spawn qa-verifier once with this extra instruction: "CodeGraph is available. Re-run structural lookups with CodeGraph; use Grep only for literal text scans." Treat the second verdict as authoritative.

- **`pass`** → proceed to 7c/7d, then STEP 7.5 (runtime smoke).
- **`warn`** → proceed to 7c/7d, then STEP 7.5 — note the `warn` findings in the DONE summary.
- **`fail`** → enter the **auto-fix loop** (same shape as STEP 6d, max 2 rounds):
  - Read `missed_criteria`.
  - Capture `git diff --staged > .agents/tmp/backlog/verify-before.diff`.
  - Fix the code, `git add -A`.
  - Re-run preflight. Fix definite critical findings before re-spawning qa-verifier.
  - Capture `git diff --staged > .agents/tmp/backlog/verify-after.diff`.
  - Re-spawn qa-verifier with previous `missed_criteria`, latest preflight JSON, delta diff, and full staged diff when needed.
  - After Round 2 if still `fail`: print a clear report and exit with:
    `VERIFY_BLOCKED — manual intervention required. Run /run-backlog again after fixing.`
  - DO NOT commit.

### 7c. Capture manual verify steps

The QA-verifier output has a `manual_verify_steps` field — copy this list exactly to paste into the DONE summary in STEP 8 and the REPORT in STEP 9. DO NOT modify or shorten it.

### 7d. Final deterministic preflight before DONE

```bash
# macOS / Linux — use the Python port (identical JSON output)
python3 .agents/scripts/backlog-preflight.py -Pretty
```

- If `summary.has_blocking_definite = false` → proceed to STEP 7.5 (runtime smoke).
- If `summary.has_blocking_definite = true` → fix definite critical findings, `git add -A`, and re-run qa-verifier if the fix might affect completion criteria. If not resolved cleanly after 2 rounds, stop with:
  `PREFLIGHT_BLOCKED — deterministic critical findings require manual intervention before DONE.`

---

## STEP 7.5 — Quality Gate: Runtime smoke (M / L only, orchestrator-side)

**Purpose:** every gate so far only READS the diff — none observes the game running. This gate boots the game in the Editor and fails on runtime errors, catching scene wiring, lifecycle, presentation, and state-reset failures that a static diff reader cannot see.

Run it for **M/L** after qa-verifier passes (STEP 7) and the final preflight (7d). XS/S skip it (tier guard 5c). **You run it yourself** — the `mcp__unity__*` tools are available to the orchestrator only; do NOT spawn a subagent for this gate.

**Skip conditions (graceful — never fail the task on these; record the reason in the DONE summary):**
- Unity MCP not connected / no Editor open for this project → probe `mcp__unity__unity_list_instances`; on fail/timeout record `Runtime smoke: skipped (Unity MCP not connected / Editor not open)`.
- The staged diff has no runtime surface (docs/`.md`, CSV comments only, editor-only `#if UNITY_EDITOR` code) → record `Runtime smoke: skipped (no runtime surface)`.

**Procedure:**
1. **Compile settled first** — poll `mcp__unity__unity_editor_state` until the Editor is NOT compiling. NEVER enter play mode with a compile pending: a mid-play domain reload wipes statics and produces a false NRE storm.
2. `mcp__unity__unity_console_clear` — start from a clean console.
3. Enter play mode (`mcp__unity__unity_play_mode`, play). Poll `unity_editor_state` until playing, then let the project's normal boot flow run **~20–30 s**. Use task-specific liveness signals from the task spec or current bootstrap scene; do not assume a particular game mode.
4. **Execute the spec's acceptance recipes** via `mcp__unity__unity_execute_code` wherever a criterion is expressible as a code assert (read a service value, confirm an object/prefab is live, invoke the flow under test). The C# payload MUST be ASCII-only (non-ASCII gets mangled in transit — route Vietnamese strings through files on disk if ever needed).
5. **`$SENSITIVE` invariant suite** (only when STEP 6c set `$SENSITIVE = true` — economy/save/reset surfaces). Run via `unity_execute_code`, snapshot-first so player state is always restored:
   - *Currency conservation:* read balance → grant X → spend X → assert balance == baseline (net-zero by construction).
   - *Save-load roundtrip:* `PlayerDataManager.<Module>.Save()` → read the persisted JSON → assert persisted fields equal live values.
   - *Reset scope* (only when the diff touches a reset flow): snapshot every affected module's JSON → invoke the reset → assert ONLY the modules the spec intends changed → restore all modules from the snapshot and `Save()`.
6. Read `mcp__unity__unity_console_log` (errors + exceptions only). **Any exception/NRE, or any error originating from code the diff touches → FAIL.** Error-level noise that is provably pre-existing and unrelated to the diff → record as `warn` with a one-line justification; do not fail on it.
7. `mcp__unity__unity_screenshot_game` → save to `.agents/tmp/backlog/runtime-smoke-<NNN>.png` and reference it in the DONE summary.
8. **Exit play mode** (`unity_play_mode`, stop) before doing anything else — never leave the Editor playing.

**On FAIL — auto-fix loop (max 2 rounds, same shape as STEP 6/7):** read the console evidence, exit play mode, fix the code, `git add -A`, re-run preflight if `.cs` changed, then re-run this gate from step 1. After Round 2 still failing → print the console evidence (error text + stack head) and output exactly:
`RUNTIME_BLOCKED — runtime smoke failed after 2 fix rounds. Manual intervention required.`
DO NOT commit. Stop.

---

## STEP 8 — Mark DONE

Make **two** updates (will go into the same commit in STEP 9):

1. **Run the deterministic transition** — ONE call does the `git mv` in-progress → done AND removes the IN PROGRESS bullet (restoring `- (none)` when empty), then self-lints. It never adds a DONE bullet — `backlog/done/` is the source of truth:

   ```bash
   python3 .agents/scripts/backlog-ops.py done <NNN>
   ```

2. **Edit the moved file** (`backlog/done/<NNN-slug>.md`): replace the long task body with a short completion summary — this is content work, so YOU write it (the script only handles the transition). Keep the heading `### [PRIORITY] Title`. Add:
   ```
   **Completed on:** YYYY-MM-DD

   **Fix Summary:** 1–3 sentences summarizing what changed and why.

   **Quality gates:**
   - Code review: <pass|warn|skipped (XS tier)> (rounds used: 1|2) [tool: codegraph|grep-fallback|n/a]
   - Performance review: <pass|warn|skipped (XS tier)|skipped (no perf-sensitive change)> (rounds used: 1|2) [tool: codegraph|grep-fallback|n/a]
   - Security review: <pass|warn|skipped — no sensitive files|skipped (XS/S tier)> (rounds used if spawned) [tool: codegraph|grep-fallback|n/a]
   - QA verify: <pass|warn|skipped (XS/S tier)> (rounds used: 1|2) [tool: codegraph|grep-fallback|n/a]
   - Runtime smoke: <pass|warn|skipped (XS/S tier)|skipped (Unity MCP not connected / Editor not open)|skipped (no runtime surface)> (rounds used: 1|2) [screenshot: .agents/tmp/backlog/runtime-smoke-<NNN>.png|n/a]

   **Manual verify steps (USER MUST RUN before merging agent/dev → $BASE_BRANCH):**
   <M/L: copy exact `manual_verify_steps` from qa-verifier output>
   <XS/S: copy acceptance criteria items verbatim from the task spec>
   ```

---

## STEP 9 — Commit and push to agent/dev

Run the index consistency lint one last time before committing:

```bash
python3 .agents/scripts/backlog-ops.py lint
```

If `ok = false` → the errors indicate a hand-edit or merge residue (dual-state file, orphan bullet, leaked markup). Fix them, re-run the lint, and only then commit.

Stage and commit all changed files (including `git mv` moves):
```bash
git add -A
git commit -m "<concise commit message max 50 chars>"
SHORT_SHA=$(git rev-parse --short HEAD)
```

Use `$SHORT_SHA` only in the STEP 10 user report. Do NOT edit `backlog/done/*.md` after committing just to fill the commit SHA; that creates a second metadata-only `chore` commit for every task.

Commit message format:
- `feat: new shop popup with daily deals`
- `fix: notification badge stale on logout`
- `refactor: extract feature config parser`

Push to `agent/dev`:
```bash
git push -u origin agent/dev
```

> **REMOTE DETECTION:** when `HAS_REMOTE=1`, the push above is required. Only skip it when `HAS_REMOTE=0`; report `committed locally (no remote — push skipped)`.

**DO NOT create a PR.** The user manually merges `agent/dev → $BASE_BRANCH` after running manual verification steps.

---

## STEP 10 — Report to user

Notify the user:
- Task completed (link to the file in `backlog/done/`)
- Files changed
- Commit message + short SHA used
- Branch + remote pushed (`agent/dev`)
- **Pipeline summary**: every gate verdict (code / perf / security / QA / runtime smoke) + rounds used in auto-fix
- **MANUAL VERIFY REMINDER**: specific verification steps from the qa-verifier output, numbered clearly

Example report format:
```
✅ Completed: backlog/done/001-new-shop-popup.md
Files: 3 changed (Assets/_Project/.../ShopController.cs, ...)
Commit: feat: new shop popup with daily deals (a1b2c3d)
Branch: agent/dev (pushed to origin)

Pipeline:
  - Code review: pass (1 round) [codegraph]
  - Performance review: pass (1 round) [codegraph]
  - Security review: skipped (no sensitive files)
  - QA verify: pass (1 round) [codegraph]
  - Runtime smoke: pass (1 round) [screenshot: .agents/tmp/backlog/runtime-smoke-001.png]
  - Tool efficiency: 3/3 codegraph — optimal ✅

⚠️ MANUAL VERIFY REQUIRED before merging agent/dev → $BASE_BRANCH:
  1. Open MainScene, open Shop popup, confirm daily deals display correctly
  2. Tap buy twice quickly — confirm only 1 purchase triggers
  3. Regression: existing shop features (currency display, IAP packs) still work
  4. Build Android APK to test on real device

After verification passes: `git checkout $BASE_BRANCH && git merge agent/dev`
```

If any gate agent used grep-fallback, add a warning:
```
  ⚠️ Tool efficiency: perf-reviewer used grep-fallback (CodeGraph MCP unavailable or errored)
```

---

## Notes for orchestrator

- **You are both orchestrator and implementer.** Subagents only review/audit/verify. You write the code, you fix bugs from findings, and you make commits.
- **Subagents are stateless across invocations.** Each spawn receives a fresh prompt with the diff and task spec.
- **Preflight is a deterministic guard, not a replacement for reviewers.** Only auto-fix findings with `confidence=definite`; findings with `confidence=contextual` must go into the reviewer/qa prompt.
- **Delta diff is only used for fix rounds.** The initial review and final QA/preflight must still have the full staged context.
- **Spawn reviewers in parallel** — code-reviewer always; performance-reviewer only when `$PERF_SENSITIVE` (STEP 6c-bis); security-auditor only when `$SENSITIVE` — one tool-use block, multiple Agent calls. DO NOT run sequentially.
- **Hard stop conditions** (never bypass):
  - `BACKLOG EMPTY` — no tasks available.
  - `NO_CHANGES` — implementer did not produce a diff.
  - `PREFLIGHT_BLOCKED` — deterministic definite critical findings remain.
  - `REVIEW_BLOCKED` after Round 2 in STEP 6.
  - `VERIFY_BLOCKED` after Round 2 in STEP 7.
  - `RUNTIME_BLOCKED` after Round 2 in STEP 7.5.
  - `VISUAL_BLOCKED` — a UI task with an approved-mockup ground truth (STEP 2c) still fails `ui-visual-reviewer` after Round 2 (live build diverges from the approved `<S>.png` / ui-spec structure).
  - `MOCKUP_BLOCKED` — a `/new-ui` task reached STEP 5.0 with a `PENDING-*` groundTruth (approve via `/ui-mockup` first).
  - `EDITOR_REQUIRED` — every remaining TODO task declares `**Requires:** unity-editor` and no Editor is live (STEP 1b); also writes `EDITOR_REQUIRED` to `.agents/state`. (`DEFERRED` is NOT a stop — it ends the iteration normally and the next run picks the next task.)
- **No `--ship-anyway` mode.** If the user wants to force-ship a blocked task, they manually resolve the block and re-run the skill.
- **No PR creation.** The pipeline only pushes to `agent/dev`; the user merges manually after verification.
- **No deploy step.** Mobile game builds are done via Unity Editor, no CLI deploy exists.
- **No `npm run lint` equivalent.** Unity projects lack a CLI compilation check. Rely on the 3 quality gates + manual verification.
- **Verifier limitation:** qa-verifier is a static diff check. The runtime smoke gate (STEP 7.5) covers boot + console + spec recipes for M/L when the Editor is up — but it is a smoke test, not full QA. The manual verification steps in the task spec + DONE summary remain the final safety net — the user MUST still run them.
