#!/usr/bin/env bash
# Dựng project Unity giả vào thư mục chạy thử (thư mục hiện tại), cache Asset Store giả bên cạnh, bằng make_fixture.mjs của skill.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
node "$here/../../scripts/tests/make_fixture.mjs" "$PWD" "$PWD/../assetstore-cache"
