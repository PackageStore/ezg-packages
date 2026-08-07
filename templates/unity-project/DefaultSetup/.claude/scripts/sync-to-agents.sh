#!/bin/bash
# Ensure the .agents/ link views exist and point back to .claude/.
#
# Convention in this repo:
#   - .claude/ is the CANONICAL source (real files, tracked in git).
#   - .agents/ holds LINKS to .claude/ so other AI tools (Codex, Gemini, Cline...)
#     that read .agents/ keep working. No file copies, no sync needed.
#       * macOS / Linux: symlinks (ln -s)
#       * Windows: directory junctions — use sync-to-agents.ps1 instead.
#
# Link map (.agents/<link>  ->  .claude/<target>):
#   agents    -> agents
#   rules     -> rules
#   skills    -> skills
#   workflows -> commands     (Claude calls them "commands"; .agents calls them "workflows")
#   scripts   -> scripts
#   docs      -> docs
#   backlog-templates -> backlog-templates
#   ui-kit    -> ui-kit
#
# Run this script ONCE after cloning. Links are gitignored, not stored in git
# (tracking links breaks `git switch` on Windows). Editing files under .claude/
# is reflected through the links instantly — no need to re-run.
#
# Usage:  bash .claude/scripts/sync-to-agents.sh

set -e
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

# link_name  target_name
LINKS=(
  "agents:agents"
  "rules:rules"
  "skills:skills"
  "workflows:commands"
  "scripts:scripts"
  "docs:docs"
  "backlog-templates:backlog-templates"
  "ui-kit:ui-kit"
)

echo "=== .agents link check ==="
echo "Repo: $REPO_ROOT  (platform: POSIX symlink)"
echo ""

mkdir -p .agents
all_ok=1

for pair in "${LINKS[@]}"; do
  link_name="${pair%%:*}"
  target_name="${pair##*:}"
  link_path=".agents/$link_name"
  target_path=".claude/$target_name"
  want="../.claude/$target_name"

  if [ ! -e "$target_path" ]; then
    echo "  ERROR: target $target_path does not exist"
    all_ok=0
    continue
  fi

  if [ -L "$link_path" ] && [ "$(readlink "$link_path")" = "$want" ]; then
    echo "  OK  $link_path -> $want"
    continue
  fi

  if [ -e "$link_path" ] || [ -L "$link_path" ]; then
    echo "  FIX $link_path (wrong/stale - recreating)"
    rm -rf "$link_path"
  else
    echo "  NEW $link_path -> $want"
  fi

  ln -s "$want" "$link_path"
  if [ -L "$link_path" ]; then
    echo "  OK  $link_path -> $want"
  else
    echo "  ERROR: failed to create link $link_path"
    all_ok=0
  fi
done

echo ""
if [ "$all_ok" -eq 1 ]; then
  echo "=== Done - all links OK ==="
else
  echo "=== Done - some links failed (see errors above) ==="
fi

# Cổng "phải có codegraph": kiểm tra codegraph đã sẵn sàng chưa (CLI + .codegraph/
# index của máy này + MCP config) và in cảnh báo loud nếu thiếu. Chạy
# `codegraph-doctor.sh --fix` để tự cài + index.
echo ""
CODEGRAPH_OK=1
bash "$REPO_ROOT/.claude/scripts/codegraph-doctor.sh" || CODEGRAPH_OK=0

if [ "$CODEGRAPH_OK" -ne 1 ]; then
  echo ""
  echo "NOTE: link view vẫn tạo xong; codegraph index là bước riêng (xem 'fix:' ở trên)."
fi

# Exit code CHỈ phản ánh việc tạo link — đúng như tên script, và khớp với
# sync-to-agents.ps1. Project vừa generate thì .codegraph/ chưa thể tồn tại (db bị
# gitignore), nên nếu codegraph cũng chặn thì bootstrap của MỌI project mới đều báo
# INCOMPLETE dù không có gì hỏng.
if [ "$all_ok" -ne 1 ]; then
  exit 1
fi
