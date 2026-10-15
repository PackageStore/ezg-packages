# Notification classification and Discord routing (PowerShell twin of notify.sh).
#
# Self-contained: builds the Discord Embed AND sends it inline (the sender block below
# is byte-identical to discord-send.ps1), so a project that updates only this file
# still sends correctly. Posts to developers' DMs, to server channels, or both;
# gracefully degrades (no-op, exit 0) when Discord is not configured, so the loop
# never fails just because notifications are off.
#
# Configure by adding to <repo>/.env (gitignored):
#   DISCORD_BOT_TOKEN=...
#   DISCORD_DEVELOPERS=userid1,userid2      # DM each user (optional)
#   DISCORD_CHANNELS=channelid1,channelid2  # post in each channel (optional)
#
# Usage:
#   powershell -File notify.ps1 -Event TASK_COMPLETED -Task "..." [-Url "..."] [-Details "..."] [-Tokens "..."] [-Progress "5/12"] [-Duration "00:04:12"] [-Breakdown "..."] [-PerModel "..."] [-Cumulative "..."] [-Project "My Game"]
#   NOTIFY_DRY_RUN=1 prints the payload instead of sending it.
#
# Embed layout (kept short on purpose - it is read on a phone): the title carries
# project + event + position + duration, and EVERYTHING else is one description with
# one line per topic. No embed fields: each field costs a header line, inline fields
# wrap in narrow columns, and the old fixed-width tables cost a line per row.

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
    # Per-tool time+token table (from Get-TimingTokenBreakdown): a header row, then one
    # fixed-width "<tool>  <time>  <tokens>" row per tool. Empty when the caller has
    # nothing to analyze (e.g. BACKLOG_EMPTY, where no iteration log exists).
    [string]$Breakdown = "",
    # Per-model token + cost rows for this iteration (subagent usage included), one model
    # per line. Empty when the log carries no modelUsage block.
    [string]$PerModel = "",
    # Running total across every iteration of the current loop run
    # ("3 iters | 41.2M tok | ~$28.90"). Empty on the first notification.
    [string]$Cumulative = "",
    # Human-readable project name prefixed to the title ("My Game"). Empty =
    # resolved from .claude/project-profile.json (see Get-ProjectDisplayName).
    [string]$Project = ""
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
        # No description: the title already says it, and the details line says where
        # the commit went (the old fixed sentence named agent/dev even in Current mode).
        $title = "✅ Task Completed"
        $description = ""
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
    "SILENT_RETRY" {
        $title = "⚠️ Silent End - Resuming Once"
        $description = "The iteration exited cleanly without a stop token while its task was still in progress. The loop has NOT stopped: the next iteration resumes the task once."
        $color = $COLOR_WARNING
    }
    "TASK_CHECKPOINTED" {
        $title = "⏸️ Task Checkpointed - Resuming"
        $description = "A multi-iteration task saved its progress at the iteration deadline. The loop has NOT stopped: the next iteration resumes the same task."
        $color = $COLOR_SUCCESS
    }
    "CHECKPOINT_LIMIT" {
        $title = "🟠 Checkpoint Limit Reached"
        $description = "A multi-iteration task checkpointed too many consecutive iterations without finishing. The loop stopped for a human look."
        $color = $COLOR_WARNING
    }
    "TASK_RECOVERING" {
        $title = "🛠️ Self-Heal - Recovering Task"
        $description = "The iteration ended without finishing its task (block, crash, hang or silent end). The loop has NOT stopped: the next iteration resumes the task in recovery mode with a brief of what failed."
        $color = $COLOR_WARNING
    }
    "TASK_PARKED" {
        $title = "🅿️ Self-Heal - Task Parked"
        $description = "The task could not be finished within its recovery budget. Its partial work was saved under refs/backlog/parked/<NNN>, the task moved to the tail of TODO, and the loop continues with the next task."
        $color = $COLOR_WARNING
    }
    "PARKED_ONLY" {
        $title = "🟠 Only Parked Tasks Remain"
        $description = "Every remaining task was parked during this run. The loop stopped; relaunch it to give the parked tasks a fresh budget."
        $color = $COLOR_WARNING
    }
    "LIMIT_WAIT" {
        $title = "⏳ Waiting Out a Usage Limit"
        $description = "The usage/session limit (or an API outage) was hit. The loop sleeps until the reset and then resumes the same task. It has NOT stopped."
        $color = $COLOR_WARNING
    }
    "BUSY_WAIT" {
        $title = "⏳ Waiting for Another Session"
        $description = "Another live session owns the task. The loop waits for it to finish before continuing."
        $color = $COLOR_WARNING
    }
    "EDITOR_RECOVERY" {
        $title = "🛠️ Self-Heal - Unity Editor (Re)started"
        $description = "Every remaining task needs a live Unity Editor and none answered. The loop (re)started this project's Editor and resumes."
        $color = $COLOR_WARNING
    }
    "GATE_RECEIPT_MISSING" {
        $title = "🟠 Reviewer Receipt Missing"
        $description = "A task reached DONE without a mandatory reviewer spawn in its log. With self-heal, a post-hoc audit iteration runs the missing reviewer(s) next."
        $color = $COLOR_WARNING
    }
    "AUDIT_DONE" {
        $title = "🔎 Post-hoc Review Finished"
        $description = "The missing reviewer(s) ran on the task's commit; any fix landed as a follow-up commit."
        $color = $COLOR_SUCCESS
    }
    "MANUAL_INTERVENTION" {
        $title = "🔴 Manual Intervention Requested"
        $description = "The iteration asked for a human without a named block token."
        $color = $COLOR_ERROR
    }
    default {
        $title = "⚠️ Automation Event: $Event"
        $description = "An automation stop condition or event occurred."
        $color = $COLOR_WARNING
    }
}

# "My Game" for the title prefix. Twin of Profile.display_name in
# project_profile.py, but read straight from the JSON: on Windows `python3` is often
# only the Microsoft Store stub, and the project name must not depend on it.
function Get-ProjectDisplayName {
    $name = ""
    $profilePath = Join-Path (Split-Path -Parent $ScriptDir) "project-profile.json"
    if (Test-Path -LiteralPath $profilePath) {
        try {
            $json = Get-Content -LiteralPath $profilePath -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($json.displayName -and ([string]$json.displayName).Trim()) { return ([string]$json.displayName).Trim() }
            $name = [string]$json.projectName
        } catch { }
    }
    if (-not $name) { $name = Split-Path -Leaf $ProjectRoot }
    $name = $name -replace '[_-]+', ' '
    $name = $name -creplace '(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])', ' '
    return ((($name -split '\s+') | Where-Object { $_ }) -join ' ')
}

# Backslash-escape Discord markdown so a task title like "fix *_x_*" shows literally.
function ConvertTo-DiscordText {
    param([string]$Text)
    return [regex]::Replace($Text, '([\\*_~`|])', '\$1')
}

# "a | b`nc | d" -> "a<inner>b · c<inner>d": the caller's multi-line, pipe-separated
# summaries folded onto one wrapping line.
function Join-SummaryLines {
    param([string]$Text, [string]$Inner = " · ")
    $parts = @()
    foreach ($line in ($Text -split "`r?`n")) {
        $line = $line.Trim()
        if ($line) { $parts += ($line -replace ' \| ', $Inner) }
    }
    return ($parts -join " · ")
}

# Fixed-width breakdown table -> "exec 12.3m/1.2M · Read 3.1m/400K · ...". Rows are
# "<name>  <time>  <tokens>" with 2+ spaces between columns (the name itself may hold
# one space: "+3 more"); the first row is the header.
function Format-Breakdown {
    param([string]$Text)
    $parts = @()
    foreach ($row in @($Text -split "`r?`n" | Select-Object -Skip 1)) {
        $m = [regex]::Match($row, '^(.*\S) {2,}(\S+) {2,}(\S+) *$')
        if ($m.Success) {
            $parts += ("{0} {1}/{2}" -f (ConvertTo-DiscordText $m.Groups[1].Value.Trim()), $m.Groups[2].Value, $m.Groups[3].Value)
        }
    }
    return ($parts -join " · ")
}

if (-not $Project) { $Project = Get-ProjectDisplayName }
if ($Project) { $title = "${Project}: $title" }

# Fold "which task in the backlog" + "how long it took" into the title so
# they're visible without opening the embed body.
$titleSuffixParts = @()
if ($Progress) { $titleSuffixParts += "Task $Progress" }
if ($Duration) { $titleSuffixParts += $Duration }
if ($titleSuffixParts.Count -gt 0) {
    $title = "$title (" + ($titleSuffixParts -join ", ") + ")"
}
if ($title.Length -gt 256) { $title = $title.Substring(0, 253) + "..." }

# Invariant culture: in a .NET format ":" is the CULTURE's time separator (fi-FI gives
# "11.01.49"), and Discord rejects a timestamp that is not ISO 8601.
$timestamp = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", [System.Globalization.CultureInfo]::InvariantCulture)
$bt = '`'
$fence = '```'   # triple backtick code fence (literal in single quotes)

$lines = New-Object System.Collections.Generic.List[string]

# Task. The loop's Url is a file:// path on its own machine, which Discord does not
# render as a link (only http/https) - the old "[title](file://...)" arrived as raw
# text spanning lines. The file name identifies the task and stays true after the
# file moves from in-progress/ to done/.
if ($Task -and $Task -ne "N/A") {
    $taskText = ConvertTo-DiscordText $Task
    if ($Url -match '^https?://') {
        $lines.Add("**[$taskText]($Url)**")
    } else {
        $taskLine = "**$taskText**"
        $leaf = if ($Url) { ($Url -split '[\\/]')[-1] } else { "" }
        if ($leaf) { $taskLine += " · $bt$leaf$bt" }
        $lines.Add($taskLine)
    }
}

if ($description) { $lines.Add($description) }

# Details: the success events' details are the loop's own one-liner (it may carry
# inline code), so they stay plain; anything else is an error/stop reason quoted
# verbatim from a log, so it goes in a code block. Truncated well under the 4096-char
# description limit.
if ($Details) {
    $detailsText = $Details
    if ($detailsText.Length -gt 900) { $detailsText = $detailsText.Substring(0, 900) + "..." }
    if ($color -eq $COLOR_SUCCESS) {
        $lines.Add($detailsText)
    } else {
        $lines.Add("$fence`n$($detailsText.Replace($fence, "'''"))`n$fence")
    }
}

if ($Tokens) { $lines.Add("**Tokens** " + (Join-SummaryLines $Tokens)) }
# Per-model split (main agent + subagents). Only present when the CLI reported a
# modelUsage block, so it stays absent for providers/runs that don't emit one.
if ($PerModel) { $lines.Add("**By model** " + (Join-SummaryLines ($PerModel -replace ': ', ' ') " ")) }
# Loop-wide running total, so a long unattended run shows what it has burned so far
# without having to add up every per-task notification by hand.
if ($Cumulative) { $lines.Add("**Loop total** " + (Join-SummaryLines $Cumulative)) }
if ($Breakdown) {
    $breakdownText = Format-Breakdown $Breakdown
    if ($breakdownText) { $lines.Add("**Per tool** (time/tokens, approx.) " + $breakdownText) }
}

$embed = [ordered]@{
    title     = $title
    color     = $color
    timestamp = $timestamp
}
if ($lines.Count -gt 0) {
    $descriptionText = ($lines -join "`n")
    if ($descriptionText.Length -gt 4000) { $descriptionText = $descriptionText.Substring(0, 4000) + "..." }
    $embed.description = $descriptionText
}

# NOTIFY_DRY_RUN=1: print the payload instead of sending it (layout checks).
if ($env:NOTIFY_DRY_RUN -eq "1") {
    @{ embeds = @($embed) } | ConvertTo-Json -Depth 10
    exit 0
}

# ------------------------------------------------------------------------------
# Inline Discord sender (DMs + channels).
# ------------------------------------------------------------------------------

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

try {
    Send-DiscordPayload -Embed $embed -EnvPath (Join-Path $ProjectRoot ".env")
} catch {
    Write-Host "[Discord Send] Unexpected error. $($_.Exception.Message)"
}
exit 0
