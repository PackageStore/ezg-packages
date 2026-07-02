# Claude CLI wrapper for the shared autonomous backlog loop.

[CmdletBinding()]
param(
    [int]$MaxIterations = 100,
    [string]$LogDir = "logs/backlog-loop",
    [AllowEmptyString()]
    [string]$Model = "sonnet",
    [switch]$NoSkipPermissions,
    # Extended thinking budget (output tokens reserved for reasoning) for the
    # orchestrator and the subagents it spawns. Pass 0 to disable thinking.
    [int]$ThinkingTokens = 10000,
    [int]$XsThinkingTokens = 3000,
    [int]$SThinkingTokens = 6000,
    [int]$MThinkingTokens = 10000,
    [int]$LThinkingTokens = 10000,
    [switch]$NoAutoThinkingByTier,
    # Claude reasoning effort tier. Empty = leave the CLI default untouched.
    [AllowEmptyString()]
    [string]$Effort = ""
)

$coreArgs = @{
    Provider = "claude"
    MaxIterations = $MaxIterations
    LogDir = $LogDir
    Model = $Model
    ThinkingTokens = $ThinkingTokens
    ReasoningEffort = $Effort
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
