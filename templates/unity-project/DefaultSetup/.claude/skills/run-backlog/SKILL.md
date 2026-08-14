---
description: Autonomous backlog agent for this Unity project — pick the first task in TODO, implement it, run quality gates (code-reviewer + performance-reviewer when perf-sensitive + security-auditor when sensitive, in parallel + qa-verifier) with auto-fix max 2 rounds per gate, mark it DONE, and commit + push to the work branch (current mode: the branch already checked out; worktree mode: agent/dev-<base>). DO NOT create PRs.
---

# Run Backlog — Autonomous Task Agent

You are an autonomous development agent and the **orchestrator** of a multi-agent pipeline for this Unity/C# project. Task: pick the first task from the backlog, implement it, pass quality gates by delegating to subagents, mark it DONE, and commit + push to the `agent/dev` branch.

Follow these steps **precisely**.

## Project profile — resolve `<placeholders>` before you use them

This skill ships unchanged to every project on this base, so anything that
differs per project lives in `.claude/project-profile.json` instead of being
written into these instructions. Wherever you see an angle-bracket placeholder
below — `<sourceRoot>`, `<featuresRoot>`, `<gameplayRoot>`, `<gitConfigPrefix>` —
substitute that key's value.

```bash
python3 .claude/scripts/project_profile.py            # all keys, merged
python3 .claude/scripts/project_profile.py sourceRoot # one key
```

Read it once at the start and reuse the values. Do not guess a path from what
the repo looks like: the whole point of the profile is that the same sentence
means `Assets/_Game` in one project and `Assets/_Project` in the next. If the
file is absent the reader falls back to built-in defaults, which is normal and
not an error.

## Where the backlog lives — and what mode you are running in

**Location.** The backlog is NOT in the worktree. It lives in the git common dir:

```
$(git rev-parse --git-common-dir)/backlog/     # i.e. <repo>/.git/backlog/
```

It is per-developer bookkeeping, never committed and never merged — tracking it
made every dev branch carry its own index, so two devs collided on `BACKLOG.md`
and on NNN numbering. `.git/` is shared by every linked worktree of the clone, so
an agent in a `git worktree` sees the SAME queue as the dev's main checkout.

Consequences you must respect:
- **Never `git add` / `git mv` / commit a task file.** Nothing under `.git/` can be
  tracked. Every transition goes through `backlog-ops.py`, which does plain
  filesystem moves. The DONE summary therefore does NOT appear in the commit.
- Bullet paths inside `BACKLOG.md` stay written `backlog/<state>/<file>.md`; they
  resolve against the git common dir, not the worktree.
- `$BACKLOG_ROOT` below means that directory. The loop runner exports it as
  `AGENT_BACKLOG_ROOT`.

**Mode.** The loop runner exports `AGENT_MODE`:

| | `current` (default) | `worktree` |
|---|---|---|
| Working dir | the dev's checkout | sibling `<repo>-agent-<base>` |
| Work branch | the branch already checked out | `agent/dev-<base>` (mandatory) |
| STEP 2 | **skipped entirely** | create/reuse worktree, merge base |
| Compile check (5b) | runs | **always skipped** |
| Runtime smoke (7.5) | runs | **always skipped** |

Read the env var once at STEP 1 and store it as `$AGENT_MODE`; also store
`$WORK_BRANCH` = `AGENT_BRANCH`. If either is unset (an ad-hoc
`/run-backlog` outside the loop), default to `current` and the current branch.

**Remote.** Do not assume one exists. A project freshly generated from the base
template has no `origin` until the dev creates the repository, and some repos stay
deliberately local. Probe once at STEP 1 and store the answer:

```bash
git remote get-url origin >/dev/null 2>&1 && HAS_REMOTE=1 || HAS_REMOTE=0
```

- `HAS_REMOTE=1` → fetch / pull / merge `origin/...` / push exactly as written below.
- `HAS_REMOTE=0` → **skip every network command** and work from local refs only. The
  task still implements, gates, and commits normally; only the push is dropped, and
  STEP 10 reports `committed locally (no remote — push skipped)`.

A missing remote is a normal state, never a blocker. Do NOT `git remote add` one and
do NOT stop to ask — wiring a repository up is the developer's decision, not the
agent's.

**Split-file layout** (keeps token usage flat):
- `$BACKLOG_ROOT/BACKLOG.md` = short index (the only file you read for the "directory")
- `$BACKLOG_ROOT/planning/` = drafted-but-not-queued tasks; **ignore** (managed by `/planning-task` + `/add-to-backlog`)
- `$BACKLOG_ROOT/todo/NNN-TIER-slug.md` = one file per queued task (full details)
- `$BACKLOG_ROOT/in-progress/NNN-TIER-slug.md` = task currently in progress
- `$BACKLOG_ROOT/done/NNN-TIER-slug.md` = completed tasks (summary; legacy DONE files may omit TIER)

You read the index + **exactly one** task file — never scan all tasks.

Pipeline orchestration:
```
[1]   PICK     → backlog-ops pick: resolve the task from the index (todo | in-progress resume | empty → pause)
[2]   BRANCH   → worktree mode only: create/reuse the worktree branch + merge base in. current mode: SKIP
[3]   START    → backlog-ops start: todo → in-progress + BACKLOG.md bullet move
[4]   CONTEXT  → read CLAUDE.md + .claude/rules/* + task file + relevant code
[5]   IMPLEMENT→ write code, git add + 3-tier compile check (STEP 5b) (DO NOT commit yet)
[6]   REVIEW   → deterministic preflight, then spawn code-reviewer + (performance-reviewer IF perf-sensitive) + (security-auditor IF sensitive) in parallel; auto-fix max 2 rounds
[7]   VERIFY   → spawn qa-verifier (M/L); auto-fix max 2 rounds if failed; final preflight
[7.5] SMOKE    → runtime smoke gate (M/L, orchestrator-side, Unity MCP): play mode + console assert + screenshot; auto-skips if Editor absent
[8]   DONE     → backlog-ops done: in-progress → done + bullet removal, write summary with all gate verdicts
[9]   SHIP     → backlog-ops lint, then commit + push to $WORK_BRANCH (DO NOT create a PR)
[10]  REPORT   → summarize for user, including manual verification steps
```

> **Deterministic bookkeeping:** every backlog state transition (pick / start / done / demote / index edits) runs through `python3 .claude/scripts/backlog-ops.py` — NEVER hand-edit `BACKLOG.md` or `git mv` a task file yourself for a transition. Hand-edited bookkeeping corrupts the index (leaked tool-call markup, dual-state task files, forbidden DONE bullets); the script self-lints after every mutation.

---

## STEP 1 — Read index and pick task

Resolve the task deterministically (do NOT parse `BACKLOG.md` yourself):

```bash
python3 .claude/scripts/backlog-ops.py pick
# → JSON: {state, resume, nnn, tier, priority, title, path} — or {"state":"empty"} (exit code 2)
```

- `state: "in-progress"` (`resume: true`) → **resume** that task. Read the file at `path`. (The todo→in-progress transition already happened in a previous run — skip STEP 3.)
- `state: "todo"` → the first TODO entry. Note `path`, `nnn`, `tier`, `priority`.
- `state: "empty"` (exit code 2 = the PAUSED signal) → backlog is empty. Run the **self-pause flow**:
  1. Write the string `PAUSED` into `$BACKLOG_ROOT/state` (loop state lives next to the
     backlog for the same reason the backlog does: it is per-developer, and the old
     tracked `.claude/state` made every dev branch carry a conflicting pause marker).
  2. Do NOT commit or push it — nothing under `.git/` is trackable.
  3. Stop and output: `TODO is empty — agent paused. Add tasks via /planning-task then /add-to-backlog, then re-run.`
- exit code 3 (`backlog not initialised`) → this checkout has no backlog yet. Run
  `python3 .claude/scripts/backlog-ops.py init`, then re-run `pick`. Do NOT hand-create
  `BACKLOG.md` — the lint invariants are strict.

Then read **exactly one** identified task file (at `path`). DO NOT read other task files.

Extract from the task file:
- Task title and priority
- **Task tier** — store as `$TASK_TIER` (one of `XS` / `S` / `M` / `L`), used for tier-gated reviewer spawning in STEP 6. Resolve it in this order:
  1. The `tier` field from the `pick` JSON (sourced from the BACKLOG.md bullet by the script). Use this if present.
  2. Else the `**Tier:** X` line in the task body (read it from the identified file).
  3. Else (neither found — legacy task) default to `M` and note `tier: defaulted to M (not declared)` in the DONE summary. Never infer the tier from the priority.
- **Backed by workflow** — if the task body has a `**Backed by workflow:** /new-xxx` line, store `$WF_CMD = /new-xxx` and `$WF_ARGS` from the `**Workflow args:**` line, plus `**Custom delta:**`. This routes implementation through STEP 5.0 (workflow-backed shortcut). If absent, `$WF_CMD = none` (normal free-form implement).
- **Context docs** — if the task body has a `**Context docs:**` line (batch tasks from `/planning-system`), store the paths as `$CONTEXT_DOCS`. These are design docs (typically `TechSpec/<Name>-Implementation.md` + `-TechSpec.md`) holding the concrete values (Manager Type, CSV columns, economy numbers, event tables) the task was planned from — read them in STEP 4/5.0. If absent, `$CONTEXT_DOCS = none`.
- **Requires** — if the task body has a `**Requires:**` line, run the **requires gate** below BEFORE STEP 3 (the task is still in todo/ — defer is only possible pre-start). For this and the other optional fields (`**Context docs:**`, `**Depends on:**`): ignore occurrences inside HTML comments (`<!-- ... -->`) — those are template leftovers, not declarations.
- **Description** (what to do and why)
- **Context & Constraints**
- **Related files** (files to read first)
- **Completion criteria** (exit conditions)
- **Required verification steps after loop stops (manual)** — will be copied verbatim into the DONE summary for the user.

### 1b. Requires gate (only when the task declares `**Requires:**`)

Currently one requirement token is defined: `unity-editor` (the task authors prefabs / needs a live Editor, e.g. `/new-ui`- and `/new-package`-backed tasks).

#### 0. Worktree mode short-circuit — do NOT probe

If `$AGENT_MODE = worktree`, the requirement can never be met: the worktree is a
separate Unity project folder and the only live Editor is attached to the dev's
checkout. Probing would find that Editor and tempt you into refreshing/playing the
**wrong project**. Skip the probe entirely and go straight to step 2 below (defer,
or `EDITOR_REQUIRED` if every remaining task needs the Editor).

#### 1. Probe the Editor — retry-with-wait, NEVER single-shot (current mode only)

**Why:** "The Editor is open" ≠ "the MCP bridge answers *this instant*". Discovery is a port scan that needs the Editor's main thread to respond; right after a heavy previous task (asset import, a large AssetBundle rebuild, or a domain/assembly reload) the main thread is blocked and a one-shot `unity_list_instances` returns **zero instances even though the Editor is up**. A single-shot gate turns that transient into a false hard `EDITOR_REQUIRED` pause. So classify the result, and retry only the genuinely-ambiguous case:

Probe `mcp__unity__unity_list_instances` and classify:

- **This project's Editor is listed** → requirement met. If several instances are listed, `mcp__unity__unity_select_instance` the one whose project path matches this repo (**never** stop to ask a human — the loop is autonomous). If that instance is mid-compile, poll `mcp__unity__unity_editor_state` until it is NOT compiling, then continue to STEP 2 normally.
- **MCP reachable but only OTHER projects are listed** (a different game's Editor) → a genuine "not live for this project". Do NOT retry — go to step 2.
- **MCP unreachable / call errored or timed out / zero instances returned** → treat as *maybe-busy, not maybe-absent*. Wait ~5 s (`sleep 5`) and re-probe. Repeat up to **4 attempts (~20 s total)**. If any attempt lists this project → requirement met (first bullet). Only after the full retry budget is exhausted without the project Editor ever appearing → treat as NOT met and go to step 2.

#### 2. Requirement NOT met (retry budget exhausted, or MCP reachable with only other projects):

   - Check whether any OTHER task remains that could run headless: `grep -L '\*\*Requires:\*\*' "$BACKLOG_ROOT"/todo/*.md` (cheap, deterministic — reads no task body beyond the marker).
   - **Some headless task exists** → defer this one and let the loop continue:
     ```bash
     python3 .claude/scripts/backlog-ops.py defer <NNN>
     ```
     Then output: `DEFERRED — task <NNN> requires unity-editor (not live); moved to the tail of TODO. Re-run picks the next task.` and STOP this iteration (the loop runner treats it as a normal iteration end and starts the next one).
   - **Every remaining TODO task requires the editor** → pause the loop exactly like the empty-backlog flow: write `EDITOR_REQUIRED` into `$BACKLOG_ROOT/state` (no commit — it is inside `.git/`), and output: `EDITOR_REQUIRED — all remaining tasks need a live Unity Editor. Open the Editor (and re-run in current mode — worktree mode can never satisfy this), delete $BACKLOG_ROOT/state, then re-run.` (Without the state write, a headless loop would defer-cycle forever.)

---

## STEP 2 — Get on the work branch

### 2.0 — Current mode: SKIP this whole step

If `$AGENT_MODE = current` (the default), you are already in the dev's checkout on the
branch they chose. **Do not checkout, do not create a branch, do not merge.**
`$WORK_BRANCH` is simply the current branch; you commit onto it in STEP 9. A branch
switch here would make the dev's open Unity Editor reimport the whole project for
nothing, and a separate agent branch buys no isolation when both share one directory.
Go straight to STEP 3.

Everything below applies to `$AGENT_MODE = worktree` only.

### 2a. Resolve the base branch (worktree mode)

```bash
[ "$HAS_REMOTE" = "1" ] && git fetch origin      # local-only repo: nothing to fetch
```

Resolve in this order:

```bash
BASE_BRANCH="${AGENT_BASE_BRANCH:-}"  # captured by loop runner
if [ -z "$BASE_BRANCH" ]; then
  BASE_BRANCH=$(git rev-parse --abbrev-ref HEAD)     # ad-hoc single /run-backlog invocation
fi
# Starting from an agent branch is ALLOWED (a previous loop leaves HEAD there). Never
# stop for it — resolve the real base from the recorded config, then the repo default.
case "$BASE_BRANCH" in
  ""|HEAD|agent/dev|agent/dev-*)
    BASE_BRANCH=$(git config "$(python3 .claude/scripts/project_profile.py gitConfigPrefix).agentBaseBranch" 2>/dev/null || true) ;;
esac
case "$BASE_BRANCH" in
  ""|agent/dev|agent/dev-*) BASE_BRANCH=$(python3 .claude/scripts/project_profile.py defaultBaseBranch) ;;   # profile default, last resort
esac
```

- **No stop here.** An agent branch / detached `HEAD` is a normal starting point, not an error — the fallback chain above always yields a usable base. Log which branch was resolved and continue.

**Work branch name.** Use `$WORK_BRANCH` from the loop runner. If it is unset, derive it
exactly the way the runner does — `agent/dev-` plus the base with every `/` replaced by `-`:

```bash
WORK_BRANCH="${AGENT_BRANCH:-agent/dev-$(printf '%s' "$BASE_BRANCH" | tr '/' '-')}"
```

The slashes MUST be flattened. Git stores refs as files, so a branch named `Dev1` and a
branch named `Dev1/agent/dev` cannot coexist — creating the second fails with
`cannot lock ref ...: 'refs/heads/Dev1' exists`. Never build the name by appending a
path segment to the base branch.

**Merge source = "latest":** prefer the remote tip `origin/$BASE_BRANCH` whenever it exists (freshest); fall back to the local `$BASE_BRANCH` when the base has no remote tracking branch — which is always the case when `HAS_REMOTE=0`.

### 2b. Sync the worktree branch

The loop runner already created (or reused) the worktree and put it on `$WORK_BRANCH`,
and your cwd is that worktree. You only need to bring the base in:

```bash
if [ "$HAS_REMOTE" = "1" ]; then
  git pull origin "$WORK_BRANCH"                   # skip if the remote branch doesn't exist yet
fi

if [ "$HAS_REMOTE" = "1" ] && git show-ref --verify --quiet "refs/remotes/origin/$BASE_BRANCH"; then
  git merge --no-edit "origin/$BASE_BRANCH"        # freshest tip
else
  git merge --no-edit "$BASE_BRANCH"               # local-only repo, or base never pushed
fi

git config "$(python3 .claude/scripts/project_profile.py gitConfigPrefix).agentBaseBranch" "$BASE_BRANCH"   # reporting convenience only, not next-run input
```

- If the merge reports **conflicts** → STOP with:
  `BASE_MERGE_CONFLICT — merging <BASE_BRANCH> into <WORK_BRANCH> conflicts. Resolve manually, commit, then re-run.`
  DO NOT auto-resolve.
- **Never `git checkout` another branch here.** Git refuses to check out a branch that is
  already checked out in another worktree, and switching would desync the runner.

> Branch model: each loop run captures whichever non-agent branch is checked out when the runner starts. The repo-local `<gitConfigPrefix>.agentBaseBranch` config is updated only as a reporting convenience; it does not select the next loop's base. The user manually merges `$WORK_BRANCH -> <base branch>` after running the manual verification steps.

---

## STEP 3 — Mark IN PROGRESS

Run the deterministic transition — ONE call does the `git mv` todo → in-progress AND the BACKLOG.md bullet move (preserving the `[TIER]` bracket the loop runner reads), then self-lints:

```bash
python3 .claude/scripts/backlog-ops.py start <NNN>
```

- The JSON result echoes the new `path` plus a `lint` block. If `lint.ok = false`, the errors are pre-existing index damage (hand-edit or merge residue) — fix them before writing any code.
- DO NOT hand-edit `BACKLOG.md` or `git mv` the task file yourself for this transition.
- (Resume case: if `pick` returned `state: "in-progress"`, the transition already happened in a previous run — skip this step.)

Do this **before** writing any code.

---

## STEP 4 — Understand context

Before writing code:

### 4a. Probe CodeGraph availability (ONCE — determines exploration method for the entire task)

```
mcp__codegraph__codegraph_search(query="FeatureBaseController", limit=1)
```

- **Success** → set `CODEGRAPH_UP = true`. ALL code exploration in this task MUST use CodeGraph (see 4c). Grep/Read for symbol lookups when CodeGraph is available = **wasted tokens**.
- **Error / timeout / tool not found** → set `CODEGRAPH_UP = false`. Fall back to Grep/Read efficiently (4d).

Carry `CODEGRAPH_UP` forward — it is passed into every reviewer prompt in STEP 6/7 so reviewers use the same method and the orchestrator can flag grep-fallback when CodeGraph was actually up.

### 4b. Read project context

1. `CLAUDE.md` is already auto-injected into your context by the Claude Code CLI at session start — DO NOT Read it again (redundant read = wasted tokens). Just apply its rules.
2. Read the files in `.claude/rules/` — `code-style.md`, `core-system.md`, `data-persistence.md`, `third-party.md`. (`output-format.md` is only for text responses, do not apply it in this autonomous loop.)
3. Read `SKILL.md` files in `.claude/skills/` that correspond to the system being touched (see mapping in `.claude/agents/code-reviewer.md` under "Skill-specific conventions").
4. Read the files listed in the **Related files** of the task (for any detail `codegraph_explore` trimmed).
5. If `$CONTEXT_DOCS != none` → Read those design docs now. They are the source of truth for concrete values (CSV columns + 6 resource fields, economy numbers, Manager Type, event tables) — NEVER re-invent a value the mapping/TechSpec already states. This is the one sanctioned exception to "read exactly one task file": the task explicitly links its design context.
6. Read other necessary files to understand the surrounding context.

### 4c. CodeGraph exploration (when CODEGRAPH_UP = true)

This project has a CodeGraph MCP index (`mcp__codegraph__*` tools) pre-indexing 1900+ files. Use it **instead of** Grep or Read loops for structural information:

| What you need | Tool |
|---|---|
| How does X work / survey an area / read several related files at once | `codegraph_explore` (primary — usually the only call needed) |
| Find where a class/method is defined | `codegraph_search` |
| Understand what a class does + who calls it | `codegraph_context` |
| Check what a method calls (detect missing dependency, wrong call) | `codegraph_callees` |
| What would break if I change X? | `codegraph_callers`, then `codegraph_explore` for the wider flow |
| Trace a flow from trigger → output (e.g. button → save) | `codegraph_trace` |
| List files under a directory | `codegraph_files` |

**Rules (enforced — violations waste 40-60% more tokens):**
- NEVER Grep for a class/method name — `codegraph_search` / `codegraph_explore` is faster and returns kind + location + signature.
- NEVER chain multiple Read calls across different files when `codegraph_explore` returns them grouped.
- Only fall back to Grep for **literal string content**: hardcoded text, localize key strings, CSV values, log messages.
- **New files** (created in this same implementation) are not yet indexed (~1s file-watcher lag) — Read them directly instead of querying CodeGraph.

### 4d. Grep/Read fallback (when CODEGRAPH_UP = false)

- Prefer `Grep` with precise patterns over blind reads.
- Read files only after Grep confirms the symbol exists there.
- Minimize Read calls — read only the relevant section.

DO NOT skip this step. This project's conventions are strict — violations will be blocked by the code-reviewer in STEP 5.

---

## STEP 5 — Implement task

### 5.0 — Workflow-backed shortcut (run FIRST if `$WF_CMD != none`)

If STEP 1 found a `**Backed by workflow:**` line, the scaffold is specified deterministically by a `/new-*` workflow — do NOT re-derive it free-form.

1. **Read the workflow file inline**: `.claude/commands/<name>.md` (e.g. `new-package.md`). If that file does not exist, STOP with `WORKFLOW_MISSING` — do not improvise a scaffold the workflow was supposed to define. Follow its steps **inline as instructions** — do NOT invoke it as a slash command (you are already mid-orchestration; the Skill tool would fork the flow). Read any reference files / docs the workflow points to (e.g. `FeatureBaseController.cs`, the guide it names, the example features it names).
1b. **Context docs as 0th-priority workflow input:** if `$CONTEXT_DOCS` includes a `TechSpec/<Name>-Implementation.md`, treat it as the workflow's structured input — for `/new-feature` this IS the "TechSpec attached file" its step 2 gives 0th priority (sections 10.1–10.7 drive Sub-Features, Save Data, CSV Columns, Events, Registration Points). Use the mapping rows already pasted in the task body first; open the full doc when they lack a detail.
2. **Execute the workflow** using `$WF_ARGS` as its `{{args}}` / argument input. Generate exactly the files, registrations, and conventions the workflow prescribes (controller/manager, `PlayerDataManager`/`CsvAssetDir`/`DataManagerAutoGenerate` registrations, the right CSVs, naming rules, ID ranges, etc.). Honor every "DO NOT" the workflow states (e.g. enemy skills do NOT touch `SkillAffectConfig.csv`/`SkillInfo.csv`). If the `**Custom delta:**` says a workflow step is deferred to another queued task (e.g. "SKIP workflow step 8 — prefab is task NN"), skip that step and note it in the DONE summary instead of executing it.
3. **Apply the `**Custom delta:**`** from the task body (the logic/wiring/balance beyond the scaffold). For a pure scaffold the delta is `none` — **except a cheat list** (`name · label · method`), which is a legal delta on a pure scaffold; implement it per the cheat bullet below.
4. Then continue with the normal rules below (conventions, no extra features) and proceed to staging (5a) + compile check (5b).

The workflow's own CHECKLIST is part of the acceptance criteria — make sure every item is satisfied before staging. Quality gates (STEP 6/7) still run in full per `$TASK_TIER`.

If `$WF_CMD = none`, skip this section and implement free-form below.

---

Write code to fulfill the task. Rules:
- Follow exactly the conventions in `.claude/rules/`:
  - Inherit `FeatureBaseController` for UI features, `BaseNotification` for notifications.
  - Use `UIManager.Show/Hide` instead of `SetActive`.
  - Use `TimeManager` instead of `DateTime.Now`.
  - Use `UniTask` instead of `Coroutine`/`Task`. NO `async void`.
  - Save data using `DataPlayer` via `PlayerDataManager.[Module]`; include a `SetupDefaultData()` fallback when adding fields.
  - Use `TigerForge` + `EventName` constants for cross-system events.
  - DOTween: `OnComplete`/`Kill`; UI tweens must use `SetUpdate(true)`.
  - Localize all user-facing text.
  - Magic numbers → CSV config or `SCREAMING_CONST`.
- **Cheat affordance** — when the task carries `[CHEAT]` on its `**Guardrails:**` line, implement it as specified: `public Cheat_*` methods in a `#region Cheats` on the Controller (`[TabGroup("Cheats")] [Button]`, ending in a UI refresh) + `ButtonNormal` instances under the prefab's **inherited** `ButtonCheatMenu/MenuParent`, wired to those methods. Read [.claude/skills/feature-cheat/SKILL.md](../feature-cheat/SKILL.md) first — it has the exact sizes/labels/guids and the `unity_execute_code` recipe for persistent `onClick` wiring; the design must mirror `DailyLoginV2.prefab` / `Equipment.prefab`. Never re-instantiate `ButtonCheatMenu` and never localize cheat labels. **If the task has no `[CHEAT]` tag, do NOT add cheats on your own** — that is a planning decision, not an implementer one. **Editor absent:** write the `Cheat_*` code, leave the prefab buttons undone, and say so explicitly in the DONE summary + as a manual verify step (do not silently drop the criterion).
- No new abstractions, no extra features beyond the task spec.
- No comments unless the WHY is non-obvious.
- Do not hardcode API keys, secrets, or tokens.
- Backend writes must go through Cloudflare Workers; reads can directly access Supabase with the anon key.

**There is no `npm run lint` in a Unity project.** Compilation is only checked when the user opens the Editor. Rely on the quality gates below to catch errors.

### 5a — Stage changes

When implementation is done, **stage** all changes:
```bash
git add -A
```

**Do not commit yet.** Quality gates run on the staged diff. Commit only after all gates pass.

### 5b — Unity compile check (3-tier, mandatory) — runs BEFORE the quality gates

> **Worktree mode: skip this step entirely.** Record
> `compile-check: skipped (worktree mode — no Editor, no .sln)` and go to STEP 6.
> Not one tier can run there, and each fails in a way that would mislead you:
> Tier 1 would find the Editor attached to the **dev's** checkout and compile the
> wrong code; Tier 2 has no `.sln`/`.csproj` (both gitignored — Unity generates
> them, and copying the dev's in is worse than useless because they carry absolute
> paths back into the dev's `Library/`); Tier 3 would build a multi-GB `Library/`
> from scratch inside the worktree. This is the accepted cost of worktree mode —
> STEP 10 must therefore demand `/compile-check` as the first manual step.

After staging, attempt compile verification in order. Stop at the first tier that **runs successfully** (regardless of whether it finds errors or not). Only skip if **all 3 tiers cannot run**. This is the early gate — Unity projects have no `npm run lint`, so a compile pass here keeps the reviewers (STEP 6+) from wasting tokens on code that does not build.

> **Standalone twin:** this same 3-tier logic is packaged as the hand-invokable `/compile-check` skill (`.claude/skills/compile-check/SKILL.md`) for ad-hoc use outside the loop. Keep the two in lockstep when either changes.

> **Platform note (macOS/Linux):** Tier 1 (Unity Editor MCP) is platform-agnostic and is the preferred path. Tier 2 `dotnet build` runs the same in bash. Tier 3's snippet is PowerShell; on macOS run the equivalent in bash (the editor binary lives at `<UnityHub>/Editor/<ver>/Unity.app/Contents/MacOS/Unity`) or SKIP if it cannot run — the manual verify steps remain the safety net.

For any tier that runs and finds errors, enter the fix loop before trying the next tier.

**Fix loop (shared across all tiers, max 2 rounds):**
1. Read the error output and fix the code.
2. `git add -A` to re-stage.
3. Re-run the same tier's compile check.
4. If errors remain after 2 rounds → output exactly:
   `COMPILE_BLOCKED — Unity compilation errors remain after 2 fix rounds. Manual intervention required. Run /run-backlog again after fixing, or run python3 .claude/scripts/backlog-ops.py demote <NNN> to abandon (returns the task to the head of TODO).`
   DO NOT proceed. Stop.

---

**Tier 1 — Unity Editor MCP (instant, preferred)**

1. Force a refresh/recompile so the Editor picks up the staged edits: `mcp__unity__unity_execute_menu_item("Assets/Refresh")`.
2. Poll `mcp__unity__unity_editor_state` until the Editor is **NOT compiling** (never read errors mid-compile — the result is stale).
3. Read errors: `mcp__unity__unity_get_compilation_errors` (severity: error).

- **No errors** → proceed to STEP 6.
- **Errors returned** → enter fix loop. If COMPILE_BLOCKED → stop.
- **Tool unavailable (Editor not open / MCP not connected)** → proceed to Tier 2.

---

**Tier 2 — dotnet build (~10–40 s)**

```powershell
dotnet build "$(python3 .claude/scripts/project_profile.py solutionFile)" --nologo -v q 2>&1
```

Parse stdout/stderr for lines containing `error CS`.

- **No `error CS` lines** → proceed to STEP 6.
- **`error CS` lines found** → enter fix loop. If COMPILE_BLOCKED → stop.
- **`dotnet` not found / non-compile exit (e.g., .sln stale, SDK mismatch)** → proceed to Tier 3.

---

**Tier 3 — Unity batch mode (~60–180 s)**

Get Unity install path:
```
mcp__unity__unity_hub_list_editors  →  pick version matching this project
```

Run:
```powershell
$unityExe = "<path from hub>/Editor/Unity.exe"
$logFile  = ".claude/tmp/backlog/unity-compile.log"
New-Item -ItemType Directory -Path .claude/tmp/backlog -Force | Out-Null
& $unityExe -batchmode -nographics -projectPath (Resolve-Path .) -logFile $logFile -quit
Get-Content $logFile | Select-String "error CS"
```

- **No `error CS` lines in log** → proceed to STEP 6.
- **`error CS` lines found** → enter fix loop. If COMPILE_BLOCKED → stop.
- **Unity.exe not found / process fails for non-compile reason** → SKIP.

---

**SKIP** (only when all 3 tiers cannot run):
Note `compile-check: skipped (all 3 methods unavailable)` in the DONE summary Quality gates section. Proceed to STEP 6.

> **Rule:** Skip only when the compile check *cannot run*. `COMPILE_BLOCKED` only when the check *runs and finds errors* that survive 2 fix rounds.

---

## STEP 6 — Quality Gate: Code Review + Security Review (parallel when sensitive)

**Purpose:** Before committing, have an independent reviewer check the diff against the task spec + audit security if the task touches a sensitive surface.

### 6a + 6b. Snapshot the staged diff and run preflight — ONE call

```bash
py .claude/scripts/backlog-snapshot.py --pretty       # Windows (python3 thường là Store stub — dùng `py`)
# python3 .claude/scripts/backlog-snapshot.py --pretty  # macOS/Linux
```

This replaces the old six-command cluster (`git diff --staged --name-only` → `git diff --staged` → `backlog-preflight` → `git add -A` → re-run preflight → re-capture the diff). **Use it instead of running those by hand.** It returns `files[]`, `file_count`, `stat`, `diff_path`, `diff_bytes`, and the full `preflight` JSON in one payload.

Why it is one call, and why the diff is a path: each Bash call re-reads the entire conversation context from cache, so shell cost tracks the **number of calls**, not the size of their output — the 061 run spent ~10.6M cache-read tokens (~36% of the task) across 103 Bash calls. And a diff pasted into stdout is re-read by every later call for the rest of the task; on disk it costs nothing until someone opens it. Read `diff_path` only when you actually need the hunks, and pass that path to reviewers rather than the bytes.

Exit codes: `0` ok · `2` no staged changes · `3` `preflight.summary.has_blocking_definite = true` · `1` internal error (payload has `error`).

On exit `2`:
- Output: `NO_CHANGES — implementation produced no diff. Task may already be complete or implementation skipped.`
- Stop. DO NOT commit. The user needs to manually review if the task setup was incorrect.

Flags: `--stage` runs `git add -A` first (use it for each preflight-fix round instead of a separate `git add`); `--label <name>` names the diff file (`review-before` / `review-after` for §6-fix); `--no-preflight` captures the diff only.

The `preflight` object in the payload is the same JSON `backlog-preflight.py` emits standalone (the wrapper subprocesses it, so rules stay in one place). It contains:
- `summary.has_blocking_definite`: whether there is a critical finding based on hard rules.
- `summary.definite_critical_count`: the number of critical findings that can be fixed before LLM review.
- `findings[]`: each finding contains `rule`, `severity`, `confidence`, `file`, `line`, `evidence`, `suggestion`.
- `sensitive.value` + `sensitive.reasons[]`: used as input for the security-auditor decision.

Decision:
- If `summary.has_blocking_definite = true` and `summary.definite_critical_count <= 5`:
  1. Fix findings with `severity=critical` + `confidence=definite` using orchestrator reasoning. DO NOT blind grep-replace.
  2. Re-snapshot with `py .claude/scripts/backlog-snapshot.py --stage --pretty` (`--stage` does the `git add -A`, re-runs preflight, and re-captures the diff — one call, not three).
  3. Repeat for a maximum of 2 preflight-fix rounds before spawning reviewers.
- If `summary.definite_critical_count > 5` or after 2 preflight-fix rounds `has_blocking_definite` remains `true`:
  - Print a clear report containing all remaining definite critical findings.
  - Output exactly: `PREFLIGHT_BLOCKED — deterministic critical findings require manual intervention before LLM review.`
  - DO NOT commit. DO NOT proceed. Stop.
- Findings with `confidence=contextual` DO NOT automatically block reviewers. Paste the raw preflight JSON into the reviewer prompt to let the reviewer/qa-verifier decide based on context.

The final `--stage` snapshot of the fix loop already re-captured the diff, so there is nothing to re-capture here — carry its `files[]` / `diff_path` forward into §6c.

### 6c. Detect sensitive files

Security review is for **value-bearing / trust-boundary** surfaces, NOT for plain progress save. Set `$SENSITIVE = true` if any trigger below matches (case-insensitive) OR the preflight JSON has `sensitive.value = true`, else `false`.

**Real backend / trust-boundary triggers (always spawn the security-auditor — do NOT weaken these):**

- `<sourceRoot>/**/Backend*`, `*Supabase*`, `*Cloudflare*`, `*Worker*` (any server/backend write path)
- `<sourceRoot>/**/Purchase*`, `*IAP*`, `*Receipt*`, `*Payment*`
- `<sourceRoot>/**/Auth*`, `*Login*`, `*Token*`, `*Session*`
- `<sourceRoot>/**/Leaderboard*`, `*Ranking*`, `*Social*`
- `<sourceRoot>/**/AntiCheat*`, `*Validation*`, `*Integrity*`
- New files containing strings that look like credentials (regex `[A-Z0-9_]{3,}_(KEY|SECRET|TOKEN|PASSWORD)`, **case-sensitive** — UPPER_SNAKE only, matching the preflight `credential` rule; `player_token`-style lowercase identifiers do NOT count)
- `*.env*`, `*.config`, `*Secrets*`, `*Credential*`

**Value-bearing writes (inspect the diff CONTENT, not just the filename):** code that **grants or spends currency**, **grants owned items** (typically through the project's reward service under `<featuresRoot>`), writes **leaderboard / competitive** values, or writes to the **server** (Cloudflare Worker / Supabase upsert/delete). A `DataPlayer` / `PlayerDataManager.[Module]` save is sensitive ONLY when it carries such value.

> **NOT security-sensitive by itself:** plain progress save (depth, level, unlock flags, settings) through `PlayerDataManager.[Module]` / `DataPlayer` with a `SetupDefaultData()` fallback. Save-tampering of non-value progress data is low-impact and is already covered by the deterministic preflight save rules (`PlayerPrefs`, `Save()` in Update, `DataManager` write) + qa-verifier's `[PERSIST-RESTART]` check — it does NOT warrant spawning the security-auditor. Only escalate a save task to security review when it grants/spends a value-bearing resource per the list above.

### 6c-bis. Detect perf-sensitive diff

Set `$PERF_SENSITIVE = true` if ANY `*.cs` file in the diff touches a runtime hot surface:

- A per-frame method (`Update` / `FixedUpdate` / `LateUpdate`) or a loop over a gameplay collection (enemies, projectiles, etc.).
- Spawn/despawn: `Instantiate(`, `Destroy(`, or object-pool calls — especially under `<gameplayRoot>` (enemies, projectiles, VFX, floating damage text).
- List / scroll / UI binding or layout: `LoopListView2` binding, `LayoutRebuilder`, per-frame `Canvas`/`SetActive` churn.
- Allocation on a hot path: `new List/Dictionary/HashSet/StringBuilder`, LINQ (`.Where/.Select/.ToList`), or string concatenation in the contexts above.
- The preflight already flagged a `mobile-performance` rule.

If the diff is **only** non-`.cs` (prefab / scene / CSV / `.md` / art) OR pure data/POCO/constants with no gameplay-loop touch → `$PERF_SENSITIVE = false`.

### 6d. Spawn reviewer subagent(s) — tier-gated

Read `$TASK_TIER` extracted in STEP 1 from the BACKLOG.md bullet.

---

**Tier XS — skip code-reviewer + qa-verifier (security is sensitivity-gated, NOT tier-gated)**

Preflight + compile-check (STEP 5b) are sufficient for zero-logic tasks (CSV tweaks, constant updates, dead code removal). No code-reviewer, no qa-verifier.

**Exception — `$SENSITIVE = true` (from STEP 6c):** spawn the **`security-auditor`** (default model, standard prompt body below, `SCOPED_DIFF`) even at XS. Sensitivity comes from the diff, not the tier — an XS-labeled CSV tweak that changes IAP pack contents or touches `Purchase*`/`Auth*` is still a value-bearing change. Verdict `block` → auto-fix loop (max 2 rounds, as in 6e); still `block` → `REVIEW_BLOCKED`.

Generate `manual_verify_steps` directly from the task spec's **Required verification steps** section. Proceed to STEP 8 (Mark DONE).

Quality gates entry for DONE summary:
```
- Code review: skipped (XS tier)
- Security review: <pass|warn if $SENSITIVE, else: skipped — no sensitive files>
- QA verify: skipped (XS tier)
```

---

**Tier S — lightweight review, no qa-verifier (security is sensitivity-gated, NOT tier-gated)**

Spawn **`code-reviewer`** with `model: "sonnet"`. In the **same message** (parallel), also spawn **`performance-reviewer`** with `model: "sonnet"` if `$PERF_SENSITIVE = true`, and **`security-auditor`** (default model — do not downgrade it) if `$SENSITIVE = true`. Do NOT spawn qa-verifier. An S task touching `Purchase*`/`Auth*`/value-bearing writes gets the same security audit as M/L.

```
Agent({
  description: "Code review backlog task (S tier)",
  subagent_type: "code-reviewer",
  model: "sonnet",
  prompt: <<see prompt body below>>
})
```

**Performance Reviewer** (only when `$PERF_SENSITIVE = true`, same message as code-reviewer):
```
Agent({
  description: "Performance review backlog task (S tier)",
  subagent_type: "performance-reviewer",
  model: "sonnet",
  prompt: <<see prompt body below>>
})
```

**Security Auditor** (only when `$SENSITIVE = true`, same message — default model, no `model:` override):
```
Agent({
  description: "Security audit backlog task (S tier)",
  subagent_type: "security-auditor",
  prompt: <<see prompt body below>>
})
```

→ all `pass` / `warn` → proceed to STEP 8. Generate `manual_verify_steps` from the task spec's **Required verification steps** section directly.
→ any `block` → auto-fix loop (max 2 rounds, re-spawn the blocking reviewer(s) — code/perf with `model: "sonnet"`, security with its default model), then STEP 8.

---

**Tier M / L — full pipeline**

Always spawn the **`code-reviewer`** subagent (model: opus, default). In the **same message** (parallel tool-use block), also spawn **`performance-reviewer`** if `$PERF_SENSITIVE = true`, and **`security-auditor`** if `$SENSITIVE = true`. All spawned reviewers run in parallel.

**Code Reviewer:**
```
Agent({
  description: "Code review backlog task",
  subagent_type: "code-reviewer",
  prompt: <<see below>>
})
```

**Performance Reviewer** (only when `$PERF_SENSITIVE = true`):
```
Agent({
  description: "Performance review backlog task",
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

**Prompt packets — give each reviewer only what its lens needs (token discipline).** Do NOT paste the full task file + full preflight JSON + full staged diff into every reviewer. Build the shared blocks ONCE:

- `TASK_PACKET` = the task's **title + Description + Context & Constraints + Completion criteria + the `**Guardrails:**` tag line** only. Do NOT paste the full task-file boilerplate or the guardrail catalog text (tags resolve to `.claude/backlog-templates/_GUARDRAILS.md`).
- `PREFLIGHT_PACKET` = the preflight `findings[]` array + `summary`. If `findings` is empty, write `preflight: clean (no findings)` instead of pasting the whole JSON.
- `FULL_DIFF` = the `diff_path` from the §6a+6b snapshot — pass the **path** and let the reviewer `Read` it, rather than pasting the diff into the prompt (a large diff pasted here is re-read from cache by every later tool call for the rest of the task; 060's diff alone was ~170KB). `SCOPED_DIFF(globs)` = `git diff --staged -- <globs>` for the files relevant to that reviewer.

Per-reviewer prompt body (applies to S / M / L tiers):
> CODEGRAPH_UP=<true|false from STEP 4a>
> NOTES:
> - Guardrail tags in the task's `**Guardrails:**` line (e.g. `[SAVE]`, `[ASYNC]`, `[BACKEND-SECURITY]`) are defined in `.claude/backlog-templates/_GUARDRAILS.md` — read that file for the exact check + verify recipe before judging a tag. The TASK_PACKET lists tags only, not the full block text.
> - If `CODEGRAPH_UP=true`, use CodeGraph for structural symbol/flow lookups (Grep only for literal text); report your `tool_method` in the verdict.
> - Your DIFF may be scoped to your lens (see table below). If you need surrounding context, read it directly via Read/Grep/CodeGraph — do NOT treat the scoped diff as the entire change.
>
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
>
> Review according to the instructions in the agent definition and return a JSON verdict.

| Reviewer | Diff to pass |
|---|---|
| `code-reviewer` | `FULL_DIFF` (needs every changed file). |
| `performance-reviewer` | `SCOPED_DIFF` of `*.cs` only — skip prefab/scene/asset/CSV/`.md` files (no perf signal there). If only non-`.cs` files changed, you should not have spawned it (see STEP 6c-bis). |
| `security-auditor` | `SCOPED_DIFF` of the sensitive files that set `$SENSITIVE` + any `*.cs` that grants/spends value or writes to the backend — skip prefab/scene/art diffs. |

**NOTE (M/L only):** Spawn all selected reviewers (code-reviewer + performance-reviewer if `$PERF_SENSITIVE` + security-auditor if `$SENSITIVE`) in **one tool-use block** (multiple Agent calls in the same response) to run them in parallel. DO NOT run them sequentially.

**Tool-efficiency tracking:** Record each reviewer's `tool_method` field from its verdict JSON (e.g. `code-reviewer: codegraph, perf-reviewer: grep-fallback`) for the DONE summary (STEP 8). If `CODEGRAPH_UP=true` but any reviewer returns `tool_method="grep-fallback"` **without** reporting a CodeGraph tool error, re-spawn that reviewer once with the extra instruction: *"CodeGraph is available. Re-run structural lookups with CodeGraph; use Grep only for literal text scans."* Treat the second verdict as authoritative.

### 6e. Read verdicts and decide

Once all reviewers return, parse the JSON.

- **All are `pass` or `warn`** → proceed to STEP 7 (Verify).
- **Any is `block`** (code-reviewer, performance-reviewer, or security-auditor) → enter the **auto-fix loop**:

**Auto-fix loop (max 2 rounds):**

- **Round 1**:
  1. Read all `block` and `critical` findings from EVERY reviewer that returned a block.
  2. Capture the current staged diff snapshot before fixing (creates the dir and writes the file — no preflight needed for a baseline):
     ```bash
     py .claude/scripts/backlog-snapshot.py --label review-before --no-preflight
     ```
  3. Fix the code yourself (orchestrator = implementer).
  4. Re-stage, re-run preflight, and re-capture the diff in one call:
     ```bash
     py .claude/scripts/backlog-snapshot.py --stage --label review-after --pretty
     ```
     If `has_blocking_definite = true` remains (exit `3`), fix definite critical findings before re-spawning reviewers (max 2 preflight-fix rounds as in STEP 6a+6b).
  5. Build the delta prompt input:
     ```bash
     git diff --no-index -- .claude/tmp/backlog/review-before.diff .claude/tmp/backlog/review-after.diff
     ```
     `git diff --no-index` may return exit code `1` when there is a diff; this is expected, not a failure.
  6. Re-spawn the same reviewers (in parallel if both were spawned initially) with:
     - Previous blocking findings JSON.
     - Updated preflight JSON.
     - Delta diff between `review-before.diff` and `review-after.diff`.
     - Full staged diff only if the delta lacks enough context or the reviewer needs to verify side effects — pass `diff_path`, not the bytes.

- **Round 2**: same as Round 1 if reviewers still return `block`.

- **After Round 2** if still `block`:
  - Print a clear report for the user containing:
    - Each remaining `block`/`critical` finding from each reviewer (file, line, issue, suggestion)
    - What was fixed in Rounds 1 and 2
    - Current `git status` and staged diff size
  - Output exactly: `REVIEW_BLOCKED — manual intervention required. Run /run-backlog again after fixing, or run python3 .claude/scripts/backlog-ops.py demote <NNN> to abandon (returns the task to the head of TODO).`
  - DO NOT commit. DO NOT proceed. Stop.

---

## STEP 7 — Quality Gate: Verify

**Purpose:** Confirm that the code has resolved EVERY item in the "Completion criteria", not just passed convention checks.

### 7a. Spawn qa-verifier subagent

```
Agent({
  description: "Verify backlog implementation",
  subagent_type: "qa-verifier",
  prompt: <<see below>>
})
```

Prompt body:
> CODEGRAPH_UP=<true|false from STEP 4a>
> NOTE: guardrail tags on the task's `**Guardrails:**` line (e.g. `[SAVE]`, `[ASYNC]`, `[BACKEND-SECURITY]`) are defined in `.claude/backlog-templates/_GUARDRAILS.md` — read that file for the exact check + verify recipe. If `CODEGRAPH_UP=true`, use CodeGraph for structural lookups and report your `tool_method` in the verdict.
>
> TASK SPEC (focus especially on the "Completion criteria" and the `**Guardrails:**` tag line — each tag resolves to a check in `.claude/backlog-templates/_GUARDRAILS.md` — plus the section "Required verification steps after loop stops (manual)"):
> ```
> <paste full content of backlog/in-progress/<NNN-TIER-slug>.md>
> ```
>
> PREFLIGHT (trimmed packet — `findings[]` + `summary` after all review-fix rounds; if empty, `preflight: clean (no findings)`):
> ```
> <PREFLIGHT_PACKET>
> ```
>
> STAGED DIFF (`git diff --staged` — qa-verifier cross-checks every criterion, so it gets the FULL diff):
> ```
> <FULL_DIFF>
> ```
>
> Run verification according to the instructions in the agent definition and return a JSON verdict + criteria_check + manual_verify_steps.

### 7b. Read verdict and decide

- **`pass`** → proceed to 7c/7d, then STEP 7.5 (runtime smoke).
- **`warn`** → proceed to 7c/7d, then STEP 7.5 — note the `warn` findings in the DONE summary.
- **`fail`** → enter the **auto-fix loop** (same shape as STEP 6e, max 2 rounds):
  - Read `missed_criteria`.
  - Capture `git diff --staged > .claude/tmp/backlog/verify-before.diff`.
  - Fix the code, `git add -A`.
  - Re-run preflight. If there are definite critical findings, fix them before re-spawning qa-verifier.
  - Capture `git diff --staged > .claude/tmp/backlog/verify-after.diff`.
  - Re-spawn qa-verifier with previous `missed_criteria`, latest preflight JSON, delta diff between before/after, and full staged diff only when final context is needed.
  - After Round 2 if still `fail`: print a clear report and exit with:
    `VERIFY_BLOCKED — manual intervention required. Run /run-backlog again after fixing, or run python3 .claude/scripts/backlog-ops.py demote <NNN> to abandon (returns the task to the head of TODO).`
  - DO NOT commit.

### 7c. Capture manual verify steps

The QA-verifier output has a `manual_verify_steps` field — a list of steps the user must run manually. Capture this list exactly to paste into the DONE summary in STEP 8 and the REPORT in STEP 9. DO NOT modify or shorten it.

### 7d. Final deterministic preflight before DONE

Before moving the task to DONE, snapshot + preflight one last time on the full staged diff:

```bash
py .claude/scripts/backlog-snapshot.py --stage --label final --pretty
```

- Exit `0` (`summary.has_blocking_definite = false`) → proceed to STEP 7.5 (runtime smoke).
- Exit `3` (`summary.has_blocking_definite = true`) → fix definite critical findings, re-run the same command (`--stage` re-stages for you), and re-run qa-verifier if the fix might affect completion criteria. If it cannot be resolved cleanly after 2 rounds, stop with:
  `PREFLIGHT_BLOCKED — deterministic critical findings require manual intervention before DONE.`

---

## STEP 7.5 — Quality Gate: Runtime smoke (M / L only, orchestrator-side)

**Purpose:** every gate so far only READS the diff — none observes the game running. This gate boots the game in the Editor and fails on runtime errors (NRE storms, exceptions, broken economy/save flows that no diff reader can catch). It automates the first slice of what "Required verification steps" otherwise defers entirely to the user.

Run it for **M / L** after qa-verifier passes (STEP 7) and the final preflight (7d). **XS / S skip it** (they already routed straight to STEP 8 in STEP 6d). **You run it yourself** — the `mcp__unity__*` tools are available to the orchestrator only; do NOT spawn a subagent for this gate.

**Skip conditions (graceful — NEVER fail the task on these; record the reason in the DONE summary as `runtime-smoke: skipped (<reason>)`):**
- **`$AGENT_MODE = worktree` → skip WITHOUT probing.** Record `runtime-smoke: skipped (worktree mode)`. The only live Editor belongs to the dev's checkout; probing would find it and play-test the **wrong project** while reporting a pass for yours. Do not open Unity on the worktree either — the machine may not have the RAM for a second Editor, which is why worktree mode exists.
- Unity MCP not connected / no live Editor open for this project → probe `mcp__unity__unity_list_instances`; on fail/timeout record `runtime-smoke: skipped (Unity MCP not connected / Editor not open)`. **The headless loop must NEVER be hard-blocked by the absence of Unity** — absence is a skip, not a block.
- In current mode with several Editors listed, select by **project path**, never by project name — a worktree from an earlier run may still be open under the same name.
- The staged diff has no runtime surface (docs/`.md`, CSV comments only, editor-only `#if UNITY_EDITOR` code) → record `runtime-smoke: skipped (no runtime surface)`.
- **The Editor was live at gate entry but stops responding, or the game never boots, part-way through** → run the *mid-gate stall recovery* below. This is a **skip, not a block** — see that section for the exact reason strings.

> **The gate never strands the task.** Every exit from STEP 7.5 is exactly one of: `pass` · `warn` · a `skipped (…)` reason · `RUNTIME_BLOCKED` (code failed 2 fix rounds). "The Editor went quiet / the game never booted" is infrastructure, NOT a code failure — it can never end the iteration without one of those outcomes. Ending the turn describing a stall in prose, without reaching STEP 8, is a **silent failure**: the task stays in `backlog/in-progress/`, the loop runner reports `SILENT_FAIL`, and the whole run stops. If you are ever unsure which outcome applies, choose a `skipped (…)` reason and continue to STEP 8.

**Procedure:**
1. **Compile settled first** — poll `mcp__unity__unity_editor_state` until the Editor is NOT compiling. NEVER enter play mode with a compile pending: a mid-play domain reload wipes statics and produces a false NRE storm.
2. `mcp__unity__unity_console_clear` — start from a clean console.
3. Enter play mode (`mcp__unity__unity_play_mode`, play). Poll `unity_editor_state` until playing, then let the game boot **~20–30 s**. The default landing is `Assets/Scenes/BattleScene.unity` (gameplay); if the task's relevant scene is elsewhere (e.g. `Assets/Scenes/HomeScene.unity` for menu/boot flows), drive to it. Enemies spawning + the player auto-attacking is the baseline liveness signal.
   **Hard budget:** at most **8 polls / ~90 s** from "playing" to a booted game. Overrun → mid-gate stall recovery. Do NOT keep polling past the budget hoping it recovers — that is what burns the iteration.
4. **Execute the task spec's acceptance recipe** via `mcp__unity__unity_execute_code` wherever a completion criterion is expressible as a code assert (read a service value, confirm an object/prefab is live, invoke the flow under test). The C# payload MUST be ASCII-only (non-ASCII gets mangled in transit — route Vietnamese/localized strings through files on disk if ever needed).
   **Use the feature's own cheats to reach gated state.** When the task carries `[CHEAT]`, the fastest acceptance recipe is invoking the `Cheat_*` methods you just wrote (`FindObjectOfType<<Feature>Controller>().Cheat_NextDay()` etc.) instead of hand-rolling state setup — that is what they exist for, and calling them here also proves each button's target method resolves. Assert the visible state changed and the Console stayed clean; a `Cheat_*` method that throws is a gate FAIL like any other.
5. **`$SENSITIVE` invariant suite** (only when STEP 6c set `$SENSITIVE = true` — economy/save/reset surfaces). Run via `unity_execute_code`, snapshot-first so player state is always restored:
   - *Currency / reward conservation (net-zero):* read balance via `PlayerDataManager.[Module]` → grant X through `RewardManager` → spend X → assert balance == baseline (net-zero by construction).
   - *Save → load roundtrip:* save via `DataPlayer` (`PlayerDataManager.[Module]`) → read back the persisted data → simulate a restart (reload the module from disk) → assert persisted fields equal the pre-save live values (no data loss across the roundtrip).
   - *Reset scope* (only when the diff touches reset/restart/rebirth state): snapshot every touched module's data → invoke the reset → assert ONLY the modules the spec intends changed → restore all modules from the snapshot and re-save.
6. Read `mcp__unity__unity_console_log` (errors + exceptions only). **Any exception/NRE, or any error originating from code the diff touches → FAIL.** Error-level noise that is provably pre-existing and unrelated to the diff → record as `warn` with a one-line justification; do not fail on it.
7. `mcp__unity__unity_screenshot_game` → save to `.claude/tmp/backlog/runtime-smoke-<NNN>.png` and reference it in the DONE summary.
8. **Exit play mode** (`unity_play_mode`, stop) before doing anything else — never leave the Editor playing.

**Mid-gate stall recovery (the Editor was live at gate entry, then went quiet — or the game never booted):**

A **modal dialog blocks Unity's main thread**, and the MCP bridge needs that thread to answer — so an unanswered dialog is indistinguishable from a crash: every call just times out. A genuine Editor hang is rare; a modal ("The open scene(s) have been modified externally", a save prompt, an import error) is the common cause. Either way the loop must not sit there.

1. **Capture the evidence first — always, before any recovery attempt.** `mcp__unity__unity_screenshot_editor_window` → `.claude/tmp/backlog/stall-<NNN>.png`. If it returns, the frame shows the modal (or the frozen Editor) and names the cause for the human; reference the path in the DONE summary. If it times out too, note `no frame (bridge unresponsive)`.
2. **Re-probe once, bounded:** `mcp__unity__unity_list_instances`, then `mcp__unity__unity_editor_state`. Wait ~5 s between attempts, **max 3 attempts (~15 s)**. Do not exceed it.
3. **Classify and take exactly one exit:**
   - **Bridge answers again and the game booted** → continue the procedure from where it stalled. The stall was a transient import/compile spike.
   - **Bridge answers, `isPlaying = true`, but the game still has not booted after the step-3 budget** → the *game* is stuck, not the Editor. Exit play mode, and record `runtime-smoke: skipped (game did not boot within budget)`. Treat it as a **`warn`, never a silent pass**: name it in the DONE summary and add "boot the scene manually and confirm the game reaches gameplay" as the FIRST manual verify step. Do not spend fix rounds on it — a boot deadlock is usually pre-existing (see the domain-reload note below), not the diff's doing.
   - **Bridge still unresponsive after the retry budget** → record `runtime-smoke: skipped (Editor became unresponsive mid-gate)` and **continue to STEP 8**. Do NOT keep polling, do NOT print `RUNTIME_BLOCKED` (that token means *the code failed*, not *the tooling died*), and do NOT stop the iteration. You cannot exit play mode without the bridge — say so in the summary so the user restarts the Editor.
4. **In every skip case:** the diff is unverified at runtime, so copy the task's manual verify steps into the DONE summary **verbatim and unabridged**, and lead the summary with the skip reason.

> **Why the Editor may need a restart between tasks:** this project runs with Enter Play Mode Options enabled and *both* reloads disabled (`ProjectSettings/EditorSettings.asset` → `m_EnterPlayModeOptionsEnabled: 1`, `m_EnterPlayModeOptions: 3` = `DisableDomainReload | DisableSceneReload`). Statics are therefore **not** reset between play sessions. A long loop enters play mode once per M/L task in the *same* Editor session, so static state accumulates across tasks and a boot flow awaiting a one-shot static event can deadlock on the Nth entry while task 1 was fine. If runtime smoke stalls at boot for two tasks in a row, that is the signal — tell the user to restart the Editor; it is not the task's bug.

**On FAIL — auto-fix loop (max 2 rounds, same shape as STEP 6/7):** read the console evidence, exit play mode, fix the code, `git add -A`, re-run preflight if `.cs` changed, then re-run this gate from step 1. **"FAIL" means the console showed an exception/error from the diff — not a stall** (a stall routes to mid-gate stall recovery above). After Round 2 still failing → print the console evidence (error text + stack head) and output exactly:
`RUNTIME_BLOCKED — runtime smoke failed after 2 fix rounds. Manual intervention required. Run /run-backlog again after fixing, or run python3 .claude/scripts/backlog-ops.py demote <NNN> to abandon (returns the task to the head of TODO).`
DO NOT commit. Stop.

---

## STEP 8 — Mark DONE

Make **two** updates. Neither is part of the STEP 9 commit — the backlog lives in
`.git/` and is untrackable by construction, so the DONE summary exists only on this
machine. That makes STEP 10's report the user's ONLY delivery of the manual verify
steps; do not shorten it on the assumption they can read the file later.

1. **Run the deterministic transition** — ONE call moves in-progress → done AND removes the IN PROGRESS bullet (restoring `- (none)` when empty), then self-lints. It never adds a DONE bullet — `$BACKLOG_ROOT/done/` is the source of truth:

   ```bash
   python3 .claude/scripts/backlog-ops.py done <NNN>
   ```

   DO NOT hand-edit `BACKLOG.md` or move the task file yourself, and never `git mv` it — it is not tracked. The script does NOT write the completion summary — that is step 2 below.

2. **Edit the moved file** (`$BACKLOG_ROOT/done/<NNN-TIER-slug>.md`): replace the long task body with a short completion summary — this is content work, so YOU write it. Keep the heading `### [PRIORITY] Title`. Add:
   ```
   **Completed on:** YYYY-MM-DD (commit `<short-sha>` — fill after commit if needed)

   **Fix Summary:** 1–3 sentences summarizing what changed and why.

   **Quality gates:**
   - Compile check: <pass|skipped (worktree mode — no Editor, no .sln)|skipped (all 3 methods unavailable)>
   - Code review: <pass|warn|skipped (XS tier)> (rounds used: 1|2) [tool: codegraph|grep-fallback|n/a]
   - Performance review: <pass|warn|skipped (not perf-sensitive)|skipped (XS tier)> (rounds used if spawned) [tool: codegraph|grep-fallback|n/a]
   - Security review: <pass|warn|skipped — no sensitive files> (rounds used if spawned) [tool: codegraph|grep-fallback|n/a]
   - QA verify: <pass|warn|skipped (XS/S tier)> (rounds used: 1|2) [tool: codegraph|grep-fallback|n/a]
   - Runtime smoke: <pass|warn|skipped (XS/S tier)|skipped (worktree mode)|skipped (Unity MCP not connected / Editor not open)|skipped (no runtime surface)|skipped (Editor became unresponsive mid-gate)|skipped (game did not boot within budget)> (rounds used: 1|2) [screenshot: .claude/tmp/backlog/runtime-smoke-<NNN>.png|stall frame: .claude/tmp/backlog/stall-<NNN>.png|n/a]

   **Mode:** <current|worktree>

   **Manual verify steps (USER MUST RUN before merging $WORK_BRANCH → base branch):**
   <copy exact `manual_verify_steps` from qa-verifier output>
   ```
   You can keep the full original body below the summary if history is needed, but the summary is what future readers will scan.

---

## STEP 9 — Commit and push to the work branch

Run the index consistency lint one last time before committing:

```bash
python3 .claude/scripts/backlog-ops.py lint
```

If `ok = false` → the errors indicate a hand-edit or merge residue (dual-state file, orphan bullet, leaked markup). Fix them, re-run the lint, and only then commit.

Stage and commit the code changes. The backlog moves are NOT included — they happened
inside `.git/` and git cannot see them:
```bash
git add -A
git commit -m "<concise commit message max 50 chars>"
```

Format the commit message concisely according to the task. For example:
- `feat: ice boom cooldown ui` (for feature task)
- `fix: notification badge stale on logout` (for bug task)
- `refactor: extract skill csv parser` (for refactor task)

Push to the work branch:
```bash
if [ "$HAS_REMOTE" = "1" ]; then
  git push -u origin "$WORK_BRANCH"
else
  echo "no remote — push skipped, commit stays local"
fi
```

In **current mode** `$WORK_BRANCH` is the dev's own branch, so this pushes straight to
where they are working — that is intended, it is the branch they chose. In **worktree
mode** it is `agent/dev-<base>`, which the user merges themselves.

When `HAS_REMOTE=0` the push is the ONLY step that is dropped: the commit is already
made and the task is already DONE, so the loop continues to the next task normally.
Report it in STEP 10 rather than treating it as a failure — a project generated from
the base template runs its first several tasks before anyone creates a remote.

**DO NOT create a PR.** This is a house convention.

---

## STEP 10 — Report to user

Notify the user:
- Task completed (link to the file in `$BACKLOG_ROOT/done/` — an absolute path, since it is outside the worktree)
- Files changed
- Commit message used
- Branch + push status (`$WORK_BRANCH`) — `pushed to origin`, or `committed locally (no remote — push skipped)` when `HAS_REMOTE=0`
- Mode (`current` / `worktree`)
- **Pipeline summary**: every gate verdict (code / perf / security / QA / runtime smoke) + rounds used in auto-fix
- **MANUAL VERIFY REMINDER**: specific verification steps from the qa-verifier output, numbered clearly

Example report format:
```
[OK] Completed: <abs path>/.git/backlog/done/001-M-ice-boom-cooldown.md
Files: 3 changed (<featuresRoot>/.../SomeController.cs, ...)
Commit: feat: ice boom cooldown ui (a1b2c3d)
Branch: agent/dev-Dev1 (pushed to origin)   Mode: worktree

Pipeline:
  - Compile check: skipped (worktree mode — no Editor, no .sln)
  - Code review: pass (1 round)
  - Performance review: pass (1 round)
  - Security review: skipped (no sensitive files)
  - QA verify: pass (1 round)
  - Runtime smoke: skipped (worktree mode)

[WARN] MANUAL VERIFY REQUIRED before merging agent/dev-Dev1 -> Dev1:
  1. FIRST: merge the branch and run /compile-check — worktree mode never compiled this code
  2. Open Battle scene, cast IceBoom, confirm cooldown UI displays correctly
  3. Cast twice in rapid succession — the second must be blocked
  4. Regression: other skills (FireBall, ThunderStrike) cooldown UI still works
  5. Build Android APK to test on real device

After verification passes: `git checkout Dev1 && git merge agent/dev-Dev1`  (base branch = the loop-start branch; `git config --get "$(python3 .claude/scripts/project_profile.py gitConfigPrefix).agentBaseBranch"` mirrors it for reporting)
```

**Worktree mode — compile-check is step 1, always.** Nothing in the pipeline ever built
this code, so `/compile-check` MUST be the first numbered manual step, ahead of any
gameplay verification. In current mode drop that line (STEP 5b already compiled).

**Current mode** — the same report, with `Branch: <the dev's branch>   Mode: current`,
`Compile check: pass`, and the real runtime-smoke verdict.

---

## Notes for orchestrator

- **You are both orchestrator and implementer.** Subagents only review/audit/verify. You write the code, you fix bugs from findings, and you make commits.
- **Subagents are stateless across invocations.** Each spawn receives a fresh prompt with the diff and task spec. Do not assume they remember previous rounds.
- **Preflight is a deterministic guard, not a replacement for reviewers.** Only auto-fix findings with `confidence=definite`; findings with `confidence=contextual` must go into the reviewer/qa prompt.
- **Delta diff is only used for fix rounds.** The initial review and final QA/preflight must still have the full staged context to avoid missing side effects.
- **Spawn reviewers in parallel** when more than one of code-reviewer / performance-reviewer / security-auditor is needed — one tool-use block, multiple Agent calls. DO NOT run sequentially (waste of time).
- **Hard stop conditions** (never bypass — print the token EXACTLY as written when blocked; the loop runner watches for these strings):
  - Empty backlog — not a sentinel token: STEP 1's self-pause flow writes `PAUSED` to `$BACKLOG_ROOT/state` and the loop runner independently detects the empty index (it counts TODO/IN PROGRESS bullets itself).
  - `EDITOR_REQUIRED` — every remaining TODO task declares `**Requires:** unity-editor` and no Editor is live for this project after STEP 1b's retry-with-wait probe (a single unreachable/busy probe no longer triggers this — it retries ~20 s first); also writes `EDITOR_REQUIRED` to `$BACKLOG_ROOT/state`. The loop runner greps this token and stops. (`DEFERRED` is NOT a stop — it ends the iteration normally and the next run picks the next task.)
  - ~~`BASE_UNKNOWN`~~ — retired. Starting from an agent branch / detached `HEAD` is allowed; STEP 2a falls back to the recorded config then the repo default. Never emit this token.
  - `BASE_MERGE_CONFLICT` — merging the base branch into the work branch conflicts (STEP 2b, worktree mode only).
  - `NO_CHANGES` — implementer did not produce a diff.
  - `COMPILE_BLOCKED` — Unity compile errors remain after 2 fix rounds in STEP 5b. Impossible in worktree mode (the gate is skipped, never run).
  - `PREFLIGHT_BLOCKED` — deterministic definite critical findings remain after the preflight-fix limit.
  - `REVIEW_BLOCKED` after Round 2 in STEP 6.
  - `VERIFY_BLOCKED` after Round 2 in STEP 7.
  - `RUNTIME_BLOCKED` after Round 2 in STEP 7.5 — **only when the diff's code failed at runtime**. Unity tooling dying mid-gate (unanswered modal / unresponsive bridge / game never boots) is NOT this token: it degrades to `runtime-smoke: skipped (…)` and the task still reaches STEP 8.
- **No `--ship-anyway` mode.** If the user wants to force-ship a blocked task, they manually resolve the block and re-run the skill.
- **No PR creation.** The pipeline only pushes to the work branch; in worktree mode the user merges it manually after manual verification.
- **No deploy step.** Mobile game builds are done via Unity Editor, no CLI deploy exists.
- **No `npm run lint` equivalent.** Unity projects lack a CLI compilation check. Rely on the 3 quality gates + manual verification.
- **Verifier limitation:** qa-verifier is a static diff check. The runtime smoke gate (STEP 7.5) covers boot + console + spec recipes for M/L when the Editor is up — but it auto-skips when Unity MCP is absent and is a smoke test, not full QA. The manual verification steps in the task spec + DONE summary remain the final safety net — the user MUST still run them.
- **Worktree mode ships uncompiled code by design.** Both Unity gates are off, so the only checks left are the deterministic preflight and the LLM reviewers — none of which can tell whether the project builds. Never describe such a task as "verified"; say what ran and what did not, and lead the report with `/compile-check`.
- **Backlog writes never touch git.** `git add -A` in STEP 9 stages code only; the backlog is inside `.git/`. If you ever see a task file in `git status`, something re-created it in the worktree — do not commit it, re-run `backlog-ops.py lint`.
