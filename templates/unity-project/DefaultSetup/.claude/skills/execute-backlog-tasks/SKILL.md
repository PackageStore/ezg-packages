---
name: execute-backlog-tasks
description: Automatically execute backlog tasks by launching the run-backlog loop runner. On macOS/Linux it runs run-backlog-loop.sh (which spawns one Terminal window per task); on Windows it falls back to the PowerShell/.bat runner. Triggers on requests like "execute backlog tasks", "run backlog loop", "execute tasks in backlog", "run backlog".
---

# Execute Backlog Tasks

Launch the backlog loop runner so it processes queued tasks one at a time. Pick the launcher by OS — **do not run the PowerShell/.bat path on macOS/Linux** (it will fail), and do not run the `.sh` path on Windows.

## STEP 0 — Detect OS

Route on the environment platform:
- **macOS / Linux (`darwin`, `linux`)** → use `.claude/scripts/run-backlog-loop.sh` (STEP 1A).
- **Windows** → use the PowerShell `.bat`/`.ps1` runner (STEP 1B).

If unsure, prefer the `.sh` path — that is the maintained runner for this template.

## STEP 1A — macOS / Linux

1. Get the absolute repo root dynamically (do not hardcode): the directory containing `.claude/`.
2. The `.sh` runner is itself the **controller**: it spawns one new Terminal window per task (via `osascript`) and waits for each to finish before the next. So you only launch it once; it does the looping.
3. Run it with `--auto-model-by-tier` so each task window uses the tier-mapped model/effort (XS/S → sonnet, M → sonnet/high, L → opus/xhigh):

   ```bash
   bash <REPO_ROOT>/.claude/scripts/run-backlog-loop.sh --auto-model-by-tier
   ```

   Optional flags: `--max-iterations <n>` to cap the run, `--inline` to run every task in the current window instead of spawning new ones.
4. The runner pauses on its own when the backlog is empty (`PAUSED` sentinel) or stops on a blocker (`COMPILE_BLOCKED` / `PREFLIGHT_BLOCKED` / `REVIEW_BLOCKED` / `VERIFY_BLOCKED`). Logs land in `logs/backlog-loop/`.
5. Notify the user that the loop is running, which model map is in effect, and where the logs are.

> Granting **Automation permission to Terminal** is required the first time so `osascript` can open task windows. If that is denied, use `--inline`.

## STEP 1B — Windows (fallback)

1. Identify the absolute workspace path dynamically.
2. Use the PowerShell tool to spawn a new detached window (no `-Verb RunAs` — UAC elevation is not needed and fails in non-interactive context):
   ```powershell
   Start-Process powershell -ArgumentList "-NoExit", "-Command", "& { Set-Location '<WorkspacePath>'; & '<WorkspacePath>\.agents\scripts\run-backlog-loop.ps1' --auto-model-by-tier }"
   ```
3. The new window runs independently — do NOT wait for it to finish.
4. Notify the user that the loop is running in the background.
