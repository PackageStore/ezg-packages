# Windows twin of git_prepare_scoped.sh — keep the two in lockstep (same markers, same
# NO_CHANGES contract). Stages ONLY the paths passed in, and never prints a diff.
#
# Usage:  powershell -ExecutionPolicy Bypass -File .claude/scripts/git_prepare_scoped.ps1 "path/one.cs" "path/two.prefab"
param(
    [Parameter(Mandatory = $true, ValueFromRemainingArguments = $true)]
    [string[]]$paths
)

# Nested Join-Path: PS 5.1 chỉ nhận 2 tham số, và "..\.." không phải path hợp lệ trên PowerShell
# Core ở macOS/Linux. Cách này chạy đúng trên cả ba.
$repoRoot = Resolve-Path (Join-Path (Join-Path $PSScriptRoot "..") "..")
Set-Location $repoRoot

# `git add` dies on a pathspec matching nothing, which would abort the whole run over one
# stale path. Split the list instead: stage what is real, report the rest.
$real = @()
$missing = @()
foreach ($p in $paths) {
    git ls-files --error-unmatch -- "$p" 2>&1 | Out-Null
    if ((Test-Path -LiteralPath $p) -or ($LASTEXITCODE -eq 0)) {
        $real += $p
    }
    else {
        $missing += $p
    }
}

if ($real.Count -gt 0) {
    # -A so a deleted file in the list is staged as a deletion, not silently ignored.
    git add -A -- $real
}

$staged = git diff --cached --name-status
if (-not $staged) {
    Write-Output "NO_CHANGES"
    if ($missing.Count -gt 0) {
        Write-Output "--- SKIPPED (not found) ---"
        Write-Output $missing
    }
    exit 0
}

Write-Output "--- STAGED ---"
Write-Output $staged
Write-Output "--- STAT ---"
Write-Output (git diff --cached --stat)

if ($missing.Count -gt 0) {
    Write-Output "--- SKIPPED (not found) ---"
    Write-Output $missing
}

# Index column is a space (worktree-only change) or `?` (untracked) => nothing of this file
# is staged, so it is somebody else's work: Unity auto-dirt, or the dev editing in parallel.
$others = @(git status --porcelain | Where-Object { $_ -match '^[ ?]' })
if ($others.Count -gt 0) {
    Write-Output "--- DIRTY OUTSIDE SESSION ($($others.Count)) ---"
    Write-Output ($others | Select-Object -First 20)
    if ($others.Count -gt 20) {
        Write-Output "... (+$($others.Count - 20) more, not staged)"
    }
}

Write-Output "--- REMOTE ---"
git remote get-url origin 2>&1 | Out-Null
if ($LASTEXITCODE -eq 0) { Write-Output "origin" } else { Write-Output "none" }
