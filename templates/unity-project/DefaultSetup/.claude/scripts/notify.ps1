# Notification classification and Discord routing (PowerShell twin of notify.sh).
#
# Self-contained: builds the Discord Embed AND sends the developer DM inline.
# BlazeSurvivor ships only discord-send.sh (bash), so this script does not depend on
# a discord-send.ps1 — it POSTs to Discord directly via Invoke-RestMethod. Gracefully
# degrades (no-op, exit 0) when Discord is not configured, so the loop never fails
# just because notifications are off.
#
# Configure by adding to <repo>/.env (gitignored):
#   DISCORD_BOT_TOKEN=...
#   DISCORD_DEVELOPERS=userid1,userid2
#
# Usage:
#   powershell -File notify.ps1 -Event TASK_COMPLETED -Task "..." [-Url "..."] [-Details "..."] [-Tokens "..."] [-Progress "5/12"] [-Duration "00:04:12"] [-Breakdown "..."] [-PerModel "..."] [-Cumulative "..."]

param(
    [Parameter(Mandatory = $true)]
    [string]$Event,
    [string]$Task = "",
    [string]$Details = "",
    [string]$Url = "",
    # Multi-line token summary for this iteration (total / in / out / cache / turns),
    # pre-formatted by the caller's Get-TokenReport.
    [string]$Tokens = "",
    # "<current>/<total>" position of this task in the whole backlog (e.g. "5/12").
    [string]$Progress = "",
    # Wall-clock time this task's iteration took, e.g. "00:04:12".
    [string]$Duration = "",
    # Per-tool time+token table (from Get-TimingTokenBreakdown), pre-formatted as fixed-width
    # text rows. Empty when the caller has nothing to analyze (e.g. BACKLOG_EMPTY, where no
    # iteration log exists) - the field below is only added when this is non-empty.
    [string]$Breakdown = "",
    # Per-model token + cost rows for this iteration (subagent usage included), one model
    # per line. Empty when the log carries no modelUsage block.
    [string]$PerModel = "",
    # Running total across every iteration of the current loop run
    # ("3 iters | 41.2M tok | ~$28.90"). Empty on the first notification.
    [string]$Cumulative = ""
)

$ErrorActionPreference = "Stop"
$ScriptDir = $PSScriptRoot
$ProjectRoot = Split-Path -Parent (Split-Path -Parent $ScriptDir)

# Embed colors (decimal)
$COLOR_SUCCESS = 3066993    # Green  (#2ecc71)
$COLOR_ERROR   = 15158332   # Red    (#e74c3c)
$COLOR_WARNING = 15105570   # Orange (#e67e22)

switch ($Event) {
    "BACKLOG_EMPTY" {
        $title = "✅ All Backlog Tasks Completed"
        $description = "The backlog TODO queue is empty. The automation loop has paused safely."
        $color = $COLOR_SUCCESS
    }
    "TASK_COMPLETED" {
        $title = "✅ Task Completed"
        $description = "A backlog task passed all quality gates and was committed to agent/dev."
        $color = $COLOR_SUCCESS
    }
    "COMPILE_BLOCKED" {
        $title = "🔴 Compilation Blocked"
        $description = "Unity compilation failed and could not be resolved automatically after 2 fix rounds."
        $color = $COLOR_ERROR
    }
    "PREFLIGHT_BLOCKED" {
        $title = "🔴 Preflight Blocked"
        $description = "Deterministic critical findings detected in preflight checks."
        $color = $COLOR_ERROR
    }
    "REVIEW_BLOCKED" {
        $title = "🔴 Review Blocked"
        $description = "Code, Performance, or Security reviewer blocked the changes after 2 fix rounds."
        $color = $COLOR_ERROR
    }
    "VERIFY_BLOCKED" {
        $title = "🔴 QA Verification Blocked"
        $description = "QA Verifier reported unmet acceptance criteria after 2 fix rounds."
        $color = $COLOR_ERROR
    }
    "RUNTIME_BLOCKED" {
        $title = "🔴 Runtime Smoke Blocked"
        $description = "Runtime smoke gate (play mode + console assert) still failing after 2 fix rounds."
        $color = $COLOR_ERROR
    }
    "CLI_ERROR" {
        $title = "🔴 Automation CLI Error"
        $description = "The agent CLI exited with a non-zero status. The loop stopped unexpectedly."
        $color = $COLOR_ERROR
    }
    "API_RETRY" {
        $title = "⚠️ Transient API Error - Retrying"
        $description = "The agent CLI hit a transport break or an overloaded API. The loop is retrying the same task and has NOT stopped."
        $color = $COLOR_WARNING
    }
    default {
        $title = "⚠️ Automation Event: $Event"
        $description = "An automation stop condition or event occurred."
        $color = $COLOR_WARNING
    }
}

# Fold "which task in the backlog" + "how long it took" into the title so
# they're visible without opening the embed body.
$titleSuffixParts = @()
if ($Progress) { $titleSuffixParts += "Task $Progress" }
if ($Duration) { $titleSuffixParts += $Duration }
if ($titleSuffixParts.Count -gt 0) {
    $title = "$title (" + ($titleSuffixParts -join ", ") + ")"
}

$timestamp = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

# Build the Task field value (markdown link when a URL is present)
if ($Url) {
    $taskFieldVal = "[$Task]($Url)"
} else {
    $taskFieldVal = $Task
}
if (-not $taskFieldVal) { $taskFieldVal = "N/A" }

# Details block: default + truncate to stay under Discord's 1024-char field limit
$detailsText = $Details
if (-not $detailsText) { $detailsText = "No additional details provided." }
if ($detailsText.Length -gt 900) { $detailsText = $detailsText.Substring(0, 900) + "..." }

$fence = '```'   # triple backtick code fence (literal in single quotes)
$detailsValue = "$fence`n$detailsText`n$fence"

# Construct the embed object, then serialize. ConvertTo-Json handles all escaping.
$tokenVal = if ($Tokens) { $Tokens } else { "N/A" }
$fields = [System.Collections.ArrayList]@(
    [ordered]@{ name = "Task"; value = $taskFieldVal; inline = $true }
    [ordered]@{ name = "Token Usage"; value = $tokenVal; inline = $true }
)

# Per-model split (main agent + subagents). Only present when the CLI reported a
# modelUsage block, so it stays absent for providers/runs that don't emit one.
if ($PerModel) {
    $perModelText = $PerModel
    if ($perModelText.Length -gt 1000) { $perModelText = $perModelText.Substring(0, 1000) + "..." }
    [void]$fields.Add([ordered]@{ name = "Cost by Model"; value = $perModelText; inline = $true })
}

# Loop-wide running total, so a long unattended run shows what it has burned so far
# without having to add up every per-task notification by hand.
if ($Cumulative) {
    [void]$fields.Add([ordered]@{ name = "Loop Total (so far)"; value = $Cumulative; inline = $true })
}

[void]$fields.Add([ordered]@{ name = "Details / Error Log"; value = $detailsValue; inline = $false })

# Only add the breakdown field when the caller actually had something to analyze
# (empty for BACKLOG_EMPTY, and for any event where the log couldn't be parsed).
if ($Breakdown) {
    $breakdownText = $Breakdown
    if ($breakdownText.Length -gt 1000) { $breakdownText = $breakdownText.Substring(0, 1000) + "..." }
    [void]$fields.Add([ordered]@{ name = "Time & Token Breakdown (approx., per tool)"; value = "$fence`n$breakdownText`n$fence"; inline = $false })
}

$embed = [ordered]@{
    title       = $title
    description = $description
    color       = $color
    timestamp   = $timestamp
    fields      = $fields
}

# ------------------------------------------------------------------------------
# Inline Discord DM sender (self-contained port of discord-send.sh).
# ------------------------------------------------------------------------------

# Load environment variables if .env exists.
$envPath = Join-Path $ProjectRoot ".env"
if (Test-Path $envPath) {
    foreach ($line in Get-Content $envPath) {
        $trimmed = $line.Trim()
        if (-not $trimmed -or $trimmed.StartsWith('#') -or -not $trimmed.Contains('=')) { continue }
        $idx = $trimmed.IndexOf('=')
        $key = $trimmed.Substring(0, $idx).Trim()
        $val = $trimmed.Substring($idx + 1).Trim()
        if ($key) { Set-Item -Path "Env:$key" -Value $val }
    }
}

$token = $env:DISCORD_BOT_TOKEN
$developers = $env:DISCORD_DEVELOPERS

if (-not $token -or $token -eq "YOUR_DISCORD_BOT_TOKEN_HERE") {
    Write-Host "[Discord Send] DISCORD_BOT_TOKEN not configured in .env. Skipping notification."
    exit 0
}
if (-not $developers -or $developers -eq "DEVELOPER_USER_ID_1,DEVELOPER_USER_ID_2") {
    Write-Host "[Discord Send] DISCORD_DEVELOPERS not configured in .env. Skipping notification."
    exit 0
}

$headers = @{
    "Authorization" = "Bot $token"
    "Content-Type"  = "application/json"
}
# Discord's Cloudflare edge blocks Bot API requests carrying a browser-like User-Agent
# (PS 5.1's default UA starts with "Mozilla/5.0..."), returning a misleading
# 403 { "code": 40333, "message": "internal network error" }. Send the documented
# bot UA format to avoid the block: https://github.com/discord/discord-api-docs/issues/6473
$discordUserAgent = "DiscordBot (https://github.com/bf-gamedev/BlazeSurvivor, 1.0)"

foreach ($rawId in ($developers -split ',')) {
    $userId = $rawId.Trim()
    if (-not $userId) { continue }

    Write-Host "[Discord Send] Creating DM channel for user $userId..."
    try {
        $dmBody = @{ recipient_id = $userId } | ConvertTo-Json -Compress
        $dmResp = Invoke-RestMethod -Uri "https://discord.com/api/v10/users/@me/channels" `
            -Method Post -Headers $headers -UserAgent $discordUserAgent -Body $dmBody
        $channelId = $dmResp.id
    } catch {
        Write-Host "[Discord Send] Failed to create DM channel for user $userId. $($_.Exception.Message)"
        continue
    }

    if (-not $channelId) {
        Write-Host "[Discord Send] Failed to resolve DM channel id for user $userId."
        continue
    }

    Write-Host "[Discord Send] Sending notification to Channel ID $channelId..."
    try {
        $msgBody = @{ embeds = @($embed) } | ConvertTo-Json -Depth 10
        # Send the body as explicit UTF-8 bytes so emoji/Unicode survive on Windows PowerShell 5.1.
        $bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($msgBody)
        $sendResp = Invoke-RestMethod -Uri "https://discord.com/api/v10/channels/$channelId/messages" `
            -Method Post -Headers $headers -UserAgent $discordUserAgent -Body $bodyBytes
        if ($sendResp.id) {
            Write-Host "[Discord Send] Notification successfully sent to developer $userId."
        } else {
            Write-Host "[Discord Send] Send returned no message id for developer $userId."
        }
    } catch {
        Write-Host "[Discord Send] Failed to send message to developer $userId. $($_.Exception.Message)"
    }
}
