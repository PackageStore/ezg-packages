#!/usr/bin/env bash
# codegraph-doctor.sh — cổng "phải có codegraph" cho repo này (đặc biệt trước khi
# làm việc với tool UI Catalog / @ui-catalog/, vốn dựa vào codegraph để khám phá code).
#
# Kiểm tra 3 thứ (đúng như đã chốt):
#   1) CLI 'codegraph' có trên PATH                (npm i -g @colbymchenry/codegraph)
#   2) .codegraph/ index đã build TRÊN MÁY NÀY     (db bị gitignore → clone về KHÔNG có, phải index lại)
#   3) MCP server 'codegraph' có trong config repo (.mcp.json + .claude/settings.json)
#
# Mặc định = CHECK-ONLY: in trạng thái + cách fix, exit 1 nếu thiếu (để wire vào
# bootstrap/CI làm cổng chặn). Thêm cờ --fix để TỰ remediate (cài CLI + build index).
#
# Usage:
#   bash .claude/scripts/codegraph-doctor.sh          # chỉ kiểm tra
#   bash .claude/scripts/codegraph-doctor.sh --fix    # kiểm tra + tự cài/index
set -uo pipefail

PKG="@colbymchenry/codegraph"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$REPO_ROOT"

FIX=0
[ "${1:-}" = "--fix" ] && FIX=1

# Màu (tắt khi không phải terminal, để log/CI sạch)
if [ -t 1 ]; then R=$'\e[31m'; G=$'\e[32m'; Y=$'\e[33m'; B=$'\e[1m'; N=$'\e[0m'; else R=''; G=''; Y=''; B=''; N=''; fi
pass() { printf "  ${G}✓${N} %s\n" "$1"; }
fail() { printf "  ${R}✗${N} %s\n" "$1"; }
warn() { printf "  ${Y}!${N} %s\n" "$1"; }
hint() { printf "      fix: ${B}%s${N}\n" "$1"; }

FAILED=0

printf "${B}CodeGraph doctor${N}  (repo: %s)\n\n" "$REPO_ROOT"

# --- prereq: npm (cần để cài CLI) -------------------------------------------
HAS_NPM=1
command -v npm >/dev/null 2>&1 || HAS_NPM=0

# --- 1) CLI trên PATH -------------------------------------------------------
if command -v codegraph >/dev/null 2>&1; then
  pass "CLI: codegraph $(codegraph --version 2>/dev/null) ($(command -v codegraph))"
else
  fail "CLI: 'codegraph' chưa có trên PATH"
  if [ "$FIX" = 1 ] && [ "$HAS_NPM" = 1 ]; then
    printf "      → npm install -g %s\n" "$PKG"
    if npm install -g "$PKG"; then pass "CLI: đã cài"; else fail "CLI: cài thất bại"; FAILED=1; fi
  else
    [ "$HAS_NPM" = 0 ] && warn "npm chưa có — cài Node.js (https://nodejs.org) trước, không thể auto-cài."
    hint "npm install -g $PKG"
    FAILED=1
  fi
fi

# --- 2) .codegraph/ index đã build trên máy này -----------------------------
# db là file local (bị gitignore toàn bộ .codegraph/ trừ .gitignore) → mỗi máy phải tự index.
if [ -f ".codegraph/codegraph.db" ]; then
  pass ".codegraph/ index: có (codegraph.db)"
else
  fail ".codegraph/ index: chưa build trên máy này (db bị gitignore → clone về không có)"
  if [ "$FIX" = 1 ] && command -v codegraph >/dev/null 2>&1; then
    printf "      → codegraph init\n"
    if codegraph init; then pass ".codegraph/ index: đã build"; else fail "index: build thất bại"; FAILED=1; fi
  else
    hint "codegraph init      # chạy ở repo root"
    FAILED=1
  fi
fi

# --- 3) MCP server config có trong repo (đã commit → sau clone phải có) ------
if [ -f ".mcp.json" ] && grep -q '"codegraph"' .mcp.json; then
  pass ".mcp.json: có server 'codegraph'"
else
  fail ".mcp.json: thiếu server 'codegraph'"
  hint "codegraph install   # thêm MCP server vào agent — hoặc: git checkout .mcp.json"
  FAILED=1
fi
if [ -f ".claude/settings.json" ] && grep -q 'codegraph' .claude/settings.json; then
  pass ".claude/settings.json: 'codegraph' đã enable"
else
  warn ".claude/settings.json: 'codegraph' chưa nằm trong enabledMcpjsonServers (Claude Code sẽ không auto-bật)"
fi

echo
if [ "$FAILED" = 0 ]; then
  printf "${G}${B}codegraph: OK${N} — sẵn sàng dùng tool UI Catalog.\n"
  exit 0
else
  printf "${R}${B}codegraph: CHƯA SẴN SÀNG${N} — chạy lại với ${B}--fix${N} để tự cài, hoặc làm theo 'fix:' ở trên.\n"
  exit 1
fi
