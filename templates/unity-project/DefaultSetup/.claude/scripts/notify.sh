#!/bin/bash
# Notification classification and routing for the backlog loop.
# Translates automation stop conditions and events into structured Discord Embeds,
# then forwards to discord-send.sh (which no-ops if Discord is not configured).
#
# PowerShell twin: notify.ps1 — keep the event table and the embed fields in lockstep.
#
# Usage:
#   notify.sh --event TASK_COMPLETED --task "..." [--url ...] [--details ...]
#             [--tokens ...] [--per-model ...] [--cumulative ...] [--breakdown ...]
#             [--progress 5/12] [--duration 00:04:12]

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

EVENT_TYPE=""
TASK_NAME=""
DETAILS=""
TASK_URL=""
TOKENS=""     # multi-line token summary for this iteration (total / in / out / cache / turns)
PROGRESS=""   # "<current>/<total>" position of this task in the whole backlog, e.g. "5/12"
DURATION=""   # wall-clock time this task's iteration took, e.g. "00:04:12"
BREAKDOWN=""  # per-tool time+token table (fixed-width rows) from get_timing_breakdown
PER_MODEL=""  # per-model token + cost rows for this iteration (subagent usage included)
CUMULATIVE="" # running total across every iteration of the current loop run

while [[ "$#" -gt 0 ]]; do
  case $1 in
    -e|--event)      EVENT_TYPE="$2"; shift ;;
    -t|--task)       TASK_NAME="$2"; shift ;;
    -d|--details)    DETAILS="$2"; shift ;;
    -u|--url)        TASK_URL="$2"; shift ;;
    -k|--tokens)     TOKENS="$2"; shift ;;
    -p|--progress)   PROGRESS="$2"; shift ;;
    -x|--duration)   DURATION="$2"; shift ;;
    -b|--breakdown)  BREAKDOWN="$2"; shift ;;
    -m|--per-model)  PER_MODEL="$2"; shift ;;
    -c|--cumulative) CUMULATIVE="$2"; shift ;;
    *) echo "Unknown parameter: $1"; exit 1 ;;
  esac
  shift
done

if [ -z "$EVENT_TYPE" ]; then
  echo "Error: --event parameter is required."
  exit 1
fi

# Truncate details (Discord embed value limit is 1024 chars; keep well under).
if [ ${#DETAILS} -gt 900 ]; then
  DETAILS="${DETAILS:0:900}..."
fi
# Same limit for the two generated tables (code fence adds ~8 chars on top).
if [ ${#BREAKDOWN} -gt 1000 ]; then
  BREAKDOWN="${BREAKDOWN:0:1000}..."
fi
if [ ${#PER_MODEL} -gt 1000 ]; then
  PER_MODEL="${PER_MODEL:0:1000}..."
fi

COLOR_SUCCESS=3066993   # Green (#2ecc71)
COLOR_ERROR=15158332    # Red (#e74c3c)
COLOR_WARNING=15105570  # Orange (#e67e22)

TITLE=""
DESCRIPTION=""
COLOR=$COLOR_ERROR

case "$EVENT_TYPE" in
  BACKLOG_EMPTY)
    TITLE="✅ All Backlog Tasks Completed"
    DESCRIPTION="The backlog TODO queue is empty. The automation loop has paused safely."
    COLOR=$COLOR_SUCCESS ;;
  TASK_COMPLETED)
    TITLE="✅ Task Completed"
    DESCRIPTION="A backlog task passed all quality gates and was committed to agent/dev."
    COLOR=$COLOR_SUCCESS ;;
  COMPILE_BLOCKED)
    TITLE="🔴 Compilation Blocked"
    DESCRIPTION="Unity compilation failed and could not be resolved automatically after 2 fix rounds."
    COLOR=$COLOR_ERROR ;;
  PREFLIGHT_BLOCKED)
    TITLE="🔴 Preflight Blocked"
    DESCRIPTION="Deterministic critical findings detected in preflight checks."
    COLOR=$COLOR_ERROR ;;
  REVIEW_BLOCKED)
    TITLE="🔴 Review Blocked"
    DESCRIPTION="Code, Performance, or Security reviewer blocked the changes after 2 fix rounds."
    COLOR=$COLOR_ERROR ;;
  VERIFY_BLOCKED)
    TITLE="🔴 QA Verification Blocked"
    DESCRIPTION="QA Verifier reported unmet acceptance criteria after 2 fix rounds."
    COLOR=$COLOR_ERROR ;;
  RUNTIME_BLOCKED)
    TITLE="🔴 Runtime Smoke Blocked"
    DESCRIPTION="Runtime smoke gate (play mode + invariant suite) still failing after 2 fix rounds."
    COLOR=$COLOR_ERROR ;;
  CLI_ERROR)
    TITLE="🔴 Automation CLI Error"
    DESCRIPTION="The claude CLI exited with a non-zero status. The loop stopped unexpectedly."
    COLOR=$COLOR_ERROR ;;
  API_RETRY)
    TITLE="⚠️ Transient API Error - Retrying"
    DESCRIPTION="The claude CLI hit a transport break or an overloaded API. The loop is retrying the same task and has NOT stopped."
    COLOR=$COLOR_WARNING ;;
  *)
    TITLE="⚠️ Automation Event: $EVENT_TYPE"
    DESCRIPTION="An automation stop condition or event occurred."
    COLOR=$COLOR_WARNING ;;
esac

# Fold "which task in the backlog" + "how long it took" into the title so
# they're visible without opening the embed body.
TITLE_SUFFIX=""
[ -n "$PROGRESS" ] && TITLE_SUFFIX="Task $PROGRESS"
if [ -n "$DURATION" ]; then
  if [ -n "$TITLE_SUFFIX" ]; then TITLE_SUFFIX="$TITLE_SUFFIX, $DURATION"; else TITLE_SUFFIX="$DURATION"; fi
fi
[ -n "$TITLE_SUFFIX" ] && TITLE="$TITLE ($TITLE_SUFFIX)"

TIMESTAMP=$(date -u +"%Y-%m-%dT%H:%M:%SZ")

escape_json() {
  local input="$1"
  if [ -z "$input" ]; then echo -n ""; return; fi
  echo -n "$input" | sed 's/\\/\\\\/g' | sed 's/"/\\"/g' | awk '{printf "%s\\n", $0}' | sed 's/\\n$//'
}

ESC_TITLE=$(escape_json "$TITLE")
ESC_DESC=$(escape_json "$DESCRIPTION")
ESC_DETAILS=$(escape_json "$DETAILS")
ESC_TASK_NAME=$(escape_json "$TASK_NAME")
ESC_TASK_URL=$(escape_json "$TASK_URL")
ESC_TOKENS=$(escape_json "$TOKENS")
ESC_BREAKDOWN=$(escape_json "$BREAKDOWN")
ESC_PER_MODEL=$(escape_json "$PER_MODEL")
ESC_CUMULATIVE=$(escape_json "$CUMULATIVE")

if [ -n "$ESC_TASK_URL" ]; then
  TASK_FIELD_VAL="[$ESC_TASK_NAME]($ESC_TASK_URL)"
else
  TASK_FIELD_VAL="$ESC_TASK_NAME"
fi
[ -z "$TASK_FIELD_VAL" ] && TASK_FIELD_VAL="N/A"
[ -z "$ESC_DETAILS" ] && ESC_DETAILS="No additional details provided."

# Fields are assembled incrementally: the optional ones are only emitted when the
# caller actually produced them (no jq, no result line in the log, or an event with
# no iteration to analyze such as BACKLOG_EMPTY). Keep the order and the names in
# lockstep with notify.ps1.
FIELDS="    { \"name\": \"Task\", \"value\": \"$TASK_FIELD_VAL\", \"inline\": true },
    { \"name\": \"Token Usage\", \"value\": \"${ESC_TOKENS:-N/A}\", \"inline\": true }"

if [ -n "$ESC_PER_MODEL" ]; then
  FIELDS="$FIELDS,
    { \"name\": \"Cost by Model\", \"value\": \"$ESC_PER_MODEL\", \"inline\": true }"
fi

if [ -n "$ESC_CUMULATIVE" ]; then
  FIELDS="$FIELDS,
    { \"name\": \"Loop Total (so far)\", \"value\": \"$ESC_CUMULATIVE\", \"inline\": true }"
fi

FIELDS="$FIELDS,
    { \"name\": \"Details / Error Log\", \"value\": \"\`\`\`\\n$ESC_DETAILS\\n\`\`\`\", \"inline\": false }"

if [ -n "$ESC_BREAKDOWN" ]; then
  FIELDS="$FIELDS,
    { \"name\": \"Time & Token Breakdown (approx., per tool)\", \"value\": \"\`\`\`\\n$ESC_BREAKDOWN\\n\`\`\`\", \"inline\": false }"
fi

EMBED_JSON=$(cat <<EOF
{
  "title": "$ESC_TITLE",
  "description": "$ESC_DESC",
  "color": $COLOR,
  "timestamp": "$TIMESTAMP",
  "fields": [
$FIELDS
  ]
}
EOF
)

bash "$SCRIPT_DIR/discord-send.sh" "" "$EMBED_JSON"
