# Backward-compatible alias for the Claude backlog loop.
#
# Existing usage still works:
#   powershell -ExecutionPolicy Bypass -File .agents/scripts/run-backlog-loop.ps1

[CmdletBinding()]
param(
    [int]$MaxIterations = 100,
    [string]$LogDir = "logs/backlog-loop",
    [AllowEmptyString()]
    [string]$Model = "claude-sonnet-4-6",
    [switch]$NoSkipPermissions
)

$argsForClaude = @{
    MaxIterations = $MaxIterations
    LogDir = $LogDir
    Model = $Model
}

if ($NoSkipPermissions) {
    $argsForClaude.NoSkipPermissions = $true
}

& "$PSScriptRoot\run-backlog-loop-claude.ps1" @argsForClaude
exit $LASTEXITCODE
