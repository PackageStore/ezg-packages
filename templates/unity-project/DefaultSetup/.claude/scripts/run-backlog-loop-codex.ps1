# Codex CLI wrapper for the shared autonomous backlog loop.
#
# Default behavior mirrors the Claude wrapper by running headless with automatic
# approvals/sandbox bypass. Use -NoSkipPermissions to run with workspace-write
# sandboxing and approval policy never. Codex reasoning is an effort tier, not a
# token budget, so this wrapper defaults ReasoningEffort = high.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File .claude/scripts/run-backlog-loop-codex.ps1
#   ... -ReasoningEffort medium -MaxIterations 5

[CmdletBinding()]
param(
    [int]$MaxIterations = 100,
    [string]$LogDir = "logs/backlog-loop",
    [AllowEmptyString()]
    [string]$Model = "",
    [switch]$NoSkipPermissions,
    # Where the agent works: Current = this checkout (keeps both Unity gates,
    # dev must not edit concurrently); Worktree = sibling checkout on
    # agent/dev-<base> (dev undisturbed, but NO compile check and NO runtime
    # smoke - merge and run /compile-check afterwards).
    [ValidateSet("Current", "Worktree")]
    [string]$Mode = "Current",
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

$coreArgs.Mode = $Mode

& "$PSScriptRoot\run-backlog-loop-core.ps1" @coreArgs
exit $LASTEXITCODE
