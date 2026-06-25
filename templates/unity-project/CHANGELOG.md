# Changelog — Unity Project Template

Các thay đổi đáng chú ý của template Unity (`templates/unity-project/`, gồm builder + `DefaultSetup/`) được ghi tại đây.

Định dạng mục: **Added** / **Changed** / **Fixed**, mới nhất ở trên cùng.

## 2026-06-25

**Changed**
- CLAUDE.md Auto-Inject Rule — Bổ sung quy tắc trong run-backlog SKILL để ngăn chặn việc đọc lại CLAUDE.md nhằm tránh lãng phí tokens.
- Reviewer Models — Thay đổi mô hình AI thực thi cho các agent code-reviewer, performance-reviewer, và security-auditor từ Opus sang Sonnet để tối ưu thời gian chạy.

## 2026-06-24

**Added**
- backlog-preflight.py — bản port Python của preflight, chạy được trên macOS/Linux không cần PowerShell (JSON output giống hệt bản .ps1)
- backlog/ scaffolding — 5 task template (_TEMPLATE + XS/S/M/L) và 4 thư mục vòng đời pending/todo/in-progress/done
- .gitignore Unity generic (ignore Library/, builds, .agents/*, .codegraph/)
- LOCAL-ONLY MODE cho /run-backlog: tự bỏ qua git fetch/pull/push origin khi checkout không có remote

**Changed**
- Preflight trong skill + CLAUDE.md: hỗ trợ song song Windows (.ps1) và macOS/Linux (.py)
- dotnet build: bỏ hardcode tên solution → tự dò .sln trong repo root
- .mcp.json: GITLAB_PROJECT_ID → placeholder ezg-puzzle-space/PROJECT_NAME

**Fixed**
- Sai tên solution m1.sln trong skill run-backlog
- .agents/ từ bản copy stale → symlink trỏ về .claude/ (sửa đổi ở .claude/ tự lan sang)
