# Backward-compatible alias for the Claude backlog loop.
#
# Existing usage still works:
#   powershell -ExecutionPolicy Bypass -File .agents/scripts/run-backlog-loop.ps1

[CmdletBinding()]
param(
    [int]$MaxIterations = 100,
    [string]$LogDir = "logs/backlog-loop",
    [AllowEmptyString()]
    [string]$Model = "sonnet",
    [AllowEmptyString()]
    [string]$Effort = "",
    [int]$ThinkingTokens = 10000,
    [int]$XsThinkingTokens = 3000,
    [int]$SThinkingTokens = 6000,
    [int]$MThinkingTokens = 10000,
    [int]$LThinkingTokens = 10000,
    [switch]$NoAutoThinkingByTier,
    [switch]$NoSkipPermissions
)

$argsForClaude = @{
    MaxIterations = $MaxIterations
    LogDir = $LogDir
    Model = $Model
    Effort = $Effort
    ThinkingTokens = $ThinkingTokens
    XsThinkingTokens = $XsThinkingTokens
    SThinkingTokens = $SThinkingTokens
    MThinkingTokens = $MThinkingTokens
    LThinkingTokens = $LThinkingTokens
}

if ($NoAutoThinkingByTier) {
    $argsForClaude.NoAutoThinkingByTier = $true
}

if ($NoSkipPermissions) {
    $argsForClaude.NoSkipPermissions = $true
}

& "$PSScriptRoot\run-backlog-loop-claude.ps1" @argsForClaude
exit $LASTEXITCODE
