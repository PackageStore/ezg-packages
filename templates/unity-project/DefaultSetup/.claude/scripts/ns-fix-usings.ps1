param([switch]$Apply)

$ErrorActionPreference = "Stop"
$scopeRoot = "Assets/_Project/Features"
$targets = "Events","GameData","Meta","Monetization","Onboarding","Social","System"
$projectRoot = "Assets/_Project"

function Get-Bom([string]$p) {
  $b = [System.IO.File]::ReadAllBytes($p)
  return ($b.Length -ge 3 -and $b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF)
}
function Read-Text([string]$p) { return [System.IO.File]::ReadAllText($p) }
function Write-Text([string]$p, [string]$t, [bool]$bom) {
  $enc = New-Object System.Text.UTF8Encoding($bom)
  [System.IO.File]::WriteAllText($p, $t, $enc)
}

# Names that collide with types declared OUTSIDE the moved scope (e.g. global partial 'EventName')
# -> excluded from auto-insert to avoid CS0104 ambiguity. Handle via compile-check.
$denylist = @("EventName")

$typeRegex = [regex]'(?m)^\s*(?:\[[^\]]*\]\s*)*(?:public|internal|private|protected|sealed|abstract|static|partial|\s)*\b(class|struct|interface|enum|record)\s+([A-Za-z_][A-Za-z0-9_]*)'

# --- 1. Build map: declared type name -> set of new namespaces ---
$typeToNs = @{}
foreach ($t in $targets) {
  $dir = Join-Path $scopeRoot $t
  if (-not (Test-Path $dir)) { continue }
  Get-ChildItem -Path $dir -Recurse -Filter *.cs | ForEach-Object {
    $content = Read-Text $_.FullName
    $nsm = [regex]::Match($content, '(?m)^\s*namespace\s+([A-Za-z0-9_.]+)')
    if (-not $nsm.Success) { return }
    $ns = $nsm.Groups[1].Value
    if ($ns -notlike "Ezg.Feature.*") { return }
    foreach ($tm in $typeRegex.Matches($content)) {
      $name = $tm.Groups[2].Value
      if (-not $typeToNs.ContainsKey($name)) { $typeToNs[$name] = New-Object System.Collections.Generic.HashSet[string] }
      [void]$typeToNs[$name].Add($ns)
    }
  }
}

$ambiguous = @{}
$clean = @{}
foreach ($k in $typeToNs.Keys) {
  if ($denylist -contains $k) { continue }
  if ($typeToNs[$k].Count -gt 1) { $ambiguous[$k] = ($typeToNs[$k] -join ", ") }
  else { $clean[$k] = ($typeToNs[$k] | Select-Object -First 1) }
}

"=== Distinct moved types: $($typeToNs.Count) ==="
"=== Clean (unambiguous, auto-fixable) types: $($clean.Count) ==="
"=== AMBIGUOUS type names (same name in multiple new namespaces) - excluded: $($ambiguous.Count) ==="
$ambiguous.GetEnumerator() | Sort-Object Name | ForEach-Object { "  {0}  ->  {1}" -f $_.Key, $_.Value }
""

if (-not $Apply) {
  "(dry-run: no files written. Re-run with -Apply.)"
  return
}

# --- 2. Insert usings across whole project for clean types (encoding/EOL-safe) ---
$filesTouched = 0
Get-ChildItem -Path $projectRoot -Recurse -Filter *.cs | ForEach-Object {
  $f = $_.FullName
  $content = Read-Text $f
  $ownNs = ([regex]::Match($content, '(?m)^\s*namespace\s+([A-Za-z0-9_.]+)')).Groups[1].Value

  $needed = New-Object System.Collections.Generic.HashSet[string]
  foreach ($k in $clean.Keys) {
    $targetNs = $clean[$k]
    if ($ownNs -eq $targetNs) { continue }
    if ($content -match ("using\s+" + [regex]::Escape($targetNs) + "\s*;")) { continue }
    if ($content -match ("\b" + [regex]::Escape($k) + "\b")) { [void]$needed.Add($targetNs) }
  }
  if ($needed.Count -eq 0) { return }

  $eol = if ($content.Contains("`r`n")) { "`r`n" } else { "`n" }
  $insertLines = ($needed | Sort-Object | ForEach-Object { "using $_;" }) -join $eol

  # splice right after the last existing using-line; else after BOM-less start
  $usings = [regex]::Matches($content, '(?m)^[ \t]*using[ \t]+[A-Za-z0-9_.]+[ \t]*;[ \t]*')
  if ($usings.Count -gt 0) {
    $last = $usings[$usings.Count - 1]
    $pos = $last.Index + $last.Length
    $content = $content.Substring(0, $pos) + $eol + $insertLines + $content.Substring($pos)
  } else {
    $content = $insertLines + $eol + $content
  }
  $bom = Get-Bom $f
  Write-Text $f $content $bom
  $filesTouched++
}
"Files touched (usings inserted): $filesTouched"
