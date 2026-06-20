#!/usr/bin/env bash
set -euo pipefail

# When launched through the thin bootstrap (build_unity_template.sh), the real logic lives in a
# cache folder, so BASH_SOURCE would point there instead of the user's folder. The bootstrap passes
# the original location via EZG_SCRIPT_DIR/EZG_SCRIPT_NAME. Standalone runs fall back to BASH_SOURCE.
SCRIPT_NAME="${EZG_SCRIPT_NAME:-$(basename "$0")}"
SCRIPT_DIR="${EZG_SCRIPT_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)}"
ROOT_BASH_PID="${BASHPID:-$$}"

DEFAULT_PROJECT_NAME="UnityTemplateProject"
PROJECT_NAME_OVERRIDE=""
PROJECT_PATH="$SCRIPT_DIR/$DEFAULT_PROJECT_NAME"
PROJECT_PATH_PROVIDED=0
PACKAGE_TEMPLATE_DIR="$SCRIPT_DIR/PackageTemplate"
TEMPLATE_FILE="$SCRIPT_DIR/unity-template.json"
TEMPLATE_FILE_PROVIDED=0
# By default the script fetches the latest published manifest from the server every run. Pass
# --template-file <path> (or --template-url "") to build from the local unity-template.json.
DEFAULT_TEMPLATE_URL="https://pub-d76b7e028ac14f9bb044ebd65bccd3d9.r2.dev/unity-template/latest.json"
TEMPLATE_URL="${UNITY_TEMPLATE_URL:-$DEFAULT_TEMPLATE_URL}"
DOWNLOAD_CACHE_DIR="$SCRIPT_DIR/.ezg-cache"
DOWNLOAD_CACHE_DIR_CUSTOM=0
DOWNLOAD_CACHE_DIR_PREEXISTED=0
KEEP_DOWNLOAD_CACHE="${KEEP_DOWNLOAD_CACHE:-0}"
UNITY_PATH_OVERRIDE="${UNITY_PATH:-}"
UNITY_VERSION_OVERRIDE="${UNITY_VERSION:-}"
SELECT_UNITY=0
SKIP_IMPORT=0
# Unity build target baked into the generated project launcher (.command/.bat). When left empty it
# defaults per host OS below (Windows -> Android, macOS -> iOS). Override with --build-target or
# BUILD_TARGET to force a specific target regardless of host.
BUILD_TARGET="${BUILD_TARGET:-}"
PAUSE_ON_EXIT="${PAUSE_ON_EXIT:-auto}"
# After a successful build, auto-run the generated launcher (.command on macOS, .bat on Windows) to
# open the freshly created project in Unity. Disable with --no-launch or AUTO_LAUNCH=0 (CI/headless).
AUTO_LAUNCH="${AUTO_LAUNCH:-1}"
LAUNCHER_PATH=""
SCRIPT_SUCCEEDED=0
INSTALL_CURRENT_STEP=0
INSTALL_TOTAL_STEPS=0
MANIFEST_PACKAGE_COUNT=0
UNITYPACKAGE_COUNT=0
PROGRESS_BAR_WIDTH=30

# Persistent transcript so the messages survive after the window scrolls or closes.
BUILD_RUN_LOG="${BUILD_RUN_LOG:-$SCRIPT_DIR/build_unity_template.last-run.log}"
: >"$BUILD_RUN_LOG" 2>/dev/null || true

usage() {
  cat <<USAGE
Usage:
  ./$SCRIPT_NAME [project-path]
  ./$SCRIPT_NAME --project-path <path> [options]
  ./$SCRIPT_NAME --project-name <name> [options]

Options:
  --project-name <name>          Create/update a sibling folder next to this script.
  --project-path <path>          Unity project path to create/update.
  --unity-path <path>            Exact Unity executable to use.
  --unity-version <version>      Prefer an installed Unity version, for example 2022.3.62f1.
  --template-file <path>         Build from this local Unity template JSON (forces local; skips the server).
  --template-url <url>           Download Unity template JSON from this URL (default: published latest.json).
  --package-template-dir <path>  Folder containing .unitypackage and local .tgz files.
  --download-cache-dir <path>    Folder used for downloaded template files.
  --keep-cache                   Keep downloaded cache files after a successful run.
  --select-unity                 Force Unity version selection even in non-interactive runs.
  --skip-import                  Only create/update project and manifest; skip .unitypackage import.
  --build-target <target>        Unity build target for the generated launcher (default by host OS: Windows=Android, macOS=iOS).
  --no-pause                     Do not wait for Enter before the window closes.
  --no-launch                    Do not auto-open the project in Unity after a successful build.
  -h, --help                     Show this help.

Environment overrides:
  UNITY_PATH=<path>              Same as --unity-path.
  UNITY_VERSION=<version>        Same as --unity-version.
  BUILD_TARGET=<target>          Same as --build-target.
  UNITY_TEMPLATE_URL=<url>       Same as --template-url.
  KEEP_DOWNLOAD_CACHE=1          Same as --keep-cache.
  UNITY_HUB_EDITORS_DIR=<path>   Extra Unity Hub Editor folder to scan.
  AUTO_LAUNCH=0                  Same as --no-launch (default 1: open Unity after build).
  PAUSE_ON_EXIT=always|auto|never

Default interactive project folder:
  $DEFAULT_PROJECT_NAME
USAGE
}

log() {
  local line
  line="$(printf '[%s] %s' "$(date '+%H:%M:%S')" "$*")"
  printf '%s\n' "$line" >&2
  printf '%s\n' "$line" >>"$BUILD_RUN_LOG" 2>/dev/null || true
}

repeat_char() {
  local char="$1"
  local count="$2"
  local output=""
  local i

  for ((i = 0; i < count; i++)); do
    output="${output}${char}"
  done

  printf '%s' "$output"
}

render_step_progress_bar() {
  local label="$1"
  local tick="$2"
  local filled empty bar

  filled=$((tick % (PROGRESS_BAR_WIDTH + 1)))
  empty=$((PROGRESS_BAR_WIDTH - filled))
  bar="$(repeat_char "#" "$filled")$(repeat_char "." "$empty")"

  printf '\r\033[K[%s/%s] %s [%s] %ss' "$INSTALL_CURRENT_STEP" "$INSTALL_TOTAL_STEPS" "$label" "$bar" "$tick" >&2
}

begin_install_step() {
  INSTALL_CURRENT_STEP=$((INSTALL_CURRENT_STEP + 1))
  log "[$INSTALL_CURRENT_STEP/$INSTALL_TOTAL_STEPS] $*"
}

run_with_progress_bar() {
  local label="$1"
  local pid tick status
  shift

  if [ ! -t 2 ]; then
    "$@"
    return $?
  fi

  "$@" &
  pid="$!"
  tick=0

  while kill -0 "$pid" 2>/dev/null; do
    render_step_progress_bar "$label" "$tick"
    sleep 1
    tick=$((tick + 1))
  done

  if wait "$pid"; then
    status=0
  else
    status=$?
  fi

  printf '\r\033[K' >&2
  return "$status"
}

run_install_step_with_progress() {
  local label="$1"
  shift

  begin_install_step "$label"
  run_with_progress_bar "$label" "$@"
}

dump_unity_log() {
  local log_file="$1"

  [ -n "$log_file" ] || return 0

  if [ -f "$log_file" ]; then
    {
      printf '\n----- Last 60 lines of Unity log: %s -----\n' "$log_file"
      tail -n 60 "$log_file" || true
      printf '----- End of Unity log -----\n'
    } | tee -a "$BUILD_RUN_LOG" >&2 || true
  else
    printf '\nUnity log not found (Unity may have crashed before writing it): %s\n' "$log_file" | tee -a "$BUILD_RUN_LOG" >&2 || true
  fi
}

# Run a Unity batch step and, on failure, print the tail of its log so the
# error is visible instead of being hidden inside the project folder.
run_install_unity_step() {
  local label="$1"
  local log_file="$2"
  local status
  shift 2

  run_install_step_with_progress "$label" "$@" && status=0 || status=$?
  if [ "$status" -ne 0 ]; then
    dump_unity_log "$log_file"
  fi
  return "$status"
}

pause_before_close() {
  local status="$?"
  local os

  [ "${BASHPID:-$$}" = "$ROOT_BASH_PID" ] || return "$status"
  [ "$PAUSE_ON_EXIT" != "never" ] || return "$status"

  os="$(uname -s 2>/dev/null || true)"
  if [ "$PAUSE_ON_EXIT" = "auto" ]; then
    case "$os" in
      MINGW*|MSYS*|CYGWIN*) ;;
      *) return "$status" ;;
    esac
  fi

  if [ "$SCRIPT_SUCCEEDED" -eq 1 ] && [ "$status" -eq 0 ]; then
    printf '\nDone. Press Enter to close this window...' >&2
  else
    printf '\nScript stopped before finishing. The full output was saved to:\n  %s\nRead it (or the message above), then press Enter to close this window...' "$BUILD_RUN_LOG" >&2
  fi

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

die() {
  printf 'ERROR: %s\n' "$*" >&2
  printf 'ERROR: %s\n' "$*" >>"$BUILD_RUN_LOG" 2>/dev/null || true
  exit 1
}

while [ "$#" -gt 0 ]; do
  case "$1" in
    --project-name)
      [ "$#" -ge 2 ] || die "--project-name requires a value"
      PROJECT_NAME_OVERRIDE="$2"
      PROJECT_PATH_PROVIDED=1
      shift 2
      ;;
    --project-path)
      [ "$#" -ge 2 ] || die "--project-path requires a value"
      PROJECT_PATH="$2"
      PROJECT_PATH_PROVIDED=1
      shift 2
      ;;
    --unity-path)
      [ "$#" -ge 2 ] || die "--unity-path requires a value"
      UNITY_PATH_OVERRIDE="$2"
      shift 2
      ;;
    --unity-version)
      [ "$#" -ge 2 ] || die "--unity-version requires a value"
      UNITY_VERSION_OVERRIDE="$2"
      shift 2
      ;;
    --package-template-dir)
      [ "$#" -ge 2 ] || die "--package-template-dir requires a value"
      PACKAGE_TEMPLATE_DIR="$2"
      shift 2
      ;;
    --template-file)
      [ "$#" -ge 2 ] || die "--template-file requires a value"
      TEMPLATE_FILE="$2"
      TEMPLATE_FILE_PROVIDED=1
      shift 2
      ;;
    --template-url)
      [ "$#" -ge 2 ] || die "--template-url requires a value"
      TEMPLATE_URL="$2"
      shift 2
      ;;
    --download-cache-dir)
      [ "$#" -ge 2 ] || die "--download-cache-dir requires a value"
      DOWNLOAD_CACHE_DIR="$2"
      DOWNLOAD_CACHE_DIR_CUSTOM=1
      shift 2
      ;;
    --keep-cache)
      KEEP_DOWNLOAD_CACHE=1
      shift
      ;;
    --select-unity)
      SELECT_UNITY=1
      shift
      ;;
    --skip-import)
      SKIP_IMPORT=1
      shift
      ;;
    --build-target)
      [ "$#" -ge 2 ] || die "--build-target requires a value"
      BUILD_TARGET="$2"
      shift 2
      ;;
    --no-pause)
      PAUSE_ON_EXIT="never"
      shift
      ;;
    --no-launch)
      AUTO_LAUNCH=0
      shift
      ;;
    -h|--help)
      PAUSE_ON_EXIT="never"
      usage
      exit 0
      ;;
    --*)
      die "Unknown option: $1"
      ;;
    *)
      PROJECT_PATH="$1"
      PROJECT_PATH_PROVIDED=1
      shift
      ;;
  esac
done

detect_os() {
  case "$(uname -s)" in
    Darwin*) printf 'macos' ;;
    MINGW*|MSYS*|CYGWIN*) printf 'windows' ;;
    *) die "Only Windows Git Bash/MSYS/Cygwin and macOS are supported by this script" ;;
  esac
}

OS_NAME="$(detect_os)"

# Pick the default launcher build target from the host OS unless one was given explicitly.
if [ -z "$BUILD_TARGET" ]; then
  case "$OS_NAME" in
    windows) BUILD_TARGET="Android" ;;
    macos) BUILD_TARGET="iOS" ;;
  esac
fi

case "$BUILD_TARGET" in
  *[!A-Za-z0-9]*|"")
    die "Invalid --build-target '$BUILD_TARGET'. Use a Unity build target name like Android, iOS, StandaloneOSX, StandaloneWindows64, or WebGL."
    ;;
esac

to_unix_path() {
  if [ "$OS_NAME" = "windows" ] && command -v cygpath >/dev/null 2>&1; then
    cygpath -u "$1"
  else
    printf '%s\n' "$1"
  fi
}

to_unity_arg_path() {
  if [ "$OS_NAME" = "windows" ] && command -v cygpath >/dev/null 2>&1; then
    cygpath -w "$1"
  else
    printf '%s\n' "$1"
  fi
}

abspath() {
  local target="$1"
  local dir base
  if [ -d "$target" ]; then
    (cd "$target" && pwd)
  else
    dir="$(dirname "$target")"
    base="$(basename "$target")"
    (cd "$dir" && printf '%s/%s\n' "$(pwd)" "$base")
  fi
}

trim_text() {
  printf '%s' "$1" | sed 's/^[[:space:]]*//;s/[[:space:]]*$//'
}

validate_project_name() {
  local name="$1"
  [ -n "$name" ] || return 1

  case "$name" in
    "."|".."|*[\\/:\*\?\"\<\>\|]*)
      return 1
      ;;
  esac

  return 0
}

project_path_from_name() {
  local name
  name="$(trim_text "$1")"
  validate_project_name "$name" || die "Invalid project name: '$1'. Avoid empty names and these characters: / \\ : * ? \" < > |"
  printf '%s/%s\n' "$SCRIPT_DIR" "$name"
}

prompt_project_name() {
  local project_name

  if [ "$PROJECT_PATH_PROVIDED" -eq 1 ]; then
    if [ -n "$PROJECT_NAME_OVERRIDE" ]; then
      PROJECT_PATH="$(project_path_from_name "$PROJECT_NAME_OVERRIDE")"
    fi
    return 0
  fi

  while true; do
    printf 'Project name [%s]: ' "$DEFAULT_PROJECT_NAME" >&2

    if read -r project_name; then
      project_name="$(trim_text "$project_name")"
    else
      project_name=""
    fi

    [ -n "$project_name" ] || project_name="$DEFAULT_PROJECT_NAME"

    if validate_project_name "$project_name"; then
      PROJECT_PATH="$SCRIPT_DIR/$project_name"
      return 0
    fi

    printf 'Invalid project name. Avoid empty names and these characters: / \\ : * ? " < > |\n' >&2
  done
}

unity_version_from_path() {
  local path="$1"
  case "$path" in
    *"/Hub/Editor/"*"/Editor/Unity.exe")
      printf '%s\n' "$path" | sed -E 's#.*Hub/Editor/([^/]+)/Editor/Unity\.exe#\1#'
      ;;
    *"/Hub/Editor/"*"/Unity.app/Contents/MacOS/Unity")
      printf '%s\n' "$path" | sed -E 's#.*Hub/Editor/([^/]+)/Unity\.app/Contents/MacOS/Unity#\1#'
      ;;
    *"/Editor/Unity.exe")
      printf '%s\n' "$path" | sed -E 's#^.*/([^/]+)/Editor/Unity\.exe#\1#'
      ;;
    *"/Unity.app/Contents/MacOS/Unity")
      printf '%s\n' "$path" | sed -E 's#^.*/([^/]+)/Unity\.app/Contents/MacOS/Unity#\1#'
      ;;
    *)
      printf 'unknown'
      ;;
  esac
}

add_unity_candidate() {
  local executable="$1"
  [ -n "$executable" ] || return 0
  executable="$(to_unix_path "$executable")"
  [ -x "$executable" ] || [ -f "$executable" ] || return 0
  printf '%s|%s\n' "$(unity_version_from_path "$executable")" "$executable"
}

find_unity_candidates() {
  local candidate dir drive raw

  if [ "$OS_NAME" = "macos" ]; then
    for dir in \
      "${UNITY_HUB_EDITORS_DIR:-}" \
      "/Applications/Unity/Hub/Editor" \
      "$HOME/Applications/Unity/Hub/Editor"; do
      [ -n "$dir" ] || continue
      [ -d "$dir" ] || continue
      for candidate in "$dir"/*/Unity.app/Contents/MacOS/Unity; do
        [ -e "$candidate" ] && add_unity_candidate "$candidate"
      done
    done

    add_unity_candidate "/Applications/Unity/Unity.app/Contents/MacOS/Unity"
    return 0
  fi

  if [ -n "${UNITY_HUB_EDITORS_DIR:-}" ]; then
    dir="$(to_unix_path "$UNITY_HUB_EDITORS_DIR")"
    if [ -d "$dir" ]; then
      for candidate in "$dir"/*/Editor/Unity.exe; do
        [ -e "$candidate" ] && add_unity_candidate "$candidate"
      done
    fi
  fi

  for drive in /c /d /e; do
    for dir in \
      "$drive/Program Files/Unity/Hub/Editor" \
      "$drive/Program Files (x86)/Unity/Hub/Editor"; do
      [ -d "$dir" ] || continue
      for candidate in "$dir"/*/Editor/Unity.exe; do
        [ -e "$candidate" ] && add_unity_candidate "$candidate"
      done
    done
  done

  if command -v powershell.exe >/dev/null 2>&1; then
    powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "\
      \$paths = @();\
      \$roots = @(\$env:ProgramFiles, \${env:ProgramFiles(x86)}, \$env:ProgramW6432);\
      foreach (\$root in \$roots) {\
        if (-not [string]::IsNullOrWhiteSpace(\$root)) {\
          \$paths += Join-Path \$root 'Unity\\Hub\\Editor\\*\\Editor\\Unity.exe';\
        }\
      }\
      if (-not [string]::IsNullOrWhiteSpace(\$env:LOCALAPPDATA)) {\
        \$paths += Join-Path \$env:LOCALAPPDATA 'Programs\\Unity\\Hub\\Editor\\*\\Editor\\Unity.exe';\
      }\
      foreach (\$p in \$paths) {\
        Get-ChildItem -Path \$p -ErrorAction SilentlyContinue | ForEach-Object { \$_.FullName }\
      }" \
      | tr -d '\r' \
      | while IFS= read -r raw; do add_unity_candidate "$raw"; done

    powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "\
      \$registryRoots = @(\
        'HKLM:\\SOFTWARE\\Unity Technologies\\Installer',\
        'HKCU:\\SOFTWARE\\Unity Technologies\\Installer',\
        'HKLM:\\SOFTWARE\\WOW6432Node\\Unity Technologies\\Installer',\
        'HKCU:\\SOFTWARE\\WOW6432Node\\Unity Technologies\\Installer'\
      );\
      foreach (\$root in \$registryRoots) {\
        if (Test-Path \$root) {\
          Get-ChildItem \$root -ErrorAction SilentlyContinue | Where-Object { \$_.PSChildName -like 'Unity *' } | ForEach-Object {\
            \$props = Get-ItemProperty \$_.PSPath -ErrorAction SilentlyContinue;\
            \$location = \$props.'Location x64';\
            if (-not [string]::IsNullOrWhiteSpace(\$location)) {\
              Join-Path \$location 'Editor\\Unity.exe';\
            }\
          }\
        }\
      }" \
      | tr -d '\r' \
      | while IFS= read -r raw; do add_unity_candidate "$raw"; done
  fi
}

choose_unity() {
  local candidates selected selected_path match_count prompt_index line version path default_index

  if [ -n "$UNITY_PATH_OVERRIDE" ]; then
    selected="$(to_unix_path "$UNITY_PATH_OVERRIDE")"
    [ -f "$selected" ] || die "UNITY_PATH does not point to a Unity executable: $UNITY_PATH_OVERRIDE"
    printf '%s\n' "$selected"
    return 0
  fi

  candidates="$(find_unity_candidates | awk '!seen[$0]++')"
  [ -n "$candidates" ] || die "No Unity installation detected. Set UNITY_PATH or UNITY_HUB_EDITORS_DIR and run again."

  if sort -V </dev/null >/dev/null 2>&1; then
    candidates="$(printf '%s\n' "$candidates" | sort -t '|' -k1,1V)"
  else
    candidates="$(printf '%s\n' "$candidates" | sort)"
  fi
  line="$(printf '%s\n' "$candidates" | tail -n 1)"

  log "Detected Unity versions:"
  printf '%s\n' "$candidates" | awk -F'|' '{ printf "  %d) %s\n", NR, $1 }' >&2

  if [ -n "$UNITY_VERSION_OVERRIDE" ]; then
    match_count="$(printf '%s\n' "$candidates" | awk -F'|' -v version="$UNITY_VERSION_OVERRIDE" '$1 == version { count++ } END { print count + 0 }')"
    [ "$match_count" -gt 0 ] || die "Unity version '$UNITY_VERSION_OVERRIDE' was not found."
    printf '%s\n' "$candidates" | awk -F'|' -v version="$UNITY_VERSION_OVERRIDE" '$1 == version { print $2; exit }'
    return 0
  fi

  if [ "$SELECT_UNITY" -eq 1 ] || [ -t 0 ]; then
    default_index="$(printf '%s\n' "$candidates" | awk 'END { print NR }')"

    while true; do
      printf 'Select Unity number [%s]: ' "$default_index" >&2
      if read -r selected; then
        selected="$(trim_text "$selected")"
      else
        selected=""
      fi
      [ -n "$selected" ] || selected="$default_index"

      selected_path="$(printf '%s\n' "$candidates" | awk -F'|' -v n="$selected" 'NR == n { print $2; exit }')"
      if [ -n "$selected_path" ]; then
        printf '%s\n' "$selected_path"
        return 0
      fi

      printf 'Invalid selection. Please enter a number from 1 to %s.\n' "$default_index" >&2
    done
  fi

  printf '%s\n' "$line" | awk -F'|' '{ print $2 }'
}

find_python() {
  local candidate
  for candidate in python3 python; do
    if command -v "$candidate" >/dev/null 2>&1 && "$candidate" - <<'PY' >/dev/null 2>&1
import json
PY
    then
      printf '%s\n' "$candidate"
      return 0
    fi
  done
  return 1
}

count_manifest_dependencies_with_python() {
  local python_bin="$1"
  # tr -d '\r': Python print() emits CRLF on Windows; strip the CR so callers (read / $(...) /
  # arithmetic) never see a trailing carriage return, mirroring the PowerShell helpers below.
  "$python_bin" - "$TEMPLATE_FILE" <<'PY' | tr -d '\r'
import json
import sys

with open(sys.argv[1], "r", encoding="utf-8-sig") as handle:
    template = json.load(handle)

dependencies = template.get("dependencies", {})
if not isinstance(dependencies, dict):
    raise SystemExit("unity-template.json field 'dependencies' must be an object")

print(len(dependencies))
PY
}

count_manifest_dependencies_with_powershell() {
  local template_arg
  template_arg="$(to_unity_arg_path "$TEMPLATE_FILE")"

  TEMPLATE_FILE_FOR_COUNT="$template_arg" powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "\
    \$path = \$env:TEMPLATE_FILE_FOR_COUNT;\
    \$template = Get-Content -Raw \$path | ConvertFrom-Json;\
    if (\$null -eq \$template.dependencies) {\
      Write-Output 0;\
    } else {\
      Write-Output @(\$template.dependencies.PSObject.Properties).Count;\
    }" | tr -d '\r'
}

count_manifest_dependencies() {
  local python_bin

  if python_bin="$(find_python)"; then
    count_manifest_dependencies_with_python "$python_bin"
    return 0
  fi

  if [ "$OS_NAME" = "windows" ] && command -v powershell.exe >/dev/null 2>&1; then
    count_manifest_dependencies_with_powershell
    return 0
  fi

  die "Cannot read unity-template.json because neither Python nor PowerShell is available."
}

list_template_files_with_python() {
  local python_bin="$1"
  local file_kind="$2"
  # tr -d '\r': strip Windows CRLF so the trailing field (sha256) never carries a CR into the
  # fileName|url|sha256 split, which would break checksum verification.
  "$python_bin" - "$TEMPLATE_FILE" "$file_kind" <<'PY' | tr -d '\r'
import json
import sys

template_file, file_kind = sys.argv[1:3]

with open(template_file, "r", encoding="utf-8-sig") as handle:
    template = json.load(handle)

files = template.get("files", {})
if files is None:
    files = {}
if not isinstance(files, dict):
    raise SystemExit("unity-template.json field 'files' must be an object")

entries = files.get(file_kind, [])
if entries is None:
    entries = []
if not isinstance(entries, list):
    raise SystemExit(f"unity-template.json field 'files.{file_kind}' must be an array")

for entry in entries:
    if not isinstance(entry, dict):
        raise SystemExit(f"Each item in 'files.{file_kind}' must be an object")
    file_name = entry.get("fileName", "")
    if not isinstance(file_name, str) or not file_name:
        raise SystemExit(f"Each item in 'files.{file_kind}' must include fileName")
    url = entry.get("url", "")
    sha256 = entry.get("sha256", "")
    print(f"{file_name}|{url or ''}|{sha256 or ''}")
PY
}

list_template_files_with_powershell() {
  local file_kind="$1"
  local template_arg
  template_arg="$(to_unity_arg_path "$TEMPLATE_FILE")"

  TEMPLATE_FILE_FOR_LIST="$template_arg" TEMPLATE_FILE_KIND="$file_kind" powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "\
    \$template = Get-Content -Raw \$env:TEMPLATE_FILE_FOR_LIST | ConvertFrom-Json;\
    \$files = \$template.files;\
    if (\$null -eq \$files) { exit 0 }\
    \$property = \$files.PSObject.Properties[\$env:TEMPLATE_FILE_KIND];\
    if (\$null -eq \$property) { exit 0 }\
    \$entries = \$property.Value;\
    if (\$null -eq \$entries) { exit 0 }\
    foreach (\$entry in @(\$entries)) {\
      \$fileName = [string]\$entry.fileName;\
      if ([string]::IsNullOrWhiteSpace(\$fileName)) { throw \"Each item in files.\$env:TEMPLATE_FILE_KIND must include fileName\" }\
      \$url = [string]\$entry.url;\
      \$sha256 = [string]\$entry.sha256;\
      Write-Output (\$fileName + \"|\" + \$url + \"|\" + \$sha256);\
    }" | tr -d '\r'
}

list_template_files() {
  local file_kind="$1"
  local python_bin

  if python_bin="$(find_python)"; then
    list_template_files_with_python "$python_bin" "$file_kind"
    return 0
  fi

  if [ "$OS_NAME" = "windows" ] && command -v powershell.exe >/dev/null 2>&1; then
    list_template_files_with_powershell "$file_kind"
    return 0
  fi

  die "Cannot read unity-template.json files because neither Python nor PowerShell is available."
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

  if [ "$OS_NAME" = "windows" ] && command -v powershell.exe >/dev/null 2>&1; then
    FILE_FOR_HASH="$(to_unity_arg_path "$path")" powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "\
      (Get-FileHash -Algorithm SHA256 -LiteralPath \$env:FILE_FOR_HASH).Hash.ToLowerInvariant()" | tr -d '\r'
    return 0
  fi

  die "Cannot calculate SHA-256 because sha256sum, shasum, and PowerShell are unavailable."
}

# Soft check: returns 0 when the file matches (or no checksum is expected), non-zero on
# mismatch -- without dying. Used for the FIRST check of a resolved local file so a stale
# copy can be healed by a single redownload instead of aborting the whole run.
checksum_matches() {
  local path="$1"
  local expected="$2"
  local actual

  [ -n "$expected" ] || return 0

  actual="$(calculate_sha256 "$path")"
  [ "$actual" = "$(printf '%s' "$expected" | tr '[:upper:]' '[:lower:]')" ]
}

verify_file_checksum() {
  local path="$1"
  local expected="$2"
  local actual

  [ -n "$expected" ] || return 0

  actual="$(calculate_sha256 "$path")"
  [ "$actual" = "$(printf '%s' "$expected" | tr '[:upper:]' '[:lower:]')" ] || \
    die "SHA-256 mismatch for $(basename "$path"). Expected $expected but got $actual"
}

hide_cache_directory() {
  local dir="$1"

  [ -d "$dir" ] || return 0

  case "$OS_NAME" in
    windows)
      if command -v attrib.exe >/dev/null 2>&1; then
        attrib.exe +h "$(to_unity_arg_path "$dir")" >/dev/null 2>&1 || true
      elif command -v powershell.exe >/dev/null 2>&1; then
        CACHE_DIR_FOR_HIDE="$(to_unity_arg_path "$dir")" powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "\
          \$item = Get-Item -LiteralPath \$env:CACHE_DIR_FOR_HIDE -ErrorAction SilentlyContinue;\
          if (\$null -ne \$item) { \$item.Attributes = \$item.Attributes -bor [System.IO.FileAttributes]::Hidden }" >/dev/null 2>&1 || true
      fi
      ;;
    macos)
      chflags hidden "$dir" >/dev/null 2>&1 || true
      ;;
  esac
}

mark_download_cache_state() {
  if [ -d "$DOWNLOAD_CACHE_DIR" ]; then
    DOWNLOAD_CACHE_DIR_PREEXISTED=1
    hide_cache_directory "$DOWNLOAD_CACHE_DIR"
  fi
}

cleanup_download_cache() {
  [ "$KEEP_DOWNLOAD_CACHE" != "1" ] || return 0
  [ -d "$DOWNLOAD_CACHE_DIR" ] || return 0

  case "$DOWNLOAD_CACHE_DIR" in
    ""|"/"|"$HOME"|"$SCRIPT_DIR"|"$PROJECT_PATH"|"$PACKAGE_TEMPLATE_DIR")
      log "Skipping cache cleanup because the cache path is not safe to remove: $DOWNLOAD_CACHE_DIR"
      return 0
      ;;
  esac

  if [ "$DOWNLOAD_CACHE_DIR_CUSTOM" -eq 1 ] && [ "$DOWNLOAD_CACHE_DIR_PREEXISTED" -eq 1 ]; then
    log "Leaving existing custom cache directory in place: $DOWNLOAD_CACHE_DIR"
    return 0
  fi

  rm -rf -- "$DOWNLOAD_CACHE_DIR"
  log "Removed download cache: $DOWNLOAD_CACHE_DIR"
}

download_template_file() {
  local url="$1"
  local target="$2"
  local temp_target

  [ -n "$url" ] || die "File missing and no download URL was provided for: $(basename "$target")"

  mkdir -p "$(dirname "$target")"
  hide_cache_directory "$(dirname "$target")"
  temp_target="${target}.download"
  rm -f "$temp_target"

  log "Downloading: $(basename "$target")"
  if command -v curl >/dev/null 2>&1; then
    curl -fL --retry 3 --output "$temp_target" "$url"
  elif command -v wget >/dev/null 2>&1; then
    wget -O "$temp_target" "$url"
  elif [ "$OS_NAME" = "windows" ] && command -v powershell.exe >/dev/null 2>&1; then
    URL_FOR_DOWNLOAD="$url" TARGET_FOR_DOWNLOAD="$(to_unity_arg_path "$temp_target")" powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "\
      Invoke-WebRequest -Uri \$env:URL_FOR_DOWNLOAD -OutFile \$env:TARGET_FOR_DOWNLOAD"
  else
    die "Cannot download $(basename "$target") because curl, wget, and PowerShell are unavailable."
  fi

  mv "$temp_target" "$target"
}

download_remote_template() {
  local remote_template_file

  # An explicit --template-file always wins: build from that local file, never the server.
  if [ "$TEMPLATE_FILE_PROVIDED" -eq 1 ]; then
    log "Using local template file (--template-file): $TEMPLATE_FILE"
    return 0
  fi

  [ -n "$TEMPLATE_URL" ] || return 0

  remote_template_file="$DOWNLOAD_CACHE_DIR/unity-template.remote.json"
  download_template_file "$TEMPLATE_URL" "$remote_template_file"
  TEMPLATE_FILE="$remote_template_file"
  log "Using remote template: $TEMPLATE_URL"
}

resolve_template_file() {
  local file_name="$1"
  local candidate

  candidate="$PACKAGE_TEMPLATE_DIR/$file_name"
  if [ -f "$candidate" ]; then
    printf '%s\n' "$candidate"
    return 0
  fi

  candidate="$DOWNLOAD_CACHE_DIR/$file_name"
  if [ -f "$candidate" ]; then
    printf '%s\n' "$candidate"
    return 0
  fi

  return 1
}

prepare_template_files() {
  local file_kind file_name url sha256 path cache_path

  for file_kind in localPackages unityPackages; do
    while IFS='|' read -r file_name url sha256; do
      [ -n "$file_name" ] || continue

      if path="$(resolve_template_file "$file_name")"; then
        if checksum_matches "$path" "$sha256"; then
          continue
        fi

        # Stale local copy (e.g. an older version still in PackageTemplate or .ezg-cache after
        # the published checksum changed). Heal it with a SINGLE redownload that overwrites the
        # exact path that resolved -- so a stale PackageTemplate copy can't keep shadowing a fresh
        # cache download on later runs -- then verify hard. No loop: if it still mismatches we die.
        [ -n "$url" ] || die "SHA-256 mismatch for $file_name and no download URL to refresh it. Replace the file in PackageTemplate with the matching version."
        log "Checksum mismatch for $file_name; redownloading once to refresh the stale copy."
        rm -f "$path"
        download_template_file "$url" "$path"
        verify_file_checksum "$path" "$sha256"
        continue
      fi

      cache_path="$DOWNLOAD_CACHE_DIR/$file_name"
      download_template_file "$url" "$cache_path"
      verify_file_checksum "$cache_path" "$sha256"
    done < <(list_template_files "$file_kind")
  done
}

count_unitypackages() {
  list_template_files "unityPackages" | wc -l | awk '{ print $1 + 0 }'
}

merge_manifest_with_python() {
  local python_bin="$1"
  local start_step="$2"
  local total_steps="$3"
  local dep_filter="$4"
  "$python_bin" - "$PROJECT_PATH" "$TEMPLATE_FILE" "$PACKAGE_TEMPLATE_DIR" "$DOWNLOAD_CACHE_DIR" "$start_step" "$total_steps" "$dep_filter" <<'PY'
import json
import os
import shutil
import sys

project_path, template_file, template_dir, cache_dir = sys.argv[1:5]
current_step = int(sys.argv[5])
total_steps = int(sys.argv[6])
dep_filter = sys.argv[7] if len(sys.argv) > 7 else "all"
packages_dir = os.path.join(project_path, "Packages")
manifest_path = os.path.join(packages_dir, "manifest.json")

EZG_PREFIX = "com.ezg."

def is_included(name):
    if dep_filter == "exclude-ezg":
        return not name.startswith(EZG_PREFIX)
    if dep_filter == "only-ezg":
        return name.startswith(EZG_PREFIX)
    return True

def log_step(message):
    global current_step
    current_step += 1
    print(f"[{current_step}/{total_steps}] {message}", file=sys.stderr)

def copy_local_file_dependency(value):
    if not isinstance(value, str) or not value.startswith("file:"):
        return

    rel = value[len("file:"):]
    if os.path.isabs(rel):
        return

    file_name = os.path.basename(rel)
    candidates = [
        os.path.join(template_dir, rel),
        os.path.join(template_dir, file_name),
        os.path.join(cache_dir, rel),
        os.path.join(cache_dir, file_name),
    ]
    source = next((candidate for candidate in candidates if os.path.exists(candidate)), None)

    if source:
        target = os.path.join(packages_dir, rel)
        if os.path.abspath(source) != os.path.abspath(target):
            os.makedirs(os.path.dirname(target), exist_ok=True)
            if os.path.isdir(source):
                if os.path.exists(target):
                    shutil.rmtree(target)
                shutil.copytree(source, target)
            else:
                shutil.copy2(source, target)
            print(f"Copied local package: {source} -> {target}")
    else:
        print(f"WARNING: Local file dependency not found in PackageTemplate or download cache: {rel}")

os.makedirs(packages_dir, exist_ok=True)

with open(template_file, "r", encoding="utf-8-sig") as handle:
    template = json.load(handle)

dependencies = template.get("dependencies", {})
if not isinstance(dependencies, dict):
    raise SystemExit("unity-template.json field 'dependencies' must be an object")

scoped_registries = template.get("scopedRegistries", [])
if isinstance(scoped_registries, dict):
    scoped_registries = [scoped_registries]
if not isinstance(scoped_registries, list):
    raise SystemExit("unity-template.json field 'scopedRegistries' must be an array")

if os.path.exists(manifest_path):
    with open(manifest_path, "r", encoding="utf-8-sig") as handle:
        manifest = json.load(handle)
else:
    manifest = {}

manifest.setdefault("dependencies", {})
for name, value in dependencies.items():
    if not is_included(name):
        continue
    log_step(f"Adding manifest package: {name}")
    manifest["dependencies"][name] = value
    copy_local_file_dependency(value)

if scoped_registries:
    registries = manifest.setdefault("scopedRegistries", [])
    for registry in scoped_registries:
        if not isinstance(registry, dict):
            raise SystemExit("Each scoped registry in unity-template.json must be an object")
        replaced = False
        for index, existing in enumerate(registries):
            if existing.get("name") == registry.get("name"):
                registries[index] = registry
                replaced = True
                break
        if not replaced:
            registries.append(registry)

with open(manifest_path, "w", encoding="utf-8", newline="\n") as handle:
    json.dump(manifest, handle, indent=2, ensure_ascii=False)
    handle.write("\n")

print(f"Updated manifest: {manifest_path}")
PY
}

merge_manifest_with_powershell() {
  local start_step="$1"
  local total_steps="$2"
  local dep_filter="$3"
  local temp_script
  temp_script="$(mktemp "${TMPDIR:-/tmp}/unity-manifest-merge.XXXXXX.ps1")"
  cat >"$temp_script" <<'PS1'
param(
  [string]$ProjectPath,
  [string]$TemplateFile,
  [string]$TemplateDir,
  [string]$CacheDir,
  [int]$StartStep,
  [int]$TotalSteps,
  [string]$DepFilter = "all"
)

$ErrorActionPreference = "Stop"
$packagesDir = Join-Path $ProjectPath "Packages"
$manifestPath = Join-Path $packagesDir "manifest.json"
$currentStep = $StartStep
$ezgPrefix = "com.ezg."

function Test-Included([string]$Name) {
  if ($DepFilter -eq "exclude-ezg") { return -not $Name.StartsWith($ezgPrefix) }
  if ($DepFilter -eq "only-ezg") { return $Name.StartsWith($ezgPrefix) }
  return $true
}

function Write-Step([string]$Message) {
  $script:currentStep++
  $line = "[{0}/{1}] {2}" -f $script:currentStep, $TotalSteps, $Message
  [Console]::Error.WriteLine($line)
}

function Ensure-Property([psobject]$Object, [string]$Name, $Value) {
  if ($null -eq $Object.PSObject.Properties[$Name]) {
    $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value
  }
}

function Set-Property([psobject]$Object, [string]$Name, $Value) {
  if ($null -eq $Object.PSObject.Properties[$Name]) {
    $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value
  } else {
    $Object.$Name = $Value
  }
}

New-Item -ItemType Directory -Force -Path $packagesDir | Out-Null

$template = Get-Content -Raw $TemplateFile | ConvertFrom-Json
$dependencies = $template.dependencies
$scopedRegistries = @($template.scopedRegistries)

if ($null -eq $dependencies) {
  $dependencies = [pscustomobject]@{}
}

if (Test-Path $manifestPath) {
  $manifest = Get-Content -Raw $manifestPath | ConvertFrom-Json
} else {
  $manifest = [pscustomobject]@{}
}

Ensure-Property $manifest "dependencies" ([pscustomobject]@{})

foreach ($property in $dependencies.PSObject.Properties) {
  if (-not (Test-Included $property.Name)) { continue }
  Write-Step "Adding manifest package: $($property.Name)"
  Set-Property $manifest.dependencies $property.Name $property.Value

  if (($property.Value -is [string]) -and $property.Value.StartsWith("file:")) {
    $relative = $property.Value.Substring(5)
    if (-not [System.IO.Path]::IsPathRooted($relative)) {
      $fileName = [System.IO.Path]::GetFileName($relative)
      $candidates = @(
        (Join-Path $TemplateDir $relative),
        (Join-Path $TemplateDir $fileName),
        (Join-Path $CacheDir $relative),
        (Join-Path $CacheDir $fileName)
      )
      $source = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
      if ($source) {
        $target = Join-Path $packagesDir $relative
        $targetParent = Split-Path -Parent $target
        New-Item -ItemType Directory -Force -Path $targetParent | Out-Null
        if ((Resolve-Path $source).Path -ne $target) {
          Copy-Item -Force -Recurse $source $target
          Write-Host "Copied local package: $source -> $target"
        }
      } else {
        Write-Warning "Local file dependency not found in PackageTemplate or download cache: $relative"
      }
    }
  }
}

if ($scopedRegistries.Count -gt 0 -and $null -ne $scopedRegistries[0]) {
  Ensure-Property $manifest "scopedRegistries" @()
  $registries = @($manifest.scopedRegistries)
  foreach ($registry in $scopedRegistries) {
    $replaced = $false
    for ($i = 0; $i -lt $registries.Count; $i++) {
      if ($registries[$i].name -eq $registry.name) {
        $registries[$i] = $registry
        $replaced = $true
        break
      }
    }
    if (-not $replaced) {
      $registries += $registry
    }
  }
  $manifest.scopedRegistries = $registries
}

$json = $manifest | ConvertTo-Json -Depth 100
[System.IO.File]::WriteAllText($manifestPath, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
Write-Host "Updated manifest: $manifestPath"
PS1

  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$temp_script" \
    -ProjectPath "$(to_unity_arg_path "$PROJECT_PATH")" \
    -TemplateFile "$(to_unity_arg_path "$TEMPLATE_FILE")" \
    -TemplateDir "$(to_unity_arg_path "$PACKAGE_TEMPLATE_DIR")" \
    -CacheDir "$(to_unity_arg_path "$DOWNLOAD_CACHE_DIR")" \
    -StartStep "$start_step" \
    -TotalSteps "$total_steps" \
    -DepFilter "$dep_filter"
  rm -f "$temp_script"
}

# merge_manifest <filter> <step_count>
#   filter: all | exclude-ezg | only-ezg  (com.ezg.* packages are split into a second pass)
#   step_count: how many packages this pass writes, used to advance the progress counter
merge_manifest() {
  local dep_filter="${1:-all}"
  local step_count="${2:-$MANIFEST_PACKAGE_COUNT}"
  local python_bin

  if python_bin="$(find_python)"; then
    merge_manifest_with_python "$python_bin" "$INSTALL_CURRENT_STEP" "$INSTALL_TOTAL_STEPS" "$dep_filter"
    INSTALL_CURRENT_STEP=$((INSTALL_CURRENT_STEP + step_count))
    return 0
  fi

  if [ "$OS_NAME" = "windows" ] && command -v powershell.exe >/dev/null 2>&1; then
    merge_manifest_with_powershell "$INSTALL_CURRENT_STEP" "$INSTALL_TOTAL_STEPS" "$dep_filter"
    INSTALL_CURRENT_STEP=$((INSTALL_CURRENT_STEP + step_count))
    return 0
  fi

  die "Cannot merge Packages/manifest.json because neither Python nor PowerShell is available."
}

resolve_packages() {
  local resolve_log project_arg log_arg

  resolve_log="$PROJECT_PATH/unity-resolve-packages.log"
  project_arg="$(to_unity_arg_path "$PROJECT_PATH")"
  log_arg="$(to_unity_arg_path "$resolve_log")"

  log "Resolving Unity packages and compiling. On the first run this can take"
  log "several minutes (git packages are downloaded). Watch progress in: $resolve_log"

  if ! run_install_unity_step "Resolving Unity packages" "$resolve_log" \
    "$UNITY_EXECUTABLE" -quit -batchmode -nographics -projectPath "$project_arg" -logFile "$log_arg"; then
    die "Unity failed while resolving packages. Open the log above to see which package could not be resolved or compiled."
  fi
}

# A .unitypackage is a gzip tar of <guid>/{asset,asset.meta,pathname,preview.png} entries.
# We reconstruct Assets/ ourselves so we do not pay a full Unity launch per package and so the
# copy works even while the project does not compile. The asset.meta files are copied byte for
# byte (no re-encoding) so every GUID is preserved exactly.
extract_unitypackages_with_python() {
  local python_bin="$1"
  local start_step="$2"
  local total_steps="$3"
  shift 3
  "$python_bin" - "$PROJECT_PATH" "$start_step" "$total_steps" "$@" <<'PY'
import os
import shutil
import sys
import tarfile

project_path = sys.argv[1]
start_step = int(sys.argv[2])
total_steps = int(sys.argv[3])
packages = sys.argv[4:]

ALLOWED = ("asset", "asset.meta", "pathname", "preview.png")
seen = {}
step = start_step

def safe(rel):
    norm = os.path.normpath(rel)
    if norm.startswith("..") or os.path.isabs(norm):
        return None
    if len(norm) > 1 and norm[1] == ":":
        return None
    return norm

for pkg in packages:
    step += 1
    print(f"[{step}/{total_steps}] Extracting .unitypackage: {os.path.basename(pkg)}", file=sys.stderr)
    with tarfile.open(pkg, "r:gz") as tf:
        groups = {}
        for member in tf.getmembers():
            if not member.isfile():
                continue
            # Some exporters prefix every entry with "./" (AppLovin, Facebook, the
            # com.google.play.* plugins), others do not (Odin, Firebase). Normalize the
            # leading "./" so "<guid>/asset" and "./<guid>/asset" are treated the same.
            name = member.name
            while name.startswith("./"):
                name = name[2:]
            parts = name.split("/")
            if len(parts) != 2 or parts[1] not in ALLOWED:
                continue
            groups.setdefault(parts[0], {})[parts[1]] = member

        for guid, files in groups.items():
            if "pathname" not in files:
                continue
            raw = tf.extractfile(files["pathname"]).read().decode("utf-8", "replace").splitlines()
            rel = raw[0].strip().replace("\\", "/") if raw else ""
            if not rel:
                continue
            norm = safe(rel)
            if norm is None:
                print(f"  WARNING: skipping suspicious path: {rel}", file=sys.stderr)
                continue

            target = os.path.join(project_path, norm)
            if "asset" in files:
                os.makedirs(os.path.dirname(target), exist_ok=True)
                with tf.extractfile(files["asset"]) as src, open(target, "wb") as out:
                    shutil.copyfileobj(src, out)
            else:
                os.makedirs(target, exist_ok=True)

            if "asset.meta" in files:
                with tf.extractfile(files["asset.meta"]) as src, open(target + ".meta", "wb") as out:
                    shutil.copyfileobj(src, out)

            if guid in seen and seen[guid] != norm:
                print(f"  WARNING: GUID {guid} maps to two paths: '{seen[guid]}' and '{norm}'", file=sys.stderr)
            seen[guid] = norm

print(f"Extracted {len(packages)} .unitypackage file(s).", file=sys.stderr)
PY
}

extract_unitypackages_with_powershell() {
  local start_step="$1"
  local total_steps="$2"
  shift 2
  local temp_script list_file project_arg p
  list_file="$(mktemp "${TMPDIR:-/tmp}/unity-unitypackages.XXXXXX.txt")"
  : >"$list_file"
  for p in "$@"; do
    to_unity_arg_path "$p" >>"$list_file"
  done
  project_arg="$(to_unity_arg_path "$PROJECT_PATH")"

  temp_script="$(mktemp "${TMPDIR:-/tmp}/unity-unitypackages.XXXXXX.ps1")"
  cat >"$temp_script" <<'PS1'
param(
  [string]$ProjectPath,
  [int]$StartStep,
  [int]$TotalSteps,
  [string]$PackageListFile
)

$ErrorActionPreference = "Stop"
$tarExe = Join-Path $env:SystemRoot "System32\tar.exe"
if (-not (Test-Path -LiteralPath $tarExe)) { $tarExe = "tar" }

$seen = @{}
$step = $StartStep
$packages = @(Get-Content -LiteralPath $PackageListFile | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

foreach ($pkg in $packages) {
  $step++
  [Console]::Error.WriteLine("[$step/$TotalSteps] Extracting .unitypackage: $([System.IO.Path]::GetFileName($pkg))")

  $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("ezg-upkg-" + [System.IO.Path]::GetRandomFileName())
  New-Item -ItemType Directory -Force -Path $tmp | Out-Null
  try {
    & $tarExe -xzf $pkg -C $tmp
    if ($LASTEXITCODE -ne 0) { throw "tar failed to extract $pkg" }

    foreach ($guidDir in (Get-ChildItem -LiteralPath $tmp -Directory)) {
      $pathnameFile = Join-Path $guidDir.FullName "pathname"
      if (-not (Test-Path -LiteralPath $pathnameFile)) { continue }

      $rel = Get-Content -LiteralPath $pathnameFile -TotalCount 1 -Encoding UTF8
      if ([string]::IsNullOrWhiteSpace($rel)) { continue }
      $rel = $rel.Trim().Replace("\", "/")
      if ($rel.StartsWith("/") -or $rel.Contains("..") -or ($rel -match '^[A-Za-z]:')) {
        [Console]::Error.WriteLine("  WARNING: skipping suspicious path: $rel")
        continue
      }

      $target = Join-Path $ProjectPath ($rel -replace '/', '\')
      $assetFile = Join-Path $guidDir.FullName "asset"
      $metaFile = Join-Path $guidDir.FullName "asset.meta"
      $targetParent = Split-Path -Parent $target
      if ($targetParent) { New-Item -ItemType Directory -Force -Path $targetParent | Out-Null }

      if (Test-Path -LiteralPath $assetFile) {
        Copy-Item -LiteralPath $assetFile -Destination $target -Force
      } else {
        New-Item -ItemType Directory -Force -Path $target | Out-Null
      }

      if (Test-Path -LiteralPath $metaFile) {
        Copy-Item -LiteralPath $metaFile -Destination ($target + ".meta") -Force
      }

      $guid = $guidDir.Name
      if ($seen.ContainsKey($guid) -and $seen[$guid] -ne $rel) {
        [Console]::Error.WriteLine("  WARNING: GUID $guid maps to two paths: '$($seen[$guid])' and '$rel'")
      }
      $seen[$guid] = $rel
    }
  } finally {
    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
  }
}

[Console]::Error.WriteLine("Extracted $($packages.Count) .unitypackage file(s).")
PS1

  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$temp_script" \
    -ProjectPath "$project_arg" \
    -StartStep "$start_step" \
    -TotalSteps "$total_steps" \
    -PackageListFile "$(to_unity_arg_path "$list_file")"
  rm -f "$temp_script" "$list_file"
}

extract_unitypackages() {
  local file_name url sha256 package
  local -a packages=()

  while IFS='|' read -r file_name url sha256; do
    [ -n "$file_name" ] || continue
    if ! package="$(resolve_template_file "$file_name")"; then
      die "Declared .unitypackage file was not found after preparation: $file_name"
    fi
    packages+=("$package")
  done < <(list_template_files "unityPackages")

  if [ "${#packages[@]}" -eq 0 ]; then
    log "No .unitypackage files declared in: $TEMPLATE_FILE"
    return 0
  fi

  log "Extracting ${#packages[@]} .unitypackage file(s) into Assets/ (preserving .meta GUIDs)..."

  local python_bin
  if python_bin="$(find_python)"; then
    extract_unitypackages_with_python "$python_bin" "$INSTALL_CURRENT_STEP" "$INSTALL_TOTAL_STEPS" "${packages[@]}"
    INSTALL_CURRENT_STEP=$((INSTALL_CURRENT_STEP + ${#packages[@]}))
    return 0
  fi

  if [ "$OS_NAME" = "windows" ] && command -v powershell.exe >/dev/null 2>&1; then
    extract_unitypackages_with_powershell "$INSTALL_CURRENT_STEP" "$INSTALL_TOTAL_STEPS" "${packages[@]}"
    INSTALL_CURRENT_STEP=$((INSTALL_CURRENT_STEP + ${#packages[@]}))
    return 0
  fi

  die "Cannot extract .unitypackage files because neither Python nor PowerShell is available."
}

# Read a top-level array-of-strings field (e.g. removePaths, embedPackages) from the template.
list_template_string_array_with_python() {
  local python_bin="$1"
  local field="$2"
  # tr -d '\r': strip Windows CRLF so values feeding `read` never carry a trailing CR.
  "$python_bin" - "$TEMPLATE_FILE" "$field" <<'PY' | tr -d '\r'
import json
import sys

template_file, field = sys.argv[1:3]

with open(template_file, "r", encoding="utf-8-sig") as handle:
    template = json.load(handle)

values = template.get(field, [])
if values is None:
    values = []
if not isinstance(values, list):
    raise SystemExit(f"unity-template.json field '{field}' must be an array")

for entry in values:
    if isinstance(entry, str) and entry.strip():
        print(entry.strip())
PY
}

list_template_string_array_with_powershell() {
  local field="$1"
  local template_arg
  template_arg="$(to_unity_arg_path "$TEMPLATE_FILE")"

  TEMPLATE_FILE_FOR_ARRAY="$template_arg" TEMPLATE_ARRAY_FIELD="$field" powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "\
    \$template = Get-Content -Raw \$env:TEMPLATE_FILE_FOR_ARRAY | ConvertFrom-Json;\
    \$field = \$env:TEMPLATE_ARRAY_FIELD;\
    \$prop = \$template.PSObject.Properties[\$field];\
    if (\$null -eq \$prop -or \$null -eq \$prop.Value) { exit 0 }\
    foreach (\$entry in @(\$prop.Value)) {\
      if (-not [string]::IsNullOrWhiteSpace([string]\$entry)) { Write-Output ([string]\$entry).Trim() }\
    }" | tr -d '\r'
}

list_template_string_array() {
  local field="$1"
  local python_bin
  if python_bin="$(find_python)"; then
    list_template_string_array_with_python "$python_bin" "$field"
    return 0
  fi
  if [ "$OS_NAME" = "windows" ] && command -v powershell.exe >/dev/null 2>&1; then
    list_template_string_array_with_powershell "$field"
    return 0
  fi
  return 0
}

# Delete project-relative paths declared in template "removePaths" (e.g. SDK Example/Demo
# folders that ship broken sample scripts). Runs after extraction so the single resolve pass
# never sees them.
apply_remove_paths() {
  local rel target removed=0

  while IFS= read -r rel; do
    [ -n "$rel" ] || continue
    rel="${rel//\\//}"
    rel="${rel#/}"
    case "$rel" in
      ""|*..*|*:*)
        log "Skipping unsafe removePaths entry: $rel"
        continue
        ;;
    esac

    target="$PROJECT_PATH/$rel"
    if [ -e "$target" ] || [ -L "$target" ]; then
      rm -rf -- "$target"
      rm -f -- "$target.meta"
      log "Removed declared path: $rel"
      removed=$((removed + 1))
    fi
  done < <(list_template_string_array "removePaths")

  [ "$removed" -eq 0 ] || log "Removed $removed declared path(s) before compiling."
}

# Some packages (e.g. com.google.play.games / GPGS) refuse to run from the immutable PackageCache
# because their editor code hard-requires a real Packages/<name> folder. For each name in the
# template "embedPackages", copy the package Unity resolved into Library/PackageCache out to
# Packages/<name> so it becomes an embedded package. Runs AFTER resolve_packages populated the cache.
apply_embed_packages() {
  local name dst embedded=0
  local -a matches

  while IFS= read -r name; do
    [ -n "$name" ] || continue
    case "$name" in
      */*|*\\*|*..*|*:*)
        log "Skipping unsafe embedPackages entry: $name"
        continue
        ;;
    esac

    dst="$PROJECT_PATH/Packages/$name"
    matches=( "$PROJECT_PATH/Library/PackageCache/${name}@"* )

    if [ -d "${matches[0]}" ]; then
      rm -rf -- "$dst"
      cp -r -- "${matches[0]}" "$dst"
      log "Embedded package into Packages/: $name (from $(basename "${matches[0]}"))"
      embedded=$((embedded + 1))
    elif [ -d "$dst" ]; then
      log "Package already embedded: $name"
    else
      log "WARNING: could not find resolved '$name' in Library/PackageCache to embed."
    fi
  done < <(list_template_string_array "embedPackages")

  [ "$embedded" -eq 0 ] || log "Embedded $embedded package(s) into Packages/."
}

# A Unity project is just a folder with Assets/ and ProjectSettings/ProjectVersion.txt. We
# create that skeleton ourselves instead of launching Unity with -createProject, because
# -createProject -quit races the Package Manager (it tries to resolve before manifest.json
# exists, then -quit cancels it -> exit 1 and a half-written project). Writing the skeleton is
# deterministic and lets the single resolve pass later be the only Unity launch.
project_needs_creation() {
  local version_file="$PROJECT_PATH/ProjectSettings/ProjectVersion.txt"
  [ -f "$version_file" ] || return 0
  grep -q "UnknownUnityVersion" "$version_file" 2>/dev/null && return 0
  return 1
}

create_project_skeleton() {
  local version_file="$PROJECT_PATH/ProjectSettings/ProjectVersion.txt"

  begin_install_step "Creating Unity project ($UNITY_SELECTED_VERSION)"
  mkdir -p "$PROJECT_PATH/Assets" "$PROJECT_PATH/Packages" "$PROJECT_PATH/ProjectSettings"
  {
    printf 'm_EditorVersion: %s\n' "$UNITY_SELECTED_VERSION"
    printf 'm_EditorVersionWithRevision: %s\n' "$UNITY_SELECTED_VERSION"
  } >"$version_file"
  log "Created Unity project skeleton at: $PROJECT_PATH"
}

# Write a double-clickable launcher inside the project folder so the user can re-open it in Unity
# (with the right -buildTarget) without re-running this whole build. On macOS we emit a .command
# that mirrors the hand-written launchers; on Windows a .bat. Both re-detect the Unity version from
# ProjectSettings/ProjectVersion.txt at run time, so they keep working after Unity Hub updates the
# editor. The __BUILD_TARGET__ placeholder is substituted with $BUILD_TARGET after the heredoc so the
# heredoc body itself stays unexpanded (its $... refer to the launcher's own runtime, not this build).
generate_launcher() {
  local project_name launcher tmp

  project_name="$(basename "$PROJECT_PATH")"

  if [ "$OS_NAME" = "windows" ]; then
    launcher="$PROJECT_PATH/${project_name}.bat"
    tmp="${launcher}.tmp"
    cat >"$tmp" <<'LAUNCHER'
@echo off
setlocal
cd /d "%~dp0"

set "UNITY_VERSION="
for /f "tokens=2" %%i in ('findstr /b "m_EditorVersion:" "ProjectSettings\ProjectVersion.txt"') do set "UNITY_VERSION=%%i"
if not defined UNITY_VERSION (
  echo Could not determine Unity version from ProjectSettings\ProjectVersion.txt
  pause
  exit /b 1
)
echo Detected Unity version: %UNITY_VERSION%

set "UNITY_EXE=%ProgramFiles%\Unity\Hub\Editor\%UNITY_VERSION%\Editor\Unity.exe"
if not exist "%UNITY_EXE%" set "UNITY_EXE=%ProgramFiles(x86)%\Unity\Hub\Editor\%UNITY_VERSION%\Editor\Unity.exe"
if not exist "%UNITY_EXE%" set "UNITY_EXE=%LOCALAPPDATA%\Programs\Unity\Hub\Editor\%UNITY_VERSION%\Editor\Unity.exe"
if not exist "%UNITY_EXE%" (
  echo Unity application not found for version %UNITY_VERSION%.
  echo Please ensure Unity %UNITY_VERSION% is installed via Unity Hub.
  pause
  exit /b 1
)

echo Opening Unity Editor...
start "" "%UNITY_EXE%" -projectPath "%CD%" -buildTarget __BUILD_TARGET__
endlocal
LAUNCHER
    # .bat files want CRLF line endings to run reliably from a Windows double-click.
    sed "s|__BUILD_TARGET__|${BUILD_TARGET}|g" "$tmp" | awk 'BEGIN { ORS = "\r\n" } { print }' >"$launcher"
    rm -f "$tmp"
  else
    launcher="$PROJECT_PATH/${project_name}.command"
    tmp="${launcher}.tmp"
    cat >"$tmp" <<'LAUNCHER'
#!/bin/bash
# Auto-generated launcher: re-open this project in Unity with the chosen build target.
cd "$(dirname "$0")" || exit

UNITY_VERSION=$(awk '/m_EditorVersion:/ {print $2}' ProjectSettings/ProjectVersion.txt)
if [ -z "$UNITY_VERSION" ]; then
    echo "Could not determine Unity version from ProjectSettings/ProjectVersion.txt"
    exit 1
fi
echo "Detected Unity version: $UNITY_VERSION"

UNITY_APP_PATH="/Applications/Unity/Hub/Editor/$UNITY_VERSION/Unity.app"
if [ ! -d "$UNITY_APP_PATH" ] && [ -d "$HOME/Applications/Unity/Hub/Editor/$UNITY_VERSION/Unity.app" ]; then
    UNITY_APP_PATH="$HOME/Applications/Unity/Hub/Editor/$UNITY_VERSION/Unity.app"
fi
if [ ! -d "$UNITY_APP_PATH" ]; then
    echo "Unity application not found at: $UNITY_APP_PATH"
    echo "Please ensure Unity $UNITY_VERSION is installed via Unity Hub."
    exit 1
fi

# Check if Unity Hub is already running
HUB_WAS_RUNNING=$(pgrep -x "Unity Hub")

echo "Opening Unity Editor..."
"$UNITY_APP_PATH/Contents/MacOS/Unity" -projectPath "$(pwd)" -buildTarget __BUILD_TARGET__ > /dev/null 2>&1 &

# If Unity Hub was not running before, quit it after a delay to allow licensing to complete
if [ -z "$HUB_WAS_RUNNING" ]; then
    (sleep 20 && osascript -e 'tell application "System Events" to if process "Unity Hub" exists then tell application "Unity Hub" to quit') &
fi
LAUNCHER
    sed "s|__BUILD_TARGET__|${BUILD_TARGET}|g" "$tmp" >"$launcher"
    rm -f "$tmp"
    chmod +x "$launcher"
  fi

  LAUNCHER_PATH="$launcher"
  log "Created project launcher: $launcher (build target: $BUILD_TARGET)"
}

# Auto-open the freshly built project in Unity by running the launcher generate_launcher just wrote
# (.command on macOS, .bat on Windows). Both launchers start Unity in the background and return
# immediately, so this never blocks the script's own exit/pause. A launcher failure is logged as a
# warning, not fatal: the project is already built and can be opened by hand. Disable with
# --no-launch or AUTO_LAUNCH=0.
run_launcher() {
  if [ "$AUTO_LAUNCH" != "1" ]; then
    log "Auto-launch disabled (--no-launch / AUTO_LAUNCH=0); skipping opening Unity."
    return 0
  fi
  if [ -z "$LAUNCHER_PATH" ] || [ ! -f "$LAUNCHER_PATH" ]; then
    log "No launcher found to auto-run; skipping opening Unity."
    return 0
  fi

  log "Auto-opening the project in Unity via launcher: $LAUNCHER_PATH"
  if [ "$OS_NAME" = "windows" ]; then
    if command -v cmd.exe >/dev/null 2>&1; then
      MSYS2_ARG_CONV_EXCL="*" cmd.exe //c "$(to_unity_arg_path "$LAUNCHER_PATH")" \
        || log "WARNING: launcher did not start cleanly: $LAUNCHER_PATH"
    else
      log "WARNING: cmd.exe is unavailable; open the project manually with: $LAUNCHER_PATH"
    fi
  else
    bash "$LAUNCHER_PATH" || log "WARNING: launcher did not start cleanly: $LAUNCHER_PATH"
  fi
}

# Unity pops a "Missing Signature" dialog on every editor open when a project pulls packages from a
# self-hosted scoped registry (ours is unsigned). Pre-set oneTimeWarningShown=1 in
# ProjectSettings/PackageManagerSettings.asset so the dialog never appears. When the asset already
# exists (Unity recreates it during the resolve pass) we flip just that flag in place to preserve any
# other settings; otherwise we write a minimal valid asset so the value is in place before the first
# editor launch. Idempotent: re-flipping an already-1 flag is a no-op.
suppress_missing_signature_warning() {
  local settings_file="$PROJECT_PATH/ProjectSettings/PackageManagerSettings.asset"

  if [ -f "$settings_file" ]; then
    if grep -q "oneTimeWarningShown:" "$settings_file"; then
      sed "s/oneTimeWarningShown: [0-9]*/oneTimeWarningShown: 1/" "$settings_file" >"${settings_file}.tmp" \
        && mv "${settings_file}.tmp" "$settings_file"
      log "Suppressed Missing Signature warning (oneTimeWarningShown=1) in existing PackageManagerSettings.asset"
    fi
    return
  fi

  mkdir -p "$PROJECT_PATH/ProjectSettings"
  cat >"$settings_file" <<'PMSETTINGS'
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &1
MonoBehaviour:
  m_ObjectHideFlags: 53
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 0}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 13964, guid: 0000000000000000e000000000000000, type: 0}
  m_Name:
  m_EditorClassIdentifier:
  m_EnablePreReleasePackages: 0
  m_AdvancedSettingsExpanded: 1
  m_ScopedRegistriesSettingsExpanded: 1
  m_SeeAllPackageVersions: 0
  m_DismissPreviewPackagesInUse: 0
  oneTimeWarningShown: 1
  oneTimeDeprecatedPopUpShown: 0
  m_Registries:
  - m_Id: main
    m_Name:
    m_Url: https://packages.unity.com
    m_Scopes: []
    m_IsDefault: 1
    m_Capabilities: 7
    m_ConfigSource: 0
    m_Compliance:
      m_Status: 0
      m_Violations: []
  m_UserSelectedRegistryName:
  m_UserAddingNewScopedRegistry: 0
  m_RegistryInfoDraft:
    m_Modified: 0
    m_ErrorMessage:
    m_UserModificationsInstanceId: 0
    m_OriginalInstanceId: 0
  m_LoadAssets: 0
PMSETTINGS
  log "Wrote PackageManagerSettings.asset with Missing Signature warning suppressed (oneTimeWarningShown=1)"
}

prompt_project_name

if [ "$OS_NAME" = "windows" ]; then
  PROJECT_PATH="$(to_unix_path "$PROJECT_PATH")"
  PACKAGE_TEMPLATE_DIR="$(to_unix_path "$PACKAGE_TEMPLATE_DIR")"
  TEMPLATE_FILE="$(to_unix_path "$TEMPLATE_FILE")"
  DOWNLOAD_CACHE_DIR="$(to_unix_path "$DOWNLOAD_CACHE_DIR")"
fi

PROJECT_PATH="$(abspath "$PROJECT_PATH")"
PACKAGE_TEMPLATE_DIR="$(abspath "$PACKAGE_TEMPLATE_DIR")"
TEMPLATE_FILE="$(abspath "$TEMPLATE_FILE")"
DOWNLOAD_CACHE_DIR="$(abspath "$DOWNLOAD_CACHE_DIR")"
mark_download_cache_state

if [ ! -d "$PACKAGE_TEMPLATE_DIR" ]; then
  mkdir -p "$PACKAGE_TEMPLATE_DIR"
  log "Created missing package template folder: $PACKAGE_TEMPLATE_DIR"
fi
download_remote_template
[ -f "$TEMPLATE_FILE" ] || die "Unity template file not found: $TEMPLATE_FILE"

UNITY_EXECUTABLE="$(choose_unity)"
[ -n "$UNITY_EXECUTABLE" ] || die "Unity executable selection failed."
UNITY_SELECTED_VERSION="$(unity_version_from_path "$UNITY_EXECUTABLE")"

log "OS detected: $OS_NAME"
log "Unity selected: $UNITY_SELECTED_VERSION"
log "Project path: $PROJECT_PATH"
log "Preparing template files..."
prepare_template_files

MANIFEST_PACKAGE_COUNT="$(count_manifest_dependencies)"
if [ "$SKIP_IMPORT" -eq 0 ]; then
  UNITYPACKAGE_COUNT="$(count_unitypackages)"
else
  UNITYPACKAGE_COUNT=0
fi

NEED_CREATE=0
if project_needs_creation; then
  NEED_CREATE=1
fi

INSTALL_TOTAL_STEPS=$((MANIFEST_PACKAGE_COUNT + UNITYPACKAGE_COUNT))
if [ "$NEED_CREATE" -eq 1 ]; then
  INSTALL_TOTAL_STEPS=$((INSTALL_TOTAL_STEPS + 1))
fi
if [ "$SKIP_IMPORT" -eq 0 ]; then
  # Extra step: the single Unity pass that resolves UPM packages and compiles everything.
  INSTALL_TOTAL_STEPS=$((INSTALL_TOTAL_STEPS + 1))
fi

log "Install progress: 0/$INSTALL_TOTAL_STEPS"

mkdir -p "$PROJECT_PATH"

if [ "$NEED_CREATE" -eq 1 ]; then
  create_project_skeleton
else
  log "Existing Unity project detected. It will be updated."
fi

# Write the launcher now, right after the project skeleton exists, BEFORE the import/compile pass.
# resolve_packages can die on script compile errors (common when SDKs first land), so creating the
# launcher here guarantees the user still gets a double-clickable .command/.bat even if compilation
# fails. The launcher reads the Unity version at its own run time, so it does not need a compiled project.
generate_launcher

# Write the full manifest in one pass. Order no longer matters: extraction (below) drops SDK
# assets into Assets/ without launching Unity, so the project never needs to compile until the
# single final resolve pass when everything is already in place.
log "Updating Packages/manifest.json with $MANIFEST_PACKAGE_COUNT package(s)..."
merge_manifest "all" "$MANIFEST_PACKAGE_COUNT"

# Pre-suppress the "Missing Signature" dialog before Unity ever opens (our scoped registry is unsigned).
suppress_missing_signature_warning

if [ "$SKIP_IMPORT" -eq 0 ]; then
  # Extract the .unitypackage SDKs straight into Assets/ (no per-package Unity launch), drop any
  # declared sample/demo paths, then do one Unity pass that resolves UPM packages and compiles
  # everything together.
  extract_unitypackages
  apply_remove_paths
  resolve_packages
  apply_embed_packages
else
  log "Skipping .unitypackage extraction because --skip-import was provided."
fi

cleanup_download_cache
log "Done. Open the project in Unity: $PROJECT_PATH"
SCRIPT_SUCCEEDED=1

# Project is fully built; auto-open it in Unity by running the generated launcher.
run_launcher
