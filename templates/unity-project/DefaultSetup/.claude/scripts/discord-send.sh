#!/bin/bash
# Core script to send Discord Direct Messages (DMs) to developers using a Bot Token.
# Gracefully degrades (exit 0, no-op) if not configured — the loop never fails
# just because notifications are off.
#
# Configure by adding to <repo>/.env (gitignored):
#   DISCORD_BOT_TOKEN=...
#   DISCORD_DEVELOPERS=userid1,userid2

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# Load environment variables if .env exists.
if [ -f "$PROJECT_ROOT/.env" ]; then
  export $(grep -v '^#' "$PROJECT_ROOT/.env" | xargs)
fi

if [ -z "${DISCORD_BOT_TOKEN:-}" ] || [ "${DISCORD_BOT_TOKEN:-}" = "YOUR_DISCORD_BOT_TOKEN_HERE" ]; then
  echo "[Discord Send] DISCORD_BOT_TOKEN not configured in .env. Skipping notification."
  exit 0
fi

if [ -z "${DISCORD_DEVELOPERS:-}" ] || [ "${DISCORD_DEVELOPERS:-}" = "DEVELOPER_USER_ID_1,DEVELOPER_USER_ID_2" ]; then
  echo "[Discord Send] DISCORD_DEVELOPERS not configured in .env. Skipping notification."
  exit 0
fi

MESSAGE="$1"
EMBED_JSON="$2"

# Parse comma-separated user IDs.
IFS=',' read -ra ADDR <<< "$DISCORD_DEVELOPERS"
for USER_ID in "${ADDR[@]}"; do
  USER_ID=$(echo "$USER_ID" | xargs)   # trim
  [ -z "$USER_ID" ] && continue

  echo "[Discord Send] Creating DM channel for user $USER_ID..."
  RESPONSE=$(curl -s -X POST \
    -H "Authorization: Bot $DISCORD_BOT_TOKEN" \
    -H "Content-Type: application/json" \
    -d "{\"recipient_id\": \"$USER_ID\"}" \
    "https://discord.com/api/v10/users/@me/channels")

  if command -v jq >/dev/null 2>&1; then
    CHANNEL_ID=$(echo "$RESPONSE" | jq -r '.id')
  else
    CHANNEL_ID=$(echo "$RESPONSE" | sed -n 's/.*"id": *"\([0-9]*\)".*/\1/p')
  fi

  if [ -z "$CHANNEL_ID" ] || [ "$CHANNEL_ID" = "null" ]; then
    echo "[Discord Send] Failed to create DM channel for user $USER_ID. API response: $RESPONSE"
    continue
  fi

  echo "[Discord Send] Sending notification to Channel ID $CHANNEL_ID..."
  if [ -n "$EMBED_JSON" ]; then
    SEND_RESP=$(curl -s -X POST \
      -H "Authorization: Bot $DISCORD_BOT_TOKEN" \
      -H "Content-Type: application/json" \
      -d "{\"embeds\": [$EMBED_JSON]}" \
      "https://discord.com/api/v10/channels/$CHANNEL_ID/messages")
  else
    SEND_RESP=$(curl -s -X POST \
      -H "Authorization: Bot $DISCORD_BOT_TOKEN" \
      -H "Content-Type: application/json" \
      -d "{\"content\": \"$MESSAGE\"}" \
      "https://discord.com/api/v10/channels/$CHANNEL_ID/messages")
  fi

  if echo "$SEND_RESP" | grep -q '"id":'; then
    echo "[Discord Send] Notification successfully sent to developer $USER_ID."
  else
    echo "[Discord Send] Failed to send message to developer $USER_ID. Response: $SEND_RESP"
  fi
done
