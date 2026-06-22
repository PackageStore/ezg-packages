# Deterministic preflight for /run-backlog staged diffs.
#
# Purpose:
#   Catch hard project-rule violations before spending LLM reviewer tokens.
#   The script is intentionally conservative: it reports confidence so the
#   orchestrator can auto-fix only "definite" findings and route contextual
#   findings to reviewers.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File .agents/scripts/backlog-preflight.ps1
#   powershell -ExecutionPolicy Bypass -File .agents/scripts/backlog-preflight.ps1 -Pretty

[CmdletBinding()]
param(
    [switch]$Pretty,
    [switch]$IncludeDiffStat = $true
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$RepoRoot = Split-Path -Parent $RepoRoot
Set-Location $RepoRoot

function Invoke-Git {
    param([string[]]$GitArgs)

    $output = & git @GitArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git $($GitArgs -join ' ') failed: $output"
    }

    return $output
}

function Test-CodeLine {
    param([string]$Line)

    $trimmed = $Line.Trim()
    if ($trimmed.Length -eq 0) { return $false }
    if ($trimmed.StartsWith("//")) { return $false }
    if ($trimmed.StartsWith("*")) { return $false }
    if ($trimmed.StartsWith("/*")) { return $false }
    return $true
}

function Add-Finding {
    param(
        [System.Collections.Generic.List[object]]$Findings,
        [string]$Rule,
        [string]$Severity,
        [string]$Confidence,
        [string]$File,
        [Nullable[int]]$Line,
        [string]$Evidence,
        [string]$Suggestion
    )

    $location = if ($Line.HasValue) { "${File}:$($Line.Value)" } else { $File }
    $Findings.Add([PSCustomObject]@{
        rule = $Rule
        severity = $Severity
        confidence = $Confidence
        file = $File
        line = if ($Line.HasValue) { $Line.Value } else { $null }
        location = $location
        evidence = $Evidence
        suggestion = $Suggestion
    }) | Out-Null
}

function Test-FileUsings {
    param(
        [string]$File,
        [System.Collections.Generic.HashSet[string]]$AddedUsings,
        [hashtable]$NeededUsings,
        [System.Collections.Generic.List[object]]$Findings,
        [string]$RepoRoot
    )

    foreach ($ns in @($NeededUsings.Keys)) {
        if ($AddedUsings.Contains($ns)) { continue }

        $filePath = Join-Path $RepoRoot $File
        if (Test-Path $filePath) {
            $fileContent = Get-Content $filePath -Raw -ErrorAction SilentlyContinue
            if ($fileContent -and ($fileContent -match "using\s+$([regex]::Escape($ns))\s*;")) { continue }
        }

        $occ = $NeededUsings[$ns]
        Add-Finding $Findings "missing-using" "critical" "definite" $File $occ.Line `
            $occ.Evidence `
            "Add 'using $ns;' at the top of the file."
    }
}

$changedFilesRaw = @(Invoke-Git -GitArgs @("diff", "--staged", "--name-only"))
$changedFiles = @($changedFilesRaw | Where-Object { $_ -and $_.Trim().Length -gt 0 })

$diff = ""
if ($changedFiles.Count -gt 0) {
    $diff = (Invoke-Git -GitArgs @("diff", "--staged", "--unified=20")) -join "`n"
}

$diffStat = ""
if ($IncludeDiffStat -and $changedFiles.Count -gt 0) {
    $diffStat = (Invoke-Git -GitArgs @("diff", "--staged", "--stat")) -join "`n"
}

$findings = [System.Collections.Generic.List[object]]::new()
$sensitiveReasons = [System.Collections.Generic.List[object]]::new()

# Sensitive file patterns for Merge Two (no backend/supabase/cloudflare)
$sensitiveFilePatterns = @(
    "*Purchase*",
    "*IAP*",
    "*Receipt*",
    "*Payment*",
    "*DataPlayer*",
    "*SaveData*",
    "*PlayerPrefs*",
    "*Persistence*",
    "*Auth*",
    "*Token*",
    "*Session*",
    "*.env*",
    "*.config",
    "*Secrets*",
    "*Credential*"
)

foreach ($file in $changedFiles) {
    foreach ($pattern in $sensitiveFilePatterns) {
        if ($file -like $pattern) {
            $sensitiveReasons.Add([PSCustomObject]@{
                type = "file-pattern"
                file = $file
                pattern = $pattern
            }) | Out-Null
            break
        }
    }
}

# Namespace requirements: if a diff line uses one of these API patterns, the file
# must have the matching 'using' directive (checked at each file boundary).
$nsRequirements = @(
    @{ Pattern = '\.(Where|Select|ToList|FirstOrDefault|LastOrDefault|Any|All|OrderBy|OrderByDescending|ThenBy|ThenByDescending|GroupBy|Distinct|Skip|Take|Sum|Count|Max|Min|Average|SelectMany|Aggregate)\s*\('; Namespace = 'System.Linq' },
    @{ Pattern = '\b(UniTask|UniTaskVoid|UniTaskCompletionSource)\b'; Namespace = 'Cysharp.Threading.Tasks' },
    @{ Pattern = '\.DO(Fade|Move|Scale|Color|Rotate|Jump|Punch|Shake|Value|Blendable|Path)\s*\(|\b(DOTween|DOVirtual|Tweener|TweenParams)\b'; Namespace = 'DG.Tweening' },
    @{ Pattern = '\b(EasyEventManager)\b'; Namespace = 'TigerForge' },
    @{ Pattern = '\b(TextMeshProUGUI|TMP_Text|TMP_InputField|TextMeshPro)\b'; Namespace = 'TMPro' },
    @{ Pattern = '\bAction\s*[<(]|\bFunc\s*<|\[Serializable\]'; Namespace = 'System' },
    @{ Pattern = '\bList\s*<|\bDictionary\s*<|\bHashSet\s*<|\bQueue\s*<|\bStack\s*<'; Namespace = 'System.Collections.Generic' },
    @{ Pattern = '\b(Button|Slider|Toggle|Dropdown|ScrollRect|RawImage|Scrollbar)\b'; Namespace = 'UnityEngine.UI' }
)

$currentFile = $null
$newLine = $null
$hunkBuffer = New-Object System.Collections.Generic.Queue[string]
$fileAddedUsings = [System.Collections.Generic.HashSet[string]]::new()
$fileNeededUsings = @{}

foreach ($rawLine in ($diff -split "`n")) {
    $line = $rawLine.TrimEnd("`r")

    if ($line -match '^diff --git a/(.+?) b/(.+)$') {
        if ($null -ne $currentFile) {
            Test-FileUsings -File $currentFile -AddedUsings $fileAddedUsings -NeededUsings $fileNeededUsings -Findings $findings -RepoRoot $RepoRoot
        }
        $currentFile = $matches[2]
        $newLine = $null
        $hunkBuffer.Clear()
        $fileAddedUsings = [System.Collections.Generic.HashSet[string]]::new()
        $fileNeededUsings = @{}
        continue
    }

    if ($line -match '^\+\+\+ b/(.+)$') {
        $currentFile = $matches[1]
        continue
    }

    if ($line -match '^@@ -\d+(?:,\d+)? \+(\d+)(?:,\d+)? @@') {
        $newLine = [int]$matches[1]
        $hunkBuffer.Clear()
        continue
    }

    if ($null -eq $currentFile -or $null -eq $newLine) {
        continue
    }

    if ($line.StartsWith("+") -and -not $line.StartsWith("+++")) {
        $code = $line.Substring(1)
        $lineNumber = $newLine
        $isCode = Test-CodeLine $code
        $trimmed = $code.Trim()
        $context = ($hunkBuffer.ToArray() -join "`n")

        if ($isCode) {
            # Time — must use TimeManager, not DateTime
            if ($trimmed -match '\bDateTime\.(Now|UtcNow)\b') {
                Add-Finding $findings "time-manager" "critical" "definite" $currentFile $lineNumber $trimmed "Use TimeManager instead of DateTime.Now/DateTime.UtcNow."
            }

            if ($trimmed -match '\bTime\.realtimeSinceStartup\b') {
                Add-Finding $findings "time-manager" "major" "contextual" $currentFile $lineNumber $trimmed "Verify this is not game-time logic. Use TimeManager for game cooldown/save/time rules."
            }

            # Async — must use UniTask, not Coroutine
            if ($trimmed -match '\bStartCoroutine\s*\(' -or $trimmed -match '\bStopCoroutine\s*\(') {
                Add-Finding $findings "unitask" "critical" "definite" $currentFile $lineNumber $trimmed "Use UniTask with cancellation instead of new coroutine calls."
            }

            if ($trimmed -match '\bIEnumerator\b') {
                Add-Finding $findings "unitask" "critical" "contextual" $currentFile $lineNumber $trimmed "New async/game flows should use UniTask. Verify this is not an allowed Unity/third-party signature."
            }

            if ($trimmed -match '\basync\s+void\b') {
                Add-Finding $findings "unitask" "critical" "contextual" $currentFile $lineNumber $trimmed "Avoid async void except narrow Unity event-handler cases; prefer UniTask."
            }

            if ($trimmed -match '\bTask\s*(<|\b)') {
                Add-Finding $findings "unitask" "critical" "contextual" $currentFile $lineNumber $trimmed "Use UniTask instead of Task for game code."
            }

            # UI — must use UIManager, not SetActive for top-level UI
            if ($trimmed -match '\.SetActive\s*\(' -or $trimmed -match '\bgameObject\.SetActive\s*\(') {
                Add-Finding $findings "ui-manager" "critical" "contextual" $currentFile $lineNumber $trimmed "Use UIManager for top-level UI feature show/hide. Child component toggles may be acceptable with task-specific justification."
            }

            # Data persistence — must use PlayerDataManager, not PlayerPrefs directly
            if ($trimmed -match '\bPlayerPrefs\b') {
                Add-Finding $findings "data-persistence" "critical" "definite" $currentFile $lineNumber $trimmed "Use PlayerDataManager.[Module] instead of PlayerPrefs/direct local persistence."
            }

            # DataManager is read-only config — never write to it
            if ($trimmed -match '\bDataManager\s*\.\s*\w+\s*=') {
                Add-Finding $findings "data-persistence" "critical" "definite" $currentFile $lineNumber $trimmed "DataManager is read-only config. Do not assign values to DataManager properties at runtime."
            }

            # Logging
            if ($trimmed -match '\bConsole\.WriteLine\s*\(') {
                Add-Finding $findings "logging" "critical" "definite" $currentFile $lineNumber $trimmed "Use Unity Debug.Log/LogWarning/LogError instead of Console.WriteLine."
            }

            if ($trimmed -match '\bDebug\.Log(Exception|Error)\s*\(') {
                Add-Finding $findings "console-noise" "major" "contextual" $currentFile $lineNumber $trimmed "Verify this is restricted to exceptional/catch paths and does not create new normal-flow console errors."
            }

            # Mobile performance — Find calls must be in Awake
            if ($trimmed -match '\b(GameObject\.Find|FindObjectOfType|FindObjectsOfType)\s*\(') {
                $confidence = if ($context -match '\bAwake\s*\(') { "contextual" } else { "definite" }
                Add-Finding $findings "mobile-performance" "critical" $confidence $currentFile $lineNumber $trimmed "Cache Find/GetComponent lookups in Awake; do not use Find APIs in hot paths."
            }

            # Mobile performance — LINQ in hot paths
            if ($trimmed -match '\.(Where|Select|ToList)\s*\(') {
                $severity = if ($context -match '\b(Update|FixedUpdate|LateUpdate)\s*\(') { "major" } else { "minor" }
                Add-Finding $findings "mobile-performance" $severity "contextual" $currentFile $lineNumber $trimmed "Verify LINQ is not in a gameplay hot path."
            }

            # Mobile performance — allocations in Update loops
            if ($trimmed -match '\bnew\s+(List|Dictionary|HashSet|Queue|Stack|StringBuilder)\b' -and $context -match '\b(Update|FixedUpdate|LateUpdate)\s*\(') {
                Add-Finding $findings "mobile-performance" "major" "contextual" $currentFile $lineNumber $trimmed "Avoid allocations in gameplay/update loops."
            }

            # Data persistence — Save() in Update
            if ($trimmed -match '\.Save\s*\(' -and $context -match '\b(Update|FixedUpdate|LateUpdate)\s*\(') {
                Add-Finding $findings "data-persistence" "critical" "contextual" $currentFile $lineNumber $trimmed "Never call Save() from Update/FixedUpdate/LateUpdate or per-frame loops."
            }

            # Credential patterns
            if ($trimmed -match '[A-Z0-9_]{3,}_(KEY|SECRET|TOKEN|PASSWORD)\b') {
                Add-Finding $findings "credential" "critical" "definite" $currentFile $lineNumber $trimmed "Do not add hardcoded credential-like identifiers or secrets to client code."
                $sensitiveReasons.Add([PSCustomObject]@{
                    type = "credential-pattern"
                    file = $currentFile
                    line = $lineNumber
                }) | Out-Null
            }

            if ($trimmed -match '(sk_[A-Za-z0-9_]+|Bearer\s+[A-Za-z0-9._-]+|eyJ[A-Za-z0-9._-]+)') {
                Add-Finding $findings "credential" "critical" "definite" $currentFile $lineNumber $trimmed "Potential secret/JWT/Bearer token in staged diff. Remove from client/repo."
                $sensitiveReasons.Add([PSCustomObject]@{
                    type = "credential-pattern"
                    file = $currentFile
                    line = $lineNumber
                }) | Out-Null
            }

            # Using directive tracking (for missing-using check at file boundary)
            if ($trimmed -match '^using\s+([\w\.]+(?:\.[\w]+)*)\s*;') {
                [void]$fileAddedUsings.Add($matches[1])
            }

            # Namespace requirement detection
            foreach ($nsReq in $nsRequirements) {
                if ($trimmed -match $nsReq.Pattern) {
                    if (-not $fileNeededUsings.ContainsKey($nsReq.Namespace)) {
                        $fileNeededUsings[$nsReq.Namespace] = [PSCustomObject]@{ Line = $lineNumber; Evidence = $trimmed }
                    }
                }
            }
        }

        $hunkBuffer.Enqueue($code)
        while ($hunkBuffer.Count -gt 40) {
            [void]$hunkBuffer.Dequeue()
        }

        $newLine++
        continue
    }

    if ($line.StartsWith(" ") -or $line.Length -eq 0) {
        $contextLine = if ($line.Length -gt 0) { $line.Substring(1) } else { "" }
        $hunkBuffer.Enqueue($contextLine)
        while ($hunkBuffer.Count -gt 40) {
            [void]$hunkBuffer.Dequeue()
        }
        $newLine++
        continue
    }

    if ($line.StartsWith("-") -and -not $line.StartsWith("---")) {
        continue
    }
}

# Final file check for the last file in the diff
if ($null -ne $currentFile) {
    Test-FileUsings -File $currentFile -AddedUsings $fileAddedUsings -NeededUsings $fileNeededUsings -Findings $findings -RepoRoot $RepoRoot
}

$criticalCount = @($findings | Where-Object { $_.severity -eq "critical" }).Count
$definiteCriticalCount = @($findings | Where-Object { $_.severity -eq "critical" -and $_.confidence -eq "definite" }).Count
$contextualCount = @($findings | Where-Object { $_.confidence -eq "contextual" }).Count

$result = [PSCustomObject]@{
    schema_version = 1
    generated_at = (Get-Date).ToString("o")
    repo = $RepoRoot
    diff = [PSCustomObject]@{
        staged = $true
        files_changed_count = $changedFiles.Count
        changed_files = $changedFiles
        stat = $diffStat
    }
    sensitive = [PSCustomObject]@{
        value = ($sensitiveReasons.Count -gt 0)
        reasons = $sensitiveReasons
    }
    summary = [PSCustomObject]@{
        findings_count = $findings.Count
        critical_count = $criticalCount
        definite_critical_count = $definiteCriticalCount
        contextual_count = $contextualCount
        has_blocking_definite = ($definiteCriticalCount -gt 0)
    }
    findings = $findings
}

$depth = 8
if ($Pretty) {
    $result | ConvertTo-Json -Depth $depth
} else {
    $result | ConvertTo-Json -Depth $depth -Compress
}
