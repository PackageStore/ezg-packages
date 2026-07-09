# Shared autonomous backlog loop for Claude, Codex, and Gemini CLI.
#
# This file owns the loop behavior:
#   - inspect BACKLOG.md
#   - run one headless agent iteration
#   - write per-iteration logs
#   - stop on empty backlog, non-zero CLI exit, blocker sentinels, or MaxIterations
#
# Use one of the provider wrappers instead of calling this directly:
#   run-backlog-loop-claude.ps1
#   run-backlog-loop-codex.ps1
#   run-backlog-loop-gemini.ps1

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("claude", "codex", "gemini")]
    [string]$Provider,

    [int]$MaxIterations = 100,
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

    [switch]$AutoThinkingByTier,
    [int]$XsThinkingTokens = 3000,
    [int]$SThinkingTokens = 6000,
    [int]$MThinkingTokens = 10000,
    [int]$LThinkingTokens = 10000
)

$ErrorActionPreference = "Continue"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$RepoRoot = Split-Path -Parent $RepoRoot
Set-Location $RepoRoot

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
    if (-not (Test-Path "BACKLOG.md")) { return $result }

    $content = Get-Content "BACKLOG.md" -Raw

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

function Get-NextBacklogTaskProfile {
    $result = @{ Tier = ""; Title = ""; State = "" }
    if (-not (Test-Path "BACKLOG.md")) { return $result }

    $content = Get-Content "BACKLOG.md" -Raw
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
        if ($resultText -match '\b(COMPILE_BLOCKED|PREFLIGHT_BLOCKED|REVIEW_BLOCKED|VERIFY_BLOCKED|RUNTIME_BLOCKED)\b') { return $true }
        if ($resultText -match 'manual intervention required') { return $true }
    }
    return $false
}

# Resolve the current task's title + file URL for notifications (mirrors the bash loop).
function Get-NotifyTaskInfo {
    $result = @{ Title = "Unknown Task"; Url = "" }
    if (-not (Test-Path "BACKLOG.md")) { return $result }
    $content = Get-Content "BACKLOG.md" -Raw
    foreach ($name in @("IN PROGRESS", "TODO")) {
        $escaped = [regex]::Escape($name)
        if ($content -notmatch "(?ms)^## $escaped\s*\r?\n(.*?)(?=^## )") { continue }
        $section = $matches[1]
        foreach ($line in ($section -split "`r?`n")) {
            if ($line -match '^\s*-\s*\[(HIGH|MEDIUM|LOW)\]\s+(?:\[(XS|S|M|L)\]\s+)?\[([^\]]+)\]') {
                $result.Title = [string]$matches[3]
                if ($line -match '\]\((backlog/[^)]+)\)') {
                    $result.Url = "file://$RepoRoot/$($matches[1])"
                }
                return $result
            }
        }
    }
    return $result
}

# Classify which blocker fired from the iteration log (mirrors the bash loop).
function Get-BlockClassification {
    param([string]$LogPath)
    $result = @{ Event = "VERIFY_BLOCKED"; Details = "Manual intervention required." }
    if (-not (Test-Path $LogPath)) { return $result }
    $content = Get-Content $LogPath -Raw -ErrorAction SilentlyContinue
    if (-not $content) { return $result }
    foreach ($token in @("COMPILE_BLOCKED", "PREFLIGHT_BLOCKED", "REVIEW_BLOCKED", "RUNTIME_BLOCKED", "VERIFY_BLOCKED")) {
        if ($content -match $token) {
            $result.Event = $token
            $m = [regex]::Match($content, "$token.*")
            if ($m.Success) { $result.Details = $m.Value }
            return $result
        }
    }
    $m = [regex]::Match($content, "(?i)manual intervention.*")
    if ($m.Success) { $result.Details = $m.Value } else { $result.Details = "Automation paused. Manual intervention required." }
    return $result
}

function Get-TokenUsage {
    param([string]$LogFile)
    if (-not (Test-Path $LogFile)) { return "" }
    try {
        $lines = Get-Content $LogFile -ErrorAction SilentlyContinue
        if ($null -eq $lines) { return "" }
        $usages = @()
        foreach ($line in $lines) {
            if (-not $line.Trim()) { continue }
            $obj = ConvertFrom-Json $line -ErrorAction SilentlyContinue
            if ($obj -and $obj.type -eq "assistant" -and $obj.message -and $obj.message.id -and $obj.message.usage) {
                $usages += $obj.message
            }
        }
        if ($usages.Count -eq 0) { return "" }
        $grouped = $usages | Group-Object -Property id
        $uniqueUsages = @()
        foreach ($g in $grouped) {
            $maxUsage = $g.Group | Sort-Object -Property { $_.usage.output_tokens } -Descending | Select-Object -First 1
            $uniqueUsages += $maxUsage.usage
        }
        
        $in = 0; $out = 0; $cr = 0
        foreach ($u in $uniqueUsages) {
            $in += $u.input_tokens
            if ($u.cache_creation_input_tokens) { $in += $u.cache_creation_input_tokens }
            if ($u.output_tokens) { $out += $u.output_tokens }
            if ($u.cache_read_input_tokens) { $cr += $u.cache_read_input_tokens }
        }
        
        function Format-N($n) {
            if ($n -ge 1000000) { 
                $val = [Math]::Round($n / 100000) / 10
                return "$val`M"
            }
            if ($n -ge 1000) { 
                $val = [Math]::Round($n / 100) / 10
                return "$val`K"
            }
            return "$n"
        }
        
        $total = $in + $out
        return "$(Format-N $total) ($(Format-N $in) In, $(Format-N $out) Out, $(Format-N $cr) Cache Read)"
    } catch {
        return ""
    }
}

# Fire a Discord notification via notify.ps1 (gracefully no-ops if not configured).
function Send-Notify {
    param([string]$EventType, [string]$Task = "N/A", [string]$Url = "", [string]$Details = "", [string]$Tokens = "")
    $notifyScript = Join-Path $PSScriptRoot "notify.ps1"
    if (-not (Test-Path $notifyScript)) { return }
    try {
        & $notifyScript -Event $EventType -Task $Task -Url $Url -Details $Details -Tokens $Tokens
    } catch {
        Write-Log "Notify failed: $($_.Exception.Message)" "Yellow"
    }
}

function New-RunBacklogAdapterPrompt {
    return @"
You are running the [Project Name] backlog workflow through a non-Claude CLI adapter.

Goal: execute exactly one backlog task iteration with behavior equivalent to the Claude Code slash command /run-backlog.

Required contract:
1. Read .agents/skills/run-backlog/SKILL.md before changing files.
2. Follow that skill exactly for one iteration only.
3. Read CLAUDE.md, .agents/rules/*, the selected task file, and only the relevant code requested by the workflow.
4. If your CLI cannot spawn subagents, perform the code-reviewer, security-auditor, and qa-verifier gates in this same session by reading their instructions from .agents/agents/*.md and applying the same blocking criteria.
5. Preserve the same stop tokens and print them exactly when blocked: COMPILE_BLOCKED, PREFLIGHT_BLOCKED, REVIEW_BLOCKED, VERIFY_BLOCKED, RUNTIME_BLOCKED, or "manual intervention required".
6. Commit and push to agent/dev only when the run-backlog skill says the task is DONE. Do not create a PR.
7. Do not ask for confirmation. Work autonomously inside this repository.
8. Use English for all output, progress messages, reports, and commit messages.

Start now.
"@
}

function New-ClaudeRunBacklogPrompt {
    return @"
Execute exactly one iteration of the [Project Name] run-backlog workflow.

Required contract:
1. Read .agents/skills/run-backlog/SKILL.md before changing any files.
2. Follow that skill exactly for one iteration only.
3. Read CLAUDE.md, .agents/rules/*, the selected task file, and only the relevant code the workflow requests.
4. Spawn the code-reviewer, security-auditor, and qa-verifier subagents per the skill spec using the Agent tool.
5. Print exactly these tokens when blocked: COMPILE_BLOCKED, PREFLIGHT_BLOCKED, REVIEW_BLOCKED, VERIFY_BLOCKED, RUNTIME_BLOCKED, or "manual intervention required".
6. Commit and push to agent/dev only when the skill marks the task DONE. Do not create a PR.
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

    if ($null -eq $ToolName) { $ToolName = "" }

    if ($ToolName -in "Bash", "PowerShell", "run_shell_command") {
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
    $command = $null
    $description = $null
    if ($props -contains "command") {
        $command = [string]$Payload.command
    }
    if ($props -contains "description") {
        $description = [string]$Payload.description
    }

    if ($command) {
        $body = $command
        if ($description) {
            $body = "{0}`n# {1}" -f $body, $description
        }
        return $body
    }

    # 2. File Write Tool
    $hasFilePath = $props -contains "file_path"
    $hasContent = $props -contains "content"
    if ($hasFilePath -and $hasContent) {
        $fp = [string]$Payload.file_path
        $len = if ($Payload.content) { [string]$Payload.content.Length } else { "0" }
        return "Write {0} ({1} chars)" -f $fp, $len
    }

    # 3. File Edit Tool (Edit)
    $hasNewText = $props -contains "new_text"
    $hasNewString = $props -contains "new_string"
    $hasReplaceAll = $props -contains "replace_all"
    if ($hasFilePath -and ($hasNewText -or $hasNewString -or $hasReplaceAll)) {
        $fp = [string]$Payload.file_path
        $replaceAll = if ($hasReplaceAll) { [string]$Payload.replace_all } else { "false" }
        return "Edit {0} (replace_all={1})" -f $fp, $replaceAll
    }

    # 4. File Read Tool (Read)
    if ($hasFilePath) {
        $fp = [string]$Payload.file_path
        $offset = if ($props -contains "offset") { [string]$Payload.offset } else { "0" }
        $limit = if ($props -contains "limit") { [string]$Payload.limit } else { "0" }
        return "Read {0} (offset={1}, limit={2})" -f $fp, $offset, $limit
    }

    # 5. Agent Tool (Agent)
    $hasSubagentType = $props -contains "subagent_type"
    if ($hasSubagentType) {
        $sat = [string]$Payload.subagent_type
        $desc = if ($props -contains "description") { [string]$Payload.description } else { "" }
        return "Agent ({0}): {1}" -f $sat, $desc
    }

    # 6. ToolSearch Tool
    $hasQuery = $props -contains "query"
    $hasMaxResults = $props -contains "max_results"
    if ($hasQuery -and $hasMaxResults) {
        return "Search Tools: {0}" -f [string]$Payload.query
    }

    # 7. MCP Codegraph/Search queries
    if ($hasQuery) {
        return "Query: {0}" -f [string]$Payload.query
    }

    # 8. MCP Unity Exec Code
    $hasCode = $props -contains "code"
    if ($hasCode) {
        return "Execute code: {0}" -f [string]$Payload.code
    }

    # 9. MCP Unity Menu Item
    $hasMenuPath = $props -contains "menuPath"
    if ($hasMenuPath) {
        return "Menu: {0}" -f [string]$Payload.menuPath
    }

    # 10. General MCP port payload
    if ($Payload.PSObject.Properties.Count -eq 1 -and ($props -contains "port")) {
        return "port={0}" -f [string]$Payload.port
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

    Finish-TextLine
    $code = $LASTEXITCODE
} finally {
    Set-Content -Path $flagFile -Value $code
}
'@

    $escapedLogPath = $LogPath.Replace("'", "''")
    $escapedFlagFile = $flagFile.Replace("'", "''")
    $escapedOutputMode = $outputMode.Replace("'", "''")
    $escapedFilterPattern = $filterPattern.Replace("'", "''")
    $escapedHeaderProvider = $headerProvider.Replace("'", "''")
    $escapedHeaderWorkdir = $RepoRoot.Replace("'", "''")
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

    $bytes = [System.Text.Encoding]::Unicode.GetBytes($scriptToRun)
    $encodedCommand = [Convert]::ToBase64String($bytes)

    $process = Start-Process powershell.exe -ArgumentList "-EncodedCommand", $encodedCommand -PassThru

    Write-Host "Waiting for agent window to finish..." -ForegroundColor Gray
    while (-not (Test-Path $flagFile)) {
        if ($process.HasExited) {
            Write-Host "Agent window was closed unexpectedly before finishing!" -ForegroundColor Red
            return 1
        }
        Start-Sleep -Milliseconds 500
    }

    $exitCodeStr = Get-Content $flagFile -Raw -ErrorAction SilentlyContinue
    $exitCode = 0
    if ($exitCodeStr -match '\d+') {
        $exitCode = [int]$exitCodeStr.Trim()
    }

    Remove-Item -Path $flagFile -ErrorAction SilentlyContinue
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

$cli = Get-Command $Provider -ErrorAction SilentlyContinue
if (-not $cli) {
    Write-Log "ERROR: '$Provider' CLI not found on PATH." "Red"
    Write-Host ""
    Write-Host "Press Enter to close this window..." -ForegroundColor DarkGray
    $null = Read-Host
    exit 1
}
Write-Log "CLI:             $($cli.Source)" "Gray"

$promptFile = Join-Path $LogDir "prompt-$Provider-$timestamp.md"
Write-Log "Adapter prompt:  $promptFile" "Gray"

$iter = 0
$completedIterations = 0
$stopReason = "MaxIterations reached"

for ($iter = 1; $iter -le $MaxIterations; $iter++) {
    Write-Log ""
    Write-Log "=== Iteration $iter / $MaxIterations ===" "Cyan"

    $status = Get-BacklogStatus
    Write-Log "Backlog state: TODO=$($status.TodoCount), IN_PROGRESS=$($status.InProgressCount)" "Gray"

    if ($status.TodoCount -eq 0 -and $status.InProgressCount -eq 0) {
        $stopReason = "Backlog empty (no TODO, no IN PROGRESS)"
        Write-Log $stopReason "Green"
        Send-Notify -EventType "BACKLOG_EMPTY" -Task "N/A" -Details "All backlog tasks have been processed successfully."
        break
    }

    # Resolve current task info for notifications
    $notifyInfo = Get-NotifyTaskInfo

    $selectedThinkingBudget = $ThinkingTokens
    if ($AutoThinkingByTier) {
        $taskProfile = Get-NextBacklogTaskProfile
        $selectedThinkingBudget = Get-ThinkingBudgetForTier -Tier $taskProfile.Tier
        Write-Log "Task profile: [$($taskProfile.Tier)] $($taskProfile.Title) ($($taskProfile.State)); thinking=$selectedThinkingBudget" "Gray"
    }

    $invocation = New-AgentInvocation `
        -ProviderName $Provider `
        -RepoRootPath $RepoRoot `
        -SelectedModel $Model `
        -PromptFile $promptFile `
        -DisableSkipPermissions:$NoSkipPermissions `
        -ThinkingBudget $selectedThinkingBudget `
        -ReasoningEffortTier $ReasoningEffort

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
        if ($Provider -eq "gemini") {
            $logContent = Get-Content $iterLog -Raw -ErrorAction SilentlyContinue
            if ($logContent -match "Error: AttachConsole failed") {
                $isGeminiConsoleCrash = $true
            }
        }
        
        if ($isGeminiConsoleCrash) {
            Write-Log "Gemini CLI crashed with AttachConsole failed, ignoring non-zero exit code." "Yellow"
        } else {
            $stopReason = "$Provider exited non-zero (exit code: $exitCode). See $iterLog"
            Write-Log $stopReason "Red"
            $tokens = Get-TokenUsage -LogFile $iterLog
            Send-Notify -EventType "CLI_ERROR" -Task $notifyInfo.Title -Url $notifyInfo.Url -Details $stopReason -Tokens $tokens
            break
        }
    }

    if (Test-Blocked -LogPath $iterLog) {
        $stopReason = "Detected COMPILE_BLOCKED, PREFLIGHT_BLOCKED, REVIEW_BLOCKED, VERIFY_BLOCKED, RUNTIME_BLOCKED, or manual intervention required. See $iterLog"
        Write-Log $stopReason "Red"
        $block = Get-BlockClassification -LogPath $iterLog
        $tokens = Get-TokenUsage -LogFile $iterLog
        Send-Notify -EventType $block.Event -Task $notifyInfo.Title -Url $notifyInfo.Url -Details $block.Details -Tokens $tokens
        break
    }

    # Task passed all gates this iteration — notify success.
    $statusAfter = Get-BacklogStatus
    $doneCount = 0
    if (Test-Path "backlog/done") {
        $doneCount = (Get-ChildItem -Path "backlog/done" -Filter "*.md" -Recurse -File -ErrorAction SilentlyContinue | Measure-Object).Count
    }
    $totalCount = $statusAfter.TodoCount + $statusAfter.InProgressCount + $doneCount
    $completedDetails = "Progress: Task $doneCount of $totalCount completed successfully.`nCommitted & pushed to agent/dev. Ready for manual verify + merge."
    $tokens = Get-TokenUsage -LogFile $iterLog
    Send-Notify -EventType "TASK_COMPLETED" -Task $notifyInfo.Title -Url $notifyInfo.Url -Details $completedDetails -Tokens $tokens
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
