#!/bin/bash
# Core script to post a Discord message with a Bot Token — to developers' DMs, to
# server channels, or both. Each destination list is optional: an empty list (or
# one holding only the .env placeholder) sends nothing to that kind, and with no
# list configured at all the script is a no-op. Always exits 0, so the loop never
# fails just because notifications are off or Discord is unreachable.
#
# PowerShell twin: discord-send.ps1 — keep the .env keys, the ID rules and the
# messages in lockstep.
#
# Configure by adding to <repo>/.env (gitignored):
#   DISCORD_BOT_TOKEN=...
#   DISCORD_DEVELOPERS=userid1,userid2      # DM each user (optional)
#   DISCORD_CHANNELS=channelid1,channelid2  # post in each channel (optional)
# The bot must share a server with every DM user, and must be a member of every
# channel's server with View Channel + Send Messages + Embed Links there.
#
# Usage: discord-send.sh "<plain text>" ['<single embed JSON object>']
#   Either argument may be empty; with both empty nothing is sent.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

DISCORD_API="https://discord.com/api/v10"
# Discord's documented bot User-Agent format ("DiscordBot (<url>, <version>)").
DISCORD_UA="DiscordBot (https://github.com/PackageStore/ezg-packages, 1.0)"
ENV_KEYS=" DISCORD_BOT_TOKEN DISCORD_DEVELOPERS DISCORD_CHANNELS "

# Load the DISCORD_* keys from .env (a key set there wins over the shell's). Parsed
# line by line rather than `export $(... | xargs)`: that broke on quotes, on a CRLF
# file (the token gained a trailing \r) and exported every other key in the file.
load_env() {
  local line key val first=1
  [ -f "$1" ] || return 0
  while IFS= read -r line || [ -n "$line" ]; do
    # A UTF-8 BOM (Windows Notepad) would otherwise glue onto the first key.
    [ "$first" -eq 1 ] && line="${line#$'\xef\xbb\xbf'}"
    first=0
    line="${line%$'\r'}"
    line="${line#"${line%%[![:space:]]*}"}"
    case "$line" in ""|"#"*) continue ;; esac
    case "$line" in "export "*) line="${line#export }" ;; esac
    case "$line" in *=*) ;; *) continue ;; esac
    key="${line%%=*}"
    key="${key%"${key##*[![:space:]]}"}"
    case "$ENV_KEYS" in *" $key "*) ;; *) continue ;; esac
    val="${line#*=}"
    val="${val#"${val%%[![:space:]]*}"}"
    val="${val%"${val##*[![:space:]]}"}"
    # Quoted: the value ends at the closing quote (a " # comment" may follow it).
    # Unquoted: drop an inline " # comment".
    case "$val" in
      \"*) val="${val#\"}"; val="${val%%\"*}" ;;
      \'*) val="${val#\'}"; val="${val%%\'*}" ;;
      *) val="${val%%[[:space:]]#*}"
         val="${val%"${val##*[![:space:]]}"}" ;;
    esac
    export "$key=$val"
  done < "$1"
}
load_env "$PROJECT_ROOT/.env"

# Echo the valid Discord IDs (snowflakes: digits only) of a comma/space separated
# list, one per line, deduplicated. The .env placeholders ("DEVELOPER_USER_ID_1")
# and typos are reported and skipped, so a half-filled .env never sends anywhere.
parse_ids() {  # parse_ids <list> <label>
  local id seen=" "
  local -a ids=()
  # read -a, not an unquoted $list: a stray "*" must not glob into file names.
  IFS=$', ;\t' read -r -a ids <<< "$(printf '%s' "$1" | tr '\r\n' '  ')"
  for id in "${ids[@]}"; do
    [ -z "$id" ] && continue
    case "$id" in
      *[!0-9]*) echo "[Discord Send] Ignoring invalid $2 ID '$id' (expected digits only)." >&2; continue ;;
    esac
    case "$seen" in *" $id "*) continue ;; esac
    seen="$seen$id "
    printf '%s\n' "$id"
  done
}

TOKEN="${DISCORD_BOT_TOKEN:-}"
if [ -z "$TOKEN" ] || [ "$TOKEN" = "YOUR_DISCORD_BOT_TOKEN_HERE" ]; then
  echo "[Discord Send] DISCORD_BOT_TOKEN not configured in .env. Skipping notification."
  exit 0
fi

DM_IDS="$(parse_ids "${DISCORD_DEVELOPERS:-}" "developer")"
CHANNEL_IDS="$(parse_ids "${DISCORD_CHANNELS:-}" "channel")"
if [ -z "$DM_IDS" ] && [ -z "$CHANNEL_IDS" ]; then
  echo "[Discord Send] Neither DISCORD_DEVELOPERS nor DISCORD_CHANNELS holds a valid ID in .env. Skipping notification."
  exit 0
fi

MESSAGE="${1:-}"
EMBED_JSON="${2:-}"
if [ -z "$MESSAGE" ] && [ -z "$EMBED_JSON" ]; then
  echo "[Discord Send] Nothing to send (empty message and embed)."
  exit 0
fi

json_escape() {
  printf '%s' "$1" | tr -d '\r' | tr '\t' ' ' | sed 's/\\/\\\\/g; s/"/\\"/g' \
    | awk '{printf "%s\\n", $0}' | sed 's/\\n$//'
}

# allowed_mentions parse=[]: text quoted from a log or a task title ("@everyone")
# must never ping a channel.
BODY='{"allowed_mentions": {"parse": []}'
[ -n "$MESSAGE" ] && BODY="$BODY, \"content\": \"$(json_escape "$MESSAGE")\""
[ -n "$EMBED_JSON" ] && BODY="$BODY, \"embeds\": [$EMBED_JSON]"
BODY="$BODY}"

# POST stdin to $1, printing "<response body>\n<http status>". The body goes on
# stdin, not argv: Git Bash hands a native curl.exe its arguments in the ANSI code
# page, which mangles emoji / "·" into invalid JSON (Discord error 50109).
discord_post() {
  curl -s --connect-timeout 10 --max-time 30 -X POST \
    -A "$DISCORD_UA" \
    -H "Authorization: Bot $TOKEN" \
    -H "Content-Type: application/json" \
    --data-binary @- \
    -w '\n%{http_code}' \
    "$DISCORD_API$1"
}

# The id of the channel object in a create-DM response: jq, then python3, then the
# FIRST "id" in the body (a greedy match would return the last one, the recipient's
# user id). Each answer must be all digits or the next method runs: on Windows
# `python3` is often the Store stub (prints nothing), and native jq.exe / python.exe
# end their output with \r.
dm_channel_id() {
  local id=""
  if command -v jq >/dev/null 2>&1; then
    id="$(printf '%s' "$1" | jq -r '.id // empty' 2>/dev/null | tr -d '\r')"
    case "$id" in *[!0-9]*) id="" ;; esac
  fi
  if [ -z "$id" ] && command -v python3 >/dev/null 2>&1; then
    id="$(printf '%s' "$1" | python3 -c 'import json,sys
try: print(json.load(sys.stdin).get("id") or "")
except Exception: print("")' 2>/dev/null | tr -d '\r')"
    case "$id" in *[!0-9]*) id="" ;; esac
  fi
  if [ -z "$id" ]; then
    id="$(printf '%s' "$1" | tr -d '\r\n' | grep -o '"id": *"[0-9]*"' | head -n 1 | grep -o '[0-9][0-9]*')"
  fi
  printf '%s' "$id"
}

send_to_channel() {  # send_to_channel <channel id> <label for the log>
  local resp code
  resp="$(printf '%s' "$BODY" | discord_post "/channels/$1/messages")"
  code="${resp##*$'\n'}"
  if [ "$code" = "200" ]; then
    echo "[Discord Send] Notification sent to $2."
  else
    echo "[Discord Send] Failed to send message to $2 (HTTP ${code:-none}). Response: ${resp%$'\n'*}"
  fi
}

for USER_ID in $DM_IDS; do
  echo "[Discord Send] Creating DM channel for user $USER_ID..."
  RESP="$(printf '{"recipient_id": "%s"}' "$USER_ID" | discord_post "/users/@me/channels")"
  CODE="${RESP##*$'\n'}"
  RESP="${RESP%$'\n'*}"
  CHANNEL_ID=""
  [ "$CODE" = "200" ] && CHANNEL_ID="$(dm_channel_id "$RESP")"
  if [ -z "$CHANNEL_ID" ]; then
    echo "[Discord Send] Failed to create DM channel for user $USER_ID (HTTP ${CODE:-none}). Response: $RESP"
    continue
  fi
  send_to_channel "$CHANNEL_ID" "developer $USER_ID (DM)"
done

for CHANNEL_ID in $CHANNEL_IDS; do
  send_to_channel "$CHANNEL_ID" "channel $CHANNEL_ID"
done

exit 0
