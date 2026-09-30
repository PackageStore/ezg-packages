#!/usr/bin/env bash
# Dựng thư mục chạy thử giống repo thật: chép mọi file đang track (bản trong working tree, nên sửa quy chuẩn chưa commit
# vẫn được thử). Bỏ .claude/ và mọi folder Skill~ (skill nằm trong module) để nhánh không skill không thấy skill, và
# không nhánh nào đọc được đáp án.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(git -C "$here" rev-parse --show-toplevel)" || { echo "scaffold: $here không nằm trong repo git" >&2; exit 1; }
dest="$PWD"
( cd "$root" && git ls-files -z -- . ':(exclude).claude' ':(exclude,glob)**/Skill~/**' | tar --null -T - -cf - ) \
  | tar -xf - -C "$dest"
