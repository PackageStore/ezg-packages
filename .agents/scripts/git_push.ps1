param(
    [Parameter(Mandatory=$true)]
    [string]$CommitMessage
)

git commit -m $CommitMessage
git push
