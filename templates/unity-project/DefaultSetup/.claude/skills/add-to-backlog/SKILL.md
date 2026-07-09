---
name: add-to-backlog
description: Pick one or more tasks from backlog/planning/ into BACKLOG.md (assign NNN, move planning → todo, insert bullet). Used when the user says "add task to backlog" / "pick task to backlog" / "promote task". To CREATE a new task spec, use /planning-task. When intent is unclear between the two skills, confirm with the user first.
---

# Add to Backlog — Pick from Planning Agent

Move one or more tasks from `backlog/planning/` to the active queue. This is an **intentional commit step**: once picked, the task will be visible to `run-backlog` and will be implemented in the next run.

This skill is the counterpart to `/planning-task`:
- `/planning-task` = parallel-safe capture (writes to `backlog/planning/`, never touches `BACKLOG.md`)
- `/add-to-backlog` (this skill) = serial pick (reads `backlog/planning/`, moves to `backlog/todo/`, updates `BACKLOG.md`)

Since picking is a single-user serial operation, there is no race condition on `BACKLOG.md`.

The layout you operate on:
- `backlog/planning/<timestamp>-<TIER>-<slug>.md` = drafted, not yet queued (you read + move from here)
- `backlog/todo/NNN-<slug>.md` = queued task (the promote script moves files here)
- `BACKLOG.md` = index (the promote script appends the bullets)

---

## Pipeline

```
[0] CONFIRM_INTENT   → if ambiguous between pick vs create, ask
[1] LIST             → glob backlog/planning/*.md, parse index/tier/priority/title/timestamp
[2] DISPLAY          → show indexed list in TASK ORDER (lowest index / oldest first)
[3] PICK             → user selects 1 / multiple / range / all
[4] OVERRIDE         → optional priority TAG override (metadata only; does NOT reorder queue. tier CANNOT be changed)
[5] PROMOTE          → backlog-ops promote: assigns NNN + git mv planning → todo + appends bullets + self-lints
[6] REPORT           → summarize what was picked and where
```

> Steps [1]–[4] are interactive (you talk to the user). Step [5] is deterministic — ONE script call owns NNN assignment, the `git mv`, and the BACKLOG.md write. NEVER hand-edit `BACKLOG.md` or assign NNNs yourself.

---

## STEP 0 — Confirm intent (if ambiguous)

Skip this step entirely if the user's message clearly says "pick", "add task to backlog", "promote", or names an existing planning task.

Confirm only when there is genuine ambiguity:
- The user says "add task X" without "to backlog" → they might want to create a new planning task → ask.
- The user names a task that is not in `backlog/planning/` → ask:
  > *"This task is not in planning. Would you like to (a) create a new planning task with `/planning-task`, or (b) did you mistype the name?"*

Maximum ONE confirmation question. If still unclear, default to listing planning tasks and letting the user pick.

---

## STEP 1 — List planning tasks

1. Glob `backlog/planning/*.md` (ignore `.gitkeep` and non-`.md` files).
2. For each file, parse:
   - **Filename**: `<timestamp>[-<index>]-<TIER>-<slug>.md`
     - `timestamp` = first 18 characters (`YYYYMMDDTHHmmssSSS`)
     - `index` = OPTIONAL numeric segment right after the timestamp (e.g. `01`, `37`) — present when tasks are batch-seeded for a roadmap phase, so it encodes the intended dependency sequence. Absent for one-off drafts.
     - `TIER` = `XS` | `S` | `M` | `L`
     - `slug` = everything between the tier and `.md`
   - **Priority** from file content: first heading matching `### [PRIORITY] ...`
   - **Title** from the same heading.
   - **Display timestamp**: reformat to `YYYY-MM-DD HH:mm`.
3. Sort in **task order** = ascending by `(timestamp, index, filename)`. This is FIFO (oldest draft first) and, for a batch-seeded phase, preserves the authored dependency sequence (`01 → 02 → …`). Do **NOT** sort by priority.

If the result is empty → notify the user:
> *"Planning is empty, no tasks to pick. Use `/planning-task` (or 'create planning task') to create a new task."*
Then exit.

---

## STEP 2 — Display

Render each planning task as an indexed line, in **task order** (the STEP 1 sort):

```
[1] [S]  [HIGH]   Bootstrap CSV pipeline      — 2026-05-22 09:15
[2] [M]  [HIGH]   Author balance CSV tables   — 2026-05-22 09:15
[3] [S]  [HIGH]   Author config CSV model     — 2026-05-22 09:15
[4] [M]  [MEDIUM] New shop popup feature      — 2026-05-23 14:23
```

- Pad the tier to 2 characters and priority to 6 characters for column alignment.
- Listed in task order (lowest index / oldest first) so the displayed index `[n]`, the NNN assigned in STEP 5, and the final TODO position all line up.

Then ask:
> *"Which task(s) to pick? (`1`, `1,3`, `1-3`, or `all`)"*

---

## STEP 3 — Pick

Accept the following input formats:
- Single index: `2`
- Comma-separated: `1,3,5`
- Space-separated: `1 3 5`
- Range: `1-3` (inclusive)
- Mixed: `1-2,4`
- `all` → all planning tasks

Validate:
- All indices must exist in the displayed list.
- If any index is invalid → report which one and re-ask (max 2 re-asks, then abort).

Treat the pick as a **set**, not a sequence: no matter what order the indices are typed (`5,3,1`), the promote script (STEP 5) assigns NNN and appends bullets in **task order** (the STEP 1 sort), so the queue always stays dependency-ordered.

---

## STEP 4 — Override priority tag (optional)

> Priority is a **metadata tag only** — it no longer changes a task's position in `## TODO` (the queue is ordered by task order, see STEP 5). Override it only when the tag itself is wrong, not to reorder the queue.

Ask once for the batch:
> *"Keep current priorities for all picked tasks, or would you like to override any?"*

Accept:
- keep / empty → do not override
- Per-task override format: `2:HIGH, 4:LOW`

**Tier CANNOT be changed.** If the user requests a tier change → reply:
> *"The tier is a property of the task (determined during capture). If you want to change the tier, edit the `backlog/planning/<filename>.md` file directly and pick again."*

---

## STEP 5 — Promote (deterministic)

Run ONE script call with all picked planning files (any argv order — the script sorts them into **task order** internally, assigns consecutive NNNs from `max(todo, in-progress, done)+1`, does each `git mv`, appends the bullets to the END of `## TODO` in one atomic write, and self-lints):

```bash
python3 .claude/scripts/backlog-ops.py promote backlog/planning/<file1>.md [backlog/planning/<file2>.md ...]
```

- **Priority override** (from STEP 4): `--priority HIGH|MEDIUM|LOW` applies to every file in the call. When overrides **differ per task**, run one `promote` call **per file, in task order** (earliest timestamp first) — never group by priority, or NNN assignment can invert the dependency chain. No override → each file keeps the priority parsed from its own `### [PRIORITY]` heading.
- The JSON result lists `moved[]` (`nnn`, `path`, `tier`, `priority`, `title`) + `actions[]` + a `lint` block. A failed `git mv` is reported as a `FAILED ...` action and the batch continues — report skipped files in STEP 6.
- If `lint.ok = false` after the write → the errors are pre-existing index damage; surface them to the user in STEP 6.

> **Why task order, not priority buckets?** A seeded roadmap phase is a dependency chain — task `N+1` often `depends_on` task `N`. Bucketing by priority (HIGH→MEDIUM→LOW) reorders the chain and can place a dependent task *above* its own dependency — exactly the inversion that breaks sequential execution. `run-backlog` picks the **first** bullet in `## TODO`, so index order = execution order. Use `[PRIORITY]` to signal importance, never to sequence the queue.

---

## STEP 6 — Report

Notify the user, in order:
1. **Number of tasks picked**: e.g., *"Picked 3 tasks from planning."*
2. **List each moved task**:
   ```
   [011] [HIGH]   New shop popup feature   → backlog/todo/011-new-shop-popup-feature.md
   [012] [MEDIUM] Fix notification badge   → backlog/todo/012-notification-badge-stale.md
   ```
3. **Priority overrides applied** (if any).
4. **Position in queue**: e.g., *"Tasks #011–#013 appended to the end of TODO in task order; #011 runs after the 10 already queued."*
5. **Remaining planning tasks**: e.g., *"2 planning tasks remaining."*
6. **Skipped tasks** (if any move failed): name + reason.

DO NOT commit. The user may want to review before `run-backlog` picks it up.
