git add .
$status = git status -s
$stat = git diff --cached --stat
$diff = git diff --cached -- | Select-Object -First 80
if (-not $status) {
    Write-Output "NO_CHANGES"
}
else {
    Write-Output "--- STATUS ---"
    Write-Output $status
    Write-Output "--- STAT ---"
    Write-Output $stat
    Write-Output "--- DIFF (first 80 lines) ---"
    Write-Output $diff
}
