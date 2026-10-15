#!/bin/bash
# Notification classification and routing for the backlog loop.
# Translates automation stop conditions and events into structured Discord Embeds,
# then forwards to discord-send.sh (which no-ops if Discord is not configured).
#
# PowerShell twin: notify.ps1 — keep the event table and the embed layout in lockstep.
#
# Usage:
#   notify.sh --event TASK_COMPLETED --task "..." [--url ...] [--details ...]
#             [--tokens ...] [--per-model ...] [--cumulative ...] [--breakdown ...]
#             [--progress 5/12] [--duration 00:04:12] [--project "My Game"]
#   NOTIFY_DRY_RUN=1 prints the embed instead of sending it.
#
# Embed layout (kept short on purpose — it is read on a phone): the title carries
# project + event + position + duration, and EVERYTHING else is one description with
# one line per topic. No embed fields: each field costs a header line, inline fields
# wrap in narrow columns, and the old fixed-width tables cost a line per row.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

EVENT_TYPE=""
TASK_NAME=""
DETAILS=""
TASK_URL=""
TOKENS=""     # multi-line token summary for this iteration (total / in / out / cache / turns)
PROGRESS=""   # "<current>/<total>" position of this task in the whole backlog, e.g. "5/12"
DURATION=""   # wall-clock time this task's iteration took, e.g. "00:04:12"
BREAKDOWN=""  # per-tool time+token table (header + fixed-width rows) from get_timing_breakdown
PER_MODEL=""  # per-model token + cost rows for this iteration (subagent usage included)
CUMULATIVE="" # running total across every iteration of the current loop run
PROJECT=""    # human-readable project name for the title; empty = resolved from the profile

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
    -n|--project)    PROJECT="$2"; shift ;;
    *) echo "Unknown parameter: $1"; exit 1 ;;
  esac
  shift
done

if [ -z "$EVENT_TYPE" ]; then
  echo "Error: --event parameter is required."
  exit 1
fi

# Truncate details (keep well under Discord's 4096-char description limit).
if [ ${#DETAILS} -gt 900 ]; then
  DETAILS="${DETAILS:0:900}..."
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
    # No description: the title already says it, and the details line says where
    # the commit went (the old fixed sentence named agent/dev even in current mode).
    TITLE="✅ Task Completed"
    DESCRIPTION=""
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
  SILENT_RETRY)
    TITLE="⚠️ Silent End - Resuming Once"
    DESCRIPTION="The iteration exited cleanly without a stop token while its task was still in progress. The loop has NOT stopped: the next iteration resumes the task once."
    COLOR=$COLOR_WARNING ;;
  TASK_CHECKPOINTED)
    TITLE="⏸️ Task Checkpointed - Resuming"
    DESCRIPTION="A multi-iteration task saved its progress at the iteration deadline. The loop has NOT stopped: the next iteration resumes the same task."
    COLOR=$COLOR_SUCCESS ;;
  CHECKPOINT_LIMIT)
    TITLE="🟠 Checkpoint Limit Reached"
    DESCRIPTION="A multi-iteration task checkpointed too many consecutive iterations without finishing. The loop stopped for a human look."
    COLOR=$COLOR_WARNING ;;
  TASK_RECOVERING)
    TITLE="🛠️ Self-Heal - Recovering Task"
    DESCRIPTION="The iteration ended without finishing its task (block, crash, hang or silent end). The loop has NOT stopped: the next iteration resumes the task in recovery mode with a brief of what failed."
    COLOR=$COLOR_WARNING ;;
  TASK_PARKED)
    TITLE="🅿️ Self-Heal - Task Parked"
    DESCRIPTION="The task could not be finished within its recovery budget. Its partial work was saved under refs/backlog/parked/<NNN>, the task moved to the tail of TODO, and the loop continues with the next task."
    COLOR=$COLOR_WARNING ;;
  PARKED_ONLY)
    TITLE="🟠 Only Parked Tasks Remain"
    DESCRIPTION="Every remaining task was parked during this run. The loop stopped; relaunch it to give the parked tasks a fresh budget."
    COLOR=$COLOR_WARNING ;;
  LIMIT_WAIT)
    TITLE="⏳ Waiting Out a Usage Limit"
    DESCRIPTION="The usage/session limit (or an API outage) was hit. The loop sleeps until the reset and then resumes the same task. It has NOT stopped."
    COLOR=$COLOR_WARNING ;;
  BUSY_WAIT)
    TITLE="⏳ Waiting for Another Session"
    DESCRIPTION="Another live session owns the task. The loop waits for it to finish before continuing."
    COLOR=$COLOR_WARNING ;;
  EDITOR_RECOVERY)
    TITLE="🛠️ Self-Heal - Unity Editor (Re)started"
    DESCRIPTION="Every remaining task needs a live Unity Editor and none answered. The loop (re)started this project's Editor and resumes."
    COLOR=$COLOR_WARNING ;;
  GATE_RECEIPT_MISSING)
    TITLE="🟠 Reviewer Receipt Missing"
    DESCRIPTION="A task reached DONE without a mandatory reviewer spawn in its log. With self-heal, a post-hoc audit iteration runs the missing reviewer(s) next."
    COLOR=$COLOR_WARNING ;;
  AUDIT_DONE)
    TITLE="🔎 Post-hoc Review Finished"
    DESCRIPTION="The missing reviewer(s) ran on the task's commit; any fix landed as a follow-up commit."
    COLOR=$COLOR_SUCCESS ;;
  MANUAL_INTERVENTION)
    TITLE="🔴 Manual Intervention Requested"
    DESCRIPTION="The iteration asked for a human without a named block token."
    COLOR=$COLOR_ERROR ;;
  *)
    TITLE="⚠️ Automation Event: $EVENT_TYPE"
    DESCRIPTION="An automation stop condition or event occurred."
    COLOR=$COLOR_WARNING ;;
esac

# "My Game" for the title prefix: project_profile.py's displayName (only when the
# profile exists — without it the module answers its "UnityProject" default), else the
# repo folder split at its CamelCase humps (same rule as Profile.display_name).
# Twin of Get-ProjectDisplayName in notify.ps1.
if [ -z "$PROJECT" ] && [ -f "$SCRIPT_DIR/../project-profile.json" ]; then
  PROJECT="$(python3 "$SCRIPT_DIR/project_profile.py" displayName 2>/dev/null | tr -d '\r')"
fi
if [ -z "$PROJECT" ]; then
  PROJECT="$(basename "$(cd "$SCRIPT_DIR/../.." && pwd)" \
    | sed -E 's/[_-]+/ /g; s/([a-z])([A-Z])/\1 \2/g; s/([A-Z])([A-Z][a-z])/\1 \2/g')"
fi
[ -n "$PROJECT" ] && TITLE="$PROJECT: $TITLE"

# Fold "which task in the backlog" + "how long it took" into the title so
# they're visible without opening the embed body.
TITLE_SUFFIX=""
[ -n "$PROGRESS" ] && TITLE_SUFFIX="Task $PROGRESS"
if [ -n "$DURATION" ]; then
  if [ -n "$TITLE_SUFFIX" ]; then TITLE_SUFFIX="$TITLE_SUFFIX, $DURATION"; else TITLE_SUFFIX="$DURATION"; fi
fi
[ -n "$TITLE_SUFFIX" ] && TITLE="$TITLE ($TITLE_SUFFIX)"
[ ${#TITLE} -gt 256 ] && TITLE="${TITLE:0:253}..."

TIMESTAMP=$(date -u +"%Y-%m-%dT%H:%M:%SZ")

# Backslash-escape Discord markdown so a task title like "fix *_x_*" shows literally.
md_escape() {
  printf '%s' "$1" | sed -E 's/([\\*_~`|])/\\\1/g'
}

# "a | b<NL>c | d" -> "a<inner>b · c<inner>d": the loop's multi-line, pipe-separated
# summaries folded onto one wrapping line.
join_summary() {
  printf '%s\n' "$1" | tr -d '\r' | awk -v inner="${2:- · }" '
    { sub(/^[ \t]+/, ""); sub(/[ \t]+$/, "") }
    length($0) { gsub(/ \| /, inner); out = out (n++ ? " · " : "") $0 }
    END { printf "%s", out }'
}

# Fixed-width breakdown table -> "exec 12.3m/1.2M · Read 3.1m/400K · ...". Rows are
# "<name>  <time>  <tokens>" with 2+ spaces between columns (the name itself may hold
# one space: "+3 more"); the first row is the header.
format_breakdown() {
  printf '%s\n' "$1" | tr -d '\r' \
    | sed -nE '1d; s/^(.*[^ ]) {2,}([^ ]+) {2,}([^ ]+) *$/\1 \2\/\3/p' \
    | sed -E 's/([\\*_~`|])/\\\1/g' \
    | awk '{ out = out (n++ ? " · " : "") $0 } END { printf "%s", out }'
}

DESC=""
add_line() {
  if [ -n "$DESC" ]; then DESC="$DESC
$1"; else DESC="$1"; fi
}

# Task. The loop's url is a file:// path on its own machine, which Discord does not
# render as a link (only http/https) — the old "[title](file://...)" arrived as raw
# text spanning lines. The file name identifies the task and stays true after the
# file moves from in-progress/ to done/.
if [ -n "$TASK_NAME" ] && [ "$TASK_NAME" != "N/A" ]; then
  TASK_TEXT="$(md_escape "$TASK_NAME")"
  case "$TASK_URL" in
    http://*|https://*) add_line "**[$TASK_TEXT]($TASK_URL)**" ;;
    "") add_line "**$TASK_TEXT**" ;;
    *) add_line "**$TASK_TEXT** · \`${TASK_URL##*[/\\]}\`" ;;
  esac
fi

[ -n "$DESCRIPTION" ] && add_line "$DESCRIPTION"

# Details: the success events' details are the loop's own one-liner (it may carry
# inline code), so they stay plain; anything else is an error/stop reason quoted
# verbatim from a log, so it goes in a code block.
if [ -n "$DETAILS" ]; then
  if [ "$COLOR" -eq "$COLOR_SUCCESS" ]; then
    add_line "$DETAILS"
  else
    SAFE_DETAILS="$(printf '%s' "$DETAILS" | sed "s/\`\`\`/'''/g")"
    add_line "\`\`\`
$SAFE_DETAILS
\`\`\`"
  fi
fi

[ -n "$TOKENS" ] && add_line "**Tokens** $(join_summary "$TOKENS")"
# Per-model split (main agent + subagents). Only present when the CLI reported a
# modelUsage block, so it stays absent for runs that don't emit one.
[ -n "$PER_MODEL" ] && add_line "**By model** $(join_summary "$(printf '%s' "$PER_MODEL" | sed 's/: / /')" " ")"
# Loop-wide running total, so a long unattended run shows what it has burned so far.
[ -n "$CUMULATIVE" ] && add_line "**Loop total** $(join_summary "$CUMULATIVE")"
if [ -n "$BREAKDOWN" ]; then
  BREAKDOWN_TEXT="$(format_breakdown "$BREAKDOWN")"
  [ -n "$BREAKDOWN_TEXT" ] && add_line "**Per tool** (time/tokens, approx.) $BREAKDOWN_TEXT"
fi
[ ${#DESC} -gt 4000 ] && DESC="${DESC:0:4000}..."

escape_json() {
  local input="$1"
  if [ -z "$input" ]; then echo -n ""; return; fi
  printf '%s' "$input" | tr -d '\r' | tr '\t' ' ' | sed 's/\\/\\\\/g' | sed 's/"/\\"/g' \
    | awk '{printf "%s\\n", $0}' | sed 's/\\n$//'
}

ESC_TITLE=$(escape_json "$TITLE")
ESC_DESC=$(escape_json "$DESC")

DESC_JSON=""
[ -n "$ESC_DESC" ] && DESC_JSON="
  \"description\": \"$ESC_DESC\","

EMBED_JSON=$(cat <<EOF
{
  "title": "$ESC_TITLE",$DESC_JSON
  "color": $COLOR,
  "timestamp": "$TIMESTAMP"
}
EOF
)

if [ "${NOTIFY_DRY_RUN:-}" = "1" ]; then
  printf '%s\n' "$EMBED_JSON"
  exit 0
fi

bash "$SCRIPT_DIR/discord-send.sh" "" "$EMBED_JSON"
