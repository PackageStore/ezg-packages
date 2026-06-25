# Gemini CLI wrapper for the shared autonomous backlog loop.
#
# Default behavior mirrors the Claude wrapper by running headless with --yolo.
# Use -NoSkipPermissions to omit --yolo.

[CmdletBinding()]
param(
    [int]$MaxIterations = 100,
    [string]$LogDir = "logs/backlog-loop",
    [AllowEmptyString()]
    [string]$Model = "gemini-3.1-pro-preview",
    [switch]$NoSkipPermissions,
    # Gemini thinking budget (tokens), exported as GEMINI_THINKING_BUDGET.
    # Pass 0 to disable thinking.
    [int]$ThinkingTokens = 10000,
    [int]$XsThinkingTokens = 3000,
    [int]$SThinkingTokens = 6000,
    [int]$MThinkingTokens = 10000,
    [int]$LThinkingTokens = 10000,
    [switch]$NoAutoThinkingByTier
)

$coreArgs = @{
    Provider = "gemini"
    MaxIterations = $MaxIterations
    LogDir = $LogDir
    Model = $Model
    ThinkingTokens = $ThinkingTokens
}

if (-not $NoAutoThinkingByTier) {
    $coreArgs.AutoThinkingByTier = $true
    $coreArgs.XsThinkingTokens = $XsThinkingTokens
    $coreArgs.SThinkingTokens = $SThinkingTokens
    $coreArgs.MThinkingTokens = $MThinkingTokens
    $coreArgs.LThinkingTokens = $LThinkingTokens
}

if ($NoSkipPermissions) {
    $coreArgs.NoSkipPermissions = $true
}

& "$PSScriptRoot\run-backlog-loop-core.ps1" @coreArgs
exit $LASTEXITCODE
