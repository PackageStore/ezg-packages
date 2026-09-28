# restart-unity.ps1 — ép restart Unity Editor của project hiện tại NGAY, không hỏi, không save (Windows).
#
#   powershell -ExecutionPolicy Bypass -File .claude/skills/restart-unity/scripts/restart-unity.ps1 [-DryRun] [-NoLaunch] [-Project <path>]
#
#   -DryRun    chỉ in kế hoạch (PID sẽ kill, editor sẽ mở), không đụng gì
#   -NoLaunch  kill xong thì thôi, không mở lại
#   -Project   project root (mặc định: dò ngược từ cwd tới thư mục có ProjectSettings\ProjectVersion.txt)
#
# Luồng giống restart-unity.sh: tìm Unity.exe có -projectPath khớp → taskkill /F /T → gỡ
# Temp\UnityLockfile + dời Temp\__Backupscenes sang Logs\ → mở lại đúng Unity.exe vừa chạy (không có
# process thì lấy version trong ProjectVersion.txt → thư mục cài Unity Hub).
# Dòng cuối stdout luôn là một status: RESTARTED pid=<n> | KILLED | DRY_RUN | ERROR <lý do>.
param(
    [switch]$DryRun,
    [switch]$NoLaunch,
    [string]$Project = ""
)

# Continue: stderr của git/taskkill trên PS 5.1 không được biến thành lỗi dừng script; chỗ cần bắt lỗi dùng -ErrorAction Stop + try
$ErrorActionPreference = "Continue"

function Fail([string]$msg) { Write-Output "ERROR $msg"; exit 1 }

#region Project root
function Find-ProjectRoot([string]$dir) {
    while ($dir) {
        if (Test-Path (Join-Path $dir "ProjectSettings\ProjectVersion.txt")) { return $dir }
        $parent = Split-Path $dir -Parent
        if ($parent -eq $dir) { break }
        $dir = $parent
    }
    return $null
}

if (-not $Project) {
    $Project = Find-ProjectRoot (Get-Location).Path
    if (-not $Project) {
        $gitRoot = try { (& git rev-parse --show-toplevel 2>$null) } catch { $null }
        if ($gitRoot) { $Project = Find-ProjectRoot ($gitRoot -replace '/', '\') }
    }
    if (-not $Project) { Fail "không tìm thấy Unity project (thiếu ProjectSettings\ProjectVersion.txt) từ $((Get-Location).Path)" }
}
if (-not (Test-Path (Join-Path $Project "ProjectSettings\ProjectVersion.txt"))) { Fail "$Project không phải Unity project" }
$Project = (Resolve-Path $Project -ErrorAction Stop).Path.TrimEnd('\')
Write-Output "project: $Project"
#endregion

#region Tìm process Editor đang mở project này
function Normalize([string]$p) { return ($p -replace '/', '\').TrimEnd('\').ToLowerInvariant() }

# -GuiOnly: bỏ process -batchmode (AssetImportWorker, build CLI) — chỉ Editor GUI
function Find-EditorProcesses([switch]$GuiOnly) {
    $root = Normalize $Project
    $procs = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" -ErrorAction SilentlyContinue
    foreach ($p in $procs) {
        if (-not $p.CommandLine) { continue }
        if ($GuiOnly -and $p.CommandLine -match '(?i)\s-batchmode(\s|$)') { continue }
        # -projectPath "<path>" hoặc -projectPath <path> (Hub truyền chữ thường "-projectpath")
        $m = [regex]::Match($p.CommandLine, '-projectpath\s+(?:"([^"]+)"|(\S+))', 'IgnoreCase')
        if (-not $m.Success) { continue }
        $arg = if ($m.Groups[1].Success) { $m.Groups[1].Value } else { $m.Groups[2].Value }
        if ((Normalize $arg) -eq $root) { $p }
    }
}

$running = @(Find-EditorProcesses)
$gui = @(Find-EditorProcesses -GuiOnly) | Select-Object -First 1
$editorExe = $null
if ($running.Count -gt 0) {
    $editorExe = if ($gui) { $gui.ExecutablePath } else { $running[0].ExecutablePath }
    Write-Output ("editor đang chạy: gui=" + $(if ($gui) { $gui.ProcessId } else { "không" }) + " kill=[$($running.ProcessId -join ' ')] exe=$editorExe")
} else {
    Write-Output "editor đang chạy: không có"
}
#endregion

#region Resolve Unity.exe để mở lại
function Resolve-EditorFromVersion {
    $line = Select-String -Path (Join-Path $Project "ProjectSettings\ProjectVersion.txt") -Pattern '^m_EditorVersion:\s*(\S+)' | Select-Object -First 1
    if (-not $line) { return $null }
    $ver = $line.Matches[0].Groups[1].Value
    $bases = @()
    $sipFile = Join-Path $env:APPDATA "UnityHub\secondaryInstallPath.json"
    if (Test-Path $sipFile) {
        $sip = (Get-Content $sipFile -Raw).Trim().Trim('"')
        if ($sip) { $bases += $sip }
    }
    $bases += "$env:ProgramFiles\Unity\Hub\Editor", "$env:ProgramFiles\Unity"
    foreach ($b in $bases) {
        $cand = Join-Path $b "$ver\Editor\Unity.exe"
        if (Test-Path $cand) { return $cand }
    }
    $script:missingVersion = $ver
    return $null
}

if (-not $editorExe -or -not (Test-Path $editorExe)) {
    $editorExe = Resolve-EditorFromVersion
    if (-not $editorExe -and -not $NoLaunch) {
        Fail "không tìm thấy Unity Editor để mở (VERSION_NOT_INSTALLED:$script:missingVersion)"
    }
}
Write-Output ("editor sẽ mở: " + $(if ($NoLaunch) { "(không mở, -NoLaunch)" } else { $editorExe }))
#endregion

if ($DryRun) {
    Write-Output ("DRY_RUN kill=[" + ($running.ProcessId -join ' ') + "] launch=" + $(if ($NoLaunch) { "no" } else { "yes" }))
    exit 0
}

#region Kill (không save, không hỏi)
if ($running.Count -gt 0) {
    foreach ($p in $running) {
        try { & taskkill /F /T /PID $p.ProcessId 2>&1 | Out-Null } catch { }   # /T: kèm UPM server, shader compiler…
    }
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline) {
        $alive = @($running | Where-Object { Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue })
        if ($alive.Count -eq 0) { break }
        Start-Sleep -Milliseconds 200
    }
    if ($alive.Count -gt 0) { Fail "process $($alive.ProcessId -join ' ') vẫn sống sau taskkill /F" }
    Write-Output "killed: $($running.ProcessId -join ' ')"
}
#endregion

#region Dọn trạng thái để Editor mới mở thẳng
Remove-Item (Join-Path $Project "Temp\UnityLockfile") -Force -ErrorAction SilentlyContinue
$backup = Join-Path $Project "Temp\__Backupscenes"
if (Test-Path $backup) {
    $stash = Join-Path $Project ("Logs\restart-unity\__Backupscenes-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
    try {
        New-Item -ItemType Directory -Force -Path (Split-Path $stash -Parent) -ErrorAction Stop | Out-Null
        Move-Item $backup $stash -Force -ErrorAction Stop
        Write-Output "scene backup dời sang: $stash"
    } catch { }
}
#endregion

if ($NoLaunch) { Write-Output "KILLED"; exit 0 }

#region Mở lại
try {
    Start-Process -FilePath $editorExe -ArgumentList @("-projectPath", "`"$Project`"") -ErrorAction Stop | Out-Null
} catch { Fail "Start-Process $editorExe thất bại: $($_.Exception.Message)" }

$newProc = $null
$deadline = (Get-Date).AddSeconds(10)
while ((Get-Date) -lt $deadline) {
    $newProc = @(Find-EditorProcesses -GuiOnly) | Select-Object -First 1
    if ($newProc) { break }
    Start-Sleep -Milliseconds 200
}
if (-not $newProc) { Fail "đã gọi mở Editor nhưng không thấy process mới sau 10s" }
Write-Output "RESTARTED pid=$($newProc.ProcessId)"
#endregion
