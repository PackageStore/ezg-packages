#!/usr/bin/env bash
# restart-unity.sh — ép restart Unity Editor của project hiện tại NGAY, không hỏi, không save.
#
#   bash .claude/skills/restart-unity/scripts/restart-unity.sh [--dry-run] [--no-launch | --open-only | --status] [--project <path>]
#
#   --dry-run    chỉ in kế hoạch (PID sẽ kill, editor sẽ mở), không đụng gì
#   --no-launch  kill xong thì thôi, không mở lại
#   --open-only  KHÔNG kill: Editor GUI đã mở thì thôi (ALREADY_RUNNING), chưa có thì mở (OPENED) kèm
#                -ignoreCompilerErrors để không kẹt dialog Safe Mode. Dùng cho agent (/fix-bug --watch).
#   --status     chỉ in trạng thái: RUNNING gui=<pid|none> pids=[…] | NOT_RUNNING
#   --project    project root (mặc định: dò ngược từ cwd tới thư mục có ProjectSettings/ProjectVersion.txt)
#
# Luồng: tìm process Unity đang mở đúng project (khớp -projectPath) → kill -9 cả nó lẫn process con →
# gỡ Temp/UnityLockfile + dời Temp/__Backupscenes sang Logs/ (để Editor mới không hiện hộp thoại
# khôi phục scene) → mở lại đúng binary Editor vừa chạy (không có process nào thì lấy version trong
# ProjectVersion.txt → thư mục cài Unity Hub).
# Dòng cuối stdout luôn là một status: RESTARTED pid=<n> | KILLED | OPENED pid=<n> | ALREADY_RUNNING pid=<n>
# | RUNNING … | NOT_RUNNING | DRY_RUN | ERROR <lý do>.
set -uo pipefail

DRY_RUN=0
NO_LAUNCH=0
OPEN_ONLY=0
STATUS_ONLY=0
PROJECT=""

while [ $# -gt 0 ]; do
  case "$1" in
    --dry-run)   DRY_RUN=1 ;;
    --no-launch) NO_LAUNCH=1 ;;
    --open-only) OPEN_ONLY=1 ;;
    --status)    STATUS_ONLY=1 ;;
    --project)   PROJECT="${2:-}"; shift ;;
    -h|--help)   sed -n '2,15p' "$0"; exit 0 ;;
    *) echo "ERROR tham số lạ: $1"; exit 2 ;;
  esac
  shift
done

fail() { echo "ERROR $*"; exit 1; }

[ $((NO_LAUNCH + OPEN_ONLY + STATUS_ONLY)) -le 1 ] || { echo "ERROR --no-launch / --open-only / --status loại trừ nhau"; exit 2; }

#region Project root
find_project_root() {
  local dir="$1"
  while [ -n "$dir" ] && [ "$dir" != "/" ]; do
    [ -f "$dir/ProjectSettings/ProjectVersion.txt" ] && { echo "$dir"; return 0; }
    dir="$(dirname "$dir")"
  done
  return 1
}

if [ -z "$PROJECT" ]; then
  PROJECT="$(find_project_root "$(pwd -P)")" \
    || PROJECT="$(find_project_root "$(git rev-parse --show-toplevel 2>/dev/null || true)")" \
    || fail "không tìm thấy Unity project (thiếu ProjectSettings/ProjectVersion.txt) từ $(pwd)"
fi
[ -f "$PROJECT/ProjectSettings/ProjectVersion.txt" ] || fail "$PROJECT không phải Unity project"
PROJECT="$(cd "$PROJECT" && pwd -P)"
echo "project: $PROJECT"
#endregion

#region Tìm process Editor đang mở project này
# Khớp executable Unity (macOS .app/Contents/MacOS/Unity, Linux .../Editor/Unity) có
# -projectPath <PROJECT> (Hub truyền "-projectpath" chữ thường; so không phân biệt hoa thường).
# Sau tên executable phải là " -<tham số>" hoặc hết dòng — "…/Contents/MacOS/Unity Hub -projectPath …" (Unity
# Hub vừa mở project) không phải Editor.
#   $1 = all → mọi process trên project (Editor GUI + AssetImportWorker + build -batchmode đang giữ lock)
#   $1 = gui → chỉ Editor GUI (không có -batchmode)
find_editor_pids() {
  ps -axo pid=,command= 2>/dev/null | awk -v root="$PROJECT" -v mode="${1:-all}" '
    BEGIN { r = tolower(root); sub(/\/+$/, "", r) }
    {
      pid = $1; line = $0; sub(/^[ \t]*[0-9]+[ \t]+/, "", line); l = tolower(line)
      if (l !~ /(\/contents\/macos\/unity|\/editor\/unity)( -| *$)/) next
      if (mode == "gui" && l ~ / -batchmode( |$)/) next
      p = index(l, "-projectpath " r)
      if (p == 0) next
      c = substr(l, p + length("-projectpath " r), 1)
      if (c == "" || c == " " || c == "/") print pid
    }'
}

PIDS="$(find_editor_pids all | tr '\n' ' ' | sed 's/ *$//')"
GUI_PID="$(find_editor_pids gui | head -1)"
EDITOR_EXE=""
if [ -n "$PIDS" ]; then
  first="${GUI_PID:-${PIDS%% *}}"
  EDITOR_EXE="$(ps -o comm= -p "$first" 2>/dev/null | sed 's/^ *//')"
  echo "editor đang chạy: gui=${GUI_PID:-không} kill=[$PIDS] exe=$EDITOR_EXE"
else
  echo "editor đang chạy: không có"
fi

if [ "$STATUS_ONLY" = 1 ]; then
  if [ -n "$PIDS" ]; then echo "RUNNING gui=${GUI_PID:-none} pids=[$PIDS]"; else echo "NOT_RUNNING"; fi
  exit 0
fi
if [ "$OPEN_ONLY" = 1 ]; then
  [ -n "$GUI_PID" ] && { echo "ALREADY_RUNNING pid=$GUI_PID"; exit 0; }
  # Không có GUI mà vẫn có process giữ project (build -batchmode, import worker mồ côi) → không mở chồng.
  [ -n "$PIDS" ] && fail "BUSY project đang bị process khác giữ (build -batchmode?): [$PIDS]"
fi
#endregion

#region Resolve binary Editor để mở lại
resolve_editor_from_version() {
  local ver sip base cand
  ver="$(awk -F': *' '/^m_EditorVersion:/ {print $2; exit}' "$PROJECT/ProjectSettings/ProjectVersion.txt" | tr -d '\r ')"
  [ -n "$ver" ] || return 1
  if [ "$(uname -s)" = "Darwin" ]; then
    sip="$(tr -d '"\r\n' < "$HOME/Library/Application Support/UnityHub/secondaryInstallPath.json" 2>/dev/null || true)"
    for base in "$sip" "/Applications/Unity/Hub/Editor" "/Applications/Unity"; do
      [ -n "$base" ] || continue
      cand="$base/$ver/Unity.app/Contents/MacOS/Unity"
      [ -x "$cand" ] && { echo "$cand"; return 0; }
    done
  else
    sip="$(tr -d '"\r\n' < "$HOME/.config/UnityHub/secondaryInstallPath.json" 2>/dev/null || true)"
    for base in "$sip" "$HOME/Unity/Hub/Editor" "/opt/unity/editors"; do
      [ -n "$base" ] || continue
      cand="$base/$ver/Editor/Unity"
      [ -x "$cand" ] && { echo "$cand"; return 0; }
    done
  fi
  echo "VERSION_NOT_INSTALLED:$ver"
  return 1
}

if [ -z "$EDITOR_EXE" ] || [ ! -x "$EDITOR_EXE" ]; then
  EDITOR_EXE="$(resolve_editor_from_version)" || {
    [ "$NO_LAUNCH" = 1 ] || fail "không tìm thấy Unity Editor để mở (${EDITOR_EXE:-thiếu m_EditorVersion})"
    EDITOR_EXE=""
  }
fi
if [ "$NO_LAUNCH" = 1 ]; then echo "editor sẽ mở: (không mở, --no-launch)"; else echo "editor sẽ mở: $EDITOR_EXE"; fi
#endregion

if [ "$DRY_RUN" = 1 ]; then
  echo "DRY_RUN kill=[$([ "$OPEN_ONLY" = 1 ] || echo "${PIDS:-}")] launch=$([ "$NO_LAUNCH" = 1 ] && echo no || echo yes)"
  exit 0
fi

#region Kill -9 (không save, không hỏi)
if [ -n "$PIDS" ]; then
  for pid in $PIDS; do
    pkill -9 -P "$pid" 2>/dev/null || true   # UPM server, shader compiler, ILPP…
    kill -9 "$pid" 2>/dev/null || true
  done
  for _ in $(seq 1 50); do                     # chờ tối đa ~10s cho process chết hẳn
    alive=0
    for pid in $PIDS; do kill -0 "$pid" 2>/dev/null && alive=1; done
    [ "$alive" = 0 ] && break
    sleep 0.2
  done
  [ "$alive" = 0 ] || fail "process $PIDS vẫn sống sau kill -9"
  echo "killed: $PIDS"
fi
#endregion

#region Dọn trạng thái để Editor mới mở thẳng
rm -f "$PROJECT/Temp/UnityLockfile" 2>/dev/null || true
if [ -d "$PROJECT/Temp/__Backupscenes" ]; then
  stash="$PROJECT/Logs/restart-unity/__Backupscenes-$(date +%Y%m%d-%H%M%S)"
  mkdir -p "$(dirname "$stash")" && mv "$PROJECT/Temp/__Backupscenes" "$stash" 2>/dev/null \
    && echo "scene backup dời sang: $stash"
fi
#endregion

if [ "$NO_LAUNCH" = 1 ]; then
  echo "KILLED"
  exit 0
fi

#region Mở lại
EXTRA_ARGS=()
[ "$OPEN_ONLY" = 1 ] && EXTRA_ARGS+=(-ignoreCompilerErrors)
if [ "$(uname -s)" = "Darwin" ]; then
  APP="${EDITOR_EXE%/Contents/MacOS/Unity}"
  open -n -a "$APP" --args -projectPath "$PROJECT" ${EXTRA_ARGS[@]+"${EXTRA_ARGS[@]}"} || fail "open -a $APP thất bại"
else
  nohup "$EDITOR_EXE" -projectPath "$PROJECT" ${EXTRA_ARGS[@]+"${EXTRA_ARGS[@]}"} >/dev/null 2>&1 &
  disown 2>/dev/null || true
fi

NEW_PID=""
for _ in $(seq 1 50); do                       # chờ tối đa ~10s cho process mới xuất hiện
  NEW_PID="$(find_editor_pids gui | head -1)"
  [ -n "$NEW_PID" ] && break
  sleep 0.2
done
[ -n "$NEW_PID" ] || fail "đã gọi mở Editor nhưng không thấy process mới sau 10s"
if [ "$OPEN_ONLY" = 1 ]; then echo "OPENED pid=$NEW_PID"; else echo "RESTARTED pid=$NEW_PID"; fi
#endregion
