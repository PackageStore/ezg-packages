# Windows twin of auto-clear.sh - keep the two in lockstep (same subcommands, same
# ~/.claude/auto-clear/ layout, same ARMED / NOT_ARMED / RETRY_ARMED / UNSUPPORTED / NOT_INSTALLED markers).
# Run `probe` then `type-test` once on a new machine (see SKILL.md "Cai tren may moi").
#
# KEEP THIS FILE PURE ASCII: Windows PowerShell 5.1 reads a script without BOM in the ANSI code page,
# so any non-ASCII character (Vietnamese, em dash, infinity sign...) turns into garbage or breaks parsing.
#
# Types "/clear" + Enter into the console of the Claude Code process that armed the flag, right after
# the turn ends. It targets the PROCESS, not the focused window: AttachConsole(<claude pid>) then
# WriteConsoleInput on that console's input buffer. Every Windows Terminal pane owns its own
# (pseudo)console, so the right pane is hit even while another pane has focus.
#
#   arm [--stdin] [--then "<prompt>"]
#                   arm the current Claude process; --stdin saves the final report read from stdin;
#                   --then: after "/clear", wait AUTO_CLEAR_THEN_DELAY seconds and type <prompt> + Enter
#                   (one line, no control characters, <= 200 characters - otherwise the flag is NOT armed)
#   off             disarm the current Claude process (both the /clear flag and the API-error retry flag)
#   status          is the current Claude process armed (plus the pending --then prompt, retry counter)
#   probe           attach to the Claude console and open its input buffer, WITHOUT typing anything
#   type-test       type "auto-clear type-test ok" + Enter into the current Claude console: run it from a
#                   Claude Code Bash tool call - the text must show up as a queued user message
#   hook            Stop hook: armed -> disarm -> wait AUTO_CLEAR_DELAY seconds -> type "/clear" + Enter
#                   (+ the --then prompt when the flag carries one, after AUTO_CLEAR_THEN_DELAY seconds);
#                   a normal turn end also resets the API-error retry counter to 0
#   retry-arm [--prompt "<prompt>"] [--max N]
#                   arm API-ERROR RETRY for the current Claude process: when a turn dies on an API error of
#                   Claude (org access disabled, rate limit, usage limit, overloaded, 5xx...) the StopFailure
#                   hook waits AUTO_CLEAR_RETRY_DELAY seconds then types <prompt> + Enter, at most N times in a
#                   row (default AUTO_CLEAR_RETRY_MAX; 0 = unlimited). The flag lives until retry-off / off
#   retry-off       disarm the API-error retry of the current Claude process
#   failhook        StopFailure hook (internal) - see retry-arm
#   inject <pid>    type "/clear" + Enter into the console of process <pid> (internal / testing)
#   install         copy this script to ~/.claude/auto-clear/auto-clear.ps1 and register the Stop + StopFailure
#                   hooks (async) on that copy in ~/.claude/settings.json - idempotent, with backup
#   uninstall       remove both hooks from ~/.claude/settings.json and delete the copy
#
# The hook belongs to the MACHINE (user-level settings), not to a repo: one hook serves every project
# that installs this skill. It therefore runs the copy under ~/.claude/auto-clear/ instead of pointing
# into a repo, so deleting/moving a repo cannot silently kill it. `arm` / `retry-arm` re-sync the copy.
#
# auto-clear-version: 1
#   Bump this number (same number in auto-clear.sh) whenever the hook side changes. `arm` / `retry-arm` /
#   `install` never overwrite a hook copy carrying a HIGHER number: with several projects on one machine,
#   a project still on an old skill would otherwise downgrade the shared hook on every arm (an old copy
#   whose failhook does nothing = API-error retry silently dead for every project). Scripts without the
#   marker count as version 0.
#
# Called from Git Bash (the Bash tool of Claude Code on Windows), an argument starting with "/" is rewritten
# into a Windows path ("/fix-bug --watch" -> "C:/Program Files/Git/fix-bug --watch"). arm --then and
# retry-arm --prompt undo that rewrite (only when MSYSTEM is set and the prefix is the Git root).
#
# Usage:  powershell -NoProfile -ExecutionPolicy Bypass -File .claude/skills/auto-clear/scripts/auto-clear.ps1 <subcommand>

$ErrorActionPreference = 'Stop'

$Sub = if ($args.Count -gt 0) { [string]$args[0] } else { 'status' }
$Rest = if ($args.Count -gt 1) { @($args[1..($args.Count - 1)]) } else { @() }

function Get-EnvNumber([string]$Name, [double]$Default) {
    $raw = [Environment]::GetEnvironmentVariable($Name)
    $value = 0.0
    if ($raw -and [double]::TryParse($raw, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$value) -and $value -ge 0) { return $value }
    return $Default
}

$ClaudeDir = Join-Path $HOME '.claude'
$StateDir = Join-Path $ClaudeDir 'auto-clear'
$ReportDir = Join-Path $StateDir 'reports'
$Settings = Join-Path $ClaudeDir 'settings.json'
$LogFile = Join-Path $StateDir 'auto-clear.log'
$Delay = Get-EnvNumber 'AUTO_CLEAR_DELAY' 1.0
$ThenDelay = Get-EnvNumber 'AUTO_CLEAR_THEN_DELAY' 3.0
$RetryDelay = Get-EnvNumber 'AUTO_CLEAR_RETRY_DELAY' 30.0
$RetryMaxDefault = [int](Get-EnvNumber 'AUTO_CLEAR_RETRY_MAX' 5)
$RetryPromptDefault = 'Continue: the previous turn stopped on an API error - resume the exact step in progress.'
$TypeTestText = 'auto-clear type-test ok'
$ThenMax = 200
$ScriptPath = $PSCommandPath
$HookScript = Join-Path $StateDir 'auto-clear.ps1'

$InjectSource = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace AutoClear
{
    public static class ConsoleInject
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct KEY_EVENT_RECORD
        {
            public int bKeyDown;
            public ushort wRepeatCount;
            public ushort wVirtualKeyCode;
            public ushort wVirtualScanCode;
            public char UnicodeChar;
            public uint dwControlKeyState;
        }

        [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
        struct INPUT_RECORD
        {
            [FieldOffset(0)] public ushort EventType;
            [FieldOffset(4)] public KEY_EVENT_RECORD KeyEvent;
        }

        const ushort KEY_EVENT = 1;
        const ushort VK_RETURN = 0x0D;
        const uint GENERIC_READ_WRITE = 0xC0000000;
        const uint FILE_SHARE_READ_WRITE = 3;
        const uint OPEN_EXISTING = 3;
        const int CHAR_DELAY_MS = 30;
        const int ENTER_DELAY_MS = 200;

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool FreeConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AttachConsole(uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool WriteConsoleInputW(IntPtr hConsoleInput, INPUT_RECORD[] lpBuffer, uint nLength,
            out uint lpNumberOfEventsWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern short VkKeyScanW(char ch);

        [DllImport("user32.dll")]
        static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

        static INPUT_RECORD Key(char ch, ushort vk, bool down)
        {
            INPUT_RECORD r = new INPUT_RECORD();
            r.EventType = KEY_EVENT;
            r.KeyEvent.bKeyDown = down ? 1 : 0;
            r.KeyEvent.wRepeatCount = 1;
            r.KeyEvent.wVirtualKeyCode = vk;
            r.KeyEvent.wVirtualScanCode = (ushort)MapVirtualKeyW(vk, 0);
            r.KeyEvent.UnicodeChar = ch;
            r.KeyEvent.dwControlKeyState = 0;
            return r;
        }

        static bool Press(IntPtr h, char ch, ushort vk)
        {
            INPUT_RECORD[] recs = new INPUT_RECORD[] { Key(ch, vk, true), Key(ch, vk, false) };
            uint written;
            return WriteConsoleInputW(h, recs, (uint)recs.Length, out written) && written == recs.Length;
        }

        public static string Send(int pid, string text, bool dryRun)
        {
            FreeConsole();
            if (!AttachConsole((uint)pid)) return "attach-failed err=" + Marshal.GetLastWin32Error();
            IntPtr h = CreateFileW("CONIN$", GENERIC_READ_WRITE, FILE_SHARE_READ_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == new IntPtr(-1))
            {
                int err = Marshal.GetLastWin32Error();
                FreeConsole();
                return "conin-failed err=" + err;
            }
            try
            {
                if (dryRun) return "ok-dry attached pid=" + pid;
                foreach (char c in text)
                {
                    if (!Press(h, c, (ushort)(VkKeyScanW(c) & 0xFF))) return "write-failed err=" + Marshal.GetLastWin32Error();
                    Thread.Sleep(CHAR_DELAY_MS);
                }
                Thread.Sleep(ENTER_DELAY_MS);
                if (!Press(h, '\r', VK_RETURN)) return "write-failed err=" + Marshal.GetLastWin32Error();
                return "ok";
            }
            finally
            {
                CloseHandle(h);
                FreeConsole();
            }
        }
    }
}
'@

function Write-Utf8([string]$Path, [string]$Text) {
    [System.IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding $false))
}

function Write-Log([string]$Message) {
    New-Item -ItemType Directory -Force -Path $StateDir | Out-Null
    $line = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' ' + $Message + [Environment]::NewLine
    [System.IO.File]::AppendAllText($LogFile, $line, (New-Object System.Text.UTF8Encoding $false))
}

# PID of the nearest ancestor Claude Code process: claude.exe (native) or node.exe running claude (npm).
function Get-ClaudePid {
    $procId = $PID
    for ($i = 0; $i -lt 24 -and $procId -gt 0; $i++) {
        $p = Get-CimInstance Win32_Process -Filter "ProcessId=$procId" -ErrorAction SilentlyContinue
        if (-not $p) { return $null }
        $name = ([string]$p.Name).ToLowerInvariant()
        if ($name -eq 'claude.exe') { return [int]$p.ProcessId }
        if ($name -eq 'node.exe' -and ([string]$p.CommandLine) -match 'claude') { return [int]$p.ProcessId }
        $procId = [int]$p.ParentProcessId
    }
    return $null
}

function Get-FlagPath([int]$ClaudePid) {
    return (Join-Path $StateDir ('pid-' + $ClaudePid + '.flag'))
}

function Get-RetryPath([int]$ClaudePid) {
    return (Join-Path $StateDir ('pid-' + $ClaudePid + '.retry'))
}

# Flags are keyed by PID and Windows reuses PIDs: drop the flags of Claude processes that are gone, so a
# new process that happens to get an old PID never inherits a stale /clear or retry flag.
function Remove-StaleFlags {
    if (-not (Test-Path $StateDir)) { return }
    foreach ($file in @(Get-ChildItem -Path $StateDir -File -ErrorAction SilentlyContinue)) {
        if ($file.Name -notmatch '^pid-(\d+)\.(flag|retry)$') { continue }
        $proc = Get-Process -Id ([int]$Matches[1]) -ErrorAction SilentlyContinue
        if ($proc -and @('claude', 'node') -contains ([string]$proc.ProcessName).ToLowerInvariant()) { continue }
        Remove-Item -LiteralPath $file.FullName -Force -ErrorAction SilentlyContinue
        Write-Log ('stale-flag removed ' + $file.Name)
    }
}

# Script path the hook of <HookEvent> points at ($null = not registered). Also reads old installs that point into a repo.
function Get-HookTarget([string]$HookEvent = 'Stop', [string]$SubCommand = 'hook') {
    if (-not (Test-Path $Settings)) { return $null }
    try { $data = [System.IO.File]::ReadAllText($Settings) | ConvertFrom-Json } catch { return $null }
    if (-not ($data -and $data.PSObject.Properties['hooks'] -and $data.hooks.PSObject.Properties[$HookEvent])) { return $null }
    foreach ($group in @($data.hooks.$HookEvent)) {
        foreach ($hook in @($group.hooks)) {
            if (([string]$hook.command) -match ('-File "([^"]*auto-clear\.ps1)" ' + $SubCommand + '(\s|$)')) { return $Matches[1] }
        }
    }
    return $null
}

# Registered AND the target script still exists (a hook pointing into a deleted/moved repo counts as not installed).
function Test-HookInstalled {
    $target = Get-HookTarget 'Stop' 'hook'
    return ([bool]$target -and (Test-Path $target))
}

# StopFailure hook registered too (installs older than retry-arm only had the Stop hook -> run install again).
function Test-FailHookInstalled {
    $target = Get-HookTarget 'StopFailure' 'failhook'
    return ((Test-HookInstalled) -and [bool]$target -and (Test-Path $target))
}

function Test-SamePath([string]$A, [string]$B) {
    return (($A -replace '\\', '/') -eq ($B -replace '\\', '/'))
}

# "auto-clear-version: N" marker of a script file (0 = no marker = older than the marker itself).
function Get-ScriptVersion([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return 0 }
    $m = [regex]::Match([System.IO.File]::ReadAllText($Path), '(?m)^# auto-clear-version: (\d+)\s*$')
    if ($m.Success) { return [int]$m.Groups[1].Value }
    return 0
}

# Copy this script over the hook copy unless the copy is NEWER (see "auto-clear-version" in the header).
# Returns $true when the copy now matches this script's version or newer, $false when a newer copy was kept.
function Copy-HookScript {
    if (Test-SamePath $ScriptPath $HookScript) { return $true }
    $mine = Get-ScriptVersion $ScriptPath
    $theirs = Get-ScriptVersion $HookScript
    if ((Test-Path -LiteralPath $HookScript) -and $theirs -gt $mine) {
        Write-Log ('keep hook copy v' + $theirs + ' (newer than v' + $mine + ' of ' + $ScriptPath + ')')
        return $false
    }
    Copy-Item $ScriptPath $HookScript -Force
    return $true
}

# The hooks run the copy - re-sync it with the script that is arming, so hook and flag share one version.
function Sync-HookCopy {
    if (Test-SamePath (Get-HookTarget 'Stop' 'hook') $HookScript) { [void](Copy-HookScript) }
}

# Undo the Git Bash (MSYS) rewrite of a leading "/" argument: "C:/Program Files/Git/fix-bug" -> "/fix-bug".
function Repair-MsysPrompt([string]$Text) {
    if (-not $env:MSYSTEM -or [string]::IsNullOrEmpty($Text)) { return $Text }
    # EXEPATH = <git root>\bin (string ops, not Split-Path: keep it working under pwsh on macOS/Linux CI too).
    $roots = @()
    if ($env:EXEPATH) { $roots += (($env:EXEPATH -replace '\\', '/').TrimEnd('/') -replace '/bin$', '') }
    if ($env:ProgramFiles) { $roots += ($env:ProgramFiles + '/Git') }
    foreach ($root in $roots) {
        $prefix = ($root -replace '\\', '/').TrimEnd('/')
        if ($prefix -and $Text.StartsWith($prefix + '/', [System.StringComparison]::OrdinalIgnoreCase)) {
            $fixed = $Text.Substring($prefix.Length)
            Write-Log ('msys-path repaired: ' + $Text + ' -> ' + $fixed)
            return $fixed
        }
    }
    return $Text
}

function Send-Text([int]$ClaudePid, [string]$Text, [bool]$DryRun) {
    if (-not ('AutoClear.ConsoleInject' -as [type])) { Add-Type -TypeDefinition $InjectSource }
    return [AutoClear.ConsoleInject]::Send($ClaudePid, $Text, $DryRun)
}

function Send-Clear([int]$ClaudePid, [bool]$DryRun) {
    return (Send-Text $ClaudePid '/clear' $DryRun)
}

# Prompts are stored base64 on one line so newlines / "=" cannot break the key=value flag format.
function ConvertTo-B64([string]$Text) {
    return [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($Text))
}

function ConvertFrom-B64([string]$Text) {
    try { return [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($Text)) } catch { return '' }
}

# Valid prompt: non-empty, one line, no control characters, <= ThenMax characters (code points, like
# `wc -m` in the bash twin). Returns the rejection reason, or $null when valid.
function Get-ThenError([string]$Prompt) {
    if ([string]::IsNullOrEmpty($Prompt)) { return 'INVALID_THEN: empty prompt' }
    if ($Prompt -match "[`r`n]") { return 'INVALID_THEN: prompt must be a single line' }
    if ($Prompt -match '[\x00-\x1F\x7F]') { return 'INVALID_THEN: prompt contains control characters' }
    $count = @($Prompt.ToCharArray() | Where-Object { -not [char]::IsLowSurrogate($_) }).Count
    if ($count -gt $ThenMax) { return ('INVALID_THEN: prompt is ' + $count + ' characters long (max ' + $ThenMax + ')') }
    return $null
}

function Get-FlagValue([string]$Flag, [string]$Key) {
    foreach ($line in [System.IO.File]::ReadAllLines($Flag)) {
        if ($line.StartsWith($Key + '=')) { return $line.Substring($Key.Length + 1) }
    }
    return $null
}

# Rewrite one key=value line of a flag (keeps every other line).
function Set-FlagValue([string]$Flag, [string]$Key, [string]$Value) {
    $lines = @([System.IO.File]::ReadAllLines($Flag) | Where-Object { -not $_.StartsWith($Key + '=') })
    $lines += ($Key + '=' + $Value)
    Write-Utf8 $Flag (($lines -join "`n") + "`n")
}

# Retry limit stored in a retry flag (missing / garbage -> AUTO_CLEAR_RETRY_MAX). 0 = unlimited.
function Get-RetryMax([string]$Flag) {
    $raw = Get-FlagValue $Flag 'max'
    if ($raw -match '^[0-9]+$') { return [int]$raw }
    return $RetryMaxDefault
}

function Get-MaxLabel([int]$Max) {
    if ($Max -eq 0) { return 'unlimited' }
    return [string]$Max
}

function Require-ClaudePid {
    $claudePid = Get-ClaudePid
    if (-not $claudePid) {
        # [Console]::Out, not Write-Output: the caller captures this function's output into a variable.
        [Console]::Out.WriteLine('UNSUPPORTED: no ancestor claude process found - type /clear manually')
        exit 2
    }
    return $claudePid
}

function Invoke-Arm([object[]]$Options) {
    $useStdin = $false
    $thenPrompt = $null
    for ($i = 0; $i -lt $Options.Count; $i++) {
        $opt = [string]$Options[$i]
        if ($opt -eq '--stdin') { $useStdin = $true }
        elseif ($opt -eq '--then') {
            if ($i + 1 -ge $Options.Count) { Write-Output 'INVALID_THEN: --then is missing its prompt'; exit 1 }
            $i++
            $thenPrompt = Repair-MsysPrompt ([string]$Options[$i])
        }
        else { Write-Output 'usage: auto-clear.ps1 arm [--stdin] [--then "<prompt>"]'; exit 1 }
    }
    # Validate before any side effect: a bad prompt saves no report and arms nothing.
    if ($null -ne $thenPrompt) {
        $thenError = Get-ThenError $thenPrompt
        if ($thenError) { Write-Output $thenError; exit 1 }
    }
    if (-not (Test-HookInstalled)) {
        Write-Output ('NOT_INSTALLED: Stop hook not registered (or its script no longer exists) - run: powershell -ExecutionPolicy Bypass -File "' + $ScriptPath + '" install')
        exit 3
    }
    $claudePid = Require-ClaudePid
    Sync-HookCopy
    Remove-StaleFlags
    New-Item -ItemType Directory -Force -Path $ReportDir | Out-Null
    if ($useStdin) {
        # Raw UTF-8 stdin: [Console]::In would decode with the OEM code page and mangle Vietnamese text.
        $reader = New-Object System.IO.StreamReader([Console]::OpenStandardInput(), (New-Object System.Text.UTF8Encoding $false))
        $text = $reader.ReadToEnd()
        if ($text.Trim().Length -gt 0) {
            $report = Join-Path $ReportDir ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-pid' + $claudePid + '.md')
            Write-Utf8 $report $text
            Copy-Item $report (Join-Path $StateDir 'last-report.md') -Force
            Write-Output ('REPORT ' + $report)
        }
    }
    $stamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    $flagText = "pid=$claudePid`nwt_session=$($env:WT_SESSION)`narmed_at=$stamp`n"
    if ($null -ne $thenPrompt) {
        Write-Utf8 (Get-FlagPath $claudePid) ($flagText + 'then_b64=' + (ConvertTo-B64 $thenPrompt) + "`n")
        Write-Log ('arm pid=' + $claudePid + ' wt_session=' + $env:WT_SESSION + ' then=' + $thenPrompt)
        Write-Output ('ARMED pid=' + $claudePid + ' wt_session=' + $env:WT_SESSION + ' then=' + $thenPrompt)
    }
    else {
        Write-Utf8 (Get-FlagPath $claudePid) $flagText
        Write-Log ('arm pid=' + $claudePid + ' wt_session=' + $env:WT_SESSION)
        Write-Output ('ARMED pid=' + $claudePid + ' wt_session=' + $env:WT_SESSION)
    }
}

function Invoke-Off {
    $claudePid = Require-ClaudePid
    # `off` = the dev stops the loop -> drop the API-error retry too, or a later API error types the loop prompt.
    $retry = Get-RetryPath $claudePid
    if (Test-Path $retry) {
        Remove-Item $retry -Force
        Write-Log ('retry-off pid=' + $claudePid + ' (off)')
        Write-Output ('RETRY_DISARMED pid=' + $claudePid)
    }
    $flag = Get-FlagPath $claudePid
    if (Test-Path $flag) {
        Remove-Item $flag -Force
        Write-Log ('off pid=' + $claudePid)
        Write-Output ('DISARMED pid=' + $claudePid)
    }
    else {
        Write-Output ('NOT_ARMED pid=' + $claudePid)
    }
}

function Invoke-RetryArm([object[]]$Options) {
    $prompt = $RetryPromptDefault
    $max = $RetryMaxDefault
    for ($i = 0; $i -lt $Options.Count; $i++) {
        $opt = [string]$Options[$i]
        if ($opt -eq '--prompt') {
            if ($i + 1 -ge $Options.Count) { Write-Output 'INVALID_THEN: --prompt is missing its text'; exit 1 }
            $i++
            $prompt = Repair-MsysPrompt ([string]$Options[$i])
        }
        elseif ($opt -eq '--max') {
            if ($i + 1 -ge $Options.Count -or ([string]$Options[$i + 1]) -notmatch '^[0-9]+$') {
                Write-Output 'INVALID_MAX: --max needs an integer >= 0 (0 = unlimited)'; exit 1
            }
            $i++
            $max = [int]$Options[$i]
        }
        else { Write-Output 'usage: auto-clear.ps1 retry-arm [--prompt "<prompt>"] [--max N]'; exit 1 }
    }
    $promptError = Get-ThenError $prompt
    if ($promptError) { Write-Output $promptError; exit 1 }
    if (-not (Test-FailHookInstalled)) {
        Write-Output ('NOT_INSTALLED: StopFailure hook not registered - run: powershell -ExecutionPolicy Bypass -File "' + $ScriptPath + '" install')
        exit 3
    }
    $claudePid = Require-ClaudePid
    Sync-HookCopy
    Remove-StaleFlags
    New-Item -ItemType Directory -Force -Path $StateDir | Out-Null
    $stamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    Write-Utf8 (Get-RetryPath $claudePid) ("pid=$claudePid`nwt_session=$($env:WT_SESSION)`ncount=0`nmax=$max`nprompt_b64=" + (ConvertTo-B64 $prompt) + "`narmed_at=$stamp`n")
    Write-Log ('retry-arm pid=' + $claudePid + ' max=' + (Get-MaxLabel $max) + ' prompt=' + $prompt)
    Write-Output ('RETRY_ARMED pid=' + $claudePid + ' delay=' + $RetryDelay + 's max=' + (Get-MaxLabel $max))
}

function Invoke-RetryOff {
    $claudePid = Require-ClaudePid
    $retry = Get-RetryPath $claudePid
    if (Test-Path $retry) {
        Remove-Item $retry -Force
        Write-Log ('retry-off pid=' + $claudePid)
        Write-Output ('RETRY_DISARMED pid=' + $claudePid)
    }
    else {
        Write-Output ('RETRY_NOT_ARMED pid=' + $claudePid)
    }
}

function Invoke-Status {
    $claudePid = Require-ClaudePid
    $target = Get-HookTarget 'Stop' 'hook'
    if (Test-HookInstalled) { Write-Output ('HOOK installed (' + $Settings + ' -> ' + $target + ')') }
    elseif ($target) { Write-Output ('HOOK broken: script ' + $target + ' no longer exists - run install again') }
    else { Write-Output 'HOOK not-installed' }
    if (Test-FailHookInstalled) { Write-Output 'FAILHOOK installed' } else { Write-Output 'FAILHOOK not-installed' }
    $retry = Get-RetryPath $claudePid
    if (Test-Path $retry) {
        Write-Output ('RETRY_ARMED pid=' + $claudePid + ' count=' + (Get-FlagValue $retry 'count') + '/' + (Get-MaxLabel (Get-RetryMax $retry)))
    }
    else {
        Write-Output ('RETRY_NOT_ARMED pid=' + $claudePid)
    }
    $flag = Get-FlagPath $claudePid
    if (Test-Path $flag) {
        Write-Output ('ARMED pid=' + $claudePid)
        Write-Output ([System.IO.File]::ReadAllText($flag))
        $thenB64 = Get-FlagValue $flag 'then_b64'
        if ($thenB64) { Write-Output ('THEN ' + (ConvertFrom-B64 $thenB64)) }
    }
    else {
        Write-Output ('NOT_ARMED pid=' + $claudePid)
    }
}

function Invoke-Probe {
    $claudePid = Require-ClaudePid
    Write-Output ('claude_pid=' + $claudePid + ' wt_session=' + $env:WT_SESSION)
    Write-Output (Send-Clear $claudePid $true)
}

function Invoke-TypeTest {
    $claudePid = Require-ClaudePid
    $result = Send-Text $claudePid $TypeTestText $false
    Write-Log ('type-test pid=' + $claudePid + ' -> ' + $result)
    Write-Output ('type-test pid=' + $claudePid + ' -> ' + $result)
}

function Invoke-Hook {
    # The Stop hook receives JSON on stdin - drain it so Claude Code never hits EPIPE.
    if ([Console]::IsInputRedirected) { [void][Console]::In.ReadToEnd() }
    $claudePid = Get-ClaudePid
    if (-not $claudePid) { return }
    # A normal turn end (an API error fires StopFailure, never Stop) -> the run of API errors is over.
    $retry = Get-RetryPath $claudePid
    if ((Test-Path $retry) -and (Get-FlagValue $retry 'count') -ne '0') {
        Set-FlagValue $retry 'count' '0'
        Write-Log ('retry-reset pid=' + $claudePid)
    }
    $flag = Get-FlagPath $claudePid
    if (-not (Test-Path $flag)) { return }
    $thenB64 = Get-FlagValue $flag 'then_b64'
    Remove-Item $flag -Force
    $dryRun = [bool]$env:AUTO_CLEAR_DRY
    Start-Sleep -Milliseconds ([int]($Delay * 1000))
    $result = Send-Clear $claudePid $dryRun
    Write-Log ('hook pid=' + $claudePid + ' -> ' + $result)
    if (-not $thenB64) { return }
    $thenPrompt = ConvertFrom-B64 $thenB64
    # The flag may have been hand-edited between arm and hook - re-validate before typing.
    if (Get-ThenError $thenPrompt) { Write-Log ('hook-then pid=' + $claudePid + ' skip: invalid prompt in flag'); return }
    if (-not ([string]$result).StartsWith('ok')) { Write-Log ('hook-then pid=' + $claudePid + ' skip: /clear was not typed (' + $result + ')'); return }
    # Let /clear finish so the prompt lands in a clean session; Send-Text re-attaches to the same pid's console.
    Start-Sleep -Milliseconds ([int]($ThenDelay * 1000))
    $result = Send-Text $claudePid $thenPrompt $dryRun
    Write-Log ('hook-then pid=' + $claudePid + ' prompt=' + $thenPrompt + ' -> ' + $result)
}

# StopFailure hook: the turn died on an API error. Retry flag of this Claude process present -> wait
# RetryDelay seconds, then type the prompt; more than `max` failures in a row (0 = unlimited) -> stop typing
# (the flag stays, give-up is logged).
function Invoke-FailHook {
    $errorCode = ''
    if ([Console]::IsInputRedirected) {
        $payload = [Console]::In.ReadToEnd()
        try { $errorCode = [string](($payload | ConvertFrom-Json).error) } catch { $errorCode = '' }
    }
    if (-not $errorCode) { $errorCode = '?' }
    $claudePid = Get-ClaudePid
    if (-not $claudePid) { return }
    $retry = Get-RetryPath $claudePid
    if (-not (Test-Path $retry)) { return }
    $max = Get-RetryMax $retry
    $countRaw = Get-FlagValue $retry 'count'
    $count = 1
    if ($countRaw -match '^[0-9]+$') { $count = [int]$countRaw + 1 }
    if ($max -gt 0 -and $count -gt $max) {
        Write-Log ('failhook give-up pid=' + $claudePid + ' error=' + $errorCode + ': retried ' + $max + ' times')
        return
    }
    Set-FlagValue $retry 'count' ([string]$count)
    $prompt = ConvertFrom-B64 (Get-FlagValue $retry 'prompt_b64')
    if (Get-ThenError $prompt) { Write-Log ('failhook pid=' + $claudePid + ' skip: invalid prompt in flag'); return }
    Write-Log ('failhook pid=' + $claudePid + ' error=' + $errorCode + ' try ' + $count + '/' + (Get-MaxLabel $max) + ' - wait ' + $RetryDelay + 's')
    Start-Sleep -Milliseconds ([int]($RetryDelay * 1000))
    # The dev disarmed (retry-off / off) while we waited -> do not type.
    if (-not (Test-Path $retry)) { Write-Log ('failhook pid=' + $claudePid + ' skip: retry flag removed while waiting'); return }
    $result = Send-Text $claudePid $prompt ([bool]$env:AUTO_CLEAR_DRY)
    Write-Log ('failhook pid=' + $claudePid + ' try ' + $count + ' -> ' + $result)
}

function Get-FilteredGroups($Data, [string]$HookEvent) {
    $result = @()
    if ($Data.hooks.PSObject.Properties[$HookEvent]) {
        foreach ($group in @($Data.hooks.$HookEvent)) {
            $kept = @(@($group.hooks) | Where-Object { ([string]$_.command) -notmatch 'auto-clear' })
            if ($kept.Count -gt 0) {
                $group.hooks = $kept
                $result += $group
            }
        }
    }
    # Unrolled on purpose - callers re-wrap with @(), which keeps 0/1/N groups an array.
    return $result
}

function Set-Groups($Data, [string]$HookEvent, [object[]]$Groups) {
    if ($Data.hooks.PSObject.Properties[$HookEvent]) { $Data.hooks.$HookEvent = $Groups }
    else { $Data.hooks | Add-Member -NotePropertyName $HookEvent -NotePropertyValue $Groups }
}

function Save-Settings($Data) {
    # Windows PowerShell 5.1 serializes some arrays as {"value":[...],"Count":n} because of the ETS Count
    # property on System.Array - drop that type data first (no-op on PowerShell 7).
    if ($PSVersionTable.PSVersion.Major -lt 6) { Remove-TypeData System.Array -ErrorAction SilentlyContinue }
    $json = $Data | ConvertTo-Json -Depth 64
    # Never write something we cannot read back: a broken settings.json disables every hook on the machine.
    $null = $json | ConvertFrom-Json
    $tmp = $Settings + '.tmp-auto-clear'
    Write-Utf8 $tmp ($json + "`n")
    [System.IO.File]::Copy($tmp, $Settings, $true)
    Remove-Item -LiteralPath $tmp -Force
}

function Read-Settings {
    $data = $null
    if (Test-Path $Settings) {
        $data = [System.IO.File]::ReadAllText($Settings) | ConvertFrom-Json
        Copy-Item $Settings ($Settings + '.bak-auto-clear') -Force
    }
    if ($null -eq $data) { $data = [pscustomobject]@{} }
    if (-not $data.PSObject.Properties['hooks']) { $data | Add-Member -NotePropertyName 'hooks' -NotePropertyValue ([pscustomobject]@{}) }
    return $data
}

function Invoke-Install {
    New-Item -ItemType Directory -Force -Path $StateDir | Out-Null
    if (-not (Copy-HookScript)) {
        Write-Output ('KEPT_NEWER: ' + $HookScript + ' is v' + (Get-ScriptVersion $HookScript) + ', this script is v' + (Get-ScriptVersion $ScriptPath) + ' - hooks registered on the newer copy')
    }
    $data = Read-Settings
    # Forward slashes: the hook may run through Git Bash, where backslashes inside quotes are fragile.
    $file = ($HookScript -replace '\\', '/')
    # StopFailure waits AUTO_CLEAR_RETRY_DELAY (30 s) before typing -> its timeout needs headroom.
    foreach ($spec in @(@('Stop', 'hook', 30), @('StopFailure', 'failhook', 120))) {
        $groups = @(Get-FilteredGroups $data $spec[0])
        $command = 'powershell -NoProfile -ExecutionPolicy Bypass -File "' + $file + '" ' + $spec[1]
        $entry = [pscustomobject]@{ type = 'command'; command = $command; async = $true; timeout = $spec[2] }
        $groups += [pscustomobject]@{ hooks = @($entry) }
        Set-Groups $data $spec[0] $groups
    }
    Save-Settings $data
    Write-Output ('INSTALLED Stop + StopFailure hook -> ' + $Settings + ' (backup: ' + $Settings + '.bak-auto-clear)')
}

function Invoke-Uninstall {
    if (Test-Path $HookScript) { Remove-Item $HookScript -Force }
    if (-not (Test-Path $Settings)) { Write-Output 'NOT_INSTALLED'; return }
    $data = Read-Settings
    foreach ($hookEvent in @('Stop', 'StopFailure')) { Set-Groups $data $hookEvent @(Get-FilteredGroups $data $hookEvent) }
    Save-Settings $data
    Write-Output ('UNINSTALLED Stop + StopFailure hook <- ' + $Settings)
}

switch ($Sub) {
    'arm' { Invoke-Arm $Rest }
    'off' { Invoke-Off }
    'status' { Invoke-Status }
    'probe' { Invoke-Probe }
    'type-test' { Invoke-TypeTest }
    'hook' {
        # A hook must never fail the turn: log and exit 0 whatever happens.
        try { Invoke-Hook } catch { Write-Log ('hook error: ' + $_.Exception.Message) }
        exit 0
    }
    'retry-arm' { Invoke-RetryArm $Rest }
    'retry-off' { Invoke-RetryOff }
    'failhook' {
        try { Invoke-FailHook } catch { Write-Log ('failhook error: ' + $_.Exception.Message) }
        exit 0
    }
    'inject' {
        if ($Rest.Count -lt 1) { Write-Output 'usage: auto-clear.ps1 inject <pid>'; exit 1 }
        Write-Output (Send-Clear ([int]$Rest[0]) $false)
    }
    'install' { Invoke-Install }
    'uninstall' { Invoke-Uninstall }
    default {
        Write-Output 'usage: auto-clear.ps1 {arm [--stdin] [--then "<prompt>"]|off|status|probe|type-test|hook|retry-arm [--prompt "<prompt>"] [--max N]|retry-off|failhook|inject <pid>|install|uninstall}'
        exit 1
    }
}
