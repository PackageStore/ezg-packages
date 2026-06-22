#!/usr/bin/env bash
git add .
status=$(git status -s)
if [ -z "$status" ]; then
    echo "NO_CHANGES"
else
    echo "--- STATUS ---"
    echo "$status"
    echo "--- STAT ---"
    git diff --cached --stat
    echo "--- DIFF (first 80 lines) ---"
    git diff --cached -- | head -n 80
fi
