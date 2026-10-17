#!/usr/bin/env bash
# bughub-wait.sh — wait until the project has a BugHub bug handed to the AI (retry > 0), then exit, spending NO
# tokens: /fix-bug --watch runs this script in the background and the model only wakes up when it ends.
# Windows twin: bughub-wait.ps1 — keep the same arguments, env, exit codes and JSON line.
#
# Polls GET <endpoint>/v1/projects/<code>/pending (public, no token, returns counts only
# {"new":N,"retry":M,…}) every BUGHUB_POLL_INTERVAL seconds. Only `retry` (someone pressed "Giao lại cho AI",
# i.e. hand back to the AI) wakes it up — `new` bugs wait for human triage, the server does not hand them to the AI
# (TechSpec BugHub D13).
#
#   Env:  BUGHUB_ENDPOINT        Worker base URL (default DEFAULT_ENDPOINT below)
#         BUGHUB_POLL_INTERVAL   seconds between two polls, integer ≥ 1 (default 60)
#         BUGHUB_WATCH_HEARTBEAT file touched on every poll (set by `bughub-watch.py start`) — so its watchdog knows
#                                the loop is waiting for bugs, not idle
#
#   Exit: 0  bug found — stdout is exactly one JSON line {"new":N,"retry":M}
#         1  curl / a working Python (python3, python, py -3 — tried in that order) missing, or Python broke
#            midway. A tool error does not go away by itself, so exit to tell the model, do NOT keep polling:
#            `python3` may be only a stub (Windows: the Microsoft Store alias "Python was not found"; macOS without
#            Command Line Tools) — `command -v` still finds it, running it fails, and polling on here would hang
#            silently until someone stops it.
#         2  bad arguments (missing code, extra arguments, code not matching ^[A-Za-z0-9_-]+$, invalid interval or
#            endpoint)
#         3  project does not exist on the server (404)
#   Network error / timeout / 5xx / bad payload → keep polling (stderr is logged only when the error kind changes,
#   to avoid spam).
#
# Usage:  bash .claude/scripts/bughub-wait.sh <projectCode>

set -eu
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

DEFAULT_ENDPOINT="https://bug-reporter.developer-a1f.workers.dev"
DEFAULT_INTERVAL=60
CURL_TIMEOUT=15

usage() {
  echo 'Usage: bash .claude/scripts/bughub-wait.sh <projectCode>   (code: ^[A-Za-z0-9_-]+$)' >&2
}

if [ "$#" -ne 1 ]; then
  usage
  exit 2
fi
code="$1"
# bash's =~ anchors the whole string (POSIX ERE), so a code containing newlines/spaces never matches.
if ! [[ "$code" =~ ^[A-Za-z0-9_-]+$ ]]; then
  echo "bughub-wait: invalid projectCode: '$code'" >&2
  usage
  exit 2
fi

interval="${BUGHUB_POLL_INTERVAL:-$DEFAULT_INTERVAL}"
if ! [[ "$interval" =~ ^[1-9][0-9]*$ ]]; then
  echo "bughub-wait: BUGHUB_POLL_INTERVAL must be an integer >= 1, got '$interval'" >&2
  exit 2
fi

if ! command -v curl >/dev/null 2>&1; then
  echo "bughub-wait: 'curl' is missing" >&2
  exit 1
fi

# Pick Python by ACTUALLY RUNNING it, not just `command -v`: stubs are still on PATH (see Exit 1 above).
PY=()
for cand in python3 python "py -3"; do
  read -r -a argv <<<"$cand"
  if command -v "${argv[0]}" >/dev/null 2>&1 && "${argv[@]}" -c 'import json, sys' >/dev/null 2>&1; then
    PY=("${argv[@]}")
    break
  fi
done
if [ "${#PY[@]}" -eq 0 ]; then
  echo "bughub-wait: no working Python (tried python3, python, py -3 — python3 may be only a Microsoft Store / Xcode Command Line Tools stub). Windows: use bughub-wait.ps1" >&2
  exit 1
fi

endpoint="${BUGHUB_ENDPOINT:-$DEFAULT_ENDPOINT}"
endpoint="${endpoint%/}"
# Only a bare base URL is accepted: https, or http for localhost (mock server in tests). Rejects stray path/query
# and values starting with '-' (curl would take them for an option).
if ! [[ "$endpoint" =~ ^https://[A-Za-z0-9.-]+(:[0-9]+)?$ || "$endpoint" =~ ^http://(127\.0\.0\.1|localhost)(:[0-9]+)?$ ]]; then
  echo "bughub-wait: BUGHUB_ENDPOINT must be https://<host>[:port] (http only for localhost), got '$endpoint'" >&2
  exit 2
fi
url="$endpoint/v1/projects/$code/pending"

# Parsed with Python (no jq dependency). Exit 0 = bug found (prints JSON), 5 = none yet, 4 = bad payload.
# The body goes through stdin, not a path: Windows Python run from Git Bash cannot open paths like /tmp/….
PARSE_PY='
import json, sys
try:
    data = json.loads(sys.stdin.buffer.read().decode("utf-8"))
    new = int(data.get("new", 0))
    retry = int(data.get("retry", 0))
except Exception:
    sys.exit(4)
if retry > 0:
    print(json.dumps({"new": new, "retry": retry}, separators=(",", ":")))
    sys.exit(0)
sys.exit(5)
'

body_file="$(mktemp)"
trap 'rm -f "$body_file"' EXIT

echo "bughub-wait: waiting for bugs of $code ($url, every ${interval}s)" >&2
last_err=""

while :; do
  [ -z "${BUGHUB_WATCH_HEARTBEAT:-}" ] || touch "$BUGHUB_WATCH_HEARTBEAT" 2>/dev/null || true
  # -f so an error page is not taken as payload; -w still prints the HTTP code even when -f makes curl fail.
  status="$(curl -fsS --max-time "$CURL_TIMEOUT" -o "$body_file" -w '%{http_code}' --proto '=http,https' -- "$url" 2>/dev/null)" || true
  err=""
  case "$status" in
    200)
      rc=0
      out="$("${PY[@]}" -c "$PARSE_PY" <"$body_file")" || rc=$?
      case "$rc" in
        0) printf '%s\n' "${out%$'\r'}"; exit 0 ;;   # Windows Python prints \r\n
        5) ;;
        4) err="payload is not JSON {new,retry}" ;;
        *)
          echo "bughub-wait: ${PY[*]} failed reading the payload (exit $rc) — stopping, no more polling" >&2
          exit 1
          ;;
      esac
      ;;
    404)
      echo "bughub-wait: project '$code' does not exist on the server (404) — check bugHub.projectCode / run /bughub-setup" >&2
      exit 3
      ;;
    000|"") err="cannot connect to the server" ;;
    *) err="HTTP $status" ;;
  esac

  if [ "$err" != "$last_err" ]; then
    if [ -n "$err" ]; then
      echo "bughub-wait: $err — still polling" >&2
    else
      echo "bughub-wait: server responding normally again" >&2
    fi
    last_err="$err"
  fi
  sleep "$interval"
done
