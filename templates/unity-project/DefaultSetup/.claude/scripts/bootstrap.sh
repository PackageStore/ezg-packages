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
note "[1/7] Link view"
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
note "[2/7] Git repository"
if git rev-parse --git-dir >/dev/null 2>&1; then
  note "  OK  already a git repository"
else
  note "  NEW running 'git init' (the backlog lives in .git/ and needs one)"
  git init >/dev/null 2>&1 || fail "git init failed"
fi
note ""

# --- 3. backlog -------------------------------------------------------------
note "[3/7] Backlog"
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
note "[4/7] UI kit"
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

# --- 5. Art style -----------------------------------------------------------
# ArtStyle.md is OWNED BY THE PROJECT (its palette, kit, rejected directions) and is
# never shipped by the template — only ArtStyle.template.md is, so a template update
# can never overwrite a filled-in style guide. Copy the frame when the project has
# none; an unfilled one is a next-step note, not a failure.
note "[5/7] Art style"
DOCS_DIR="$(cd "$SCRIPT_DIR/../docs" 2>/dev/null && pwd)"
ART_STATE=""
if [ -n "$DOCS_DIR" ] && [ -f "$DOCS_DIR/ArtStyle.md" ]; then
  if grep -q '^\*\*Status:\*\* template' "$DOCS_DIR/ArtStyle.md"; then
    ART_STATE="template"; note "  WARN ArtStyle.md is still the empty frame (see next steps)"
  else
    note "  OK  ArtStyle.md present"
  fi
elif [ -n "$DOCS_DIR" ] && [ -f "$DOCS_DIR/ArtStyle.template.md" ]; then
  cp "$DOCS_DIR/ArtStyle.template.md" "$DOCS_DIR/ArtStyle.md" \
    && { ART_STATE="template"; note "  NEW ArtStyle.md created from the template"; } \
    || fail "could not create ArtStyle.md"
else
  note "  --  no ArtStyle.template.md in this setup — skipped"
fi
note ""

# --- 6. QA profile ----------------------------------------------------------
# QaProfile.md (+ docs/qa/charters/) is OWNED BY THE PROJECT: the conventions, live/dead
# screens, verified cheats and test charters that /auto-test-device reads. The template
# ships only QaProfile.template.md, so a template update can never overwrite it. Copy the
# frame when the project has none; the real values come from
# `qadevice.py profile init` (it scans the repo), which is a next-step note, not a failure.
note "[6/7] QA profile"
QA_STATE=""
if [ -n "$DOCS_DIR" ] && [ -f "$DOCS_DIR/QaProfile.md" ]; then
  if grep -q '^\*\*Status:\*\* template' "$DOCS_DIR/QaProfile.md"; then
    QA_STATE="template"; note "  WARN QaProfile.md is still the empty frame (see next steps)"
  else
    note "  OK  QaProfile.md present"
  fi
elif [ -n "$DOCS_DIR" ] && [ -f "$DOCS_DIR/QaProfile.template.md" ]; then
  cp "$DOCS_DIR/QaProfile.template.md" "$DOCS_DIR/QaProfile.md" \
    && { QA_STATE="template"; note "  NEW QaProfile.md created from the template"; } \
    || fail "could not create QaProfile.md"
else
  note "  --  no QaProfile.template.md in this setup — skipped"
fi
if [ -n "$DOCS_DIR" ] && [ -f "$DOCS_DIR/QaProfile.md" ] && [ ! -d "$DOCS_DIR/qa/charters" ]; then
  mkdir -p "$DOCS_DIR/qa/charters" \
    && note "  NEW docs/qa/charters/ created (test charters live here, committed with the repo)" \
    || fail "could not create docs/qa/charters"
fi
note ""

# --- 7. Project instructions ------------------------------------------------
# CLAUDE.md is OWNED BY THE TEMPLATE (Feature Hub / agents-update overwrite it) and
# imports CLAUDE_PROJECT.md, which is OWNED BY THE PROJECT and never shipped — only
# docs/CLAUDE_PROJECT.template.md is. Copy the frame to the repo root when there is
# none, so the import always resolves; an unfilled one is a next-step note, not a
# failure. An existing file is never touched.
note "[7/7] Project instructions"
PROJ_DOC="$REPO_ROOT/CLAUDE_PROJECT.md"
PROJ_STATE=""
if [ -f "$PROJ_DOC" ]; then
  if grep -q '^\*\*Status:\*\* template' "$PROJ_DOC"; then
    PROJ_STATE="template"; note "  WARN CLAUDE_PROJECT.md is still the empty frame (see next steps)"
  else
    note "  OK  CLAUDE_PROJECT.md present"
  fi
elif [ -n "$DOCS_DIR" ] && [ -f "$DOCS_DIR/CLAUDE_PROJECT.template.md" ]; then
  cp "$DOCS_DIR/CLAUDE_PROJECT.template.md" "$PROJ_DOC" \
    && { PROJ_STATE="template"; note "  NEW CLAUDE_PROJECT.md created from the template"; } \
    || fail "could not create CLAUDE_PROJECT.md"
else
  note "  --  no CLAUDE_PROJECT.template.md in this setup — skipped"
fi
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

if [ "$ART_STATE" = "template" ]; then
  note "  - [ACTION] Fill in .claude/docs/ArtStyle.md (palette, UI kit, layout, fonts) — every visual"
  note "    skill reads it. Its § Bootstrap says how to draft it from the project's own art."
fi

if [ "$QA_STATE" = "template" ]; then
  note "  - [ACTION] Draft the QA profile: python3 .claude/skills/auto-test-device/scripts/qadevice.py profile init"
  note "    (scans the repo, fills .claude/project-profile.json[\"qa\"] + .claude/docs/QaProfile.md — review, then commit)."
fi

if [ "$PROJ_STATE" = "template" ]; then
  note "  - [ACTION] Fill in CLAUDE_PROJECT.md (game, gameplay buckets, project-only rules) — Claude loads it"
  note "    in every session through CLAUDE.md. Never edit CLAUDE.md itself: template updates overwrite it."
fi

note "  - [ACTION] Open the project in Unity, then run /setup-project in Claude Code — it opens the"
note "    Ezg > EzgKit window (identity, marketing sheet, ads & privacy, IAP list, ArtStyle, localization)."
note "  - Queue work with /planning-task then /add-to-backlog, and run it with /run-backlog."
note ""

if [ "$step_failed" -ne 0 ]; then
  note "=== bootstrap INCOMPLETE — fix the errors above and re-run ==="
  exit 1
fi
note "=== bootstrap done ==="
