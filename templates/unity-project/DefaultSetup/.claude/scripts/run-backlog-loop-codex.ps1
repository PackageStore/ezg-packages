# Codex CLI wrapper for the shared autonomous backlog loop.
#
# Default behavior mirrors the Claude wrapper by running headless with automatic
# approvals/sandbox bypass. Use -NoSkipPermissions to run with workspace-write
# sandboxing and approval policy never.

[CmdletBinding()]
param(
    [int]$MaxIterations = 100,
    [string]$LogDir = "logs/backlog-loop",
    [AllowEmptyString()]
    [string]$Model = "",
    [switch]$NoSkipPermissions,
    # Codex reasoning effort tier for the orchestrator. Empty = leave the CLI/model
    # default untouched. Allowed: minimal | low | medium | high.
    [ValidateSet("", "minimal", "low", "medium", "high")]
    [string]$ReasoningEffort = "high"
)

$coreArgs = @{
    Provider = "codex"
    MaxIterations = $MaxIterations
    LogDir = $LogDir
    Model = $Model
    ReasoningEffort = $ReasoningEffort
}

if ($NoSkipPermissions) {
    $coreArgs.NoSkipPermissions = $true
}

& "$PSScriptRoot\run-backlog-loop-core.ps1" @coreArgs
exit $LASTEXITCODE
