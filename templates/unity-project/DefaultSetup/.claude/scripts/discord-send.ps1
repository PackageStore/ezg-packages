# Core script to send Discord Direct Messages (DMs) to developers using a Bot Token.
# PowerShell port of discord-send.sh. Gracefully degrades if not configured.
#
# Usage: powershell -File discord-send.ps1 -Message "text" [-EmbedJson '<single-embed-json>']

param(
    [string]$Message = "",
    [string]$EmbedJson = ""
)

$ErrorActionPreference = "Stop"

$ScriptDir = $PSScriptRoot
$ProjectRoot = Split-Path -Parent (Split-Path -Parent $ScriptDir)

# Load environment variables if .env exists
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

foreach ($rawId in ($developers -split ',')) {
    $userId = $rawId.Trim()
    if (-not $userId) { continue }

    Write-Host "[Discord Send] Creating DM channel for user $userId..."
    try {
        $dmBody = @{ recipient_id = $userId } | ConvertTo-Json -Compress
        $dmResp = Invoke-RestMethod -Uri "https://discord.com/api/v10/users/@me/channels" `
            -Method Post -Headers $headers -Body $dmBody
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
        if ($EmbedJson) {
            $embedObj = $EmbedJson | ConvertFrom-Json
            $msgBody = @{ embeds = @($embedObj) } | ConvertTo-Json -Depth 10
        } else {
            $msgBody = @{ content = $Message } | ConvertTo-Json -Compress
        }
        # Send the body as explicit UTF-8 bytes so emoji/Unicode survive on Windows PowerShell 5.1.
        $bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($msgBody)
        $sendResp = Invoke-RestMethod -Uri "https://discord.com/api/v10/channels/$channelId/messages" `
            -Method Post -Headers $headers -Body $bodyBytes
        if ($sendResp.id) {
            Write-Host "[Discord Send] Notification successfully sent to developer $userId."
        } else {
            Write-Host "[Discord Send] Send returned no message id for developer $userId."
        }
    } catch {
        Write-Host "[Discord Send] Failed to send message to developer $userId. $($_.Exception.Message)"
    }
}
