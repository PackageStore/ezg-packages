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
function Get-Ns([string]$content) {
  $m = [regex]::Match($content, '(?m)^\s*namespace\s+([A-Za-z0-9_.]+)')
  if ($m.Success) { return $m.Groups[1].Value } else { return $null }
}

# --- 1. Build old->new map from git HEAD for the renamed files ---
$old2new = @{}   # oldNs -> hashset newNs
foreach ($t in $targets) {
  $dir = Join-Path $scopeRoot $t
  if (-not (Test-Path $dir)) { continue }
  Get-ChildItem -Path $dir -Recurse -Filter *.cs | ForEach-Object {
    $rel = (Resolve-Path $_.FullName -Relative).Replace('\','/').TrimStart('.','/')
    $newNs = Get-Ns (Read-Text $_.FullName)
    if (-not $newNs -or $newNs -notlike "Ezg.Feature.*") { return }
    $headContent = (git show "HEAD:$rel" 2>$null) -join "`n"
    if (-not $headContent) { return }
    $oldNs = Get-Ns $headContent
    if ($oldNs -and $oldNs -ne $newNs) {
      if (-not $old2new.ContainsKey($oldNs)) { $old2new[$oldNs] = New-Object System.Collections.Generic.HashSet[string] }
      [void]$old2new[$oldNs].Add($newNs)
    }
  }
}

# --- 2. Determine which namespaces are STILL declared anywhere (live) ---
$live = New-Object System.Collections.Generic.HashSet[string]
Get-ChildItem -Path $projectRoot -Recurse -Filter *.cs | ForEach-Object {
  $ns = Get-Ns (Read-Text $_.FullName)
  if ($ns) { [void]$live.Add($ns) }
}

# Dead = old namespaces no file declares anymore
$dead = @{}
foreach ($k in $old2new.Keys) { if (-not $live.Contains($k)) { $dead[$k] = $old2new[$k] } }

"=== Renamed old namespaces: $($old2new.Count) ==="
"=== DEAD old namespaces (no longer declared; their 'using' lines are broken): $($dead.Count) ==="
$dead.GetEnumerator() | Sort-Object Name | ForEach-Object { "  {0}`n      -> {1}" -f $_.Key, ($_.Value -join ", ") }
""

if (-not $Apply) { "(dry-run: no files written. Re-run with -Apply.)"; return }

# --- 3. Replace `using <deadNs>;` with destination usings across project ---
$filesTouched = 0
Get-ChildItem -Path $projectRoot -Recurse -Filter *.cs | ForEach-Object {
  $f = $_.FullName
  $content = Read-Text $f
  $changed = $false
  $eol = if ($content.Contains("`r`n")) { "`r`n" } else { "`n" }
  $ownNs = Get-Ns $content

  foreach ($deadNs in $dead.Keys) {
    $pattern = '(?m)^[ \t]*using[ \t]+' + [regex]::Escape($deadNs) + '[ \t]*;[ \t]*\r?\n?'
    if ($content -match $pattern) {
      # destinations not already present and not the file's own namespace
      $repl = @()
      foreach ($newNs in ($dead[$deadNs] | Sort-Object)) {
        if ($ownNs -eq $newNs) { continue }
        if ($content -match ('(?m)^[ \t]*using[ \t]+' + [regex]::Escape($newNs) + '[ \t]*;')) { continue }
        $repl += "using $newNs;"
      }
      $replText = if ($repl.Count -gt 0) { ($repl -join $eol) + $eol } else { "" }
      $content = [regex]::Replace($content, $pattern, $replText)
      $changed = $true
    }
  }
  if ($changed) { Write-Text $f $content (Get-Bom $f); $filesTouched++ }
}
"Files touched (dead usings replaced): $filesTouched"
