#!/bin/bash
# Double-clickable launcher (macOS Finder) for the backlog loop.
# Finder opens .command files in Terminal and runs them. We resolve our own
# location so it works no matter what the current directory is.
#
# Edit the defaults below, or run run-backlog-loop.sh directly for full flags.

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# --- defaults (change these to taste) -------------------------------------------
MODEL="claude-opus-4-8"     # "" = CLI default; e.g. claude-sonnet-4-6 / claude-opus-4-8
EFFORT="xhigh"              # low | medium | high | xhigh; "" = CLI default
MAX_ITERATIONS=100
THINKING_TOKENS=10000
# --------------------------------------------------------------------------------

echo "Starting Merge Two backlog loop..."
echo "  model=${MODEL:-<default>}  effort=${EFFORT:-<default>}  max-iterations=$MAX_ITERATIONS  thinking=$THINKING_TOKENS"
echo ""

ARGS=(--max-iterations "$MAX_ITERATIONS" --thinking-tokens "$THINKING_TOKENS")
[ -n "$MODEL" ] && ARGS+=(--model "$MODEL")
[ -n "$EFFORT" ] && ARGS+=(--effort "$EFFORT")

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
