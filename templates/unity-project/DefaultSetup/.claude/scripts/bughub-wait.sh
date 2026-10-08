#!/usr/bin/env bash
# bughub-wait.sh — chờ tới khi project có bug BugHub đã giao cho AI (retry > 0) rồi thoát, KHÔNG tốn
# token: /fix-bug --watch chạy script này ở background, model chỉ thức dậy khi nó kết thúc.
# Twin Windows: bughub-wait.ps1 — giữ cùng tham số, cùng env, cùng exit code, cùng dòng JSON.
#
# Poll GET <endpoint>/v1/projects/<code>/pending (public, không token, chỉ trả số đếm
# {"new":N,"retry":M,…}) mỗi BUGHUB_POLL_INTERVAL giây. Chỉ `retry` (người đã bấm "Giao lại cho AI")
# đánh thức — bug `new` chờ người duyệt, server không giao cho AI (TechSpec BugHub D13).
#
#   Env:  BUGHUB_ENDPOINT        gốc URL Worker (mặc định DEFAULT_ENDPOINT bên dưới)
#         BUGHUB_POLL_INTERVAL   giây giữa hai lần poll, số nguyên ≥ 1 (mặc định 60)
#
#   Exit: 0  có bug — stdout đúng một dòng JSON {"new":N,"retry":M}
#         1  thiếu curl / python3
#         2  sai tham số (thiếu code, thừa tham số, code lệch ^[A-Za-z0-9_-]+$, interval hoặc endpoint không hợp lệ)
#         3  project không tồn tại trên server (404)
#   Lỗi mạng / timeout / 5xx / payload hỏng → poll tiếp (chỉ log stderr khi loại lỗi đổi, tránh spam).
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
# =~ của bash neo cả chuỗi (POSIX ERE), nên code có xuống dòng/khoảng trắng đều trượt.
if ! [[ "$code" =~ ^[A-Za-z0-9_-]+$ ]]; then
  echo "bughub-wait: projectCode không hợp lệ: '$code'" >&2
  usage
  exit 2
fi

interval="${BUGHUB_POLL_INTERVAL:-$DEFAULT_INTERVAL}"
if ! [[ "$interval" =~ ^[1-9][0-9]*$ ]]; then
  echo "bughub-wait: BUGHUB_POLL_INTERVAL phải là số nguyên ≥ 1, nhận '$interval'" >&2
  exit 2
fi

for dep in curl python3; do
  if ! command -v "$dep" >/dev/null 2>&1; then
    echo "bughub-wait: thiếu '$dep'" >&2
    exit 1
  fi
done

endpoint="${BUGHUB_ENDPOINT:-$DEFAULT_ENDPOINT}"
endpoint="${endpoint%/}"
# Chỉ nhận gốc URL thuần: https, hoặc http cho localhost (mock server khi test). Chặn path/query lạ
# và giá trị bắt đầu bằng '-' (curl hiểu nhầm thành option).
if ! [[ "$endpoint" =~ ^https://[A-Za-z0-9.-]+(:[0-9]+)?$ || "$endpoint" =~ ^http://(127\.0\.0\.1|localhost)(:[0-9]+)?$ ]]; then
  echo "bughub-wait: BUGHUB_ENDPOINT phải là https://<host>[:port] (http chỉ cho localhost), nhận '$endpoint'" >&2
  exit 2
fi
url="$endpoint/v1/projects/$code/pending"

# Parse bằng python3 (không phụ thuộc jq). Exit 0 = có bug (in JSON), 5 = chưa có, 4 = payload hỏng.
PARSE_PY='
import json, sys
try:
    with open(sys.argv[1], encoding="utf-8") as f:
        data = json.load(f)
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

echo "bughub-wait: chờ bug của $code ($url, mỗi ${interval}s)" >&2
last_err=""

while :; do
  # -f để không coi trang lỗi là payload; -w vẫn in mã HTTP kể cả khi -f làm curl trả lỗi.
  status="$(curl -fsS --max-time "$CURL_TIMEOUT" -o "$body_file" -w '%{http_code}' --proto '=http,https' -- "$url" 2>/dev/null)" || true
  err=""
  case "$status" in
    200)
      rc=0
      out="$(python3 -c "$PARSE_PY" "$body_file")" || rc=$?
      case "$rc" in
        0) printf '%s\n' "$out"; exit 0 ;;
        5) ;;
        *) err="payload không phải JSON {new,retry}" ;;
      esac
      ;;
    404)
      echo "bughub-wait: project '$code' không tồn tại trên server (404) — kiểm bugHub.projectCode / chạy /bughub-setup" >&2
      exit 3
      ;;
    000|"") err="không kết nối được server" ;;
    *) err="HTTP $status" ;;
  esac

  if [ "$err" != "$last_err" ]; then
    if [ -n "$err" ]; then
      echo "bughub-wait: $err — vẫn poll tiếp" >&2
    else
      echo "bughub-wait: server trả lời bình thường trở lại" >&2
    fi
    last_err="$err"
  fi
  sleep "$interval"
done
