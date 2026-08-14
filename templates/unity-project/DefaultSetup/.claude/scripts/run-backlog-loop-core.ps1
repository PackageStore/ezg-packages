# Shared autonomous backlog loop engine for Claude, Codex, and Gemini CLI.
#
# This file owns the loop behavior:
#   - inspect BACKLOG.md
#   - run one headless agent iteration
#   - write per-iteration logs
#   - stop on empty backlog, non-zero CLI exit, blocker sentinels, or MaxIterations
#
# Entry points:
#   run-backlog-loop.bat         -> interactive provider menu (Claude / Codex / Gemini)
#   run-backlog-loop.ps1         -> non-interactive default (Claude, headless)
#   run-backlog-loop-claude.ps1  -> Claude wrapper (quality-first model+thinking by tier)
#   run-backlog-loop-codex.ps1   -> Codex wrapper  (reasoning effort = high)
#   run-backlog-loop-gemini.ps1  -> Gemini wrapper (default model + thinking-by-tier)
#   direct:  run-backlog-loop-core.ps1 -Provider claude|codex|gemini

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("claude", "codex", "gemini")]
    [string]$Provider,

    [int]$MaxIterations = 100,

    # Per-task watchdog (Windows-runner parity with run-backlog-loop.sh): kill the
    # agent process when its iteration log stops growing (hang / token exhaustion)
    # or the task overruns the hard cap, so a stuck task stops consuming tokens.
    [int]$TaskInactivityTimeoutSec = 900,     # 15 min with no new log output
    [int]$TaskHardTimeoutSec = 10800,         # 3 hours total per task

    # Consecutive transient API blips (transport break / 529) tolerated before the
    # loop gives up. Fatal classes (auth, exhausted usage) ignore this and stop at
    # once - see Get-ClaudeFailureClass.
    [int]$MaxTransientApiRetries = 3,

    [string]$LogDir = "logs/backlog-loop",
    [AllowEmptyString()]
    [string]$Model = "",
    [switch]$NoSkipPermissions,
    # Reasoning/thinking budget (output tokens reserved for reasoning).
    #   claude  -> exported as MAX_THINKING_TOKENS
    #   gemini  -> exported as GEMINI_THINKING_BUDGET
    # 0 disables thinking for those providers. Ignored by codex (which uses an
    # effort tier, see -ReasoningEffort).
    [int]$ThinkingTokens = 0,
    # Codex reasoning effort tier. Empty = leave the CLI/model default untouched.
    # Ignored by claude/gemini (they use -ThinkingTokens).
    [ValidateSet("", "minimal", "low", "medium", "high")]
    [string]$ReasoningEffort = "",

    # Pick the thinking budget per iteration from the BACKLOG.md task tier.
    [switch]$AutoThinkingByTier,
    [int]$XsThinkingTokens = 3000,
    [int]$SThinkingTokens = 6000,
    [int]$MThinkingTokens = 10000,
    [int]$LThinkingTokens = 10000,

    # Pick the model + reasoning effort per iteration from the BACKLOG.md task
    # tier (quality-first, mirrors run-backlog-loop.sh --auto-model-by-tier).
    # claude only: cheaper model for small tiers, opus for M/L. Non-claude
    # providers keep their flat -Model (sonnet/opus are claude aliases).
    [switch]$AutoModelByTier,
    [string]$XsModel = "sonnet",
    [string]$SModel  = "sonnet",
    [string]$MModel  = "opus",
    [string]$LModel  = "opus",
    [string]$XsEffort = "medium",
    [string]$SEffort  = "high",
    [string]$MEffort  = "high",
    [string]$LEffort  = "xhigh",

    # Where the agent does its work.
    #   Current  - implement in THIS checkout, commit onto the branch already
    #              checked out. One Unity Editor, so the compile-check and the
    #              runtime-smoke gates keep working. The dev must not edit files
    #              while the loop runs (the agent stages with `git add -A`).
    #   Worktree - implement in a sibling `git worktree` on agent/dev-<base>, so
    #              the dev keeps working undisturbed. Costs BOTH Unity gates: the
    #              worktree is a separate Unity project with no .sln/.csproj (both
    #              gitignored, Unity-generated) and no Editor attached, so the
    #              agent cannot compile or play-test what it wrote. Merge and run
    #              /compile-check yourself afterwards.
    [ValidateSet("Current", "Worktree")]
    [string]$Mode = "Current"
)

$ErrorActionPreference = "Continue"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$RepoRoot = Split-Path -Parent $RepoRoot
Set-Location $RepoRoot

# Project-specific values come from project-profile.json so this controller stays
# byte-identical across every project on this base (see project_profile.py). The
# -Fallback value is what a box without python3 gets, and it matches
# project_profile.DEFAULTS. Keep in lockstep with run-backlog-loop.sh.
function Get-ProfileValue {
    param([string]$Key, [string]$Fallback)
    try {
        $v = (& python3 (Join-Path $PSScriptRoot "project_profile.py") $Key 2>$null)
        if ($LASTEXITCODE -eq 0 -and $v) { return ([string]$v).Trim() }
    } catch { }
    return $Fallback
}
$GitCfgBaseBranch   = (Get-ProfileValue "gitConfigPrefix"  "agent") + ".agentBaseBranch"
$ProfileDefaultBase = Get-ProfileValue "defaultBaseBranch" "main"

# The backlog lives in the git COMMON dir (.git/backlog/), never in the tree: it
# is per-developer bookkeeping, so tracking it made every dev branch carry its
# own index and collide on merge. --git-common-dir (not --git-dir) is what makes
# one queue visible from every linked worktree of the clone. Git returns it
# relative to the cwd on some versions, absolute on others - resolve both.
$gitCommonDir = [string](& git rev-parse --git-common-dir 2>$null)
$gitCommonDir = $gitCommonDir.Trim()
if (-not $gitCommonDir) { $gitCommonDir = ".git" }
$gitCommonDir = (Resolve-Path -LiteralPath (Join-Path $RepoRoot $gitCommonDir) -ErrorAction SilentlyContinue).Path
if (-not $gitCommonDir) { $gitCommonDir = Join-Path $RepoRoot ".git" }
$BacklogRoot = Join-Path $gitCommonDir "backlog"
$BacklogIndex = Join-Path $BacklogRoot "BACKLOG.md"
$env:AGENT_BACKLOG_ROOT = $BacklogRoot

# Capture the base once before iteration 1 can checkout the agent branch. Child
# agent processes inherit this environment variable through Start-Process.
$LoopBaseBranch = [string](& git rev-parse --abbrev-ref HEAD 2>$null)
$LoopBaseBranch = $LoopBaseBranch.Trim()
# Normally the base = whichever non-agent branch is checked out at start.
# But a previous loop run leaves HEAD on the agent branch, so re-running the
# .bat from there used to hard-fail (BASE_UNKNOWN) and the window vanished.
# Instead of blocking, resolve the base from the recorded git config, then the
# repo default, so the loop starts fine from an agent branch and still re-syncs
# against the real base branch (never merging the agent branch into itself).
if (-not $LoopBaseBranch -or $LoopBaseBranch -eq "HEAD" -or $LoopBaseBranch -like "agent/dev-*" -or $LoopBaseBranch -eq "agent/dev") {
    $cfgBase = [string](& git config $GitCfgBaseBranch 2>$null)
    $cfgBase = $cfgBase.Trim()
    if ($cfgBase -and $cfgBase -notlike "agent/dev*") {
        $LoopBaseBranch = $cfgBase
    } else {
        $LoopBaseBranch = $ProfileDefaultBase
    }
    Write-Host "HEAD is an agent branch or detached - base branch resolved to '$LoopBaseBranch' (git config / repo default)." -ForegroundColor Yellow
}
$env:AGENT_BASE_BRANCH = $LoopBaseBranch

# Work branch.
#   Current  - commit straight onto the branch already checked out. A separate
#              agent branch would buy nothing here (same directory either way)
#              and every checkout makes the dev's open Unity Editor reimport.
#   Worktree - a dedicated agent/dev-<base> branch is MANDATORY, not a
#              convention: git refuses to check out one branch in two worktrees.
# The slashes in the base MUST be flattened: git stores refs as files, so
# `Dev1` and `Dev1/agent/dev` cannot coexist ("cannot lock ref ...
# 'refs/heads/Dev1' exists"). Flattening also gives each developer their own
# agent branch on a shared remote.
if ($Mode -eq "Worktree") {
    $AgentBranch = "agent/dev-" + ($LoopBaseBranch -replace "/", "-")
} else {
    $AgentBranch = $LoopBaseBranch
}
$env:AGENT_BRANCH = $AgentBranch
$env:AGENT_MODE = $Mode.ToLowerInvariant()
Write-Host "Base branch: $LoopBaseBranch (captured at loop start)" -ForegroundColor Cyan
Write-Host "Mode:        $Mode (work branch: $AgentBranch)" -ForegroundColor Cyan
Write-Host "Backlog:     $BacklogRoot" -ForegroundColor DarkGray

# --- Worktree mode: create (or reuse) the sibling checkout -------------------
# Created ONCE and kept: a worktree is a full second Unity project, so tearing it
# down each run would re-import Library/ from scratch every time.
$WorkDir = $RepoRoot
if ($Mode -eq "Worktree") {
    $wtPath = Join-Path (Split-Path -Parent $RepoRoot) ("{0}-agent-{1}" -f (Split-Path -Leaf $RepoRoot), ($LoopBaseBranch -replace "[/\\]", "-"))
    $wtList = (& git worktree list --porcelain 2>$null) -join "`n"
    if (Test-Path -LiteralPath $wtPath) {
        Write-Host "Worktree:    reusing $wtPath" -ForegroundColor DarkGray
    } else {
        $branchExists = [bool](& git rev-parse --verify --quiet "refs/heads/$AgentBranch" 2>$null)
        if ($branchExists) {
            & git worktree add $wtPath $AgentBranch 2>&1 | Write-Host
        } else {
            & git worktree add -b $AgentBranch $wtPath $LoopBaseBranch 2>&1 | Write-Host
        }
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $wtPath)) {
            Write-Host "WORKTREE_FAILED - could not create $wtPath. Re-run with -Mode Current, or remove a stale entry with 'git worktree prune'." -ForegroundColor Red
            exit 1
        }
        Write-Host "Worktree:    created $wtPath on $AgentBranch" -ForegroundColor Green
    }
    $WorkDir = (Resolve-Path -LiteralPath $wtPath).Path
    Write-Host "WARNING: worktree mode has NO compile check and NO runtime smoke - the" -ForegroundColor Yellow
    Write-Host "         worktree is a separate Unity project with no .sln/.csproj and no" -ForegroundColor Yellow
    Write-Host "         Editor. Merge $AgentBranch into $LoopBaseBranch and run /compile-check" -ForegroundColor Yellow
    Write-Host "         BEFORE anything else." -ForegroundColor Yellow
}
$env:AGENT_WORKDIR = $WorkDir

# Current mode shares the checkout with the developer, and the agent stages with
# `git add -A` - so anything uncommitted at this moment lands in the first task's
# commit. Warn, do not block: owning that risk is the whole point of choosing
# Current over Worktree.
if ($Mode -eq "Current") {
    $dirty = @(& git status --porcelain 2>$null | Where-Object { $_ })
    if ($dirty.Count -gt 0) {
        Write-Host "WARNING: $($dirty.Count) uncommitted change(s) in this checkout. The agent stages" -ForegroundColor Yellow
        Write-Host "         with 'git add -A', so they will be swept into the first task's commit." -ForegroundColor Yellow
        Write-Host "         Commit or stash them first if that is not what you want." -ForegroundColor Yellow
    }
}

# Per-provider default model when -Model is not supplied
# (this default previously lived in the per-provider wrapper scripts).
if (-not $Model) {
    switch ($Provider) {
        "claude" { $Model = "opus" }
        "gemini" { $Model = "gemini-3.1-pro-preview" }
        # codex: leave empty -> Codex CLI uses its own default model
    }
}

if (-not (Test-Path $LogDir)) {
    New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
}

$startTime = Get-Date
$timestamp = $startTime.ToString("yyyyMMdd-HHmmss")
$summaryLog = Join-Path $LogDir "loop-$Provider-$timestamp.summary.log"

function Write-Log {
    param([string]$Message, [string]$Color = "White")
    $line = "[$(Get-Date -Format 'HH:mm:ss')] $Message"
    Write-Host $line -ForegroundColor $Color
    Add-Content -Path $summaryLog -Value $line -Encoding utf8
}

function ConvertTo-CmdArgument {
    param([AllowEmptyString()][string]$Value)

    if ($null -eq $Value -or $Value.Length -eq 0) {
        return '""'
    }

    if ($Value -notmatch '[\s"&|<>^]') {
        return $Value
    }

    return '"' + ($Value -replace '"', '\"') + '"'
}

function Join-CmdLine {
    param([string]$Command, [string[]]$Arguments)
    $parts = @($Command) + $Arguments
    return (($parts | ForEach-Object { ConvertTo-CmdArgument $_ }) -join " ")
}

function Get-BacklogStatus {
    $result = @{ TodoCount = 0; InProgressCount = 0 }
    if (-not (Test-Path -LiteralPath $BacklogIndex)) { return $result }

    $content = Get-Content -LiteralPath $BacklogIndex -Raw

    if ($content -match '(?ms)^## TODO\s*\r?\n(.*?)(?=^## )') {
        $section = $matches[1]
        $matches2 = [regex]::Matches($section, '^\s*-\s*\[(HIGH|MEDIUM|LOW)\]', 'Multiline')
        $result.TodoCount = $matches2.Count
    }

    if ($content -match '(?ms)^## IN PROGRESS\s*\r?\n(.*?)(?=^## )') {
        $section = $matches[1]
        $lines = $section -split "`r?`n" | Where-Object { $_ -match '^\s*-\s*\[' }
        $result.InProgressCount = ($lines | Measure-Object).Count
    }

    return $result
}

# Resolve the next task's tier/title/state (IN PROGRESS first, then TODO). Mirrors the
# bash loop's next_task_profile - used to pick a per-tier thinking budget.
function Get-NextBacklogTaskProfile {
    $result = @{ Tier = ""; Title = ""; State = "" }
    if (-not (Test-Path -LiteralPath $BacklogIndex)) { return $result }

    $content = Get-Content -LiteralPath $BacklogIndex -Raw
    $sections = @(
        @{ Name = "IN PROGRESS"; State = "in-progress" },
        @{ Name = "TODO"; State = "todo" }
    )

    foreach ($entry in $sections) {
        $sectionName = [regex]::Escape($entry.Name)
        if ($content -notmatch "(?ms)^## $sectionName\s*\r?\n(.*?)(?=^## )") {
            continue
        }

        $section = $matches[1]
        foreach ($line in ($section -split "`r?`n")) {
            if ($line -match '^\s*-\s*\[(HIGH|MEDIUM|LOW)\]\s+(?:\[(XS|S|M|L)\]\s+)?\[([^\]]+)\]') {
                $result.Tier = [string]$matches[2]
                $result.Title = [string]$matches[3]
                $result.State = [string]$entry.State
                return $result
            }
        }
    }

    return $result
}

function Get-ThinkingBudgetForTier {
    param([AllowEmptyString()][string]$Tier)

    switch ($Tier) {
        "XS" { return $XsThinkingTokens }
        "S"  { return $SThinkingTokens }
        "M"  { return $MThinkingTokens }
        "L"  { return $LThinkingTokens }
        default { return $MThinkingTokens }
    }
}

function Get-ModelForTier {
    param([string]$ProviderName, [AllowEmptyString()][string]$Tier)

    # sonnet/opus are claude aliases; only tier-switch the model for claude.
    # Other providers keep whatever flat -Model resolved to.
    if ($ProviderName -ne "claude") { return $Model }

    switch ($Tier) {
        "XS" { return $XsModel }
        "S"  { return $SModel }
        "M"  { return $MModel }
        "L"  { return $LModel }
        default { return $MModel }
    }
}

function Get-EffortForTier {
    param([string]$ProviderName, [AllowEmptyString()][string]$Tier)

    # Per-tier reasoning effort is calibrated for claude (--effort supports xhigh).
    # For non-claude providers fall back to the flat -ReasoningEffort.
    if ($ProviderName -ne "claude") { return $ReasoningEffort }

    switch ($Tier) {
        "XS" { return $XsEffort }
        "S"  { return $SEffort }
        "M"  { return $MEffort }
        "L"  { return $LEffort }
        default { return $MEffort }
    }
}

function Test-Blocked {
    param([string]$LogPath)
    if (-not (Test-Path $LogPath)) { return $false }
    $lines = Get-Content $LogPath -ErrorAction SilentlyContinue
    if (-not $lines) { return $false }
    # Only check the final {"type":"result"} event's result field.
    # Scanning all lines causes false positives when the injected prompt
    # (which lists block tokens as examples) appears in the conversation JSON.
    foreach ($line in $lines) {
        if (-not $line.TrimStart().StartsWith('{')) { continue }
        try { $obj = $line | ConvertFrom-Json -ErrorAction Stop } catch { continue }
        if ($obj.type -ne 'result') { continue }
        $resultText = [string]$obj.result
        # Token list mirrors the "Hard stop conditions" of run-backlog/SKILL.md -
        # keep in lockstep with is_blocked() in run-backlog-loop.sh.
        if ($resultText -match '\b(COMPILE_BLOCKED|PREFLIGHT_BLOCKED|REVIEW_BLOCKED|VERIFY_BLOCKED|RUNTIME_BLOCKED|EDITOR_REQUIRED|NO_CHANGES|BASE_UNKNOWN|BASE_MERGE_CONFLICT)\b') { return $true }
        if ($resultText -match 'manual intervention required') { return $true }
    }
    return $false
}

# Classify a non-zero claude iteration as transient (worth retrying) or fatal (stop
# now). Grounded in the failure classes actually seen in logs/backlog-loop/:
#
#   terminal_reason=api_error  "Connection closed mid-response"   transport break -> retry
#   result="API Error: Overloaded"                                529             -> retry
#   result="Failed to authenticate. API Error: 401 ..."                           -> STOP
#   result="You're out of extra usage - resets <time>"                            -> STOP
#
# Only the first two are worth another attempt. Retrying an exhausted quota burns
# what is left against a wall, and retrying bad credentials cannot fix them - both
# must keep the original break behaviour. Keep in lockstep with
# claude_failure_class() in run-backlog-loop.sh.
function Get-ClaudeFailureClass {
    param([string]$LogPath)

    $verdict = @{ Transient = $false; Reason = "unclassified non-zero exit" }
    $logContent = Get-Content $LogPath -Raw -ErrorAction SilentlyContinue
    if (-not $logContent) { return $verdict }

    # Last {"type":"result"} event only - the injected prompt echoes error-ish words
    # into the conversation JSON, so a whole-log match would misclassify.
    $resultObj = $null
    foreach ($line in ($logContent -split "`r?`n")) {
        if (-not $line.TrimStart().StartsWith('{')) { continue }
        try { $obj = $line | ConvertFrom-Json -ErrorAction Stop } catch { continue }
        if ($obj.type -eq 'result') { $resultObj = $obj }
    }
    if (-not $resultObj) { return $verdict }

    $resultText = [string]$resultObj.result

    # Fatal classes first: a quota/auth failure may still carry an api_error
    # terminal_reason, and must never be read as a retryable blip.
    if ($resultText -match "out of extra usage|usage limit|credit balance|Insufficient credit") {
        $verdict.Reason = "usage/credit exhausted - retrying would burn quota against a wall"
        return $verdict
    }
    if ($resultText -match "Invalid authentication credentials|Failed to authenticate") {
        $verdict.Reason = "authentication failure - a retry cannot fix credentials"
        return $verdict
    }

    if ($resultObj.terminal_reason -eq 'api_error') {
        $verdict.Transient = $true
        $verdict.Reason = "API/transport error"
        if ($resultText) {
            $snippet = $resultText.Substring(0, [Math]::Min(120, $resultText.Length))
            $verdict.Reason = "API/transport error ($snippet)"
        }
        return $verdict
    }
    if ($resultText -match "Overloaded|overloaded_error") {
        $verdict.Transient = $true
        $verdict.Reason = "API overloaded (529)"
        return $verdict
    }

    return $verdict
}

# Resolve the current task's title + file URL + tier + bullet path for
# notifications and the deterministic outcome/receipt checks (mirrors the bash loop).
function Get-NotifyTaskInfo {
    $result = @{ Title = "Unknown Task"; Url = ""; Tier = ""; RelPath = "" }
    if (-not (Test-Path -LiteralPath $BacklogIndex)) { return $result }
    $content = Get-Content -LiteralPath $BacklogIndex -Raw
    foreach ($name in @("IN PROGRESS", "TODO")) {
        $escaped = [regex]::Escape($name)
        if ($content -notmatch "(?ms)^## $escaped\s*\r?\n(.*?)(?=^## )") { continue }
        $section = $matches[1]
        foreach ($line in ($section -split "`r?`n")) {
            if ($line -match '^\s*-\s*\[(HIGH|MEDIUM|LOW)\]\s+(?:\[(XS|S|M|L)\]\s+)?\[([^\]]+)\]') {
                $result.Title = [string]$matches[3]
                $result.Tier = [string]$matches[2]
                if ($line -match '\]\((backlog/[^)]+)\)') {
                    $result.Url = "file://$gitCommonDir/$($matches[1])"
                    $result.RelPath = [string]$matches[1]
                }
                return $result
            }
        }
    }
    return $result
}

# Classify which blocker fired from the iteration log (mirrors the bash loop).
# Reads ONLY the final {"type":"result"} event - the full log always contains
# every token name (the injected prompt echoes them in the conversation JSON),
# so a whole-log match would mislabel the event as the first token checked.
function Get-BlockClassification {
    param([string]$LogPath)
    $result = @{ Event = "VERIFY_BLOCKED"; Details = "Manual intervention required." }
    if (-not (Test-Path $LogPath)) { return $result }
    $resultText = ""
    foreach ($line in (Get-Content $LogPath -ErrorAction SilentlyContinue)) {
        if (-not $line.TrimStart().StartsWith('{')) { continue }
        try { $obj = $line | ConvertFrom-Json -ErrorAction Stop } catch { continue }
        if ($obj.type -eq 'result') { $resultText = [string]$obj.result }
    }
    if (-not $resultText) { return $result }
    # Token order in lockstep with the .sh classification loop.
    foreach ($token in @("EDITOR_REQUIRED", "COMPILE_BLOCKED", "PREFLIGHT_BLOCKED", "REVIEW_BLOCKED", "RUNTIME_BLOCKED", "VERIFY_BLOCKED", "NO_CHANGES", "BASE_MERGE_CONFLICT", "BASE_UNKNOWN")) {
        if ($resultText -match $token) {
            $result.Event = $token
            $m = [regex]::Match($resultText, "$token.*")
            if ($m.Success) { $result.Details = $m.Value }
            return $result
        }
    }
    $m = [regex]::Match($resultText, "(?i)manual intervention.*")
    if ($m.Success) { $result.Details = $m.Value } else { $result.Details = "Automation paused. Manual intervention required." }
    return $result
}

# Compact "1.2M" / "3.4K" token formatter, shared by the token report, the per-tool
# breakdown and the loop-wide running total.
function Format-TokenCount {
    param([double]$Value)
    if ($Value -ge 1000000) { return "$([Math]::Round($Value / 100000) / 10)M" }
    if ($Value -ge 1000) { return "$([Math]::Round($Value / 100) / 10)K" }
    return "$([Math]::Round($Value))"
}

# "12.34" - always 2 decimals so costs line up across notifications.
function Format-CostUsd {
    param([double]$Value)
    return ([Math]::Round($Value, 2)).ToString('0.00', [System.Globalization.CultureInfo]::InvariantCulture)
}

# "claude-opus-4-8" -> "opus-4-8". Discord fields are narrow and the vendor prefix
# carries no information once every row is a claude model.
function Format-ModelName {
    param([string]$Name)
    if (-not $Name) { return "model" }
    if ($Name.StartsWith("claude-")) { return $Name.Substring(7) }
    return $Name
}

# Token + cost report for one iteration, from a claude stream-json log (bash uses jq).
#
# Source of truth is the CLI's own "result" line(s): they carry the authoritative
# aggregates - usage, num_turns, total_cost_usd and modelUsage (per model, subagent
# turns included). Summing the per-turn assistant snapshots instead undercounts output
# badly, because a streamed message only reaches its final output_tokens on the last
# snapshot; that path stays as a fallback for runs that died before emitting a result
# line, and it can report tokens but never cost.
#
# One iteration can emit SEVERAL result lines (the CLI closes a result when the main
# turn ends, then continues once a backgrounded task returns), and they mix two scopes:
#   - modelUsage / total_cost_usd -> session-cumulative, identical in every line
#     => take the last modelUsage, the max cost. Summing would double-count.
#   - usage / num_turns           -> per segment => sum across the lines.
#
# Returns @{ Summary; PerModel; TotalTokens; CostUsd } - Summary and PerModel are
# pre-formatted ASCII strings ready to hand to notify.ps1.
function Get-TokenReport {
    param([string]$LogFile)
    $report = [ordered]@{ Summary = ""; PerModel = ""; TotalTokens = 0.0; CostUsd = 0.0 }
    if (-not (Test-Path $LogFile)) { return $report }
    try {
        $resultObjs = @()
        $turnUsage = @{}
        foreach ($line in (Get-Content $LogFile -ErrorAction SilentlyContinue)) {
            # Cheap prefilter - only usage-bearing lines are worth a full JSON parse.
            if ($line -notmatch '"usage"') { continue }
            $obj = ConvertFrom-Json $line -ErrorAction SilentlyContinue
            if (-not $obj) { continue }
            if ($obj.type -eq "result") { $resultObjs += $obj; continue }
            if ($obj.type -eq "assistant" -and $obj.message -and $obj.message.id -and $obj.message.usage) {
                $cur = 0
                if ($obj.message.usage.output_tokens) { $cur = [int]$obj.message.usage.output_tokens }
                $prev = -1
                if ($turnUsage.ContainsKey($obj.message.id)) { $prev = [int]$turnUsage[$obj.message.id].output_tokens }
                if ($cur -ge $prev) { $turnUsage[$obj.message.id] = $obj.message.usage }
            }
        }

        $in = 0.0; $out = 0.0; $cacheWrite = 0.0; $cacheRead = 0.0; $cost = 0.0
        $turns = 0
        $modelRows = @()
        $partial = $false

        # Cumulative scope: last non-empty modelUsage, highest reported cost.
        $modelProps = @()
        $maxCost = 0.0
        foreach ($r in $resultObjs) {
            if ($r.modelUsage) {
                $props = @($r.modelUsage.PSObject.Properties)
                if ($props.Count -gt 0) { $modelProps = $props }
            }
            if ($r.total_cost_usd -and [double]$r.total_cost_usd -gt $maxCost) { $maxCost = [double]$r.total_cost_usd }
        }

        if ($modelProps.Count -gt 0) {
            foreach ($p in $modelProps) {
                $m = $p.Value
                $mIn = [double]$m.inputTokens
                $mOut = [double]$m.outputTokens
                $mWrite = [double]$m.cacheCreationInputTokens
                $mRead = [double]$m.cacheReadInputTokens
                $mCost = [double]$m.costUSD
                $in += $mIn; $out += $mOut; $cacheWrite += $mWrite; $cacheRead += $mRead; $cost += $mCost
                $modelRows += [PSCustomObject]@{
                    Name   = (Format-ModelName $p.Name)
                    Tokens = ($mIn + $mOut + $mWrite + $mRead)
                    Cost   = $mCost
                }
            }
        } elseif ($resultObjs.Count -gt 0) {
            # Per-segment scope: sum the usage blocks, keep the cumulative cost as-is.
            foreach ($r in $resultObjs) {
                $u = $r.usage
                if (-not $u) { continue }
                $in += [double]$u.input_tokens
                if ($u.output_tokens) { $out += [double]$u.output_tokens }
                if ($u.cache_creation_input_tokens) { $cacheWrite += [double]$u.cache_creation_input_tokens }
                if ($u.cache_read_input_tokens) { $cacheRead += [double]$u.cache_read_input_tokens }
            }
            $cost = $maxCost
        } else {
            foreach ($u in $turnUsage.Values) {
                $in += [double]$u.input_tokens
                if ($u.output_tokens) { $out += [double]$u.output_tokens }
                if ($u.cache_creation_input_tokens) { $cacheWrite += [double]$u.cache_creation_input_tokens }
                if ($u.cache_read_input_tokens) { $cacheRead += [double]$u.cache_read_input_tokens }
            }
            $partial = $true
        }

        if ($resultObjs.Count -gt 0) {
            foreach ($r in $resultObjs) { if ($r.num_turns) { $turns += [int]$r.num_turns } }
        } else {
            $turns = $turnUsage.Count
        }

        $total = $in + $out + $cacheWrite + $cacheRead
        if ($total -le 0) { return $report }

        $summaryLines = @()
        $head = "$(Format-TokenCount $total) total"
        if ($cost -gt 0) { $head = $head + ' | ~$' + (Format-CostUsd $cost) }
        $summaryLines += $head
        $summaryLines += "In $(Format-TokenCount $in) | Out $(Format-TokenCount $out)"
        $summaryLines += "Cache W $(Format-TokenCount $cacheWrite) | R $(Format-TokenCount $cacheRead)"
        if ($turns -gt 0) { $summaryLines += "$turns turns" }
        # No result line means the run was cut off mid-flight (crash / kill), so the
        # numbers above are a floor, not the real total. Say so instead of implying
        # a clean measurement.
        if ($partial) { $summaryLines += "(partial: no result line)" }

        $report.Summary = ($summaryLines -join "`n")
        $report.TotalTokens = $total
        $report.CostUsd = $cost

        if ($modelRows.Count -gt 0) {
            # Discord collapses runs of spaces outside code fences, so this field uses
            # separators rather than column padding.
            $sorted = @($modelRows | Sort-Object -Property Cost -Descending)
            $top = @($sorted | Select-Object -First 4)
            $rest = @($sorted | Select-Object -Skip 4)
            $perModelLines = @()
            foreach ($r in $top) {
                $perModelLines += "$($r.Name): $(Format-TokenCount $r.Tokens)" + ' | ~$' + (Format-CostUsd $r.Cost)
            }
            if ($rest.Count -gt 0) {
                $restTok = 0.0; $restCost = 0.0
                foreach ($r in $rest) { $restTok += $r.Tokens; $restCost += $r.Cost }
                $perModelLines += "+$($rest.Count) more: $(Format-TokenCount $restTok)" + ' | ~$' + (Format-CostUsd $restCost)
            }
            $report.PerModel = ($perModelLines -join "`n")
        }

        return $report
    } catch {
        return $report
    }
}

# Categorize a tool name the same way the live console renderer does (Normalize-ToolLabel
# inside the child-process script template) - kept as a separate copy here since that one
# lives in an isolated script scope launched via Start-Process.
function Get-ToolCategory {
    param([string]$ToolName)
    if ([string]::IsNullOrEmpty($ToolName)) { return "(other)" }
    if ($ToolName -in @("Bash", "PowerShell", "run_shell_command")) { return "exec" }
    if ($ToolName.StartsWith("mcp__")) {
        $parts = $ToolName -split "__"
        if ($parts.Count -ge 3) { return $parts[2] }
        return $ToolName.Substring(5)
    }
    return $ToolName
}

# Approximate per-tool time + token breakdown from a claude stream-json iteration log, for
# the Discord "Time & Token Breakdown" embed field. Two approximations, both unavoidable
# given what stream-json actually timestamps:
#   - Time: stream-json only puts a "timestamp" on tool_result ("user" role) lines, not on
#     the tool_use call itself. The gap between consecutive tool_result timestamps is
#     attributed to whichever tool(s) were in flight during that gap (same heuristic as a
#     manual post-hoc transcript read: "what was running just before this gap").
#   - Tokens: usage is reported per assistant turn, not per individual tool call. A turn's
#     incremental usage is split evenly across the tool_use block(s) issued in that turn
#     (almost always 1; only differs for parallel tool calls in the same turn).
function Get-TimingTokenBreakdown {
    param([string]$LogFile)
    if (-not (Test-Path $LogFile)) { return "" }
    try {
        $lines = Get-Content $LogFile -ErrorAction SilentlyContinue
        if (-not $lines) { return "" }

        $toolIdToCategory = @{}
        $msgUsage = @{}
        $msgCategories = @{}
        $timeEvents = @()

        foreach ($line in $lines) {
            if (-not $line.Trim()) { continue }
            $obj = ConvertFrom-Json $line -ErrorAction SilentlyContinue
            if (-not $obj) { continue }

            if ($obj.type -eq "assistant" -and $obj.message -and $obj.message.id) {
                $msg = $obj.message
                $cats = @()
                if ($msg.content) {
                    foreach ($block in $msg.content) {
                        if ($block.type -eq "tool_use" -and $block.name) {
                            $cat = Get-ToolCategory $block.name
                            $cats += $cat
                            if ($block.id) { $toolIdToCategory[$block.id] = $cat }
                        }
                    }
                }
                # Streaming re-emits growing snapshots of the same message id; keep
                # overwriting with the latest non-empty set so a later, fuller snapshot
                # (e.g. a second tool_use block added mid-stream) isn't missed.
                if ($cats.Count -gt 0) { $msgCategories[$msg.id] = $cats }
                if ($msg.usage) {
                    $outTok = 0
                    if ($msg.usage.output_tokens) { $outTok = [int]$msg.usage.output_tokens }
                    $prevOut = -1
                    if ($msgUsage.ContainsKey($msg.id)) { $prevOut = [int]$msgUsage[$msg.id].output_tokens }
                    if ($outTok -ge $prevOut) { $msgUsage[$msg.id] = $msg.usage }
                }
                continue
            }

            if ($obj.type -eq "user" -and $obj.timestamp -and $obj.message -and $obj.message.content) {
                $cats = @()
                foreach ($c in $obj.message.content) {
                    if ($c.type -eq "tool_result" -and $c.tool_use_id -and $toolIdToCategory.ContainsKey($c.tool_use_id)) {
                        $cats += $toolIdToCategory[$c.tool_use_id]
                    }
                }
                if ($cats.Count -eq 0) { continue }
                try { $t = [datetime]::Parse($obj.timestamp) } catch { continue }
                $timeEvents += [PSCustomObject]@{ Time = $t; Categories = $cats }
            }
        }

        if ($timeEvents.Count -lt 2 -and $msgUsage.Count -eq 0) { return "" }

        $stats = @{}
        function Get-Stat($cat) {
            if (-not $stats.ContainsKey($cat)) {
                $stats[$cat] = [PSCustomObject]@{ Seconds = 0.0; Tokens = 0.0 }
            }
            return $stats[$cat]
        }

        # Token totals per category (split each turn's usage across its tool_use blocks).
        foreach ($id in $msgUsage.Keys) {
            if (-not $msgCategories.ContainsKey($id)) { continue }
            $u = $msgUsage[$id]
            $cats = $msgCategories[$id]
            $n = $cats.Count
            if ($n -lt 1) { $n = 1 }
            $tok = [double]$u.input_tokens
            if ($u.cache_creation_input_tokens) { $tok += [double]$u.cache_creation_input_tokens }
            if ($u.output_tokens) { $tok += [double]$u.output_tokens }
            if ($u.cache_read_input_tokens) { $tok += [double]$u.cache_read_input_tokens }
            foreach ($cat in $cats) {
                (Get-Stat $cat).Tokens += ($tok / $n)
            }
        }

        # Time totals per category (gap between consecutive tool_result timestamps).
        # Attributed to the tool whose result ARRIVES at the end of the gap (index $i),
        # not the one before it - a gap represents "model picks the next tool + that tool
        # runs", so it belongs to the tool that just finished, not the one that already had.
        $timeEvents = $timeEvents | Sort-Object Time
        for ($i = 1; $i -lt $timeEvents.Count; $i++) {
            $gap = ($timeEvents[$i].Time - $timeEvents[$i - 1].Time).TotalSeconds
            if ($gap -lt 0) { continue }
            $cats = $timeEvents[$i].Categories
            $n = $cats.Count
            if ($n -lt 1) { $n = 1 }
            foreach ($cat in $cats) {
                (Get-Stat $cat).Seconds += ($gap / $n)
            }
        }

        if ($stats.Count -eq 0) { return "" }

        function Format-CompactSecs($s) {
            if ($s -ge 60) { return "$([Math]::Round($s / 60, 1))m" }
            return "$([Math]::Round($s))s"
        }

        $rows = $stats.GetEnumerator() | Sort-Object { $_.Value.Seconds } -Descending
        $top = @($rows | Select-Object -First 8)
        $rest = @($rows | Select-Object -Skip 8)

        $nameWidth = 12
        foreach ($r in $top) { if ($r.Key.Length -gt $nameWidth) { $nameWidth = $r.Key.Length } }

        $sb = New-Object System.Text.StringBuilder
        [void]$sb.AppendLine(("{0,-$nameWidth}  {1,7}  {2,8}" -f "Tool", "Time", "Tokens"))
        foreach ($r in $top) {
            [void]$sb.AppendLine(("{0,-$nameWidth}  {1,7}  {2,8}" -f $r.Key, (Format-CompactSecs $r.Value.Seconds), (Format-TokenCount $r.Value.Tokens)))
        }
        if ($rest.Count -gt 0) {
            $restSec = 0.0; $restTok = 0.0
            foreach ($r in $rest) { $restSec += $r.Value.Seconds; $restTok += $r.Value.Tokens }
            [void]$sb.AppendLine(("{0,-$nameWidth}  {1,7}  {2,8}" -f "+$($rest.Count) more", (Format-CompactSecs $restSec), (Format-TokenCount $restTok)))
        }

        return $sb.ToString().TrimEnd()
    } catch {
        return ""
    }
}

# Loop-wide running totals, folded in by Get-IterationTokenPayload after every
# iteration that produced a parsable log - completed and blocked alike, since both
# burned tokens.
$script:LoopIterationsCounted = 0
$script:LoopTokensTotal = 0.0
$script:LoopCostTotal = 0.0

# Everything the notification needs about one iteration's usage, in a single call:
# the token summary, the per-model cost split, the per-tool breakdown, and the
# loop-so-far running total (which this call also advances). Every notify site that
# has an iteration log goes through here, so the running total can never miss one.
function Get-IterationTokenPayload {
    param([string]$LogFile)
    $payload = [ordered]@{ Tokens = ""; PerModel = ""; Breakdown = ""; Cumulative = "" }
    $report = Get-TokenReport -LogFile $LogFile
    $payload.Tokens = $report.Summary
    $payload.PerModel = $report.PerModel
    $payload.Breakdown = Get-TimingTokenBreakdown -LogFile $LogFile

    if ($report.Summary) {
        $script:LoopIterationsCounted++
        $script:LoopTokensTotal += [double]$report.TotalTokens
        $script:LoopCostTotal += [double]$report.CostUsd
    }
    $payload.Cumulative = Get-LoopCumulative
    return $payload
}

# "3 iters | 41.2M tok | ~$28.90" - empty until at least one iteration has been
# measured, so the field simply doesn't appear on the very first notification.
function Get-LoopCumulative {
    if ($script:LoopIterationsCounted -le 0) { return "" }
    $cumulative = "$($script:LoopIterationsCounted) iters | $(Format-TokenCount $script:LoopTokensTotal) tok"
    if ($script:LoopCostTotal -gt 0) { $cumulative = $cumulative + ' | ~$' + (Format-CostUsd $script:LoopCostTotal) }
    return $cumulative
}

# Fire a Discord notification via notify.ps1 (gracefully no-ops if not configured).
function Send-Notify {
    param([string]$EventType, [string]$Task = "N/A", [string]$Url = "", [string]$Details = "", [string]$Tokens = "", [string]$Progress = "", [string]$Duration = "", [string]$Breakdown = "", [string]$PerModel = "", [string]$Cumulative = "")
    $notifyScript = Join-Path $PSScriptRoot "notify.ps1"
    if (-not (Test-Path $notifyScript)) { return }
    try {
        & $notifyScript -Event $EventType -Task $Task -Url $Url -Details $Details -Tokens $Tokens -Progress $Progress -Duration $Duration -Breakdown $Breakdown -PerModel $PerModel -Cumulative $Cumulative
    } catch {
        Write-Log "Notify failed: $($_.Exception.Message)" "Yellow"
    }
}

# Count backlog/done/*.md once - shared by the per-iteration progress label
# and the post-completion progress label.
function Get-DoneCount {
    $doneCount = 0
    if (Test-Path -LiteralPath (Join-Path $BacklogRoot "done")) {
        $doneCount = (Get-ChildItem -Path (Join-Path $BacklogRoot "done") -Filter "*.md" -Recurse -File -ErrorAction SilentlyContinue | Measure-Object).Count
    }
    return $doneCount
}

function New-RunBacklogAdapterPrompt {
    return @"
You are running this project's backlog workflow through a non-Claude CLI adapter.

Goal: execute exactly one backlog task iteration with behavior equivalent to the Claude Code slash command /run-backlog.

Required contract:
1. Read .claude/skills/run-backlog/SKILL.md before changing files.
2. Follow that skill exactly for one iteration only.
3. Read CLAUDE.md, .claude/rules/*, the selected task file, and only the relevant code requested by the workflow.
4. If your CLI cannot spawn subagents, perform the code-reviewer, security-auditor, and qa-verifier gates in this same session by reading their instructions from .claude/agents/*.md and applying the same blocking criteria.
5. Preserve the same stop tokens and print them exactly when blocked: COMPILE_BLOCKED, PREFLIGHT_BLOCKED, REVIEW_BLOCKED, VERIFY_BLOCKED, RUNTIME_BLOCKED, EDITOR_REQUIRED, NO_CHANGES, BASE_MERGE_CONFLICT, or "manual intervention required". (DEFERRED is NOT a block - end the iteration normally. Starting on an agent branch is allowed - never print BASE_UNKNOWN.)
6. Commit to the work branch ($AgentBranch) only when the run-backlog skill says the task is DONE, and push it only when the repo has an origin remote (the skill's HAS_REMOTE probe decides). Do not create a PR.

Environment for this iteration (STEP 2 of the skill reads these):
- AGENT_MODE=$($Mode.ToLowerInvariant())
- AGENT_BRANCH=$AgentBranch
- AGENT_BASE_BRANCH=$LoopBaseBranch
- AGENT_BACKLOG_ROOT=$BacklogRoot
7. Do not ask for confirmation. Work autonomously inside this repository.
8. Use English for all output, progress messages, reports, and commit messages.

Start now.
"@
}

function New-ClaudeRunBacklogPrompt {
    return @"
Execute exactly one iteration of this project's run-backlog workflow.

Required contract:
1. Read .claude/skills/run-backlog/SKILL.md before changing any files.
2. Follow that skill exactly for one iteration only.
3. Read CLAUDE.md, .claude/rules/*, the selected task file, and only the relevant code the workflow requests.
4. Spawn the code-reviewer, security-auditor, and qa-verifier subagents per the skill spec using the Agent tool.
5. Print exactly these tokens when blocked: COMPILE_BLOCKED, PREFLIGHT_BLOCKED, REVIEW_BLOCKED, VERIFY_BLOCKED, RUNTIME_BLOCKED, EDITOR_REQUIRED, NO_CHANGES, BASE_MERGE_CONFLICT, or "manual intervention required". (DEFERRED is NOT a block - end the iteration normally. Starting on an agent branch is allowed - never print BASE_UNKNOWN.)
6. Commit to the work branch ($AgentBranch) only when the skill marks the task DONE, and push it only when the repo has an origin remote (the skill's HAS_REMOTE probe decides). Do not create a PR.

Environment for this iteration (STEP 2 of the skill reads these):
- AGENT_MODE=$($Mode.ToLowerInvariant())
- AGENT_BRANCH=$AgentBranch
- AGENT_BASE_BRANCH=$LoopBaseBranch
- AGENT_BACKLOG_ROOT=$BacklogRoot
7. Do not ask for confirmation. Work autonomously inside this repository.
8. Use English for all output, progress messages, reports, and commit messages.

Start now.
"@
}

function New-AgentInvocation {
    param(
        [string]$ProviderName,
        [string]$RepoRootPath,
        [string]$SelectedModel,
        [string]$PromptFile,
        [switch]$DisableSkipPermissions,
        [int]$ThinkingBudget = 0,
        [string]$ReasoningEffortTier = ""
    )

    switch ($ProviderName) {
        "claude" {
            Set-Content -Path $PromptFile -Value (New-ClaudeRunBacklogPrompt) -Encoding utf8
            $cliArgs = @("--verbose", "--output-format", "stream-json", "--include-partial-messages")
            if (-not $DisableSkipPermissions) {
                $cliArgs += "--dangerously-skip-permissions"
            }
            if ($SelectedModel) {
                $cliArgs += @("--model", $SelectedModel)
            }
            if ($ReasoningEffortTier) {
                $cliArgs += @("--effort", $ReasoningEffortTier)
            }
            # Enable extended thinking via env var. claude (and its spawned subagents)
            # inherit MAX_THINKING_TOKENS from this process through Start-Process.
            # 0 = thinking off; clear any stale value so it never leaks across runs.
            if ($ThinkingBudget -gt 0) {
                $env:MAX_THINKING_TOKENS = "$ThinkingBudget"
            } else {
                Remove-Item Env:\MAX_THINKING_TOKENS -ErrorAction SilentlyContinue
            }
            return @{
                Command = "claude"
                Args = $cliArgs
                StdinFile = $PromptFile
                UseNullStdin = $false
                PromptFile = $PromptFile
                OutputMode = "claude-stream-json"
                HeaderProvider = "claude"
                HeaderModel = if ($SelectedModel) { $SelectedModel } else { "default" }
                HeaderEffort = if ($ReasoningEffortTier) { $ReasoningEffortTier } else { "default" }
                HeaderApproval = if ($DisableSkipPermissions) { "default" } else { "bypassPermissions" }
                HeaderSandbox = "n/a"
                HeaderThinking = if ($ThinkingBudget -gt 0) { "$ThinkingBudget tokens" } else { "off" }
            }
        }

        "codex" {
            Set-Content -Path $PromptFile -Value (New-RunBacklogAdapterPrompt) -Encoding utf8
            $cliArgs = @("exec", "-C", $RepoRootPath)
            if ($SelectedModel) {
                $cliArgs += @("-m", $SelectedModel)
            }
            # Codex reasoning is an effort tier, set via a config override (-c key=value),
            # which is stable across codex versions. Empty = leave the default untouched.
            if ($ReasoningEffortTier) {
                $cliArgs += @("-c", "model_reasoning_effort=`"$ReasoningEffortTier`"")
            }
            if ($DisableSkipPermissions) {
                $cliArgs += @("--ask-for-approval", "never", "--sandbox", "workspace-write")
            } else {
                $cliArgs += "--dangerously-bypass-approvals-and-sandbox"
            }
            $cliArgs += "-"
            return @{
                Command = "codex"
                Args = $cliArgs
                StdinFile = $PromptFile
                UseNullStdin = $false
                PromptFile = $PromptFile
                OutputMode = "raw"
                HeaderEffort = if ($ReasoningEffortTier) { $ReasoningEffortTier } else { "default" }
                HeaderThinking = if ($ReasoningEffortTier) { "effort=$ReasoningEffortTier" } else { "default" }
            }
        }

        "gemini" {
            Set-Content -Path $PromptFile -Value (New-RunBacklogAdapterPrompt) -Encoding utf8
            $cliArgs = @("--skip-trust", "-p", "Run the backlog loop using the instructions provided on stdin.", "-o", "stream-json")
            if ($SelectedModel) {
                $cliArgs += @("--model", $SelectedModel)
            }
            # Gemini thinking budget via env var (CLI flag name varies across versions;
            # an unread env var is harmless). gemini inherits it through Start-Process.
            # 0 = off; clear any stale value so it never leaks across runs.
            if ($ThinkingBudget -gt 0) {
                $env:GEMINI_THINKING_BUDGET = "$ThinkingBudget"
            } else {
                Remove-Item Env:\GEMINI_THINKING_BUDGET -ErrorAction SilentlyContinue
            }
            if (-not $DisableSkipPermissions) {
                $cliArgs += "--yolo"
            }
            # Filter startup noise and node-pty crash from terminal; full output still written to log via Tee-Object.
            $geminiFilter = "^Warning: Windows|^Warning: 256-color|^YOLO mode is enabled|^Ripgrep is not available|^Falling back to GrepTool|AttachConsole|conpty_console_list|consoleProcessList|^Node\.js v\d|^\s+at |^\s+\^"
            return @{
                Command = "gemini"
                Args = $cliArgs
                StdinFile = $PromptFile
                UseNullStdin = $false
                PromptFile = $PromptFile
                FilterPattern = $geminiFilter
                OutputMode = "gemini-stream-json"
                HeaderProvider = "gemini"
                HeaderModel = if ($SelectedModel) { $SelectedModel } else { "default" }
                HeaderEffort = "n/a"
                HeaderApproval = if ($DisableSkipPermissions) { "default" } else { "yolo" }
                HeaderSandbox = "n/a"
                HeaderThinking = if ($ThinkingBudget -gt 0) { "$ThinkingBudget tokens" } else { "off" }
            }
        }
    }
}

function Invoke-AgentInvocation {
    param(
        [hashtable]$Invocation,
        [string]$LogPath
    )

    $cmdLine = Join-CmdLine -Command $Invocation.Command -Arguments $Invocation.Args
    $flagFile = "$LogPath.done"
    Remove-Item -Path $flagFile -ErrorAction SilentlyContinue

    $runLine = if ($Invocation.StdinFile) {
        $stdinPath = ConvertTo-CmdArgument $Invocation.StdinFile
        "cmd.exe /c `"type $stdinPath | $cmdLine 2>&1`""
    } elseif ($Invocation.UseNullStdin) {
        "cmd.exe /c `"$cmdLine < nul 2>&1`""
    } else {
        "cmd.exe /c `"$cmdLine 2>&1`""
    }

    $outputMode = if ($Invocation.ContainsKey('OutputMode') -and $Invocation.OutputMode) {
        [string]$Invocation.OutputMode
    } else {
        "raw"
    }
    $filterPattern = if ($Invocation.ContainsKey('FilterPattern') -and $Invocation.FilterPattern) {
        [string]$Invocation.FilterPattern
    } else {
        ""
    }
    $headerProvider = if ($Invocation.ContainsKey('HeaderProvider') -and $Invocation.HeaderProvider) {
        [string]$Invocation.HeaderProvider
    } else {
        [string]$Invocation.Command
    }
    $headerModel = if ($Invocation.ContainsKey('HeaderModel') -and $Invocation.HeaderModel) {
        [string]$Invocation.HeaderModel
    } else {
        "default"
    }
    $headerEffort = if ($Invocation.ContainsKey('HeaderEffort') -and $Invocation.HeaderEffort) {
        [string]$Invocation.HeaderEffort
    } else {
        "default"
    }
    $headerApproval = if ($Invocation.ContainsKey('HeaderApproval') -and $Invocation.HeaderApproval) {
        [string]$Invocation.HeaderApproval
    } else {
        "n/a"
    }
    $headerSandbox = if ($Invocation.ContainsKey('HeaderSandbox') -and $Invocation.HeaderSandbox) {
        [string]$Invocation.HeaderSandbox
    } else {
        "n/a"
    }

    $scriptTemplate = @'
$helper = @"
using System;
using System.Runtime.InteropServices;
public class ConsoleHelper {
    const int STD_INPUT_HANDLE = -10;
    const uint ENABLE_QUICK_EDIT_MODE = 0x0040;
    const uint ENABLE_EXTENDED_FLAGS = 0x0080;
    [DllImport("kernel32.dll")]
    public static extern IntPtr GetStdHandle(int n);
    [DllImport("kernel32.dll")]
    public static extern bool GetConsoleMode(IntPtr h, out uint m);
    [DllImport("kernel32.dll")]
    public static extern bool SetConsoleMode(IntPtr h, uint m);
    public static void Disable() {
        IntPtr h = GetStdHandle(STD_INPUT_HANDLE);
        uint m;
        if (GetConsoleMode(h, out m)) {
            m &= ~ENABLE_QUICK_EDIT_MODE;
            m |= ENABLE_EXTENDED_FLAGS;
            SetConsoleMode(h, m);
        }
    }
}
"@

try {
    Add-Type -TypeDefinition $helper -ErrorAction SilentlyContinue
    [ConsoleHelper]::Disable()
} catch {}

$logPath = '__LOG_PATH__'
$flagFile = '__FLAG_FILE__'
$outputMode = '__OUTPUT_MODE__'
$filterPattern = '__FILTER_PATTERN__'
$headerProvider = '__HEADER_PROVIDER__'
$headerWorkdir = '__HEADER_WORKDIR__'
$headerModel = '__HEADER_MODEL__'
$headerEffort = '__HEADER_EFFORT__'
$headerApproval = '__HEADER_APPROVAL__'
$headerSandbox = '__HEADER_SANDBOX__'
$script:OpenTextLine = $false
$script:HeaderPrinted = $false

function Write-AccentLabel {
    param(
        [string]$Text,
        [ConsoleColor]$Color = [ConsoleColor]::Cyan
    )

    Write-Host $Text -NoNewline -ForegroundColor $Color
}

function Write-SpeakerBlock {
    param(
        [string]$Label,
        [string]$Body,
        [ConsoleColor]$LabelColor = [ConsoleColor]::Cyan,
        [ConsoleColor]$BodyColor = [ConsoleColor]::White
    )

    Finish-TextLine
    if ([string]::IsNullOrWhiteSpace($Body)) {
        Write-Host ("{0}:" -f $Label) -ForegroundColor $LabelColor
        return
    }

    $lines = $Body -split "`r?`n"
    Write-Host ("{0}: " -f $Label) -NoNewline -ForegroundColor $LabelColor
    Write-Host $lines[0] -ForegroundColor $BodyColor

    for ($i = 1; $i -lt $lines.Count; $i++) {
        if ([string]::IsNullOrWhiteSpace($lines[$i])) {
            continue
        }

        Write-Host "  " -NoNewline -ForegroundColor DarkGray
        Write-Host $lines[$i] -ForegroundColor $BodyColor
    }
}

function Write-SessionHeader {
    param(
        [string]$Workdir,
        [string]$Model,
        [string]$Provider,
        [string]$Effort,
        [string]$Approval,
        [string]$Sandbox,
        [string]$SessionId
    )

    if ($script:HeaderPrinted) {
        return
    }

    $script:HeaderPrinted = $true
    Write-Host ""
    Write-Host "--------" -ForegroundColor DarkGray
    Write-Host ("workdir: {0}" -f $Workdir) -ForegroundColor Gray
    Write-Host ("model: {0}" -f $Model) -ForegroundColor Gray
    Write-Host ("provider: {0}" -f $Provider) -ForegroundColor Gray
    Write-Host ("effort: {0}" -f $Effort) -ForegroundColor Gray
    Write-Host ("approval: {0}" -f $Approval) -ForegroundColor Gray
    Write-Host ("sandbox: {0}" -f $Sandbox) -ForegroundColor Gray
    if ($SessionId) {
        Write-Host ("session id: {0}" -f $SessionId) -ForegroundColor Gray
    }
    Write-Host "--------" -ForegroundColor DarkGray
}

function Finish-TextLine {
    if ($script:OpenTextLine) {
        Write-Host ""
        $script:OpenTextLine = $false
    }
}

function Normalize-ToolLabel {
    param([string]$ToolName)

    if ([string]::IsNullOrEmpty($ToolName)) {
        return ""
    }

    if ($ToolName -in @("Bash", "PowerShell", "run_shell_command")) {
        return "exec"
    }

    if ($ToolName.StartsWith("mcp__")) {
        $parts = $ToolName -split "__"
        if ($parts.Count -ge 3) {
            return $parts[2]
        }
        return $ToolName.Substring(5)
    }

    return $ToolName.ToLowerInvariant()
}

function Get-ToolBody {
    param(
        [string]$ToolName,
        [object]$Payload
    )

    if ($null -eq $Payload) {
        return ""
    }

    $props = $Payload.PSObject.Properties.Name

    # 1. Command Execution (Bash / PowerShell)
    $command = if ($props -contains "command") { [string]$Payload.command } else { $null }
    if ($command) {
        $description = if ($props -contains "description") { [string]$Payload.description } else { $null }
        if ($description) {
            return "{0}`n# {1}" -f $command, $description
        }
        return $command
    }

    # 2. File Write Tool
    if (($props -contains "file_path") -and ($props -contains "content")) {
        $fp = [string]$Payload.file_path
        $contentLen = ([string]$Payload.content).Length
        return "Write {0} ({1} chars)" -f $fp, $contentLen
    }

    # 3. File Edit Tool (Edit)
    if (($props -contains "file_path") -and (($props -contains "new_string") -or ($props -contains "replace_all") -or ($props -contains "new_text"))) {
        $fp = [string]$Payload.file_path
        $replaceAll = if ($props -contains "replace_all") { [bool]$Payload.replace_all } else { $false }
        return "Edit {0} (replace_all={1})" -f $fp, $replaceAll
    }

    # 4. File Read Tool (Read)
    if ($props -contains "file_path") {
        $fp = [string]$Payload.file_path
        $offset = if ($props -contains "offset") { $Payload.offset } else { 0 }
        $limit = if ($props -contains "limit") { $Payload.limit } else { 0 }
        return "Read {0} (offset={1}, limit={2})" -f $fp, $offset, $limit
    }

    # 5. Agent Tool (Agent)
    if ($props -contains "subagent_type") {
        $sat = [string]$Payload.subagent_type
        $desc = if ($props -contains "description") { [string]$Payload.description } else { "" }
        return "Agent ({0}): {1}" -f $sat, $desc
    }

    # 6. ToolSearch Tool
    if (($props -contains "query") -and ($props -contains "max_results")) {
        return "Search Tools: {0}" -f $Payload.query
    }

    # 7. MCP Codegraph/Search queries
    if ($props -contains "query") {
        return "Query: {0}" -f $Payload.query
    }

    # 8. MCP Unity Exec Code
    if ($props -contains "code") {
        return "Execute code: {0}" -f $Payload.code
    }

    # 9. MCP Unity Menu Item
    if ($props -contains "menuPath") {
        return "Menu: {0}" -f $Payload.menuPath
    }

    # 10. General MCP port payload
    if (($props.Count -eq 1) -and ($props -contains "port")) {
        return "port={0}" -f $Payload.port
    }

    return Format-CompactValue $Payload
}

function Write-StreamText {
    param([string]$Text)

    if ([string]::IsNullOrEmpty($Text)) {
        return
    }

    if (-not $script:OpenTextLine) {
        Write-Host ("{0}: " -f $headerProvider) -NoNewline -ForegroundColor Cyan
        $script:OpenTextLine = $true
    }

    Write-Host $Text -NoNewline -ForegroundColor White
}

function Write-ToolLine {
    param(
        [string]$Label,
        [string]$Body
    )

    Write-SpeakerBlock -Label $Label -Body $Body -LabelColor ([ConsoleColor]::Green) -BodyColor ([ConsoleColor]::White)
}

function Write-InfoLine {
    param([string]$Message)

    if ([string]::IsNullOrWhiteSpace($Message)) {
        return
    }

    Write-SpeakerBlock -Label "info" -Body $Message -LabelColor ([ConsoleColor]::DarkGray) -BodyColor ([ConsoleColor]::Gray)
}

function Format-CompactValue {
    param([object]$Value)

    if ($null -eq $Value) {
        return ""
    }

    if ($Value -is [string]) {
        return $Value
    }

    return ($Value | ConvertTo-Json -Compress -Depth 20)
}

function ConvertFrom-JsonSafe {
    param([string]$Line)

    if ([string]::IsNullOrWhiteSpace($Line)) {
        return $null
    }

    try {
        return ($Line | ConvertFrom-Json -ErrorAction Stop)
    } catch {
        return $null
    }
}

function Write-ToolOutput {
    param(
        [string]$Prefix,
        [string]$Text,
        [ConsoleColor]$Color = [ConsoleColor]::White
    )

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return
    }

    Finish-TextLine
    $lines = $Text -split "`r?`n"
    foreach ($line in $lines) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }
        Write-SpeakerBlock -Label $Prefix.Trim() -Body $line -LabelColor ([ConsoleColor]::DarkCyan) -BodyColor $Color
    }
}

function Render-ClaudeStreamLine {
    param([string]$Line)

    $obj = ConvertFrom-JsonSafe $Line

    if ($null -eq $obj) {
        if ($filterPattern -and $Line -match $filterPattern) {
            return
        }
        Write-InfoLine $Line
        return
    }

    switch ($obj.type) {
        "system" {
            if ($obj.subtype -eq "init") {
                $model = if ($obj.model) { [string]$obj.model } else { $headerModel }
                $workdir = if ($obj.cwd) { [string]$obj.cwd } else { $headerWorkdir }
                $sessionId = if ($obj.session_id) { [string]$obj.session_id } else { "" }
                $approval = if ($obj.permissionMode) { [string]$obj.permissionMode } else { $headerApproval }
                Write-SessionHeader $workdir $model $headerProvider $headerEffort $approval $headerSandbox $sessionId
            }
        }

        "assistant" {
            if ($obj.message -and $obj.message.content) {
                foreach ($block in $obj.message.content) {
                    if ($block.type -eq "tool_use") {
                        $toolName = [string]$block.name
                        $label = Normalize-ToolLabel $toolName
                        $body = Get-ToolBody $toolName $block.input
                        Write-ToolLine $label $body
                    }
                }
            }
        }

        "user" {
            if ($obj.tool_use_result) {
                $status = if ($obj.tool_use_result.is_error) { "error" } elseif ($obj.tool_use_result.interrupted) { "interrupted" } else { "ok" }
                Write-ToolLine "result" $status
                Write-ToolOutput "stdout" $obj.tool_use_result.stdout
                Write-ToolOutput "stderr" $obj.tool_use_result.stderr ([ConsoleColor]::Yellow)
            }
        }

        "stream_event" {
            if (-not $obj.event) {
                return
            }

            switch ($obj.event.type) {
                "content_block_delta" {
                    if ($obj.event.delta.type -eq "text_delta") {
                        Write-StreamText $obj.event.delta.text
                    }
                }

                "content_block_stop" {
                    Finish-TextLine
                }
            }
        }

        "result" {
            Finish-TextLine
            $status = if ($obj.subtype) { $obj.subtype } elseif ($obj.is_error) { "error" } else { "completed" }
            Write-ToolLine "done" ("Claude {0}" -f $status)
        }
    }
}

function Render-GeminiStreamLine {
    param([string]$Line)

    $obj = ConvertFrom-JsonSafe $Line

    if ($null -eq $obj) {
        if ($filterPattern -and $Line -match $filterPattern) {
            return
        }
        Write-InfoLine $Line
        return
    }

    switch ($obj.type) {
        "init" {
            $model = if ($obj.model) { [string]$obj.model } else { $headerModel }
            $sessionId = if ($obj.session_id) { [string]$obj.session_id } else { "" }
            Write-SessionHeader $headerWorkdir $model $headerProvider $headerEffort $headerApproval $headerSandbox $sessionId
        }

        "tool_use" {
            $toolName = [string]$obj.tool_name
            $label = Normalize-ToolLabel $toolName
            $body = Get-ToolBody $toolName $obj.parameters
            Write-ToolLine $label $body
        }

        "tool_result" {
            $status = if ($obj.status) { [string]$obj.status } else { "completed" }
            Write-ToolLine "result" $status
            if ($obj.PSObject.Properties.Name -contains "output") {
                Write-ToolOutput "stdout" ([string]$obj.output)
            }
        }

        "message" {
            if ($obj.role -eq "assistant" -and $obj.delta) {
                Write-StreamText ([string]$obj.content)
            }
        }

        "result" {
            Finish-TextLine
            $status = if ($obj.status) { [string]$obj.status } else { "completed" }
            Write-ToolLine "done" ("Gemini {0}" -f $status)
        }
    }
}

$code = 1
try {
    # PowerShell 5.1: piping a native exe loses $LASTEXITCODE after ForEach-Object.
    # Wrap in cmd.exe /c and capture the process exit code via $LASTEXITCODE immediately
    # after the pipeline (before any cmdlet resets it), then record for the outer loop.
    __RUN_LINE__ | ForEach-Object {
        $line = [string]$_
        Add-Content -Path $logPath -Value $line -Encoding utf8

        switch ($outputMode) {
            "claude-stream-json" { Render-ClaudeStreamLine $line; break }
            "gemini-stream-json" { Render-GeminiStreamLine $line; break }
            default {
                if ($filterPattern -and $line -match $filterPattern) {
                    break
                }
                Finish-TextLine
                Write-Host $line
            }
        }
    }

    # $LASTEXITCODE here reflects the cmd.exe wrapper exit code, which mirrors the
    # underlying CLI process. "success" result events in the log already signal a clean
    # run; treat exit code 0 or 1 from cmd.exe wrapping as success when the log
    # contains a non-error result event (handled by Test-Blocked in the outer loop).
    $rawCode = $LASTEXITCODE
    $code = if ($null -eq $rawCode) { 0 } else { [int]$rawCode }
    Finish-TextLine
} finally {
    Set-Content -Path $flagFile -Value $code
}

# Keep this task window open ONLY when the agent genuinely failed, so the error stays
# visible. A finished task must close its own window (the outer loop opens a new one
# per task, and a stack of "Press Enter" windows piles up otherwise).
# The flag file is already written above, so the outer loop proceeds regardless.
if ($code -ne 0) {
    $keepOpen = $true
    # Mirror the outer loop: a Gemini AttachConsole crash exits non-zero but is benign.
    if ($headerProvider -eq 'gemini') {
        $logTail = Get-Content $logPath -Raw -ErrorAction SilentlyContinue
        if ($logTail -match 'Error: AttachConsole failed') { $keepOpen = $false }
    }
    # Mirror the outer loop: `cmd.exe /c "type prompt | claude ..."` propagates the
    # last piped process' code, so a task that ran to completion (task DONE, commit
    # + push) still surfaces here as exit 1. Treat a successful result event in the
    # log as success and let the window close by itself.
    if ($headerProvider -eq 'claude') {
        $logLines = Get-Content $logPath -ErrorAction SilentlyContinue
        foreach ($logLine in $logLines) {
            $trimmed = ([string]$logLine).TrimStart()
            if (-not $trimmed.StartsWith('{')) { continue }
            try { $resultObj = $trimmed | ConvertFrom-Json -ErrorAction Stop } catch { continue }
            if ($resultObj.type -eq 'result' -and -not $resultObj.is_error) {
                $keepOpen = $false
                break
            }
        }
    }
    if ($keepOpen) {
        Write-Host ""
        Write-Host ("Task FAILED (exit code {0}) - this window is kept open so you can read the error above." -f $code) -ForegroundColor Red
        Write-Host "Press Enter to close this window..." -ForegroundColor DarkGray
        $null = Read-Host
    }
}
'@

    $escapedLogPath = $LogPath.Replace("'", "''")
    $escapedFlagFile = $flagFile.Replace("'", "''")
    $escapedOutputMode = $outputMode.Replace("'", "''")
    $escapedFilterPattern = $filterPattern.Replace("'", "''")
    $escapedHeaderProvider = $headerProvider.Replace("'", "''")
    $escapedHeaderWorkdir = $WorkDir.Replace("'", "''")
    $escapedHeaderModel = $headerModel.Replace("'", "''")
    $escapedHeaderEffort = $headerEffort.Replace("'", "''")
    $escapedHeaderApproval = $headerApproval.Replace("'", "''")
    $escapedHeaderSandbox = $headerSandbox.Replace("'", "''")
    $scriptToRun = $scriptTemplate.Replace('__LOG_PATH__', $escapedLogPath)
    $scriptToRun = $scriptToRun.Replace('__FLAG_FILE__', $escapedFlagFile)
    $scriptToRun = $scriptToRun.Replace('__OUTPUT_MODE__', $escapedOutputMode)
    $scriptToRun = $scriptToRun.Replace('__FILTER_PATTERN__', $escapedFilterPattern)
    $scriptToRun = $scriptToRun.Replace('__HEADER_PROVIDER__', $escapedHeaderProvider)
    $scriptToRun = $scriptToRun.Replace('__HEADER_WORKDIR__', $escapedHeaderWorkdir)
    $scriptToRun = $scriptToRun.Replace('__HEADER_MODEL__', $escapedHeaderModel)
    $scriptToRun = $scriptToRun.Replace('__HEADER_EFFORT__', $escapedHeaderEffort)
    $scriptToRun = $scriptToRun.Replace('__HEADER_APPROVAL__', $escapedHeaderApproval)
    $scriptToRun = $scriptToRun.Replace('__HEADER_SANDBOX__', $escapedHeaderSandbox)
    $scriptToRun = $scriptToRun.Replace('__RUN_LINE__', $runLine)

    # Windows CreateProcess caps the command line at ~32K chars, and -EncodedCommand
    # (base64 of UTF-16LE) blows past that for our ~12KB scriptTemplate. Write to a
    # temp .ps1 and launch with -File instead.
    $tempScript = "$LogPath.run.ps1"
    Set-Content -Path $tempScript -Value $scriptToRun -Encoding utf8

    # -WorkingDirectory pins the agent to the work dir: in Worktree mode that is
    # the sibling checkout, not the dev's. Without it the agent would edit the
    # dev's files while committing on the worktree branch.
    $process = Start-Process powershell.exe -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $tempScript) -WorkingDirectory $WorkDir -PassThru

    Write-Host "Waiting for agent window to finish..." -ForegroundColor Gray
    # Watchdog (mirrors run-backlog-loop.sh:459-494): a hung/token-exhausted task
    # otherwise polls forever and keeps burning tokens. Kill it on inactivity (no log
    # growth for $TaskInactivityTimeoutSec) or on the hard cap ($TaskHardTimeoutSec).
    $waitStart = Get-Date
    $lastSize = -1L
    $lastGrowth = $waitStart
    while (-not (Test-Path $flagFile)) {
        if ($process.HasExited) {
            Write-Host "Agent window was closed unexpectedly before finishing!" -ForegroundColor Red
            return 1
        }

        $now = Get-Date
        $curSize = 0L
        if (Test-Path $LogPath) {
            try { $curSize = (Get-Item -LiteralPath $LogPath -ErrorAction Stop).Length } catch { $curSize = $lastSize }
        }
        if ($curSize -ne $lastSize) {
            $lastSize = $curSize
            $lastGrowth = $now
        }

        $inactiveSec = ($now - $lastGrowth).TotalSeconds
        $elapsedSec = ($now - $waitStart).TotalSeconds
        if ($inactiveSec -ge $TaskInactivityTimeoutSec -or $elapsedSec -ge $TaskHardTimeoutSec) {
            if ($inactiveSec -ge $TaskInactivityTimeoutSec) {
                $reason = "Task hung or stopped due to token exhaustion/inactivity (no log updates for {0}m)" -f [int]($TaskInactivityTimeoutSec / 60)
            } else {
                $reason = "Task timed out (exceeded {0}m limit)" -f [int]($TaskHardTimeoutSec / 60)
            }
            Write-Host ""
            Write-Host "  [WATCHDOG] $reason. Killing agent process so it stops consuming tokens." -ForegroundColor Red
            # Claim the result first so the killed launcher's finally block can't clobber it.
            Set-Content -Path $flagFile -Value 124 -ErrorAction SilentlyContinue
            # /T kills the whole tree (powershell launcher -> cmd.exe -> claude/node).
            & taskkill /PID $process.Id /T /F 2>$null | Out-Null
            break
        }

        Start-Sleep -Milliseconds 500
    }

    $exitCodeStr = Get-Content $flagFile -Raw -ErrorAction SilentlyContinue
    $exitCode = 0
    if ($exitCodeStr -match '\d+') {
        $exitCode = [int]$exitCodeStr.Trim()
    }

    Remove-Item -Path $flagFile -ErrorAction SilentlyContinue
    Remove-Item -Path $tempScript -ErrorAction SilentlyContinue
    return $exitCode
}

Write-Log "=== Backlog Loop Started ===" "Cyan"
Write-Log "Provider:        $Provider" "Gray"
Write-Log "Repo:            $RepoRoot" "Gray"
Write-Log "Max iterations:  $MaxIterations" "Gray"
Write-Log "Log dir:         $LogDir" "Gray"
Write-Log "Summary log:     $summaryLog" "Gray"
if ($AutoThinkingByTier) {
    Write-Log "Thinking:        auto by tier (XS=$XsThinkingTokens, S=$SThinkingTokens, M=$MThinkingTokens, L=$LThinkingTokens)" "Gray"
} elseif ($ThinkingTokens -gt 0) {
    Write-Log "Thinking:        $ThinkingTokens tokens" "Gray"
} else {
    Write-Log "Thinking:        off" "Gray"
}
if ($AutoModelByTier) {
    Write-Log "Model:           auto by tier (XS=$XsModel/$XsEffort, S=$SModel/$SEffort, M=$MModel/$MEffort, L=$LModel/$LEffort)" "Gray"
} else {
    Write-Log "Model:           $Model" "Gray"
}
if ($ReasoningEffort) {
    Write-Log "Reasoning effort: $ReasoningEffort" "Gray"
}

$cli = Get-Command $Provider -ErrorAction SilentlyContinue
if (-not $cli) {
    Write-Log "ERROR: '$Provider' CLI not found on PATH." "Red"
    Write-Host ""
    Write-Host "Press Enter to close this window..." -ForegroundColor DarkGray
    $null = Read-Host
    exit 1
}
Write-Log "CLI:             $($cli.Source)" "Gray"

# --- Prerequisite self-check (fail fast BEFORE running any task) ------------
# Root cause of past GATE_RECEIPT_MISSING stops: the loop's quality gates spawn
# named subagents (code-reviewer, qa-verifier, security-auditor,
# performance-reviewer) that live in .claude/agents/*.md and reach Claude Code
# ONLY through the .claude/agents -> .claude/agents junction. If that junction
# is missing, Claude cannot spawn them by type, silently falls back to
# general-purpose, and the post-iteration receipt audit then fails with
# GATE_RECEIPT_MISSING *after* a whole task already ran. Auto-repair the
# junctions (idempotent), then hard-fail if still broken.
if ($Provider -eq "claude") {
    $requiredAgents = @("code-reviewer", "qa-verifier", "security-auditor", "performance-reviewer")
    $agentsDir = Join-Path $RepoRoot ".claude\agents"
    $missingAgents = @($requiredAgents | Where-Object { -not (Test-Path (Join-Path $agentsDir "$_.md")) })

    if ($missingAgents.Count -gt 0) {
        Write-Log "Subagents:       NOT registered (.claude/agents missing: $($missingAgents -join ', ')) - running sync-to-agents.ps1" "Yellow"
        $syncScript = Join-Path $PSScriptRoot "sync-to-agents.ps1"
        if (Test-Path $syncScript) {
            & powershell -NoProfile -ExecutionPolicy Bypass -File $syncScript | Out-Null
        }
        $missingAgents = @($requiredAgents | Where-Object { -not (Test-Path (Join-Path $agentsDir "$_.md")) })
    }

    if ($missingAgents.Count -gt 0) {
        Write-Log "FATAL: custom subagents still unregistered after sync: $($missingAgents -join ', ')." "Red"
        Write-Log "  Fix manually: powershell -ExecutionPolicy Bypass -File .claude/scripts/sync-to-agents.ps1" "Red"
        Write-Log "  (.claude/agents must junction to .claude/agents so the gate reviewers spawn by type; otherwise" "Red"
        Write-Log "   Claude falls back to general-purpose and the iteration ends in GATE_RECEIPT_MISSING.)" "Red"
        Write-Host ""
        Write-Host "Press Enter to close this window..." -ForegroundColor DarkGray
        $null = Read-Host
        exit 1
    }
    Write-Log "Subagents:       registered ($($requiredAgents -join ', '))" "Gray"
}

# backlog-ops.py + preflight are invoked as `python3 ...` inside each iteration.
# On Windows the Microsoft Store alias answers python/python3 with an error
# (exit 9009), which would silently skip those deterministic gates. Warn loudly
# if the interpreter the skill calls ('python3') is not really on PATH.
$pythonName = $null
foreach ($c in @("python3", "python", "py")) {
    $ver = (& $c --version 2>&1 | Out-String)
    if ($LASTEXITCODE -eq 0 -and $ver -match 'Python \d') { $pythonName = $c; break }
}
if (-not $pythonName) {
    Write-Log "WARNING: no working Python (python3/python/py) on PATH - backlog-ops.py + preflight may be skipped silently." "Yellow"
    Write-Log "  Fix: install Python, or disable the Store alias: Settings > Apps > Advanced app settings > App execution aliases > turn OFF python.exe/python3.exe" "Yellow"
} elseif ($pythonName -ne "python3") {
    Write-Log "Python:          '$pythonName' works but the skill calls 'python3'. If iterations report 'Python was not found', alias python3 -> $pythonName or disable the Store stub." "Yellow"
} else {
    Write-Log "Python:          python3 OK" "Gray"
}

$promptFile = Join-Path $LogDir "prompt-$Provider-$timestamp.md"
Write-Log "Adapter prompt:  $promptFile" "Gray"

$iter = 0
$completedIterations = 0
$stopReason = "MaxIterations reached"
$transientApiRetries = 0   # consecutive transient API blips; reset by any completed iteration

for ($iter = 1; $iter -le $MaxIterations; $iter++) {
    Write-Log ""
    Write-Log "=== Iteration $iter / $MaxIterations ===" "Cyan"

    $status = Get-BacklogStatus
    Write-Log "Backlog state: TODO=$($status.TodoCount), IN_PROGRESS=$($status.InProgressCount)" "Gray"

    # doneCountBefore/totalTasksBefore snapshot the backlog at the TOP of this
    # iteration, so every notification fired during the iteration (including
    # blocked/error events, where the task never reaches backlog/done) can
    # still say "this is task N of M".
    $doneCountBefore = Get-DoneCount
    $totalTasksBefore = $doneCountBefore + $status.TodoCount + $status.InProgressCount

    if ($status.TodoCount -eq 0 -and $status.InProgressCount -eq 0) {
        $stopReason = "Backlog empty (no TODO, no IN PROGRESS)"
        Write-Log $stopReason "Green"
        # No iteration log to analyze here, but the loop-so-far total is exactly what
        # closes the run out ("everything done - this is what it cost").
        Send-Notify -EventType "BACKLOG_EMPTY" -Task "N/A" -Details "All backlog tasks have been processed successfully." -Progress "$doneCountBefore/$doneCountBefore" -Cumulative (Get-LoopCumulative)
        break
    }

    # "Current" task's 1-based position among all tasks (todo + in-progress + done).
    $taskProgress = "$($doneCountBefore + 1)/$totalTasksBefore"

    # Resolve current task info for notifications.
    $notifyInfo = Get-NotifyTaskInfo

    # Pick the thinking budget + model + effort for this iteration (per-tier when requested).
    $selectedThinkingBudget = $ThinkingTokens
    $selectedModel = $Model
    $selectedEffort = $ReasoningEffort
    if ($AutoThinkingByTier -or $AutoModelByTier) {
        $taskProfile = Get-NextBacklogTaskProfile
        if ($AutoThinkingByTier) {
            $selectedThinkingBudget = Get-ThinkingBudgetForTier -Tier $taskProfile.Tier
        }
        if ($AutoModelByTier) {
            $selectedModel = Get-ModelForTier -ProviderName $Provider -Tier $taskProfile.Tier
            $selectedEffort = Get-EffortForTier -ProviderName $Provider -Tier $taskProfile.Tier
        }
        Write-Log "Task profile: [$($taskProfile.Tier)] $($taskProfile.Title) ($($taskProfile.State)); model=$selectedModel; effort=$selectedEffort; thinking=$selectedThinkingBudget" "Gray"
    }

    $invocation = New-AgentInvocation `
        -ProviderName $Provider `
        -RepoRootPath $WorkDir `
        -SelectedModel $selectedModel `
        -PromptFile $promptFile `
        -DisableSkipPermissions:$NoSkipPermissions `
        -ThinkingBudget $selectedThinkingBudget `
        -ReasoningEffortTier $selectedEffort

    Write-Log "Agent args:      $($invocation.Args -join ' ')" "Gray"

    $iterLog = Join-Path $LogDir "iter-$Provider-$timestamp-$($iter.ToString('000')).log"
    Write-Log "Starting $Provider (iter log: $iterLog)" "Gray"

    $iterStart = Get-Date
    $exitCode = Invoke-AgentInvocation -Invocation $invocation -LogPath $iterLog
    $iterDuration = (Get-Date) - $iterStart
    $completedIterations = $iter

    Write-Log "Iter $iter done in $($iterDuration.ToString('hh\:mm\:ss')) (exit: $exitCode)" "Gray"

    if ($exitCode -ne 0) {
        $isGeminiConsoleCrash = $false
        $isClaudeSuccessResult = $false

        $logContent = Get-Content $iterLog -Raw -ErrorAction SilentlyContinue

        if ($Provider -eq "gemini") {
            if ($logContent -match "Error: AttachConsole failed") {
                $isGeminiConsoleCrash = $true
            }
        }

        # cmd.exe /c "type prompt | claude ..." returns exit code 1 even when Claude
        # exits cleanly (cmd.exe propagates the last piped process exit code, and
        # claude CLI itself may exit 1 on Windows for benign reasons). Check for a
        # successful result event in the log before treating non-zero as a failure.
        if ($Provider -eq "claude" -and $logContent) {
            foreach ($line in ($logContent -split "`r?`n")) {
                if (-not $line.TrimStart().StartsWith('{')) { continue }
                try { $obj = $line | ConvertFrom-Json -ErrorAction Stop } catch { continue }
                if ($obj.type -eq 'result' -and -not $obj.is_error) {
                    $isClaudeSuccessResult = $true
                    break
                }
            }
        }

        # Transient blips (transport break / 529) are worth another attempt; auth and
        # exhausted-usage failures are not, and keep the unconditional break below.
        $failureClass = $null
        if ($Provider -eq "claude" -and -not $isClaudeSuccessResult) {
            $failureClass = Get-ClaudeFailureClass -LogPath $iterLog
        }

        if ($isGeminiConsoleCrash) {
            Write-Log "Gemini CLI crashed with AttachConsole failed, ignoring non-zero exit code." "Yellow"
        } elseif ($isClaudeSuccessResult) {
            Write-Log "Claude returned non-zero exit code ($exitCode) but log contains a successful result event - treating as success." "Yellow"
        } elseif ($failureClass -and $failureClass.Transient -and $transientApiRetries -lt $MaxTransientApiRetries) {
            $transientApiRetries++
            # 30s, 60s, 120s - a dropped stream usually clears on the first retry; the
            # backoff matters for an overload, which needs the far side to drain.
            $backoffSec = [int](30 * [Math]::Pow(2, $transientApiRetries - 1))
            $retryMsg = "Transient API failure on iteration $iter ($($failureClass.Reason)). Retry $transientApiRetries/$MaxTransientApiRetries in ${backoffSec}s."
            Write-Log $retryMsg "Yellow"
            Send-Notify -EventType "API_RETRY" -Task $notifyInfo.Title -Url $notifyInfo.Url -Details $retryMsg -Progress $taskProgress -Duration $iterDuration.ToString('hh\:mm\:ss')
            Start-Sleep -Seconds $backoffSec
            # The next iteration re-picks the same task (it is still in
            # backlog/in-progress/), so the retry resumes rather than skips it.
            continue
        } else {
            $stopReason = "$Provider exited non-zero (exit code: $exitCode). See $iterLog"
            if ($failureClass -and $failureClass.Reason -ne "unclassified non-zero exit") {
                $exhausted = if ($failureClass.Transient) { " after $transientApiRetries retries" } else { "" }
                $stopReason = "$Provider stopped$($exhausted): $($failureClass.Reason) (exit code: $exitCode). See $iterLog"
            }
            Write-Log $stopReason "Red"
            $payload = Get-IterationTokenPayload -LogFile $iterLog
            Send-Notify -EventType "CLI_ERROR" -Task $notifyInfo.Title -Url $notifyInfo.Url -Details $stopReason -Tokens $payload.Tokens -Progress $taskProgress -Duration $iterDuration.ToString('hh\:mm\:ss') -Breakdown $payload.Breakdown -PerModel $payload.PerModel -Cumulative $payload.Cumulative
            break
        }
    }

    # Reaching here means the iteration ran to completion (clean, or benign non-zero),
    # so the streak resets: $MaxTransientApiRetries counts CONSECUTIVE blips, not
    # lifetime ones. The retry path above uses `continue` and deliberately skips this.
    $transientApiRetries = 0

    if (Test-Blocked -LogPath $iterLog) {
        $stopReason = "Detected COMPILE_BLOCKED, PREFLIGHT_BLOCKED, REVIEW_BLOCKED, VERIFY_BLOCKED, RUNTIME_BLOCKED, EDITOR_REQUIRED, NO_CHANGES, BASE_UNKNOWN, BASE_MERGE_CONFLICT, or manual intervention required. See $iterLog"
        Write-Log $stopReason "Red"
        $block = Get-BlockClassification -LogPath $iterLog
        $payload = Get-IterationTokenPayload -LogFile $iterLog
        Send-Notify -EventType $block.Event -Task $notifyInfo.Title -Url $notifyInfo.Url -Details $block.Details -Tokens $payload.Tokens -Progress $taskProgress -Duration $iterDuration.ToString('hh\:mm\:ss') -Breakdown $payload.Breakdown -PerModel $payload.PerModel -Cumulative $payload.Cumulative
        break
    }

    # Deterministic outcome check (mirrors run-backlog-loop.sh): exit 0 + no
    # sentinel, but the picked task is still in backlog/in-progress/ -> the model
    # stopped without printing its blocker token. Stop instead of re-running the
    # same task forever.
    $taskBase = ""
    if ($notifyInfo.RelPath) { $taskBase = Split-Path -Leaf $notifyInfo.RelPath }
    if ($taskBase -and (Test-Path -LiteralPath (Join-Path (Join-Path $BacklogRoot "in-progress") $taskBase))) {
        $stopReason = "Silent failure on iteration ${iter}: clean exit + no blocker sentinel, but $taskBase is still in backlog/in-progress/. See $iterLog"
        Write-Log $stopReason "Red"
        $payload = Get-IterationTokenPayload -LogFile $iterLog
        Send-Notify -EventType "SILENT_FAIL" -Task $notifyInfo.Title -Url $notifyInfo.Url -Details $stopReason -Tokens $payload.Tokens -Progress $taskProgress -Duration $iterDuration.ToString('hh\:mm\:ss') -Breakdown $payload.Breakdown -PerModel $payload.PerModel -Cumulative $payload.Cumulative
        break
    }

    # Gate receipts (claude only - the subagent_type evidence is claude
    # stream-json): an S/M/L task that reached DONE must show the tier's
    # mandatory reviewer spawns in the log. S -> code-reviewer; M/L ->
    # code-reviewer + qa-verifier ("gates ran" must never be only the model's
    # own claim). perf/security receipts are conditional, so not required here.
    if ($Provider -eq "claude" -and $notifyInfo.Tier -and $taskBase -and (Test-Path -LiteralPath (Join-Path (Join-Path $BacklogRoot "done") $taskBase))) {
        $required = @()
        switch ($notifyInfo.Tier) {
            "S" { $required = @("code-reviewer") }
            "M" { $required = @("code-reviewer", "qa-verifier") }
            "L" { $required = @("code-reviewer", "qa-verifier") }
        }
        $missing = @()
        foreach ($agentName in $required) {
            # Match plain tool_use JSON ("subagent_type":"x") and the escaped
            # partial-message delta form (\"subagent_type\":\"x\").
            $receiptPattern = '("|\\")subagent_type("|\\")\s*:\s*("|\\")' + [regex]::Escape($agentName)
            if (-not (Select-String -Path $iterLog -Pattern $receiptPattern -Quiet -ErrorAction SilentlyContinue)) { $missing += $agentName }
        }
        if ($missing.Count -gt 0) {
            $stopReason = "Gate receipt missing on iteration ${iter}: $taskBase (tier $($notifyInfo.Tier)) reached DONE but the log has no Agent spawn for: $($missing -join ', '). See $iterLog"
            Write-Log $stopReason "Red"
            $payload = Get-IterationTokenPayload -LogFile $iterLog
            Send-Notify -EventType "GATE_RECEIPT_MISSING" -Task $notifyInfo.Title -Url $notifyInfo.Url -Details $stopReason -Tokens $payload.Tokens -Progress $taskProgress -Duration $iterDuration.ToString('hh\:mm\:ss') -Breakdown $payload.Breakdown -PerModel $payload.PerModel -Cumulative $payload.Cumulative
            break
        }
    }

    # Task passed all gates this iteration - notify success.
    $statusAfter = Get-BacklogStatus
    $doneCount = Get-DoneCount
    $totalCount = $statusAfter.TodoCount + $statusAfter.InProgressCount + $doneCount
    $completedDetails = "Progress: Task $doneCount of $totalCount completed successfully.`nCommitted to $AgentBranch (pushed if the repo has a remote). Ready for manual verify + merge into the base branch."
    $payload = Get-IterationTokenPayload -LogFile $iterLog
    Send-Notify -EventType "TASK_COMPLETED" -Task $notifyInfo.Title -Url $notifyInfo.Url -Details $completedDetails -Tokens $payload.Tokens -Progress "$doneCount/$totalCount" -Duration $iterDuration.ToString('hh\:mm\:ss') -Breakdown $payload.Breakdown -PerModel $payload.PerModel -Cumulative $payload.Cumulative
}

$totalDuration = (Get-Date) - $startTime
Write-Log ""
Write-Log "=== Loop Finished ===" "Cyan"
Write-Log "Iterations ran:  $completedIterations" "Gray"
Write-Log "Total duration:  $($totalDuration.ToString('hh\:mm\:ss'))" "Gray"
Write-Log "Stop reason:     $stopReason" "Gray"
Write-Log "Summary log:     $summaryLog" "Gray"

Write-Host ""
Write-Host "Press Enter to close this window..." -ForegroundColor DarkGray
$null = Read-Host
exit 0
