#!/usr/bin/env bash
# Công cụ eval chỉ cho gọi script nằm trong thư mục câu thử; logic chung ở _shared/scaffold.sh.
exec bash "$(dirname "$0")/../_shared/scaffold.sh"
