param([switch]$Apply)

$ErrorActionPreference = "Stop"
$scopeRoot = "Assets/_Project/Features"
$targets = "Events","GameData","Meta","Monetization","Onboarding","Social","System"
$projectRoot = "Assets/_Project"

function Get-Bom([string]$p) { $b=[System.IO.File]::ReadAllBytes($p); return ($b.Length -ge 3 -and $b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF) }
function Read-Text([string]$p) { return [System.IO.File]::ReadAllText($p) }
function Write-Text([string]$p,[string]$t,[bool]$bom){ $enc=New-Object System.Text.UTF8Encoding($bom); [System.IO.File]::WriteAllText($p,$t,$enc) }
function Get-Ns([string]$c){ $m=[regex]::Match($c,'(?m)^\s*namespace\s+([A-Za-z0-9_.]+)'); if($m.Success){return $m.Groups[1].Value}else{return $null} }

function Add-Using([string]$content, [string]$ns, [string]$eol) {
  if ($content -match ('(?m)^[ \t]*using[ \t]+' + [regex]::Escape($ns) + '[ \t]*;')) { return $content }
  $usings = [regex]::Matches($content, '(?m)^[ \t]*using[ \t]+[A-Za-z0-9_.]+[ \t]*;[ \t]*')
  if ($usings.Count -gt 0) {
    $last = $usings[$usings.Count-1]; $pos = $last.Index + $last.Length
    return $content.Substring(0,$pos) + $eol + "using $ns;" + $content.Substring($pos)
  } else { return "using $ns;" + $eol + $content }
}

$cntMergeUsing=0; $cntRtUsing=0; $cntSupa=0; $cntShadow=0

# ---- Pass over scope files: add using ProjectNamespace / Game.Runtime based on OLD ns ----
foreach ($t in $targets) {
  $dir = Join-Path $scopeRoot $t; if (-not (Test-Path $dir)) { continue }
  Get-ChildItem -Path $dir -Recurse -Filter *.cs | ForEach-Object {
    $f=$_.FullName; $rel=(Resolve-Path $f -Relative).Replace('\','/').TrimStart('.','/')
    $content = Read-Text $f
    $newNs = Get-Ns $content
    if (-not $newNs -or $newNs -notlike "Ezg.Feature.*") { return }
    $head = (git show "HEAD:$rel" 2>$null) -join "`n"
    if (-not $head) { return }
    $oldNs = Get-Ns $head
    if (-not $oldNs) { return }
    $eol = if ($content.Contains("`r`n")) {"`r`n"} else {"`n"}
    $orig = $content
    if ($oldNs -eq "ProjectNamespace" -or $oldNs -like "ProjectNamespace.*") { $content = Add-Using $content "ProjectNamespace" $eol }
    if ($oldNs -eq "Game.Runtime" -or $oldNs -like "Game.Runtime.*") { $content = Add-Using $content "Game.Runtime" $eol }
    if ($content -ne $orig) {
      if ($Apply) { Write-Text $f $content (Get-Bom $f) }
      if ($content -match "using ProjectNamespace;") { $script:cntMergeUsing++ }
    }
  }
}

# ---- Pass over WHOLE project: fully-qualify moved SupabaseHandleEvent + global:: shadow fix ----
Get-ChildItem -Path $projectRoot -Recurse -Filter *.cs | ForEach-Object {
  $f=$_.FullName; $content = Read-Text $f; $orig = $content
  $ns = Get-Ns $content

  # (5) ProjectNamespace.SupabaseHandleEvent -> fully qualified new location
  if ($content.Contains("ProjectNamespace.SupabaseHandleEvent")) {
    $content = $content.Replace("ProjectNamespace.SupabaseHandleEvent","Ezg.Feature.Events._Core.SupabaseHandleEvent")
    $script:cntSupa++
  }

  # (6) shadow fix only inside Ezg.Feature.* files: System. / Firebase. qualifiers -> global::
  if ($ns -like "Ezg.Feature.*") {
    $eol = if ($content.Contains("`r`n")) {"`r`n"} else {"`n"}
    $lines = $content -split "`n"
    for ($i=0; $i -lt $lines.Count; $i++) {
      $ln = $lines[$i]
      if ($ln -match '^\s*using\s') { continue }            # skip using directives
      if ($ln -match '^\s*namespace\s') { continue }
      foreach ($root in @('System','Firebase')) {
        # match root qualifier not preceded by word/dot/colon/quote/slash, not already global::
        $rx = New-Object System.Text.RegularExpressions.Regex ('(?<![\w."/:])' + $root + '\.')
        $ln2 = $rx.Replace($ln, {
          param($m)
          # skip if inside a string literal: odd number of unescaped quotes before match
          $before = $ln.Substring(0, $m.Index)
          $q = ([regex]::Matches($before, '(?<!\\)"')).Count
          if ($q % 2 -eq 1) { return $m.Value }   # inside string, leave as-is
          return "global::$($m.Value)"
        })
        $ln = $ln2
      }
      $lines[$i] = $ln
    }
    $content = ($lines -join "`n")
    if ($content -ne $orig -and ($content -match 'global::System|global::Firebase')) { $script:cntShadow++ }
  }

  if ($content -ne $orig -and $Apply) { Write-Text $f $content (Get-Bom $f) }
}

"using ProjectNamespace/Game.Runtime added to scope files (approx): $cntMergeUsing"
"Files with SupabaseHandleEvent requalified: $cntSupa"
"Files with System/Firebase shadow fixed: $cntShadow"
if (-not $Apply) { "(dry-run)" }
