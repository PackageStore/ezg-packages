#!/bin/bash
# Double-clickable launcher (macOS Finder) for the .agents/ link sync.
# Finder opens .command files in Terminal and runs them. We resolve our own
# location so it works no matter what the current directory is.

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "Running sync-to-agents..."
echo ""
bash "$DIR/sync-to-agents.sh"
status=$?

echo ""
if [ $status -eq 0 ]; then
    echo "✅ Done. You can close this window."
else
    echo "❌ Failed (exit $status). See messages above."
fi
echo ""
read -n 1 -s -r -p "Press any key to close..."
echo ""
exit $status
