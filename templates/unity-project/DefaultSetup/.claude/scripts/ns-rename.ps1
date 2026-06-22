param([switch]$Apply)

$ErrorActionPreference = "Stop"
$root = "Assets/_Project/Features"
$targets = "Events","GameData","Meta","Monetization","Onboarding","Social","System"

function Get-Bom([string]$p) {
  $b = [System.IO.File]::ReadAllBytes($p)
  return ($b.Length -ge 3 -and $b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF)
}
function Read-Text([string]$p) { return [System.IO.File]::ReadAllText($p) }   # UTF-8, BOM auto-detected & stripped
function Write-Text([string]$p, [string]$t, [bool]$bom) {
  $enc = New-Object System.Text.UTF8Encoding($bom)
  [System.IO.File]::WriteAllText($p, $t, $enc)
}

$results = @()
foreach ($t in $targets) {
  $dir = Join-Path $root $t
  if (-not (Test-Path $dir)) { continue }
  Get-ChildItem -Path $dir -Recurse -Filter *.cs | ForEach-Object {
    $f = $_.FullName
    $rel = (Resolve-Path $f -Relative).Replace('\','/')
    $after = ($rel -split '/Features/')[1]
    $parts = $after -split '/'
    $L1 = $parts[0]
    $L2 = if ($parts.Count -ge 3) { $parts[1] } else { $null }
    $newNs = if ($L2) { "Ezg.Feature.$L1.$L2" } else { "Ezg.Feature.$L1" }

    $content = Read-Text $f
    $m = [regex]::Match($content, '(?m)^(\s*)namespace\s+([A-Za-z0-9_.]+)')
    if ($m.Success) {
      $oldNs = $m.Groups[2].Value
      if ($oldNs -ne $newNs) {
        $results += [pscustomobject]@{ File=$rel; Old=$oldNs; New=$newNs }
        if ($Apply) {
          $bom = Get-Bom $f
          $updated = [regex]::Replace($content, '(?m)^(\s*namespace\s+)[A-Za-z0-9_.]+', "`${1}$newNs")
          Write-Text $f $updated $bom
        }
      }
    }
  }
}

$results | Sort-Object New, File | ForEach-Object { "{0}`n    {1}  ->  {2}" -f $_.File, $_.Old, $_.New }
""
"TOTAL files to change: $($results.Count)"
