# Notification classification and routing script (PowerShell port of notify.sh).
# Translates automation stop conditions and events into structured Discord Embeds.
#
# Usage: powershell -File notify.ps1 -Event TASK_COMPLETED -Task "..." [-Url "..."] [-Details "..."]

param(
    [Parameter(Mandatory = $true)]
    [string]$Event,
    [string]$Task = "",
    [string]$Details = "",
    [string]$Url = "",
    [string]$Tokens = ""
)

$ErrorActionPreference = "Stop"
$ScriptDir = $PSScriptRoot

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
        $description = "A backlog task passed all quality gates and was committed successfully."
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
        $description = "Code, Performance, or Security reviewer has blocked the changes."
        $color = $COLOR_ERROR
    }
    "VERIFY_BLOCKED" {
        $title = "🔴 QA Verification Blocked"
        $description = "QA Verifier reported unmet acceptance criteria after 2 fix rounds."
        $color = $COLOR_ERROR
    }
    "CLI_ERROR" {
        $title = "🔴 Automation CLI Error"
        $description = "The agent CLI exited with a non-zero status. The loop stopped unexpectedly."
        $color = $COLOR_ERROR
    }
    default {
        $title = "⚠️ Automation Event: $Event"
        $description = "An automation stop condition or event occurred."
        $color = $COLOR_WARNING
    }
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
$embed = [ordered]@{
    title       = $title
    description = $description
    color       = $color
    timestamp   = $timestamp
    fields      = @(
        [ordered]@{ name = "Task"; value = $taskFieldVal; inline = $true }
        [ordered]@{ name = "Token Usage"; value = $tokenVal; inline = $true }
        [ordered]@{ name = "Details / Error Log"; value = $detailsValue; inline = $false }
    )
}

$embedJson = $embed | ConvertTo-Json -Depth 6 -Compress

# Forward to the core sender (same host, works under both powershell.exe and pwsh)
& (Join-Path $ScriptDir "discord-send.ps1") -EmbedJson $embedJson
