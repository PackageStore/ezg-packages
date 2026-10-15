# Core script to post a Discord message with a Bot Token - to developers' DMs, to
# server channels, or both. PowerShell twin of discord-send.sh. Each destination
# list is optional: an empty list (or one holding only the .env placeholder) sends
# nothing to that kind, and with no list configured at all the script is a no-op.
# Always exits 0, so the loop never fails just because notifications are off or
# Discord is unreachable.
#
# Configure by adding to <repo>/.env (gitignored):
#   DISCORD_BOT_TOKEN=...
#   DISCORD_DEVELOPERS=userid1,userid2      # DM each user (optional)
#   DISCORD_CHANNELS=channelid1,channelid2  # post in each channel (optional)
# The bot must share a server with every DM user, and must be a member of every
# channel's server with View Channel + Send Messages + Embed Links there.
#
# Usage: powershell -File discord-send.ps1 -Message "text" [-EmbedJson '<single-embed-json>']
#   Either argument may be empty; with both empty nothing is sent.

param(
    [string]$Message = "",
    [string]$EmbedJson = ""
)

$ErrorActionPreference = "Stop"

$ScriptDir = $PSScriptRoot
$ProjectRoot = Split-Path -Parent (Split-Path -Parent $ScriptDir)

# >>> discord-sender (byte-identical in discord-send.ps1 and notify.ps1 - edit both;
# test_discord_notify.py fails when they drift. Twin of discord-send.sh.)
$DiscordApi = "https://discord.com/api/v10"
# Discord's Cloudflare edge blocks Bot API requests carrying a browser-like User-Agent
# (PS 5.1's default UA starts with "Mozilla/5.0..."), returning a misleading
# 403 { "code": 40333, "message": "internal network error" }. Send the documented
# bot UA format ("DiscordBot (<url>, <version>)") to avoid the block.
$DiscordUserAgent = "DiscordBot (https://github.com/PackageStore/ezg-packages, 1.0)"
$DiscordEnvKeys = @("DISCORD_BOT_TOKEN", "DISCORD_DEVELOPERS", "DISCORD_CHANNELS")

# The DISCORD_* settings: a key in .env wins over the process env (an empty value
# turns it off). Returned as a hashtable, never written to the process env: notify.ps1
# runs inside the long-lived loop controller, where a write would outlive later .env
# edits and hand the bot token to every agent process the loop starts.
# Quoted values end at the closing quote; an unquoted inline " # comment" is dropped.
function Get-DiscordConfig {
    param([string]$Path)
    $config = @{}
    foreach ($key in $DiscordEnvKeys) { $config[$key] = [string][Environment]::GetEnvironmentVariable($key) }
    if (-not (Test-Path -LiteralPath $Path)) { return $config }
    foreach ($line in (Get-Content -LiteralPath $Path -Encoding UTF8)) {
        $trimmed = ([string]$line).Trim()
        if (-not $trimmed -or $trimmed.StartsWith('#')) { continue }
        if ($trimmed.StartsWith('export ')) { $trimmed = $trimmed.Substring(7).TrimStart() }
        $idx = $trimmed.IndexOf('=')
        if ($idx -lt 1) { continue }
        $key = $trimmed.Substring(0, $idx).Trim()
        if ($DiscordEnvKeys -notcontains $key) { continue }
        $val = $trimmed.Substring($idx + 1).Trim()
        if ($val.StartsWith('"') -or $val.StartsWith("'")) {
            $quote = $val.Substring(0, 1)
            $val = $val.Substring(1)
            $end = $val.IndexOf($quote)
            if ($end -ge 0) { $val = $val.Substring(0, $end) }
        } else {
            $val = $val -replace '\s+#.*$', ''
        }
        $config[$key] = $val.Trim()
    }
    return $config
}

# The valid Discord IDs (snowflakes: digits only) of a comma/space separated list,
# deduplicated. The .env placeholders ("DEVELOPER_USER_ID_1") and typos are reported
# and skipped, so a half-filled .env never sends anywhere.
function Get-DiscordIds {
    param([string]$List, [string]$Label)
    $ids = New-Object System.Collections.Generic.List[string]
    foreach ($raw in ([string]$List -split '[,;\s]+')) {
        $id = $raw.Trim()
        if (-not $id) { continue }
        if ($id -notmatch '^[0-9]+$') {
            Write-Host "[Discord Send] Ignoring invalid $Label ID '$id' (expected digits only)."
            continue
        }
        if (-not $ids.Contains($id)) { $ids.Add($id) }
    }
    return $ids.ToArray()
}

function Invoke-DiscordPost {
    param([string]$Token, [string]$Path, [string]$Json)
    # Explicit UTF-8 bytes so emoji/Unicode survive on Windows PowerShell 5.1.
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Json)
    return Invoke-RestMethod -Uri "$DiscordApi$Path" -Method Post `
        -Headers @{ "Authorization" = "Bot $Token" } -UserAgent $DiscordUserAgent `
        -ContentType "application/json" -Body $bytes -TimeoutSec 30
}

# Post $Message and/or one $Embed (hashtable or object) to every configured DM user
# and channel. Never throws: every failure is logged and skipped.
function Send-DiscordPayload {
    param([string]$Message = "", $Embed = $null, [string]$EnvPath)
    $config = Get-DiscordConfig $EnvPath
    $token = [string]$config["DISCORD_BOT_TOKEN"]
    if (-not $token -or $token -eq "YOUR_DISCORD_BOT_TOKEN_HERE") {
        Write-Host "[Discord Send] DISCORD_BOT_TOKEN not configured in .env. Skipping notification."
        return
    }
    $dmIds = @(Get-DiscordIds ([string]$config["DISCORD_DEVELOPERS"]) "developer")
    $channelIds = @(Get-DiscordIds ([string]$config["DISCORD_CHANNELS"]) "channel")
    if ($dmIds.Count -eq 0 -and $channelIds.Count -eq 0) {
        Write-Host "[Discord Send] Neither DISCORD_DEVELOPERS nor DISCORD_CHANNELS holds a valid ID in .env. Skipping notification."
        return
    }
    if (-not $Message -and -not $Embed) {
        Write-Host "[Discord Send] Nothing to send (empty message and embed)."
        return
    }

    # allowed_mentions parse=[]: text quoted from a log or a task title ("@everyone")
    # must never ping a channel.
    $payload = [ordered]@{ allowed_mentions = @{ parse = @() } }
    if ($Message) { $payload.content = $Message }
    if ($Embed) { $payload.embeds = @($Embed) }
    $body = $payload | ConvertTo-Json -Depth 10 -Compress

    # Windows PowerShell 5.1 on .NET 4.x may offer only SSL3/TLS 1.0 unless the machine
    # opted in; Discord needs TLS 1.2. Left alone when it is SystemDefault (0: the OS
    # picks, TLS 1.3 included) or already has TLS 1.2; pwsh 7 ignores this setting.
    try {
        $protocols = [Net.ServicePointManager]::SecurityProtocol
        if ([int]$protocols -ne 0 -and -not ($protocols -band [Net.SecurityProtocolType]::Tls12)) {
            [Net.ServicePointManager]::SecurityProtocol = $protocols -bor [Net.SecurityProtocolType]::Tls12
        }
    } catch { }

    $targets = New-Object System.Collections.Generic.List[object]
    foreach ($userId in $dmIds) {
        Write-Host "[Discord Send] Creating DM channel for user $userId..."
        try {
            $dmResp = Invoke-DiscordPost $token "/users/@me/channels" (@{ recipient_id = $userId } | ConvertTo-Json -Compress)
            if ($dmResp.id) {
                $targets.Add(@{ Id = [string]$dmResp.id; Label = "developer $userId (DM)" })
            } else {
                Write-Host "[Discord Send] Failed to resolve DM channel id for user $userId."
            }
        } catch {
            Write-Host "[Discord Send] Failed to create DM channel for user $userId. $($_.Exception.Message)"
        }
    }
    foreach ($channelId in $channelIds) { $targets.Add(@{ Id = $channelId; Label = "channel $channelId" }) }

    foreach ($target in $targets) {
        try {
            $sendResp = Invoke-DiscordPost $token "/channels/$($target.Id)/messages" $body
            if ($sendResp.id) {
                Write-Host "[Discord Send] Notification sent to $($target.Label)."
            } else {
                Write-Host "[Discord Send] Send returned no message id for $($target.Label)."
            }
        } catch {
            Write-Host "[Discord Send] Failed to send message to $($target.Label). $($_.Exception.Message)"
        }
    }
}
# <<< discord-sender

$embed = $null
if ($EmbedJson) {
    try {
        $embed = $EmbedJson | ConvertFrom-Json
    } catch {
        Write-Host "[Discord Send] -EmbedJson is not valid JSON. $($_.Exception.Message)"
        exit 0
    }
}

try {
    Send-DiscordPayload -Message $Message -Embed $embed -EnvPath (Join-Path $ProjectRoot ".env")
} catch {
    Write-Host "[Discord Send] Unexpected error. $($_.Exception.Message)"
}
exit 0
