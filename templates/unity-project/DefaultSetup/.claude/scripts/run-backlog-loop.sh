#!/usr/bin/env bash
# Merge Two — Run Backlog Loop (macOS), per-task new Terminal window.
#
# This script is the CONTROLLER. For each iteration it spawns a SEPARATE Terminal
# window that runs exactly one /run-backlog task, then waits (via a flag file) for
# that window to finish before spawning the next. A failed task keeps its window
# open so you can read the error. Stops when the backlog is empty, a blocker
# sentinel is printed, the CLI exits non-zero, or MaxIterations is reached.
#
# The run-backlog skill itself handles git (branch agent/dev) per its own spec.
#
# Usage:
#   .claude/scripts/run-backlog-loop.sh
#   .claude/scripts/run-backlog-loop.sh --model claude-opus-4-8 --effort xhigh --max-iterations 5
#   .claude/scripts/run-backlog-loop.sh --inline        # run in THIS window (no new windows)
#
# Options:
#   --model <id>             Claude model id (default: empty = CLI default).
#   --effort <level>         Reasoning effort: low|medium|high|xhigh (default: empty = CLI default).
#   --max-iterations <n>     Max task iterations (default: 100).
#   --thinking-tokens <n>    MAX_THINKING_TOKENS for orchestrator + subagents (default: 10000; 0 = off).
#   --inline                 Run each task in the current window instead of a new one.
#   --no-skip-permissions    Do NOT pass --dangerously-skip-permissions (will prompt).
#   -h | --help              Show this help.

set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$REPO_ROOT" || { echo "Cannot cd to repo root: $REPO_ROOT" >&2; exit 1; }

# --- defaults -------------------------------------------------------------------
MODEL=""
EFFORT=""
MAX_ITERATIONS=100
THINKING_TOKENS=10000
SKIP_PERMISSIONS=1
INLINE=0
LOG_DIR="logs/backlog-loop"

# --- parse args -----------------------------------------------------------------
while [ $# -gt 0 ]; do
  case "$1" in
    --model)            MODEL="${2:-}"; shift 2 ;;
    --effort)           EFFORT="${2:-}"; shift 2 ;;
    --max-iterations)   MAX_ITERATIONS="${2:-}"; shift 2 ;;
    --thinking-tokens)  THINKING_TOKENS="${2:-}"; shift 2 ;;
    --inline)           INLINE=1; shift ;;
    --no-skip-permissions) SKIP_PERMISSIONS=0; shift ;;
    -h|--help)
      sed -n '2,24p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
      exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

command -v claude >/dev/null 2>&1 || { echo "ERROR: 'claude' CLI not found in PATH." >&2; exit 1; }
mkdir -p "$LOG_DIR"
LOG_DIR_ABS="$(cd "$LOG_DIR" && pwd)"

# Optional pretty renderer for the stream-json firehose (raw JSON still goes to the log).
RENDER="$SCRIPT_DIR/stream-render.py"
if command -v python3 >/dev/null 2>&1 && [ -f "$RENDER" ]; then HAS_RENDER=1; else HAS_RENDER=0; fi

# --- per-task prompt ------------------------------------------------------------
read -r -d '' PROMPT <<'EOF'
Execute exactly one iteration of the Merge Two run-backlog workflow.

Required contract:
1. Read .agents/skills/run-backlog/SKILL.md before changing any files.
2. Follow that skill exactly for one iteration only.
3. Read CLAUDE.md, .agents/rules/*, the selected task file, and only the relevant code the workflow requests.
4. Spawn the code-reviewer, performance-reviewer, security-auditor, and qa-verifier subagents per the skill spec using the Agent tool.
5. Print exactly these tokens when blocked: PREFLIGHT_BLOCKED, REVIEW_BLOCKED, VERIFY_BLOCKED, or "manual intervention required".
6. Commit/push to agent/dev only when the skill marks the task DONE. Do not create a PR.
7. Do not ask for confirmation. Work autonomously inside this repository.
8. Use English for all output, progress messages, reports, and commit messages.

Start now.
EOF

# Returns 0 (blocked) only if the FINAL {"type":"result"} event's line contains a
# blocker sentinel — mirrors BlazeSurvivor's Test-Blocked. Grepping the whole log
# false-positives because the prompt echoes sentinel names in the conversation JSON.
is_blocked() {
  local log="$1" result_line
  [ -f "$log" ] || return 1
  result_line="$(grep '"type":"result"' "$log" | tail -n1)"
  [ -n "$result_line" ] || return 1
  printf '%s' "$result_line" | grep -Eq 'PREFLIGHT_BLOCKED|REVIEW_BLOCKED|VERIFY_BLOCKED|manual intervention required'
}

# --- backlog status -------------------------------------------------------------
backlog_counts() {
  if [ ! -f BACKLOG.md ]; then echo "0 0"; return; fi
  awk '
    /^## / { sec=""; if ($0 ~ /^## TODO/) sec="todo"; else if ($0 ~ /^## IN PROGRESS/) sec="ip"; next }
    sec=="todo" && /^[[:space:]]*-[[:space:]]*\[(HIGH|MEDIUM|LOW)\]/ { t++ }
    sec=="ip"   && /^[[:space:]]*-[[:space:]]*\[/                    { p++ }
    END { printf "%d %d", t+0, p+0 }
  ' BACKLOG.md
}

# --- claude args ----------------------------------------------------------------
CLI_ARGS=(--verbose --output-format stream-json --include-partial-messages)
[ "$SKIP_PERMISSIONS" -eq 1 ] && CLI_ARGS+=(--dangerously-skip-permissions)
[ -n "$MODEL" ] && CLI_ARGS+=(--model "$MODEL")
[ -n "$EFFORT" ] && CLI_ARGS+=(--effort "$EFFORT")

if [ "${THINKING_TOKENS:-0}" -gt 0 ] 2>/dev/null; then
  export MAX_THINKING_TOKENS="$THINKING_TOKENS"
else
  unset MAX_THINKING_TOKENS
fi

# printf %q-quote the claude argv so it can be embedded safely in the runner script.
CLI_ARGS_Q="$(printf '%q ' "${CLI_ARGS[@]}")"

# Pipe suffix that routes console output through the pretty renderer (empty if unavailable).
if [ "$HAS_RENDER" -eq 1 ]; then
  RENDER_PIPE="| python3 $(printf '%q' "$RENDER") --provider claude"
else
  RENDER_PIPE=""
fi

echo
echo "=========================================="
echo "  Merge Two — Run Backlog Loop (controller)"
echo "=========================================="
echo "  Model:           ${MODEL:-<CLI default>}"
echo "  Effort:          ${EFFORT:-<CLI default>}"
echo "  Thinking tokens: ${MAX_THINKING_TOKENS:-off}"
echo "  Window mode:     $([ "$INLINE" -eq 1 ] && echo 'inline (this window)' || echo 'new window per task')"
echo "  Max iterations:  $MAX_ITERATIONS"
echo "  Log dir:         $LOG_DIR_ABS"
echo

# Write one runner script for iteration $1; runs claude, tees log, writes exit
# code to the flag file (via EXIT trap so it's ALWAYS written), keeps window open on failure.
write_runner() {
  local idx="$1" log="$2" flag="$3" promptfile="$4" runner="$5"
  cat > "$runner" <<RUNNER
#!/usr/bin/env bash
cd $(printf '%q' "$REPO_ROOT") || exit 9
export MAX_THINKING_TOKENS=$(printf '%q' "${MAX_THINKING_TOKENS:-}")
[ -z "\$MAX_THINKING_TOKENS" ] && unset MAX_THINKING_TOKENS
code=0
trap 'echo "\$code" > $(printf '%q' "$flag")' EXIT
echo "=== Merge Two backlog task — iteration $idx ==="
cat $(printf '%q' "$promptfile") | claude $CLI_ARGS_Q 2>&1 | tee $(printf '%q' "$log") $RENDER_PIPE
code=\${PIPESTATUS[1]}
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
  if [ "$TODO" -eq 0 ] && [ "$IP" -eq 0 ]; then
    STOP_REASON="Backlog empty (no TODO, no IN PROGRESS)"
    break
  fi

  ts="$(date +%Y%m%d-%H%M%S)"
  base="$LOG_DIR_ABS/iter-$i-$ts"
  log_file="$base.log"
  flag_file="$base.flag"
  prompt_file="$base.prompt"
  runner_file="$base.run.sh"
  printf '%s\n' "$PROMPT" > "$prompt_file"
  rm -f "$flag_file"

  if [ "$INLINE" -eq 1 ]; then
    # Same-window execution.
    if [ "$HAS_RENDER" -eq 1 ]; then
      printf '%s\n' "$PROMPT" | claude "${CLI_ARGS[@]}" 2>&1 | tee "$log_file" | python3 "$RENDER" --provider claude
    else
      printf '%s\n' "$PROMPT" | claude "${CLI_ARGS[@]}" 2>&1 | tee "$log_file"
    fi
    exit_code="${PIPESTATUS[1]}"
  else
    # New Terminal window per task.
    write_runner "$i" "$log_file" "$flag_file" "$prompt_file" "$runner_file"
    osascript -e "tell application \"Terminal\" to do script \"bash '$runner_file'\"" >/dev/null 2>&1 \
      || { STOP_REASON="Failed to open Terminal window (grant Automation permission to Terminal)"; break; }
    echo "  Spawned task window; waiting for it to finish..."
    # Wait for the flag file (always written by the runner's EXIT trap).
    while [ ! -f "$flag_file" ]; do sleep 0.5; done
    exit_code="$(tr -dc '0-9' < "$flag_file")"
    [ -z "$exit_code" ] && exit_code=0
    rm -f "$runner_file"
  fi

  if [ "$exit_code" -ne 0 ]; then
    STOP_REASON="claude exited non-zero ($exit_code) on iteration $i (see $log_file)"
    break
  fi

  if is_blocked "$log_file"; then
    STOP_REASON="Blocker sentinel detected on iteration $i (see $log_file)"
    break
  fi
done

[ -z "$STOP_REASON" ] && STOP_REASON="Reached MaxIterations ($MAX_ITERATIONS)"

echo
echo "=========================================="
echo "  Loop stopped: $STOP_REASON"
echo "  Iterations run: $i"
echo "=========================================="
