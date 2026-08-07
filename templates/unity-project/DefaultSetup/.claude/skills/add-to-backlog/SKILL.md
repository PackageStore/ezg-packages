---
description: Pick one or more tasks from backlog/planning/ into BACKLOG.md (assign NNN, move planning → todo, insert bullet). Bare invocation (no args) auto-picks ALL planning tasks. Used when the user says "add task to backlog" / "pick task to backlog" / "promote task". To CREATE a new task spec, use /planning-task. When intent is unclear between the two skills, confirm with the user first.
---

# Add to Backlog — Pick from Planning Agent

Move one or more tasks from `backlog/planning/` to the active queue. This is an **intentional commit step**: once picked, the task will be visible to `run-backlog` and will be implemented in the next run.

This skill is the counterpart to `/planning-task`:
- `/planning-task` = parallel-safe capture (writes to `backlog/planning/`, never touches `BACKLOG.md`)
- `/add-to-backlog` (this skill) = serial pick (reads `backlog/planning/`, moves to `backlog/todo/`, updates `BACKLOG.md`)

Since picking is a single-user serial operation, there is no race condition on `BACKLOG.md`.


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

The layout you operate on:
- `backlog/planning/<timestamp>-<TIER>-<slug>.md` = drafted, not yet queued (you read + pick from here)
- `backlog/todo/NNN-<TIER>-<slug>.md` = queued task (the promote script moves files here)
- `BACKLOG.md` = index (the promote script appends the bullets)

---

## Pipeline

```
[M] MODE             → bare invocation (no args at all) = AUTO-ALL; anything else = INTERACTIVE

AUTO-ALL:     [1] LIST → [5] PROMOTE (all listed tasks, no priority override) → [6] REPORT
INTERACTIVE:  [0] CONFIRM_INTENT → [1] LIST → [2] DISPLAY → [3] PICK → [4] OVERRIDE → [5] PROMOTE → [6] REPORT
```

> AUTO-ALL skips the interactive DISPLAY/PICK/OVERRIDE steps — no question is asked. INTERACTIVE steps [0], [2]–[4] are the only ones where you talk to the user. Step [5] always starts with a deterministic read-only check, then ONE mutating script call owns NNN assignment, the `git mv`, and the BACKLOG.md write. NEVER hand-edit `BACKLOG.md` or assign NNNs yourself.

---

## STEP M — Determine mode

- **Bare invocation** — the user typed `/add-to-backlog` (or said the equivalent, e.g. "add to backlog", "đưa vào backlog") with **no argument at all**: no index, no range, no task name, no filter. → **AUTO-ALL mode**: the intent is "queue everything currently in planning." Skip straight to STEP 1 (LIST), then STEP 5 (PROMOTE) with the full task-order list and no priority override, then STEP 6 (REPORT). Do not ask which tasks to pick.
- **Invocation with args** — the user named specific task(s), gave indices/ranges, or otherwise qualified the request (e.g. "add the campaign difficulty task to backlog", "pick 1,3", "promote the HIGH priority ones") → **INTERACTIVE mode**: run the full pipeline (STEP 0 → STEP 4) exactly as before.

If genuinely unsure which mode applies (rare — the args are ambiguous, not simply absent), default to INTERACTIVE and let STEP 0/STEP 2 sort it out.

---

## STEP 0 — Confirm intent (INTERACTIVE mode only, if ambiguous)

Skip this step entirely if the user's message clearly says "pick", "add task to backlog", "promote", or names an existing planning task. Also skip entirely in AUTO-ALL mode.

Confirm only when there is genuine ambiguity, for example:
- The user says "add task X" without "to backlog" → they might want to create a new planning task → ask.
- The user names a task that is not in `backlog/planning/` → they might have meant `/planning-task` → ask:
  > *"This task is not in planning. Would you like to (a) create a new planning task with `/planning-task`, or (b) did you mistype the name?"*

Maximum ONE confirmation question. If still unclear, default to listing planning tasks and letting the user pick.

---

## STEP 1 — List planning tasks

Runs in both modes.

1. Glob `$BACKLOG_ROOT/planning/*.md` (ignore `.gitkeep` and non-`.md` files). Use the resolved absolute path — the directory is outside the worktree.
2. For each file, parse:
   - **Filename**: `<timestamp>[-<index>]-<TIER>-<slug>.md`
     - `timestamp` = first 18 characters (`YYYYMMDDTHHmmssSSS`)
     - `index` = OPTIONAL numeric segment right after the timestamp (e.g. `01`, `37`) — present when tasks are batch-seeded for a roadmap phase, so it encodes the intended dependency sequence. Absent for one-off drafts.
     - `TIER` = `XS` | `S` | `M` | `L` (between the first and second `-` after the timestamp/index)
     - `slug` = everything between the tier and `.md`
   - **Priority** from the file content: the first heading matching `### [PRIORITY] ...` where `PRIORITY` is `HIGH` / `MEDIUM` / `LOW`.
   - **Title** from the same heading: text after `[PRIORITY]`, trimmed.
   - **Display timestamp**: reformat to `YYYY-MM-DD HH:mm`.
3. Sort in **task order** = ascending by `(timestamp, index, filename)`. This is FIFO (oldest draft first) and, for a batch-seeded phase, preserves the authored dependency sequence (`01 → 02 → …`). Do **NOT** sort by priority.

If the result is empty → notify the user:
> *"Planning is empty, no tasks to pick. Use `/planning-task` (or 'create planning task') to create a new task."*
Then exit.

---

## STEP 2 — Display (INTERACTIVE mode only — skip in AUTO-ALL)

Render each planning task as an indexed line, in **task order** (the STEP 1 sort):

```
[1] [S]  [HIGH]   Glory Pass final sprint offer — 2026-05-23 14:23
[2] [S]  [MEDIUM] Fix notification badge stale   — 2026-05-23 14:25
[3] [L]  [MEDIUM] IAP purchase flow migration    — 2026-05-22 09:15
[4] [XS] [LOW]    Tweak CSV config constant       — 2026-05-22 08:01
```

- Pad the tier to 2 characters and the priority to 6 characters so the columns align.
- Listed in task order (lowest index / oldest first) so the displayed index `[n]`, the NNN assigned in STEP 5, and the final TODO position all line up.
- Show timestamp as `YYYY-MM-DD HH:mm` (omit ms for readability).

Then ask:
> *"Which task(s) to pick? (`1`, `1,3`, `1-3`, or `all`)"*

---

## STEP 3 — Pick (INTERACTIVE mode only — skip in AUTO-ALL, which always targets the full list)

Accept the following input formats:
- Single index: `2`
- Comma-separated: `1,3,5`
- Space-separated: `1 3 5`
- Range: `1-3` (inclusive of both ends)
- Mixed: `1-2,4`
- `all` → all planning tasks

Validate:
- All indices must exist in the displayed list.
- If any index is invalid → report which one and re-ask (max 2 re-asks, then abort).

Treat the pick as a **set**, not a sequence: no matter what order the indices are typed (`5,3,1`), the promote script (STEP 5) assigns NNN and appends bullets in **task order** (the STEP 1 sort), so the queue always stays dependency-ordered.

---

## STEP 4 — Override priority tag (optional, INTERACTIVE mode only — skip in AUTO-ALL)

> Priority is a **metadata tag only** — it no longer changes a task's position in `## TODO` (the queue is ordered by task order, see STEP 5). Override it only when the tag itself is wrong, not to reorder the queue.

Ask once for the batch:
> *"Keep current priorities for all picked tasks, or would you like to override any?"*

Accept:
- `keep` / `giữ` / empty → do not override, use current priorities
- Per-task override format: `2:HIGH, 4:LOW` (index from STEP 2 + new priority)

**Tier CANNOT be changed.** If the user requests a tier change → reply:
> *"The tier is a property of the task (determined during capture). If you want to change the tier, edit the `backlog/planning/<filename>.md` file directly and pick again."*

Then proceed without changing the tier.

---

## STEP 5 — Promote (deterministic)

In AUTO-ALL mode the picked set = every file from STEP 1 (task order). In INTERACTIVE mode the picked set = the user's STEP 3 selection.

First run the mandatory read-only preflight with all picked planning files:

```bash
python3 .claude/scripts/backlog-ops.py promote --check backlog/planning/<file1>.md [backlog/planning/<file2>.md ...]
```

- `ok = false` in **INTERACTIVE mode** → **do not run the mutating command**. Resolve every `tier_errors[]`, `dependency_warnings[]`, and `mockup_warnings[]` with the user, then re-run `--check`.
- `ok = false` in **AUTO-ALL mode** → do NOT ask the user and do NOT abort the whole batch. Instead, drop exactly the file(s) named in `tier_errors[]` / `dependency_warnings[]` / `mockup_warnings[]` from the batch, re-run `--check` on the reduced set, and repeat until `ok = true` or the set is empty. Carry the dropped files + their blocker reason forward to STEP 6 as "skipped" — they stay untouched in `backlog/planning/` for a later manual/interactive pick.
- Missing dependencies must be included earlier in the same batch or already exist in todo/in-progress/done.
- Pending mockups must be approved to a `.png` (run `/ui-mockup` — it drafts + auto-approves; only screens with open forbidden-to-invent questions need the dev) or changed to a valid `clone:<ExistingPrefab>` ground truth.
- There is no proceed-anyway path for the files that remain blocked: normal `promote` enforces the same blockers before its first mutation.

Only when `--check` returns `ok = true`, run ONE mutating script call (any argv order — the script sorts them into **task order** internally, assigns consecutive NNNs from `max(todo, in-progress, done)+1`, does each `git mv`, appends the bullets to the END of `## TODO` in one atomic write, and self-lints):

```bash
python3 .claude/scripts/backlog-ops.py promote backlog/planning/<file1>.md [backlog/planning/<file2>.md ...]
```

- **Priority override** (from STEP 4): `--priority HIGH|MEDIUM|LOW` applies to every file in the call. When overrides **differ per task**, run one `promote` call **per file, in task order** (earliest timestamp first) — never group by priority, or NNN assignment can invert the dependency chain. No override → each file keeps the priority parsed from its own `### [PRIORITY]` heading.
- The JSON result lists `moved[]` (`nnn`, `path`, `tier`, `priority`, `title`) + `actions[]` + `dependency_warnings[]` + `mockup_warnings[]` + a `lint` block. A failed `git mv` is reported as a `FAILED ...` action and the batch continues — report skipped files in STEP 6. (Untracked planning files are handled automatically: the script falls back to filesystem-move + `git add`.)
- **`dependency_warnings[]` non-empty during `--check`** → block promotion. Include the missing upstream planning file(s) or correct/remove an obsolete dependency in the task spec, then check again.
- **`mockup_warnings[]` non-empty during `--check`** → block promotion. Run `/ui-mockup` (drafts missing screens + auto-approves everything that validates); or use `groundTruth=clone:<ExistingPrefab>` when appropriate, then check again.
- If `lint.ok = false` after the write → the errors are pre-existing index damage; surface them to the user in STEP 6.

> **Why task order, not priority buckets?** A seeded roadmap phase is a dependency chain — task `N+1` often `depends_on` task `N`. Bucketing by priority (HIGH→MEDIUM→LOW) reorders the chain and can place a dependent task *above* its own dependency — exactly the inversion that breaks sequential execution. `run-backlog` picks the **first** bullet in `## TODO`, so index order = execution order. Use `[PRIORITY]` to signal importance, never to sequence the queue.

**NEVER hand-edit `BACKLOG.md`, hand-assign NNNs, or run a manual `git mv` for a pick — the promote script owns all three so the index can never drift.**

---

## STEP 6 — Report

Notify the user, in order:
1. **Mode used**: in AUTO-ALL mode, lead with e.g. *"Bare invocation → auto-picked all N planning tasks."* In INTERACTIVE mode, skip this line.
2. **Number of tasks picked**: e.g., *"Picked 3 tasks from planning."*
3. **List each moved task**:
   ```
   [041] [HIGH]   New shop popup feature   → backlog/todo/041-M-new-shop-popup-feature.md
   [042] [MEDIUM] Fix notification badge   → backlog/todo/042-S-notification-badge-stale.md
   ```
4. **Priority overrides applied** (if any): *"Task [042] changed MEDIUM → HIGH."* (never happens in AUTO-ALL mode)
5. **Position in queue**: e.g., *"Tasks #041–#043 appended to the end of TODO in task order; #041 runs after the tasks already queued."*
6. **Remaining planning tasks**: e.g., *"2 planning tasks remaining."*
7. **Skipped tasks** (if any move failed, or were dropped in AUTO-ALL mode due to blockers): name + reason, suggesting a manual fix (e.g. run `/ui-mockup`, or pick interactively once the missing dependency is queued).

DO NOT commit. The user may want to review before `run-backlog` picks it up.
