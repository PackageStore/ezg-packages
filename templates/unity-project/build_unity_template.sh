#!/usr/bin/env bash
# Thin bootstrap for the EZG Unity template builder.
#
# This file deliberately stays tiny and almost never changes. On every run it downloads the latest
# build logic (build_unity_template.logic.sh) from the server, verifies its SHA-256, and hands over.
# That means end users get logic fixes/improvements automatically without ever replacing this file.
#
# If you only need to tweak the build behaviour, edit build_unity_template.logic.sh and re-publish it
# to the server (see build_unity_template.sha256 sidecar) -- you do NOT need to touch this bootstrap.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRIPT_NAME="$(basename "$0")"

# Public R2 location of the build logic. Override with UNITY_TEMPLATE_SCRIPT_URL to point elsewhere
# (e.g. a staging bucket). The matching ".sha256" sidecar is fetched from "<url>.sha256".
DEFAULT_SCRIPT_URL="https://pub-d76b7e028ac14f9bb044ebd65bccd3d9.r2.dev/unity-template/build_unity_template.logic.sh"
SCRIPT_URL="${UNITY_TEMPLATE_SCRIPT_URL:-$DEFAULT_SCRIPT_URL}"

BOOTSTRAP_CACHE_DIR="$SCRIPT_DIR/.ezg-bootstrap"
LOGIC_FILE="$BOOTSTRAP_CACHE_DIR/build_unity_template.logic.sh"
PAUSE_ON_EXIT="${PAUSE_ON_EXIT:-auto}"

log() { printf '[bootstrap] %s\n' "$*" >&2; }
die() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }

is_windows() {
  case "$(uname -s 2>/dev/null || true)" in
    MINGW*|MSYS*|CYGWIN*) return 0 ;;
    *) return 1 ;;
  esac
}

winpath() {
  if is_windows && command -v cygpath >/dev/null 2>&1; then
    cygpath -w "$1"
  else
    printf '%s\n' "$1"
  fi
}

# Keep the double-clicked window open on a bootstrap failure so the user can read the error. On
# success we exec into the logic, which installs its own pause handler, so this trap never fires.
pause_before_close() {
  local status="$?"
  [ "$status" -ne 0 ] || return 0
  [ "$PAUSE_ON_EXIT" != "never" ] || return "$status"
  if [ "$PAUSE_ON_EXIT" = "auto" ] && ! is_windows; then
    return "$status"
  fi
  printf '\nBootstrap could not start the build. Read the error above, then press Enter to close...' >&2
  if [ -r /dev/tty ]; then
    read -r _ </dev/tty || true
  elif [ -t 0 ]; then
    read -r _ || true
  else
    sleep 10
  fi
  return "$status"
}
trap pause_before_close EXIT

hide_dir() {
  local dir="$1"
  [ -d "$dir" ] || return 0
  if is_windows; then
    if command -v attrib.exe >/dev/null 2>&1; then
      attrib.exe +h "$(winpath "$dir")" >/dev/null 2>&1 || true
    fi
  else
    chflags hidden "$dir" >/dev/null 2>&1 || true
  fi
}

download() {
  local url="$1"
  local target="$2"
  local tmp="${target}.download"
  rm -f "$tmp"

  if command -v curl >/dev/null 2>&1; then
    curl -fL --retry 3 --output "$tmp" "$url" || return 1
  elif command -v wget >/dev/null 2>&1; then
    wget -O "$tmp" "$url" || return 1
  elif is_windows && command -v powershell.exe >/dev/null 2>&1; then
    URL_FOR_DOWNLOAD="$url" TARGET_FOR_DOWNLOAD="$(winpath "$tmp")" powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "\
      Invoke-WebRequest -Uri \$env:URL_FOR_DOWNLOAD -OutFile \$env:TARGET_FOR_DOWNLOAD" || return 1
  else
    die "Cannot download the build logic because curl, wget, and PowerShell are all unavailable."
  fi

  mv "$tmp" "$target"
}

calculate_sha256() {
  local path="$1"
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$path" | awk '{ print tolower($1) }'
    return 0
  fi
  if command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$path" | awk '{ print tolower($1) }'
    return 0
  fi
  if is_windows && command -v powershell.exe >/dev/null 2>&1; then
    FILE_FOR_HASH="$(winpath "$path")" powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "\
      (Get-FileHash -Algorithm SHA256 -LiteralPath \$env:FILE_FOR_HASH).Hash.ToLowerInvariant()" | tr -d '\r'
    return 0
  fi
  return 1
}

mkdir -p "$BOOTSTRAP_CACHE_DIR"
hide_dir "$BOOTSTRAP_CACHE_DIR"

fetched_fresh=0
if download "$SCRIPT_URL" "$LOGIC_FILE"; then
  fetched_fresh=1
else
  log "Could not download the latest build logic from: $SCRIPT_URL"
  [ -f "$LOGIC_FILE" ] || die "No internet connection and no previously downloaded build logic is available. Connect to the internet and run again."
  log "Using the last downloaded build logic (offline mode)."
fi

# Integrity check against the published .sha256 sidecar. This catches corrupted/partial downloads
# and a tampered file on the CDN. We only enforce it for a freshly downloaded logic; in offline mode
# we trust the previously verified cache.
if [ "$fetched_fresh" -eq 1 ]; then
  if download "${SCRIPT_URL}.sha256" "${LOGIC_FILE}.sha256"; then
    expected="$(awk '{ print tolower($1) }' "${LOGIC_FILE}.sha256" | head -n 1)"
    actual="$(calculate_sha256 "$LOGIC_FILE" || true)"
    if [ -n "$expected" ] && [ -n "$actual" ] && [ "$expected" != "$actual" ]; then
      rm -f "$LOGIC_FILE"
      die "Downloaded build logic failed its SHA-256 integrity check (expected $expected, got $actual). Aborting for safety."
    fi
    if [ -z "$actual" ]; then
      log "WARNING: could not compute SHA-256 locally; skipping integrity check."
    fi
  else
    log "WARNING: no SHA-256 sidecar published; skipping integrity check."
  fi
fi

# Hand over to the real builder. exec replaces this process, so the logic's own EXIT/pause handler
# (not this bootstrap's) governs the rest of the run. EZG_SCRIPT_* tell the logic to treat the user's
# folder -- not the cache -- as its working directory.
EZG_SCRIPT_DIR="$SCRIPT_DIR" EZG_SCRIPT_NAME="$SCRIPT_NAME" exec bash "$LOGIC_FILE" "$@"
