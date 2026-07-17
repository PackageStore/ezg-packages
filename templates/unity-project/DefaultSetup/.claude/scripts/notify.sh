#!/bin/bash
# Notification classification and routing script.
# Translates automation stop conditions and events into structured Discord Embeds.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Initialize parameters
EVENT_TYPE=""
TASK_NAME=""
DETAILS=""
TASK_URL=""
TOKENS=""
PROGRESS=""
DURATION=""     # wall-clock time this task's iteration took, e.g. "00:04:12"
BREAKDOWN=""    # pre-formatted per-tool time+token table (from get_timing_token_breakdown)

# Parse arguments
while [[ "$#" -gt 0 ]]; do
  case $1 in
    -e|--event) EVENT_TYPE="$2"; shift ;;
    -t|--task) TASK_NAME="$2"; shift ;;
    -d|--details) DETAILS="$2"; shift ;;
    -u|--url) TASK_URL="$2"; shift ;;
    -k|--tokens) TOKENS="$2"; shift ;;
    -p|--progress) PROGRESS="$2"; shift ;;
    -x|--duration) DURATION="$2"; shift ;;
    -b|--breakdown) BREAKDOWN="$2"; shift ;;
    *) echo "Unknown parameter: $1"; exit 1 ;;
  esac
  shift
done

if [ -z "$EVENT_TYPE" ]; then
  echo "Error: --event parameter is required."
  exit 1
fi

# Truncate details to prevent Discord API errors (max 1024 character value limit, 6000 total embed limit)
if [ ${#DETAILS} -gt 900 ]; then
  DETAILS="${DETAILS:0:900}..."
fi

# Define embed colors (decimal values)
COLOR_SUCCESS=3066993   # Green (#2ecc71)
COLOR_ERROR=15158332    # Red (#e74c3c)
COLOR_WARNING=15105570  # Orange (#e67e22)

# Set defaults based on EVENT_TYPE
TITLE=""
DESCRIPTION=""
COLOR=$COLOR_ERROR

case "$EVENT_TYPE" in
  BACKLOG_EMPTY)
    TITLE="✅ All Backlog Tasks Completed"
    DESCRIPTION="The backlog TODO queue is empty. The automation loop has paused safely."
    COLOR=$COLOR_SUCCESS
    ;;
  TASK_COMPLETED)
    TITLE="✅ Task Completed"
    DESCRIPTION="A backlog task passed all quality gates and was committed successfully."
    COLOR=$COLOR_SUCCESS
    ;;
  COMPILE_BLOCKED)
    TITLE="🔴 Compilation Blocked"
    DESCRIPTION="Unity compilation failed and could not be resolved automatically after 2 fix rounds."
    COLOR=$COLOR_ERROR
    ;;
  PREFLIGHT_BLOCKED)
    TITLE="🔴 Preflight Blocked"
    DESCRIPTION="Deterministic critical findings detected in preflight checks."
    COLOR=$COLOR_ERROR
    ;;
  REVIEW_BLOCKED)
    TITLE="🔴 Review Blocked"
    DESCRIPTION="Code, Performance, or Security reviewer has blocked the changes."
    COLOR=$COLOR_ERROR
    ;;
  VERIFY_BLOCKED)
    TITLE="🔴 QA Verification Blocked"
    DESCRIPTION="QA Verifier reported unmet acceptance criteria after 2 fix rounds."
    COLOR=$COLOR_ERROR
    ;;
  RUNTIME_BLOCKED)
    TITLE="🔴 Runtime Smoke Blocked"
    DESCRIPTION="Runtime smoke gate (play mode + console assert) still failing after 2 fix rounds."
    COLOR=$COLOR_ERROR
    ;;
  VISUAL_BLOCKED)
    TITLE="🔴 Visual Review Blocked"
    DESCRIPTION="The live Unity UI still diverges from its approved visual contract after the allowed fix rounds."
    COLOR=$COLOR_ERROR
    ;;
  MOCKUP_BLOCKED)
    TITLE="🟠 Mockup Approval Required"
    DESCRIPTION="A UI task reached execution without an approved visual contract."
    COLOR=$COLOR_WARNING
    ;;
  EDITOR_REQUIRED)
    TITLE="🟠 Unity Editor Required"
    DESCRIPTION="All remaining runnable tasks require a live Unity Editor instance."
    COLOR=$COLOR_WARNING
    ;;
  CLI_ERROR)
    TITLE="🔴 Automation CLI Error"
    DESCRIPTION="The claude CLI exited with a non-zero status. The loop stopped unexpectedly."
    COLOR=$COLOR_ERROR
    ;;
  *)
    TITLE="⚠️ Automation Event: $EVENT_TYPE"
    DESCRIPTION="An automation stop condition or event occurred."
    COLOR=$COLOR_WARNING
    ;;
esac

# Fold "which task in the backlog" + "how long it took" into the title so
# they're visible without opening the embed body, e.g. "(Task 14/90, 00:04:12)".
TITLE_SUFFIX=""
[ -n "$PROGRESS" ] && TITLE_SUFFIX="Task $PROGRESS"
if [ -n "$DURATION" ]; then
  if [ -n "$TITLE_SUFFIX" ]; then TITLE_SUFFIX="$TITLE_SUFFIX, $DURATION"; else TITLE_SUFFIX="$DURATION"; fi
fi
[ -n "$TITLE_SUFFIX" ] && TITLE="$TITLE ($TITLE_SUFFIX)"

# ISO 8601 Timestamp
TIMESTAMP=$(date -u +"%Y-%m-%dT%H:%M:%SZ")

# JSON escaping helper
escape_json() {
  local input="$1"
  if [ -z "$input" ]; then
    echo -n ""
    return
  fi
  # Escape backslashes and double quotes, and handle newlines safely
  echo -n "$input" | sed 's/\\/\\\\/g' | sed 's/"/\\"/g' | awk '{printf "%s\\n", $0}' | sed 's/\\n$//'
}

ESC_TITLE=$(escape_json "$TITLE")
ESC_DESC=$(escape_json "$DESCRIPTION")
ESC_DETAILS=$(escape_json "$DETAILS")
ESC_TASK_NAME=$(escape_json "$TASK_NAME")
ESC_TASK_URL=$(escape_json "$TASK_URL")
ESC_TOKENS=$(escape_json "$TOKENS")
ESC_BREAKDOWN=$(escape_json "$BREAKDOWN")

# Build Markdown for Task field (incorporate URL if present)
if [ -n "$ESC_TASK_URL" ]; then
  TASK_FIELD_VAL="[$ESC_TASK_NAME]($ESC_TASK_URL)"
else
  TASK_FIELD_VAL="$ESC_TASK_NAME"
fi

if [ -z "$TASK_FIELD_VAL" ]; then
  TASK_FIELD_VAL="N/A"
fi

# Build details block. Default to N/A if empty.
if [ -z "$ESC_DETAILS" ]; then
  ESC_DETAILS="No additional details provided."
fi

# Optional per-tool time+token breakdown field — only added when the caller passed one
# (empty for BACKLOG_EMPTY, and for any event where the log couldn't be parsed).
BREAKDOWN_FIELD=""
if [ -n "$ESC_BREAKDOWN" ]; then
  BREAKDOWN_FIELD=$(cat <<BDEOF
,
    {
      "name": "Time & Token Breakdown (approx., per tool)",
      "value": "\`\`\`\\n$ESC_BREAKDOWN\\n\`\`\`",
      "inline": false
    }
BDEOF
)
fi

# Construct Embed JSON
EMBED_JSON=$(cat <<EOF
{
  "title": "$ESC_TITLE",
  "description": "$ESC_DESC",
  "color": $COLOR,
  "timestamp": "$TIMESTAMP",
  "fields": [
    {
      "name": "Task",
      "value": "$TASK_FIELD_VAL",
      "inline": true
    },
    {
      "name": "Token Usage",
      "value": "${ESC_TOKENS:-N/A}",
      "inline": true
    },
    {
      "name": "Details / Error Log",
      "value": "\`\`\`\\n$ESC_DETAILS\\n\`\`\`",
      "inline": false
    }$BREAKDOWN_FIELD
  ]
}
EOF
)

# Forward to core discord-send.sh script
bash "$SCRIPT_DIR/discord-send.sh" "" "$EMBED_JSON"
