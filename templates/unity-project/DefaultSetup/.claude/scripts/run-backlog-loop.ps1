# Non-interactive default entrypoint for the autonomous backlog loop (Claude, headless).
#
# Existing usage still works:
#   powershell -ExecutionPolicy Bypass -File .claude/scripts/run-backlog-loop.ps1
#
# For Codex/Gemini or an interactive provider menu, use run-backlog-loop.bat.

[CmdletBinding()]
param(
    [int]$MaxIterations = 100,
    [string]$LogDir = "logs/backlog-loop",
    [AllowEmptyString()]
    [string]$Model = "opus",
    [switch]$NoSkipPermissions,
    # Where the agent works: Current = this checkout (keeps both Unity gates,
    # dev must not edit concurrently); Worktree = sibling checkout on
    # agent/dev-<base> (dev undisturbed, but NO compile check and NO runtime
    # smoke - merge and run /compile-check afterwards).
    [ValidateSet("Current", "Worktree")]
    [string]$Mode = "Current"
)

$coreArgs = @{
    Provider = "claude"
    MaxIterations = $MaxIterations
    LogDir = $LogDir
    Model = $Model
}

if ($NoSkipPermissions) {
    $coreArgs.NoSkipPermissions = $true
}

$coreArgs.Mode = $Mode

& "$PSScriptRoot\run-backlog-loop-core.ps1" @coreArgs
exit $LASTEXITCODE
