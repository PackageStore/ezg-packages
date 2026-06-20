#!/usr/bin/env bash
# Transcode broken (POSIX-ustar) .unitypackage files to Unity-compatible GNU tar format.
# Lossless: only the tar header format changes; file contents are identical.
set -u

ROOT="/d/AppDevelop/ezg-packages/templates/unity-project"
CAT="$ROOT/asset-catalog.json"
OUT="/d/AppDevelop/ezg-packages/fixed-unitypackages"
DL="$OUT/_download"
WORK="$OUT/_work"
LOG="$OUT/report.txt"
SRC_DIRS=("$ROOT/.ezg-cache" "$ROOT/PackageTemplate" "/c/Users/Black Face/Downloads")

mkdir -p "$OUT" "$DL" "$WORK"
: > "$LOG"
log(){ echo "$*" | tee -a "$LOG"; }

magic_of(){ gzip -dc "$1" 2>/dev/null | dd bs=1 skip=257 count=8 2>/dev/null | xxd -p; }

node -e 'const c=JSON.parse(require("fs").readFileSync(process.argv[1],"utf8"));for(const a of c.assets)console.log(a.fileName+"\t"+a.url);' "$CAT" > "$WORK/urls.tsv"

log "=== fix-unitypackages run ==="
log "output dir: $OUT"
log ""
fixed=0; okcount=0; failed=0

while IFS=$'\t' read -r fn url; do
  [ -z "$fn" ] && continue
  # locate a local source first
  src=""
  for d in "${SRC_DIRS[@]}"; do
    if [ -f "$d/$fn" ]; then src="$d/$fn"; break; fi
  done
  origin="local"
  if [ -z "$src" ]; then
    origin="download"
    src="$DL/$fn"
    if [ ! -f "$src" ]; then
      curl -fSL "$url" -o "$src" 2>>"$LOG" || { log "FAIL download  $fn"; failed=$((failed+1)); continue; }
    fi
  fi

  m=$(magic_of "$src")
  if [[ "$m" != 757374617200* ]]; then
    log "OK    $fn  (format=$m, $origin) -> skip"
    okcount=$((okcount+1))
    continue
  fi

  # broken -> transcode to GNU
  w="$WORK/x"; rm -rf "$w"; mkdir -p "$w"
  if ! tar -xzf "$src" -C "$w" 2>>"$LOG"; then log "FAIL extract   $fn"; failed=$((failed+1)); rm -rf "$w"; continue; fi
  tar -tzf "$src" 2>/dev/null > "$w/.order.txt"
  ( cd "$w" && tar --format=gnu --no-recursion -czf "$OUT/$fn" -T .order.txt ) 2>>"$LOG" || { log "FAIL repack    $fn"; failed=$((failed+1)); rm -rf "$w"; continue; }

  # verify: counts match + new magic is GNU
  oc=$(tar -tzf "$src" 2>/dev/null | wc -l)
  nc=$(tar -tzf "$OUT/$fn" 2>/dev/null | wc -l)
  nm=$(magic_of "$OUT/$fn")
  rm -rf "$w"
  if [ "$oc" != "$nc" ] || [[ "$nm" != 7573746172202000 && "$nm" != 7573746172203200 ]]; then
    log "FAIL verify    $fn (entries $oc->$nc magic $nm)"; failed=$((failed+1)); continue
  fi
  sha=$(sha256sum "$OUT/$fn" | cut -d' ' -f1)
  log "FIXED $fn  entries=$nc  sha256=$sha  ($origin)"
  fixed=$((fixed+1))
done < "$WORK/urls.tsv"

# cleanup downloads + work to save space (keep only fixed outputs + report)
rm -rf "$WORK"
log ""
log "=== DONE: fixed=$fixed, ok(skipped)=$okcount, failed=$failed ==="
