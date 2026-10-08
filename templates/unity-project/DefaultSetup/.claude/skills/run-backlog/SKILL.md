---
name: run-backlog
description: Autonomous backlog agent for this Unity project — pick the first task in TODO, implement it, run quality gates (code-reviewer + performance-reviewer when perf-sensitive + security-auditor when sensitive, in parallel + qa-verifier) with auto-fix max 2 rounds per gate, mark it DONE, and commit + push to the work branch in the project's push-in-session style — only this task's files, `<prefix> Tag: <subject>` message (current mode: the branch already checked out; worktree mode: agent/dev-<base>). DO NOT create PRs. Under the loop runner a blocked/failed iteration is self-healed: the next iteration resumes the task in recovery mode (1f), and a task that still cannot finish is parked so the loop goes on.
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
[1]   PICK     → backlog-ops pick: resolve the task (ship-pending | in-progress → resume | todo | empty → pause | busy → stop, change nothing)
[2]   BRANCH   → worktree mode only: create/reuse the worktree branch + merge base in. current mode: SKIP
[3]   START    → backlog-ops start: todo → in-progress + BACKLOG.md bullet move
[4]   CONTEXT  → read CLAUDE.md + .claude/rules/* + task file + relevant code
[5]   IMPLEMENT→ write code, git add + 3-tier compile check (STEP 5b) (DO NOT commit yet)
[6]   REVIEW   → deterministic preflight, then spawn code-reviewer + (performance-reviewer IF perf-sensitive) + (security-auditor IF sensitive) in parallel; auto-fix max 2 rounds
[7]   VERIFY   → spawn qa-verifier (M/L); auto-fix max 2 rounds if failed; final preflight
[7.5] SMOKE    → runtime smoke gate (M/L, orchestrator-side, Unity MCP): play mode + console assert + screenshot; auto-skips if Editor absent
[8]   DONE     → backlog-ops done: in-progress → done + bullet removal (task is now ship-pending), write summary with all gate verdicts
[9]   SHIP     → backlog-ops lint, then push-in-session style: reset index → stage ONLY this task's files → `<prefix> Tag: <subject>` → record the staged tree (checkpoint --step commit) → commit + push to $WORK_BRANCH (DO NOT create a PR), then backlog-ops shipped
[10]  REPORT   → summarize for user, including manual verification steps
```

> **Self-heal (loop runner default).** A block token is no longer the end of the task. The
> controller answers any iteration that ends without finishing its task — a block token,
> `manual intervention required`, a crash, a watchdog kill, a silent end — with a
> **recovery iteration** on the same task: fresh context, a brief of what failed
> (`AGENT_RECOVERY_BRIEF`), the strongest model, and a fresh fix budget for every gate
> (section 1f). After `-MaxRecoveries` (default 3) it **parks** the task — partial work
> saved to `refs/backlog/parked/<NNN>` and taken out of the tree, task moved to the tail
> of TODO — and carries on with the next task. Usage/session limits are slept out until
> their reset. So: print the token honestly, leave the work resumable (staged + a block
> report in the task file), and let the controller decide. `-NoSelfHeal` restores
> stop-on-first-block. One block still stops the loop either way: a STEP 9e push failure
> (a diverged branch is a human's call — see 9e).

> **Deterministic bookkeeping:** every backlog state transition (pick / start / resume / checkpoint / done / shipped / demote / index edits) runs through `python3 .claude/scripts/backlog-ops.py` — NEVER hand-edit `BACKLOG.md`, `$BACKLOG_ROOT/runs/*.json`, or `git mv` a task file yourself for a transition. Hand-edited bookkeeping corrupts the index (leaked tool-call markup, dual-state task files, forbidden DONE bullets); the script self-lints after every mutation.

## Concurrency & resume contract — one consumer per clone, no blind resume

The queue is shared by every loop controller and every hand-run `/run-backlog` on this
clone, so `backlog-ops.py` fences it (all state under `$BACKLOG_ROOT/runs/`):

| Guard | What it stops | Held by |
|---|---|---|
| **Loop lease** (`runs/loop.json`) | a second loop controller; a hand-run `/run-backlog` while a loop runs | the loop controller process — its iterations carry `BACKLOG_LOOP_TOKEN` |
| **Task claim** (`runs/<NNN>.json`) | two sessions working one task (loop + hand-run, a killed controller's still-running window, two hand-runs) | the agent CLI process that ran `start` / `resume` (found by walking up the process tree) |
| **Ship fence** (claim `phase: done`) | a task marked DONE whose commit never happened being swept into the NEXT task's `git add -A` | same claim, closed by `shipped` |

A claim dies with the session that owns it — usage limit, crash, watchdog kill, closed
window, reboot — so a dead run is resumable at once, and a LIVE one can never be
resumed by someone else. Processes are identified by pid + start time (a recycled PID
never looks alive); liveness that cannot be read counts as live.

**Exit code 4 = busy** from `pick` / `start` / `resume` / `defer` / `checkpoint` / `done` /
`demote` / `shipped`: another session owns the queue or the task. **Change NOTHING** — no
edit, no transition, no demote, no `git` write — and end the iteration with exactly:
`<sentinel> — manual intervention required: <reason> — <hint>` (`sentinel` is `LOOP_BUSY`
or `TASK_BUSY` from the JSON; the `manual intervention required` phrase is what stops a loop
runner that predates these tokens). **Exit code 5 = `RESUME_CONFLICT`** (the partial work
lives in another checkout/branch): same shape, same stop. `start` / `resume` exiting 1 with
`cannot identify the agent session` means a claim could not be given an owner: print that
message (it ends in `manual intervention required`) and stop. The operator override is
`backlog-ops.py lock status` / `lock break --yes [--task NNN]`, never yours to run.

**Checkpoints** — journal each finished step so a resumed run knows how far the dead one got
(advisory: a resumed run re-runs every gate anyway). Call exactly these, in order, and
only for the task you own:

| When | Command |
|---|---|
| end of STEP 5 (implementation complete, before 5a) | `backlog-ops.py checkpoint <NNN> --step implemented` |
| end of STEP 5b | `… --step compile --result <pass\|skipped>` |
| STEP 6 passed (preflight + reviewers) | `… --step preflight --result pass` then `… --step review --result <pass\|warn>` |
| STEP 7 passed | `… --step qa --result <pass\|warn\|skipped>` |
| STEP 7.5 finished | `… --step smoke --result <pass\|warn\|skipped>` |
| STEP 9e, after the scoped stage and right before `git commit` (task already `done`) | `… --step commit --tree "$(git write-tree)"` — the ship fence's record of the commit (no trailer) |

---

## STEP 1 — Read index and pick task

Resolve the task deterministically (do NOT parse `BACKLOG.md` yourself):

```bash
python3 .claude/scripts/backlog-ops.py pick
# → JSON: {state, resume, nnn, tier, priority, title, path} — or {"state":"empty"} (exit code 2)
```

- **exit code 4** (`state: "busy"`) → another session owns the queue or this task. Follow the busy rule of the *Concurrency & resume contract*: change nothing, print `<sentinel> — <reason>: <hint>` (`LOOP_BUSY` / `TASK_BUSY`) and stop.
- `state: "ship-pending"` → a previous run marked task `nnn` DONE but died before its commit. Run **STEP 1c** first, then run `pick` again and continue with whatever it returns.
- `state: "in-progress"` (`resume: true`) → **resume** that task: run **STEP 1d** (it takes over the dead run's claim and tells you what it left). Read the file at `path`. (The todo→in-progress transition already happened in a previous run — skip STEP 3.)
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

Every `pick` result also carries `repo_state` — leftovers of a git command killed with the
previous run. Clear them before any git write: `index_lock` with `git_running: false` →
delete that file (with `git_running: true` another git command is live — wait, never
delete); `merge_in_progress` / `rebase_in_progress` / `cherry_pick_in_progress` → abort it
(`git merge --abort` / `git rebase --abort` / `git cherry-pick --abort`) only when it was left
by this pipeline (STEP 2b merge); otherwise stop with `manual intervention required`.

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

### 1c. Ship recovery (`state: "ship-pending"`)

The task is already in `$BACKLOG_ROOT/done/` with its summary, but `shipped` was never
recorded — the run died somewhere in STEP 8–9. Its code is either still uncommitted, or
committed but not pushed, or fully shipped with only the record missing.

1. `python3 .claude/scripts/backlog-ops.py resume <NNN>` — takes over the dead run's claim
   (exit 4/5 → busy rule).
2. Decide from `task_commits` — the commits since the task started whose tree matches the
   tree STEP 9e recorded right before its commit (`checkpoint --step commit --tree`; the
   message itself carries no trailer). Never from `partial_work`: after a commit it only
   holds dirt that arrived later (Unity auto-dirt, the dev's edits), and committing that
   would be wrong.
   - `task_commits` empty → the code was never committed: run **STEP 9** for this task now,
     exactly as written (9a reset → 9b list → 9c scoped stage → 9d message for THIS task,
     from its done-file title → 9e tree checkpoint + commit + push). This run has no
     transcript of the implementation, so 9b's list comes from `partial_work`, cross-checked
     against the done file's summary — leave out what is plainly auto-dirt and name it in
     STEP 10. Do not start any other task first — its `git add -A` would otherwise swallow
     this code.
   - `task_commits` non-empty → already committed. `unpushed_commits` > 0 (HAS_REMOTE=1) →
     `git push` only; otherwise nothing to do in git. **Do not commit again.**
3. `python3 .claude/scripts/backlog-ops.py shipped <NNN> --commit <sha from task_commits, or the one just made> --push <pushed|no-remote|failed> --note recovered`
4. **A push that fails here is the same stop as in 9e**: record `shipped … --push failed`
   (step 3), then output the 9e `manual intervention required — push of <WORK_BRANCH> to
   origin failed; …` line and stop — do NOT go on to `pick`, or the next task stacks one
   more unpushed commit onto the diverged branch.
5. Otherwise mention the recovery in STEP 10, then go back to STEP 1 and `pick` again.

### 1d. Resume protocol (`state: "in-progress"`)

Never resume blind — the previous attempt usually left real work behind.

1. `python3 .claude/scripts/backlog-ops.py resume <NNN>` — exit 4 (`TASK_BUSY`: its owner
   is alive) or exit 5 (`RESUME_CONFLICT`: it started in another checkout/branch) → print
   the token and stop without touching anything.
2. Read the JSON:
   - `partial_work` — files changed since the task started (staged, unstaged, untracked):
     this attempt's inheritance. `note: unverified (no baseline)` (`legacy: true`, the task
     predates claims) means pre-existing dirt cannot be told apart — judge each file by content.
   - `preexisting_dirty_unchanged` — dirt that was already there at `start` and is untouched:
     NOT this task's work. Never edit or revert it; name it in the DONE summary if STEP 9's
     `git add -A` will carry it.
   - `commits_since_start` — commits that landed after `start` (a STEP 2b merge, the dev's
     own commits). Read them before assuming the base is unchanged.
   - `checkpoints` / `last_checkpoint` + `guidance` — how far the dead attempt got.
3. Inspect the partial work BEFORE writing anything: `git diff -- <paths>`,
   `git diff --cached -- <paths>`, and read each untracked file. Then follow `guidance`:
   - nothing survived → a fresh start from STEP 4;
   - no `implemented` checkpoint → **continue** the implementation from the partial diff
     against the task's completion criteria — keep what is correct, finish what is missing;
     never revert it or re-implement from scratch;
   - `implemented` or later → the implementation was complete: re-check it against the
     completion criteria, then continue at STEP 5a.
4. **Every gate from STEP 5b on runs again** — a dead attempt's verdicts are not trusted
   (the diff may have changed after them). Checkpoints are re-recorded as you go.
5. The DONE summary records `**Attempts:** <attempt> (resumed from <last checkpoint or "start">)`.
6. A dead attempt may have left the Editor busy: before any Unity work, poll `unity_editor_state`
   and stop play mode if it is still playing (a run that died mid-smoke leaves it on).

### 1e. Multi-iteration tasks — checkpointed exit (`TASK_CHECKPOINTED`)

The loop controller kills an iteration at its hard cap (180 min). The iteration prompt carries
`AGENT_ITERATION_DEADLINE` (epoch seconds, ~20 min before that kill, leaving time to wrap up;
absent on a hand run = no deadline). Some tasks need more than one iteration **by design** —
a full validation runbook, a multi-round defect-fix loop, any spec that says "unfinished ⇒
`checkpoint` + resume next run". For those, ending with saved progress is the normal outcome,
**not** a block: print `TASK_CHECKPOINTED` and the loop starts the next iteration, which
resumes the task through 1d.

- **Watch the clock.** At every natural break (between phases, sessions, fix rounds) compare
  `date +%s` with `AGENT_ITERATION_DEADLINE`. Never start a step that cannot finish before it —
  wrap up instead. Overrunning means a watchdog kill mid-step: the work survives, but the next
  iteration has to recover it blind (1f) instead of resuming from your notes.
- **Checkpoint only when ALL hold:** (a) the task spec allows multi-run, or the remaining work
  plainly cannot fit in one iteration; (b) this iteration made real progress (a phase finished,
  defects fixed, sessions run); (c) nothing is left undecided — choices are made here under the
  *Autonomous decision policy* (Notes), never deferred to a human. A broken environment or no
  progress in this iteration → print the matching block token (or `manual intervention
  required`); the controller answers it with a recovery iteration (1f).
- **A job longer than one iteration is a checkpoint, not a question.** "This needs ~5 h of bot
  sessions — run it?" is never a reason to stop: start it, checkpoint at the deadline, and
  let the next iteration resume it.
- **Wrap-up before printing it:**
  1. Leave the Editor idle (not playing), the dev save restored, any test-only state reverted.
  2. Compile clean (STEP 5b procedure). A checkpoint never leaves the tree broken for the next attempt.
  3. Keep the work **uncommitted** and staged (`git add -A`). The next attempt reads it from
     `partial_work`. Never `done`, never commit, never `demote`.
  4. Append `## Resume notes — attempt <N> (<date>)` to the task file: what was done, what is
     still open (severity), the exact next-step order, and any environment traps you hit.
  5. Do **not** record `checkpoint --step implemented` unless the implementation is truly
     complete — resume would skip straight to the gates.
- **Final line of the report** (STEP 10 shape, minus commit/push):
  `TASK_CHECKPOINTED — <why one iteration is not enough> — next: <first resume step>`.
  The report must contain **no** block token and **not** the phrase `manual intervention required`,
  because the controller checks block tokens first and would stop.
- The controller caps consecutive checkpointed iterations of one task (`--max-checkpoints`,
  default 6). Past the cap it parks the task (`CHECKPOINT_LIMIT`) and moves on; with
  `-NoSelfHeal` it stops for a human look.

### 1f. Recovery iteration (prompt carries `AGENT_RECOVERY_BRIEF`)

The previous iteration on this task ended without finishing it, and the controller sent you
back in instead of stopping. The prompt names the brief (`AGENT_RECOVERY_BRIEF=<path>`) and
the attempt (`AGENT_RECOVERY_ATTEMPT=<n>/<max>`).

1. **Read the brief first.** It names what happened (`<TOKEN>` / `WATCHDOG_KILL` /
   `ITERATION_FAILED` / `SILENT_END` / `MANUAL_INTERVENTION`) and carries the failed
   iteration's final report (or its last messages when it died). Then `pick` as usual — it
   returns the same task — and take it over through **1d** (`resume`), or STEP 3 if it never
   started. Read the task file's `## Block report` / `## Resume notes` sections too.
2. **Diagnose before you edit.** Name the root cause in one sentence, from evidence, before
   touching code. Repeating the previous attempt's fix is the one thing guaranteed to fail.

   | What happened | Do |
   |---|---|
   | `COMPILE_BLOCKED` | Re-run 5b and read every error in full. Errors outside the task's files (another session mid-edit, pre-existing breakage): wait ~2 min in the foreground and re-check once; still broken → make the minimal fix that restores a green build and name it in the DONE summary. |
   | `PREFLIGHT_BLOCKED` | Each `definite` finding is a hard rule — restructure the code so the rule holds (a coroutine becomes UniTask, `DateTime.Now` becomes `TimeManager`, a secret moves out of source…). Never edit the preflight rules. |
   | `REVIEW_BLOCKED` / `VERIFY_BLOCKED` | Take the remaining findings from the brief and fix their cause, not the symptom. A finding you believe is wrong (contradicts the spec, targets code outside the diff, already fixed) is settled only by re-spawning that reviewer with your counter-evidence and getting its verdict — never by your own say-so. A criterion that truly cannot be met in code (needs an asset the task does not own, a decision) goes through the decision policy below. |
   | `RUNTIME_BLOCKED` | Reproduce in play mode, read the stack head, fix, re-run 7.5. |
   | `NO_CHANGES` | Decide which case it is (STEP 6a, exit 2): already satisfied → close it that way; skipped/lost implementation → implement it now. |
   | `MANUAL_INTERVENTION` (a question or a decision) | Apply the *Autonomous decision policy* (Notes) and continue. |
   | `WATCHDOG_KILL` / `SILENT_END` / `ITERATION_FAILED` | The run hung or ended its turn early. Same task, smaller steps: no background waits, foreground polls only, checkpoint (1e) before `AGENT_ITERATION_DEADLINE`. If a single step (a bake, a bot session, a bundle build) cannot fit, run it in slices across checkpoints. |
   | `BASE_MERGE_CONFLICT` (worktree) | Resolve the conflicts on the agent branch (keep both sides' intent; the base wins on files the task does not touch), commit the merge, continue. |
3. **Every gate gets a fresh budget** in this iteration (2 fix rounds each) and runs again in
   full — the dead attempt's verdicts are not trusted.
4. **Never weaken a gate to get past it.** No deleting or loosening completion criteria,
   tests, preflight rules or reviewer definitions; no skipping a mandatory reviewer; no
   `--no-verify`; no `#pragma warning disable` to silence a finding. A recovery that
   "passes" this way is worse than a parked task.
5. **Still blocked after your fix rounds?** Write the block report (Notes), then print the
   block token exactly as STEP 5b–7.5 define. The controller either sends a further recovery
   or parks the task; you never `demote` or park it yourself.
6. DONE summary: add `**Self-heal:** recovered on attempt <n> after <what happened> — root
   cause: <one line>`.

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
- `start` also writes the task claim (owner = this session) and a baseline of the files that were already dirty, which is what lets a later `resume` tell your work from pre-existing dirt. Exit 4 (another session started it first, or a loop owns the queue) → busy rule: change nothing, print the token, stop.
- DO NOT hand-edit `BACKLOG.md` or `git mv` the task file yourself for this transition.
- **`parked_work` in the result** → an earlier loop run gave up on this task and saved its partial work at `refs/backlog/parked/<NNN>` (the task file ends with `## Parked` notes saying why). Read its `hint`: inspect the diff, restore only what still fits the current code (`git checkout refs/backlog/parked/<NNN> -- <path>`), and avoid the approach the park reason says failed. `shipped` deletes the ref.
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

**`/new-ui` (and the `/new-package` / `/new-feature` UI step) does not end at Phase C.** Root screens get the **designer pass — Phase D** ([`.claude/docs/ui-designer-pass.md`](../../docs/ui-designer-pass.md)): art, FX and motion laid over the frozen layout, `ui-layout-lock.py diff` + `ui-visual-reviewer` phase D. Its skips (` | polish=off` in `$WF_ARGS`, dev-only screens) and its revert-to-Phase-C fallback are defined there — Phase D never ends the run with a block. That doc or `.claude/scripts/ui-layout-lock.py` missing, or `ui-layout-lock.py begin` answering `skip` → skip it as `designer-pass: skipped (not installed: <file>)` and list the missing item in STEP 10, like a missing commit-style dependency. Its session (backups, lock snapshots) lives in `.claude/tmp/ui-designer/<screen>-<hash>/`; call `ui-layout-lock.py begin --prefab <prefab> --task <NNN>` with this task's number, so a resumed/recovery iteration picks the same session up again (`begin` answers `resume` / `already-done` / `already-reverted` — never retake the lock-before snapshot, never run Phase D twice) while a later task on the same screen starts fresh. A STEP 5b / preflight / 6 / 7 / 7.5 block caused only by Phase D output that the gate's fix rounds cannot clear → revert Phase D and re-run that gate instead of emitting the block token (the **Phase D exception** line at each gate). Record its outcome line (`designer-pass: done|skipped|reverted …`) in the DONE summary, and its outputs (art + `.meta`, motion code, `ArtStyle.md`, the pending screen PNG, `<S>.ui-layout-lock.json`) in the STEP 9b file list.

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
- **Cheat affordance** — when the task carries `[CHEAT]` on its `**Guardrails:**` line, implement it as specified: `public Cheat_*` methods in a `#region Cheats` on the Controller (`[TabGroup("Cheats")] [Button]`, ending in a UI refresh) + `ButtonNormal` instances under the prefab's **inherited** `CheatMenu/Menu`, wired to those methods. Read [.claude/skills/feature-cheat/SKILL.md](../feature-cheat/SKILL.md) first — it has the exact sizes/labels/guids and the `unity_execute_code` recipe for persistent `onClick` wiring; the design must mirror `Features/System/GameCheat`. Never re-instantiate `CheatMenu` and never localize cheat labels. **If the task has no `[CHEAT]` tag, do NOT add cheats on your own** — that is a planning decision, not an implementer one. **Editor absent:** write the `Cheat_*` code, leave the prefab buttons undone, and say so explicitly in the DONE summary + as a manual verify step (do not silently drop the criterion).
- No new abstractions, no extra features beyond the task spec.
- No comments unless the WHY is non-obvious.
- Do not hardcode API keys, secrets, or tokens.
- Backend writes must go through Cloudflare Workers; reads can directly access Supabase with the anon key.

**There is no `npm run lint` in a Unity project.** Compilation is only checked when the user opens the Editor. Rely on the quality gates below to catch errors.

### 5a — Stage changes

When implementation is done, record it — `python3 .claude/scripts/backlog-ops.py checkpoint <NNN> --step implemented` — then **stage** all changes:
```bash
git add -A
```

**Do not commit yet.** Quality gates run on the staged diff. Commit only after all gates pass.
This whole-tree staging exists for the gates only — STEP 9 resets the index and
re-stages just this task's files (push-in-session scoped stage) before committing.

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
   **Phase D exception:** when everything still failing comes only from the designer pass's own output (its motion / idle-FX code, overlay nodes, art — not Phase C or the task's logic), do not emit the token yet: `python3 .claude/scripts/ui-layout-lock.py revert --prefab <screen prefab>`, refresh Unity, regenerate the §5 evidence on the Phase C screen, re-run this gate, and record `designer-pass: reverted (<gate>: <reason>)` ([ui-designer-pass.md](../../docs/ui-designer-pass.md) D6–D7). The revert refuses when Phase D's files were edited after it finished (`--discard-later-edits` only if every such edit was itself a Phase D fix attempt); refused (or the revert exits 2: stale / no session) → the token stands.

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

> Checkpoint: `backlog-ops.py checkpoint <NNN> --step compile --result <pass|skipped>` once 5b has an outcome.

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

On exit `2` (no staged diff) — find out which case it is before giving up:
1. **Already satisfied?** The work may already be in `HEAD` (an earlier task or commit did it).
   Spawn `qa-verifier` (any tier) with the task spec and the note "NO DIFF — verify every
   completion criterion against the CURRENT code at HEAD". `pass`/`warn` → close it as
   already satisfied: STEP 8 (`done`, summary `**Fix Summary:** Already satisfied at
   <HEAD short sha> — no change needed` + the qa verdict and criteria evidence), skip the
   STEP 9 commit, then `python3 .claude/scripts/backlog-ops.py shipped <NNN> --commit
   "$(git rev-parse --short HEAD)" --push skipped --note already-satisfied`, and report
   normally in STEP 10.
2. **Not satisfied** (`fail`) → the implementation was skipped or lost: go back to STEP 5 and
   implement it now (once).
3. Still no diff after that → write the block report, then output `NO_CHANGES — implementation
   produced no diff. Task may already be complete or implementation skipped.` DO NOT commit.

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
  - **Phase D exception:** when everything still failing comes only from the designer pass's own output (its motion / idle-FX code, overlay nodes, art — not Phase C or the task's logic), do not emit the token yet: `python3 .claude/scripts/ui-layout-lock.py revert --prefab <screen prefab>`, refresh Unity, regenerate the §5 evidence on the Phase C screen, re-run this gate, and record `designer-pass: reverted (<gate>: <reason>)` ([ui-designer-pass.md](../../docs/ui-designer-pass.md) D6–D7). The revert refuses when Phase D's files were edited after it finished (`--discard-later-edits` only if every such edit was itself a Phase D fix attempt); refused (or the revert exits 2: stale / no session) → the token stands.
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
- List / scroll / UI binding or layout: recycling-scroller binding, `LayoutRebuilder`, per-frame `Canvas`/`SetActive` churn.
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

Spawn **`code-reviewer`** with `model: "opus"`. In the **same message** (parallel), also spawn **`performance-reviewer`** with `model: "opus"` if `$PERF_SENSITIVE = true`, and **`security-auditor`** (default model, `opus` — do not downgrade it) if `$SENSITIVE = true`. S tier no longer downgrades reviewers below opus; the `model:` override is explicit only to pin the floor if an agent default ever changes. Do NOT spawn qa-verifier. An S task touching `Purchase*`/`Auth*`/value-bearing writes gets the same security audit as M/L.

```
Agent({
  description: "Code review backlog task (S tier)",
  subagent_type: "code-reviewer",
  model: "opus",
  prompt: <<see prompt body below>>
})
```

**Performance Reviewer** (only when `$PERF_SENSITIVE = true`, same message as code-reviewer):
```
Agent({
  description: "Performance review backlog task (S tier)",
  subagent_type: "performance-reviewer",
  model: "opus",
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
→ any `block` → auto-fix loop (max 2 rounds, re-spawn the blocking reviewer(s) — code/perf with `model: "opus"`, security with its default model), then STEP 8.

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
  - **Phase D exception:** when everything still failing comes only from the designer pass's own output (its motion / idle-FX code, overlay nodes, art — not Phase C or the task's logic), do not emit the token yet: `python3 .claude/scripts/ui-layout-lock.py revert --prefab <screen prefab>`, refresh Unity, regenerate the §5 evidence on the Phase C screen, re-run this gate, and record `designer-pass: reverted (<gate>: <reason>)` ([ui-designer-pass.md](../../docs/ui-designer-pass.md) D6–D7). The revert refuses when Phase D's files were edited after it finished (`--discard-later-edits` only if every such edit was itself a Phase D fix attempt); refused (or the revert exits 2: stale / no session) → the token stands.

---

> Checkpoint once STEP 6 passes: `… --step preflight --result pass` then `… --step review --result <pass|warn>`.

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
  - **Phase D exception:** when everything still failing comes only from the designer pass's own output (its motion / idle-FX code, overlay nodes, art — not Phase C or the task's logic), do not emit the token yet: `python3 .claude/scripts/ui-layout-lock.py revert --prefab <screen prefab>`, refresh Unity, regenerate the §5 evidence on the Phase C screen, re-run this gate, and record `designer-pass: reverted (<gate>: <reason>)` ([ui-designer-pass.md](../../docs/ui-designer-pass.md) D6–D7). The revert refuses when Phase D's files were edited after it finished (`--discard-later-edits` only if every such edit was itself a Phase D fix attempt); refused (or the revert exits 2: stale / no session) → the token stands.

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

**Phase D exception:** when everything still failing comes only from the designer pass's own output (its motion / idle-FX code, overlay nodes, art — not Phase C or the task's logic), do not emit the token yet: `python3 .claude/scripts/ui-layout-lock.py revert --prefab <screen prefab>`, refresh Unity, regenerate the §5 evidence on the Phase C screen, re-run this gate, and record `designer-pass: reverted (<gate>: <reason>)` ([ui-designer-pass.md](../../docs/ui-designer-pass.md) D6–D7). The revert refuses when Phase D's files were edited after it finished (`--discard-later-edits` only if every such edit was itself a Phase D fix attempt); refused (or the revert exits 2: stale / no session) → the token stands.

---

> Checkpoint once STEP 7 passes (or is skipped for the tier): `… --step qa --result <pass|warn|skipped>`.

## STEP 7.5 — Quality Gate: Runtime smoke (M / L only, orchestrator-side)

**Purpose:** every gate so far only READS the diff — none observes the game running. This gate boots the game in the Editor and fails on runtime errors (NRE storms, exceptions, broken economy/save flows that no diff reader can catch). It automates the first slice of what "Required verification steps" otherwise defers entirely to the user.

Run it for **M / L** after qa-verifier passes (STEP 7) and the final preflight (7d). **XS / S skip it** (they already routed straight to STEP 8 in STEP 6d). **You run it yourself** — the `mcp__unity__*` tools are available to the orchestrator only; do NOT spawn a subagent for this gate.

**Skip conditions (graceful — NEVER fail the task on these; record the reason in the DONE summary as `runtime-smoke: skipped (<reason>)`):**
- **`$AGENT_MODE = worktree` → skip WITHOUT probing.** Record `runtime-smoke: skipped (worktree mode)`. The only live Editor belongs to the dev's checkout; probing would find it and play-test the **wrong project** while reporting a pass for yours. Do not open Unity on the worktree either — the machine may not have the RAM for a second Editor, which is why worktree mode exists.
- Unity MCP not connected / no live Editor open for this project → probe `mcp__unity__unity_list_instances`; on fail/timeout record `runtime-smoke: skipped (Unity MCP not connected / Editor not open)`. **The headless loop must NEVER be hard-blocked by the absence of Unity** — absence is a skip, not a block.
- In current mode with several Editors listed, select by **project path**, never by project name — a worktree from an earlier run may still be open under the same name.
- The staged diff has no runtime surface (docs/`.md`, CSV comments only, editor-only `#if UNITY_EDITOR` code) → record `runtime-smoke: skipped (no runtime surface)`.
- **The Editor was live at gate entry but stops responding, or the game never boots, part-way through** → run the *mid-gate stall recovery* below. This is a **skip, not a block** — see that section for the exact reason strings.

> **The gate never strands the task.** Every exit from STEP 7.5 is exactly one of: `pass` · `warn` · a `skipped (…)` reason · `RUNTIME_BLOCKED` (code failed 2 fix rounds). "The Editor went quiet / the game never booted" is infrastructure, NOT a code failure — it can never end the iteration without one of those outcomes. Ending the turn describing a stall in prose, without reaching STEP 8, is a **silent failure**: the task stays in `backlog/in-progress/`, and the loop runner has to spend a recovery iteration (`SILENT_END`, 1f) re-entering a task you could have finished. If you are ever unsure which outcome applies, choose a `skipped (…)` reason and continue to STEP 8.

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
**Phase D exception:** when everything still failing comes only from the designer pass's own output (its motion / idle-FX code, overlay nodes, art — not Phase C or the task's logic), do not emit the token yet: `python3 .claude/scripts/ui-layout-lock.py revert --prefab <screen prefab>`, refresh Unity, regenerate the §5 evidence on the Phase C screen, re-run this gate, and record `designer-pass: reverted (<gate>: <reason>)` ([ui-designer-pass.md](../../docs/ui-designer-pass.md) D6–D7). The revert refuses when Phase D's files were edited after it finished (`--discard-later-edits` only if every such edit was itself a Phase D fix attempt); refused (or the revert exits 2: stale / no session) → the token stands.

---

> Checkpoint once STEP 7.5 has an outcome (XS/S included: `--result skipped`): `… --step smoke --result <pass|warn|skipped>`.

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

   From here until STEP 9 records `shipped`, the task is **ship-pending**: if this run dies
   now, the next `pick` returns it first (STEP 1c) so its code can never be swept into the
   next task's commit.

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

   **Attempts:** <N> (resumed from <last checkpoint | start>) — omit when N = 1

   **Manual verify steps (USER MUST RUN before merging $WORK_BRANCH → base branch):**
   <copy exact `manual_verify_steps` from qa-verifier output>
   ```
   You can keep the full original body below the summary if history is needed, but the summary is what future readers will scan.

---

## STEP 9 — Commit and push to the work branch (push-in-session style)

Run the index consistency lint one last time before committing:

```bash
python3 .claude/scripts/backlog-ops.py lint
```

If `ok = false` → the errors indicate a hand-edit or merge residue (dual-state file, orphan bullet, leaked markup). Fix them, re-run the lint, and only then commit.

**The commit follows the project's push style: [`.claude/skills/push-in-session/SKILL.md`](../push-in-session/SKILL.md).**
Read that file now and apply its §1 (file list) → §2 (scoped stage) → §3 (message) →
§4 (commit + push), with the loop adaptations 9a–9e below. Read the file directly
rather than invoking `/push-in-session` — the non-Claude loop adapters run this skill
too and have no Skill tool. **Never fall back to `git add -A` + a free-form message**:
that is exactly the behavior this step replaced — not even when a dependency is missing
(9.0 covers that). The backlog moves are NOT part of the commit either way — they
happened inside `.git/` and git cannot see them.

**Ship fence without a trailer.** Between `done` (STEP 8) and `shipped` (end of 9e) the task
is ship-pending. 9e records the staged tree right before `git commit`, so a run that dies
after the commit but before `shipped` is recognised by STEP 1c as already committed — by
content, never by a message trailer — instead of being committed a second time.

### 9.0 — Probe the dependencies (one call)

push-in-session and its two scripts are separate Feature Hub items, so a project that
installed `run-backlog` on its own may lack any of them. Check once:

```bash
for f in .claude/skills/push-in-session/SKILL.md .claude/scripts/git_prepare_scoped.sh .claude/scripts/git_push.sh; do
  [ -f "$f" ] && echo "OK $f" || echo "MISSING $f"
done
# Windows: probe the git_prepare_scoped.ps1 / git_push.ps1 twins instead of the .sh ones
```

| MISSING | Fallback — the commit style does not change |
|---|---|
| `push-in-session/SKILL.md` | 9b's list rules + the built-in copy of the message rules in 9d |
| `git_prepare_scoped` | the inline stage loop in 9c |
| `git_push` | the inline commit + push in 9e |

A missing dependency is never a stop condition. Record
`commit-style fallback: <missing file(s)>` for STEP 10 so the dev can install the item
from Feature Hub (AI Feature tab).

### 9a — Reset the index (loop-only, mandatory)

STEP 5a / 6 staged the whole tree with `git add -A` so the gates could review it.
`git_prepare_scoped` only ADDS paths — it never unstages — so skipping this reset lets
every staged file (Unity auto-dirt, the dev's parallel edits) ride into the commit and
defeats the scoped stage entirely:

```bash
git reset -q
```

The working tree is untouched; only the index returns to `HEAD`. This is the one git
command on top of push-in-session's 2-command budget, and it is required here.

### 9b — Build the file list (push-in-session §1)

The list = what THIS iteration changed, STEP 5 through the last fix round of any gate,
taken from your own transcript — not from `git status` / `git diff`. In the loop that is:

- files you wrote with Write / Edit / Bash (implementation + every review/QA/runtime fix);
- files produced by a command you ran on purpose: CSV importer output
  (`DataManager.Generated.cs`, `CsvAssetDir.cs`, `Resources/*.asset`), prefabs / scenes /
  SOs you created or saved through Unity MCP (STEP 5.0's `/new-ui` / `/new-feature`
  workflows write most of their output this way), `ui-kit-sync.py`, `/add-localize` output;
- **the `.meta` of every file AND folder you created.** Unity generates them during the
  STEP 5b refresh; a commit without them hands every other dev a fresh random GUID.
  Pass `<path>.meta` for each new file and each new folder even when unsure it exists —
  a missing one only lands in `--- SKIPPED (not found) ---` (normal in worktree mode,
  where no Editor ever ran);
- **deleted / renamed paths** — pass the old path too, so the deletion is staged.

Leave out, per push-in-session §1: files you only read; Unity auto-dirt after
`Assets/Refresh` that the task did not target (typically scenes / ScriptableObjects an
editor tool re-syncs on refresh — CLAUDE.md lists the project's known offenders); anything
under `Library/`, `Temp/`, `.claude/tmp/`, the scratchpad. Unsure → leave it out and
list it in STEP 10.

Cross-check with the last snapshot's `files[]` (STEP 6/7 — already in context, no new
command): a reviewed file that is not in your list is either auto-dirt (correct to drop)
or something you forgot. Decide each one explicitly; never drop a file silently.

### 9c — Stage (push-in-session §2)

```bash
bash .claude/scripts/git_prepare_scoped.sh "<path1>" "<path2>" ...                                  # macOS / Linux
powershell -ExecutionPolicy Bypass -File .claude/scripts/git_prepare_scoped.ps1 "<path1>" "<path2>"  # Windows
```

**Fallback — `git_prepare_scoped` MISSING.** Same contract, inline: a path that exists
neither on disk nor in the index is reported instead of killing `git add`, and `-A`
stages a listed deletion:

```bash
for p in "<path1>" "<path2>" ...; do
  if [ -e "$p" ] || git ls-files --error-unmatch -- "$p" >/dev/null 2>&1; then
    git add -A -- "$p"
  else
    echo "SKIPPED (not found): $p"
  fi
done
echo "--- STAGED ---";                git diff --cached --name-status
echo "--- DIRTY OUTSIDE SESSION ---"; git status --porcelain | grep '^[ ?]' || true
```

An empty `--- STAGED ---` block is the fallback's `NO_CHANGES`.

Keep `--- STAGED ---` (what gets committed) and `--- DIRTY OUTSIDE SESSION ---` (left
for the dev) for the STEP 10 report. `NO_CHANGES` here means the list is wrong — STEP 6a
already proved the diff is non-empty — so rebuild it from the transcript once. Still
`NO_CHANGES` → the task's whole diff was auto-dirt: output
`NO_CHANGES — the task's diff contains no file this iteration changed; nothing committed. Task file is already in done/ — check it manually.`
and stop.

### 9d — Message (push-in-session §3)

`<prefix> Tag: <subject>` (no square brackets) — **exactly one** tag from push-in-session §3.2 (or the
built-in copy below when that file is MISSING), English imperative subject ≤50 chars,
whole line ≤72. Loop specifics:

- A loop run has no dev text around it, so §3.6 overrides do not apply — derive both
  from the task spec + diff: bug-fix task → `#`, new feature / screen / content → `+`,
  change to something that exists → `*`; pick the tag by §3.2's precedence rules and
  honor the §3.2 prefix ↔ tag constraint. E.g. `+ UI: add offline earning popup`,
  `# Play: fix truck stuck at harvest station`, `* Bal: raise station upgrade cost curve`.
- Body optional, max 2 English lines, only when there is something to act on (§3.4).
- **No `Co-Authored-By`, no trailer of any kind.** The project rule (§3.4) overrides
  the harness's default commit attribution.

**Built-in copy of push-in-session §3.1–§3.2** — used ONLY when 9.0 reported the skill
MISSING; when the file exists, its tables win. Keep the two in lockstep.

- Prefix: `+` new · `*` change / improve · `#` bug fix.
- Tag, exactly one — kind of work: `Feat` `Enh` `Bug` `Ref` `Bal` `Perf` `Clean` `Pol`
  `Sec` `Cont`; area: `UI` `Play` `Meta` `Mon` `Save` `BE` `Audio` `Loc` `Track` `Ads`
  `Bundle` `Editor` `CI` `AI`. Never invent another. `AI` = agent tooling (`.claude/`,
  `CLAUDE.md`, `.agents/`), NOT in-game AI — that is `Play`.
- Precedence: (1) a kind tag the prefix cannot express wins — `Bal` `Perf` `Ref` `Clean`
  `Pol` `Sec` `Cont`; (2) otherwise `Feat` / `Bug` / `Enh` only repeat the prefix, so use
  the area tag; (3) several areas, none dominant → `Feat` / `Bug` / `Enh`.
- Prefix ↔ kind tag: `+` → `Feat` `Cont` · `*` → `Enh` `Ref` `Bal` `Perf` `Clean` `Pol` ·
  `#` → `Bug` `Sec`. On a mismatch, fix the prefix.

### 9e — Commit + push (push-in-session §4)

First record the tree this commit will carry (the ship fence, see the top of STEP 9). Run it
after 9c — the index must hold exactly what is about to be committed:

```bash
python3 .claude/scripts/backlog-ops.py checkpoint <NNN> --step commit --tree "$(git write-tree)"
```

Then commit + push:

```bash
if [ "$HAS_REMOTE" = "1" ]; then
  bash .claude/scripts/git_push.sh "<final message>"   # Windows: powershell -ExecutionPolicy Bypass -File .claude/scripts/git_push.ps1 "<final message>"
else
  git commit -m "<final message>"                       # no remote — push skipped, commit stays local
fi
```

`git_push` commits, then pushes the checked-out branch, which is `$WORK_BRANCH` in both
modes. When that branch has no upstream yet (`agent/dev-<base>` on its first worktree
run) it runs `git push -u origin HEAD` by itself — no separate `-u` step.

**Fallback — `git_push` MISSING** (`HAS_REMOTE=1`), same behavior inline:

```bash
git commit -m "<final message>" && {
  if git rev-parse --abbrev-ref --symbolic-full-name '@{u}' >/dev/null 2>&1; then
    git push
  else
    git push -u origin HEAD
  fi
}
```

In **current mode** `$WORK_BRANCH` is the dev's own branch, so this pushes straight to
where they are working — that is intended, it is the branch they chose. In **worktree
mode** it is `agent/dev-<base>`, which the user merges themselves.

**Push rejected / failed** → per push-in-session §4: no `--force`, no rebase, no pull.
The commit stays local and the task is already DONE. First close the run with the sha
you just made — the dev will pull/rebase by hand, which rewrites that commit's tree, so
the ship fence must not be left to find it later:

```bash
python3 .claude/scripts/backlog-ops.py shipped <NNN> --commit "$(git rev-parse --short HEAD)" --push failed --note push-rejected
```

Then print git's error verbatim and output exactly
`manual intervention required — push of <WORK_BRANCH> to origin failed; commit <short-sha> is local only. Pull/rebase by hand, push, then re-run.`
and stop — the loop runner greps that phrase, so later tasks do not pile more unpushed
commits onto a diverged branch.

When `HAS_REMOTE=0` the push is the ONLY step that is dropped: the commit is already
made and the task is already DONE, so the loop continues to the next task normally.
Report it in STEP 10 rather than treating it as a failure — a project generated from
the base template runs its first several tasks before anyone creates a remote.

Then close the task's run — this lifts the ship fence (STEP 1c) and archives the claim:

```bash
python3 .claude/scripts/backlog-ops.py shipped <NNN> --commit "$(git rev-parse --short HEAD)" --push <pushed|no-remote|failed>
```

**DO NOT create a PR.** This is a house convention.

---

## STEP 10 — Report to user

Notify the user:
- Task completed (link to the file in `$BACKLOG_ROOT/done/` — an absolute path, since it is outside the worktree)
- Files committed (`--- STAGED ---` of STEP 9c) and files **left uncommitted** (`--- DIRTY OUTSIDE SESSION ---`, plus any path you chose to leave out) — the dev decides what to do with the latter
- Commit message used (push-in-session format), plus `commit-style fallback: …` when STEP 9.0 found a dependency missing
- Branch + push status (`$WORK_BRANCH`) — `pushed to origin`, or `committed locally (no remote — push skipped)` when `HAS_REMOTE=0`
- Mode (`current` / `worktree`)
- **Pipeline summary**: every gate verdict (code / perf / security / QA / runtime smoke) + rounds used in auto-fix
- **MANUAL VERIFY REMINDER**: specific verification steps from the qa-verifier output, numbered clearly

Example report format:
```
[OK] Completed: <abs path>/.git/backlog/done/001-M-ice-boom-cooldown.md
Files: 3 committed (<featuresRoot>/.../SomeController.cs, ...)
Left uncommitted: 1 (M <sourceRoot>/Scenes/<Scene>.unity — editor auto-sync, not part of this task)
Commit: + UI: add ice boom cooldown ui (a1b2c3d)
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
- **Never end the turn to wait.** The loop runs `claude -p`: the end of your turn is the end of the iteration — a background job's completion (Bash `run_in_background`, `Monitor`, a background Agent) cannot reliably wake it again, and the task is stranded in `in-progress/` (seen in practice: an iteration that ended with "Waiting for the Editor recompile to finish." → silent end). Every wait — recompile, asset import, bundle build, play-mode boot, reviewer — happens in the foreground: Bash `sleep ≤10` between `unity_editor_state` / file polls (chain several calls for long waits), Agent calls with `run_in_background: false`. The turn ends only with the STEP 10 report or a stop token. The controller answers a silent end with a recovery iteration (`SILENT_END`, 1f); with `-NoSelfHeal` it resumes once (`SILENT_RETRY`) and then stops (`SILENT_FAIL`).
- **Autonomous decision policy.** Inside a loop iteration nobody is there to answer, so a decision is never a reason to stop or to end with a question:
  1. Take the option the task spec / `**Context docs:**` / TechSpec recommends; else the one most consistent with the existing code and data; else the most conservative, reversible one.
  2. Forbidden-to-invent groups (economy/reward numbers, save migration, backend/IAP/security, core UX flow): invent nothing new — reuse the value the spec/TechSpec gives, or keep the currently shipped value / the closest existing analogue; keep save changes additive with a `SetupDefaultData()` fallback; never weaken a security check.
  3. Record every such choice under `## Autonomous decisions` in the task file, in the DONE summary, and in STEP 10, each with a `[DECISION-REVIEW]` manual verify step so the dev can overrule it afterwards.
  4. Work that is too long for one iteration is a checkpoint (1e), not a question.
  Only a genuinely external blocker (missing credentials, a service that is down, hardware) ends the iteration with a block — and even then the controller retries or parks; it does not wait for a human.
- **Block report — before printing ANY block token** (`*_BLOCKED`, `NO_CHANGES`, `manual intervention required`): append `## Block report — <TOKEN> — <date>` to the task file with the remaining findings (file:line + issue), what each fix round tried, and your best root-cause guess. Leave the work staged; never `demote`. The next (recovery) iteration starts from that report.
- **Stop tokens** (never bypass — print the token EXACTLY as written when blocked; the loop runner watches for these strings). They end the *iteration*, not the loop: under self-heal the controller follows each with a recovery iteration or a park (busy tokens wait, `EDITOR_REQUIRED` starts the Editor); only `-NoSelfHeal` turns them into loop stops:
  - Empty backlog — not a sentinel token: STEP 1's self-pause flow writes `PAUSED` to `$BACKLOG_ROOT/state` and the loop runner independently detects the empty index (it counts TODO/IN PROGRESS bullets itself).
  - `TASK_CHECKPOINTED` is **not** a stop. It is the continuation token of a multi-iteration task (1e): the task stays in progress and the loop resumes it next iteration. Never print it with a block token, and never use it to dodge one.
  - `EDITOR_REQUIRED` — every remaining TODO task declares `**Requires:** unity-editor` and no Editor is live for this project after STEP 1b's retry-with-wait probe (a single unreachable/busy probe no longer triggers this — it retries ~20 s first); also writes `EDITOR_REQUIRED` to `$BACKLOG_ROOT/state`. The loop runner greps this token: under self-heal it (re)starts this project's Editor (current mode, at most twice per run) and resumes; in worktree mode, or once those restarts fail, it stops. (`DEFERRED` is NOT a stop — it ends the iteration normally and the next run picks the next task.)
  - ~~`BASE_UNKNOWN`~~ — retired. Starting from an agent branch / detached `HEAD` is allowed; STEP 2a falls back to the recorded config then the repo default. Never emit this token.
  - `BASE_MERGE_CONFLICT` — merging the base branch into the work branch conflicts (STEP 2b, worktree mode only).
  - `LOOP_BUSY` — `backlog-ops.py` exit 4 with that sentinel: another loop holds this clone's lease (or a pre-lease loop is running) and you are not one of its iterations. Nothing was changed; the operator stops that loop.
  - `TASK_BUSY` — exit 4 with that sentinel: a live session owns the task (another hand-run, or the task window of a killed controller). Nothing was changed.
  - `RESUME_CONFLICT` — `resume` exit 5: the task's partial work lives in another checkout/branch (it started in the other `--mode`). Resume it from there, or `demote` it there.
  - `NO_CHANGES` — implementer did not produce a diff.
  - `COMPILE_BLOCKED` — Unity compile errors remain after 2 fix rounds in STEP 5b. Impossible in worktree mode (the gate is skipped, never run).
  - `PREFLIGHT_BLOCKED` — deterministic definite critical findings remain after the preflight-fix limit.
  - `REVIEW_BLOCKED` after Round 2 in STEP 6.
  - `VERIFY_BLOCKED` after Round 2 in STEP 7.
  - `RUNTIME_BLOCKED` after Round 2 in STEP 7.5 — **only when the diff's code failed at runtime**. Unity tooling dying mid-gate (unanswered modal / unresponsive bridge / game never boots) is NOT this token: it degrades to `runtime-smoke: skipped (…)` and the task still reaches STEP 8.
- **No `--ship-anyway` mode.** Self-heal fixes the cause or parks the task; it never ships past a failed gate. A parked task is retried with a fresh budget on the next loop launch (its partial work comes back as `parked_work`).
- **Push failure is a stop, not a retry.** STEP 9e prints `manual intervention required — push of <WORK_BRANCH> to origin failed; …` — never `--force`, never auto-rebase (push-in-session §4). The loop controller stops on that line even with self-heal on — no recovery iteration is spent on a diverged branch.
- **No PR creation.** The pipeline only pushes to the work branch; in worktree mode the user merges it manually after manual verification.
- **No deploy step.** Mobile game builds are done via Unity Editor, no CLI deploy exists.
- **No `npm run lint` equivalent.** Unity projects lack a CLI compilation check. Rely on the 3 quality gates + manual verification.
- **Verifier limitation:** qa-verifier is a static diff check. The runtime smoke gate (STEP 7.5) covers boot + console + spec recipes for M/L when the Editor is up — but it auto-skips when Unity MCP is absent and is a smoke test, not full QA. The manual verification steps in the task spec + DONE summary remain the final safety net — the user MUST still run them.
- **Worktree mode ships uncompiled code by design.** Both Unity gates are off, so the only checks left are the deterministic preflight and the LLM reviewers — none of which can tell whether the project builds. Never describe such a task as "verified"; say what ran and what did not, and lead the report with `/compile-check`.
- **Backlog writes never touch git.** STEP 9 stages only this task's files (push-in-session scoped stage); the backlog is inside `.git/`. If you ever see a task file in `git status`, something re-created it in the worktree — do not commit it, re-run `backlog-ops.py lint`.
