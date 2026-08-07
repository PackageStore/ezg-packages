# Deterministic preflight for /run-backlog staged diffs.
#
# Purpose:
#   Catch hard project-rule violations before spending LLM reviewer tokens.
#   The script is intentionally conservative: it reports confidence so the
#   orchestrator can auto-fix only "definite" findings and route contextual
#   findings to reviewers.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File .claude/scripts/backlog-preflight.ps1
#   powershell -ExecutionPolicy Bypass -File .claude/scripts/backlog-preflight.ps1 -Pretty

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

function Invoke-GitSoft {
    # git that tolerates failure (path absent, or no HEAD version yet) - returns $null, never throws.
    param([string[]]$GitArgs)

    $output = & git @GitArgs 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    return @($output)
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

$script:LocalizeKeyPattern = '^(#[a-z0-9_]+),'

function Get-LocalizeKeys {
    param([string[]]$Lines)

    $keys = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($line in $Lines) {
        if ($line -match $script:LocalizeKeyPattern) { [void]$keys.Add($Matches[1].ToLower()) }
    }
    return $keys
}

# Two deterministic guards on the localize table - both are regressions we already shipped.
#
# 1. localize-key-loss. CsvImportManager.BuildLocalizationCsvContent rewrites Localization.csv
#    wholesale from the Google Sheet, and the Sheet does not contain keys that were only ever
#    added to the local file. Re-importing therefore deletes them with no error: it cost 144
#    already-shipped keys in backlog/done/116, and the same trap is recorded in backlog/done/104.
#    Any staged removal of a key present in HEAD is a regression, so this is `definite` - it
#    blocks before the LLM reviewers ever run.
# 2. localize-escape-collision. The importer escapes ',' as '%%' and LocalizationCollection
#    unescapes with a left-to-right Replace('%%', ','), so a literal '%' immediately before a
#    comma becomes '%%%' and decodes as ',%' - "40,%" instead of "40%,". Only ADDED lines are
#    inspected, so the pre-existing rows carrying this artifact do not spam every diff that
#    happens to touch the file.
function Test-LocalizeIntegrity {
    param(
        [string[]]$ChangedFiles,
        [System.Collections.Generic.List[object]]$Findings
    )

    foreach ($f in $ChangedFiles) {
        if ($f.ToLower() -notlike '*localization.csv') { continue }

        $staged = Invoke-GitSoft -GitArgs @('show', ":$f")
        if ($null -eq $staged) { continue }

        $head = Invoke-GitSoft -GitArgs @('show', "HEAD:$f")
        if ($null -ne $head) {
            $headKeys = Get-LocalizeKeys -Lines $head
            $stagedKeys = Get-LocalizeKeys -Lines $staged
            $lost = @($headKeys | Where-Object { -not $stagedKeys.Contains($_) } | Sort-Object)
            if ($lost.Count -gt 0) {
                $shown = ($lost | Select-Object -First 8) -join ', '
                if ($lost.Count -gt 8) { $shown += ', ...' }
                Add-Finding $Findings "localize-key-loss" "critical" "definite" $f $null `
                    "$($lost.Count) key(s) in HEAD are missing from the staged file: $shown" `
                    ("Do NOT overwrite Localization.csv wholesale - re-importing it from the Google " +
                     "Sheet drops keys that exist only locally. Merge instead: keep the freshly " +
                     "downloaded rows and re-append the local-only ones from 'git show HEAD:<file>'. " +
                     "Add every NEW key through /add-localize so it lives in the Sheet and survives " +
                     "the next import.")
            }
        }

        $added = Invoke-GitSoft -GitArgs @('diff', '--staged', '-U0', '--', $f)
        if ($null -eq $added) { $added = @() }
        foreach ($line in $added) {
            if (-not $line.StartsWith('+') -or $line.StartsWith('+++')) { continue }
            $content = $line.Substring(1)
            if (($content -match $script:LocalizeKeyPattern) -and ($content -like '*%%%*')) {
                $evidence = if ($content.Length -gt 160) { $content.Substring(0, 160) } else { $content }
                Add-Finding $Findings "localize-escape-collision" "critical" "definite" $f $null $evidence `
                    ("'%%%' decodes as ',%' not '%,'. The importer escapes ',' as '%%' and the reader " +
                     "replaces left-to-right, so a literal '%' directly before a comma collides. " +
                     "Reword so no '%' is immediately followed by a comma (e.g. 'shrinks 40% in area,' " +
                     "instead of 'shrinks 40%,').")
            }
        }
    }
}

$script:UiKitJson = ".claude/ui-kit/ui-kit.json"

function Get-UiTemplatesRoot {
    # Twin of project_profile.DEFAULTS["uiTemplatesRoot"]. Read as data so this
    # rule does not become a NEW hardcoded path in the PowerShell half; the
    # fallback reproduces the default, matching the Python reader.
    $default = "Assets/Resources/Prefabs/Templates"
    foreach ($rel in @(".claude/project-profile.json", ".claude/project-profile.json")) {
        $path = Join-Path $RepoRoot $rel
        if (-not (Test-Path -LiteralPath $path)) { continue }
        try {
            $value = (Get-Content -LiteralPath $path -Raw | ConvertFrom-Json).uiTemplatesRoot
        } catch { return $default }
        if ($value) { return [string]$value }
        return $default
    }
    return $default
}

# A staged screen-template edit must carry the regenerated UI kit with it.
#
# The kit (ui-kit.json + .css + gallery) is extracted from the template prefabs and read by
# mockup-drafter, the ui-spec validator and /new-ui. It has no watcher, and drifting costs
# nothing at commit time: the kit simply keeps describing the old prefabs while the whole UI
# test suite stands down with "kit not generated yet". That is exactly how this repo shipped a
# kit describing 46 templates against a folder of 48 for weeks. So the diff itself is the
# checkpoint. Only fires when the kit is TRACKED: a project may legitimately gitignore the
# generated half and rebuild it at bootstrap, and nagging there would be noise forever.
function Test-UiKitStaleness {
    param(
        [string[]]$ChangedFiles,
        [System.Collections.Generic.List[object]]$Findings
    )

    $root = (Get-UiTemplatesRoot).Replace('\', '/').TrimEnd('/').ToLower()
    if (-not $root) { return }

    $touched = @($ChangedFiles | Where-Object {
        $normalized = $_.Replace('\', '/').ToLower()
        $normalized.StartsWith("$root/") -and
        ($normalized.EndsWith('.prefab') -or $normalized.EndsWith('.prefab.meta'))
    })
    if ($touched.Count -eq 0) { return }

    $kitStaged = @($ChangedFiles | Where-Object { $_.Replace('\', '/').ToLower() -eq $script:UiKitJson.ToLower() })
    if ($kitStaged.Count -gt 0) { return }
    if ($null -eq (Invoke-GitSoft -GitArgs @('ls-files', '--error-unmatch', $script:UiKitJson))) { return }

    $shown = (($touched | Select-Object -First 6) | ForEach-Object { Split-Path -Leaf $_ }) -join ', '
    if ($touched.Count -gt 6) { $shown += ', ...' }
    $first = $touched[0]
    Add-Finding $Findings "ui-kit-stale" "major" "definite" $first $null `
        "$($touched.Count) screen template file(s) staged without the regenerated kit: $shown" `
        ("Run 'python3 .claude/scripts/ui-kit-sync.py' and stage .claude/ui-kit/ in the same commit. " +
         "The kit is generated FROM these prefabs, so leaving it behind makes every later mockup " +
         "describe UI that no longer exists - silently, because the UI tests skip instead of failing " +
         "when the kit is out of date. See .claude/skills/ui-kit/SKILL.md.")
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

$sensitiveFilePatterns = @(
    "*Backend*",
    "*Supabase*",
    "*Cloudflare*",
    "*Worker*",
    "*Purchase*",
    "*IAP*",
    "*Receipt*",
    "*DataPlayer*",
    "*SaveData*",
    "*PlayerPrefs*",
    "*Persistence*",
    "*Auth*",
    "*Login*",
    "*Token*",
    "*Session*",
    "*Leaderboard*",
    "*Ranking*",
    "*Social*",
    "*AntiCheat*",
    "*Validation*",
    "*Integrity*",
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

Test-LocalizeIntegrity -ChangedFiles $changedFiles -Findings $findings
Test-UiKitStaleness -ChangedFiles $changedFiles -Findings $findings

$nsRequirements = @(
    @{ Pattern = '\.(Where|Select|ToList|FirstOrDefault|LastOrDefault|Any|All|OrderBy|OrderByDescending|ThenBy|ThenByDescending|GroupBy|Distinct|Skip|Take|Sum|Count|Max|Min|Average|SelectMany|Aggregate)\s*\('; Namespace = 'System.Linq' },
    @{ Pattern = '\b(UniTask|UniTaskVoid|UniTaskCompletionSource)\b'; Namespace = 'Cysharp.Threading.Tasks' },
    @{ Pattern = '\.DO(Fade|Move|Scale|Color|Rotate|Jump|Punch|Shake|Value|Blendable|Path)\s*\(|\b(DOTween|DOVirtual|Tweener|TweenParams)\b'; Namespace = 'DG.Tweening' },
    @{ Pattern = '\b(EasyEventManager)\b'; Namespace = 'TigerForge' },
    @{ Pattern = '\b(TextMeshProUGUI|TMP_Text|TMP_InputField|TextMeshPro)\b'; Namespace = 'TMPro' },
    @{ Pattern = '\bAction\s*[<(]|\bFunc\s*<|\[Serializable\]'; Namespace = 'System' },
    @{ Pattern = '\bList\s*<|\bDictionary\s*<|\bHashSet\s*<|\bQueue\s*<|\bStack\s*<'; Namespace = 'System.Collections.Generic' },
    # Negative lookbehind on '[': Odin's attribute [Button("...")] is Sirenix.OdinInspector.Button,
    # NOT UnityEngine.UI.Button. Without it every cheat file the [CHEAT] guardrail asks for is
    # blocked by a false critical. The '.' does the same for IMGUI calls such as
    # EditorGUILayout.Toggle(...) / GUILayout.Button(...), which blocked every EditorWindow.
    # Keep in lockstep with backlog-preflight.py.
    @{ Pattern = '(?<![\[.])\b(Button|Slider|Toggle|Dropdown|ScrollRect|RawImage|Scrollbar)\b'; Namespace = 'UnityEngine.UI' }
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
            if ($trimmed -match '\bDateTime\.(Now|UtcNow)\b') {
                Add-Finding $findings "time-manager" "critical" "definite" $currentFile $lineNumber $trimmed "Use TimeManager instead of DateTime.Now/DateTime.UtcNow."
            }

            if ($trimmed -match '\bTime\.realtimeSinceStartup\b') {
                Add-Finding $findings "time-manager" "major" "contextual" $currentFile $lineNumber $trimmed "Verify this is not game-time logic. Use TimeManager for game cooldown/save/time rules."
            }

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

            if ($trimmed -match '\.SetActive\s*\(' -or $trimmed -match '\bgameObject\.SetActive\s*\(') {
                Add-Finding $findings "ui-manager" "critical" "contextual" $currentFile $lineNumber $trimmed "Use UIManager for top-level UI feature show/hide. Child component toggles may be acceptable with task-specific justification."
            }

            if ($trimmed -match '\bPlayerPrefs\b') {
                Add-Finding $findings "data-persistence" "critical" "definite" $currentFile $lineNumber $trimmed "Use DataPlayer through PlayerDataManager instead of PlayerPrefs/direct local persistence."
            }

            if ($trimmed -match '\bConsole\.WriteLine\s*\(') {
                Add-Finding $findings "logging" "critical" "definite" $currentFile $lineNumber $trimmed "Use Unity Debug.Log/LogWarning/LogError instead of Console.WriteLine."
            }

            if ($trimmed -match '\bDebug\.Log(Exception|Error)\s*\(') {
                Add-Finding $findings "console-noise" "major" "contextual" $currentFile $lineNumber $trimmed "Verify this is restricted to exceptional/catch paths and does not create new normal-flow console errors."
            }

            if ($trimmed -match '\b(GameObject\.Find|FindObjectOfType|FindObjectsOfType)\s*\(') {
                $confidence = if ($context -match '\bAwake\s*\(') { "contextual" } else { "definite" }
                Add-Finding $findings "mobile-performance" "critical" $confidence $currentFile $lineNumber $trimmed "Cache Find/GetComponent lookups in Awake; do not use Find APIs in hot paths."
            }

            if ($trimmed -match '\.(Where|Select|ToList)\s*\(') {
                $severity = if ($context -match '\b(Update|FixedUpdate|LateUpdate)\s*\(') { "major" } else { "minor" }
                Add-Finding $findings "mobile-performance" $severity "contextual" $currentFile $lineNumber $trimmed "Verify LINQ is not in a gameplay hot path."
            }

            if ($trimmed -match '\bnew\s+(List|Dictionary|HashSet|Queue|Stack|StringBuilder)\b' -and $context -match '\b(Update|FixedUpdate|LateUpdate)\s*\(') {
                Add-Finding $findings "mobile-performance" "major" "contextual" $currentFile $lineNumber $trimmed "Avoid allocations in gameplay/update loops."
            }

            if ($trimmed -match '\.Save\s*\(' -and $context -match '\b(Update|FixedUpdate|LateUpdate)\s*\(') {
                Add-Finding $findings "data-persistence" "critical" "contextual" $currentFile $lineNumber $trimmed "Never call Save() from Update/FixedUpdate/LateUpdate or per-frame loops."
            }

            if ($trimmed -match '(?i)supabase\s*\.\s*from\s*\([^\)]*\)\s*\.\s*(insert|update|upsert|delete)\b') {
                Add-Finding $findings "backend-security" "critical" "definite" $currentFile $lineNumber $trimmed "Client writes must go through Cloudflare Worker, not direct Supabase mutation."
                $sensitiveReasons.Add([PSCustomObject]@{
                    type = "direct-supabase-write"
                    file = $currentFile
                    line = $lineNumber
                }) | Out-Null
            }

            # Credential-LIKE identifier. The scoped (?-i:...) keeps the match
            # UPPER_SNAKE-only even though -match is case-insensitive by default —
            # without it, lowercase identifiers/JSON keys (player_token, session_key,
            # "access_token") false-positive. Confidence is contextual (not definite):
            # legit SCREAMING_CONST names like LEADERBOARD_CACHE_KEY match the same
            # shape. Keep in lockstep with CREDENTIAL_ID_PATTERN in backlog-preflight.py.
            if ($trimmed -match '(?-i:[A-Z0-9_]{3,}_(KEY|SECRET|TOKEN|PASSWORD)\b)') {
                Add-Finding $findings "credential" "critical" "contextual" $currentFile $lineNumber $trimmed "Credential-like UPPER_SNAKE identifier. If it carries a real key/secret value, remove it from client code; if it is only a constant name, justify it to the security reviewer."
                $sensitiveReasons.Add([PSCustomObject]@{
                    type = "credential-pattern"
                    file = $currentFile
                    line = $lineNumber
                }) | Out-Null
            }

            # sk_/eyJ are word-bounded + case-scoped: unanchored `sk_` matched
            # INSIDE ordinary identifiers (task_still, TASK_BASE case-insensitively)
            # and flagged them critical-definite. Real Stripe keys are lowercase
            # `sk_...`; a JWT header is literally `eyJ` (base64 of '{"'), so exact
            # case loses nothing. Keep in lockstep with backlog-preflight.py.
            if ($trimmed -match '((?-i:\bsk_[A-Za-z0-9_]+)|Bearer\s+[A-Za-z0-9._-]+|(?-i:\beyJ[A-Za-z0-9._-]+))') {
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

            # Namespace requirement detection (C# source files only — skip prefabs, assets, etc.)
            if ($currentFile -match '\.cs$') {
                foreach ($nsReq in $nsRequirements) {
                    if ($trimmed -match $nsReq.Pattern) {
                        if (-not $fileNeededUsings.ContainsKey($nsReq.Namespace)) {
                            $fileNeededUsings[$nsReq.Namespace] = [PSCustomObject]@{ Line = $lineNumber; Evidence = $trimmed }
                        }
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
