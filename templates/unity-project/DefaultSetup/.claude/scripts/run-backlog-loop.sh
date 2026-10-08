#!/usr/bin/env bash
# Run Backlog Loop (macOS/Linux), per-task new Terminal window.
#
# This is the macOS/Linux twin of run-backlog-loop.ps1 (Windows). It is the
# CONTROLLER: for each iteration it spawns a SEPARATE Terminal window that runs
# exactly one /run-backlog task, then waits (via a flag file) for that window to
# finish before spawning the next. Each task window is titled
# "<projectName> - <task name>" so a stack of them stays readable. A failed task keeps its window open so you can
# read the error.
#
# SELF-HEAL (default): an iteration that ends without finishing its task — a
# blocker sentinel, "manual intervention required", a crash, a watchdog kill, a
# silent end — does NOT stop the loop. The next iteration resumes the same task in
# RECOVERY mode (run-backlog SKILL.md 1f: fresh context, a brief of what failed,
# --recovery-model, a fresh fix budget). After --max-recoveries the task is PARKED
# (partial work saved to refs/backlog/parked/<NNN> and taken out of the tree, task
# moved to the tail of TODO) and the loop carries on. A usage/session limit is slept
# out until its reset; a DONE task missing reviewer receipts gets a post-hoc audit
# iteration. The loop stops only when the backlog is empty, nothing but parked tasks
# remain, credentials are bad, a limit wait would exceed --max-usage-wait-minutes,
# or MaxIterations is reached. --no-self-heal restores stop-on-first-block.
#
# The run-backlog skill commits each done task to the work branch, and pushes it when
# the repo has an `origin` remote (a project freshly generated from the base template
# has none yet — the skill detects that and keeps the commit local). It does NOT
# create a PR — the user merges that branch -> the base manually (base branch = the
# branch captured when this loop process starts) after running the manual verify
# steps in the DONE summary.
#
# Usage:
#   .claude/scripts/run-backlog-loop.sh
#   .claude/scripts/run-backlog-loop.sh --model opus --effort xhigh --max-iterations 5
#   .claude/scripts/run-backlog-loop.sh --auto-model-by-tier --max-iterations 5
#   .claude/scripts/run-backlog-loop.sh --inline        # run in THIS window (no new windows)
#
# Options:
#   --model <id>             Claude model id (default: empty = CLI default).
#   --effort <level>         Reasoning effort: low|medium|high|xhigh (default: empty = CLI default).
#   --auto-model-by-tier     Pick model/effort per iteration from the BACKLOG.md task tier.
#   --xs-model/--xs-effort   Override XS profile (default: opus/medium).
#   --s-model/--s-effort     Override S profile  (default: opus/high).
#   --m-model/--m-effort     Override M profile  (default: opus/high).
#   --l-model/--l-effort     Override L profile  (default: opus/xhigh).
#                            (quality-first default: every tier runs on opus to match the
#                             opus reviewers; sonnet is no longer used anywhere. Escalating
#                             L to fable is opt-in per run: --l-model fable.
#                             A recovery iteration escalates to --recovery-model.)
#   --max-iterations <n>     Max task iterations (default: 100).
#   --max-checkpoints <n>    Max CONSECUTIVE iterations of one task that end with
#                            TASK_CHECKPOINTED (multi-iteration task, resumed by the next
#                            iteration) before the task is parked — or, with
#                            --no-self-heal, the loop stops with CHECKPOINT_LIMIT (default: 6).
#   --max-recoveries <n>     Recovery iterations per task before it is parked (default: 3).
#   --no-self-heal           Stop on the first block / failure (the pre-self-heal behaviour).
#   --recovery-model <id>    Model for recovery + audit iterations when --auto-model-by-tier
#   --recovery-effort <lvl>  is on (default: opus / xhigh).
#   --max-usage-wait-minutes <n>  Longest consecutive wait for a usage/session limit reset
#                            or an API outage before stopping (default: 600).
#   --max-quota-retries <n>  Retries of the org-quota 403 ("organization has disabled Claude
#                            subscription access", oauth_not_allowed_for_organization) before
#                            stopping, backoff 30s doubling (default: 5).
#   --busy-wait-minutes <n>  How long to wait for another live session that owns the task
#                            (TASK_BUSY) before stopping (default: 180).
#   --max-editor-recoveries <n>  EDITOR_REQUIRED (current mode): Unity Editor (re)starts via
#                            restart-unity.sh per loop run (default: 2).
#   --thinking-tokens <n>    Legacy/global MAX_THINKING_TOKENS override (default: 10000; 0 = off).
#   --xs-thinking-tokens <n> Override XS thinking budget (default: 3000; 0 = off).
#   --s-thinking-tokens <n>  Override S thinking budget (default: 6000; 0 = off).
#   --m-thinking-tokens <n>  Override M thinking budget (default: 10000; 0 = off).
#   --l-thinking-tokens <n>  Override L thinking budget (default: 10000; 0 = off).
#   --inline                 Run each task in the current window instead of a new one.
#   --no-skip-permissions    Do NOT pass --dangerously-skip-permissions (will prompt).
#   --mode <current|worktree> Where the agent works (default: current).
#                            current  = THIS checkout, commits onto the branch already
#                                       checked out. One Unity Editor, so the compile
#                                       check and the runtime smoke gate keep working —
#                                       but do not edit files while the loop runs (the
#                                       agent stages with `git add -A`).
#                            worktree = a sibling `git worktree` on agent/dev-<base>, so
#                                       you keep working undisturbed. Costs BOTH Unity
#                                       gates: a worktree is a separate Unity project with
#                                       no .sln/.csproj (gitignored, Unity-generated) and
#                                       no Editor attached. Merge the branch and run
#                                       /compile-check yourself afterwards.
#   -h | --help              Show this help.

set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SELF_PATH="$SCRIPT_DIR/$(basename "${BASH_SOURCE[0]}")"   # absolute — survives the cd below
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$REPO_ROOT" || { echo "Cannot cd to repo root: $REPO_ROOT" >&2; exit 1; }

# Project-specific values come from project-profile.json so this controller is
# byte-identical across every project that ships the agent system (see
# project_profile.py). The second argument is the fallback used when python3 is
# unavailable — the loop must still start on a box without it, and these
# fallbacks match project_profile.DEFAULTS.
profile_get() {
  python3 "$SCRIPT_DIR/project_profile.py" "$1" 2>/dev/null || printf '%s' "$2"
}
PROJECT_NAME="$(profile_get projectName UnityProject)"
GIT_CFG_BASE_BRANCH="$(profile_get gitConfigPrefix agent).agentBaseBranch"
PROFILE_DEFAULT_BASE="$(profile_get defaultBaseBranch main)"

# --- Terminal window title -------------------------------------------------------
# A long loop stacks up one window per task, so the title is the only thing that
# tells them apart: "<project> - <task>". Twin of Format-WindowTitle /
# Set-WindowTitle in run-backlog-loop-core.ps1 — keep the wording identical so a
# Windows box and a Mac label their windows the same way.
#
# Two mechanisms, because they cover different terminals:
#   * OSC 0 escape — Terminal.app, iTerm2 and every Linux emulator honour it, and
#     it is the only one that works for --inline (no new window is opened) and on
#     Linux (no osascript there).
#   * AppleScript `custom title` — macOS only, set on the tab right after it is
#     spawned, and it survives a child process that prints its own OSC title.
WINDOW_TITLE_MAX=120

# Compose the title for task "$1" ("" = the controller itself). Control chars are
# stripped and the result truncated: a pasted multi-line backlog title would
# otherwise break the escape sequence, not just the tab label.
format_window_title() {
  local task title
  task="$(printf '%s' "${1:-}" | tr '\r\n\t' '   ' | tr -s ' ')"
  task="${task#"${task%%[![:space:]]*}"}"
  task="${task%"${task##*[![:space:]]}"}"
  if [ -n "$task" ]; then
    title="$PROJECT_NAME - $task"
  else
    title="$PROJECT_NAME - Backlog Loop"
  fi
  if [ "${#title}" -gt "$WINDOW_TITLE_MAX" ]; then
    title="${title:0:$((WINDOW_TITLE_MAX - 3))}..."
  fi
  printf '%s' "$title"
}

# Retitle THIS window (controller window, and every task in --inline mode).
set_window_title() {
  [ -t 1 ] || return 0
  printf '\033]0;%s\007' "$1"
}

# Escape for an AppleScript double-quoted literal: a task title with a quote or a
# backslash would otherwise turn `osascript` into a syntax error and kill the spawn.
applescript_quote() {
  printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g'
}

# The backlog lives in the git COMMON dir (.git/backlog/), never in the tree: it
# is per-developer bookkeeping, so tracking it made every dev branch carry its
# own index and collide on merge. --git-common-dir (NOT --git-dir) is what makes
# one queue visible from every linked worktree of the clone. Git prints it
# relative to the cwd, so resolve it while still at the repo root.
GIT_COMMON_DIR="$(git rev-parse --git-common-dir 2>/dev/null || echo .git)"
GIT_COMMON_DIR="$(cd "$GIT_COMMON_DIR" 2>/dev/null && pwd || echo "$REPO_ROOT/.git")"
BACKLOG_ROOT="$GIT_COMMON_DIR/backlog"
BACKLOG_INDEX="$BACKLOG_ROOT/BACKLOG.md"
export AGENT_BACKLOG_ROOT="$BACKLOG_ROOT"

# Capture the base exactly once, before iteration 1 can checkout an agent branch.
# Every child task process receives this immutable loop-start value; a stale
# repo-local git config from an older loop must never retarget a new run.
LOOP_BASE_BRANCH="$(git rev-parse --abbrev-ref HEAD 2>/dev/null || true)"
# Starting the loop from an agent branch is allowed (a previous run leaves HEAD
# there): resolve the real base from the recorded git config, then the repo
# default, instead of hard-failing. The loop still merges the BASE into the agent
# branch, never the agent branch into itself.
case "$LOOP_BASE_BRANCH" in
  ""|HEAD|agent/dev|agent/dev-*)
    LOOP_BASE_BRANCH="$(git config "$GIT_CFG_BASE_BRANCH" 2>/dev/null || true)"
    case "$LOOP_BASE_BRANCH" in
      ""|agent/dev|agent/dev-*) LOOP_BASE_BRANCH="$PROFILE_DEFAULT_BASE" ;;
    esac
    echo "HEAD is an agent branch or detached — base branch resolved to '$LOOP_BASE_BRANCH' (git config / repo default)."
    ;;
esac
export AGENT_BASE_BRANCH="$LOOP_BASE_BRANCH"

usage() {
  # Print the leading comment header (lines after the shebang, up to the first blank line).
  sed -n '2,/^$/p' "$SELF_PATH" | sed 's/^# \{0,1\}//'
}

# --- defaults -------------------------------------------------------------------
MODEL=""
EFFORT=""
AUTO_MODEL_BY_TIER=0
XS_MODEL="opus"
XS_EFFORT="medium"
S_MODEL="opus"
S_EFFORT="high"
M_MODEL="opus"
M_EFFORT="high"
L_MODEL="opus"
L_EFFORT="xhigh"
MAX_ITERATIONS=100
# Consecutive transient API blips (transport break / 529) tolerated before the loop
# waits the outage out (backoff 30s doubling, capped at 15 min). Auth failures stop
# at once; an exhausted usage/session limit is slept out - see claude_failure_kind().
MAX_TRANSIENT_API_RETRIES=6
TRANSIENT_API_RETRIES=0
# The org-quota 403 ("Your organization has disabled Claude subscription access",
# oauth_not_allowed_for_organization) is NOT bad credentials: the org's shared quota
# ran dry and access flaps back within a minute or two — every one seen in
# logs/backlog-loop/ cleared on a 30-60s retry. Retried with its own consecutive
# budget (backoff 30s doubling); still refused after that = really disabled -> stop.
MAX_QUOTA_RETRIES=5
QUOTA_RETRIES=0
# Self-heal (see the header). bash 3.2 (stock macOS) has no associative arrays, so
# the per-task maps are newline-separated "key<TAB>value" strings (kv_get / kv_set).
SELF_HEAL=1
MAX_RECOVERIES=3
RECOVERY_MODEL="opus"
RECOVERY_EFFORT="xhigh"
MAX_USAGE_WAIT_MINUTES=600
BUSY_WAIT_MINUTES=180
MAX_EDITOR_RECOVERIES=2
RECOVERY_COUNTS=""        # task file -> recovery iterations started
PARKED_TASKS=""           # task file -> why it was parked
PENDING_RECOVERY_TASK=""  # the recovery the NEXT iteration runs
PENDING_RECOVERY_NNN=""
PENDING_RECOVERY_EVENT=""
PENDING_RECOVERY_BRIEF=""
PENDING_RECOVERY_ATTEMPT=0
PENDING_AUDIT_TASK=""     # a post-hoc gate audit the NEXT iteration runs
PENDING_AUDIT_NNN=""
PENDING_AUDIT_MISSING=""
PENDING_AUDIT_COMMIT=""
EDITOR_RECOVERIES=0
USAGE_WAITED_SEC=0        # consecutive time spent waiting out limits
RECOVERED_TASKS=0
# Per-iteration watchdog: no log growth for TASK_INACTIVITY_TIMEOUT_SEC, or a total
# run past TASK_HARD_TIMEOUT_SEC, kills the task window. The iteration is told its
# deadline (AGENT_ITERATION_DEADLINE = start + hard cap - margin) so a long task can
# checkpoint cleanly (TASK_CHECKPOINTED) before the kill instead of dying mid-step.
TASK_INACTIVITY_TIMEOUT_SEC=900
TASK_HARD_TIMEOUT_SEC=10800
CHECKPOINT_MARGIN_SEC=1200
# Consecutive TASK_CHECKPOINTED iterations of the SAME task tolerated before the
# loop stops for a human look (a task that never converges must not loop forever).
MAX_CHECKPOINTS=6
CHECKPOINT_STREAK=0
CHECKPOINT_TASK=""
# A clean exit with no sentinel while the task is still in progress (the model ended
# its turn mid-task, e.g. "waiting for the recompile") gets ONE automatic resume per
# task — the run journal makes the retry pick up the partial work. A second silent
# end of the same task stops the loop (SILENT_FAIL).
SILENT_RETRY_TASK=""
# Loop-wide token/cost running totals, advanced by collect_iteration_report() after
# every iteration that produced a parsable log — completed and blocked alike, since
# both burned tokens.
LOOP_ITERS_COUNTED=0
LOOP_TOKENS_TOTAL=0
LOOP_COST_TOTAL=0
# Per-iteration notification payload, filled by collect_iteration_report().
REPORT_SUMMARY=""
REPORT_PER_MODEL=""
REPORT_BREAKDOWN=""
REPORT_CUMULATIVE=""
THINKING_TOKENS=10000
XS_THINKING_TOKENS=3000
S_THINKING_TOKENS=6000
M_THINKING_TOKENS=10000
L_THINKING_TOKENS=10000
SKIP_PERMISSIONS=1
INLINE=0
MODE="current"
LOG_DIR="logs/backlog-loop"

# --- parse args -----------------------------------------------------------------
while [ $# -gt 0 ]; do
  case "$1" in
    --model)            MODEL="${2:-}"; shift 2 ;;
    --effort)           EFFORT="${2:-}"; shift 2 ;;
    --auto-model-by-tier) AUTO_MODEL_BY_TIER=1; shift ;;
    --xs-model)         XS_MODEL="${2:-}"; shift 2 ;;
    --xs-effort)        XS_EFFORT="${2:-}"; shift 2 ;;
    --s-model)          S_MODEL="${2:-}"; shift 2 ;;
    --s-effort)         S_EFFORT="${2:-}"; shift 2 ;;
    --m-model)          M_MODEL="${2:-}"; shift 2 ;;
    --m-effort)         M_EFFORT="${2:-}"; shift 2 ;;
    --l-model)          L_MODEL="${2:-}"; shift 2 ;;
    --l-effort)         L_EFFORT="${2:-}"; shift 2 ;;
    --max-iterations)   MAX_ITERATIONS="${2:-}"; shift 2 ;;
    --max-checkpoints)  MAX_CHECKPOINTS="${2:-}"; shift 2 ;;
    --max-recoveries)   MAX_RECOVERIES="${2:-}"; shift 2 ;;
    --no-self-heal)     SELF_HEAL=0; shift ;;
    --recovery-model)   RECOVERY_MODEL="${2:-}"; shift 2 ;;
    --recovery-effort)  RECOVERY_EFFORT="${2:-}"; shift 2 ;;
    --max-usage-wait-minutes) MAX_USAGE_WAIT_MINUTES="${2:-}"; shift 2 ;;
    --max-quota-retries) MAX_QUOTA_RETRIES="${2:-}"; shift 2 ;;
    --busy-wait-minutes) BUSY_WAIT_MINUTES="${2:-}"; shift 2 ;;
    --max-editor-recoveries) MAX_EDITOR_RECOVERIES="${2:-}"; shift 2 ;;
    --thinking-tokens)
      THINKING_TOKENS="${2:-}"
      XS_THINKING_TOKENS="$THINKING_TOKENS"
      S_THINKING_TOKENS="$THINKING_TOKENS"
      M_THINKING_TOKENS="$THINKING_TOKENS"
      L_THINKING_TOKENS="$THINKING_TOKENS"
      shift 2 ;;
    --xs-thinking-tokens|--xs-thinking) XS_THINKING_TOKENS="${2:-}"; shift 2 ;;
    --s-thinking-tokens|--s-thinking)   S_THINKING_TOKENS="${2:-}"; shift 2 ;;
    --m-thinking-tokens|--m-thinking)   M_THINKING_TOKENS="${2:-}"; shift 2 ;;
    --l-thinking-tokens|--l-thinking)   L_THINKING_TOKENS="${2:-}"; shift 2 ;;
    --inline)           INLINE=1; shift ;;
    --no-skip-permissions) SKIP_PERMISSIONS=0; shift ;;
    --mode)
      MODE="$(printf '%s' "${2:-}" | tr '[:upper:]' '[:lower:]')"
      case "$MODE" in
        current|worktree) ;;
        *) echo "Unknown --mode: ${2:-} (expected current|worktree)" >&2; exit 2 ;;
      esac
      shift 2 ;;
    -h|--help)          usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

command -v claude >/dev/null 2>&1 || { echo "ERROR: 'claude' CLI not found in PATH." >&2; exit 1; }

# --- work branch + work dir (depends on --mode, so resolved after parsing) -------
# current:  commit onto the branch already checked out. A separate agent branch
#           buys nothing (same directory either way) and every checkout makes the
#           dev's open Unity Editor reimport.
# worktree: a dedicated agent branch is MANDATORY, not a convention — git refuses
#           to check out one branch in two worktrees. Slashes in the base MUST be
#           flattened: refs are files, so `Dev1` and `Dev1/agent/dev` cannot
#           coexist ("cannot lock ref ... 'refs/heads/Dev1' exists").
if [ "$MODE" = "worktree" ]; then
  AGENT_BRANCH="agent/dev-$(printf '%s' "$LOOP_BASE_BRANCH" | tr '/' '-')"
else
  AGENT_BRANCH="$LOOP_BASE_BRANCH"
fi
# The child task process reads these. AGENT_BRANCH is already set just above —
# it used to be re-exported under a project-prefixed second name, which is why
# the plain export reads a little bare here.
export AGENT_BRANCH
export AGENT_MODE="$MODE"

WORK_DIR="$REPO_ROOT"
if [ "$MODE" = "worktree" ]; then
  # Created ONCE and kept: a worktree is a full second Unity project, so tearing
  # it down each run would re-import Library/ from scratch every time.
  WT_PATH="$(dirname "$REPO_ROOT")/$(basename "$REPO_ROOT")-agent-$(printf '%s' "$LOOP_BASE_BRANCH" | tr '/' '-')"
  if [ -d "$WT_PATH" ]; then
    echo "Worktree:    reusing $WT_PATH"
  else
    if git rev-parse --verify --quiet "refs/heads/$AGENT_BRANCH" >/dev/null 2>&1; then
      git worktree add "$WT_PATH" "$AGENT_BRANCH"
    else
      git worktree add -b "$AGENT_BRANCH" "$WT_PATH" "$LOOP_BASE_BRANCH"
    fi
    if [ ! -d "$WT_PATH" ]; then
      echo "WORKTREE_FAILED — could not create $WT_PATH. Re-run with --mode current, or clear a stale entry with 'git worktree prune'." >&2
      exit 1
    fi
    echo "Worktree:    created $WT_PATH on $AGENT_BRANCH"
  fi
  WORK_DIR="$(cd "$WT_PATH" && pwd)"
  echo "WARNING: worktree mode has NO compile check and NO runtime smoke — the worktree"
  echo "         is a separate Unity project with no .sln/.csproj and no Editor."
  echo "         Merge $AGENT_BRANCH into $LOOP_BASE_BRANCH and run /compile-check FIRST."
else
  # Current mode shares the checkout with the developer. The agent stages with
  # `git add -A` for review, so anything uncommitted right now lands in the first task's
  # review diff; the commit itself (STEP 9, push-in-session style) takes only the task's
  # own files — unless the agent edits a file the dev already had dirty.
  # Warn, do not block: owning that risk is the point of choosing current.
  DIRTY_COUNT="$(git status --porcelain 2>/dev/null | grep -c . || true)"
  if [ "${DIRTY_COUNT:-0}" -gt 0 ]; then
    echo "WARNING: $DIRTY_COUNT uncommitted change(s) in this checkout. They will show up in the"
    echo "         first task's review diff (the commit takes only the task's own files, but"
    echo "         a file both of you touched is committed whole)."
    echo "         Commit or stash them first if that is not what you want."
  fi
fi
export AGENT_WORKDIR="$WORK_DIR"
# backlog-ops.py prints task titles (often Vietnamese): keep every Python this loop
# starts in UTF-8 mode so a non-UTF-8 locale can never crash it mid-transition.
export PYTHONUTF8=1
echo "Mode:        $MODE (work branch: $AGENT_BRANCH)"
echo "Backlog:     $BACKLOG_ROOT"

mkdir -p "$LOG_DIR"
LOG_DIR_ABS="$(cd "$LOG_DIR" && pwd)"

# Optional pretty renderer for the stream-json firehose (raw JSON still goes to the log).
RENDER="$SCRIPT_DIR/stream-render.py"
if command -v python3 >/dev/null 2>&1 && [ -f "$RENDER" ]; then HAS_RENDER=1; else HAS_RENDER=0; fi

# Optional Discord notifier — only fires if .env configures a bot token (see discord-send.sh).
NOTIFY="$SCRIPT_DIR/notify.sh"
notify() {
  [ -f "$NOTIFY" ] || return 0
  bash "$NOTIFY" "$@" >/dev/null 2>&1 || true
}

# --- loop lease: ONE consumer of this clone's backlog ----------------------------
# Every loop controller and every hand-run /run-backlog on this clone read the SAME
# queue (.git/backlog is shared by all worktrees), and `pick` hands every caller the
# IN PROGRESS head — so a second loop used to silently "resume" the task this one was
# still working on (two agents, one Editor, one git index). backlog-ops.py keys the
# lease to this process (pid + start time, so a crash or kill -9 frees it on its
# own) and refuses while another loop, a pre-lease loop, or a live task session
# (e.g. the window of a killed controller) is still at work. Each iteration proves
# it belongs to this loop with BACKLOG_LOOP_TOKEN; a hand-run /run-backlog without
# it gets LOOP_BUSY instead of a task.
OPS="$SCRIPT_DIR/backlog-ops.py"
command -v python3 >/dev/null 2>&1 || {
  echo "ERROR: python3 is required — backlog-ops.py holds the loop lease and every task transition." >&2
  exit 1
}
json_field() {  # json_field '<json>' <key> -> value ("" when absent / unparsable)
  python3 -c 'import json,sys
try: v = json.loads(sys.argv[1]).get(sys.argv[2])
except Exception: v = None
print("" if v is None else v)' "$1" "$2" 2>/dev/null
}
LEASE_JSON="$(cd "$WORK_DIR" && python3 "$OPS" lock acquire --pid $$ --mode "$MODE" \
  --work-dir "$WORK_DIR" --branch "$AGENT_BRANCH" --log-dir "$LOG_DIR_ABS")"
LEASE_RC=$?
case "$LEASE_RC" in
  0) ;;
  3) echo "ERROR: backlog not initialised — run: python3 $OPS init" >&2; exit 1 ;;
  4)
    echo "LOOP_BUSY — another consumer of this backlog is still running; refusing to start:" >&2
    printf '%s\n' "$LEASE_JSON" >&2
    echo "Stop it (or let it finish). Status: python3 $OPS lock status" >&2
    notify --event "LOOP_BUSY" --task "N/A" \
      --details "Loop not started: $(json_field "$LEASE_JSON" reason) — $(json_field "$LEASE_JSON" hint)"
    exit 1 ;;
  *)
    echo "ERROR: could not take the loop lease (exit $LEASE_RC):" >&2
    printf '%s\n' "$LEASE_JSON" >&2
    exit 1 ;;
esac
BACKLOG_LOOP_TOKEN="$(json_field "$LEASE_JSON" token)"
[ -n "$BACKLOG_LOOP_TOKEN" ] || { echo "ERROR: lease acquired without a token: $LEASE_JSON" >&2; exit 1; }
export BACKLOG_LOOP_TOKEN
release_lease() {
  python3 "$OPS" lock release --token "$BACKLOG_LOOP_TOKEN" >/dev/null 2>&1 || true
}
trap release_lease EXIT
# A task window that is still running keeps its own claim (its claude process owns
# it), so stopping the controller never lets a new loop start on top of that task.
trap 'echo; echo "Interrupted — loop lease released. A task window still running finishes its task and keeps its claim until then."; exit 130' INT TERM HUP
echo "Lease:       held (pid $$) — a second loop or a hand-run /run-backlog on this clone gets LOOP_BUSY"

# --- per-task prompt ------------------------------------------------------------
read -r -d '' PROMPT <<EOF
Execute exactly one iteration of this project's run-backlog workflow.

Required contract:
1. Read .claude/skills/run-backlog/SKILL.md before changing any files.
2. Follow that skill exactly for one iteration only.
3. Read CLAUDE.md, .claude/rules/*, the selected task file, and only the relevant code the workflow requests.
4. Spawn the code-reviewer, performance-reviewer (when perf-sensitive), security-auditor (when sensitive), and qa-verifier subagents per the skill spec using the Agent tool.
5. Print exactly these tokens when blocked: COMPILE_BLOCKED, PREFLIGHT_BLOCKED, REVIEW_BLOCKED, VERIFY_BLOCKED, RUNTIME_BLOCKED, EDITOR_REQUIRED, NO_CHANGES, BASE_MERGE_CONFLICT, LOOP_BUSY, TASK_BUSY, RESUME_CONFLICT, or "manual intervention required". (DEFERRED is NOT a block — end the iteration normally. Starting on an agent branch is allowed — never print BASE_UNKNOWN.) A task that legitimately needs more than one iteration ends with TASK_CHECKPOINTED instead (skill section 1e) — that is NOT a block, the next iteration resumes it; never print it together with a block token.
6. Commit to the work branch (env AGENT_BRANCH) only when the skill marks the task DONE, exactly as its STEP 9 says (push-in-session style: reset the index, stage ONLY this task's files, message \`<prefix> Tag: <subject>\`, no Co-Authored-By or other trailer), and push it only when the repo has an origin remote (the skill's HAS_REMOTE probe decides). Do not create a PR.

Environment for this iteration (STEP 2 of the skill reads these):
- AGENT_MODE=$MODE
- AGENT_BRANCH=$AGENT_BRANCH
- AGENT_BASE_BRANCH=$LOOP_BASE_BRANCH
- AGENT_BACKLOG_ROOT=$BACKLOG_ROOT
- AGENT_ITERATION_DEADLINE=__ITER_DEADLINE__ (epoch seconds; this iteration is killed ${CHECKPOINT_MARGIN_SEC}s after it — wrap up and checkpoint before it, see skill section 1e)
7. Do not ask for confirmation. Work autonomously inside this repository. Never end the iteration to ask a question or wait for a decision: apply the skill's "Autonomous decision policy" (Notes for orchestrator), record the choice, and keep going.
8. Use English for all output, progress messages, reports, and commit messages.
9. Never end your turn to wait for anything (a background Bash job, Monitor, an Editor recompile/import, a bundle build, a subagent). This is a non-interactive session: the end of your turn ends the iteration and strands the task in progress. Wait in the foreground instead (Bash 'sleep <=10' between 'unity_editor_state' / file polls, Agent calls with run_in_background false). Your turn ends only with the STEP 10 report or a stop token.
__RECOVERY__
Start now.
EOF

# Returns 0 (blocked) only if the FINAL {"type":"result"} event's line contains a
# blocker sentinel. Grepping the whole log false-positives because the prompt echoes
# sentinel names in the conversation JSON. Token list mirrors the "Hard stop
# conditions" of run-backlog/SKILL.md — keep the two in lockstep.
is_blocked() {
  local log="$1" result_line
  [ -f "$log" ] || return 1
  result_line="$(grep '"type":"result"' "$log" | tail -n1)"
  [ -n "$result_line" ] || return 1
  printf '%s' "$result_line" | grep -Eq 'COMPILE_BLOCKED|PREFLIGHT_BLOCKED|REVIEW_BLOCKED|VERIFY_BLOCKED|RUNTIME_BLOCKED|EDITOR_REQUIRED|NO_CHANGES|BASE_UNKNOWN|BASE_MERGE_CONFLICT|LOOP_BUSY|TASK_BUSY|RESUME_CONFLICT' \
    || printf '%s' "$result_line" | grep -iq 'manual intervention required'   # any case, as Test-Blocked's -match
}

# Returns 0 when the FINAL result event ends a multi-iteration task with saved
# progress (run-backlog SKILL.md 1e). Checked only AFTER is_blocked, so a block
# token in the same report always wins. Keep in lockstep with Test-Checkpointed
# in run-backlog-loop-core.ps1.
is_checkpointed() {
  local log="$1" result_line
  [ -f "$log" ] || return 1
  result_line="$(grep '"type":"result"' "$log" | tail -n1)"
  [ -n "$result_line" ] || return 1
  printf '%s' "$result_line" | grep -q 'TASK_CHECKPOINTED'
}

QUOTA_403_PATTERN='organization has disabled|oauth_not_allowed_for_organization|oauth_org_not_allowed'

# Classify a non-zero claude iteration: transient (retry) vs fatal (stop now).
# Grounded in the failure classes actually seen in logs/backlog-loop/:
#
#   terminal_reason=api_error  "Connection closed mid-response"   transport break -> retry
#   result="API Error: Overloaded"                                529             -> retry
#   result="Failed to authenticate. API Error: 401 ..."                           -> STOP
#   result="You're out of extra usage - resets <time>"                            -> STOP
#   result="Your organization has disabled Claude subscription access ..." (403
#          oauth_not_allowed_for_organization) org quota ran dry -> retry (own budget)
#
# Retrying an exhausted quota burns what is left against a wall, and retrying bad
# credentials cannot fix them, so the fatal classes are matched FIRST (they can
# still carry an api_error terminal_reason). Prints the class reason on stdout and
# returns 0 only when the failure is transient. Keep in lockstep with
# Get-ClaudeFailureClass in run-backlog-loop-core.ps1.
# Pattern matches are case-insensitive, like PowerShell's -match in the .ps1 twin
# ("Session limit reached" and "session limit" are the same wall).
claude_failure_class() {
  local log="$1" result_line
  [ -f "$log" ] || { printf 'unclassified non-zero exit'; return 1; }
  result_line="$(grep '"type":"result"' "$log" | tail -n1)"
  [ -n "$result_line" ] || { printf 'unclassified non-zero exit'; return 1; }

  if printf '%s' "$result_line" | grep -Eiq "out of extra usage|usage limit|session limit|credit balance|Insufficient credit"; then
    printf 'usage/credit exhausted - retrying would burn quota against a wall'; return 1
  fi
  if printf '%s' "$result_line" | grep -Eiq "$QUOTA_403_PATTERN"; then
    printf 'org quota exhausted - organization has disabled Claude subscription access (403)'; return 1
  fi
  if printf '%s' "$result_line" | grep -Eiq 'Invalid authentication credentials|Failed to authenticate'; then
    printf 'authentication failure - a retry cannot fix credentials'; return 1
  fi
  if printf '%s' "$result_line" | grep -Eq '"terminal_reason":"api_error"'; then
    printf 'API/transport error'; return 0
  fi
  if printf '%s' "$result_line" | grep -Eiq 'Overloaded|overloaded_error'; then
    printf 'API overloaded (529)'; return 0
  fi
  printf 'unclassified non-zero exit'; return 1
}

# usage | quota | auth | transient | unknown — the same classes as claude_failure_class,
# as a word (that function runs in a $(...) subshell, so it cannot set a variable).
claude_failure_kind() {
  local log="$1" result_line
  result_line="$( [ -f "$log" ] && grep '"type":"result"' "$log" | tail -n1 )"
  if [ -z "$result_line" ]; then printf 'unknown'; return; fi
  if printf '%s' "$result_line" | grep -Eiq "out of extra usage|usage limit|session limit|credit balance|Insufficient credit"; then printf 'usage'; return; fi
  if printf '%s' "$result_line" | grep -Eiq "$QUOTA_403_PATTERN"; then printf 'quota'; return; fi
  if printf '%s' "$result_line" | grep -Eiq 'Invalid authentication credentials|Failed to authenticate'; then printf 'auth'; return; fi
  if printf '%s' "$result_line" | grep -Eiq '"terminal_reason":"api_error"|Overloaded|overloaded_error'; then printf 'transient'; return; fi
  printf 'unknown'
}

# --- self-heal helpers (keep in lockstep with the self-heal section of
# run-backlog-loop-core.ps1) ------------------------------------------------------
kv_get() {  # kv_get "<map>" <key> -> value ("" when absent)
  printf '%s\n' "$1" | awk -F'\t' -v k="$2" '$1 == k { v = $2 } END { print v }'
}
kv_set() {  # kv_set "<map>" <key> <value> -> new map
  printf '%s\n' "$1" | awk -F'\t' -v k="$2" '$1 != "" && $1 != k'
  printf '%s\t%s\n' "$2" "$3"
}

# Final result text of an iteration log ("" when the run never got that far).
last_result_text() {
  [ -f "$1" ] || return 0
  python3 - "$1" <<'PY' 2>/dev/null
import json, re, sys
text = ""
with open(sys.argv[1], encoding="utf-8", errors="replace") as fh:
    for line in fh:
        if re.match(r'\s*\{\s*"type"\s*:\s*"result"', line):
            try:
                text = str(json.loads(line).get("result") or "")
            except ValueError:
                pass
print(text)
PY
}

# The orchestrator's last few text messages — what it was doing when it died
# without a result event.
last_assistant_text() {
  [ -f "$1" ] || return 0
  python3 - "$1" <<'PY' 2>/dev/null
import json, re, sys
texts = []
with open(sys.argv[1], encoding="utf-8", errors="replace") as fh:
    for line in fh:
        if not re.match(r'\s*\{\s*"type"\s*:\s*"assistant"', line) or not re.search(r'"type"\s*:\s*"text"', line):
            continue
        try:
            obj = json.loads(line)
        except ValueError:
            continue
        if obj.get("parent_tool_use_id"):
            continue
        for c in (obj.get("message") or {}).get("content") or []:
            if c.get("type") == "text" and c.get("text"):
                texts.append(c["text"])
print("\n---\n".join(texts[-3:]))
PY
}

# Seconds until the exhausted limit resets: the CLI's own rate_limit_event (exact),
# else "resets 7:40pm" / "resets Oct 9, 3am" in the result text (local clock), else -1.
# Includes 2 min of slack.
limit_reset_delay() {
  python3 - "$1" <<'PY' 2>/dev/null || echo -1
import json, re, sys, time
from datetime import datetime, timedelta
log = sys.argv[1]
best, result = 0, ""
try:
    with open(log, encoding="utf-8", errors="replace") as fh:
        for line in fh:
            if '"type":"rate_limit_event"' in line:
                try:
                    info = json.loads(line).get("rate_limit_info") or {}
                except ValueError:
                    continue
                if info.get("status") != "allowed" and info.get("resetsAt"):
                    best = max(best, int(info["resetsAt"]))
            elif re.match(r'\s*\{\s*"type"\s*:\s*"result"', line):
                try:
                    result = str(json.loads(line).get("result") or "")
                except ValueError:
                    pass
except OSError:
    pass
if best > time.time():
    print(int(best - time.time()) + 120)
    sys.exit(0)
m = re.search(r"(?i)resets\s+(?:at\s+)?(?:(?P<mon>[a-z]{3,9})\.?\s+(?P<day>\d{1,2})(?:st|nd|rd|th)?,?\s+(?:at\s+)?)?"
              r"(?P<h>\d{1,2})(?::(?P<min>\d{2}))?\s*(?P<ap>am|pm)", result)
if not m:
    print(-1)
    sys.exit(0)
hour = int(m.group("h")) % 12 + (12 if m.group("ap").lower() == "pm" else 0)
minute = int(m.group("min") or 0)
now = datetime.now()
target = now.replace(hour=hour, minute=minute, second=0, microsecond=0)
if m.group("mon"):
    try:
        month = datetime.strptime(m.group("mon")[:3].title(), "%b").month
        target = target.replace(month=month, day=int(m.group("day")))
        if target < now - timedelta(days=1):
            target = target.replace(year=target.year + 1)
    except ValueError:
        pass
elif target <= now:
    target += timedelta(days=1)
print(int((target - now).total_seconds()) + 120)
PY
}

# Sleep out a usage/session limit (or a long API outage). Returns 1 when the wait
# would push the consecutive total past --max-usage-wait-minutes (caller stops).
wait_for_limit_reset() {  # <log> <fixed-delay-sec or 0> <why>
  local log="$1" delay="$2" why="$3" until_at
  [ "$delay" -gt 0 ] 2>/dev/null || delay="$(limit_reset_delay "$log")"
  [ "${delay:--1}" -gt 0 ] 2>/dev/null || delay=1800
  [ $((USAGE_WAITED_SEC + delay)) -le $((MAX_USAGE_WAIT_MINUTES * 60)) ] || return 1
  USAGE_WAITED_SEC=$((USAGE_WAITED_SEC + delay))
  until_at="$(date -r $(( $(date +%s) + delay )) +%H:%M 2>/dev/null || date -d "@$(( $(date +%s) + delay ))" +%H:%M 2>/dev/null)"
  local msg="$why — sleeping $(( (delay + 59) / 60 )) min until ${until_at:-later}, then resuming the same task. The loop has NOT stopped."
  echo "  ⏸ $msg"
  notify --event "LIMIT_WAIT" --task "${TASK_TITLE_NOTIF:-N/A}" --details "$msg"
  sleep "$delay"
  return 0
}

# Write the recovery brief for <task> and print its path.
write_recovery_brief() {  # <task-file> <nnn> <event> <details> <log> <attempt>
  local task="$1" nnn="$2" event="$3" details="$4" log="$5" attempt="$6" brief why report source
  brief="$LOG_DIR_ABS/recovery-$(date +%Y%m%d-%H%M%S)-$nnn-$attempt.md"
  case "$event" in
    WATCHDOG_KILL)    why="The controller killed the iteration: its log stopped growing for $((TASK_INACTIVITY_TIMEOUT_SEC / 60)) min, or it ran past the $((TASK_HARD_TIMEOUT_SEC / 60)) min cap. Something hung (a background wait, a modal dialog, a long job). Work in short foreground steps and checkpoint (skill 1e) before AGENT_ITERATION_DEADLINE." ;;
    ITERATION_FAILED) why="The agent CLI exited non-zero without a usable result (crash, closed window, unclassified error)." ;;
    SILENT_END)       why="The iteration ended its turn without a STEP 10 report or a stop token while the task was still in progress — usually it ended the turn to wait for something in the background. Never do that: every wait is a foreground poll." ;;
    CHECKPOINT_LIMIT) why="The task checkpointed too many consecutive iterations without finishing." ;;
    *)                why="The iteration ended with the stop token $event." ;;
  esac
  report="$(last_result_text "$log")"
  source="final report (result event)"
  if [ -z "$report" ]; then
    report="$(last_assistant_text "$log")"
    source="last orchestrator messages (the iteration produced no final report)"
  fi
  report="$(printf '%s' "$report" | tail -c 12000)"
  {
    printf '# Recovery brief - task %s, attempt %s of %s\n\n' "$nnn" "$attempt" "$MAX_RECOVERIES"
    printf -- '- Task file: %s (in %s)\n' "$task" "$BACKLOG_ROOT"
    printf -- '- What happened: **%s** - %s\n' "$event" "$why"
    printf -- '- Details: %s\n' "$details"
    printf -- '- Failed iteration log (stream-json, large - grep it, do not read it whole): %s\n\n' "$log"
    printf '## Previous iteration - %s\n\n%s\n' "$source" "$report"
  } > "$brief"
  printf '%s' "$brief"
}

# Park a task: partial work saved to refs/backlog/parked/<NNN> and taken out of the
# tree, task moved to the tail of TODO. Returns 1 when backlog-ops refused.
park_task() {  # <nnn> <task-file> <reason> [--keep-work]
  local nnn="$1" task="$2" reason="$3" keep="${4:-}" out rc
  out="$(cd "$WORK_DIR" && python3 "$OPS" park "$nnn" --reason "$reason" $keep 2>&1)"
  rc=$?
  if [ "$rc" -ne 0 ]; then
    echo "  ⚠️ Park of task $nnn FAILED (exit $rc): $out" >&2
    return 1
  fi
  PARKED_TASKS="$(kv_set "$PARKED_TASKS" "$task" "$reason")"
  echo "  ⚠️ PARKED task $nnn ($task): $reason — partial work saved under refs/backlog/parked/$nnn (if any). Moving on to the next task."
  return 0
}

# Decide what a failed iteration turns into: SELF_HEAL_OUTCOME = recover | parked | stop
# (STOP_REASON set on stop). Sends the matching notification.
self_heal() {  # <task-file> <nnn> <event> <details> <log> [park-now] [--keep-work]
  local task="$1" nnn="$2" event="$3" details="$4" log="$5" park_now="${6:-}" keep="${7:-}" used attempt msg evt
  collect_iteration_report "$log"
  if [ -z "$task" ] || [ -z "$nnn" ]; then
    SELF_HEAL_OUTCOME="stop"
    STOP_REASON="$event on an unidentified task — cannot self-heal (see $log)"
  else
    used="$(kv_get "$RECOVERY_COUNTS" "$task")"; used="${used:-0}"
    if [ -z "$park_now" ] && [ "$used" -lt "$MAX_RECOVERIES" ]; then
      attempt=$((used + 1))
      RECOVERY_COUNTS="$(kv_set "$RECOVERY_COUNTS" "$task" "$attempt")"
      PENDING_RECOVERY_TASK="$task"; PENDING_RECOVERY_NNN="$nnn"; PENDING_RECOVERY_EVENT="$event"
      PENDING_RECOVERY_ATTEMPT="$attempt"
      PENDING_RECOVERY_BRIEF="$(write_recovery_brief "$task" "$nnn" "$event" "$details" "$log" "$attempt")"
      SELF_HEAL_OUTCOME="recover"
      echo "  ⚠️ SELF-HEAL: $event on task $nnn — recovery iteration $attempt/$MAX_RECOVERIES next (brief: $PENDING_RECOVERY_BRIEF)."
    else
      PENDING_RECOVERY_TASK=""
      local reason="$event"
      [ -z "$park_now" ] && reason="$event after $used recovery iteration(s)"
      if park_task "$nnn" "$task" "$reason" "$keep"; then
        SELF_HEAL_OUTCOME="parked"
      else
        SELF_HEAL_OUTCOME="stop"
        STOP_REASON="Task $nnn could not be parked after $event — the loop cannot move past it safely (see $log)"
      fi
    fi
  fi
  case "$SELF_HEAL_OUTCOME" in
    recover) evt="TASK_RECOVERING"; msg="$event — $details
Recovery iteration $PENDING_RECOVERY_ATTEMPT/$MAX_RECOVERIES starts next. The loop has NOT stopped." ;;
    parked)  evt="TASK_PARKED"; msg="$event — $details
Self-heal budget spent: task parked (partial work saved under refs/backlog/parked/$nnn, task at the tail of TODO). The loop continues with the next task." ;;
    *)       evt="$event"; msg="$STOP_REASON"; echo "  ⚠️ $STOP_REASON" >&2 ;;
  esac
  notify --event "$evt" --task "$TASK_TITLE_NOTIF" --url "$TASK_URL_NOTIF" \
    --tokens "$REPORT_SUMMARY" --per-model "$REPORT_PER_MODEL" \
    --breakdown "$REPORT_BREAKDOWN" --cumulative "$REPORT_CUMULATIVE" \
    --details "$msg" --progress "$TASK_PROGRESS_NOTIF" --duration "$ITER_DURATION"
}

# EDITOR_REQUIRED (current mode): start this project's Editor, or restart a hung one.
editor_recovery() {
  EDITOR_RECOVERIES=$((EDITOR_RECOVERIES + 1))
  [ "$EDITOR_RECOVERIES" -le "$MAX_EDITOR_RECOVERIES" ] || return 1
  # The /restart-unity skill ships its script under skills/; a copy next to this
  # file (older layout) is the fallback. Only the skill's script takes --project.
  local restart="$SCRIPT_DIR/../skills/restart-unity/scripts/restart-unity.sh"
  local restart_args=(--project "$REPO_ROOT")
  if [ ! -f "$restart" ]; then
    restart="$SCRIPT_DIR/restart-unity.sh"
    restart_args=()
  fi
  [ -f "$restart" ] || return 1
  if [ "$EDITOR_RECOVERIES" -eq 1 ] && pgrep -if -- "-projectpath $REPO_ROOT" >/dev/null 2>&1; then
    echo "  EDITOR_REQUIRED: this project's Editor is running but did not answer — giving it 5 min (import / compile / boot)."
  else
    echo "  EDITOR_REQUIRED: (re)starting this project's Editor via restart-unity.sh, then waiting 5 min for it to boot."
    bash "$restart" ${restart_args[@]+"${restart_args[@]}"} >/dev/null 2>&1 || true
  fi
  sleep 300
  [ -f "$BACKLOG_ROOT/state" ] && rm -f "$BACKLOG_ROOT/state"
  return 0
}

# TODO bullets not parked in this run.
unparked_todo_count() {
  local n=0 f
  [ -f "$BACKLOG_INDEX" ] || { echo 0; return; }
  for f in $(awk '/^## /{s=($0 ~ /^## TODO/)} s' "$BACKLOG_INDEX" | sed -nE 's/.*\]\(backlog\/todo\/([^)]+)\).*/\1/p'); do
    [ -n "$(kv_get "$PARKED_TASKS" "$f")" ] || n=$((n + 1))
  done
  echo "$n"
}

# The appendix that turns the run-backlog prompt into a recovery prompt.
recovery_appendix() {
  cat <<EOT

RECOVERY MODE - self-heal attempt $PENDING_RECOVERY_ATTEMPT of $MAX_RECOVERIES for task $PENDING_RECOVERY_NNN ($PENDING_RECOVERY_TASK).
The previous iteration on this task did not finish: $PENDING_RECOVERY_EVENT. The loop did NOT stop; this iteration must fix the cause and carry the task through to DONE.
- AGENT_RECOVERY_BRIEF=$PENDING_RECOVERY_BRIEF
- AGENT_RECOVERY_ATTEMPT=$PENDING_RECOVERY_ATTEMPT/$MAX_RECOVERIES
Read the brief FIRST, then follow run-backlog SKILL.md section 1f (Recovery iteration). Every gate gets a fresh fix budget in this iteration. Never weaken a gate to get past it.
EOT
}

# Post-hoc gate audit prompt: the task is DONE and committed, but the log shows no
# spawn of a reviewer its tier requires.
audit_prompt() {
  local types="" r
  for r in $PENDING_AUDIT_MISSING; do types="${types:+$types, }subagent_type \"$r\""; done
  cat <<EOT
Post-hoc quality gate for backlog task $PENDING_AUDIT_NNN. Do NOT run /run-backlog, and do NOT pick, start, resume or touch any other backlog task.

Task $PENDING_AUDIT_NNN reached DONE and was committed as $PENDING_AUDIT_COMMIT, but the iteration that did it never spawned the mandatory reviewer(s): $PENDING_AUDIT_MISSING. Run them now:
1. Read .claude/skills/run-backlog/SKILL.md (STEP 6d/6e for code-reviewer, STEP 7 for qa-verifier - the prompt shapes), CLAUDE.md, and the task's done file: $BACKLOG_ROOT/done/$PENDING_AUDIT_TASK
2. Write the commit's diff to a file: git show --format= $PENDING_AUDIT_COMMIT > .claude/tmp/backlog/audit-$PENDING_AUDIT_NNN.diff
3. Spawn exactly the missing reviewer(s) with the Agent tool ($types), in parallel, in the foreground. Pass the task spec from the done file and the diff path.
4. pass / warn -> nothing to change. block / fail -> fix the findings (max 2 rounds, re-spawning the reviewer as in STEP 6e / 7b), compile-check per STEP 5b, then commit ONLY the files you fixed, in the skill's STEP 9 style (git reset -q; git add -- <paths>; message "<prefix> <Tag>: <subject>" with prefix and tag chosen per push-in-session sections 3.1-3.2, no Co-Authored-By or other trailer) and push when an origin remote exists.
5. Append "## Post-hoc review" with each verdict (and the fix commit, if any) to the done file.
6. End with exactly one line: AUDIT_DONE - <reviewer: verdict, ...> - <fix commit sha or "no changes">. Never print a block token; if a finding cannot be fixed, say so in that line and in the done file.

Environment:
- AGENT_MODE=$MODE
- AGENT_BRANCH=$AGENT_BRANCH
- AGENT_BACKLOG_ROOT=$BACKLOG_ROOT
Do not ask for confirmation. Use English. Never end your turn to wait for anything in the background.

Start now.
EOT
}

# --- deterministic outcome + gate receipts (never trust the model's prose) -------
# A healthy iteration always ends with the picked task OUT of backlog/in-progress/:
# either DONE (file now in backlog/done/) or still queued in backlog/todo/
# (DEFERRED / demoted). exit 0 + no blocker sentinel + the file still sitting in
# in-progress/ = silent failure (the model stopped without printing its token).
task_still_in_progress() {
  [ -n "$1" ] && [ -f "$BACKLOG_ROOT/in-progress/$1" ]
}

task_reached_done() {
  [ -n "$1" ] && [ -f "$BACKLOG_ROOT/done/$1" ]
}

# Receipt = evidence in the stream-json log that an Agent tool call actually
# spawned <name>. Matches both the plain tool_use JSON ("subagent_type":"x")
# and the escaped partial-message delta form (\"subagent_type\":\"x\").
has_agent_spawn() {
  local log="$1" name="$2"
  grep -Eq '("|\\")subagent_type("|\\")[[:space:]]*:[[:space:]]*("|\\")'"$name" "$log" 2>/dev/null
}

# Receipts required per tier (run-backlog SKILL.md STEP 6d/7):
#   XS → none · S → code-reviewer · M/L → code-reviewer + qa-verifier.
# performance-reviewer/security-auditor are conditional on $PERF_SENSITIVE /
# $SENSITIVE (computed inside the iteration) so they cannot be required here.
missing_gate_receipts() {
  local log="$1" tier="$2" missing=""
  case "$tier" in
    S)
      has_agent_spawn "$log" "code-reviewer" || missing="code-reviewer"
      ;;
    M|L)
      has_agent_spawn "$log" "code-reviewer" || missing="code-reviewer"
      has_agent_spawn "$log" "qa-verifier" || missing="${missing:+$missing }qa-verifier"
      ;;
  esac
  printf '%s' "$missing"
}

# --- backlog status -------------------------------------------------------------
backlog_counts() {
  if [ ! -f "$BACKLOG_INDEX" ]; then echo "0 0"; return; fi
  awk '
    /^## / { sec=""; if ($0 ~ /^## TODO/) sec="todo"; else if ($0 ~ /^## IN PROGRESS/) sec="ip"; next }
    sec=="todo" && /^[[:space:]]*-[[:space:]]*\[(HIGH|MEDIUM|LOW)\]/ { t++ }
    sec=="ip"   && /^[[:space:]]*-[[:space:]]*\[/                    { p++ }
    END { printf "%d %d", t+0, p+0 }
  ' "$BACKLOG_INDEX"
}

task_line_for_section() {
  local section="$1"
  awk -v wanted="$section" '
    /^## / {
      sec="";
      if ($0 == "## " wanted) sec=wanted;
      next;
    }
    sec==wanted && /^[[:space:]]*-[[:space:]]*\[(HIGH|MEDIUM|LOW)\]/ {
      print;
      exit;
    }
  ' "$BACKLOG_INDEX"
}

# Extract [XS]/[S]/[M]/[L] tier from a bullet whose 2nd bracket may be the tier.
# Falls back to empty when the bullet omits the tier bracket (legacy format).
parse_tier() {
  printf '%s\n' "$1" | sed -nE 's/^[[:space:]]*-[[:space:]]*\[[^]]+\][[:space:]]+\[(XS|S|M|L)\].*/\1/p'
}

next_task_profile() {
  local line tier state
  line="$(task_line_for_section "IN PROGRESS")"
  state="in-progress"
  if [ -z "$line" ]; then
    line="$(task_line_for_section "TODO")"
    state="todo"
  fi

  if [ -z "$line" ]; then
    TASK_TIER=""; TASK_STATE=""
    SELECTED_MODEL="$MODEL"; SELECTED_EFFORT="$EFFORT"; SELECTED_THINKING_TOKENS="$THINKING_TOKENS"
    return
  fi

  tier="$(parse_tier "$line")"
  TASK_TIER="$tier"
  TASK_STATE="$state"

  case "$tier" in
    XS) SELECTED_MODEL="$XS_MODEL"; SELECTED_EFFORT="$XS_EFFORT"; SELECTED_THINKING_TOKENS="$XS_THINKING_TOKENS" ;;
    S)  SELECTED_MODEL="$S_MODEL";  SELECTED_EFFORT="$S_EFFORT";  SELECTED_THINKING_TOKENS="$S_THINKING_TOKENS" ;;
    M)  SELECTED_MODEL="$M_MODEL";  SELECTED_EFFORT="$M_EFFORT";  SELECTED_THINKING_TOKENS="$M_THINKING_TOKENS" ;;
    L)  SELECTED_MODEL="$L_MODEL";  SELECTED_EFFORT="$L_EFFORT";  SELECTED_THINKING_TOKENS="$L_THINKING_TOKENS" ;;
    *)  SELECTED_MODEL="$M_MODEL";  SELECTED_EFFORT="$M_EFFORT";  SELECTED_THINKING_TOKENS="$M_THINKING_TOKENS" ;;
  esac
}

build_cli_args() {
  CLI_ARGS=(--verbose --output-format stream-json --include-partial-messages)
  [ "$SKIP_PERMISSIONS" -eq 1 ] && CLI_ARGS+=(--dangerously-skip-permissions)
  [ -n "${SELECTED_MODEL:-}" ] && CLI_ARGS+=(--model "$SELECTED_MODEL")
  [ -n "${SELECTED_EFFORT:-}" ] && CLI_ARGS+=(--effort "$SELECTED_EFFORT")
  CLI_ARGS_Q="$(printf '%q ' "${CLI_ARGS[@]}")"

  if [ "$HAS_RENDER" -eq 1 ]; then
    RENDER_PIPE="| python3 $(printf '%q' "$RENDER") --provider claude --effort $(printf '%q' "${SELECTED_EFFORT:-default}")"
  else
    RENDER_PIPE=""
  fi
}

set_window_title "$(format_window_title "")"
echo
echo "=========================================="
echo "  $PROJECT_NAME — Run Backlog Loop (controller)"
echo "=========================================="
if [ "$AUTO_MODEL_BY_TIER" -eq 1 ]; then
  echo "  Model:           auto by task tier"
  echo "  Tier map:        XS=$XS_MODEL/$XS_EFFORT, S=$S_MODEL/$S_EFFORT, M=$M_MODEL/$M_EFFORT, L=$L_MODEL/$L_EFFORT"
  echo "  Thinking map:    XS=$XS_THINKING_TOKENS, S=$S_THINKING_TOKENS, M=$M_THINKING_TOKENS, L=$L_THINKING_TOKENS"
else
  echo "  Model:           ${MODEL:-<CLI default>}"
  echo "  Effort:          ${EFFORT:-<CLI default>}"
  echo "  Thinking tokens: $THINKING_TOKENS"
fi
echo "  Window mode:     $([ "$INLINE" -eq 1 ] && echo 'inline (this window)' || echo 'new window per task')"
echo "  Max iterations:  $MAX_ITERATIONS"
echo "  Checkpoints:     max $MAX_CHECKPOINTS consecutive TASK_CHECKPOINTED per task; deadline = start + ${TASK_HARD_TIMEOUT_SEC}s - ${CHECKPOINT_MARGIN_SEC}s"
echo "  Base branch:     $LOOP_BASE_BRANCH (captured at loop start)"
echo "  Log dir:         $LOG_DIR_ABS"
echo

# Token + cost report for one iteration, as JSON {summary, per_model, total_tokens,
# cost_usd}. Twin of Get-TokenReport in run-backlog-loop-core.ps1 — keep the two in
# lockstep (same fields, same wording, same rounding).
#
# Source of truth is the CLI's own "result" line(s): they carry the authoritative
# aggregates — usage, num_turns, total_cost_usd and modelUsage (per model, subagent
# turns included). Summing the per-turn assistant snapshots instead undercounts output
# badly, because a streamed message only reaches its final output_tokens on the last
# snapshot; that path stays as a fallback for runs that died before emitting a result
# line, and it can report tokens but never cost.
#
# One iteration can emit SEVERAL result lines (the CLI closes a result when the main
# turn ends, then continues once a backgrounded task returns), and they mix two scopes:
#   - modelUsage / total_cost_usd → session-cumulative, identical in every line
#     ⇒ take the last modelUsage, the max cost. Summing would double-count.
#   - usage / num_turns           → per segment ⇒ sum across the lines.
get_token_report() {
  local log="$1"
  [ -f "$log" ] || return 0
  jq -s -c '
    def fmt_tokens(n):
      if n >= 1000000 then (((n / 100000) | round) / 10 | tostring) + "M"
      elif n >= 1000 then (((n / 100) | round) / 10 | tostring) + "K"
      else (n | round | tostring) end;
    def fmt_cost(c):
      ((c * 100) | round) as $cents
      | (($cents / 100) | floor | tostring) + "."
        + (($cents % 100) as $r | if $r < 10 then "0" + ($r | tostring) else ($r | tostring) end);

    [ .[] | select(.type == "result") ] as $results
    | ( [ $results[] | select(.modelUsage != null and (.modelUsage | length) > 0) | .modelUsage ] | last ) as $mu
    | ( [ $results[] | .total_cost_usd // 0 ] | max // 0 ) as $maxcost
    | ( [ $results[] | .num_turns // 0 ] | add // 0 ) as $result_turns
    | (
        if $mu != null then
          ( [ $mu | to_entries[] | {
                name: (.key | sub("^claude-"; "")),
                tin:  (.value.inputTokens // 0),
                tout: (.value.outputTokens // 0),
                tcw:  (.value.cacheCreationInputTokens // 0),
                tcr:  (.value.cacheReadInputTokens // 0),
                cost: (.value.costUSD // 0) } ] ) as $rows
          | { tin:  ([ $rows[].tin ]  | add // 0),
              tout: ([ $rows[].tout ] | add // 0),
              tcw:  ([ $rows[].tcw ]  | add // 0),
              tcr:  ([ $rows[].tcr ]  | add // 0),
              cost: ([ $rows[].cost ] | add // 0),
              turns: $result_turns, rows: $rows, partial: false }
        elif ($results | length) > 0 then
          { tin:  ([ $results[] | .usage.input_tokens // 0 ] | add // 0),
            tout: ([ $results[] | .usage.output_tokens // 0 ] | add // 0),
            tcw:  ([ $results[] | .usage.cache_creation_input_tokens // 0 ] | add // 0),
            tcr:  ([ $results[] | .usage.cache_read_input_tokens // 0 ] | add // 0),
            cost: $maxcost, turns: $result_turns, rows: [], partial: false }
        else
          ( [ .[] | select(.type == "assistant" and .message.id != null and .message.usage != null) ]
            | group_by(.message.id)
            | map(max_by(.message.usage.output_tokens // 0) | .message.usage) ) as $turns
          | { tin:  ([ $turns[] | .input_tokens // 0 ] | add // 0),
              tout: ([ $turns[] | .output_tokens // 0 ] | add // 0),
              tcw:  ([ $turns[] | .cache_creation_input_tokens // 0 ] | add // 0),
              tcr:  ([ $turns[] | .cache_read_input_tokens // 0 ] | add // 0),
              cost: 0, turns: ($turns | length), rows: [], partial: true }
        end
      ) as $t
    | ($t.tin + $t.tout + $t.tcw + $t.tcr) as $total
    | if $total <= 0 then { summary: "", per_model: "", total_tokens: 0, cost_usd: 0 }
      else
        ( $t.rows | map(. + { tok: (.tin + .tout + .tcw + .tcr) }) | sort_by(-.cost) ) as $sorted
        | ( $sorted[0:4] ) as $top
        | ( $sorted[4:] ) as $rest
        | { summary:
              ( [ (fmt_tokens($total) + " total" + (if $t.cost > 0 then " | ~$" + fmt_cost($t.cost) else "" end)),
                  ("In " + fmt_tokens($t.tin) + " | Out " + fmt_tokens($t.tout)),
                  ("Cache W " + fmt_tokens($t.tcw) + " | R " + fmt_tokens($t.tcr)) ]
                + (if $t.turns > 0 then [ ($t.turns | tostring) + " turns" ] else [] end)
                + (if $t.partial then [ "(partial: no result line)" ] else [] end)
                | join("\n") ),
            per_model:
              ( [ $top[] | (.name + ": " + fmt_tokens(.tok) + " | ~$" + fmt_cost(.cost)) ]
                + (if ($rest | length) > 0
                   then [ "+" + ($rest | length | tostring) + " more: "
                          + fmt_tokens([ $rest[].tok ] | add // 0) + " | ~$" + fmt_cost([ $rest[].cost ] | add // 0) ]
                   else [] end)
                | join("\n") ),
            total_tokens: $total,
            cost_usd: $t.cost }
      end
  ' "$log" 2>/dev/null
}

# Approximate per-tool time + token breakdown for the Discord "Time & Token Breakdown"
# field. Twin of Get-TimingTokenBreakdown in run-backlog-loop-core.ps1 (same heuristics,
# same 8-row cap, byte-identical output). Two approximations, both unavoidable given
# what stream-json actually timestamps:
#   - Time: only tool_result ("user" role) lines carry a timestamp, not the tool_use
#     call. The gap between consecutive tool_result timestamps is attributed to the tool
#     whose result ARRIVES at the end of the gap — a gap is "model picks the next tool +
#     that tool runs", so it belongs to the tool that just finished.
#   - Tokens: usage is reported per assistant turn, not per tool call. A turn's usage is
#     split evenly across the tool_use block(s) it issued (almost always 1).
get_timing_breakdown() {
  local log="$1"
  [ -f "$log" ] || return 0
  jq -s -r '
    def fmt_tokens(n):
      if n >= 1000000 then (((n / 100000) | round) / 10 | tostring) + "M"
      elif n >= 1000 then (((n / 100) | round) / 10 | tostring) + "K"
      else (n | round | tostring) end;
    def fmt_secs(s):
      if s >= 60 then (((s / 6) | round) / 10 | tostring) + "m"
      else (s | round | tostring) + "s" end;
    def rpad($n): tostring | if (length >= $n) then . else . + (" " * ($n - length)) end;
    def lpad($n): tostring | if (length >= $n) then . else (" " * ($n - length)) + . end;
    # Sub-second precision matters: fromdateiso8601 alone truncates the milliseconds and
    # the per-tool rows then drift from the .ps1 twin by up to a tenth of a minute.
    def parse_ts:
      (. | sub("Z$"; "")) as $s
      | ($s | split(".")) as $p
      | ($p[0] + "Z" | fromdateiso8601)
        + (if ($p | length) > 1 then ("0." + $p[1] | tonumber) else 0 end);
    def tool_category:
      . as $n
      | if ($n == "Bash" or $n == "PowerShell" or $n == "run_shell_command") then "exec"
        elif ($n | startswith("mcp__")) then
          ($n | split("__")) as $p
          | (if ($p | length) >= 3 then $p[2] else ($n | ltrimstr("mcp__")) end)
        else $n end;

    . as $all
    | [ $all[] | select(.type == "assistant" and .message.id != null) ] as $asst
    | ( [ $asst[] | .message.content[]?
          | select(.type == "tool_use" and .name != null and .id != null)
          | { key: .id, value: (.name | tool_category) } ] | from_entries ) as $idcat
    # Streaming re-emits growing snapshots of the same message id; the last non-empty
    # set wins so a tool_use block added mid-stream is not missed.
    | ( [ $asst[]
          | { id: .message.id,
              cats: [ .message.content[]? | select(.type == "tool_use" and .name != null) | (.name | tool_category) ] }
          | select((.cats | length) > 0) ]
        | group_by(.id) | map({ key: .[0].id, value: (.[-1].cats) }) | from_entries ) as $msgcats
    | ( [ $asst[] | select(.message.usage != null) | { id: .message.id, u: .message.usage } ]
        | group_by(.id)
        | map({ key: .[0].id, value: (max_by(.u.output_tokens // 0) | .u) }) | from_entries ) as $msgusage
    | ( reduce ($msgusage | to_entries[]) as $e ({};
          ($msgcats[$e.key] // []) as $cats
          | if ($cats | length) == 0 then .
            else
              ( ($e.value.input_tokens // 0) + ($e.value.cache_creation_input_tokens // 0)
                + ($e.value.output_tokens // 0) + ($e.value.cache_read_input_tokens // 0) ) as $tok
              | ($tok / ($cats | length)) as $share
              | reduce $cats[] as $c (.; .[$c] = ((.[$c] // 0) + $share))
            end ) ) as $tokstats
    | ( [ $all[]
          | select(.type == "user" and .timestamp != null and .message != null)
          | { t: (.timestamp | parse_ts),
              cats: [ .message.content[]?
                      | select(.type == "tool_result" and .tool_use_id != null)
                      | $idcat[.tool_use_id] // empty ] }
          | select((.cats | length) > 0) ]
        | sort_by(.t) ) as $events
    | ( reduce range(1; ($events | length)) as $i ({};
          ($events[$i].t - $events[$i - 1].t) as $gap
          | if $gap < 0 then .
            else
              ($events[$i].cats) as $cats
              | ($gap / ($cats | length)) as $share
              | reduce $cats[] as $c (.; .[$c] = ((.[$c] // 0) + $share))
            end ) ) as $timestats
    | ( (($timestats | keys) + ($tokstats | keys)) | unique ) as $cats
    | if ($cats | length) == 0 then ""
      else
        ( [ $cats[] | { name: ., secs: ($timestats[.] // 0), toks: ($tokstats[.] // 0) } ] | sort_by(-.secs) ) as $rows
        | ( $rows[0:8] ) as $top
        | ( $rows[8:] ) as $rest
        | ( [ 12 ] + [ $top[] | (.name | length) ] | max ) as $w
        | ( [ ("Tool" | rpad($w)) + "  " + ("Time" | lpad(7)) + "  " + ("Tokens" | lpad(8)) ]
            + [ $top[] | (.name | rpad($w)) + "  " + (fmt_secs(.secs) | lpad(7)) + "  " + (fmt_tokens(.toks) | lpad(8)) ]
            + (if ($rest | length) > 0
               then [ (("+" + ($rest | length | tostring) + " more") | rpad($w)) + "  "
                      + (fmt_secs([ $rest[].secs ] | add // 0) | lpad(7)) + "  "
                      + (fmt_tokens([ $rest[].toks ] | add // 0) | lpad(8)) ]
               else [] end)
          | join("\n") )
      end
  ' "$log" 2>/dev/null
}

# "3 iters | 41.2M tok | ~$28.90" — empty until at least one iteration has been
# measured, so the field simply does not appear on the very first notification.
format_loop_cumulative() {
  [ "$LOOP_ITERS_COUNTED" -eq 0 ] && return 0
  awk -v n="$LOOP_ITERS_COUNTED" -v t="$LOOP_TOKENS_TOTAL" -v c="$LOOP_COST_TOTAL" 'BEGIN {
    if (t >= 1000000) tf = sprintf("%.1fM", t / 1000000);
    else if (t >= 1000) tf = sprintf("%.1fK", t / 1000);
    else tf = sprintf("%d", t);
    if (c > 0) printf "%d iters | %s tok | ~$%.2f", n, tf, c;
    else printf "%d iters | %s tok", n, tf;
  }'
}

# Everything the notification needs about one iteration's usage, in a single call:
# fills REPORT_SUMMARY / REPORT_PER_MODEL / REPORT_BREAKDOWN / REPORT_CUMULATIVE and
# folds this iteration into the loop-wide running total. Every notify site that has an
# iteration log goes through here, so the running total can never miss one. Degrades to
# empty strings (no fields, no failure) when jq is absent.
collect_iteration_report() {
  local log="$1" json tok cost
  REPORT_SUMMARY=""; REPORT_PER_MODEL=""; REPORT_BREAKDOWN=""; REPORT_CUMULATIVE=""
  command -v jq >/dev/null 2>&1 || return 0
  [ -f "$log" ] || return 0

  json="$(get_token_report "$log")"
  [ -z "$json" ] && return 0
  REPORT_SUMMARY="$(printf '%s' "$json" | jq -r '.summary // ""')"
  REPORT_PER_MODEL="$(printf '%s' "$json" | jq -r '.per_model // ""')"
  REPORT_BREAKDOWN="$(get_timing_breakdown "$log")"

  if [ -n "$REPORT_SUMMARY" ]; then
    tok="$(printf '%s' "$json" | jq -r '.total_tokens // 0')"
    cost="$(printf '%s' "$json" | jq -r '.cost_usd // 0')"
    LOOP_ITERS_COUNTED=$((LOOP_ITERS_COUNTED + 1))
    LOOP_TOKENS_TOTAL="$(awk -v a="$LOOP_TOKENS_TOTAL" -v b="$tok" 'BEGIN { printf "%.0f", a + b }')"
    LOOP_COST_TOTAL="$(awk -v a="$LOOP_COST_TOTAL" -v b="$cost" 'BEGIN { printf "%.4f", a + b }')"
  fi
  REPORT_CUMULATIVE="$(format_loop_cumulative)"
}

# Write one runner script for iteration $1; runs claude, tees log, writes exit code to
# the flag file (via EXIT trap so it's ALWAYS written), keeps window open on failure.
write_runner() {
  local idx="$1" log="$2" flag="$3" promptfile="$4" runner="$5" pidfile="$6"
  cat > "$runner" <<RUNNER
#!/usr/bin/env bash
cd $(printf '%q' "$WORK_DIR") || exit 9
printf '\033]0;%s\007' $(printf '%q' "$(format_window_title "${TASK_TITLE_NOTIF:-}")")
# Terminal.app spawns this window, not the controller: nothing is inherited, so
# every variable the skill reads is written here explicitly.
export AGENT_BASE_BRANCH=$(printf '%q' "$LOOP_BASE_BRANCH")
export AGENT_BRANCH=$(printf '%q' "$AGENT_BRANCH")
export AGENT_MODE=$(printf '%q' "$MODE")
export AGENT_BACKLOG_ROOT=$(printf '%q' "$BACKLOG_ROOT")
export AGENT_WORKDIR=$(printf '%q' "$WORK_DIR")
export BACKLOG_LOOP_TOKEN=$(printf '%q' "$BACKLOG_LOOP_TOKEN")
export PYTHONUTF8=1
export AGENT_RECOVERY_BRIEF=$(printf '%q' "${ITER_RECOVERY_BRIEF:-}")
[ -n "\$AGENT_RECOVERY_BRIEF" ] || unset AGENT_RECOVERY_BRIEF
export MAX_THINKING_TOKENS=$(printf '%q' "${SELECTED_THINKING_TOKENS:-}")
if [ -z "\$MAX_THINKING_TOKENS" ] || [ "\$MAX_THINKING_TOKENS" = "0" ]; then
  unset MAX_THINKING_TOKENS
fi
echo \$\$ > $(printf '%q' "$pidfile")
code=0
# Idempotent: do not clobber a flag the controller already wrote (e.g. "124" on watchdog kill).
trap '[ -f $(printf '%q' "$flag") ] || echo "\$code" > $(printf '%q' "$flag")' EXIT
echo "=== $PROJECT_NAME backlog task — iteration $idx ==="
cat $(printf '%q' "$promptfile") | claude $CLI_ARGS_Q 2>&1 | tee $(printf '%q' "$log") $RENDER_PIPE
code=\${PIPESTATUS[1]}
echo "\$code" > $(printf '%q' "$flag")
if [ "\$code" -ne 0 ]; then
  echo ""
  echo "Task FAILED (exit \$code) — this window is kept open so you can read the error above."
  read -n 1 -s -r -p "Press any key to close this window..."
  echo ""
fi
RUNNER
  chmod +x "$runner"
}

STOP_REASON=""
if [ "$SELF_HEAL" -eq 1 ]; then
  echo "Self-heal:   ON — up to $MAX_RECOVERIES recovery iteration(s) per task, then park it and go on; limits wait up to $MAX_USAGE_WAIT_MINUTES min"
else
  echo "Self-heal:   OFF (--no-self-heal) — the first block stops the loop"
fi
i=0
while [ "$i" -lt "$MAX_ITERATIONS" ]; do
  i=$((i + 1))

  read -r TODO IP <<<"$(backlog_counts)"
  echo "--- Iteration $i/$MAX_ITERATIONS — backlog: TODO=$TODO, IN_PROGRESS=$IP ---"

  # DONE_BEFORE/TOTAL_BEFORE snapshot the backlog at the TOP of this iteration,
  # so every notification fired during the iteration (including blocked/error
  # events, where the task never reaches backlog/done) can still say "this is
  # task N of M".
  DONE_BEFORE=$(find "$BACKLOG_ROOT/done" -name "*.md" 2>/dev/null | wc -l | xargs)
  TOTAL_BEFORE=$((TODO + IP + DONE_BEFORE))

  IS_AUDIT=0
  [ -n "$PENDING_AUDIT_TASK" ] && IS_AUDIT=1
  PICKED_TASK=""
  TASK_NNN=""
  if [ "$IS_AUDIT" -eq 1 ]; then
    PICKED_TASK="$PENDING_AUDIT_TASK"
    TASK_NNN="$PENDING_AUDIT_NNN"
    echo "  Post-hoc gate audit of task $TASK_NNN (commit $PENDING_AUDIT_COMMIT): spawning $PENDING_AUDIT_MISSING."
  else
    # Deterministic pre-check, no model spawned: pick exactly as this loop's
    # iteration will (lease token, --probe = on its behalf). A task owned by a live
    # session (another /run-backlog, or the window of a killed controller): self-heal
    # waits for that session; --no-self-heal stops here.
    PICK_JSON="$(cd "$WORK_DIR" && python3 "$OPS" pick --probe 2>&1)"
    PICK_RC=$?
    if [ "$PICK_RC" -eq 4 ] && [ "$SELF_HEAL" -eq 1 ]; then
      busy_msg="$(json_field "$PICK_JSON" sentinel) — $(json_field "$PICK_JSON" reason) (task $(json_field "$PICK_JSON" nnn)): waiting up to $BUSY_WAIT_MINUTES min for that session to finish."
      echo "  ⏸ $busy_msg"
      notify --event "BUSY_WAIT" --task "N/A" --details "$busy_msg" --progress "$DONE_BEFORE/$TOTAL_BEFORE"
      busy_waited=0
      while [ "$PICK_RC" -eq 4 ] && [ "$busy_waited" -lt $((BUSY_WAIT_MINUTES * 60)) ]; do
        sleep 60
        busy_waited=$((busy_waited + 60))
        PICK_JSON="$(cd "$WORK_DIR" && python3 "$OPS" pick --probe 2>&1)"
        PICK_RC=$?
      done
    fi
    # Any other pick failure (a git lock, a mutex held by a dying call): one retry.
    if [ "$PICK_RC" -ne 0 ] && [ "$PICK_RC" -ne 2 ] && [ "$PICK_RC" -ne 4 ] && [ "$SELF_HEAL" -eq 1 ]; then
      echo "  backlog-ops.py pick failed (exit $PICK_RC) — retrying once in 20s" >&2
      sleep 20
      PICK_JSON="$(cd "$WORK_DIR" && python3 "$OPS" pick --probe 2>&1)"
      PICK_RC=$?
    fi
    PICK_STATE="$(json_field "$PICK_JSON" state)"
    if [ "$PICK_RC" -eq 4 ]; then
      STOP_REASON="$(json_field "$PICK_JSON" sentinel) — $(json_field "$PICK_JSON" reason) (task $(json_field "$PICK_JSON" nnn)): $(json_field "$PICK_JSON" hint)"
      echo "  $STOP_REASON" >&2
      printf '%s\n' "$PICK_JSON" >&2
      notify --event "$(json_field "$PICK_JSON" sentinel)" --task "N/A" --details "$STOP_REASON" \
        --progress "$DONE_BEFORE/$TOTAL_BEFORE"
      break
    fi
    if [ "$PICK_RC" -ne 0 ] && [ "$PICK_RC" -ne 2 ]; then
      STOP_REASON="backlog-ops.py pick failed (exit $PICK_RC): $PICK_JSON"
      break
    fi
    [ "$PICK_STATE" = "ship-pending" ] && echo "  Ship-pending: task $(json_field "$PICK_JSON" nnn) is DONE but its commit was never recorded — this iteration ships it first."

    if [ "$PICK_RC" -eq 2 ]; then
      STOP_REASON="Backlog empty (no TODO, no IN PROGRESS)"
      # No iteration log to analyze here, but the loop-so-far total is exactly what
      # closes the run out ("everything done — this is what it cost").
      notify --event "BACKLOG_EMPTY" --task "N/A" \
        --details "All backlog tasks have been processed." \
        --cumulative "$(format_loop_cumulative)" \
        --progress "$DONE_BEFORE/$DONE_BEFORE"
      break
    fi

    pick_path="$(json_field "$PICK_JSON" path)"
    [ -n "$pick_path" ] && PICKED_TASK="$(basename "$pick_path")"
    TASK_NNN="$(json_field "$PICK_JSON" nnn)"

    # A task parked earlier in this run is skipped while anything else is left.
    if [ -n "$PICKED_TASK" ] && [ -n "$(kv_get "$PARKED_TASKS" "$PICKED_TASK")" ]; then
      if [ "$PICK_STATE" = "todo" ] && [ "$(unparked_todo_count)" -gt 0 ]; then
        if (cd "$WORK_DIR" && python3 "$OPS" defer "$TASK_NNN" >/dev/null 2>&1); then
          echo "  Skipping parked task $TASK_NNN (moved behind the rest of TODO)."
          i=$((i - 1))
          continue
        fi
        STOP_REASON="Could not skip parked task $TASK_NNN (backlog-ops defer failed)"
      else
        STOP_REASON="Only parked tasks remain: $(printf '%s' "$PARKED_TASKS" | awk -F'\t' 'NF { printf "%s%s [%s]", sep, $1, $2; sep = "; " }'). Each task file in $BACKLOG_ROOT/todo ends with its park reason; partial work is under refs/backlog/parked/<NNN>. Relaunch the loop to give them a fresh budget."
      fi
      echo "  $STOP_REASON"
      notify --event "PARKED_ONLY" --task "N/A" --details "$STOP_REASON" \
        --cumulative "$(format_loop_cumulative)" --progress "$DONE_BEFORE/$TOTAL_BEFORE"
      break
    fi
  fi

  # "Current" task's 1-based position among all tasks (todo + in-progress + done).
  TASK_PROGRESS_NOTIF="$((DONE_BEFORE + 1))/$TOTAL_BEFORE"

  # Resolve current task info for notifications.
  CURRENT_TASK_LINE="$(task_line_for_section "IN PROGRESS")"
  [ -z "$CURRENT_TASK_LINE" ] && CURRENT_TASK_LINE="$(task_line_for_section "TODO")"

  if [ -n "$CURRENT_TASK_LINE" ]; then
    TASK_TIER_NOTIF="$(parse_tier "$CURRENT_TASK_LINE")"
    if [ -n "$TASK_TIER_NOTIF" ]; then
      TASK_TITLE_NOTIF="$(printf '%s\n' "$CURRENT_TASK_LINE" | sed -E 's/^[[:space:]]*-[[:space:]]*\[[^]]+\][[:space:]]+\[[^]]+\][[:space:]]+\[([^]]+)\].*/\1/')"
    else
      TASK_TITLE_NOTIF="$(printf '%s\n' "$CURRENT_TASK_LINE" | sed -E 's/^[[:space:]]*-[[:space:]]*\[[^]]+\][[:space:]]+\[([^]]+)\].*/\1/')"
    fi
    TASK_FILE_PATH_NOTIF="$(printf '%s\n' "$CURRENT_TASK_LINE" | sed -nE 's/.*\]\((backlog\/[^)]+)\).*/\1/p')"
    if [ -n "$TASK_FILE_PATH_NOTIF" ]; then
      TASK_URL_NOTIF="file://$GIT_COMMON_DIR/$TASK_FILE_PATH_NOTIF"
      TASK_BASE_NOTIF="$(basename "$TASK_FILE_PATH_NOTIF")"
    else
      TASK_URL_NOTIF=""
      TASK_BASE_NOTIF=""
    fi
  else
    TASK_TITLE_NOTIF="Unknown Task"
    TASK_URL_NOTIF=""
    TASK_TIER_NOTIF=""
    TASK_BASE_NOTIF=""
  fi
  # The task this iteration works on is what pick (or the audit) named.
  [ -n "$PICKED_TASK" ] && TASK_BASE_NOTIF="$PICKED_TASK"
  [ "$IS_AUDIT" -eq 1 ] && TASK_TITLE_NOTIF="$TASK_NNN (post-hoc audit)"

  IS_RECOVERY=0
  if [ "$IS_AUDIT" -eq 0 ] && [ -n "$PENDING_RECOVERY_TASK" ]; then
    if [ "$PENDING_RECOVERY_TASK" = "$PICKED_TASK" ]; then
      IS_RECOVERY=1
    else
      echo "  Dropping the pending recovery of $PENDING_RECOVERY_TASK: pick returned $PICKED_TASK."
      PENDING_RECOVERY_TASK=""
    fi
  fi

  if [ "$AUTO_MODEL_BY_TIER" -eq 1 ]; then
    next_task_profile
  else
    TASK_TIER=""; TASK_STATE=""
    SELECTED_MODEL="$MODEL"; SELECTED_EFFORT="$EFFORT"; SELECTED_THINKING_TOKENS="$THINKING_TOKENS"
  fi
  # A recovery (or an audit) gets the strongest model: whatever beat the tier's
  # default model once is not beaten by the same model with the same budget.
  if [ "$AUTO_MODEL_BY_TIER" -eq 1 ] && { [ "$IS_RECOVERY" -eq 1 ] || [ "$IS_AUDIT" -eq 1 ]; }; then
    SELECTED_MODEL="$RECOVERY_MODEL"; SELECTED_EFFORT="$RECOVERY_EFFORT"; SELECTED_THINKING_TOKENS="$L_THINKING_TOKENS"
    echo "  Escalated: model=$SELECTED_MODEL effort=$SELECTED_EFFORT thinking=$SELECTED_THINKING_TOKENS"
  fi
  build_cli_args

  if [ "$AUTO_MODEL_BY_TIER" -eq 1 ]; then
    echo "  Next task: [${TASK_TIER:-unknown}] $TASK_TITLE_NOTIF ($TASK_STATE)"
    echo "  Profile: model=${SELECTED_MODEL:-<CLI default>} effort=${SELECTED_EFFORT:-<CLI default>} thinking=${SELECTED_THINKING_TOKENS:-off}"
  fi

  ts="$(date +%Y%m%d-%H%M%S)"
  base="$LOG_DIR_ABS/iter-$i-$ts"
  log_file="$base.log"
  flag_file="$base.flag"
  prompt_file="$base.prompt"
  runner_file="$base.run.sh"
  pid_file="$base.pid"
  ITER_START=$(date +%s)
  ITER_RECOVERY_BRIEF=""
  if [ "$IS_AUDIT" -eq 1 ]; then
    ITER_PROMPT="$(audit_prompt)"
  else
    ITER_PROMPT="${PROMPT//__ITER_DEADLINE__/$((ITER_START + TASK_HARD_TIMEOUT_SEC - CHECKPOINT_MARGIN_SEC))}"
    if [ "$IS_RECOVERY" -eq 1 ]; then
      ITER_RECOVERY_BRIEF="$PENDING_RECOVERY_BRIEF"
      echo "  RECOVERY iteration $PENDING_RECOVERY_ATTEMPT/$MAX_RECOVERIES for task $TASK_NNN after $PENDING_RECOVERY_EVENT."
      recovery_text="$(recovery_appendix)"
      ITER_PROMPT=${ITER_PROMPT//__RECOVERY__/$recovery_text}
    else
      ITER_PROMPT=${ITER_PROMPT//__RECOVERY__/}
    fi
  fi
  printf '%s\n' "$ITER_PROMPT" > "$prompt_file"
  rm -f "$flag_file"

  if [ "$INLINE" -eq 1 ]; then
    # Same-window execution: no window to name, so retitle THIS one per task and
    # hand it back to the controller title when the loop ends.
    set_window_title "$(format_window_title "$TASK_TITLE_NOTIF")"
    if [ "${SELECTED_THINKING_TOKENS:-0}" -gt 0 ] 2>/dev/null; then
      export MAX_THINKING_TOKENS="$SELECTED_THINKING_TOKENS"
    else
      unset MAX_THINKING_TOKENS
    fi
    if [ -n "$ITER_RECOVERY_BRIEF" ]; then
      export AGENT_RECOVERY_BRIEF="$ITER_RECOVERY_BRIEF"
    else
      unset AGENT_RECOVERY_BRIEF
    fi
    if [ "$HAS_RENDER" -eq 1 ]; then
      printf '%s\n' "$ITER_PROMPT" | claude "${CLI_ARGS[@]}" 2>&1 | tee "$log_file" | python3 "$RENDER" --provider claude --effort "${SELECTED_EFFORT:-default}"
    else
      printf '%s\n' "$ITER_PROMPT" | claude "${CLI_ARGS[@]}" 2>&1 | tee "$log_file"
    fi
    exit_code="${PIPESTATUS[1]}"
  else
    # New Terminal window per task (macOS via osascript).
    write_runner "$i" "$log_file" "$flag_file" "$prompt_file" "$runner_file" "$pid_file"
    # The `custom title` set here is what actually sticks on Terminal.app; the
    # runner's own OSC escape covers the moment before this runs (and any terminal
    # that has no AppleScript). `try` around it so an OS/Terminal version without
    # the property loses only the label, never the spawn.
    win_title_as="$(applescript_quote "$(format_window_title "$TASK_TITLE_NOTIF")")"
    win_id="$(osascript -e "tell application \"Terminal\"" -e "set taskTab to do script \"bash '$runner_file'\"" -e "try" -e "set custom title of taskTab to \"$win_title_as\"" -e "end try" -e "return id of front window" -e "end tell" 2>/dev/null)" \
      || { STOP_REASON="Failed to open Terminal window (grant Automation permission to Terminal, or use --inline)"; break; }
    echo "  Spawned task window; waiting for it to finish (monitoring inactivity)..."
    # Wait for the flag file with a timeout / inactivity check to avoid hanging on token-limit errors.
    elapsed=0
    last_size=0
    inactive_seconds=0
    check_interval=5
    while [ ! -f "$flag_file" ]; do
      sleep $check_interval
      elapsed=$((elapsed + check_interval))

      if [ -f "$log_file" ]; then
        current_size=$(stat -f%z "$log_file" 2>/dev/null || stat -c%s "$log_file" 2>/dev/null || echo 0)
        if [ "$current_size" -eq "$last_size" ]; then
          inactive_seconds=$((inactive_seconds + check_interval))
        else
          inactive_seconds=0
          last_size="$current_size"
        fi
      else
        inactive_seconds=$((inactive_seconds + check_interval))
      fi

      # TASK_INACTIVITY_TIMEOUT_SEC (15 min) of absolute inactivity, or TASK_HARD_TIMEOUT_SEC (180 min) max execution time.
      if [ "$inactive_seconds" -ge "$TASK_INACTIVITY_TIMEOUT_SEC" ] || [ "$elapsed" -ge "$TASK_HARD_TIMEOUT_SEC" ]; then
        if [ "$inactive_seconds" -ge "$TASK_INACTIVITY_TIMEOUT_SEC" ]; then
          WATCHDOG_REASON="Task hung or stopped due to token exhaustion/inactivity (no log updates for $((TASK_INACTIVITY_TIMEOUT_SEC / 60))m)"
        else
          WATCHDOG_REASON="Task timed out (exceeded $((TASK_HARD_TIMEOUT_SEC / 60))m limit)"
        fi
        echo "  ⚠️ $WATCHDOG_REASON. Killing task window so it stops consuming tokens." >&2
        echo "124" > "$flag_file"   # claim the result first so the runner's EXIT trap won't clobber it
        if [ -f "$pid_file" ]; then
          runner_pid="$(tr -dc '0-9' < "$pid_file")"
          if [ -n "$runner_pid" ]; then
            pkill -TERM -P "$runner_pid" 2>/dev/null || true
            kill -TERM "$runner_pid" 2>/dev/null || true
          fi
        fi
        [ -n "${win_id:-}" ] && osascript -e "tell application \"Terminal\" to close (every window whose id is $win_id) saving no" >/dev/null 2>&1 || true
        break
      fi
    done
    exit_code="$(tr -dc '0-9' < "$flag_file")"
    [ -z "$exit_code" ] && exit_code=0
    # Task finished cleanly (implemented + DONE + pushed) -> close its window so a
    # long loop doesn't leave one window per task behind. A failed run keeps its
    # window (the runner is still blocking on "press any key") so the error stays
    # readable; the watchdog path already closed its own window above.
    if [ "$exit_code" -eq 0 ] && [ -n "${win_id:-}" ]; then
      osascript -e "tell application \"Terminal\" to close (every window whose id is $win_id) saving no" >/dev/null 2>&1 || true
    fi
    rm -f "$runner_file" "$pid_file"
  fi

  ITER_ELAPSED=$(( $(date +%s) - ITER_START ))
  ITER_DURATION=$(printf '%02d:%02d:%02d' $((ITER_ELAPSED/3600)) $(((ITER_ELAPSED%3600)/60)) $((ITER_ELAPSED%60)))

  # The recovery is consumed by this iteration; a limit/transient retry below hands
  # it back so the retry is still a recovery.
  CONSUMED_RECOVERY_TASK=""
  if [ "$IS_RECOVERY" -eq 1 ]; then
    CONSUMED_RECOVERY_TASK="$PENDING_RECOVERY_TASK"
    PENDING_RECOVERY_TASK=""
  fi

  if [ "$exit_code" -ne 0 ]; then
    fail_reason="$(claude_failure_class "$log_file")" && fail_transient=1 || fail_transient=0
    fail_kind="$(claude_failure_kind "$log_file")"

    # An exhausted usage/session limit is slept out until its reset, then the same
    # task resumes — retrying now would only burn quota against a wall.
    if [ "$fail_kind" = "usage" ] && [ "$SELF_HEAL" -eq 1 ]; then
      if wait_for_limit_reset "$log_file" 0 "Usage/session limit reached"; then
        PENDING_RECOVERY_TASK="$CONSUMED_RECOVERY_TASK"
        continue
      fi
      STOP_REASON="claude stopped: usage/credit exhausted and the reset is beyond the ${MAX_USAGE_WAIT_MINUTES} min wait budget (exit $exit_code, iteration $i, see $log_file)"
      collect_iteration_report "$log_file"
      notify --event "CLI_ERROR" --task "$TASK_TITLE_NOTIF" --url "$TASK_URL_NOTIF" \
        --tokens "$REPORT_SUMMARY" --per-model "$REPORT_PER_MODEL" \
        --breakdown "$REPORT_BREAKDOWN" --cumulative "$REPORT_CUMULATIVE" \
        --details "$STOP_REASON" --progress "$TASK_PROGRESS_NOTIF" --duration "$ITER_DURATION"
      break
    fi

    # Org quota ran dry (403): retry the same task with its own budget, then stop —
    # self-heal must not burn recoveries/parks on a task that never got to run.
    if [ "$fail_kind" = "quota" ]; then
      if [ "$QUOTA_RETRIES" -lt "$MAX_QUOTA_RETRIES" ]; then
        QUOTA_RETRIES=$((QUOTA_RETRIES + 1))
        backoff=$((30 * (1 << (QUOTA_RETRIES - 1))))
        [ "$backoff" -gt 900 ] && backoff=900
        retry_msg="Org quota 403 on iteration $i ($fail_reason). Retry $QUOTA_RETRIES/$MAX_QUOTA_RETRIES in ${backoff}s."
        echo "  $retry_msg"
        notify --event "API_RETRY" --task "$TASK_TITLE_NOTIF" --url "$TASK_URL_NOTIF" \
          --details "$retry_msg" --progress "$TASK_PROGRESS_NOTIF" --duration "$ITER_DURATION"
        sleep "$backoff"
        PENDING_RECOVERY_TASK="$CONSUMED_RECOVERY_TASK"
        continue
      fi
      STOP_REASON="claude stopped after $QUOTA_RETRIES retries: $fail_reason (exit $exit_code, iteration $i, see $log_file)"
      collect_iteration_report "$log_file"
      notify --event "CLI_ERROR" --task "$TASK_TITLE_NOTIF" --url "$TASK_URL_NOTIF" \
        --tokens "$REPORT_SUMMARY" --per-model "$REPORT_PER_MODEL" \
        --breakdown "$REPORT_BREAKDOWN" --cumulative "$REPORT_CUMULATIVE" \
        --details "$STOP_REASON" --progress "$TASK_PROGRESS_NOTIF" --duration "$ITER_DURATION"
      break
    fi

    if [ "$fail_transient" -eq 1 ] && [ "$TRANSIENT_API_RETRIES" -lt "$MAX_TRANSIENT_API_RETRIES" ]; then
      TRANSIENT_API_RETRIES=$((TRANSIENT_API_RETRIES + 1))
      # 30s, 60s, 120s ... capped at 15 min - a dropped stream usually clears on the
      # first retry; the backoff matters for an overload, which needs the far side to drain.
      backoff=$((30 * (1 << (TRANSIENT_API_RETRIES - 1))))
      [ "$backoff" -gt 900 ] && backoff=900
      retry_msg="Transient API failure on iteration $i ($fail_reason). Retry $TRANSIENT_API_RETRIES/$MAX_TRANSIENT_API_RETRIES in ${backoff}s."
      echo "  $retry_msg"
      notify --event "API_RETRY" --task "$TASK_TITLE_NOTIF" --url "$TASK_URL_NOTIF" \
        --details "$retry_msg" --progress "$TASK_PROGRESS_NOTIF" --duration "$ITER_DURATION"
      sleep "$backoff"
      # The next iteration re-picks the same task (still in backlog/in-progress/),
      # so the retry resumes it rather than skipping it.
      PENDING_RECOVERY_TASK="$CONSUMED_RECOVERY_TASK"
      continue
    fi

    # A long outage: wait it out in 30-min steps (bounded by --max-usage-wait-minutes).
    if [ "$fail_transient" -eq 1 ] && [ "$SELF_HEAL" -eq 1 ] && \
       wait_for_limit_reset "$log_file" 1800 "API still failing after $MAX_TRANSIENT_API_RETRIES retries"; then
      TRANSIENT_API_RETRIES=0
      PENDING_RECOVERY_TASK="$CONSUMED_RECOVERY_TASK"
      continue
    fi

    if [ "$SELF_HEAL" -eq 1 ] && [ "$IS_AUDIT" -eq 1 ]; then
      echo "  Post-hoc audit of task $TASK_NNN ended with exit $exit_code — continuing without it (see $log_file)."
      PENDING_AUDIT_TASK=""
      continue
    fi

    # Crash, closed window, watchdog kill: the task's claim died with the process,
    # so a recovery iteration can resume it. Only bad credentials stop the loop.
    if [ "$SELF_HEAL" -eq 1 ] && [ "$fail_kind" != "auth" ]; then
      if [ "$exit_code" -eq 124 ]; then fail_event="WATCHDOG_KILL"; else fail_event="ITERATION_FAILED"; fi
      fail_details="exit code $exit_code"
      [ "$fail_reason" != "unclassified non-zero exit" ] && fail_details="$fail_details - $fail_reason"
      [ "$exit_code" -eq 124 ] && [ -n "${WATCHDOG_REASON:-}" ] && fail_details="$fail_details - $WATCHDOG_REASON"
      TRANSIENT_API_RETRIES=0
      self_heal "$PICKED_TASK" "$TASK_NNN" "$fail_event" "$fail_details" "$log_file"
      [ "$SELF_HEAL_OUTCOME" = "stop" ] && break
      continue
    fi

    if [ "$fail_reason" = "unclassified non-zero exit" ]; then
      STOP_REASON="claude exited non-zero ($exit_code) on iteration $i (see $log_file)"
    elif [ "$fail_transient" -eq 1 ]; then
      STOP_REASON="claude stopped after $TRANSIENT_API_RETRIES retries: $fail_reason (exit $exit_code, iteration $i, see $log_file)"
    else
      STOP_REASON="claude stopped: $fail_reason (exit $exit_code, iteration $i, see $log_file)"
    fi
    collect_iteration_report "$log_file"
    notify --event "CLI_ERROR" --task "$TASK_TITLE_NOTIF" --url "$TASK_URL_NOTIF" \
      --tokens "$REPORT_SUMMARY" --per-model "$REPORT_PER_MODEL" \
      --breakdown "$REPORT_BREAKDOWN" --cumulative "$REPORT_CUMULATIVE" \
      --details "$STOP_REASON" \
      --progress "$TASK_PROGRESS_NOTIF" --duration "$ITER_DURATION"
    break
  fi

  # Reaching here means the iteration ran to completion, so the streaks reset:
  # MAX_TRANSIENT_API_RETRIES counts CONSECUTIVE blips, not lifetime ones.
  TRANSIENT_API_RETRIES=0
  QUOTA_RETRIES=0
  USAGE_WAITED_SEC=0

  # Post-hoc audit iteration: its outcome is reported, never gated on — the task is
  # already DONE and committed.
  if [ "$IS_AUDIT" -eq 1 ]; then
    audit_line="$(last_result_text "$log_file" | grep -o 'AUDIT_DONE.*' | head -n 1)"
    [ -n "$audit_line" ] || audit_line="audit ended without an AUDIT_DONE line — see $log_file"
    echo "  Post-hoc audit of task $TASK_NNN: $audit_line"
    collect_iteration_report "$log_file"
    notify --event "AUDIT_DONE" --task "$TASK_TITLE_NOTIF" --url "$TASK_URL_NOTIF" \
      --tokens "$REPORT_SUMMARY" --per-model "$REPORT_PER_MODEL" \
      --breakdown "$REPORT_BREAKDOWN" --cumulative "$REPORT_CUMULATIVE" \
      --details "$audit_line" --progress "$TASK_PROGRESS_NOTIF" --duration "$ITER_DURATION"
    PENDING_AUDIT_TASK=""
    continue
  fi

  if is_blocked "$log_file"; then
    STOP_REASON="Blocker sentinel detected on iteration $i (see $log_file)"

    # Classify from the SAME final result line is_blocked matched. The full log
    # always contains every token name (the injected prompt echoes them in the
    # conversation JSON), so a whole-log grep would mislabel the event as the
    # first token checked. Keep the token order in lockstep with the .ps1.
    result_line="$(grep '"type":"result"' "$log_file" | tail -n1)"
    block_event=""
    block_details=""
    for tok in LOOP_BUSY TASK_BUSY RESUME_CONFLICT EDITOR_REQUIRED COMPILE_BLOCKED PREFLIGHT_BLOCKED REVIEW_BLOCKED RUNTIME_BLOCKED VERIFY_BLOCKED NO_CHANGES BASE_MERGE_CONFLICT BASE_UNKNOWN; do
      if printf '%s' "$result_line" | grep -q "$tok"; then
        block_event="$tok"
        block_details="$(printf '%s' "$result_line" | grep -o "${tok}[^\"]*" | head -n 1)"
        break
      fi
    done
    if [ -z "$block_event" ]; then
      # "manual intervention required" without a named token: a decision it would
      # not take, an environment fault.
      block_event="MANUAL_INTERVENTION"
      block_details="$(printf '%s' "$result_line" | grep -io "manual intervention[^\"]*" | head -n 1)"
      [ -z "$block_details" ] && block_details="Automation paused. Manual intervention required."
    fi

    if [ "$SELF_HEAL" -eq 1 ]; then
      STOP_REASON=""
      case "$block_event" in
        LOOP_BUSY|TASK_BUSY)
          # Nothing was changed; the next pick waits for the owning session.
          echo "  $block_event inside the iteration — the next pick waits for the owning session."
          continue ;;
        EDITOR_REQUIRED)
          if [ "$MODE" != "worktree" ] && editor_recovery; then
            notify --event "EDITOR_RECOVERY" --task "$TASK_TITLE_NOTIF" --url "$TASK_URL_NOTIF" \
              --details "Every remaining task needs a live Unity Editor; the loop (re)started this project's Editor and resumes." \
              --progress "$TASK_PROGRESS_NOTIF"
            continue
          fi
          if [ "$MODE" = "worktree" ]; then
            STOP_REASON="EDITOR_REQUIRED — every remaining task needs a live Unity Editor, which worktree mode can never provide. Relaunch with --mode current."
          else
            STOP_REASON="EDITOR_REQUIRED — every remaining task needs a live Unity Editor and $MAX_EDITOR_RECOVERIES Editor (re)start(s) did not bring one up (see $log_file)"
          fi
          collect_iteration_report "$log_file"
          notify --event "EDITOR_REQUIRED" --task "$TASK_TITLE_NOTIF" --url "$TASK_URL_NOTIF" \
            --tokens "$REPORT_SUMMARY" --per-model "$REPORT_PER_MODEL" \
            --breakdown "$REPORT_BREAKDOWN" --cumulative "$REPORT_CUMULATIVE" \
            --details "$STOP_REASON" --progress "$TASK_PROGRESS_NOTIF" --duration "$ITER_DURATION"
          break ;;
        RESUME_CONFLICT)
          # The partial work lives in another checkout/branch: nothing to fix from
          # here, so park at once and leave that work where it is.
          self_heal "$PICKED_TASK" "$TASK_NNN" "$block_event" "$block_details" "$log_file" park-now --keep-work ;;
        MANUAL_INTERVENTION)
          if printf '%s' "$block_details" | grep -qi 'push of .* failed'; then
            # STEP 9e push failure: the task is committed, local only. A diverged branch
            # is never pulled / rebased / forced by an agent (push-in-session section 4),
            # and every later task would stack one more unpushed commit onto it — so this
            # one still stops the loop, self-heal or not.
            STOP_REASON="Push failed on iteration $i — $block_details (see $log_file)"
            echo "  ⚠️ $STOP_REASON" >&2
            collect_iteration_report "$log_file"
            notify --event "MANUAL_INTERVENTION" --task "$TASK_TITLE_NOTIF" --url "$TASK_URL_NOTIF" \
              --tokens "$REPORT_SUMMARY" --per-model "$REPORT_PER_MODEL" \
              --breakdown "$REPORT_BREAKDOWN" --cumulative "$REPORT_CUMULATIVE" \
              --details "$STOP_REASON" --progress "$TASK_PROGRESS_NOTIF" --duration "$ITER_DURATION"
            break
          fi
          self_heal "$PICKED_TASK" "$TASK_NNN" "$block_event" "$block_details" "$log_file" ;;
        *)
          self_heal "$PICKED_TASK" "$TASK_NNN" "$block_event" "$block_details" "$log_file" ;;
      esac
      [ "$SELF_HEAL_OUTCOME" = "stop" ] && break
      continue
    fi

    collect_iteration_report "$log_file"
    notify --event "$block_event" --task "$TASK_TITLE_NOTIF" --url "$TASK_URL_NOTIF" \
      --tokens "$REPORT_SUMMARY" --per-model "$REPORT_PER_MODEL" \
      --breakdown "$REPORT_BREAKDOWN" --cumulative "$REPORT_CUMULATIVE" \
      --details "$block_details" \
      --progress "$TASK_PROGRESS_NOTIF" --duration "$ITER_DURATION"
    break
  fi

  # Multi-iteration task (run-backlog SKILL.md 1e): the iteration saved its
  # progress and left the task in backlog/in-progress/ on purpose. Continue — the
  # next iteration's pick returns the same task and resumes it. Only consecutive
  # checkpoints of the SAME task count toward MAX_CHECKPOINTS.
  if is_checkpointed "$log_file" && task_still_in_progress "$TASK_BASE_NOTIF"; then
    if [ "$CHECKPOINT_TASK" = "$TASK_BASE_NOTIF" ]; then
      CHECKPOINT_STREAK=$((CHECKPOINT_STREAK + 1))
    else
      CHECKPOINT_TASK="$TASK_BASE_NOTIF"
      CHECKPOINT_STREAK=1
    fi
    result_line="$(grep '"type":"result"' "$log_file" | tail -n1)"
    ckpt_details="$(printf '%s' "$result_line" | grep -o 'TASK_CHECKPOINTED[^"]*' | head -n 1 | sed 's/\\n.*//')"
    collect_iteration_report "$log_file"
    if [ "$CHECKPOINT_STREAK" -ge "$MAX_CHECKPOINTS" ] && [ "$SELF_HEAL" -eq 1 ]; then
      CHECKPOINT_TASK=""
      CHECKPOINT_STREAK=0
      self_heal "$PICKED_TASK" "$TASK_NNN" "CHECKPOINT_LIMIT" "$TASK_BASE_NOTIF checkpointed $MAX_CHECKPOINTS consecutive iterations without finishing" "$log_file" park-now
      [ "$SELF_HEAL_OUTCOME" = "stop" ] && break
      continue
    fi
    if [ "$CHECKPOINT_STREAK" -ge "$MAX_CHECKPOINTS" ]; then
      STOP_REASON="CHECKPOINT_LIMIT — $TASK_BASE_NOTIF ended $CHECKPOINT_STREAK consecutive iterations with TASK_CHECKPOINTED (cap $MAX_CHECKPOINTS). Read its resume notes, then relaunch (see $log_file)"
      echo "  ⚠️ $STOP_REASON" >&2
      notify --event "CHECKPOINT_LIMIT" --task "$TASK_TITLE_NOTIF" --url "$TASK_URL_NOTIF" \
        --tokens "$REPORT_SUMMARY" --per-model "$REPORT_PER_MODEL" \
        --breakdown "$REPORT_BREAKDOWN" --cumulative "$REPORT_CUMULATIVE" \
        --details "$STOP_REASON" \
        --progress "$TASK_PROGRESS_NOTIF" --duration "$ITER_DURATION"
      break
    fi
    echo "  Checkpointed ($CHECKPOINT_STREAK/$MAX_CHECKPOINTS): ${ckpt_details:-TASK_CHECKPOINTED} — resuming next iteration."
    notify --event "TASK_CHECKPOINTED" --task "$TASK_TITLE_NOTIF" --url "$TASK_URL_NOTIF" \
      --tokens "$REPORT_SUMMARY" --per-model "$REPORT_PER_MODEL" \
      --breakdown "$REPORT_BREAKDOWN" --cumulative "$REPORT_CUMULATIVE" \
      --details "${ckpt_details:-TASK_CHECKPOINTED} (checkpoint $CHECKPOINT_STREAK/$MAX_CHECKPOINTS)" \
      --progress "$TASK_PROGRESS_NOTIF" --duration "$ITER_DURATION"
    continue
  fi
  CHECKPOINT_TASK=""
  CHECKPOINT_STREAK=0

  # Deterministic outcome check: exit 0 + no sentinel, but the picked task is
  # still in backlog/in-progress/ → the model ended its turn without finishing
  # (usually to "wait" for something). Self-heal sends a recovery iteration.
  if task_still_in_progress "$TASK_BASE_NOTIF" && [ "$SELF_HEAL" -eq 1 ]; then
    self_heal "$PICKED_TASK" "$TASK_NNN" "SILENT_END" "clean exit, no stop token, task still in backlog/in-progress/" "$log_file"
    [ "$SELF_HEAL_OUTCOME" = "stop" ] && break
    continue
  fi
  if task_still_in_progress "$TASK_BASE_NOTIF"; then
    collect_iteration_report "$log_file"
    if [ "$SILENT_RETRY_TASK" != "$TASK_BASE_NOTIF" ]; then
      SILENT_RETRY_TASK="$TASK_BASE_NOTIF"
      retry_reason="Silent end on iteration $i: clean exit + no blocker sentinel, $TASK_BASE_NOTIF still in backlog/in-progress/ — resuming it once (see $log_file)"
      echo "  ⚠️ $retry_reason" >&2
      notify --event "SILENT_RETRY" --task "$TASK_TITLE_NOTIF" --url "$TASK_URL_NOTIF" \
        --tokens "$REPORT_SUMMARY" --per-model "$REPORT_PER_MODEL" \
        --breakdown "$REPORT_BREAKDOWN" --cumulative "$REPORT_CUMULATIVE" \
        --details "$retry_reason" \
        --progress "$TASK_PROGRESS_NOTIF" --duration "$ITER_DURATION"
      continue
    fi
    STOP_REASON="Silent failure on iteration $i: clean exit + no blocker sentinel, but $TASK_BASE_NOTIF is still in backlog/in-progress/ after one automatic resume (see $log_file)"
    echo "  ⚠️ $STOP_REASON" >&2
    notify --event "SILENT_FAIL" --task "$TASK_TITLE_NOTIF" --url "$TASK_URL_NOTIF" \
      --tokens "$REPORT_SUMMARY" --per-model "$REPORT_PER_MODEL" \
      --breakdown "$REPORT_BREAKDOWN" --cumulative "$REPORT_CUMULATIVE" \
      --details "$STOP_REASON" \
      --progress "$TASK_PROGRESS_NOTIF" --duration "$ITER_DURATION"
    break
  fi

  # Gate receipts: an S/M/L task that reached DONE must show the tier's
  # mandatory reviewer spawns in the stream-json log ("gates ran" must never
  # be only the model's own claim).
  if [ -n "$TASK_TIER_NOTIF" ] && task_reached_done "$TASK_BASE_NOTIF"; then
    missing_receipts="$(missing_gate_receipts "$log_file" "$TASK_TIER_NOTIF")"
    # A task closed as "already satisfied" (no diff -> no commit) has nothing for a
    # code reviewer to read; its qa-verifier receipt still counts.
    # The commit message carries no trailer (push-in-session style): backlog-ops finds
    # the task's commit from its run record (shipped sha / recorded commit tree). Only
    # source "no-commit" (closed as already satisfied) excuses the code reviewer — an
    # unknown lookup keeps the requirement.
    task_commit=""
    commit_source=""
    if [ -n "$TASK_NNN" ]; then
      commit_json="$(cd "$WORK_DIR" && python3 "$OPS" task-commit "$TASK_NNN" 2>/dev/null)"
      task_commit="$(json_field "$commit_json" commit)"
      commit_source="$(json_field "$commit_json" source)"
    fi
    if [ "$commit_source" = "no-commit" ]; then
      missing_receipts="$(printf '%s' "$missing_receipts" | tr ' ' '\n' | grep -v '^code-reviewer$' | tr '\n' ' ' | sed 's/ *$//')"
    fi
    if [ -n "$missing_receipts" ] && [ "$SELF_HEAL" -eq 1 ] && [ -n "$task_commit" ]; then
      PENDING_AUDIT_TASK="$TASK_BASE_NOTIF"
      PENDING_AUDIT_NNN="$TASK_NNN"
      PENDING_AUDIT_MISSING="$missing_receipts"
      PENDING_AUDIT_COMMIT="$task_commit"
      audit_msg="$TASK_BASE_NOTIF (tier $TASK_TIER_NOTIF) reached DONE as $task_commit but the log has no Agent spawn for: $missing_receipts. A post-hoc audit iteration runs them next; the loop has NOT stopped."
      echo "  ⚠️ Gate receipt missing — $audit_msg"
      collect_iteration_report "$log_file"
      notify --event "GATE_RECEIPT_MISSING" --task "$TASK_TITLE_NOTIF" --url "$TASK_URL_NOTIF" \
        --tokens "$REPORT_SUMMARY" --per-model "$REPORT_PER_MODEL" \
        --breakdown "$REPORT_BREAKDOWN" --cumulative "$REPORT_CUMULATIVE" \
        --details "$audit_msg" --progress "$TASK_PROGRESS_NOTIF" --duration "$ITER_DURATION"
      continue
    fi
    if [ -n "$missing_receipts" ]; then
      STOP_REASON="Gate receipt missing on iteration $i: $TASK_BASE_NOTIF (tier $TASK_TIER_NOTIF) reached DONE but the log has no Agent spawn for: $missing_receipts (see $log_file)"
      echo "  ⚠️ $STOP_REASON" >&2
      collect_iteration_report "$log_file"
      notify --event "GATE_RECEIPT_MISSING" --task "$TASK_TITLE_NOTIF" --url "$TASK_URL_NOTIF" \
        --tokens "$REPORT_SUMMARY" --per-model "$REPORT_PER_MODEL" \
        --breakdown "$REPORT_BREAKDOWN" --cumulative "$REPORT_CUMULATIVE" \
        --details "$STOP_REASON" \
        --progress "$TASK_PROGRESS_NOTIF" --duration "$ITER_DURATION"
      break
    fi
  fi

  # Task passed all gates this iteration — notify success.
  healed_note=""
  if [ -n "$CONSUMED_RECOVERY_TASK" ]; then
    RECOVERED_TASKS=$((RECOVERED_TASKS + 1))
    healed_note="
Finished by self-heal recovery iteration $PENDING_RECOVERY_ATTEMPT/$MAX_RECOVERIES (after $PENDING_RECOVERY_EVENT)."
    echo "  ✅ Self-heal worked: task $TASK_NNN finished in recovery iteration $PENDING_RECOVERY_ATTEMPT."
  fi
  read -r TODO_NEW IP_NEW <<<"$(backlog_counts)"
  DONE_NEW=$(find "$BACKLOG_ROOT/done" -name "*.md" 2>/dev/null | wc -l | xargs)
  TOTAL_NEW=$((TODO_NEW + IP_NEW + DONE_NEW))

  collect_iteration_report "$log_file"
  notify --event "TASK_COMPLETED" --task "$TASK_TITLE_NOTIF" --url "$TASK_URL_NOTIF" \
    --tokens "$REPORT_SUMMARY" --per-model "$REPORT_PER_MODEL" \
    --breakdown "$REPORT_BREAKDOWN" --cumulative "$REPORT_CUMULATIVE" \
    --details "Progress: Task $DONE_NEW of $TOTAL_NEW completed successfully.
Committed to $AGENT_BRANCH (pushed if the repo has a remote). Ready for manual verify + merge into the base branch.$healed_note" \
    --progress "$DONE_NEW/$TOTAL_NEW" --duration "$ITER_DURATION"
done

[ -z "$STOP_REASON" ] && STOP_REASON="Reached MaxIterations ($MAX_ITERATIONS)"

# --inline retitled this window per task; hand it back so the finished window is
# not still labelled with whatever task ran last.
set_window_title "$(format_window_title "")"

echo
echo "=========================================="
echo "  Loop stopped: $STOP_REASON"
echo "  Iterations run: $i"
if [ "$SELF_HEAL" -eq 1 ]; then
  echo "  Self-heal: $(printf '%s' "$RECOVERY_COUNTS" | grep -c . ) task(s) needed recovery, $RECOVERED_TASKS finished through it, $(printf '%s' "$PARKED_TASKS" | grep -c . ) parked"
  printf '%s\n' "$PARKED_TASKS" | awk -F'\t' 'NF { printf "    parked: %s - %s (partial work: refs/backlog/parked/<NNN>; reason appended to the task file)\n", $1, $2 }'
fi
echo "=========================================="
