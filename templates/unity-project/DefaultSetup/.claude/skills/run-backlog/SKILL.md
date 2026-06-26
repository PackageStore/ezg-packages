---
name: run-backlog
description: Autonomous backlog agent for [Project Name] — pick the first task in TODO, implement it, run quality gates (code-reviewer + performance-reviewer in parallel, + security-auditor when sensitive, + qa-verifier) with auto-fix max 2 rounds per gate, mark it DONE, and commit + push to agent/dev. DO NOT create PRs.
---

# Run Backlog — Autonomous Task Agent

You are an autonomous development agent and the **orchestrator** of a multi-agent pipeline for the [Project Name] project (Unity, C#, mobile merge-grid game). Task: pick the first task from the backlog, implement it, pass quality gates by delegating to subagents, mark it DONE, and commit + push to the `agent/dev` branch.

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
[1] PICK     → read index, pick task, move todo → in-progress
[2] BRANCH   → switch to agent/dev (branch from develop if it doesn't exist yet)
[3] CONTEXT  → read CLAUDE.md + .agents/rules/* + task file + relevant code
[4] IMPLEMENT→ write code, git add (DO NOT commit yet)
[5] REVIEW   → run deterministic preflight, then spawn code-reviewer + (performance-reviewer IF perf-sensitive) + (security-auditor IF sensitive files) in parallel
             → auto-fix max 2 rounds if blocked; fix rounds use preflight + delta diff when enough context is provided
[6] VERIFY   → spawn qa-verifier; auto-fix max 2 rounds if failed; final preflight before DONE
[7] DONE     → move in-progress → done, write summary with all 3 gate verdicts
[8] SHIP     → commit + push to agent/dev (DO NOT create a PR)
[9] REPORT   → summarize for user, including manual verification steps
```

---

## STEP 1 — Read index and pick task

Read `BACKLOG.md` only. Then:

- If there is a task under `## IN PROGRESS` → **resume** that task. Read the corresponding file in `backlog/in-progress/`.
- Else if there is at least one entry under `## TODO` → pick the **first entry** (topmost). Note the file path.
- Else (no IN PROGRESS, no TODO) → backlog is empty. Run the **self-pause flow**:
  1. Write the string `PAUSED` into `.agents/state`
  2. Commit this file to `agent/dev` (push only if a remote exists — see LOCAL-ONLY MODE in STEP 2):
     ```bash
     git add .agents/state
     git commit -m "chore: pause agent — TODO is empty"
     [ "$HAS_REMOTE" = "1" ] && git push origin agent/dev   # skipped when no origin
     ```
  3. Stop and output: `TODO is empty — agent paused. Add tasks via /planning-task then /add-to-backlog, then re-run.`

Then read **exactly one** identified task file. DO NOT read other task files.

Extract from the task file:
- Task title and priority
- **Task tier** (from the BACKLOG.md bullet format `[XS]`, `[S]`, `[M]`, `[L]`) — store as `$TASK_TIER`, used for the tier guard in STEP 5c. The tier lives in the BACKLOG.md bullet, NOT in the filename.
- **Description** (what to do and why)
- **Context & Constraints**
- **Related files** (files to read first)
- **Acceptance criteria** (exit conditions)
- **Manual verify steps** — will be copied verbatim into the DONE summary for the user.

---

## STEP 2 — Switch to agent/dev branch

> **REMOTE DETECTION.** Detect once whether an `origin` remote exists:
> ```bash
> git remote | grep -q . && HAS_REMOTE=1 || HAS_REMOTE=0
> ```
> When `HAS_REMOTE=1` (the normal case — the project is connected to a remote): run `git fetch/pull/push origin` normally and **push `agent/dev` to origin** after each commit (STEP 1/STEP 9). When `HAS_REMOTE=0` (e.g. a fresh project generated from this template, not yet connected to GitLab): skip every `git fetch/pull/push origin` and determine whether `agent/dev` exists with `git branch --list agent/dev` (local) instead of `git branch -r`. Everything else runs unchanged on the `agent/dev` branch.

Before touching code, record the current branch (this is the base branch to use if `agent/dev` doesn't exist yet):

```bash
git rev-parse --abbrev-ref HEAD   # → $BASE_BRANCH
```

Then fetch and get on `agent/dev`:

```bash
git fetch origin
```

Check if `agent/dev` exists on the remote:
```bash
git branch -r | grep origin/agent/dev
```

- If it **exists**:
  ```bash
  git checkout agent/dev
  git pull origin agent/dev
  ```

- If it **does not exist yet**:
  ```bash
  git checkout $BASE_BRANCH
  git pull origin $BASE_BRANCH
  git checkout -b agent/dev
  ```

> [Project Name] branch model: `agent/dev` is branched from whatever branch was active when the loop started (captured as `$BASE_BRANCH` above). The user manually merges `agent/dev → $BASE_BRANCH` after running manual verification steps. Never branch from `main` directly.

---

## STEP 3 — Mark IN PROGRESS

Make **two** updates (will go into the same commit in STEP 8):

1. **Move task file**: `git mv backlog/todo/<NNN-slug>.md backlog/in-progress/<NNN-slug>.md`
   (preserve git history; do not copy-and-delete)

2. **Edit `BACKLOG.md`**:
   - Remove the picked entry from `## TODO`.
   - Under `## IN PROGRESS`, replace `- (none)` with: `- [PRIORITY] [TIER] [Title](backlog/in-progress/<NNN-slug>.md)`
     - Substitute **real values**, not the placeholder words: `[PRIORITY]` -> `[HIGH]`/`[MEDIUM]`/`[LOW]`, `[TIER]` -> the bracketed `$TASK_TIER` from STEP 1 (`[XS]`/`[S]`/`[M]`/`[L]`), `[Title]` -> the task title.
     - **Keep the `[TIER]` bracket** — the loop runner (`run-backlog-loop.sh --auto-model-by-tier`) reads this exact token to pick the model/effort for the next task window. Dropping it or mangling the brackets makes a resumed task fall back to the M/opus profile.
     - Example: `- [HIGH] [S] [Author CurrencyConfig CSVs](backlog/in-progress/022-author-currencyconfig-csv-collection-model.md)`

Do this **before** writing any code.

---

## STEP 4 — Understand context

Before writing code:

### 4a. Probe CodeGraph availability (ONCE — determines exploration method for the entire task)

```
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

DO NOT skip this step. [Project Name] conventions are strict — violations will be blocked by the code-reviewer in STEP 5.

---

## STEP 5 — Implement task

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
dotnet build --nologo -v q 2>&1   # auto-detects the project's single .sln in repo root
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
| **XS** | Run preflight (STEP 6b). If `has_blocking_definite = false` → skip STEP 6c/6d/6e and STEP 7 entirely. Go directly to STEP 8. Manual verify steps = copy from task spec's "Acceptance criteria". DONE summary records all three gates as `skipped (XS tier)`. |
| **S** | Run preflight (STEP 6b). Then spawn **`code-reviewer`** (always) + **`performance-reviewer`** (only if `$PERF_SENSITIVE = true`, see STEP 6c-bis) in parallel. Do NOT spawn security-auditor (unless `$SENSITIVE = true`). Do NOT spawn qa-verifier. On `pass`/`warn` from the spawned reviewers → go directly to STEP 8. Manual verify steps = copy from task spec's "Acceptance criteria". |
| **M** / **L** | Full pipeline — proceed normally through STEP 6 and STEP 7. |

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
# macOS / Linux (no PowerShell) — Python port, identical JSON output:
python3 .agents/scripts/backlog-preflight.py -Pretty
# Windows (PowerShell):
# powershell -ExecutionPolicy Bypass -File .agents/scripts/backlog-preflight.ps1 -Pretty
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
  - Output exactly: `REVIEW_BLOCKED — manual intervention required. Run /run-backlog again after fixing, or move task back to backlog/todo/ to abandon.`
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

- **`pass`** → proceed to STEP 8 (Mark DONE).
- **`warn`** → proceed to STEP 8 but note `warn` findings in the DONE summary.
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
# macOS / Linux (no PowerShell) — Python port, identical JSON output:
python3 .agents/scripts/backlog-preflight.py -Pretty
# Windows (PowerShell):
# powershell -ExecutionPolicy Bypass -File .agents/scripts/backlog-preflight.ps1 -Pretty
```

- If `summary.has_blocking_definite = false` → proceed to STEP 8.
- If `summary.has_blocking_definite = true` → fix definite critical findings, `git add -A`, and re-run qa-verifier if the fix might affect completion criteria. If not resolved cleanly after 2 rounds, stop with:
  `PREFLIGHT_BLOCKED — deterministic critical findings require manual intervention before DONE.`

---

## STEP 8 — Mark DONE

Make **two** updates (will go into the same commit in STEP 9):

1. **Move task file**: `git mv backlog/in-progress/<NNN-slug>.md backlog/done/<NNN-slug>.md`

2. **Edit the moved file**: replace the long task body with a short completion summary. Keep the heading `### [PRIORITY] Title`. Add:
   ```
   **Completed on:** YYYY-MM-DD

   **Fix Summary:** 1–3 sentences summarizing what changed and why.

   **Quality gates:**
   - Code review: <pass|warn|skipped (XS tier)> (rounds used: 1|2) [tool: codegraph|grep-fallback|n/a]
   - Performance review: <pass|warn|skipped (XS tier)|skipped (no perf-sensitive change)> (rounds used: 1|2) [tool: codegraph|grep-fallback|n/a]
   - Security review: <pass|warn|skipped — no sensitive files|skipped (XS/S tier)> (rounds used if spawned) [tool: codegraph|grep-fallback|n/a]
   - QA verify: <pass|warn|skipped (XS/S tier)> (rounds used: 1|2) [tool: codegraph|grep-fallback|n/a]

   **Manual verify steps (USER MUST RUN before merging agent/dev → develop):**
   <M/L: copy exact `manual_verify_steps` from qa-verifier output>
   <XS/S: copy acceptance criteria items verbatim from the task spec>
   ```

3. **Edit `BACKLOG.md`**:
   - Remove the entry from `## IN PROGRESS`; if empty, put back `- (none)`.
   - DO NOT list completed tasks in `## DONE` of BACKLOG.md — `backlog/done/` is the source of truth.

---

## STEP 9 — Commit and push to agent/dev

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
- `refactor: extract item merge csv parser`

Push to `agent/dev`:
```bash
git push -u origin agent/dev
```

> **REMOTE DETECTION:** when `HAS_REMOTE=1` (the normal case — a remote is connected), the `git push -u origin agent/dev` above is **required** so the task lands on the remote. Only skip the push when `HAS_REMOTE=0` (no remote at all); in that case report `committed locally (no remote — push skipped)` in STEP 10.

**DO NOT create a PR.** The user manually merges `agent/dev → $BASE_BRANCH` after running manual verification steps. This is a [Project Name] convention.

---

## STEP 10 — Report to user

Notify the user:
- Task completed (link to the file in `backlog/done/`)
- Files changed
- Commit message + short SHA used
- Branch + remote pushed (`agent/dev`)
- **Pipeline summary**: all 3 gate verdicts + rounds used in auto-fix
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
  - Tool efficiency: 3/3 codegraph — optimal ✅

⚠️ MANUAL VERIFY REQUIRED before merging agent/dev → develop:
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
- **No `--ship-anyway` mode.** If the user wants to force-ship a blocked task, they manually resolve the block and re-run the skill.
- **No PR creation.** [Project Name] only pushes to `agent/dev`; the user merges manually after manual verification.
- **No deploy step.** Mobile game builds are done via Unity Editor, no CLI deploy exists.
- **No `npm run lint` equivalent.** Unity projects lack a CLI compilation check. Rely on the 3 quality gates + manual verification.
- **Verifier limitation:** qa-verifier is primarily a static check. It does not exercise the game runtime. The manual verification steps in the task spec + DONE summary are the ultimate safety net — the user MUST run them.
