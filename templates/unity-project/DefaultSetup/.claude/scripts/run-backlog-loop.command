#!/bin/bash
# Double-clickable launcher (macOS Finder) for the backlog loop.
# Finder opens .command files in Terminal and runs them. We resolve our own
# location so it works no matter what the current directory is.
#
# Edit the defaults below, or run run-backlog-loop.sh directly for full flags.

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# --- defaults (change these to taste) -------------------------------------------
# Auto mode picks the next task's [XS]/[S]/[M]/[L] tier from BACKLOG.md before
# opening each task window:
#   XS/S -> claude-sonnet-4-6
#   M/L  -> claude-opus-4-8
AUTO_MODEL_BY_TIER=1

# Used only when AUTO_MODEL_BY_TIER=0.
MODEL="claude-opus-4-8"     # "" = CLI default; e.g. claude-sonnet-4-6 / claude-opus-4-8
EFFORT="xhigh"              # low | medium | high | xhigh; "" = CLI default
MAX_ITERATIONS=100
THINKING_TOKENS=10000
# --------------------------------------------------------------------------------

echo "Starting [Project Name] backlog loop..."
if [ "$AUTO_MODEL_BY_TIER" -eq 1 ]; then
    echo "  model=<auto by task tier>  max-iterations=$MAX_ITERATIONS  thinking=$THINKING_TOKENS"
else
    echo "  model=${MODEL:-<default>}  effort=${EFFORT:-<default>}  max-iterations=$MAX_ITERATIONS  thinking=$THINKING_TOKENS"
fi
echo ""

ARGS=(--max-iterations "$MAX_ITERATIONS" --thinking-tokens "$THINKING_TOKENS")
if [ "$AUTO_MODEL_BY_TIER" -eq 1 ]; then
    ARGS+=(--auto-model-by-tier)
else
    [ -n "$MODEL" ] && ARGS+=(--model "$MODEL")
    [ -n "$EFFORT" ] && ARGS+=(--effort "$EFFORT")
fi

bash "$DIR/run-backlog-loop.sh" "${ARGS[@]}"
status=$?

echo ""
if [ $status -eq 0 ]; then
    echo "✅ Loop finished. You can close this window."
else
    echo "❌ Loop exited with status $status. See messages above."
fi
echo ""
read -n 1 -s -r -p "Press any key to close..."
echo ""
exit $status
