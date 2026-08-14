# Registers the <gitConfigPrefix>-approve:// URL scheme on this Windows machine, pointed at
# ui-review-approve-handler.py. Run once per machine. Per-user (HKCU, no admin needed).
#
# The scheme is namespaced per project (from .claude/project-profile.json's gitConfigPrefix)
# because URL schemes are a MACHINE-WIDE registry: a dev working on two games generated from
# this template would otherwise have both register the same scheme, and whichever ran last
# would swallow the other's approvals.

$ErrorActionPreference = "Stop"

$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$handler = Join-Path $root ".claude\scripts\ui-review-approve-handler.py"

if (-not (Test-Path $handler)) {
    Write-Error "Handler not found at $handler"
    exit 1
}

$pythonCmd = Get-Command python -ErrorAction SilentlyContinue
if (-not $pythonCmd) { $pythonCmd = Get-Command python3 -ErrorAction SilentlyContinue }
if (-not $pythonCmd) {
    Write-Error "python/python3 not found on PATH"
    exit 1
}
$python = $pythonCmd.Source

function Get-ProfileValue {
    param([string]$Key, [string]$Fallback)
    try {
        $v = (& $python (Join-Path $PSScriptRoot "project_profile.py") $Key 2>$null)
        if ($LASTEXITCODE -eq 0 -and $v) { return ([string]$v).Trim() }
    } catch { }
    return $Fallback
}
$slug        = Get-ProfileValue "gitConfigPrefix" "agent"
$projectName = Get-ProfileValue "projectName"     "UnityProject"
$scheme      = "$slug-approve"

$keyPath = "HKCU:\Software\Classes\$scheme"
New-Item -Path $keyPath -Force | Out-Null
Set-ItemProperty -Path $keyPath -Name "(Default)" -Value "URL:$projectName Approve Protocol"
Set-ItemProperty -Path $keyPath -Name "URL Protocol" -Value ""

$cmdKeyPath = "$keyPath\shell\open\command"
New-Item -Path $cmdKeyPath -Force | Out-Null
Set-ItemProperty -Path $cmdKeyPath -Name "(Default)" -Value "`"$python`" `"$handler`" `"%1`""

Write-Host "OK: ${scheme}:// -> $handler (python: $python)"
Write-Host "Lan dau Windows co the hoi xac nhan mo ung dung lien ket - dong y 1 lan."
