#!/usr/bin/env bash
# Run Backlog Loop (macOS/Linux), per-task new Terminal window.
#
# This is the macOS/Linux twin of run-backlog-loop.ps1 (Windows). It is the
# CONTROLLER: for each iteration it spawns a SEPARATE Terminal window that runs
# exactly one /run-backlog task, then waits (via a flag file) for that window to
# finish before spawning the next. A failed task keeps its window open so you can
# read the error. Stops when the backlog is empty, a blocker sentinel is printed,
# the CLI exits non-zero, an inactivity/time watchdog fires, a deterministic
# outcome/receipt check fails (task still in in-progress/ despite a "clean" exit,
# or a DONE S/M/L task shows no reviewer Agent spawns in the log), or
# MaxIterations is reached.
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
#   --xs-model/--xs-effort   Override XS profile (default: sonnet/medium).
#   --s-model/--s-effort     Override S profile  (default: sonnet/high).
#   --m-model/--m-effort     Override M profile  (default: opus/high).
#   --l-model/--l-effort     Override L profile  (default: opus/xhigh).
#                            (quality-first default: M/L run on opus to match the
#                             opus code/security reviewers; XS/S run on sonnet to save cost.
#                             There is NO auto-escalation: if an M task hits REVIEW_BLOCKED
#                             the loop stops — fix and rerun.)
#   --max-iterations <n>     Max task iterations (default: 100).
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
XS_MODEL="sonnet"
XS_EFFORT="medium"
S_MODEL="sonnet"
S_EFFORT="high"
M_MODEL="opus"
M_EFFORT="high"
L_MODEL="opus"
L_EFFORT="xhigh"
MAX_ITERATIONS=100
# Consecutive transient API blips (transport break / 529) tolerated before giving
# up. Fatal classes (auth, exhausted usage) ignore this and stop at once - see
# claude_failure_class().
MAX_TRANSIENT_API_RETRIES=3
TRANSIENT_API_RETRIES=0
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
  # Current mode shares the checkout with the developer, and the agent stages with
  # `git add -A` — anything uncommitted right now lands in the first task's commit.
  # Warn, do not block: owning that risk is the point of choosing current.
  DIRTY_COUNT="$(git status --porcelain 2>/dev/null | grep -c . || true)"
  if [ "${DIRTY_COUNT:-0}" -gt 0 ]; then
    echo "WARNING: $DIRTY_COUNT uncommitted change(s) in this checkout. The agent stages with"
    echo "         'git add -A', so they will be swept into the first task's commit."
    echo "         Commit or stash them first if that is not what you want."
  fi
fi
export AGENT_WORKDIR="$WORK_DIR"
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

# --- per-task prompt ------------------------------------------------------------
read -r -d '' PROMPT <<'EOF'
Execute exactly one iteration of this project's run-backlog workflow.

Required contract:
1. Read .claude/skills/run-backlog/SKILL.md before changing any files.
2. Follow that skill exactly for one iteration only.
3. Read CLAUDE.md, .claude/rules/*, the selected task file, and only the relevant code the workflow requests.
4. Spawn the code-reviewer, performance-reviewer (when perf-sensitive), security-auditor (when sensitive), and qa-verifier subagents per the skill spec using the Agent tool.
5. Print exactly these tokens when blocked: COMPILE_BLOCKED, PREFLIGHT_BLOCKED, REVIEW_BLOCKED, VERIFY_BLOCKED, RUNTIME_BLOCKED, EDITOR_REQUIRED, NO_CHANGES, BASE_MERGE_CONFLICT, or "manual intervention required". (DEFERRED is NOT a block — end the iteration normally. Starting on an agent branch is allowed — never print BASE_UNKNOWN.)
6. Commit to the work branch (env AGENT_BRANCH) only when the skill marks the task DONE, and push it only when the repo has an origin remote (the skill's HAS_REMOTE probe decides). Do not create a PR.

Environment for this iteration (STEP 2 of the skill reads these):
- AGENT_MODE=$MODE
- AGENT_BRANCH=$AGENT_BRANCH
- AGENT_BASE_BRANCH=$LOOP_BASE_BRANCH
- AGENT_BACKLOG_ROOT=$BACKLOG_ROOT
7. Do not ask for confirmation. Work autonomously inside this repository.
8. Use English for all output, progress messages, reports, and commit messages.

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
  printf '%s' "$result_line" | grep -Eq 'COMPILE_BLOCKED|PREFLIGHT_BLOCKED|REVIEW_BLOCKED|VERIFY_BLOCKED|RUNTIME_BLOCKED|EDITOR_REQUIRED|NO_CHANGES|BASE_UNKNOWN|BASE_MERGE_CONFLICT|manual intervention required'
}

# Classify a non-zero claude iteration: transient (retry) vs fatal (stop now).
# Grounded in the failure classes actually seen in logs/backlog-loop/:
#
#   terminal_reason=api_error  "Connection closed mid-response"   transport break -> retry
#   result="API Error: Overloaded"                                529             -> retry
#   result="Failed to authenticate. API Error: 401 ..."                           -> STOP
#   result="You're out of extra usage - resets <time>"                            -> STOP
#
# Retrying an exhausted quota burns what is left against a wall, and retrying bad
# credentials cannot fix them, so the fatal classes are matched FIRST (they can
# still carry an api_error terminal_reason). Prints the class reason on stdout and
# returns 0 only when the failure is transient. Keep in lockstep with
# Get-ClaudeFailureClass in run-backlog-loop-core.ps1.
claude_failure_class() {
  local log="$1" result_line
  [ -f "$log" ] || { printf 'unclassified non-zero exit'; return 1; }
  result_line="$(grep '"type":"result"' "$log" | tail -n1)"
  [ -n "$result_line" ] || { printf 'unclassified non-zero exit'; return 1; }

  if printf '%s' "$result_line" | grep -Eq "out of extra usage|usage limit|credit balance|Insufficient credit"; then
    printf 'usage/credit exhausted - retrying would burn quota against a wall'; return 1
  fi
  if printf '%s' "$result_line" | grep -Eq 'Invalid authentication credentials|Failed to authenticate'; then
    printf 'authentication failure - a retry cannot fix credentials'; return 1
  fi
  if printf '%s' "$result_line" | grep -Eq '"terminal_reason":"api_error"'; then
    printf 'API/transport error'; return 0
  fi
  if printf '%s' "$result_line" | grep -Eq 'Overloaded|overloaded_error'; then
    printf 'API overloaded (529)'; return 0
  fi
  printf 'unclassified non-zero exit'; return 1
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
export AGENT_BASE_BRANCH=$(printf '%q' "$LOOP_BASE_BRANCH")
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

  if [ "$TODO" -eq 0 ] && [ "$IP" -eq 0 ]; then
    STOP_REASON="Backlog empty (no TODO, no IN PROGRESS)"
    # No iteration log to analyze here, but the loop-so-far total is exactly what
    # closes the run out ("everything done — this is what it cost").
    notify --event "BACKLOG_EMPTY" --task "N/A" \
      --details "All backlog tasks have been processed." \
      --cumulative "$(format_loop_cumulative)" \
      --progress "$DONE_BEFORE/$DONE_BEFORE"
    break
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

  if [ "$AUTO_MODEL_BY_TIER" -eq 1 ]; then
    next_task_profile
  else
    TASK_TIER=""; TASK_STATE=""
    SELECTED_MODEL="$MODEL"; SELECTED_EFFORT="$EFFORT"; SELECTED_THINKING_TOKENS="$THINKING_TOKENS"
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
  printf '%s\n' "$PROMPT" > "$prompt_file"
  rm -f "$flag_file"

  ITER_START=$(date +%s)

  if [ "$INLINE" -eq 1 ]; then
    # Same-window execution.
    if [ "${SELECTED_THINKING_TOKENS:-0}" -gt 0 ] 2>/dev/null; then
      export MAX_THINKING_TOKENS="$SELECTED_THINKING_TOKENS"
    else
      unset MAX_THINKING_TOKENS
    fi
    if [ "$HAS_RENDER" -eq 1 ]; then
      printf '%s\n' "$PROMPT" | claude "${CLI_ARGS[@]}" 2>&1 | tee "$log_file" | python3 "$RENDER" --provider claude --effort "${SELECTED_EFFORT:-default}"
    else
      printf '%s\n' "$PROMPT" | claude "${CLI_ARGS[@]}" 2>&1 | tee "$log_file"
    fi
    exit_code="${PIPESTATUS[1]}"
  else
    # New Terminal window per task (macOS via osascript).
    write_runner "$i" "$log_file" "$flag_file" "$prompt_file" "$runner_file" "$pid_file"
    win_id="$(osascript -e "tell application \"Terminal\"" -e "do script \"bash '$runner_file'\"" -e "return id of front window" -e "end tell" 2>/dev/null)" \
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

      # 900s (15 min) of absolute inactivity, or 10800s (180 min / 3h) max execution time.
      if [ "$inactive_seconds" -ge 900 ] || [ "$elapsed" -ge 10800 ]; then
        if [ "$inactive_seconds" -ge 900 ]; then
          STOP_REASON="Task hung or stopped due to token exhaustion/inactivity (no log updates for 15m)"
        else
          STOP_REASON="Task timed out (exceeded 180m limit)"
        fi
        echo "  ⚠️ $STOP_REASON. Killing task window so it stops consuming tokens." >&2
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

  if [ "$exit_code" -ne 0 ]; then
    fail_reason="$(claude_failure_class "$log_file")" && fail_transient=1 || fail_transient=0

    if [ "$fail_transient" -eq 1 ] && [ "$TRANSIENT_API_RETRIES" -lt "$MAX_TRANSIENT_API_RETRIES" ]; then
      TRANSIENT_API_RETRIES=$((TRANSIENT_API_RETRIES + 1))
      # 30s, 60s, 120s - a dropped stream usually clears on the first retry; the
      # backoff matters for an overload, which needs the far side to drain.
      backoff=$((30 * (1 << (TRANSIENT_API_RETRIES - 1))))
      retry_msg="Transient API failure on iteration $i ($fail_reason). Retry $TRANSIENT_API_RETRIES/$MAX_TRANSIENT_API_RETRIES in ${backoff}s."
      log "$retry_msg"
      notify --event "API_RETRY" --task "$TASK_TITLE_NOTIF" --url "$TASK_URL_NOTIF" \
        --details "$retry_msg" --progress "$TASK_PROGRESS_NOTIF" --duration "$ITER_DURATION"
      sleep "$backoff"
      # The next iteration re-picks the same task (still in backlog/in-progress/),
      # so the retry resumes it rather than skipping it.
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

  # Reaching here means the iteration ran to completion, so the streak resets:
  # MAX_TRANSIENT_API_RETRIES counts CONSECUTIVE blips, not lifetime ones.
  TRANSIENT_API_RETRIES=0

  if is_blocked "$log_file"; then
    STOP_REASON="Blocker sentinel detected on iteration $i (see $log_file)"

    # Classify from the SAME final result line is_blocked matched. The full log
    # always contains every token name (the injected prompt echoes them in the
    # conversation JSON), so a whole-log grep would mislabel the event as the
    # first token checked. Keep the token order in lockstep with the .ps1.
    result_line="$(grep '"type":"result"' "$log_file" | tail -n1)"
    block_event=""
    block_details=""
    for tok in EDITOR_REQUIRED COMPILE_BLOCKED PREFLIGHT_BLOCKED REVIEW_BLOCKED RUNTIME_BLOCKED VERIFY_BLOCKED NO_CHANGES BASE_MERGE_CONFLICT BASE_UNKNOWN; do
      if printf '%s' "$result_line" | grep -q "$tok"; then
        block_event="$tok"
        block_details="$(printf '%s' "$result_line" | grep -o "${tok}[^\"]*" | head -n 1)"
        break
      fi
    done
    if [ -z "$block_event" ]; then
      block_event="VERIFY_BLOCKED"
      block_details="$(printf '%s' "$result_line" | grep -io "manual intervention[^\"]*" | head -n 1)"
      [ -z "$block_details" ] && block_details="Automation paused. Manual intervention required."
    fi

    collect_iteration_report "$log_file"
    notify --event "$block_event" --task "$TASK_TITLE_NOTIF" --url "$TASK_URL_NOTIF" \
      --tokens "$REPORT_SUMMARY" --per-model "$REPORT_PER_MODEL" \
      --breakdown "$REPORT_BREAKDOWN" --cumulative "$REPORT_CUMULATIVE" \
      --details "$block_details" \
      --progress "$TASK_PROGRESS_NOTIF" --duration "$ITER_DURATION"
    break
  fi

  # Deterministic outcome check: exit 0 + no sentinel, but the picked task is
  # still in backlog/in-progress/ → the model stopped without printing its
  # blocker token. Stop instead of re-running the same task forever.
  if task_still_in_progress "$TASK_BASE_NOTIF"; then
    STOP_REASON="Silent failure on iteration $i: clean exit + no blocker sentinel, but $TASK_BASE_NOTIF is still in backlog/in-progress/ (see $log_file)"
    echo "  ⚠️ $STOP_REASON" >&2
    collect_iteration_report "$log_file"
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
  read -r TODO_NEW IP_NEW <<<"$(backlog_counts)"
  DONE_NEW=$(find "$BACKLOG_ROOT/done" -name "*.md" 2>/dev/null | wc -l | xargs)
  TOTAL_NEW=$((TODO_NEW + IP_NEW + DONE_NEW))

  collect_iteration_report "$log_file"
  notify --event "TASK_COMPLETED" --task "$TASK_TITLE_NOTIF" --url "$TASK_URL_NOTIF" \
    --tokens "$REPORT_SUMMARY" --per-model "$REPORT_PER_MODEL" \
    --breakdown "$REPORT_BREAKDOWN" --cumulative "$REPORT_CUMULATIVE" \
    --details "Progress: Task $DONE_NEW of $TOTAL_NEW completed successfully.
Committed to $AGENT_BRANCH (pushed if the repo has a remote). Ready for manual verify + merge into the base branch." \
    --progress "$DONE_NEW/$TOTAL_NEW" --duration "$ITER_DURATION"
done

[ -z "$STOP_REASON" ] && STOP_REASON="Reached MaxIterations ($MAX_ITERATIONS)"

echo
echo "=========================================="
echo "  Loop stopped: $STOP_REASON"
echo "  Iterations run: $i"
echo "=========================================="
