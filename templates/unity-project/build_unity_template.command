#!/usr/bin/env bash
# Thin bootstrap for the EZG Unity template builder — macOS edition (.command).
#
# Double-clicking this file in Finder opens Terminal and runs the build.
# The file itself stays tiny and rarely changes. On every run it downloads the latest
# build logic (build_unity_template.logic.sh) from the server, verifies its SHA-256,
# and hands over. Logic fixes/improvements reach users automatically without replacing
# this file.
#
# To tweak build behaviour, edit build_unity_template.logic.sh and re-publish it
# to the server (see build_unity_template.sha256 sidecar) — do NOT touch this bootstrap.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRIPT_NAME="$(basename "$0")"

# Public R2 location of the build logic. Override with UNITY_TEMPLATE_SCRIPT_URL to point elsewhere
# (e.g. a staging bucket). The matching ".sha256" sidecar is fetched from "<url>.sha256".
DEFAULT_SCRIPT_URL="https://pub-d76b7e028ac14f9bb044ebd65bccd3d9.r2.dev/unity-template/build_unity_template.logic.sh"
SCRIPT_URL="${UNITY_TEMPLATE_SCRIPT_URL:-$DEFAULT_SCRIPT_URL}"

BOOTSTRAP_CACHE_DIR="$SCRIPT_DIR/.ezg-bootstrap"
LOGIC_FILE="$BOOTSTRAP_CACHE_DIR/build_unity_template.logic.sh"
# Default to "always" so the Terminal window stays open after a double-click.
PAUSE_ON_EXIT="${PAUSE_ON_EXIT:-always}"

log() { printf '[bootstrap] %s\n' "$*" >&2; }
die() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }

# Keep the Terminal window open on a bootstrap failure so the user can read the error.
# On success we exec into the logic, which installs its own pause handler.
pause_before_close() {
  local status="$?"
  [ "$status" -ne 0 ] || return 0
  [ "$PAUSE_ON_EXIT" != "never" ] || return "$status"
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
  chflags hidden "$dir" >/dev/null 2>&1 || true
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
  else
    die "Cannot download the build logic because curl and wget are both unavailable."
  fi

  mv "$tmp" "$target"
}

calculate_sha256() {
  local path="$1"
  if command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$path" | awk '{ print tolower($1) }'
    return 0
  fi
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$path" | awk '{ print tolower($1) }'
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
# and a tampered file on the CDN. Only enforced for a freshly downloaded logic; offline mode
# trusts the previously verified cache.
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
# (not this bootstrap's) governs the rest of the run.
EZG_SCRIPT_DIR="$SCRIPT_DIR" EZG_SCRIPT_NAME="$SCRIPT_NAME" exec bash "$LOGIC_FILE" "$@"
