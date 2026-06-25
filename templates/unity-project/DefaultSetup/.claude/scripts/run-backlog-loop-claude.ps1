# Claude CLI wrapper for the shared autonomous backlog loop.

[CmdletBinding()]
param(
    [int]$MaxIterations = 100,
    [string]$LogDir = "logs/backlog-loop",
    [AllowEmptyString()]
    [string]$Model = "claude-sonnet-4-6",
    [switch]$NoSkipPermissions,
    # Extended thinking budget (output tokens reserved for reasoning) for the
    # orchestrator and the subagents it spawns. Pass 0 to disable thinking.
    [int]$ThinkingTokens = 10000,
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

if ($NoSkipPermissions) {
    $coreArgs.NoSkipPermissions = $true
}

& "$PSScriptRoot\run-backlog-loop-core.ps1" @coreArgs
exit $LASTEXITCODE
