#!/usr/bin/env bash
# Stage ONLY the paths handed in on the command line — the session-scoped twin of
# git_prepare.sh (which stages the whole tree with `git add .`). /push-in-session parses
# this output, so keep it in lockstep with git_prepare_scoped.ps1: same markers, same
# NO_CHANGES contract.
#
# Deliberately does NOT print the diff. The agent calling this already knows what it
# changed this session; re-reading the diff only burns tokens. Use git_prepare.sh when
# a real diff review is wanted.
#
# Output contract (identical on both platforms):
#   NO_CHANGES                       <- none of the given paths is dirty; caller stops
#   --- STAGED ---                   <- git diff --cached --name-status
#   --- STAT ---                     <- git diff --cached --stat
#   --- SKIPPED (not found) ---      <- paths that exist neither on disk nor in the index
#   --- DIRTY OUTSIDE SESSION ---    <- still-dirty files left untouched (max 20 + count)
#   --- REMOTE ---                   <- `origin` or `none`; caller skips push when none
#
# Usage:  bash .claude/scripts/git_prepare_scoped.sh "path/one.cs" "path/two.prefab" ...

set -e
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

if [ "$#" -eq 0 ]; then
  echo "ERROR: at least one path is required." >&2
  echo 'Usage: bash .claude/scripts/git_prepare_scoped.sh "<path>" ["<path>" ...]' >&2
  exit 1
fi

# `git add` dies on a pathspec matching nothing, which would abort the whole run over one
# stale path. Split the list instead: stage what is real, report the rest.
paths=()
missing=()
for p in "$@"; do
  if [ -e "$p" ] || git ls-files --error-unmatch -- "$p" >/dev/null 2>&1; then
    paths+=("$p")
  else
    missing+=("$p")
  fi
done

if [ "${#paths[@]}" -gt 0 ]; then
  # -A so a deleted file in the list is staged as a deletion, not silently ignored.
  git add -A -- "${paths[@]}"
fi

staged="$(git diff --cached --name-status)"
if [ -z "$staged" ]; then
  echo "NO_CHANGES"
  if [ "${#missing[@]}" -gt 0 ]; then
    echo "--- SKIPPED (not found) ---"
    printf '%s\n' "${missing[@]}"
  fi
  exit 0
fi

echo "--- STAGED ---"
printf '%s\n' "$staged"
echo "--- STAT ---"
git diff --cached --stat

if [ "${#missing[@]}" -gt 0 ]; then
  echo "--- SKIPPED (not found) ---"
  printf '%s\n' "${missing[@]}"
fi

# Index column is a space (worktree-only change) or `?` (untracked) => nothing of this file
# is staged, so it is somebody else's work: Unity auto-dirt, or the dev editing in parallel.
others="$(git status --porcelain | grep '^[ ?]' || true)"
if [ -n "$others" ]; then
  count="$(printf '%s\n' "$others" | wc -l | tr -d ' ')"
  echo "--- DIRTY OUTSIDE SESSION ($count) ---"
  printf '%s\n' "$others" | head -20
  if [ "$count" -gt 20 ]; then
    echo "... (+$((count - 20)) more, not staged)"
  fi
fi

echo "--- REMOTE ---"
if git remote get-url origin >/dev/null 2>&1; then
  echo "origin"
else
  echo "none"
fi
