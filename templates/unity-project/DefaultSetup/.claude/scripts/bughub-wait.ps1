# bughub-wait.ps1 — chờ tới khi project có bug BugHub đã giao cho AI (retry > 0) rồi thoát, KHÔNG tốn
# token: /fix-bug --watch chạy script này ở background, model chỉ thức dậy khi nó kết thúc.
# Twin macOS/Linux: bughub-wait.sh — giữ cùng tham số, cùng env, cùng exit code, cùng dòng JSON.
#
# Poll GET <endpoint>/v1/projects/<code>/pending (public, không token, chỉ trả số đếm
# {"new":N,"retry":M,…}) mỗi BUGHUB_POLL_INTERVAL giây. Chỉ `retry` (người đã bấm "Giao lại cho AI")
# đánh thức — bug `new` chờ người duyệt, server không giao cho AI (TechSpec BugHub D13).
#
#   Env:  BUGHUB_ENDPOINT        gốc URL Worker (mặc định $DefaultEndpoint bên dưới)
#         BUGHUB_POLL_INTERVAL   giây giữa hai lần poll, số nguyên ≥ 1 (mặc định 60)
#
#   Exit: 0  có bug — stdout đúng một dòng JSON {"new":N,"retry":M}
#         1  (dành cho thiếu công cụ ở bản .sh; bản này dùng cmdlet có sẵn)
#         2  sai tham số (thiếu code, thừa tham số, code lệch ^[A-Za-z0-9_-]+$, interval hoặc endpoint không hợp lệ)
#         3  project không tồn tại trên server (404)
#   Lỗi mạng / timeout / 5xx / payload hỏng → poll tiếp (chỉ log stderr khi loại lỗi đổi, tránh spam).
#
# Usage:  powershell -ExecutionPolicy Bypass -File .claude/scripts/bughub-wait.ps1 <projectCode>
#
# File lưu UTF-8 CÓ BOM: PowerShell 5.1 đọc file không BOM theo ANSI → chuỗi tiếng Việt thành ký tự
# rác, có byte còn bị hiểu thành dấu nháy cong và làm vỡ cú pháp.

param(
    [Parameter(Position = 0)]
    [string]$Code,
    # Không dùng Mandatory: PowerShell sẽ hỏi tham số thay vì thoát 2 như bản .sh.
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
# \z thay cho $: $ của .NET còn khớp trước một ký tự xuống dòng cuối chuỗi.
if ($Code -cnotmatch '^[A-Za-z0-9_-]+\z') {
    Write-Err "bughub-wait: projectCode không hợp lệ: '$Code'"
    Write-Usage
    exit 2
}

$intervalRaw = if ($env:BUGHUB_POLL_INTERVAL) { $env:BUGHUB_POLL_INTERVAL } else { "$DefaultInterval" }
if ($intervalRaw -cnotmatch '^[1-9][0-9]*\z') {
    Write-Err "bughub-wait: BUGHUB_POLL_INTERVAL phải là số nguyên ≥ 1, nhận '$intervalRaw'"
    exit 2
}
$interval = [int]$intervalRaw

$endpoint = if ($env:BUGHUB_ENDPOINT) { $env:BUGHUB_ENDPOINT } else { $DefaultEndpoint }
$endpoint = $endpoint.TrimEnd('/')
# Chỉ nhận gốc URL thuần: https, hoặc http cho localhost (mock server khi test).
if ($endpoint -cnotmatch '^https://[A-Za-z0-9.-]+(:[0-9]+)?\z' -and $endpoint -cnotmatch '^http://(127\.0\.0\.1|localhost)(:[0-9]+)?\z') {
    Write-Err "bughub-wait: BUGHUB_ENDPOINT phải là https://<host>[:port] (http chỉ cho localhost), nhận '$endpoint'"
    exit 2
}
$url = "$endpoint/v1/projects/$Code/pending"

Write-Err "bughub-wait: chờ bug của $Code ($url, mỗi ${interval}s)"
$lastErr = ''

while ($true) {
    $err = ''
    try {
        $resp = Invoke-RestMethod -Uri $url -Method Get -TimeoutSec $RequestTimeoutSec -UseBasicParsing
        # Server trả application/json thì đã được parse sẵn; content-type khác (mock server) ra chuỗi.
        if ($resp -is [string]) { $resp = $resp | ConvertFrom-Json }
        try {
            $new = [int]$resp.new
            $retry = [int]$resp.retry
        } catch {
            $new = $null
        }
        if ($null -eq $new -or $null -eq $resp) {
            $err = 'payload không phải JSON {new,retry}'
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
            Write-Err "bughub-wait: project '$Code' không tồn tại trên server (404) — kiểm bugHub.projectCode / chạy /bughub-setup"
            exit 3
        } elseif ($null -ne $statusCode) {
            $err = "HTTP $statusCode"
        } elseif ($_.Exception -is [System.ArgumentException] -or $_.FullyQualifiedErrorId -like '*ConvertFrom*') {
            $err = 'payload không phải JSON {new,retry}'
        } else {
            $err = 'không kết nối được server'
        }
    }

    if ($err -ne $lastErr) {
        if ($err) { Write-Err "bughub-wait: $err — vẫn poll tiếp" }
        else { Write-Err 'bughub-wait: server trả lời bình thường trở lại' }
        $lastErr = $err
    }
    Start-Sleep -Seconds $interval
}
