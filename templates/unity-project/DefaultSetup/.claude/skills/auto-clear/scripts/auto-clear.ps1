# Windows twin of auto-clear.sh - keep the two in lockstep (same subcommands, same
# ~/.claude/auto-clear/ layout, same ARMED / NOT_ARMED / UNSUPPORTED / NOT_INSTALLED markers).
# EXPERIMENTAL: not yet verified on a real Windows machine - run `probe` first.
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
#   off             disarm the current Claude process
#   status          is the current Claude process armed (plus the pending --then prompt, if any)
#   probe           attach to the Claude console and open its input buffer, WITHOUT typing anything
#   hook            Stop hook: armed -> disarm -> wait AUTO_CLEAR_DELAY seconds -> type "/clear" + Enter
#                   (+ the --then prompt when the flag carries one, after AUTO_CLEAR_THEN_DELAY seconds)
#   inject <pid>    type "/clear" + Enter into the console of process <pid> (internal / testing)
#   install         copy this script to ~/.claude/auto-clear/auto-clear.ps1 and register the Stop hook
#                   (async) on that copy in ~/.claude/settings.json - idempotent, with backup
#   uninstall       remove the Stop hook from ~/.claude/settings.json and delete the copy
#
# The hook belongs to the MACHINE (user-level settings), not to a repo: one hook serves every project
# that installs this skill. It therefore runs the copy under ~/.claude/auto-clear/ instead of pointing
# into a repo, so deleting/moving a repo cannot silently kill it. `arm` re-syncs the copy every time.
#
# Usage:  powershell -ExecutionPolicy Bypass -File .claude/skills/auto-clear/scripts/auto-clear.ps1 <subcommand>

$ErrorActionPreference = 'Stop'

$Sub = if ($args.Count -gt 0) { [string]$args[0] } else { 'status' }
$Rest = if ($args.Count -gt 1) { @($args[1..($args.Count - 1)]) } else { @() }

$ClaudeDir = Join-Path $HOME '.claude'
$StateDir = Join-Path $ClaudeDir 'auto-clear'
$ReportDir = Join-Path $StateDir 'reports'
$Settings = Join-Path $ClaudeDir 'settings.json'
$LogFile = Join-Path $StateDir 'auto-clear.log'
$Delay = if ($env:AUTO_CLEAR_DELAY) { [double]$env:AUTO_CLEAR_DELAY } else { 1.0 }
$ThenDelay = if ($env:AUTO_CLEAR_THEN_DELAY) { [double]$env:AUTO_CLEAR_THEN_DELAY } else { 3.0 }
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

# Script path the Stop hook points at ($null = not registered). Also reads old installs that point into a repo.
function Get-HookTarget {
    if (-not (Test-Path $Settings)) { return $null }
    try { $data = [System.IO.File]::ReadAllText($Settings) | ConvertFrom-Json } catch { return $null }
    if (-not ($data -and $data.PSObject.Properties['hooks'] -and $data.hooks.PSObject.Properties['Stop'])) { return $null }
    foreach ($group in @($data.hooks.Stop)) {
        foreach ($hook in @($group.hooks)) {
            if (([string]$hook.command) -match '-File "([^"]*auto-clear\.ps1)"') { return $Matches[1] }
        }
    }
    return $null
}

# Registered AND the target script still exists (a hook pointing into a deleted/moved repo counts as not installed).
function Test-HookInstalled {
    $target = Get-HookTarget
    return ([bool]$target -and (Test-Path $target))
}

function Test-SamePath([string]$A, [string]$B) {
    return (($A -replace '\\', '/') -eq ($B -replace '\\', '/'))
}

function Send-Text([int]$ClaudePid, [string]$Text, [bool]$DryRun) {
    if (-not ('AutoClear.ConsoleInject' -as [type])) { Add-Type -TypeDefinition $InjectSource }
    return [AutoClear.ConsoleInject]::Send($ClaudePid, $Text, $DryRun)
}

function Send-Clear([int]$ClaudePid, [bool]$DryRun) {
    return (Send-Text $ClaudePid '/clear' $DryRun)
}

# The --then prompt is stored base64 on one line so newlines / "=" cannot break the key=value flag format.
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
            $thenPrompt = [string]$Options[$i]
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
    # The hook runs the copy - re-sync it with the script that is arming, so hook and flag share one version.
    if ((Test-SamePath (Get-HookTarget) $HookScript) -and -not (Test-SamePath $ScriptPath $HookScript)) {
        Copy-Item $ScriptPath $HookScript -Force
    }
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

function Invoke-Status {
    $claudePid = Require-ClaudePid
    $target = Get-HookTarget
    if (Test-HookInstalled) { Write-Output ('HOOK installed (' + $Settings + ' -> ' + $target + ')') }
    elseif ($target) { Write-Output ('HOOK broken: script ' + $target + ' no longer exists - run install again') }
    else { Write-Output 'HOOK not-installed' }
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

function Invoke-Hook {
    # The Stop hook receives JSON on stdin - drain it so Claude Code never hits EPIPE.
    if ([Console]::IsInputRedirected) { [void][Console]::In.ReadToEnd() }
    $claudePid = Get-ClaudePid
    if (-not $claudePid) { return }
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

function Get-FilteredStopGroups($Data) {
    $result = @()
    if ($Data.hooks.PSObject.Properties['Stop']) {
        foreach ($group in @($Data.hooks.Stop)) {
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

function Save-StopGroups($Data, [object[]]$Groups) {
    if ($Data.hooks.PSObject.Properties['Stop']) { $Data.hooks.Stop = $Groups }
    else { $Data.hooks | Add-Member -NotePropertyName 'Stop' -NotePropertyValue $Groups }
    Write-Utf8 $Settings (($Data | ConvertTo-Json -Depth 64) + "`n")
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
    if (-not (Test-SamePath $ScriptPath $HookScript)) { Copy-Item $ScriptPath $HookScript -Force }
    $data = Read-Settings
    $groups = @(Get-FilteredStopGroups $data)
    # Forward slashes: the hook may run through Git Bash, where backslashes inside quotes are fragile.
    $command = 'powershell -NoProfile -ExecutionPolicy Bypass -File "' + ($HookScript -replace '\\', '/') + '" hook'
    $entry = [pscustomobject]@{ type = 'command'; command = $command; async = $true; timeout = 30 }
    $groups += [pscustomobject]@{ hooks = @($entry) }
    Save-StopGroups $data $groups
    Write-Output ('INSTALLED Stop hook -> ' + $Settings + ' (backup: ' + $Settings + '.bak-auto-clear)')
}

function Invoke-Uninstall {
    if (Test-Path $HookScript) { Remove-Item $HookScript -Force }
    if (-not (Test-Path $Settings)) { Write-Output 'NOT_INSTALLED'; return }
    $data = Read-Settings
    Save-StopGroups $data @(Get-FilteredStopGroups $data)
    Write-Output ('UNINSTALLED Stop hook <- ' + $Settings)
}

switch ($Sub) {
    'arm' { Invoke-Arm $Rest }
    'off' { Invoke-Off }
    'status' { Invoke-Status }
    'probe' { Invoke-Probe }
    'hook' {
        # A hook must never fail the turn: log and exit 0 whatever happens.
        try { Invoke-Hook } catch { Write-Log ('hook error: ' + $_.Exception.Message) }
        exit 0
    }
    'inject' {
        if ($Rest.Count -lt 1) { Write-Output 'usage: auto-clear.ps1 inject <pid>'; exit 1 }
        Write-Output (Send-Clear ([int]$Rest[0]) $false)
    }
    'install' { Invoke-Install }
    'uninstall' { Invoke-Uninstall }
    default {
        Write-Output 'usage: auto-clear.ps1 {arm [--stdin] [--then "<prompt>"]|off|status|probe|hook|inject <pid>|install|uninstall}'
        exit 1
    }
}
