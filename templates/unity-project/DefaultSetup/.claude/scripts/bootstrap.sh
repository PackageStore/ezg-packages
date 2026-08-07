#!/usr/bin/env bash
# bootstrap.sh — make a checkout usable by the agent system. Run it ONCE after
# cloning this repo, or after generating a project from the base template.
#
# WHY THIS EXISTS
# ---------------
# Three things the agent system needs cannot be shipped as files in git:
#
#   1. The link view (`.claude/` <-> `.agents/`). Links are gitignored on purpose —
#      a tracked symlink breaks `git switch` on Windows, and a symlink that survives
#      a tarball extraction becomes a stale COPY rather than a link.
#   2. The backlog. It lives in the git common dir (`.git/backlog/`), which by
#      construction is not trackable, so a fresh clone always starts without one.
#   3. The UI kit. It is extracted from THIS project's screen-template prefabs and
#      carries a hash of them, so a kit shipped from anywhere else describes the
#      wrong game and every mockup run rejects it as stale.
#
# So both are created here instead. Everything this script does is idempotent —
# re-running it is the supported way to repair a half-broken checkout.
#
# ONE FILE, BOTH DIRECTIONS
# -------------------------
# This repo keeps the real files in `.agents/` and links `.claude/` at them; the
# base template inverts that (real files in `.claude/`, links in `.agents/`). The
# script globs for whichever `sync-to-*.sh` sits next to it rather than naming a
# direction, so the same source works in both places and the template's port step
# has no name to rewrite.
#
# Usage:  bash .agents/scripts/bootstrap.sh
#         bash .claude/scripts/bootstrap.sh     # base-template layout

set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$REPO_ROOT"

step_failed=0

note()  { printf '%s\n' "$*"; }
fail()  { printf '  ERROR: %s\n' "$*" >&2; step_failed=1; }

note "=== bootstrap ==="
note "Repo: $REPO_ROOT"
note ""

# --- python -----------------------------------------------------------------
# Every deterministic script in the toolchain is Python. Find the interpreter
# once and pass it on, so a machine with only `python` on PATH still bootstraps.
PY=""
for candidate in python3 python; do
  if command -v "$candidate" >/dev/null 2>&1; then PY="$candidate"; break; fi
done
if [ -z "$PY" ]; then
  fail "python3 not found on PATH — install Python 3.9+ and re-run."
fi

# --- 1. link view -----------------------------------------------------------
note "[1/4] Link view"
sync_script=""
for candidate in "$SCRIPT_DIR"/sync-to-*.sh; do
  [ -f "$candidate" ] && { sync_script="$candidate"; break; }   # first match wins
done
if [ -n "$sync_script" ]; then
  bash "$sync_script" || fail "$(basename "$sync_script") failed"
else
  fail "no sync-to-*.sh next to this script — cannot create the link view"
fi
note ""

# --- 2. git repository ------------------------------------------------------
# The backlog is stored inside the git common dir, so a project that has not been
# `git init`-ed yet cannot have one. A project generated from the base template is
# exactly that case, and initialising is the only sensible answer — an empty repo
# with no commits and no remote is trivially undone (`rm -rf .git`).
note "[2/4] Git repository"
if git rev-parse --git-dir >/dev/null 2>&1; then
  note "  OK  already a git repository"
else
  note "  NEW running 'git init' (the backlog lives in .git/ and needs one)"
  git init >/dev/null 2>&1 || fail "git init failed"
fi
note ""

# --- 3. backlog -------------------------------------------------------------
note "[3/4] Backlog"
if [ -n "$PY" ] && git rev-parse --git-dir >/dev/null 2>&1; then
  "$PY" "$SCRIPT_DIR/backlog-ops.py" init >/dev/null || fail "backlog-ops.py init failed"
  BACKLOG_ROOT="$(git rev-parse --git-common-dir)/backlog"
  note "  OK  $BACKLOG_ROOT"
else
  fail "skipped (needs python3 + a git repository)"
fi
note ""

# --- 4. UI kit --------------------------------------------------------------
# Generate only when there is nothing there: a MISSING kit blocks /ui-mockup
# outright, while a STALE one is a real diff over tracked, generated files that
# the developer should produce and review deliberately. Neither case is fatal —
# a project without UI prefabs is a perfectly good state, and bootstrap that
# reports INCOMPLETE for it would train everyone to ignore the exit code.
note "[4/4] UI kit"
KIT_STATE=""
if [ -n "$PY" ]; then
  KIT_STATE="$("$PY" "$SCRIPT_DIR/ui-kit-sync.py" --check 2>/dev/null \
    | "$PY" -c 'import json,sys
try: print(json.load(sys.stdin).get("state", ""))
except Exception: print("")' 2>/dev/null)"
fi
case "$KIT_STATE" in
  fresh)        note "  OK  kit matches the screen templates" ;;
  no-templates) note "  --  no screen templates in this project yet — nothing to extract" ;;
  missing)
    note "  NEW generating the UI kit from this project's templates"
    "$PY" "$SCRIPT_DIR/ui-kit-sync.py" >/dev/null 2>&1 \
      || note "  WARN ui-kit-sync.py failed — run it by hand to see why" ;;
  stale)        note "  WARN kit is out of date (see next steps)" ;;
  "")           note "  WARN could not read the kit state (needs python3)" ;;
  *)            note "  WARN kit state: $KIT_STATE (see next steps)" ;;
esac
note ""

# --- what the project still owes --------------------------------------------
# Two things bootstrap deliberately does NOT decide for the developer: the
# per-project profile values, and whether this repo has a remote at all.
note "=== next steps ==="

PROFILE_FILE=""
for candidate in "$REPO_ROOT/.agents/project-profile.json" "$REPO_ROOT/.claude/project-profile.json"; do
  [ -f "$candidate" ] && { PROFILE_FILE="$candidate"; break; }
done
if [ -z "$PROFILE_FILE" ]; then
  note "  - No project-profile.json — the built-in defaults in project_profile.py apply."
  note "    Add one when a path, branch or threat surface differs from the defaults."
elif grep -q '__PROJECT_NAME__\|__PROJECT_SLUG__' "$PROFILE_FILE" 2>/dev/null; then
  note "  - [ACTION] $PROFILE_FILE still has __PROJECT_NAME__ placeholders — fill them in."
else
  note "  - Profile: $PROFILE_FILE"
fi

if git remote get-url origin >/dev/null 2>&1; then
  note "  - Remote: origin is configured; the backlog loop will push each done task."
else
  note "  - No 'origin' remote: the backlog loop commits locally and skips every push."
  note "    That is a supported mode — add a remote whenever you are ready."
fi

case "$KIT_STATE" in
  fresh|no-templates|missing) ;;
  *) note "  - [ACTION] UI kit needs regenerating: python3 .agents/scripts/ui-kit-sync.py"
     note "    (it is generated from the screen templates — see .agents/skills/ui-kit/SKILL.md)" ;;
esac

note "  - Queue work with /planning-task then /add-to-backlog, and run it with /run-backlog."
note ""

if [ "$step_failed" -ne 0 ]; then
  note "=== bootstrap INCOMPLETE — fix the errors above and re-run ==="
  exit 1
fi
note "=== bootstrap done ==="
