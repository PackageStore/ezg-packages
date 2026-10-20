#!/usr/bin/env bash
# agents-update.sh — refresh an EXISTING project's agent system from this template.
#
# The full builder already refreshes `.claude/` when you re-run it, but it also
# resolves packages, wants a Unity install and rewrites ProjectSettings. When all
# you want is the newer skills/agents/scripts, that is a lot of machinery and a
# lot of risk for a documentation update. This does only the agent system.
#
# What it will NOT touch:
#   - project-profile.json and .mcp.json — seeded once, then owned by the project.
#     The list is READ FROM the builder (DEFAULT_SETUP_PRESERVE) rather than copied
#     here, so the two can never disagree about what is seed-once.
#   - .claude/docs/ArtStyle.md (+ docs/ArtStyle/) — the project's own art direction; the
#     template ships only the empty frame docs/ArtStyle.template.md, which bootstrap copies once.
#   - .claude/docs/QaProfile.md (+ docs/qa/) — the project's own QA profile and test charters
#     (/auto-test-device); the template ships only docs/QaProfile.template.md, copied once by bootstrap.
#   - CLAUDE_PROJECT.md — the project's own half of CLAUDE.md (frame:
#     docs/CLAUDE_PROJECT.template.md, copied once by bootstrap).
#   - anything outside `.claude/`, the `.agents/` link view and CLAUDE.md. CLAUDE.md is
#     template-owned and gets updated too — but only once CLAUDE_PROJECT.md exists, since
#     before the split the project's own notes may still live inside CLAUDE.md.
#
# Base files the project EDITED IN PLACE (content differs from the baseline recorded in
# .claude/ezg-base.lock.json) are KEPT and listed — overwriting them would lose the edit. Run
# /update-ai in the project afterwards: it moves each edit into .claude/project/overrides/ (or
# CLAUDE_PROJECT.md) and then takes the new base. The lock is refreshed at the end.
# /update-ai is the everyday update path (no monorepo clone needed); this script stays for
# maintainers who have the template checked out.
#
# Files the template no longer ships are REPORTED, not deleted: a project may have
# added its own skills next to the shipped ones, and there is no safe way to tell
# "upstream dropped this" from "the project added this". Use --prune once you have
# read the list.
#
# Usage:
#   bash agents-update.sh <project-path> [--dry-run] [--prune]

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC="$SCRIPT_DIR/DefaultSetup/.claude"
BUILDER="$SCRIPT_DIR/build_unity_template.logic.sh"
DRY_RUN=0
PRUNE=0
TARGET=""

while [ $# -gt 0 ]; do
  case "$1" in
    --dry-run) DRY_RUN=1; shift ;;
    --prune)   PRUNE=1; shift ;;
    -h|--help) sed -n '2,27p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    -*) echo "unknown option: $1" >&2; exit 1 ;;
    *) TARGET="$1"; shift ;;
  esac
done

[ -n "$TARGET" ] || { echo "usage: bash agents-update.sh <project-path> [--dry-run] [--prune]" >&2; exit 1; }
[ -d "$TARGET" ] || { echo "ERROR: no such directory: $TARGET" >&2; exit 1; }
[ -d "$SRC" ]    || { echo "ERROR: template .claude/ missing at $SRC" >&2; exit 1; }
TARGET="$(cd "$TARGET" && pwd)"

# A project that has never been generated from this template has no `.claude/`.
# Copying into it would half-install the system without a profile or a backlog,
# which looks like it worked until the first /run-backlog.
[ -d "$TARGET/.claude" ] || {
  echo "ERROR: $TARGET has no .claude/ — this updates an existing install." >&2
  echo "       For a new project, generate it with build_unity_template.sh instead." >&2
  exit 1
}

# Read the seed-once list from the builder so the two stay in agreement.
PRESERVE=()
if [ -f "$BUILDER" ]; then
  while IFS= read -r line; do
    PRESERVE+=("$line")
  done < <(sed -n 's/^DEFAULT_SETUP_PRESERVE=(\(.*\))$/\1/p' "$BUILDER" | tr ' ' '\n' | tr -d '"' | grep '^\.claude/' || true)
fi
[ ${#PRESERVE[@]} -gt 0 ] || PRESERVE=(".claude/project-profile.json")

say() { printf '%s\n' "$*"; }
run() { if [ "$DRY_RUN" = "1" ]; then say "  [dry] $*"; else "$@"; fi; }

is_preserved() {  # is_preserved <path-relative-to-project-root>
  local rel="$1" p
  for p in "${PRESERVE[@]}"; do [ "$p" = "$rel" ] && return 0; done
  return 1
}

# Base files edited in place, per the project's lock (needs python3; none without a lock).
BASE_TOOL="$SRC/skills/update-ai/scripts/ezg_base.py"
EDITED=()
if [ -f "$TARGET/.claude/ezg-base.lock.json" ] && [ -f "$BASE_TOOL" ] && command -v python3 >/dev/null 2>&1; then
  while IFS= read -r line; do
    [ -n "$line" ] && EDITED+=("$line")
  done < <(python3 "$BASE_TOOL" --root "$TARGET" modified --include-merge 2>/dev/null || true)
fi
is_edited() {  # is_edited <path-relative-to-project-root>
  local rel="$1" p
  for p in "${EDITED[@]+"${EDITED[@]}"}"; do [ "$p" = "$rel" ] && return 0; done
  return 1
}

say "=== agents-update ==="
say "template : $SRC"
say "project  : $TARGET"
say "preserve : ${PRESERVE[*]}"
[ "$DRY_RUN" = "1" ] && say "mode     : DRY RUN (no writes)"
say ""

updated=0; added=0; skipped=0; kept=0
while IFS= read -r -d '' src_file; do
  rel_claude="${src_file#$SRC/}"
  rel=".claude/$rel_claude"
  dst="$TARGET/$rel"
  # Local junk in the template checkout (bytecode from running the tests, Finder metadata) is not
  # part of the template: the published defaultsetup.tgz is built from a clean CI checkout.
  case "$rel_claude" in
    __pycache__/*|*/__pycache__/*|*.pyc|.DS_Store|*/.DS_Store) continue ;;
  esac

  if is_edited "$rel"; then
    say "  keep    $rel  (edited in place — run /update-ai to move the edit into an override)"
    kept=$((kept + 1))
    continue
  fi

  if is_preserved "$rel"; then
    say "  keep    $rel  (seed-once, owned by the project)"
    skipped=$((skipped + 1))
    continue
  fi

  if [ ! -e "$dst" ]; then
    run mkdir -p "$(dirname "$dst")"
    run cp -p "$src_file" "$dst"
    say "  add     $rel"
    added=$((added + 1))
  elif ! cmp -s "$src_file" "$dst"; then
    run cp -p "$src_file" "$dst"
    say "  update  $rel"
    updated=$((updated + 1))
  fi
done < <(find "$SRC" -type f -print0)

# CLAUDE.md: template-owned (a project's own instructions live in CLAUDE_PROJECT.md, which
# CLAUDE.md imports and which nothing here ever touches), so it is refreshed like any
# .claude/ file. Except before the split: a project with no CLAUDE_PROJECT.md may still keep
# its own notes inside CLAUDE.md, and overwriting would bury them in git history. Hold it
# then and say how to unblock — never guess which lines were the project's.
say ""
say "--- CLAUDE.md (template-owned; the project's own half is CLAUDE_PROJECT.md)"
SRC_CLAUDE_MD="$SCRIPT_DIR/DefaultSetup/CLAUDE.md"
held=0
if [ ! -f "$SRC_CLAUDE_MD" ]; then
  say "  (the template ships no CLAUDE.md)"
elif [ ! -e "$TARGET/CLAUDE.md" ]; then
  run cp -p "$SRC_CLAUDE_MD" "$TARGET/CLAUDE.md"
  say "  add     CLAUDE.md"
  added=$((added + 1))
elif cmp -s "$SRC_CLAUDE_MD" "$TARGET/CLAUDE.md"; then
  say "  (up to date)"
elif is_edited "CLAUDE.md"; then
  say "  keep    CLAUDE.md  (edited in place — /update-ai moves the project's notes into CLAUDE_PROJECT.md)"
  kept=$((kept + 1))
elif [ ! -f "$TARGET/CLAUDE_PROJECT.md" ]; then
  say "  hold    CLAUDE.md  (no CLAUDE_PROJECT.md yet — the old CLAUDE.md may hold this project's own notes)"
  held=1
else
  run cp -p "$SRC_CLAUDE_MD" "$TARGET/CLAUDE.md"
  say "  update  CLAUDE.md"
  updated=$((updated + 1))
fi

# Orphans: present in the project, absent from the template.
say ""
say "--- in the project but no longer shipped by the template:"
orphans=0
while IFS= read -r -d '' dst_file; do
  rel_claude="${dst_file#$TARGET/.claude/}"
  rel=".claude/$rel_claude"
  is_preserved "$rel" && continue
  [ -e "$SRC/$rel_claude" ] && continue
  # Generated artifacts and local state are expected to be here.
  case "$rel_claude" in
    ui-kit/ui-kit.json|ui-kit/ui-kit.css|ui-kit/kit-preview.html|ui-kit/ui-kit-usage.json) continue ;;
    tmp/*|state|state/*|__pycache__/*|*/__pycache__/*) continue ;;
    # Project-owned art direction: the template ships only docs/ArtStyle.template.md, so the
    # filled-in copy (and its generated reference boards) always looks orphaned — never prune it.
    docs/ArtStyle.md|docs/ArtStyle/*) continue ;;
    # Project-owned QA profile + charters: the template ships only docs/QaProfile.template.md;
    # the filled-in copy and the charters /auto-test-device writes always look orphaned — never prune.
    docs/QaProfile.md|docs/qa/*) continue ;;
    # Project-owned by design (rule base-ownership): overrides, pins, project usage notes, the
    # project's own local-* skills/commands/agents/rules, and the lock the tooling maintains.
    project/*|ezg-base.lock.json) continue ;;
    skills/local-*|commands/local-*|agents/local-*|rules/local-*) continue ;;
  esac
  if [ "$PRUNE" = "1" ]; then
    run rm -f "$dst_file"
    say "  prune   $rel"
    # Drop folders the prune left empty (a removed skill would otherwise stay as an empty dir).
    [ "$DRY_RUN" = "1" ] || ( d="$(dirname "$dst_file")"
      while [ "$d" != "$TARGET/.claude" ] && rmdir "$d" 2>/dev/null; do d="$(dirname "$d")"; done )
  else
    say "  orphan  $rel"
  fi
  orphans=$((orphans + 1))
done < <(find "$TARGET/.claude" -type f -print0)
[ "$orphans" -eq 0 ] && say "  (none)"

# New link targets appear whenever the template grows a top-level folder under
# .claude/ — backlog-templates/ and ui-kit/ both arrived that way. Without this
# the update lands the files and every non-Claude tool keeps reading a stale view.
say ""
if [ "$DRY_RUN" = "1" ]; then
  say "  [dry] would refresh the .agents/ link view"
elif [ -f "$TARGET/.claude/scripts/sync-to-agents.sh" ]; then
  say "--- refreshing the .agents/ link view"
  ( cd "$TARGET" && bash .claude/scripts/sync-to-agents.sh ) | sed 's/^/  /'
fi

# Record the new baselines (files now equal to the template) so the edit guard and /update-ai know
# which files are the base's and which were edited.
if [ "$DRY_RUN" = "1" ]; then
  say "  [dry] would refresh .claude/ezg-base.lock.json"
elif [ -f "$BASE_TOOL" ] && command -v python3 >/dev/null 2>&1; then
  python3 "$BASE_TOOL" --root "$TARGET" lock-init --from-dir "$SCRIPT_DIR/DefaultSetup" | sed 's/^/  /'
fi

say ""
say "=== $added added, $updated updated, $skipped preserved, $kept kept (edited), $orphans orphaned ==="
if [ "$kept" -gt 0 ]; then
  say "Edited base files were kept. In the project, run /update-ai: it moves each edit into"
  say ".claude/project/overrides/ (or CLAUDE_PROJECT.md) and then takes the new base."
fi
if [ "$orphans" -gt 0 ] && [ "$PRUNE" = "0" ]; then
  say "Re-run with --prune to delete the orphans listed above."
fi
if [ "$held" = "1" ]; then
  say "CLAUDE.md was NOT updated: run 'bash .claude/scripts/bootstrap.sh' in the project (it creates"
  say "CLAUDE_PROJECT.md from .claude/docs/CLAUDE_PROJECT.template.md), move the project's own notes"
  say "from CLAUDE.md into it, then re-run this script."
fi
say ""
say "Next: re-read .claude/docs/GETTING-STARTED.md if the profile grew new keys,"
say "      and run 'python3 .claude/scripts/project_profile.py' to see the merged result."
