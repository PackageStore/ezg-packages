# unity-lite.ps1 - switch the Unity Editor of this project between LITE (low RAM) and NORMAL mode.
# Only touches things git does not track: launch flags, UserSettings/*.  ASCII only (PowerShell 5.1).
#
#   unity-lite.ps1 status            mode on file + what the running Editor really uses
#   unity-lite.ps1 on                lite: close Editor, apply lite settings, reopen with lite flags
#   unity-lite.ps1 off               normal: close Editor, restore settings, reopen without flags
#   unity-lite.ps1 open              open the Editor in the saved mode if it is not running (never closes it)
#   unity-lite.ps1 trim              give back RAM of idle Editors of this project (and its *_clone_N) to Windows
# Options: -Project <path>  -NoLaunch (only change settings)  -Force (kill if the Editor will not close)
#          -NoWait (do not wait for the Editor to finish loading)  -WaitSec <n> (default 300)
param([Parameter(Position = 0)][ValidateSet('status', 'on', 'off', 'open', 'trim')][string]$Cmd = 'status',
      [string]$Project = "", [switch]$NoLaunch, [switch]$Force, [switch]$NoWait, [int]$WaitSec = 300)
$ErrorActionPreference = 'Stop'

# ---------- lite profile (values only; RAM numbers are in SKILL.md) ----------
$LiteFlags = @('--burst-disable-compilation', '-force-d3d11')
$LiteUser = @{ indexOnEditorStartup = 'false'; m_DesiredImportWorkerCount = '1'; m_StandbyImportWorkerCount = '0' }

function Find-Root([string]$start) {
  $d = (Resolve-Path $start).Path
  while ($d) { if (Test-Path (Join-Path $d 'ProjectSettings\ProjectVersion.txt')) { return $d }; $d = Split-Path -Parent $d }
  throw "ERROR NO_PROJECT: no ProjectSettings/ProjectVersion.txt above $start"
}
$root = if ($Project) { Find-Root $Project } else { Find-Root (Get-Location).Path }
$norm = { param($p) $p.Replace('/', '\').TrimEnd('\').ToLowerInvariant() }
$rootN = & $norm $root
$userDir = Join-Path $root 'UserSettings'
$stateFile = Join-Path $userDir 'UnityLite.json'
$searchFile = Join-Path $userDir 'Search.settings'
$eusFile = Join-Path $userDir 'EditorUserSettings.asset'

function Get-ProjArg([string]$cmdline) {
  if ($cmdline -match '(?i)-projectpath\s+(?:"([^"]+)"|(\S+))') { if ($matches[1]) { return $matches[1] } else { return $matches[2] } }
  return $null
}
function Get-Editors([string]$which) {
  # $which: 'self' = this project, 'family' = this project + its *_clone_N folders
  Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object {
    $cl = $_.CommandLine; if (-not $cl -or $cl -match '(?i)AssetImportWorker|-batchmode') { return $false }
    $pa = Get-ProjArg $cl; if (-not $pa) { return $false }; $pn = & $norm $pa
    if ($which -eq 'family') { return ($pn -eq $rootN -or $pn -match ('^' + [regex]::Escape($rootN) + '_clone_\d+$')) }
    return $pn -eq $rootN
  }
}
function Read-State {
  if (Test-Path $stateFile) { try { return (Get-Content $stateFile -Raw | ConvertFrom-Json) } catch {} }
  return $null
}
function Get-UserValue([string]$key) {
  if ($key -eq 'indexOnEditorStartup') {
    if (-not (Test-Path $searchFile)) { return $null }
    $m = Select-String -Path $searchFile -Pattern 'indexOnEditorStartup = (\w+)' | Select-Object -First 1
    if ($m) { return $m.Matches[0].Groups[1].Value } else { return $null }
  }
  if (-not (Test-Path $eusFile)) { return $null }
  $m = Select-String -Path $eusFile -Pattern ("  " + $key + ": (\d+)") | Select-Object -First 1
  if ($m) { return $m.Matches[0].Groups[1].Value } else { return $null }
}
function Set-UserValue([string]$key, [string]$val) {
  if ($null -eq $val) { return }
  if ($key -eq 'indexOnEditorStartup') {
    if (-not (Test-Path $searchFile)) { "  skip $key (no Search.settings yet)"; return }
    $t = [IO.File]::ReadAllText($searchFile); $t2 = [regex]::Replace($t, 'indexOnEditorStartup = \w+', "indexOnEditorStartup = $val")
    if ($t2 -ne $t) { [IO.File]::WriteAllText($searchFile, $t2) }; return
  }
  if (-not (Test-Path $eusFile)) { "  skip $key (no EditorUserSettings.asset yet)"; return }
  $t = [IO.File]::ReadAllText($eusFile); $t2 = [regex]::Replace($t, ("(  " + $key + ": )\d+"), ('${1}' + $val))
  if ($t2 -ne $t) { [IO.File]::WriteAllText($eusFile, $t2) }
}
function Find-UnityExe {
  $run = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.ExecutablePath } | Select-Object -First 1
  $ver = ((Get-Content (Join-Path $root 'ProjectSettings\ProjectVersion.txt')) | Where-Object { $_ -match '^m_EditorVersion: ' }) -replace '^m_EditorVersion: ', ''
  $bases = @()
  $sec = Join-Path $env:APPDATA 'UnityHub\secondaryInstallPath.json'
  if (Test-Path $sec) { try { $bases += (Get-Content $sec -Raw | ConvertFrom-Json) } catch {} }
  $bases += 'C:\Program Files\Unity\Hub\Editor'
  foreach ($b in $bases) { if ($b) { $p = Join-Path $b "$ver\Editor\Unity.exe"; if (Test-Path $p) { return $p } } }
  if ($run -and $run.ExecutablePath -match [regex]::Escape($ver)) { return $run.ExecutablePath }
  throw "ERROR VERSION_NOT_INSTALLED:$ver"
}
function Close-Editor {
  $eds = @(Get-Editors 'self'); if (-not $eds.Count) { return $true }
  foreach ($e in $eds) {
    $p = Get-Process -Id $e.ProcessId -ErrorAction SilentlyContinue; if (-not $p) { continue }
    Write-Host "Closing Editor pid $($p.Id) ..."
    $null = $p.CloseMainWindow(); $t = 0
    while (-not $p.HasExited -and $t -lt 45) { Start-Sleep 1; $t++ }
    if (-not $p.HasExited) {
      if (-not $Force) { Write-Host "ERROR EDITOR_WONT_CLOSE pid=$($p.Id): a dialog (unsaved scene?) is open. Save/close it, or rerun with -Force (kills, unsaved changes lost)."; return $false }
      & taskkill.exe /F /T /PID $p.Id | Out-Null; Start-Sleep 2
    }
  }
  # kill leftover import workers of this project, drop lockfile
  Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -match '(?i)AssetImportWorker' -and (Get-ProjArg $_.CommandLine) -and ((& $norm (Get-ProjArg $_.CommandLine)) -eq $rootN) } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
  Remove-Item (Join-Path $root 'Temp\UnityLockfile') -Force -ErrorAction SilentlyContinue
  return $true
}
function Launch([string]$mode) {
  $exe = Find-UnityExe
  New-Item -ItemType Directory -Force (Join-Path $root 'Logs') | Out-Null
  $log = Join-Path $root "Logs\unity-$mode.log"
  Remove-Item $log -ErrorAction SilentlyContinue
  $flags = if ($mode -eq 'lite') { $LiteFlags -join ' ' } else { '' }
  $argline = "$flags -ignoreCompilerErrors -projectPath `"$root`" -logFile `"$log`"".Trim()
  $p = Start-Process -FilePath $exe -ArgumentList $argline -PassThru
  "Started $mode Editor pid $($p.Id) (log $log)"
  if ($NoWait) { "OPENED pid=$($p.Id) mode=$mode"; return }
  $sw = [Diagnostics.Stopwatch]::StartNew(); $port = $null; $ready = $false
  while (-not $p.HasExited -and $sw.Elapsed.TotalSeconds -lt $WaitSec) {
    if (Test-Path $log) {
      if (-not $port) { $m = Select-String -Path $log -Pattern 'AB-UMCP\] Server started on port (\d+)' | Select-Object -Last 1; if ($m) { $port = $m.Matches[0].Groups[1].Value } }
      if (Select-String -Path $log -Pattern 'Loaded scene ' -Quiet) { $ready = $true; break }
    }
    Start-Sleep -Milliseconds 700
  }
  if (Test-Path $log) { if (Select-String -Path $log -Pattern 'Scripts have compiler errors' -Quiet) { "WARNING compiler errors (see log)" } }
  if (Test-Path $log) { if (Select-String -Path $log -Pattern 'Burst was previously disabled and is now enabled' -Quiet) { "NOTE Burst back on: Unity recompiles all scripts once (clean build)" } }
  if ($ready) { "READY pid=$($p.Id) mode=$mode port=$port after {0:N0}s" -f $sw.Elapsed.TotalSeconds }
  elseif ($p.HasExited) { "ERROR EDITOR_EXITED code=$($p.ExitCode) (see log)" }
  else { "STILL_LOADING pid=$($p.Id) mode=$mode port=$port after {0:N0}s (big reimport/compile?)" -f $sw.Elapsed.TotalSeconds }
}
function Show-Status {
  $st = Read-State; $mode = if ($st) { $st.mode } else { 'normal' }
  "project: $root"
  "saved mode: $mode"
  "settings: indexOnEditorStartup=$(Get-UserValue 'indexOnEditorStartup') desiredImportWorkers=$(Get-UserValue 'm_DesiredImportWorkerCount') standbyImportWorkers=$(Get-UserValue 'm_StandbyImportWorkerCount')"
  $eds = @(Get-Editors 'family')
  if (-not $eds.Count) { "editor: NOT_RUNNING" }
  foreach ($e in $eds) {
    $p = Get-Process -Id $e.ProcessId -ErrorAction SilentlyContinue; if (-not $p) { continue }
    $burst = if ($e.CommandLine -match 'burst-disable-compilation') { 'off' } else { 'ON' }
    $gfx = if ($e.CommandLine -match '(?i)-force-d3d11') { 'd3d11' } else { 'default' }
    "editor: pid {0} {1} | burst {2} | gfx {3} | RAM {4:N0} MB / commit {5:N0} MB" -f $e.ProcessId, (Get-ProjArg $e.CommandLine), $burst, $gfx, ($p.WorkingSet64/1MB), ($p.PrivateMemorySize64/1MB)
  }
}

switch ($Cmd) {
  'status' { Show-Status }
  'trim' {
    Add-Type -Name P -Namespace UnityLite -MemberDefinition '[DllImport("psapi.dll")] public static extern bool EmptyWorkingSet(System.IntPtr h);'
    $eds = @(Get-Editors 'family'); if (-not $eds.Count) { "NOT_RUNNING"; break }
    foreach ($e in $eds) {
      $all = Get-CimInstance Win32_Process -Filter "ParentProcessId=$($e.ProcessId)"
      foreach ($id in @($e.ProcessId) + @($all | ForEach-Object { $_.ProcessId })) {
        $g = Get-Process -Id $id -ErrorAction SilentlyContinue; if (-not $g) { continue }
        $b = $g.WorkingSet64; try { [UnityLite.P]::EmptyWorkingSet($g.Handle) | Out-Null } catch { continue }; $g.Refresh()
        if ($id -eq $e.ProcessId) { "trim pid {0} ({1}): {2:N0} -> {3:N0} MB" -f $id, (Get-ProjArg $e.CommandLine), ($b/1MB), ($g.WorkingSet64/1MB) }
      }
    }
    "TRIMMED (commit unchanged; pages come back on the next MCP call)"
  }
  'open' {
    $eds = @(Get-Editors 'self'); if ($eds.Count) { "ALREADY_RUNNING pid=$($eds[0].ProcessId)"; break }
    $st = Read-State; $mode = if ($st -and $st.mode -eq 'lite') { 'lite' } else { 'normal' }
    Launch $mode
  }
  { $_ -eq 'on' -or $_ -eq 'off' } {
    $want = if ($Cmd -eq 'on') { 'lite' } else { 'normal' }
    $st = Read-State
    $running = @(Get-Editors 'self')
    if ($running.Count -and -not (Close-Editor)) { exit 1 }
    if ($want -eq 'lite') {
      if (-not $st -or $st.mode -ne 'lite') {
        $orig = @{}; foreach ($k in $LiteUser.Keys) { $orig[$k] = Get-UserValue $k }
        New-Item -ItemType Directory -Force $userDir | Out-Null
        (@{ mode = 'lite'; since = (Get-Date).ToString('s'); orig = $orig } | ConvertTo-Json) | Set-Content $stateFile -Encoding ascii
      }
      foreach ($k in $LiteUser.Keys) { Set-UserValue $k $LiteUser[$k] }
      "Mode = LITE (flags: $($LiteFlags -join ' '); UserSettings: search index on startup off, 1 import worker, 0 standby)"
    } else {
      if ($st -and $st.orig) { foreach ($k in $LiteUser.Keys) { $v = $st.orig.$k; if ($null -ne $v) { Set-UserValue $k ([string]$v) } } }
      if (Test-Path $stateFile) { Remove-Item $stateFile -Force }
      "Mode = NORMAL (settings restored)"
    }
    if (-not $NoLaunch) { Launch $want } else { "NOT_LAUNCHED (next: unity-lite.ps1 open)" }
  }
}
