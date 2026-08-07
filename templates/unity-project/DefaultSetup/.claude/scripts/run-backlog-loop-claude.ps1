# Claude CLI wrapper for the shared autonomous backlog loop.
#
# Defaults: quality-first model-by-tier + thinking-by-tier, picked per iteration
# from the BACKLOG.md task tier (mirrors run-backlog-loop.sh --auto-model-by-tier):
#   XS -> sonnet / effort medium    S -> sonnet / effort high
#   M  -> opus   / effort high      L -> opus   / effort xhigh
# Dispatches to run-backlog-loop-core.ps1 with Provider = claude.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File .claude/scripts/run-backlog-loop-claude.ps1
#   # force a flat model (disable per-tier selection):
#   ... -NoAutoModelByTier -Model opus
#   # disable per-tier thinking:
#   ... -NoAutoThinkingByTier -ThinkingTokens 10000

[CmdletBinding()]
param(
    [int]$MaxIterations = 100,
    [string]$LogDir = "logs/backlog-loop",
    # Flat model override. Only used when -NoAutoModelByTier is set (otherwise the
    # per-tier model below governs). Empty -> core default (opus) for the flat path.
    [AllowEmptyString()]
    [string]$Model = "",
    [switch]$NoSkipPermissions,
    # Where the agent works: Current = this checkout (keeps both Unity gates,
    # dev must not edit concurrently); Worktree = sibling checkout on
    # agent/dev-<base> (dev undisturbed, but NO compile check and NO runtime
    # smoke - merge and run /compile-check afterwards).
    [ValidateSet("Current", "Worktree")]
    [string]$Mode = "Current",
    # Per-tier model + reasoning effort (quality-first). Enabled by default.
    [switch]$NoAutoModelByTier,
    [string]$XsModel = "sonnet",
    [string]$SModel  = "sonnet",
    [string]$MModel  = "opus",
    [string]$LModel  = "opus",
    [string]$XsEffort = "medium",
    [string]$SEffort  = "high",
    [string]$MEffort  = "high",
    [string]$LEffort  = "xhigh",
    # Extended thinking budget (output tokens reserved for reasoning) for the
    # orchestrator and the subagents it spawns. Pass 0 to disable thinking.
    [int]$ThinkingTokens = 10000,
    [int]$XsThinkingTokens = 3000,
    [int]$SThinkingTokens = 6000,
    [int]$MThinkingTokens = 10000,
    [int]$LThinkingTokens = 10000,
    [switch]$NoAutoThinkingByTier,
    # Flat reasoning effort override (used when -NoAutoModelByTier is set).
    # Empty = leave the CLI default untouched.
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

if (-not $NoAutoModelByTier) {
    $coreArgs.AutoModelByTier = $true
    $coreArgs.XsModel = $XsModel
    $coreArgs.SModel  = $SModel
    $coreArgs.MModel  = $MModel
    $coreArgs.LModel  = $LModel
    $coreArgs.XsEffort = $XsEffort
    $coreArgs.SEffort  = $SEffort
    $coreArgs.MEffort  = $MEffort
    $coreArgs.LEffort  = $LEffort
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
