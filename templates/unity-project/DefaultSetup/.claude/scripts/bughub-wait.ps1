# bughub-wait.ps1 - wait until the project has a BugHub bug handed to the AI (retry > 0), then exit, spending NO
# tokens: /fix-bug --watch runs this script in the background and the model only wakes up when it ends.
# macOS/Linux twin: bughub-wait.sh - keep the same arguments, env, exit codes and JSON line.
#
# Polls GET <endpoint>/v1/projects/<code>/pending (public, no token, returns counts only
# {"new":N,"retry":M,...}) every BUGHUB_POLL_INTERVAL seconds. Only `retry` (someone pressed the
# "hand back to the AI" button) wakes it up - `new` bugs wait for human triage, the server does not hand them to the
# AI (TechSpec BugHub D13).
#
#   Env:  BUGHUB_ENDPOINT        Worker base URL (default $DefaultEndpoint below)
#         BUGHUB_POLL_INTERVAL   seconds between two polls, integer >= 1 (default 60)
#         BUGHUB_WATCH_HEARTBEAT file touched on every poll (set by `bughub-watch.py start`) - so its watchdog knows
#                                the loop is waiting for bugs, not idle
#
#   Exit: 0  bug found - stdout is exactly one JSON line {"new":N,"retry":M}
#         1  (reserved for missing tools in the .sh version; this one uses built-in cmdlets)
#         2  bad arguments (missing code, extra arguments, code not matching ^[A-Za-z0-9_-]+$, invalid interval or
#            endpoint)
#         3  project does not exist on the server (404)
#   Network error / timeout / 5xx / bad payload -> keep polling (stderr is logged only when the error kind changes,
#   to avoid spam).
#
# Usage:  powershell -ExecutionPolicy Bypass -File .claude/scripts/bughub-wait.ps1 <projectCode>
#
# Saved as UTF-8 WITH BOM, and kept pure ASCII: Windows PowerShell 5.1 reads a file without BOM in the ANSI code
# page, so any non-ASCII character turns into garbage, and some bytes are even read as curly quotes that break the
# syntax.

param(
    [Parameter(Position = 0)]
    [string]$Code,
    # Not Mandatory: PowerShell would prompt for the argument instead of exiting 2 like the .sh version.
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Rest
)

$ErrorActionPreference = 'Stop'
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location $RepoRoot

$DefaultEndpoint = 'https://bug-reporter.developer-a1f.workers.dev'
$DefaultInterval = 60
$RequestTimeoutSec = 15

function Write-Err([string]$Message) { [Console]::Error.WriteLine($Message) }
function Write-Usage { Write-Err 'Usage: powershell -ExecutionPolicy Bypass -File .claude/scripts/bughub-wait.ps1 <projectCode>   (code: ^[A-Za-z0-9_-]+$)' }

if ([string]::IsNullOrEmpty($Code) -or ($Rest -and $Rest.Count -gt 0)) {
    Write-Usage
    exit 2
}
# \z instead of $: .NET's $ also matches before a trailing newline.
if ($Code -cnotmatch '^[A-Za-z0-9_-]+\z') {
    Write-Err "bughub-wait: invalid projectCode: '$Code'"
    Write-Usage
    exit 2
}

$intervalRaw = if ($env:BUGHUB_POLL_INTERVAL) { $env:BUGHUB_POLL_INTERVAL } else { "$DefaultInterval" }
if ($intervalRaw -cnotmatch '^[1-9][0-9]*\z') {
    Write-Err "bughub-wait: BUGHUB_POLL_INTERVAL must be an integer >= 1, got '$intervalRaw'"
    exit 2
}
$interval = [int]$intervalRaw

$endpoint = if ($env:BUGHUB_ENDPOINT) { $env:BUGHUB_ENDPOINT } else { $DefaultEndpoint }
$endpoint = $endpoint.TrimEnd('/')
# Only a bare base URL is accepted: https, or http for localhost (mock server in tests).
if ($endpoint -cnotmatch '^https://[A-Za-z0-9.-]+(:[0-9]+)?\z' -and $endpoint -cnotmatch '^http://(127\.0\.0\.1|localhost)(:[0-9]+)?\z') {
    Write-Err "bughub-wait: BUGHUB_ENDPOINT must be https://<host>[:port] (http only for localhost), got '$endpoint'"
    exit 2
}
$url = "$endpoint/v1/projects/$Code/pending"

Write-Err "bughub-wait: waiting for bugs of $Code ($url, every ${interval}s)"
$lastErr = ''

while ($true) {
    if ($env:BUGHUB_WATCH_HEARTBEAT) {
        try { [System.IO.File]::WriteAllText($env:BUGHUB_WATCH_HEARTBEAT, (Get-Date -Format o)) } catch { }
    }
    $err = ''
    try {
        $resp = Invoke-RestMethod -Uri $url -Method Get -TimeoutSec $RequestTimeoutSec -UseBasicParsing
        # An application/json response is already parsed; other content types (mock server) come back as a string.
        if ($resp -is [string]) { $resp = $resp | ConvertFrom-Json }
        try {
            $new = [int]$resp.new
            $retry = [int]$resp.retry
        } catch {
            $new = $null
        }
        if ($null -eq $new -or $null -eq $resp) {
            $err = 'payload is not JSON {new,retry}'
        } elseif ($retry -gt 0) {
            [Console]::Out.WriteLine((([ordered]@{ new = $new; retry = $retry }) | ConvertTo-Json -Compress))
            exit 0
        }
    } catch {
        $statusCode = $null
        $response = $_.Exception.Response
        if ($null -ne $response) {
            try { $statusCode = [int]$response.StatusCode } catch { $statusCode = $null }
        }
        if ($statusCode -eq 404) {
            Write-Err "bughub-wait: project '$Code' does not exist on the server (404) - check bugHub.projectCode / run /bughub-setup"
            exit 3
        } elseif ($null -ne $statusCode) {
            $err = "HTTP $statusCode"
        } elseif ($_.Exception -is [System.ArgumentException] -or $_.FullyQualifiedErrorId -like '*ConvertFrom*') {
            $err = 'payload is not JSON {new,retry}'
        } else {
            $err = 'cannot connect to the server'
        }
    }

    if ($err -ne $lastErr) {
        if ($err) { Write-Err "bughub-wait: $err - still polling" }
        else { Write-Err 'bughub-wait: server responding normally again' }
        $lastErr = $err
    }
    Start-Sleep -Seconds $interval
}
