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
#   - anything outside `.claude/` and the `.agents/` link view.
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
    -h|--help) sed -n '2,21p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
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

say "=== agents-update ==="
say "template : $SRC"
say "project  : $TARGET"
say "preserve : ${PRESERVE[*]}"
[ "$DRY_RUN" = "1" ] && say "mode     : DRY RUN (no writes)"
say ""

updated=0; added=0; skipped=0
while IFS= read -r -d '' src_file; do
  rel_claude="${src_file#$SRC/}"
  rel=".claude/$rel_claude"
  dst="$TARGET/$rel"

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
  esac
  if [ "$PRUNE" = "1" ]; then
    run rm -f "$dst_file"
    say "  prune   $rel"
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

say ""
say "=== $added added, $updated updated, $skipped preserved, $orphans orphaned ==="
if [ "$orphans" -gt 0 ] && [ "$PRUNE" = "0" ]; then
  say "Re-run with --prune to delete the orphans listed above."
fi
say ""
say "Next: re-read .claude/docs/GETTING-STARTED.md if the profile grew new keys,"
say "      and run 'python3 .claude/scripts/project_profile.py' to see the merged result."
