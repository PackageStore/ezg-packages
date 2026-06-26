#!/usr/bin/env bash
# [Project Name] — Run Backlog Loop (macOS), per-task new Terminal window.
#
# Mirrors BlazeSurvivor's loop model: this script is the CONTROLLER. For each
# iteration it spawns a SEPARATE Terminal window that runs exactly one
# /run-backlog task, then waits (via a flag file) for that window to finish
# before spawning the next. A failed task keeps its window open so you can read
# the error. Stops when the backlog is empty, a blocker sentinel is printed, the
# CLI exits non-zero, or MaxIterations is reached.
#
# The run-backlog skill commits AND pushes each done task to agent/dev on origin.
#
# Usage:
#   .claude/scripts/run-backlog-loop.sh
#   .claude/scripts/run-backlog-loop.sh --model claude-opus-4-8 --effort xhigh --max-iterations 5
#   .claude/scripts/run-backlog-loop.sh --auto-model-by-tier --max-iterations 5
#   .claude/scripts/run-backlog-loop.sh --inline        # run in THIS window (no new windows)
#
# Options:
#   --model <id>             Claude model id (default: empty = CLI default).
#   --effort <level>         Reasoning effort: low|medium|high|xhigh (default: empty = CLI default).
#   --auto-model-by-tier     Pick model/effort per iteration from BACKLOG.md task tier.
#   --xs-model/--xs-effort   Override XS profile (default: claude-sonnet-4-6/medium).
#   --s-model/--s-effort     Override S profile (default: claude-sonnet-4-6/high).
#   --m-model/--m-effort     Override M profile (default: claude-sonnet-4-6/high).
#   --l-model/--l-effort     Override L profile (default: claude-opus-4-8/xhigh).
#                            (Opus is reserved for L; M runs on sonnet/high. There is NO
#                             auto-escalation: if an M task hits REVIEW_BLOCKED the loop
#                             stops — rerun that one task with --m-model claude-opus-4-8.)
#   --max-iterations <n>     Max task iterations (default: 100).
#   --thinking-tokens <n>    Legacy/global MAX_THINKING_TOKENS override (default: 10000; 0 = off).
#   --xs-thinking-tokens <n> Override XS thinking budget (default: 3000; 0 = off).
#   --s-thinking-tokens <n>  Override S thinking budget (default: 6000; 0 = off).
#   --m-thinking-tokens <n>  Override M thinking budget (default: 10000; 0 = off).
#   --l-thinking-tokens <n>  Override L thinking budget (default: 10000; 0 = off).
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
AUTO_MODEL_BY_TIER=0
XS_MODEL="claude-sonnet-4-6"
XS_EFFORT="medium"
S_MODEL="claude-sonnet-4-6"
S_EFFORT="high"
M_MODEL="claude-sonnet-4-6"
M_EFFORT="high"
L_MODEL="claude-opus-4-8"
L_EFFORT="xhigh"
MAX_ITERATIONS=100
THINKING_TOKENS=10000
XS_THINKING_TOKENS=3000
S_THINKING_TOKENS=6000
M_THINKING_TOKENS=10000
L_THINKING_TOKENS=10000
SKIP_PERMISSIONS=1
INLINE=0
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
    -h|--help)
      sed -n '2,36p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
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
Execute exactly one iteration of the [Project Name] run-backlog workflow.

Required contract:
1. Read .agents/skills/run-backlog/SKILL.md before changing any files.
2. Follow that skill exactly for one iteration only.
3. Read CLAUDE.md, .agents/rules/*, the selected task file, and only the relevant code the workflow requests.
4. Spawn the code-reviewer, performance-reviewer, security-auditor, and qa-verifier subagents per the skill spec using the Agent tool.
5. Print exactly these tokens when blocked: PREFLIGHT_BLOCKED, REVIEW_BLOCKED, VERIFY_BLOCKED, or "manual intervention required".
6. Commit/push to agent/dev (origin) only when the skill marks the task DONE. Do not create a PR.
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
  printf '%s' "$result_line" | grep -Eq 'COMPILE_BLOCKED|PREFLIGHT_BLOCKED|REVIEW_BLOCKED|VERIFY_BLOCKED|manual intervention required'
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
  ' BACKLOG.md
}

next_task_profile() {
  local line tier title state
  line="$(task_line_for_section "IN PROGRESS")"
  state="in-progress"
  if [ -z "$line" ]; then
    line="$(task_line_for_section "TODO")"
    state="todo"
  fi

  if [ -z "$line" ]; then
    TASK_TIER=""
    TASK_TITLE=""
    TASK_STATE=""
    SELECTED_MODEL="$MODEL"
    SELECTED_EFFORT="$EFFORT"
    SELECTED_THINKING_TOKENS="$THINKING_TOKENS"
    return
  fi

  tier="$(printf '%s\n' "$line" | sed -nE 's/^[[:space:]]*-[[:space:]]*\[[^]]+\][[:space:]]+\[(XS|S|M|L)\].*/\1/p')"
  if [ -n "$tier" ]; then
    title="$(printf '%s\n' "$line" | sed -E 's/^[[:space:]]*-[[:space:]]*\[[^]]+\][[:space:]]+\[[^]]+\][[:space:]]+\[([^]]+)\].*/\1/')"
  else
    title="$(printf '%s\n' "$line" | sed -E 's/^[[:space:]]*-[[:space:]]*\[[^]]+\][[:space:]]+\[([^]]+)\].*/\1/')"
  fi
  TASK_TIER="$tier"
  TASK_TITLE="$title"
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
echo "  [Project Name] — Run Backlog Loop (controller)"
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
echo "  Log dir:         $LOG_DIR_ABS"
echo

# Get formatted token usage from log file
get_token_usage() {
  local log="$1"
  if [ -f "$log" ]; then
    jq -s -r '
      def format_n(n):
        if n >= 1000000 then ((n / 100000 | round) / 10 | tostring) + "M"
        elif n >= 1000 then ((n / 100 | round) / 10 | tostring) + "K"
        else n | tostring
        end;
      [ .[] | select(.type == "assistant" and .message.id != null and .message.usage != null) ]
      | group_by(.message.id)
      | map(max_by(.message.usage.output_tokens))
      | map(.message.usage)
      | if length == 0 then ""
        else
          {
            in: map(.input_tokens + (.cache_creation_input_tokens // 0)) | add,
            out: map(.output_tokens) | add,
            cr: map(.cache_read_input_tokens // 0) | add
          }
          | .total = .in + .out
          | "\(format_n(.total)) (\(format_n(.in)) In, \(format_n(.out)) Out, \(format_n(.cr)) Cache Read)"
        end
    ' "$log" 2>/dev/null
  fi
}

# Write one runner script for iteration $1; runs claude, tees log, writes exit
# code to the flag file (via EXIT trap so it's ALWAYS written), keeps window open on failure.
write_runner() {
  local idx="$1" log="$2" flag="$3" promptfile="$4" runner="$5" pidfile="$6"
  cat > "$runner" <<RUNNER
#!/usr/bin/env bash
cd $(printf '%q' "$REPO_ROOT") || exit 9
export MAX_THINKING_TOKENS=$(printf '%q' "${SELECTED_THINKING_TOKENS:-}")
if [ -z "\$MAX_THINKING_TOKENS" ] || [ "\$MAX_THINKING_TOKENS" = "0" ]; then
  unset MAX_THINKING_TOKENS
fi
echo \$\$ > $(printf '%q' "$pidfile")
code=0
# Idempotent: do not clobber a flag the controller already wrote (e.g. "124" on watchdog kill).
trap '[ -f $(printf '%q' "$flag") ] || echo "\$code" > $(printf '%q' "$flag")' EXIT
echo "=== [Project Name] backlog task — iteration $idx ==="
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
  if [ "$TODO" -eq 0 ] && [ "$IP" -eq 0 ]; then
    STOP_REASON="Backlog empty (no TODO, no IN PROGRESS)"
    bash "$SCRIPT_DIR/notify.sh" \
      --event "BACKLOG_EMPTY" \
      --task "N/A" \
      --details "All backlog tasks have been processed successfully."
    break
  fi

  # Resolve current task info for notifications
  CURRENT_TASK_LINE="$(task_line_for_section "IN PROGRESS")"
  if [ -z "$CURRENT_TASK_LINE" ]; then
    CURRENT_TASK_LINE="$(task_line_for_section "TODO")"
  fi
  
  if [ -n "$CURRENT_TASK_LINE" ]; then
    TASK_TIER_NOTIF="$(printf '%s\n' "$CURRENT_TASK_LINE" | sed -nE 's/^[[:space:]]*-[[:space:]]*\[[^]]+\][[:space:]]+\[(XS|S|M|L)\].*/\1/p')"
    if [ -n "$TASK_TIER_NOTIF" ]; then
      TASK_TITLE_NOTIF="$(printf '%s\n' "$CURRENT_TASK_LINE" | sed -E 's/^[[:space:]]*-[[:space:]]*\[[^]]+\][[:space:]]+\[[^]]+\][[:space:]]+\[([^]]+)\].*/\1/')"
    else
      TASK_TITLE_NOTIF="$(printf '%s\n' "$CURRENT_TASK_LINE" | sed -E 's/^[[:space:]]*-[[:space:]]*\[[^]]+\][[:space:]]+\[([^]]+)\].*/\1/')"
    fi
    TASK_FILE_PATH_NOTIF="$(printf '%s\n' "$CURRENT_TASK_LINE" | sed -nE 's/.*\]\((backlog\/[^)]+)\).*/\1/p')"
    if [ -n "$TASK_FILE_PATH_NOTIF" ]; then
      TASK_URL_NOTIF="file://$REPO_ROOT/$TASK_FILE_PATH_NOTIF"
    else
      TASK_URL_NOTIF=""
    fi
  else
    TASK_TITLE_NOTIF="Unknown Task"
    TASK_URL_NOTIF=""
  fi

  if [ "$AUTO_MODEL_BY_TIER" -eq 1 ]; then
    next_task_profile
  else
    TASK_TIER=""
    TASK_TITLE=""
    TASK_STATE=""
    SELECTED_MODEL="$MODEL"
    SELECTED_EFFORT="$EFFORT"
    SELECTED_THINKING_TOKENS="$THINKING_TOKENS"
  fi
  build_cli_args

  if [ "$AUTO_MODEL_BY_TIER" -eq 1 ]; then
    echo "  Next task: [${TASK_TIER:-unknown}] $TASK_TITLE ($TASK_STATE)"
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
    # New Terminal window per task (BlazeSurvivor model).
    write_runner "$i" "$log_file" "$flag_file" "$prompt_file" "$runner_file" "$pid_file"
    win_id="$(osascript -e "tell application \"Terminal\"" -e "do script \"bash '$runner_file'\"" -e "return id of front window" -e "end tell" 2>/dev/null)" \
      || { STOP_REASON="Failed to open Terminal window (grant Automation permission to Terminal)"; break; }
    echo "  Spawned task window; waiting for it to finish (monitoring inactivity)..."
    # Wait for the flag file with a timeout / inactivity check to prevent hanging on token limit errors.
    elapsed=0
    last_size=0
    inactive_seconds=0
    check_interval=5
    while [ ! -f "$flag_file" ]; do
      sleep $check_interval
      elapsed=$((elapsed + check_interval))
      
      if [ -f "$log_file" ]; then
        current_size=$(stat -f%z "$log_file" 2>/dev/null || echo 0)
        if [ "$current_size" -eq "$last_size" ]; then
          inactive_seconds=$((inactive_seconds + check_interval))
        else
          inactive_seconds=0
          last_size="$current_size"
        fi
      else
        inactive_seconds=$((inactive_seconds + check_interval))
      fi

      # 900 seconds (15 minutes) of absolute inactivity, or 3600 seconds (60 minutes) max execution time
      if [ "$inactive_seconds" -ge 900 ] || [ "$elapsed" -ge 3600 ]; then
        if [ "$inactive_seconds" -ge 900 ]; then
          STOP_REASON="Task hung or stopped due to token exhaustion/inactivity (no log updates for 15m)"
        else
          STOP_REASON="Task timed out (exceeded 60m limit)"
        fi
        echo "  ⚠️ $STOP_REASON. Killing task window so it stops consuming tokens." >&2
        echo "124" > "$flag_file"   # claim the result first so the runner's EXIT trap won't clobber it
        # Kill the spawned task's process tree (claude + pipeline) so a hung/slow window
        # does not keep burning tokens after the controller moves on.
        if [ -f "$pid_file" ]; then
          runner_pid="$(tr -dc '0-9' < "$pid_file")"
          if [ -n "$runner_pid" ]; then
            pkill -TERM -P "$runner_pid" 2>/dev/null || true
            kill -TERM "$runner_pid" 2>/dev/null || true
          fi
        fi
        # Process is dead now, so closing the window won't prompt about a running process.
        [ -n "${win_id:-}" ] && osascript -e "tell application \"Terminal\" to close (every window whose id is $win_id) saving no" >/dev/null 2>&1 || true
        break
      fi
    done
    exit_code="$(tr -dc '0-9' < "$flag_file")"
    [ -z "$exit_code" ] && exit_code=0
    rm -f "$runner_file" "$pid_file"
  fi

  if [ "$exit_code" -ne 0 ]; then
    STOP_REASON="claude exited non-zero ($exit_code) on iteration $i (see $log_file)"
    local tokens_val=""
    if command -v jq >/dev/null 2>&1; then
      tokens_val=$(get_token_usage "$log_file")
    fi
    bash "$SCRIPT_DIR/notify.sh" \
      --event "CLI_ERROR" \
      --task "$TASK_TITLE_NOTIF" \
      --url "$TASK_URL_NOTIF" \
      --tokens "$tokens_val" \
      --details "$STOP_REASON"
    break
  fi

  if is_blocked "$log_file"; then
    STOP_REASON="Blocker sentinel detected on iteration $i (see $log_file)"
    
    # Classify the block type
    block_event="VERIFY_BLOCKED"
    block_details="Manual intervention required."
    
    if grep -q "COMPILE_BLOCKED" "$log_file"; then
      block_event="COMPILE_BLOCKED"
      block_details=$(grep -o "COMPILE_BLOCKED.*" "$log_file" | head -n 1)
    elif grep -q "PREFLIGHT_BLOCKED" "$log_file"; then
      block_event="PREFLIGHT_BLOCKED"
      block_details=$(grep -o "PREFLIGHT_BLOCKED.*" "$log_file" | head -n 1)
    elif grep -q "REVIEW_BLOCKED" "$log_file"; then
      block_event="REVIEW_BLOCKED"
      block_details=$(grep -o "REVIEW_BLOCKED.*" "$log_file" | head -n 1)
    elif grep -q "VERIFY_BLOCKED" "$log_file"; then
      block_event="VERIFY_BLOCKED"
      block_details=$(grep -o "VERIFY_BLOCKED.*" "$log_file" | head -n 1)
    else
      block_details=$(grep -i "manual intervention.*" "$log_file" | head -n 1)
      if [ -z "$block_details" ]; then
        block_details="Automation paused. Manual intervention required."
      fi
    fi

    local tokens_val=""
    if command -v jq >/dev/null 2>&1; then
      tokens_val=$(get_token_usage "$log_file")
    fi
    bash "$SCRIPT_DIR/notify.sh" \
      --event "$block_event" \
      --task "$TASK_TITLE_NOTIF" \
      --url "$TASK_URL_NOTIF" \
      --tokens "$tokens_val" \
      --details "$block_details"
    break
  fi

  # Task passed all gates this iteration — notify success.
  read -r TODO_NEW IP_NEW <<<"$(backlog_counts)"
  DONE_NEW=$(find backlog/done -name "*.md" 2>/dev/null | wc -l | xargs)
  TOTAL_NEW=$((TODO_NEW + IP_NEW + DONE_NEW))

  local tokens_val=""
  if command -v jq >/dev/null 2>&1; then
    tokens_val=$(get_token_usage "$log_file")
  fi
  bash "$SCRIPT_DIR/notify.sh" \
    --event "TASK_COMPLETED" \
    --task "$TASK_TITLE_NOTIF" \
    --url "$TASK_URL_NOTIF" \
    --tokens "$tokens_val" \
    --details "Progress: Task $DONE_NEW of $TOTAL_NEW completed successfully.
Committed & pushed to agent/dev. Ready for manual verify + merge."
done

[ -z "$STOP_REASON" ] && STOP_REASON="Reached MaxIterations ($MAX_ITERATIONS)"

echo
echo "=========================================="
echo "  Loop stopped: $STOP_REASON"
echo "  Iterations run: $i"
echo "=========================================="
