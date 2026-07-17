# Registers the ezg-ui-approve:// URL scheme on this Windows machine, pointed at
# ui-review-approve-handler.py. Run once per machine. Per-user (HKCU, no admin needed).

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

$keyPath = "HKCU:\Software\Classes\ezg-ui-approve"
New-Item -Path $keyPath -Force | Out-Null
Set-ItemProperty -Path $keyPath -Name "(Default)" -Value "URL:EZG UI Approve Protocol"
Set-ItemProperty -Path $keyPath -Name "URL Protocol" -Value ""

$cmdKeyPath = "$keyPath\shell\open\command"
New-Item -Path $cmdKeyPath -Force | Out-Null
Set-ItemProperty -Path $cmdKeyPath -Name "(Default)" -Value "`"$python`" `"$handler`" `"%1`""

Write-Host "OK: ezg-ui-approve:// -> $handler (python: $python)"
Write-Host "Lan dau Windows co the hoi xac nhan mo ung dung lien ket - dong y 1 lan."
