#!/usr/bin/env bash
# auto-clear.sh — types "/clear" into the EXACT iTerm2 pane running Claude Code right after the turn
# ends. The /auto-clear skill arms the flag; the Stop hook (registered by `install`) reads the flag and types.
# Windows twin: auto-clear.ps1 (EXPERIMENTAL) — keep the same subcommands and the same ~/.claude/auto-clear/ layout.
#
#   arm [--stdin] [--then "<prompt>"]
#                   arm the current pane; --stdin: save the final report (read from stdin) before clearing;
#                   --then: after "/clear" wait AUTO_CLEAR_THEN_DELAY seconds, then type <prompt> + Enter
#                   (one line, no control characters, <= 200 characters — otherwise the flag is NOT armed)
#   off             disarm the current pane
#   status          whether the current pane is armed (plus the pending --then prompt, if any)
#   probe           find the pane + compare ttys, WITHOUT typing anything — checks the setup / Automation permission
#   type-test       type "auto-clear type-test ok" + Enter into the current claude's pane — run it from a Claude
#                   Code Bash call; the text must show up as a queued user message (checks real typing)
#   hook            Stop hook: armed → disarm → wait AUTO_CLEAR_DELAY seconds → type "/clear" + Enter
#                   (+ the --then prompt when the flag carries one, after AUTO_CLEAR_THEN_DELAY seconds)
#   inject <uuid> [tty]   type "/clear" + Enter into pane <uuid> (internal / testing)
#   retry-arm [--prompt "<prompt>"] [--max N]
#                   arm API-ERROR RETRY for the current pane: when a turn dies on a Claude API error (org access
#                   lost, rate limit, overloaded, 5xx…) the StopFailure hook waits AUTO_CLEAR_RETRY_DELAY
#                   seconds, then types <prompt> + Enter, at most N times in a row (default AUTO_CLEAR_RETRY_MAX;
#                   0 = unlimited); a turn that ends normally (Stop hook) resets the counter to 0. The flag
#                   lives until retry-off / off
#   retry-off       disarm the current pane's API-error retry
#   failhook        StopFailure hook (internal) — see retry-arm
#   install         copy the script to ~/.claude/auto-clear/auto-clear.sh and register the Stop + StopFailure
#                   hooks (async) on that copy in ~/.claude/settings.json — idempotent, with backup
#   uninstall       remove both hooks from ~/.claude/settings.json + delete the copy
#
# The hook belongs to the MACHINE (user-level settings), not to a repo: one hook serves every project that
# installs this skill. It therefore runs the copy under ~/.claude/auto-clear/ instead of pointing into a repo, so
# deleting/moving a repo cannot silently kill it. `arm` re-syncs the copy on every run, so a skill update reaches
# the hook right away.
#
# auto-clear-version: 1
#   Bump this number (same number in auto-clear.ps1) whenever the hook side changes. `arm` / `retry-arm` /
#   `install` NEVER overwrite a copy carrying a HIGHER number: with several projects on one machine, a project
#   still on an old skill would downgrade the shared hook on every arm (an old copy whose failhook does nothing =
#   API-error retry silently dead for every project). Scripts without this line count as version 0.
#
# Targets the pane by the UUID in ITERM_SESSION_ID ("w0t0p3:<UUID>") through AppleScript `write text` — writes
# straight into that pane's input, does NOT send keys to the focused window. Before typing it also compares the
# pane's tty with the tty of the `claude` process that armed the flag: mismatch (tmux/ssh, Claude exited) → skip,
# never type into the wrong pane.
set -u

STATE_DIR="${HOME}/.claude/auto-clear"
SETTINGS="${HOME}/.claude/settings.json"
LOG="${STATE_DIR}/auto-clear.log"
DELAY="${AUTO_CLEAR_DELAY:-1}"
THEN_DELAY="${AUTO_CLEAR_THEN_DELAY:-3}"
THEN_MAX=200
TYPE_TEST_TEXT="auto-clear type-test ok"
RETRY_DELAY="${AUTO_CLEAR_RETRY_DELAY:-30}"
RETRY_MAX="${AUTO_CLEAR_RETRY_MAX:-5}"
RETRY_PROMPT_DEFAULT="Tiếp tục: lượt trước dừng vì lỗi API — làm tiếp đúng bước đang dở."
SCRIPT_PATH="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/$(basename "${BASH_SOURCE[0]}")"
HOOK_SCRIPT="${STATE_DIR}/auto-clear.sh"

log() { mkdir -p "$STATE_DIR"; printf '%s %s\n' "$(date '+%F %T')" "$*" >> "$LOG"; }

pane_uuid() { printf '%s' "${ITERM_SESSION_ID:-}" | sed 's/^[^:]*://'; }

flag_path() { printf '%s/%s.flag' "$STATE_DIR" "$1"; }

retry_path() { printf '%s/%s.retry' "$STATE_DIR" "$1"; }

# PID of the nearest ancestor Claude Code process: comm "claude" (native build) or node running claude (npm).
# The Bash tool's shell and the hook's shell both have NO tty of their own ("??"), so walk up to claude.
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

# Script path the Stop hook points to — empty if not registered. Also reads old installs (pointing straight into a repo).
hook_target() {
  [ -f "$SETTINGS" ] || return 0
  sed -n "s/.*f='\([^']*auto-clear\.sh\)'.*/\1/p" "$SETTINGS" | head -n 1
}

# Registered AND the target script still exists (a hook pointing into a deleted/moved repo = not installed).
hook_installed() {
  local target
  target=$(hook_target)
  [ -n "$target" ] && [ -f "$target" ]
}

# StopFailure hook registered (old installs only have the Stop hook → install must be run again).
failhook_installed() {
  hook_installed && grep -q 'auto-clear\.sh.* failhook' "$SETTINGS" 2>/dev/null
}

# The "auto-clear-version: N" number of a script file (0 = no such line = older than this mechanism itself).
script_version() {
  local v
  v="$(sed -n 's/^# auto-clear-version: \([0-9][0-9]*\)[[:space:]]*$/\1/p' "$1" 2>/dev/null | head -n 1)"
  printf '%s' "${v:-0}"
}

# Copy this script over the hook's copy, unless the copy is NEWER (see "auto-clear-version" at the top of the file).
# Exit 0 = the copy is now the same or newer than this script's version; 1 = the newer copy was kept.
copy_hook_script() {
  [ "$SCRIPT_PATH" = "$HOOK_SCRIPT" ] && return 0
  local mine theirs
  mine="$(script_version "$SCRIPT_PATH")"
  theirs="$(script_version "$HOOK_SCRIPT")"
  if [ -f "$HOOK_SCRIPT" ] && [ "$theirs" -gt "$mine" ]; then
    log "keep hook copy v$theirs (newer than v$mine of $SCRIPT_PATH)"
    return 1
  fi
  cmp -s "$SCRIPT_PATH" "$HOOK_SCRIPT" || cp "$SCRIPT_PATH" "$HOOK_SCRIPT"
}

# The hook runs the copy → sync it with the arming skill's version so the hook always matches what was just armed.
sync_hook_copy() {
  if [ "$(hook_target)" = "$HOOK_SCRIPT" ]; then
    copy_hook_script || true
  fi
}

# The --then prompt in the flag: single-line base64 so newlines / "=" cannot break the key=value format.
b64_encode() { printf '%s' "$1" | base64 | tr -d '\n'; }
b64_decode() { printf '%s' "$1" | base64 --decode 2>/dev/null; }

# Max retry count stored in the retry flag (old flags lack it → AUTO_CLEAR_RETRY_MAX). 0 = unlimited.
retry_max_of() {
  local max
  max=$(sed -n 's/^max=//p' "$1")
  case "$max" in ''|*[!0-9]*) max="$RETRY_MAX" ;; esac
  printf '%s' "$max"
}

max_label() { if [ "$1" -eq 0 ]; then printf '∞'; else printf '%s' "$1"; fi; }

# Valid prompt: not empty, one line, no control characters, <= THEN_MAX characters. Invalid → print the reason, return 1.
validate_then() {
  local p="$1" n
  [ -n "$p" ] || { echo "INVALID_THEN: empty prompt"; return 1; }
  case "$p" in *$'\n'*|*$'\r'*) echo "INVALID_THEN: prompt must be a single line"; return 1 ;; esac
  if printf '%s' "$p" | LC_ALL=C grep -q '[[:cntrl:]]'; then
    echo "INVALID_THEN: prompt contains control characters"; return 1
  fi
  n=$(printf '%s' "$p" | LC_ALL=en_US.UTF-8 wc -m | tr -d ' ')
  [ "$n" -le "$THEN_MAX" ] || { echo "INVALID_THEN: prompt is $n characters long (max $THEN_MAX)"; return 1; }
}

# $1 uuid, $2 expected tty ("" = skip the tty check), $3 "dry" | "type", $4 text to type ("" = "/clear")
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
        [ $# -ge 2 ] || { echo "INVALID_THEN: --then is missing its prompt"; return 1; }
        has_then=1; then_prompt="$2"; shift ;;
      *) echo "usage: auto-clear.sh arm [--stdin] [--then \"<prompt>\"]"; return 1 ;;
    esac
    shift
  done
  # Validate before any side effect: an invalid prompt saves no report and arms no flag.
  if [ -n "$has_then" ]; then validate_then "$then_prompt" || return 1; fi
  uuid=$(pane_uuid)
  [ -n "$uuid" ] || { echo "UNSUPPORTED: no ITERM_SESSION_ID (not running in iTerm2) — type /clear manually"; return 2; }
  hook_installed || { echo "NOT_INSTALLED: Stop hook not registered (or it points to a script that no longer exists) — run: bash $SCRIPT_PATH install"; return 3; }
  pid=$(claude_pid) || { echo "UNSUPPORTED: no ancestor claude process found"; return 2; }
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
  [ -n "$uuid" ] || { echo "UNSUPPORTED: no ITERM_SESSION_ID"; return 2; }
  # `off` = the dev stops the loop → also disarm the API-error retry, otherwise a later API error types the loop prompt into the pane.
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
        [ $# -ge 2 ] || { echo "INVALID_THEN: --prompt is missing its text"; return 1; }
        prompt="$2"; shift ;;
      --max)
        case "${2:-}" in ''|*[!0-9]*) echo "INVALID_MAX: --max needs an integer >= 0 (0 = unlimited)"; return 1 ;; esac
        max="$2"; shift ;;
      *) echo "usage: auto-clear.sh retry-arm [--prompt \"<prompt>\"] [--max N]"; return 1 ;;
    esac
    shift
  done
  validate_then "$prompt" || return 1
  uuid=$(pane_uuid)
  [ -n "$uuid" ] || { echo "UNSUPPORTED: no ITERM_SESSION_ID (not running in iTerm2) — after an API error, type to continue manually"; return 2; }
  failhook_installed || { echo "NOT_INSTALLED: StopFailure hook not registered — run: bash $SCRIPT_PATH install"; return 3; }
  pid=$(claude_pid) || { echo "UNSUPPORTED: no ancestor claude process found"; return 2; }
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
  [ -n "$uuid" ] || { echo "UNSUPPORTED: no ITERM_SESSION_ID"; return 2; }
  if [ -f "$(retry_path "$uuid")" ]; then
    rm -f "$(retry_path "$uuid")"
    log "retry-off pane=$uuid"
    echo "RETRY_DISARMED pane=$uuid"
  else
    echo "RETRY_NOT_ARMED pane=$uuid"
  fi
}

# Set count in the retry flag (other lines kept as is).
retry_set_count() {
  local file="$1" count="$2" tmp
  tmp="$file.tmp.$$"
  { grep -v '^count=' "$file"; printf 'count=%s\n' "$count"; } > "$tmp" && mv "$tmp" "$file"
}

# StopFailure hook: the turn died on an API error. The pane has a retry flag + it is the same claude process that
# armed it → wait RETRY_DELAY seconds, then type the prompt; more than the flag's `max` (0 = unlimited) times in a
# row → give up (the flag stays, give-up is logged).
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
    log "failhook give-up pane=$uuid error=${error:-?}: retried $max times"
    return 0
  fi
  retry_set_count "$file" "$count"
  if ! validate_then "$prompt" >/dev/null; then
    log "failhook skip pane=$uuid: invalid prompt in flag"
    return 0
  fi
  log "failhook pane=$uuid error=${error:-?} try $count/$(max_label "$max") — wait ${RETRY_DELAY}s"
  mode="${AUTO_CLEAR_DRY:+dry}"
  sleep "$RETRY_DELAY"
  # The dev disarmed (retry-off / off) while we waited → do not type.
  [ -f "$file" ] || { log "failhook skip pane=$uuid: retry flag removed while waiting"; return 0; }
  result=$(iterm_send "$uuid" "$armed_tty" "$mode" "$prompt" 2>&1)
  log "failhook pane=$uuid tty=$armed_tty try $count -> $result"
  return 0
}

cmd_status() {
  local uuid then_b64
  uuid=$(pane_uuid)
  [ -n "$uuid" ] || { echo "UNSUPPORTED: no ITERM_SESSION_ID"; return 2; }
  if hook_installed; then
    echo "HOOK installed ($SETTINGS -> $(hook_target))"
  elif [ -n "$(hook_target)" ]; then
    echo "HOOK broken: script $(hook_target) no longer exists — run install again"
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
  [ -n "$uuid" ] || { echo "UNSUPPORTED: no ITERM_SESSION_ID"; return 2; }
  pid=$(claude_pid) || { echo "UNSUPPORTED: no ancestor claude process found"; return 2; }
  tty=$(tty_of "$pid")
  echo "pane=$uuid claude_pid=$pid claude_tty=${tty:-?}"
  iterm_send "$uuid" "$tty" dry
}

cmd_type_test() {
  local uuid pid tty result
  uuid=$(pane_uuid)
  [ -n "$uuid" ] || { echo "UNSUPPORTED: no ITERM_SESSION_ID"; return 2; }
  pid=$(claude_pid) || { echo "UNSUPPORTED: no ancestor claude process found"; return 2; }
  tty=$(tty_of "$pid")
  result=$(iterm_send "$uuid" "$tty" type "$TYPE_TEST_TEXT" 2>&1)
  log "type-test pane=$uuid tty=$tty -> $result"
  echo "type-test pane=$uuid tty=${tty:-?} -> $result"
}

cmd_hook() {
  local uuid flag armed_pid armed_tty cur_pid result then_b64 then_prompt mode
  # The Stop hook receives JSON on stdin — drain it so Claude Code's side does not hit EPIPE.
  [ -t 0 ] || cat > /dev/null 2>&1
  uuid=$(pane_uuid)
  [ -n "$uuid" ] || return 0
  # The turn ended normally (an API error fires only StopFailure, never Stop) → the API-error streak is over.
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
  # The flag may have been hand-edited between arm and hook → re-validate before typing.
  if ! validate_then "$then_prompt" >/dev/null; then
    log "hook-then pane=$uuid skip: invalid prompt in flag"
    return 0
  fi
  case "$result" in
    ok*) ;;
    *) log "hook-then pane=$uuid skip: /clear was not typed ($result)"; return 0 ;;
  esac
  # Wait for /clear to finish so the prompt lands in a clean session; iterm_send re-checks the tty, so it still only types into the armed pane.
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
  copy_hook_script || echo "KEPT_NEWER: $HOOK_SCRIPT is v$(script_version "$HOOK_SCRIPT"), this script is v$(script_version "$SCRIPT_PATH") - hooks registered on the newer copy"
  python3 - "$SETTINGS" "$HOOK_SCRIPT" <<'PY'
import json, os, shutil, sys
path, script = sys.argv[1], sys.argv[2]
data = {}
if os.path.exists(path):
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    shutil.copy2(path, path + ".bak-auto-clear")
hooks = data.setdefault("hooks", {})
# StopFailure: the hook waits AUTO_CLEAR_RETRY_DELAY (30s) before typing → the timeout needs headroom.
for event, sub, timeout in (("Stop", "hook", 30), ("StopFailure", "failhook", 120)):
    command = f"f='{script}'; [ -f \"$f\" ] && bash \"$f\" {sub} || true"
    groups = hooks.setdefault(event, [])
    # Idempotent: drop old auto-clear entries (if any), keep every other hook as is.
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
  type-test) cmd_type_test ;;
  hook)      cmd_hook ;;
  retry-arm) shift; cmd_retry_arm "$@" ;;
  retry-off) cmd_retry_off ;;
  failhook)  cmd_failhook ;;
  inject)    shift; cmd_inject "$@" ;;
  install)   cmd_install ;;
  uninstall) cmd_uninstall ;;
  *) echo "usage: auto-clear.sh {arm [--stdin] [--then \"<prompt>\"]|off|status|probe|type-test|hook|retry-arm [--prompt \"<prompt>\"] [--max N]|retry-off|failhook|inject <uuid> [tty]|install|uninstall}"; exit 1 ;;
esac
