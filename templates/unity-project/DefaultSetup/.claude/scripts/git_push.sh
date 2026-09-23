#!/usr/bin/env bash
# Commit whatever git_prepare.sh staged, then push. macOS/Linux twin of git_push.ps1 —
# keep the two in lockstep (same argument shape, same commit-then-push behavior).
#
# The message is passed as ONE argument and may contain newlines, so trailers work:
#   bash .claude/scripts/git_push.sh "$(printf 'subject\n\nCo-Authored-By: ...')"
#
# Usage:  bash .claude/scripts/git_push.sh "<commit message>"

set -e
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

# git_push.ps1 declares the message Mandatory (PowerShell prompts when it is missing).
# A shell script has no prompt to fall back on, so fail loudly instead of committing
# something empty.
message="${1:-}"
if [ -z "$message" ]; then
  echo "ERROR: commit message is required." >&2
  echo 'Usage: bash .claude/scripts/git_push.sh "<commit message>"' >&2
  exit 1
fi

git commit -m "$message"

# A branch with no upstream yet (e.g. `agent/dev-<base>` on its first worktree-mode push)
# makes bare `git push` die with "has no upstream branch". Publish it to origin under the
# same name and set the upstream in that case only; a tracked branch keeps plain `git push`.
if git rev-parse --abbrev-ref --symbolic-full-name '@{u}' >/dev/null 2>&1; then
  git push
else
  git push -u origin HEAD
fi
