# Gemini CLI wrapper for the shared autonomous backlog loop.
#
# Defaults: gemini-3.1-pro-preview + thinking-by-tier (XS/S/M/L budgets picked from
# the BACKLOG.md task tier, exported as GEMINI_THINKING_BUDGET). Runs headless with
# --yolo; use -NoSkipPermissions to omit --yolo.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File .claude/scripts/run-backlog-loop-gemini.ps1
#   ... -Model gemini-3.1-pro-preview -MaxIterations 5
#   ... -NoAutoThinkingByTier -ThinkingTokens 10000

[CmdletBinding()]
param(
    [int]$MaxIterations = 100,
    [string]$LogDir = "logs/backlog-loop",
    [AllowEmptyString()]
    [string]$Model = "gemini-3.1-pro-preview",
    [switch]$NoSkipPermissions,
    # Where the agent works: Current = this checkout (keeps both Unity gates,
    # dev must not edit concurrently); Worktree = sibling checkout on
    # agent/dev-<base> (dev undisturbed, but NO compile check and NO runtime
    # smoke - merge and run /compile-check afterwards).
    [ValidateSet("Current", "Worktree")]
    [string]$Mode = "Current",
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

$coreArgs.Mode = $Mode

& "$PSScriptRoot\run-backlog-loop-core.ps1" @coreArgs
exit $LASTEXITCODE
