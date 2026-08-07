# bootstrap.ps1 - make a checkout usable by the agent system. Run it ONCE after
# cloning this repo, or after generating a project from the base template.
# Windows twin of bootstrap.sh - keep the two in lockstep (same steps, same order,
# same exit codes).
#
# WHY THIS EXISTS
# ---------------
# Three things the agent system needs cannot be shipped as files in git:
#
#   1. The link view (.claude\ <-> .agents\). Junctions are gitignored on purpose -
#      a tracked link breaks `git switch` on Windows, and a link that survives a
#      tarball extraction becomes a stale COPY rather than a link.
#   2. The backlog. It lives in the git common dir (.git\backlog\), which by
#      construction is not trackable, so a fresh clone always starts without one.
#   3. The UI kit. It is extracted from THIS project's screen-template prefabs and
#      carries a hash of them, so a kit shipped from anywhere else describes the
#      wrong game and every mockup run rejects it as stale.
#
# So all three are created here instead. Everything this script does is idempotent -
# re-running it is the supported way to repair a half-broken checkout.
#
# ONE FILE, BOTH DIRECTIONS
# -------------------------
# This repo keeps the real files in .agents\ and links .claude\ at them; the base
# template inverts that. The script globs for whichever sync-to-*.ps1 sits next to
# it rather than naming a direction, so the same source works in both places and
# the template's port step has no name to rewrite.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File .agents/scripts/bootstrap.ps1
#   powershell -ExecutionPolicy Bypass -File .claude/scripts/bootstrap.ps1   # template layout

[CmdletBinding()]
param()

# Deliberately NOT "Stop": a failing step must be reported and the remaining steps
# still attempted, so one run tells the developer everything that is wrong.
$ErrorActionPreference = "Continue"

$ScriptDir = $PSScriptRoot
$RepoRoot = Split-Path -Parent (Split-Path -Parent $ScriptDir)
Set-Location $RepoRoot

$stepFailed = $false
function Write-Fail($msg) {
    Write-Host "  ERROR: $msg" -ForegroundColor Red
    $script:stepFailed = $true
}

Write-Host "=== bootstrap ===" -ForegroundColor Cyan
Write-Host "Repo: $RepoRoot" -ForegroundColor Gray
Write-Host ""

# --- python -----------------------------------------------------------------
# Every deterministic script in the toolchain is Python. `py` (the Windows launcher)
# comes first on purpose: a stock Windows box resolves `python3` to the Microsoft
# Store stub, which EXISTS on PATH but exits 9009 without running anything. So a
# candidate only counts once it has actually printed a version.
$py = $null
foreach ($candidate in @("py", "python3", "python")) {
    if (-not (Get-Command $candidate -ErrorAction SilentlyContinue)) { continue }
    & $candidate --version 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) { $py = $candidate; break }
}
if (-not $py) { Write-Fail "no working Python on PATH - install Python 3.9+ (or disable the Store alias) and re-run." }

# --- 1. link view -----------------------------------------------------------
Write-Host "[1/4] Link view" -ForegroundColor Cyan
$syncScript = Get-ChildItem -Path $ScriptDir -Filter "sync-to-*.ps1" -File -ErrorAction SilentlyContinue |
              Select-Object -First 1
if ($syncScript) {
    & powershell -ExecutionPolicy Bypass -File $syncScript.FullName
    if ($LASTEXITCODE -ne 0) { Write-Fail "$($syncScript.Name) failed" }
} else {
    Write-Fail "no sync-to-*.ps1 next to this script - cannot create the link view"
}
Write-Host ""

# --- 2. git repository ------------------------------------------------------
# The backlog is stored inside the git common dir, so a project that has not been
# `git init`-ed yet cannot have one. A project generated from the base template is
# exactly that case, and initialising is the only sensible answer - an empty repo
# with no commits and no remote is trivially undone (remove the .git folder).
Write-Host "[2/4] Git repository" -ForegroundColor Cyan
git rev-parse --git-dir 2>&1 | Out-Null
$isRepo = ($LASTEXITCODE -eq 0)
if ($isRepo) {
    Write-Host "  OK  already a git repository" -ForegroundColor Green
} else {
    Write-Host "  NEW running 'git init' (the backlog lives in .git\ and needs one)" -ForegroundColor Yellow
    git init 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Fail "git init failed" } else { $isRepo = $true }
}
Write-Host ""

# --- 3. backlog -------------------------------------------------------------
Write-Host "[3/4] Backlog" -ForegroundColor Cyan
if ($py -and $isRepo) {
    & $py (Join-Path $ScriptDir "backlog-ops.py") init | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Fail "backlog-ops.py init failed"
    } else {
        $commonDir = (git rev-parse --git-common-dir).Trim()
        Write-Host "  OK  $commonDir\backlog" -ForegroundColor Green
    }
} else {
    Write-Fail "skipped (needs python3 + a git repository)"
}
Write-Host ""

# --- 4. UI kit --------------------------------------------------------------
# Generate only when there is nothing there: a MISSING kit blocks /ui-mockup
# outright, while a STALE one is a real diff over tracked, generated files that
# the developer should produce and review deliberately. Neither case is fatal -
# a project without UI prefabs is a perfectly good state, and bootstrap that
# reports INCOMPLETE for it would train everyone to ignore the exit code.
Write-Host "[4/4] UI kit" -ForegroundColor Cyan
$kitSync = Join-Path $ScriptDir "ui-kit-sync.py"
$kitState = ""
if ($py) {
    # --check exits 1 when the kit is stale, so the exit code is not an error
    # signal here — the JSON body is what carries the state.
    $raw = (& $py $kitSync --check 2>$null) -join "`n"
    if ($raw) {
        try { $kitState = [string]((ConvertFrom-Json $raw).state) } catch { $kitState = "" }
    }
}
switch ($kitState) {
    "fresh"        { Write-Host "  OK  kit matches the screen templates" -ForegroundColor Green }
    "no-templates" { Write-Host "  --  no screen templates in this project yet - nothing to extract" }
    "missing"      {
        Write-Host "  NEW generating the UI kit from this project's templates" -ForegroundColor Yellow
        & $py $kitSync 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Host "  WARN ui-kit-sync.py failed - run it by hand to see why" -ForegroundColor Yellow }
    }
    "stale"        { Write-Host "  WARN kit is out of date (see next steps)" -ForegroundColor Yellow }
    ""             { Write-Host "  WARN could not read the kit state (needs python3)" -ForegroundColor Yellow }
    default        { Write-Host "  WARN kit state: $kitState (see next steps)" -ForegroundColor Yellow }
}
Write-Host ""

# --- what the project still owes --------------------------------------------
# Two things bootstrap deliberately does NOT decide for the developer: the
# per-project profile values, and whether this repo has a remote at all.
Write-Host "=== next steps ===" -ForegroundColor Cyan

$profileFile = @(
    (Join-Path $RepoRoot ".agents\project-profile.json"),
    (Join-Path $RepoRoot ".claude\project-profile.json")
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $profileFile) {
    Write-Host "  - No project-profile.json - the built-in defaults in project_profile.py apply."
    Write-Host "    Add one when a path, branch or threat surface differs from the defaults."
} elseif (Select-String -Path $profileFile -Pattern '__PROJECT_NAME__|__PROJECT_SLUG__' -Quiet) {
    Write-Host "  - [ACTION] $profileFile still has __PROJECT_NAME__ placeholders - fill them in." -ForegroundColor Yellow
} else {
    Write-Host "  - Profile: $profileFile"
}

git remote get-url origin 2>&1 | Out-Null
if ($LASTEXITCODE -eq 0) {
    Write-Host "  - Remote: origin is configured; the backlog loop will push each done task."
} else {
    Write-Host "  - No 'origin' remote: the backlog loop commits locally and skips every push."
    Write-Host "    That is a supported mode - add a remote whenever you are ready."
}

if ($kitState -and $kitState -notin @("fresh", "no-templates", "missing")) {
    Write-Host "  - [ACTION] UI kit needs regenerating: python3 .agents/scripts/ui-kit-sync.py" -ForegroundColor Yellow
    Write-Host "    (it is generated from the screen templates - see .agents/skills/ui-kit/SKILL.md)"
}

Write-Host "  - Queue work with /planning-task then /add-to-backlog, and run it with /run-backlog."
Write-Host ""

if ($stepFailed) {
    Write-Host "=== bootstrap INCOMPLETE - fix the errors above and re-run ===" -ForegroundColor Red
    exit 1
}
Write-Host "=== bootstrap done ===" -ForegroundColor Cyan
exit 0
