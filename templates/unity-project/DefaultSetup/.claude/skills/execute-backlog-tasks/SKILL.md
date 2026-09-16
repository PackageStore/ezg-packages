---
name: execute-backlog-tasks
description: Automatically execute backlog tasks by launching the run-backlog loop runner. Runs with defaults immediately — no questions asked — unless the user passes explicit parameters. On macOS/Linux it runs run-backlog-loop.sh (which spawns one Terminal window per task); on Windows it falls back to the PowerShell runner. Triggers on requests like "execute backlog tasks", "run backlog loop", "execute tasks in backlog", "run backlog".
---

# Execute Backlog Tasks

Launch the backlog loop runner so it processes queued tasks one at a time. Pick the launcher by OS — **do not run the PowerShell/.bat path on macOS** (it will fail), and do not run the `.sh` path on Windows.

The runner is the **controller**: it captures the currently checked-out branch once at startup, spawns one new task window per iteration, and waits for it to finish before the next. It commits + pushes each DONE task to the work branch; it never creates a PR.

## STEP 0 — Resolve parameters (NEVER ask the user)

**Bare invocation runs immediately with the defaults below.** Do not ask which mode, which model,
how many iterations, or whether to proceed — launch it. The user asked to run the backlog; the
defaults are the answer.

| Parameter | Default on a bare run | Override only when the user says so |
|---|---|---|
| Mode | `current` — commits onto the branch already checked out | `worktree` |
| Model / effort | `--auto-model-by-tier` (every tier → opus, effort XS=medium → L=xhigh) | `--model <id> --effort <level>` |
| Iterations | runner default (100) | `--max-iterations <n>` |
| Window | new Terminal window per task | `--inline` |
| Permissions | `--dangerously-skip-permissions` (runner default) | `--no-skip-permissions` |

**Read overrides off the user's own words** — they are custom only when explicitly asked for:

- `worktree` / "chạy worktree" / "để tôi làm tiếp trong lúc chạy" / "đừng đụng branch hiện tại" → `--mode worktree`
- "chạy N task" / "chỉ 1 task" / "one task" → `--max-iterations N`
- "chạy trong cửa sổ này" / "đừng mở cửa sổ mới" / "inline" → `--inline`
- names a model/effort ("chạy fable", "effort xhigh") → `--model` / `--effort` (drops `--auto-model-by-tier`)

Anything the user did not mention keeps its default. Pass through any raw flag they typed verbatim.

### Mode trade-off (reference — decide from the rules above, don't ask)

| | `current` (default) | `worktree` |
|---|---|---|
| Where | this checkout | sibling `<repo>-agent-<base>` |
| Commits to | the branch already checked out | `agent/dev-<base>`, merged by the user |
| Dev can keep working | **no** — the agent stages with `git add -A` | yes |
| Compile check + runtime smoke | **yes** | **no** (no `.sln`, no Editor) |

Worktree mode ships code that was never compiled, so when the user picks it, its DONE report must
lead with `/compile-check` after merging. Flag: `--mode worktree` (bash) / `-Mode Worktree`
(PowerShell).

## STEP 0.5 — Detect OS

Route on the environment platform:
- **macOS / Linux (`darwin`, `linux`)** → use `.claude/scripts/run-backlog-loop.sh` (STEP 1A).
- **Windows** → use the PowerShell runner (STEP 1B).

If unsure, prefer the `.sh` path on a `darwin`/`linux` host.

## STEP 1A — macOS / Linux

1. Get the absolute repo root dynamically (do not hardcode): the directory containing `.claude/`.
2. The `.sh` runner spawns one new Terminal window per task (via `osascript`) and waits for each to finish before spawning the next. You launch it once; it does the looping. Each task window is titled **`<projectName> - <task name>`** (project name from `.claude/project-profile.json`), so a stack of windows stays readable; with `--inline` the current window is retitled per task instead.
3. Run it in the background. The bare-run command line is exactly this — `--mode current` is the
   runner's own default, so it needs no flag:

   ```bash
   bash <REPO_ROOT>/.claude/scripts/run-backlog-loop.sh --auto-model-by-tier
   ```

   Append only the overrides STEP 0 resolved, e.g. `--mode worktree`, `--max-iterations 5`,
   `--inline`, or `--model <id> --effort <level>` (which replaces `--auto-model-by-tier`).
4. The runner pauses on its own when the backlog is empty (`PAUSED` sentinel) or stops on a blocker (`COMPILE_BLOCKED` / `PREFLIGHT_BLOCKED` / `REVIEW_BLOCKED` / `VERIFY_BLOCKED`). Logs land in `logs/backlog-loop/`.
5. Notify the user that the loop is running, in which mode, which model map is in effect, and where the logs are.

> Double-clicking `.claude/scripts/run-backlog-loop.command` in Finder also starts the loop with sensible defaults.
>
> Granting **Automation permission to Terminal** is required the first time so `osascript` can open task windows. If that is denied, use `--inline`.
>
> First-time setup on a fresh clone: run `bash .claude/scripts/sync-to-agents.sh` once so Claude Code sees the project skills/agents/commands (the `.claude/` link views are gitignored).

## STEP 1B — Windows (fallback)

1. Identify the absolute workspace path dynamically.
2. Use the PowerShell tool to spawn a new detached window (no `-Verb RunAs`). Call
   **`run-backlog-loop.ps1`**, not the `.bat` — the `.bat` prompts interactively for provider and
   mode, which defeats the no-questions default:
   ```powershell
   Start-Process powershell -ArgumentList "-NoExit", "-Command", "& { Set-Location '<WorkspacePath>'; & '<WorkspacePath>\.claude\scripts\run-backlog-loop.ps1' -Mode Current -AutoModelByTier }"
   ```
   Append only the overrides STEP 0 resolved (`-Mode Worktree`, `-MaxIterations <n>`,
   `-Model <id>`, `-NoSkipPermissions`). Use `run-backlog-loop.bat` **only** when the user
   explicitly asks to pick provider/mode interactively.
3. The new window runs independently — do NOT wait for it to finish.
4. Each per-task console window is titled **`<projectName> - <task name>`** (same wording as the `.sh` path), so a stack of task windows stays readable.
5. Notify the user that the loop is running in the background, in which mode and with which model map.
