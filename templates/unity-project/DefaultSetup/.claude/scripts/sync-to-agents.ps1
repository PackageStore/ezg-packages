# Ensure the .agents/ link views exist and point back to .claude/.
#
# Convention in this repo:
#   - .claude/ is the CANONICAL source (real files, tracked in git).
#   - .agents/ holds LINKS to .claude/ so other AI tools (Codex, Gemini, Cline...)
#     that read .agents/ keep working. No file copies, no sync needed.
#       * Windows: directory junctions (mklink /J)
#       * macOS / Linux: symlinks (ln -s)
#
# Link map (.agents/<link>  ->  .claude/<target>):
#   agents    -> agents
#   rules     -> rules
#   skills    -> skills
#   workflows -> commands     (Claude calls them "commands"; .agents calls them "workflows")
#   scripts   -> scripts
#   docs      -> docs
#
# Run this script ONCE after cloning. Links are gitignored, not stored in git
# (tracking links breaks `git switch` on Windows). Editing files under .claude/
# is reflected through the links instantly — no need to re-run.
#
# Usage (Windows PowerShell or cross-platform pwsh):
#   powershell -ExecutionPolicy Bypass -File .claude/scripts/sync-to-agents.ps1
#   pwsh .claude/scripts/sync-to-agents.ps1
# macOS / Linux without PowerShell — use the shell companion:
#   bash .claude/scripts/sync-to-agents.sh

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$RepoRoot = Split-Path -Parent $RepoRoot
Set-Location $RepoRoot

# $IsWindows is auto-defined on PowerShell Core; on Windows PowerShell 5.x it is $null -> treat as Windows.
$onWindows = -not (Test-Path Variable:\IsWindows) -or $IsWindows

# Name = link created under .agents\ ; Target = directory under .claude\
$links = @(
    @{ Name = "agents";    Target = "agents" },
    @{ Name = "rules";     Target = "rules" },
    @{ Name = "skills";    Target = "skills" },
    @{ Name = "workflows"; Target = "commands" },
    @{ Name = "scripts";   Target = "scripts" },
    @{ Name = "docs";      Target = "docs" }
)

Write-Host "=== .agents link check ===" -ForegroundColor Cyan
Write-Host "Repo: $RepoRoot  (platform: $(if ($onWindows) { 'Windows junction' } else { 'POSIX symlink' }))" -ForegroundColor Gray
Write-Host ""

if (-not (Test-Path ".agents")) { New-Item -ItemType Directory -Path ".agents" | Out-Null }

$allOk = $true

foreach ($j in $links) {
    $linkRel    = Join-Path ".agents" $j.Name
    $linkFull   = Join-Path $RepoRoot $linkRel
    $targetRel  = Join-Path ".claude" $j.Target
    $targetFull = Join-Path $RepoRoot $targetRel

    if (-not (Test-Path $targetFull)) {
        Write-Host "  ERROR: target $targetRel does not exist" -ForegroundColor Red
        $allOk = $false
        continue
    }

    $item = Get-Item -LiteralPath $linkFull -ErrorAction SilentlyContinue
    $isLink = $item -and ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint)
    $correctTarget = $isLink -and ($item.Target -eq $targetFull -or $item.Target -eq (Join-Path ".." $targetRel))

    if ($correctTarget) {
        Write-Host "  OK  $linkRel -> $targetRel" -ForegroundColor Green
        continue
    }

    if ($item) {
        if ($isLink) {
            Write-Host "  FIX $linkRel (wrong target - recreating)" -ForegroundColor Yellow
        } else {
            Write-Host "  FIX $linkRel (plain directory - replacing with link)" -ForegroundColor Yellow
        }
        if ($onWindows) { cmd /c "rmdir `"$linkFull`"" | Out-Null }
        else            { Remove-Item -LiteralPath $linkFull -Force -Recurse }
    } else {
        Write-Host "  NEW $linkRel -> $targetRel" -ForegroundColor Yellow
    }

    $ok = $false
    if ($onWindows) {
        cmd /c "mklink /J `"$linkFull`" `"$targetFull`"" | Out-Null
        $ok = ($LASTEXITCODE -eq 0)
    } else {
        # Relative symlink so it stays valid if the repo is moved.
        New-Item -ItemType SymbolicLink -Path $linkFull -Value (Join-Path ".." $targetRel) -ErrorAction SilentlyContinue | Out-Null
        $ok = Test-Path $linkFull
    }

    if ($ok) {
        Write-Host "  OK  $linkRel -> $targetRel" -ForegroundColor Green
    } else {
        Write-Host "  ERROR: failed to create link $linkRel" -ForegroundColor Red
        $allOk = $false
    }
}

Write-Host ""
if ($allOk) {
    Write-Host "=== Done - all links OK ===" -ForegroundColor Cyan
} else {
    Write-Host "=== Done - some links failed (see errors above) ===" -ForegroundColor Red
    exit 1
}
exit 0
