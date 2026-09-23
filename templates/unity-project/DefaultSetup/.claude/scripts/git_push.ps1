param(
    [Parameter(Mandatory = $true)]
    [string]$message
)
git commit -m "$message"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# A branch with no upstream yet (e.g. `agent/dev-<base>` on its first worktree-mode push)
# makes bare `git push` die with "has no upstream branch". Publish it to origin under the
# same name and set the upstream in that case only; a tracked branch keeps plain `git push`.
# Keep in lockstep with git_push.sh.
git rev-parse --abbrev-ref --symbolic-full-name '@{u}' 2>$null | Out-Null
if ($LASTEXITCODE -eq 0) {
    git push
} else {
    git push -u origin HEAD
}
exit $LASTEXITCODE
