# run-backlog — STEP 1c · 1d · 1f — ship recovery, resume, recovery iteration

Conditional part of [`SKILL.md`](SKILL.md) (see its *Conditional sections* table). Applies when `pick` returns `ship-pending` or `in-progress`, or the prompt carries `AGENT_RECOVERY_BRIEF`. (1e, the checkpointed exit, stays in SKILL.md — it applies to every iteration.) Every rule here is as binding as SKILL.md itself.

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
