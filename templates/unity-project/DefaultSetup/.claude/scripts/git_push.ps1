param(
    [Parameter(Mandatory = $true)]
    [string]$message
)
git commit -m "$message"
git push --no-verify
