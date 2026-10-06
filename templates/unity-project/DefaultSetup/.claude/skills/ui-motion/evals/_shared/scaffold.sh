#!/usr/bin/env bash
# Dựng project Unity GIẢ vào thư mục chạy thử (cwd): code / prefab tự đặt tên (scripts/tests/make_fixture.mjs), kèm module UI
# Motion ở Assets/Game/Modules/UIMotion trừ khi NO_MODULE=1. Không chép dữ liệu của project thật nào.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
skill="$(cd "$here/../.." && pwd)"
module="${UIMOTION_MODULE:-}"
if [ -z "$module" ]; then
  if [ -f "$skill/../../UIMotionDefaults.cs" ]; then
    module="$(cd "$skill/../.." && pwd)"
  elif [ -f "$skill/install.json" ]; then
    module="$(node -e 'const j=require(process.argv[1]); console.log(j.moduleSource || "")' "$skill/install.json")"
  fi
fi
args=(--out "$PWD")
if [ "${NO_MODULE:-0}" != "1" ]; then
  [ -n "$module" ] || { echo "scaffold: không biết folder module (đặt UIMOTION_MODULE)" >&2; exit 1; }
  args+=(--module "$module" --module-at "Assets/Game/Modules/UIMotion")
fi
node "$skill/scripts/tests/make_fixture.mjs" "${args[@]}"
