# Backlog

Tasks live as **individual files** in `backlog/{todo,in-progress,done}/`. This file is just the **index** — the agent reads only this file + the one task it picks, so tokens stay flat regardless of how many tasks accumulate.

> **Remote:** if this repo has an `origin` remote, `/run-backlog` commits **and pushes** each task to `agent/dev` (it does NOT create a PR; you merge `agent/dev → base branch` manually). If there is no remote (a fresh project generated from this template), the pipeline commits locally and skips the push. See "REMOTE DETECTION" in `.claude/skills/run-backlog/SKILL.md`.

## Usage

- Agent gets the next task using `python3 .claude/scripts/backlog-ops.py pick` (reads this index, returns JSON), then reads exactly that task file
- When starting work: `backlog-ops.py start <NNN>` (git mv todo → in-progress + updates the `## IN PROGRESS` section here)
- When done: `backlog-ops.py done <NNN>` (git mv in-progress → done + removes bullet)
- DO NOT hand-edit this index file — all state transitions go through `backlog-ops.py`; `backlog-ops.py lint` checks directory↔index consistency
- Detailed format: see `backlog/_TEMPLATE.md`

## Ordering Rules in TODO

- Tasks are ordered by **insertion order** (FIFO): earlier-added on top, later-added below — do NOT sort by priority/rarity. `run-backlog` always picks the first task in the list.
- The `[PRIORITY]` tag (HIGH/MEDIUM/LOW) is **metadata only**, it does NOT change a task's position in the queue.
- NNN ascends with insertion order; the filename `NNN-slug.md` is also the execution order.

---

## TODO

- (none)

## IN PROGRESS

- (none)

## DONE

See `backlog/done/` — each completed task is a separate file with a summary and commit link. DO NOT list bullets here (`python3 .claude/scripts/backlog-ops.py lint` will block).
