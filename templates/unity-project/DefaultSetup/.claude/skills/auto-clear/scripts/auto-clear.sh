#!/usr/bin/env bash
# auto-clear.sh — tự gõ "/clear" vào ĐÚNG pane iTerm2 đang chạy Claude Code ngay sau khi lượt
# trả lời kết thúc. Skill /auto-clear bật cờ; Stop hook (đăng ký bằng `install`) đọc cờ rồi gõ.
# Twin Windows: auto-clear.ps1 (THỬ NGHIỆM) — giữ cùng subcommand, cùng layout ~/.claude/auto-clear/.
#
#   arm [--stdin] [--then "<prompt>"]
#                   bật cờ cho pane hiện tại; --stdin: lưu report cuối (đọc từ stdin) trước khi clear;
#                   --then: sau "/clear" chờ AUTO_CLEAR_THEN_DELAY giây rồi gõ tiếp <prompt> + Enter
#                   (một dòng, không ký tự điều khiển, ≤ 200 ký tự — sai thì không bật cờ)
#   off             gỡ cờ của pane hiện tại
#   status          cờ của pane hiện tại đang bật hay không (kèm prompt --then đang chờ, nếu có)
#   probe           dò pane + so tty, KHÔNG gõ gì — kiểm tra cài đặt / quyền Automation
#   hook            Stop hook: có cờ → gỡ cờ → chờ AUTO_CLEAR_DELAY giây → gõ "/clear" + Enter
#                   (+ prompt --then nếu cờ có, sau AUTO_CLEAR_THEN_DELAY giây)
#   inject <uuid> [tty]   gõ "/clear" + Enter vào pane <uuid> (nội bộ / test)
#   retry-arm [--prompt "<prompt>"] [--max N]
#                   bật THỬ LẠI LỖI API cho pane hiện tại: lượt trả lời chết vì lỗi API của Claude (mất
#                   quyền org, rate limit, overloaded, 5xx…) → StopFailure hook chờ AUTO_CLEAR_RETRY_DELAY
#                   giây rồi gõ <prompt> + Enter, tối đa N lần liên tiếp (mặc định AUTO_CLEAR_RETRY_MAX;
#                   0 = không giới hạn); một lượt kết thúc bình thường (Stop hook) đưa bộ đếm về 0. Cờ
#                   sống tới khi retry-off / off
#   retry-off       gỡ cờ thử lại lỗi API của pane hiện tại
#   failhook        StopFailure hook (nội bộ) — xem retry-arm
#   install         copy script sang ~/.claude/auto-clear/auto-clear.sh rồi đăng ký Stop + StopFailure
#                   hook (async) trỏ vào bản copy đó trong ~/.claude/settings.json — idempotent, có backup
#   uninstall       gỡ cả hai hook khỏi ~/.claude/settings.json + xoá bản copy
#
# Hook là của MÁY (settings user-level), không của repo: một hook phục vụ mọi project cài skill này.
# Vì vậy nó chạy bản copy ở ~/.claude/auto-clear/ chứ không trỏ vào repo — xoá/dời repo không làm hook
# chết âm thầm. `arm` đồng bộ lại bản copy mỗi lần chạy, nên update skill là hook dùng bản mới luôn.
#
# Nhắm pane theo UUID trong ITERM_SESSION_ID ("w0t0p3:<UUID>") qua AppleScript `write text` — ghi
# thẳng vào input của pane đó, KHÔNG gửi phím vào cửa sổ đang focus. Trước khi gõ còn so tty của pane
# với tty của process `claude` đã bật cờ: lệch (tmux/ssh, Claude đã thoát) → bỏ qua, không gõ nhầm.
set -u

STATE_DIR="${HOME}/.claude/auto-clear"
SETTINGS="${HOME}/.claude/settings.json"
LOG="${STATE_DIR}/auto-clear.log"
DELAY="${AUTO_CLEAR_DELAY:-1}"
THEN_DELAY="${AUTO_CLEAR_THEN_DELAY:-3}"
THEN_MAX=200
RETRY_DELAY="${AUTO_CLEAR_RETRY_DELAY:-30}"
RETRY_MAX="${AUTO_CLEAR_RETRY_MAX:-5}"
RETRY_PROMPT_DEFAULT="Tiếp tục: lượt trước dừng vì lỗi API — làm tiếp đúng bước đang dở."
SCRIPT_PATH="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/$(basename "${BASH_SOURCE[0]}")"
HOOK_SCRIPT="${STATE_DIR}/auto-clear.sh"

log() { mkdir -p "$STATE_DIR"; printf '%s %s\n' "$(date '+%F %T')" "$*" >> "$LOG"; }

pane_uuid() { printf '%s' "${ITERM_SESSION_ID:-}" | sed 's/^[^:]*://'; }

flag_path() { printf '%s/%s.flag' "$STATE_DIR" "$1"; }

retry_path() { printf '%s/%s.retry' "$STATE_DIR" "$1"; }

# PID của process Claude Code tổ tiên gần nhất: comm "claude" (bản native) hoặc node chạy claude (npm).
# Shell của Bash tool và của hook đều KHÔNG có tty riêng ("??") nên phải leo lên tới claude.
claude_pid() {
  local pid=$$ comm args
  while [ -n "$pid" ] && [ "$pid" -gt 1 ]; do
    comm=$(ps -o comm= -p "$pid" 2>/dev/null)
    case "${comm##*/}" in
      claude) printf '%s' "$pid"; return 0 ;;
      node)
        args=$(ps -o args= -p "$pid" 2>/dev/null)
        case "$args" in *claude*) printf '%s' "$pid"; return 0 ;; esac
        ;;
    esac
    pid=$(ps -o ppid= -p "$pid" 2>/dev/null | tr -d ' ')
  done
  return 1
}

tty_of() {
  local t
  t=$(ps -o tty= -p "$1" 2>/dev/null | tr -d ' ')
  if [ -n "$t" ] && [ "$t" != "??" ]; then printf '/dev/%s' "$t"; fi
}

# Path script mà Stop hook đang trỏ tới — rỗng nếu chưa đăng ký. Đọc cả bản cài cũ (trỏ thẳng vào repo).
hook_target() {
  [ -f "$SETTINGS" ] || return 0
  sed -n "s/.*f='\([^']*auto-clear\.sh\)'.*/\1/p" "$SETTINGS" | head -n 1
}

# Đã đăng ký VÀ script đích còn tồn tại (hook trỏ vào repo đã xoá/dời = coi như chưa cài).
hook_installed() {
  local target
  target=$(hook_target)
  [ -n "$target" ] && [ -f "$target" ]
}

# StopFailure hook đã đăng ký (bản install cũ chỉ có Stop hook → phải chạy lại install).
failhook_installed() {
  hook_installed && grep -q 'auto-clear\.sh.* failhook' "$SETTINGS" 2>/dev/null
}

# Hook chạy bản copy → đồng bộ với bản của skill đang bật cờ để hook luôn khớp phiên bản vừa arm.
sync_hook_copy() {
  if [ "$(hook_target)" = "$HOOK_SCRIPT" ] && ! cmp -s "$SCRIPT_PATH" "$HOOK_SCRIPT"; then
    cp "$SCRIPT_PATH" "$HOOK_SCRIPT"
  fi
}

# Prompt --then trong cờ: base64 một dòng để xuống dòng/dấu "=" không phá format key=value.
b64_encode() { printf '%s' "$1" | base64 | tr -d '\n'; }
b64_decode() { printf '%s' "$1" | base64 --decode 2>/dev/null; }

# Số lần thử lại tối đa ghi trong cờ retry (cờ bản cũ không có → AUTO_CLEAR_RETRY_MAX). 0 = không giới hạn.
retry_max_of() {
  local max
  max=$(sed -n 's/^max=//p' "$1")
  case "$max" in ''|*[!0-9]*) max="$RETRY_MAX" ;; esac
  printf '%s' "$max"
}

max_label() { if [ "$1" -eq 0 ]; then printf '∞'; else printf '%s' "$1"; fi; }

# Prompt hợp lệ: không rỗng, một dòng, không ký tự điều khiển, ≤ THEN_MAX ký tự. Sai → in lý do, return 1.
validate_then() {
  local p="$1" n
  [ -n "$p" ] || { echo "INVALID_THEN: prompt rỗng"; return 1; }
  case "$p" in *$'\n'*|*$'\r'*) echo "INVALID_THEN: prompt phải là một dòng"; return 1 ;; esac
  if printf '%s' "$p" | LC_ALL=C grep -q '[[:cntrl:]]'; then
    echo "INVALID_THEN: prompt chứa ký tự điều khiển"; return 1
  fi
  n=$(printf '%s' "$p" | LC_ALL=en_US.UTF-8 wc -m | tr -d ' ')
  [ "$n" -le "$THEN_MAX" ] || { echo "INVALID_THEN: prompt dài $n ký tự (tối đa $THEN_MAX)"; return 1; }
}

# $1 uuid, $2 tty mong đợi ("" = bỏ qua so tty), $3 "dry" | "type", $4 chữ cần gõ ("" = "/clear")
iterm_send() {
  osascript - "$1" "$2" "$3" "${4:-/clear}" <<'OSA'
on run argv
	set targetId to item 1 of argv
	set targetTty to item 2 of argv
	set dryRun to ((item 3 of argv) is "dry")
	set payload to item 4 of argv
	if application "iTerm2" is not running then return "iterm-not-running"
	tell application "iTerm2"
		repeat with w in windows
			repeat with t in tabs of w
				repeat with s in sessions of t
					if (unique id of s) is targetId then
						set paneTty to (tty of s)
						if targetTty is not "" and paneTty is not targetTty then return "tty-mismatch pane=" & paneTty & " claude=" & targetTty
						if dryRun then return "ok-dry tty=" & paneTty
						repeat with i from 1 to (length of payload)
							tell s to write text (character i of payload) newline no
							delay 0.03
						end repeat
						delay 0.2
						tell s to write text (character id 13) newline no
						return "ok"
					end if
				end repeat
			end repeat
		end repeat
	end tell
	return "pane-not-found"
end run
OSA
}

cmd_arm() {
  local uuid pid tty report use_stdin="" then_prompt="" has_then=""
  while [ $# -gt 0 ]; do
    case "$1" in
      --stdin) use_stdin=1 ;;
      --then)
        [ $# -ge 2 ] || { echo "INVALID_THEN: --then thiếu prompt"; return 1; }
        has_then=1; then_prompt="$2"; shift ;;
      *) echo "usage: auto-clear.sh arm [--stdin] [--then \"<prompt>\"]"; return 1 ;;
    esac
    shift
  done
  # Validate trước mọi side-effect: prompt sai thì không lưu report, không bật cờ.
  if [ -n "$has_then" ]; then validate_then "$then_prompt" || return 1; fi
  uuid=$(pane_uuid)
  [ -n "$uuid" ] || { echo "UNSUPPORTED: không có ITERM_SESSION_ID (không chạy trong iTerm2) — gõ /clear tay"; return 2; }
  hook_installed || { echo "NOT_INSTALLED: Stop hook chưa đăng ký (hoặc trỏ tới script không còn tồn tại) — chạy: bash $SCRIPT_PATH install"; return 3; }
  pid=$(claude_pid) || { echo "UNSUPPORTED: không tìm thấy process claude tổ tiên"; return 2; }
  sync_hook_copy
  tty=$(tty_of "$pid")
  mkdir -p "$STATE_DIR/reports"
  if [ -n "$use_stdin" ]; then
    report="$STATE_DIR/reports/$(date +%Y%m%d-%H%M%S)-${uuid:0:8}.md"
    cat > "$report"
    if [ -s "$report" ]; then
      cp "$report" "$STATE_DIR/last-report.md"
      echo "REPORT $report"
    else
      rm -f "$report"
    fi
  fi
  printf 'pid=%s\ntty=%s\narmed_at=%s\n' "$pid" "$tty" "$(date '+%F %T')" > "$(flag_path "$uuid")"
  if [ -n "$has_then" ]; then
    printf 'then_b64=%s\n' "$(b64_encode "$then_prompt")" >> "$(flag_path "$uuid")"
    log "arm pane=$uuid pid=$pid tty=$tty then=$then_prompt"
    echo "ARMED pane=$uuid pid=$pid tty=${tty:-?} then=$then_prompt"
  else
    log "arm pane=$uuid pid=$pid tty=$tty"
    echo "ARMED pane=$uuid pid=$pid tty=${tty:-?}"
  fi
}

cmd_off() {
  local uuid
  uuid=$(pane_uuid)
  [ -n "$uuid" ] || { echo "UNSUPPORTED: không có ITERM_SESSION_ID"; return 2; }
  # `off` = dev dừng loop → gỡ luôn cờ thử lại lỗi API, không thì lỗi API sau này gõ prompt loop vào pane.
  if [ -f "$(retry_path "$uuid")" ]; then
    rm -f "$(retry_path "$uuid")"
    log "retry-off pane=$uuid (off)"
    echo "RETRY_DISARMED pane=$uuid"
  fi
  if [ -f "$(flag_path "$uuid")" ]; then
    rm -f "$(flag_path "$uuid")"
    log "off pane=$uuid"
    echo "DISARMED pane=$uuid"
  else
    echo "NOT_ARMED pane=$uuid"
  fi
}

cmd_retry_arm() {
  local uuid pid tty prompt="$RETRY_PROMPT_DEFAULT" max="$RETRY_MAX"
  while [ $# -gt 0 ]; do
    case "$1" in
      --prompt)
        [ $# -ge 2 ] || { echo "INVALID_THEN: --prompt thiếu nội dung"; return 1; }
        prompt="$2"; shift ;;
      --max)
        case "${2:-}" in ''|*[!0-9]*) echo "INVALID_MAX: --max cần số nguyên ≥ 0 (0 = không giới hạn)"; return 1 ;; esac
        max="$2"; shift ;;
      *) echo "usage: auto-clear.sh retry-arm [--prompt \"<prompt>\"] [--max N]"; return 1 ;;
    esac
    shift
  done
  validate_then "$prompt" || return 1
  uuid=$(pane_uuid)
  [ -n "$uuid" ] || { echo "UNSUPPORTED: không có ITERM_SESSION_ID (không chạy trong iTerm2) — lỗi API phải gõ tiếp tay"; return 2; }
  failhook_installed || { echo "NOT_INSTALLED: StopFailure hook chưa đăng ký — chạy: bash $SCRIPT_PATH install"; return 3; }
  pid=$(claude_pid) || { echo "UNSUPPORTED: không tìm thấy process claude tổ tiên"; return 2; }
  sync_hook_copy
  tty=$(tty_of "$pid")
  mkdir -p "$STATE_DIR"
  printf 'pid=%s\ntty=%s\ncount=0\nmax=%s\nprompt_b64=%s\narmed_at=%s\n' \
    "$pid" "$tty" "$max" "$(b64_encode "$prompt")" "$(date '+%F %T')" > "$(retry_path "$uuid")"
  log "retry-arm pane=$uuid pid=$pid tty=$tty max=$(max_label "$max") prompt=$prompt"
  echo "RETRY_ARMED pane=$uuid pid=$pid tty=${tty:-?} delay=${RETRY_DELAY}s max=$(max_label "$max")"
}

cmd_retry_off() {
  local uuid
  uuid=$(pane_uuid)
  [ -n "$uuid" ] || { echo "UNSUPPORTED: không có ITERM_SESSION_ID"; return 2; }
  if [ -f "$(retry_path "$uuid")" ]; then
    rm -f "$(retry_path "$uuid")"
    log "retry-off pane=$uuid"
    echo "RETRY_DISARMED pane=$uuid"
  else
    echo "RETRY_NOT_ARMED pane=$uuid"
  fi
}

# Đặt count trong cờ retry (giữ nguyên các dòng khác).
retry_set_count() {
  local file="$1" count="$2" tmp
  tmp="$file.tmp.$$"
  { grep -v '^count=' "$file"; printf 'count=%s\n' "$count"; } > "$tmp" && mv "$tmp" "$file"
}

# StopFailure hook: lượt trả lời chết vì lỗi API. Có cờ retry của pane + đúng process claude đã arm →
# chờ RETRY_DELAY giây rồi gõ prompt; quá `max` của cờ (0 = không giới hạn) lần liên tiếp thì thôi (cờ giữ
# nguyên, log give-up).
cmd_failhook() {
  local input uuid file armed_pid armed_tty cur_pid count max prompt error result mode
  input=""
  [ -t 0 ] || input=$(cat 2>/dev/null)
  error=$(printf '%s' "$input" | sed -n 's/.*"error"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' | head -n 1)
  uuid=$(pane_uuid)
  [ -n "$uuid" ] || return 0
  file=$(retry_path "$uuid")
  [ -f "$file" ] || return 0
  armed_pid=$(sed -n 's/^pid=//p' "$file")
  armed_tty=$(sed -n 's/^tty=//p' "$file")
  count=$(sed -n 's/^count=//p' "$file")
  prompt=$(b64_decode "$(sed -n 's/^prompt_b64=//p' "$file")")
  cur_pid=$(claude_pid || true)
  if [ -n "$armed_pid" ] && [ "$cur_pid" != "$armed_pid" ]; then
    log "failhook skip pane=$uuid: claude pid $cur_pid != armed $armed_pid"
    return 0
  fi
  max=$(retry_max_of "$file")
  count=$(( ${count:-0} + 1 ))
  if [ "$max" -gt 0 ] && [ "$count" -gt "$max" ]; then
    log "failhook give-up pane=$uuid error=${error:-?}: đã thử lại $max lần"
    return 0
  fi
  retry_set_count "$file" "$count"
  if ! validate_then "$prompt" >/dev/null; then
    log "failhook skip pane=$uuid: prompt trong cờ không hợp lệ"
    return 0
  fi
  log "failhook pane=$uuid error=${error:-?} lần $count/$(max_label "$max") — chờ ${RETRY_DELAY}s"
  mode="${AUTO_CLEAR_DRY:+dry}"
  sleep "$RETRY_DELAY"
  # Dev đã gỡ cờ (retry-off / off) trong lúc chờ → không gõ.
  [ -f "$file" ] || { log "failhook skip pane=$uuid: cờ retry đã bị gỡ trong lúc chờ"; return 0; }
  result=$(iterm_send "$uuid" "$armed_tty" "$mode" "$prompt" 2>&1)
  log "failhook pane=$uuid tty=$armed_tty lần $count -> $result"
  return 0
}

cmd_status() {
  local uuid then_b64
  uuid=$(pane_uuid)
  [ -n "$uuid" ] || { echo "UNSUPPORTED: không có ITERM_SESSION_ID"; return 2; }
  if hook_installed; then
    echo "HOOK installed ($SETTINGS -> $(hook_target))"
  elif [ -n "$(hook_target)" ]; then
    echo "HOOK broken: script $(hook_target) không còn tồn tại — chạy lại install"
  else
    echo "HOOK not-installed"
  fi
  if failhook_installed; then echo "FAILHOOK installed"; else echo "FAILHOOK not-installed"; fi
  if [ -f "$(retry_path "$uuid")" ]; then
    echo "RETRY_ARMED pane=$uuid $(sed -n 's/^count=/count=/p' "$(retry_path "$uuid")")/$(max_label "$(retry_max_of "$(retry_path "$uuid")")")"
  else
    echo "RETRY_NOT_ARMED pane=$uuid"
  fi
  if [ -f "$(flag_path "$uuid")" ]; then
    echo "ARMED pane=$uuid"
    cat "$(flag_path "$uuid")"
    then_b64=$(sed -n 's/^then_b64=//p' "$(flag_path "$uuid")")
    [ -z "$then_b64" ] || echo "THEN $(b64_decode "$then_b64")"
  else
    echo "NOT_ARMED pane=$uuid"
  fi
}

cmd_probe() {
  local uuid pid tty
  uuid=$(pane_uuid)
  [ -n "$uuid" ] || { echo "UNSUPPORTED: không có ITERM_SESSION_ID"; return 2; }
  pid=$(claude_pid) || { echo "UNSUPPORTED: không tìm thấy process claude tổ tiên"; return 2; }
  tty=$(tty_of "$pid")
  echo "pane=$uuid claude_pid=$pid claude_tty=${tty:-?}"
  iterm_send "$uuid" "$tty" dry
}

cmd_hook() {
  local uuid flag armed_pid armed_tty cur_pid result then_b64 then_prompt mode
  # Stop hook nhận JSON qua stdin — đọc cho hết để phía Claude Code không dính EPIPE.
  [ -t 0 ] || cat > /dev/null 2>&1
  uuid=$(pane_uuid)
  [ -n "$uuid" ] || return 0
  # Lượt kết thúc bình thường (lỗi API không bắn Stop, chỉ bắn StopFailure) → chuỗi lỗi API đã dứt.
  if [ -f "$(retry_path "$uuid")" ] && ! grep -qx 'count=0' "$(retry_path "$uuid")"; then
    retry_set_count "$(retry_path "$uuid")" 0
    log "retry-reset pane=$uuid"
  fi
  flag=$(flag_path "$uuid")
  [ -f "$flag" ] || return 0
  armed_pid=$(sed -n 's/^pid=//p' "$flag")
  armed_tty=$(sed -n 's/^tty=//p' "$flag")
  then_b64=$(sed -n 's/^then_b64=//p' "$flag")
  rm -f "$flag"
  cur_pid=$(claude_pid || true)
  if [ -n "$armed_pid" ] && [ "$cur_pid" != "$armed_pid" ]; then
    log "hook skip pane=$uuid: claude pid $cur_pid != armed $armed_pid"
    return 0
  fi
  mode="${AUTO_CLEAR_DRY:+dry}"
  sleep "$DELAY"
  result=$(iterm_send "$uuid" "$armed_tty" "$mode" 2>&1)
  log "hook pane=$uuid tty=$armed_tty -> $result"
  [ -n "$then_b64" ] || return 0
  then_prompt=$(b64_decode "$then_b64")
  # Cờ có thể bị sửa tay giữa arm và hook → validate lại trước khi gõ.
  if ! validate_then "$then_prompt" >/dev/null; then
    log "hook-then pane=$uuid skip: prompt trong cờ không hợp lệ"
    return 0
  fi
  case "$result" in
    ok*) ;;
    *) log "hook-then pane=$uuid skip: /clear không gõ được ($result)"; return 0 ;;
  esac
  # Chờ /clear xong để prompt rơi vào session sạch; iterm_send so tty lại nên vẫn chỉ gõ vào pane đã arm.
  sleep "$THEN_DELAY"
  result=$(iterm_send "$uuid" "$armed_tty" "$mode" "$then_prompt" 2>&1)
  log "hook-then pane=$uuid tty=$armed_tty prompt=$then_prompt -> $result"
  return 0
}

cmd_inject() {
  [ -n "${1:-}" ] || { echo "usage: auto-clear.sh inject <uuid> [tty]"; return 1; }
  iterm_send "$1" "${2:-}" type
}

cmd_install() {
  mkdir -p "$STATE_DIR"
  [ "$SCRIPT_PATH" = "$HOOK_SCRIPT" ] || cp "$SCRIPT_PATH" "$HOOK_SCRIPT"
  python3 - "$SETTINGS" "$HOOK_SCRIPT" <<'PY'
import json, os, shutil, sys
path, script = sys.argv[1], sys.argv[2]
data = {}
if os.path.exists(path):
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    shutil.copy2(path, path + ".bak-auto-clear")
hooks = data.setdefault("hooks", {})
# StopFailure: hook chờ AUTO_CLEAR_RETRY_DELAY (30s) rồi mới gõ → timeout phải dư.
for event, sub, timeout in (("Stop", "hook", 30), ("StopFailure", "failhook", 120)):
    command = f"f='{script}'; [ -f \"$f\" ] && bash \"$f\" {sub} || true"
    groups = hooks.setdefault(event, [])
    # Idempotent: bỏ entry auto-clear cũ (nếu có), giữ nguyên mọi hook khác.
    for group in groups:
        group["hooks"] = [h for h in group.get("hooks", []) if "auto-clear" not in h.get("command", "")]
    groups[:] = [g for g in groups if g.get("hooks")]
    groups.append({"hooks": [{"type": "command", "command": command, "async": True, "timeout": timeout}]})
tmp = path + ".tmp"
with open(tmp, "w", encoding="utf-8") as f:
    json.dump(data, f, indent=2, ensure_ascii=False)
    f.write("\n")
os.replace(tmp, path)
print(f"INSTALLED Stop + StopFailure hook -> {path} (backup: {path}.bak-auto-clear)")
PY
}

cmd_uninstall() {
  rm -f "$HOOK_SCRIPT"
  [ -f "$SETTINGS" ] || { echo "NOT_INSTALLED"; return 0; }
  python3 - "$SETTINGS" <<'PY'
import json, os, sys
path = sys.argv[1]
with open(path, encoding="utf-8") as f:
    data = json.load(f)
for event in ("Stop", "StopFailure"):
    groups = data.get("hooks", {}).get(event, [])
    for group in groups:
        group["hooks"] = [h for h in group.get("hooks", []) if "auto-clear" not in h.get("command", "")]
    groups[:] = [g for g in groups if g.get("hooks")]
tmp = path + ".tmp"
with open(tmp, "w", encoding="utf-8") as f:
    json.dump(data, f, indent=2, ensure_ascii=False)
    f.write("\n")
os.replace(tmp, path)
print(f"UNINSTALLED Stop + StopFailure hook <- {path}")
PY
}

case "${1:-status}" in
  arm)       shift; cmd_arm "$@" ;;
  off)       cmd_off ;;
  status)    cmd_status ;;
  probe)     cmd_probe ;;
  hook)      cmd_hook ;;
  retry-arm) shift; cmd_retry_arm "$@" ;;
  retry-off) cmd_retry_off ;;
  failhook)  cmd_failhook ;;
  inject)    shift; cmd_inject "$@" ;;
  install)   cmd_install ;;
  uninstall) cmd_uninstall ;;
  *) echo "usage: auto-clear.sh {arm [--stdin] [--then \"<prompt>\"]|off|status|probe|hook|retry-arm [--prompt \"<prompt>\"] [--max N]|retry-off|failhook|inject <uuid> [tty]|install|uninstall}"; exit 1 ;;
esac
