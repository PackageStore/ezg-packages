---
name: execute-backlog-tasks
description: Automatically execute backlog tasks by launching the cross-platform run-backlog loop runner. On macOS/Linux it uses run-backlog-loop.sh; on Windows it uses the PowerShell/.bat runner.
---

# Execute Backlog Tasks

Launch the backlog loop runner so it processes queued tasks one at a time. Pick the launcher by OS — **do not run the PowerShell/.bat path on macOS** (it will fail).

## STEP 0 — Detect OS

Detect the current platform and route accordingly:
- **macOS / Linux (`darwin`, `linux`)** → use `.claude/scripts/run-backlog-loop.sh` (STEP 1A).
- **Windows** → use the PowerShell `.bat` runner (STEP 1B).

If unsure on a Unix-like shell, prefer the `.sh` path.

## STEP 1A — macOS / Linux

1. Get the absolute repo root dynamically (do not hardcode): the directory containing `.claude/`.
2. The `.sh` runner is itself the **controller**: it spawns one new Terminal window per task (via `osascript`) and waits for each to finish before the next. So you only launch it once; it does the looping.
3. Run it in the background with `--auto-model-by-tier` so each task window uses the tier-mapped model/effort (XS/S → sonnet, M → sonnet/high, L → opus/xhigh):

   ```bash
   bash <REPO_ROOT>/.claude/scripts/run-backlog-loop.sh --auto-model-by-tier
   ```

   Optional flags: `--max-iterations <n>` to cap the run, `--inline` to run every task in the current window instead of spawning new ones.
4. The runner pauses on its own when the backlog is empty (`PAUSED`) or when every remaining task needs the Editor (`EDITOR_REQUIRED`). It stops on compile/preflight/review/verify/runtime/mockup/visual blockers. A `DEFERRED` iteration is successful and the loop continues. Logs land in `logs/backlog-loop/`.
5. Notify the user that the loop is running, which model map is in effect, and where the logs are.

> Granting **Automation permission to Terminal** is required the first time so `osascript` can open task windows. If that is denied, use `--inline`.

## STEP 1B — Windows (fallback)

1. Identify the absolute workspace path dynamically.
2. Use the PowerShell tool to spawn a new detached window (no `-Verb RunAs`):
   ```powershell
   Start-Process powershell -ArgumentList "-NoExit", "-Command", "& { Set-Location '<WorkspacePath>'; & '<WorkspacePath>\.agents\scripts\run-backlog-loop.ps1' --auto-model-by-tier }"
   ```
3. The new window runs independently — do NOT wait for it to finish.
4. Notify the user that the loop is running in the background.
