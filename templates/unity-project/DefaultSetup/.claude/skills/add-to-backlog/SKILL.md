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
- `backlog/todo/NNN-<slug>.md` = queued task (you write here)
- `BACKLOG.md` = index (you append a bullet for each picked task)

---

## Pipeline

```
[0] CONFIRM_INTENT   → if ambiguous between pick vs create, ask
[1] LIST             → glob backlog/planning/*.md, parse tier/priority/title/timestamp
[2] DISPLAY          → show indexed list, newest first
[3] PICK             → user selects 1 / multiple / range / all
[4] OVERRIDE         → optional per-task priority override (tier CANNOT be changed)
[5] ASSIGN_NNN       → scan todo/ + in-progress/ + done/, assign consecutive NNNs
[6] MOVE             → git mv each picked file from planning/ → todo/<NNN>-<slug>.md
[7] UPDATE_BACKLOG   → single write to BACKLOG.md appending all new bullets to end of TODO (by order, not priority)
[8] REPORT           → summarize what was picked and where
```

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
   - **Filename**: `<timestamp>-<TIER>-<slug>.md`
     - `timestamp` = first 18 characters (`YYYYMMDDTHHmmssSSS`)
     - `TIER` = `XS` | `S` | `M` | `L`
     - `slug` = everything between the tier and `.md`
   - **Priority** from file content: first heading matching `### [PRIORITY] ...`
   - **Title** from the same heading.
   - **Display timestamp**: reformat to `YYYY-MM-DD HH:mm`.
3. Sort newest first (descending timestamp).

If the result is empty → notify the user:
> *"Planning is empty, no tasks to pick. Use `/planning-task` (or 'create planning task') to create a new task."*
Then exit.

---

## STEP 2 — Display

Render each planning task as an indexed line:

```
[1] [M]  [HIGH]   New shop popup feature     — 2026-05-23 14:23
[2] [S]  [MEDIUM] Fix notification badge     — 2026-05-23 14:25
[3] [L]  [MEDIUM] IAP purchase flow          — 2026-05-22 09:15
[4] [XS] [LOW]    Tweak CSV config constant  — 2026-05-22 08:01
```

- Pad the tier to 2 characters and priority to 6 characters for column alignment.
- Newest on top.

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

Preserve the user's pick order for NNN assignment in STEP 5.

---

## STEP 4 — Override priority (optional)

> **Note:** priority is an **informational label only** — it does NOT affect queue position. Tasks always run in order of addition (see STEP 7). Overriding a priority just changes the label shown in the bullet.

Ask once for the batch:
> *"Keep current priorities for all picked tasks, or would you like to override any?"*

Accept:
- `keep` / `giữ` / empty → do not override
- Per-task override format: `2:HIGH, 4:LOW`

**Tier CANNOT be changed.** If the user requests a tier change → reply:
> *"The tier is a property of the task (determined during capture). If you want to change the tier, edit the `backlog/planning/<filename>.md` file directly and pick again."*

---

## STEP 5 — Assign NNN

1. List filenames in `backlog/todo/`, `backlog/in-progress/`, and `backlog/done/`.
2. Extract the leading `NNN` from each filename (regex: `^(\d+)-`). Find the maximum.
3. For a batch of K picked tasks, assign `max+1`, `max+2`, …, `max+K` in the pick order.
4. Zero-pad to 3 digits (`021`, not `21`).

Example: if current max NNN is `010` and the user picks 3 tasks → assign `011`, `012`, `013`.

---

## STEP 6 — Move files

For each picked task, in the pick order:

```
git mv backlog/planning/<original-filename>.md backlog/todo/<NNN>-<slug>.md
```

- `<slug>` is parsed from the original planning filename (omitting the timestamp + tier prefix).
- Use `git mv` (not `mv`) to preserve git history.
- If `git mv` fails for any file: skip that file, report the error in STEP 8, continue with remaining picks. DO NOT abort the whole batch.

**Slug collision in `todo/`** (rare): if `backlog/todo/<NNN>-<slug>.md` already exists, append `-2` to the slug.

---

## STEP 7 — Update BACKLOG.md (single atomic write)

1. Read `BACKLOG.md` once.
2. Find the `## TODO` section.
3. For each successfully moved task, build a bullet using the **final** priority (post-override, kept as a label only) and the **original** tier:
   ```
   - [PRIORITY] [Tier] [Title](backlog/todo/<NNN>-<slug>.md)
   ```
   Example: `- [HIGH] [M] [New shop popup feature](backlog/todo/011-new-shop-popup-feature.md)`
4. **Append** the bullets to the **end** of `## TODO`, in the pick order (ascending NNN). The queue is **strictly order-of-addition (FIFO)** — do **NOT** sort or bucket by priority/rarity. The `[PRIORITY]` label is informational only and has no effect on queue position.
5. Preserve all existing bullets and their existing order. New bullets always go **after** the last existing TODO bullet.
6. If there is a `- (none)` line in `## TODO`, delete it when inserting.
7. Write `BACKLOG.md` ONCE (single atomic Write call).

If the picked batch has mixed priorities (e.g. 1 HIGH + 2 MEDIUM), still append them all in pick order — do NOT regroup by priority. The earliest-assigned NNN sits above the later ones.

---

## STEP 8 — Report

Notify the user, in order:
1. **Number of tasks picked**: e.g., *"Picked 3 tasks from planning."*
2. **List each moved task**:
   ```
   [011] [HIGH]   New shop popup feature   → backlog/todo/011-new-shop-popup-feature.md
   [012] [MEDIUM] Fix notification badge   → backlog/todo/012-notification-badge-stale.md
   ```
3. **Priority overrides applied** (if any).
4. **Position in queue**: e.g., *"Task #011 appended to the end of TODO (will run after all currently-queued tasks)."* The queue runs strictly in order of addition.
5. **Remaining planning tasks**: e.g., *"2 planning tasks remaining."*
6. **Skipped tasks** (if any move failed): name + reason.

DO NOT commit. The user may want to review before `run-backlog` picks it up.
