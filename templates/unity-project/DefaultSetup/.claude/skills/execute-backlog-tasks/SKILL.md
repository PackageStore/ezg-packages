---
name: execute-backlog-tasks
description: Automatically execute backlog tasks by launching the run-backlog loop runner. On macOS/Linux it runs run-backlog-loop.sh (which spawns one Terminal window per task); on Windows it falls back to the PowerShell/.bat runner. Triggers on requests like "execute backlog tasks", "run backlog loop", "execute tasks in backlog", "run backlog".
---

# Execute Backlog Tasks

Launch the backlog loop runner so it processes queued tasks one at a time. Pick the launcher by OS — **do not run the PowerShell/.bat path on macOS** (it will fail), and do not run the `.sh` path on Windows.

The runner is the **controller**: it captures the currently checked-out branch once at startup, spawns one new task window per iteration, and waits for it to finish before the next. It commits + pushes each DONE task to the work branch; it never creates a PR.

**Ask the user which mode to run in before launching** — the trade-off is real and only they can accept it:

| | `current` (default) | `worktree` |
|---|---|---|
| Where | this checkout | sibling `<repo>-agent-<base>` |
| Commits to | the branch already checked out | `agent/dev-<base>`, merged by the user |
| Dev can keep working | **no** — the agent stages with `git add -A` | yes |
| Compile check + runtime smoke | **yes** | **no** (no `.sln`, no Editor) |

Worktree mode ships code that was never compiled, so its DONE report must lead with
`/compile-check` after merging. Pass it as `--mode worktree` (bash) / `-Mode Worktree`
(PowerShell); `run-backlog-loop.bat` prompts for it interactively.

## STEP 0 — Detect OS

Route on the environment platform:
- **macOS / Linux (`darwin`, `linux`)** → use `.claude/scripts/run-backlog-loop.sh` (STEP 1A).
- **Windows** → use the PowerShell `.bat` runner (STEP 1B).

If unsure, prefer the `.sh` path on a `darwin`/`linux` host.

## STEP 1A — macOS / Linux

1. Get the absolute repo root dynamically (do not hardcode): the directory containing `.claude/`.
2. The `.sh` runner spawns one new Terminal window per task (via `osascript`) and waits for each to finish before spawning the next. You launch it once; it does the looping.
3. Run it in the background with `--auto-model-by-tier` so each task window uses the tier-mapped model/effort (quality-first map: **XS/S → sonnet**, **M/L → opus**, effort scaling XS=medium → L=xhigh):

   ```bash
   bash <REPO_ROOT>/.claude/scripts/run-backlog-loop.sh --auto-model-by-tier
   ```

   Optional flags: `--max-iterations <n>` to cap the run, `--inline` to run every task in the current window instead of spawning new ones, `--model <id> --effort <level>` to pin one profile for all tasks.
4. The runner pauses on its own when the backlog is empty (`PAUSED` sentinel) or stops on a blocker (`COMPILE_BLOCKED` / `PREFLIGHT_BLOCKED` / `REVIEW_BLOCKED` / `VERIFY_BLOCKED`). Logs land in `logs/backlog-loop/`.
5. Notify the user that the loop is running, which model map is in effect, and where the logs are.

> Double-clicking `.claude/scripts/run-backlog-loop.command` in Finder also starts the loop with sensible defaults.
>
> Granting **Automation permission to Terminal** is required the first time so `osascript` can open task windows. If that is denied, use `--inline`.
>
> First-time setup on a fresh clone: run `bash .claude/scripts/sync-to-agents.sh` once so Claude Code sees the project skills/agents/commands (the `.claude/` link views are gitignored).

## STEP 1B — Windows (fallback)

1. Identify the absolute workspace path dynamically.
2. Use the PowerShell tool to spawn a new detached window (no `-Verb RunAs`):
   ```powershell
   Start-Process powershell -ArgumentList "-NoExit", "-Command", "& { Set-Location '<WorkspacePath>'; & '<WorkspacePath>\.claude\scripts\run-backlog-loop.bat' }"
   ```
   (`run-backlog-loop.bat` prompts for provider; `run-backlog-loop.ps1` runs Claude headless directly.)
3. The new window runs independently — do NOT wait for it to finish.
4. Notify the user that the loop is running in the background.
