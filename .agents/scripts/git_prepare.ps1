git add -A
$status = git status --porcelain
if ([string]::IsNullOrEmpty($status)) {
    Write-Output "NO_CHANGES"
    exit 0
}

Write-Output "--- STAT ---"
git diff --cached --stat

Write-Output "--- DIFF (first 80 lines) ---"
git diff --cached | Select-Object -First 80
