#!/usr/bin/env bash
# Stage every change and dump the staged diff for /push to write a commit message from.
# macOS/Linux twin of git_prepare.ps1 — keep the two in lockstep: same output markers,
# same NO_CHANGES contract. /push parses these markers, so renaming one breaks the
# workflow on that platform only (the nastiest kind of drift).
#
# Output contract (identical on both platforms):
#   NO_CHANGES                     <- nothing staged; /push stops here
#   --- STATUS ---                 <- git status -s
#   --- STAT ---                   <- git diff --cached --stat
#   --- DIFF (first 80 lines) ---  <- git diff --cached, truncated
#
# Usage:  bash .claude/scripts/git_prepare.sh

set -e
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

git add .

status="$(git status -s)"
if [ -z "$status" ]; then
  echo "NO_CHANGES"
  exit 0
fi

echo "--- STATUS ---"
printf '%s\n' "$status"
echo "--- STAT ---"
git diff --cached --stat
echo "--- DIFF (first 80 lines) ---"
# No `set -o pipefail` here on purpose: head closing the pipe early is the normal
# case for a big diff, and pipefail would turn that SIGPIPE into a script failure.
git diff --cached | head -80
