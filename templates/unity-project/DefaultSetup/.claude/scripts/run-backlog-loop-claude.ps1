# Claude CLI wrapper for the shared autonomous backlog loop.
#
# Defaults: quality-first model-by-tier + thinking-by-tier, picked per iteration
# from the BACKLOG.md task tier (mirrors run-backlog-loop.sh --auto-model-by-tier):
#   XS -> opus  / effort medium    S -> opus  / effort high
#   M  -> opus  / effort high      L -> opus  / effort xhigh
# Dispatches to run-backlog-loop-core.ps1 with Provider = claude.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File .claude/scripts/run-backlog-loop-claude.ps1
#   # force a flat model (disable per-tier selection):
#   ... -NoAutoModelByTier -Model opus
#   # disable per-tier thinking:
#   ... -NoAutoThinkingByTier -ThinkingTokens 10000
#   # self-heal is ON by default (blocked/failed task -> recovery iteration, then
#   # park + continue); stop on the first block instead:
#   ... -NoSelfHeal

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
    [string]$XsModel = "opus",
    [string]$SModel  = "opus",
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
    [string]$Effort = "",
    # Per-task watchdog passthrough (core defaults: 15 min inactivity, 3 h hard cap).
    # Raise the hard cap for tasks that run long unattended Editor sessions
    # (e.g. a long data-collection or validation run). 0 = keep the core default.
    [int]$TaskInactivityTimeoutSec = 0,
    [int]$TaskHardTimeoutSec = 0,
    # Self-heal passthrough (core defaults: ON, 3 recovery iterations per task,
    # then park it and go on; usage limits are waited out up to 600 min).
    # -NoSelfHeal = stop on the first block, as before. 0 = keep the core default.
    [switch]$NoSelfHeal,
    [int]$MaxRecoveries = 0,
    [int]$MaxUsageWaitMinutes = 0
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
if ($TaskInactivityTimeoutSec -gt 0) { $coreArgs.TaskInactivityTimeoutSec = $TaskInactivityTimeoutSec }
if ($TaskHardTimeoutSec -gt 0) { $coreArgs.TaskHardTimeoutSec = $TaskHardTimeoutSec }
if ($NoSelfHeal) { $coreArgs.NoSelfHeal = $true }
if ($MaxRecoveries -gt 0) { $coreArgs.MaxRecoveries = $MaxRecoveries }
if ($MaxUsageWaitMinutes -gt 0) { $coreArgs.MaxUsageWaitMinutes = $MaxUsageWaitMinutes }

& "$PSScriptRoot\run-backlog-loop-core.ps1" @coreArgs
exit $LASTEXITCODE
