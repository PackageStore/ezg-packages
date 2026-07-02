# Changelog — Unity Project Template

Các thay đổi đáng chú ý của template Unity (`templates/unity-project/`, gồm builder + `DefaultSetup/`) được ghi tại đây.

Định dạng mục: **Added** / **Changed** / **Fixed**, mới nhất ở trên cùng.

## 2026-07-02

**Added**
- `DefaultSetup/backlog/_TEMPLATE_WF.md` — Thêm template task hỗ trợ workflow-backed scaffolding.
- `DefaultSetup/.claude/scripts/run-backlog-loop.sh` — Thêm script loop runner hỗ trợ chạy backlog tasks tự động trên macOS/Linux.

**Changed**
- `unity-template.json` — Nâng cấp package `com.ezg.iap` lên phiên bản mới nhất `0.2.0`.
- Cấu hình nhánh phát triển (`branch model`) — Cập nhật branch `agent/dev` tự động lấy base branch hiện tại (`$BASE_BRANCH`) thay vì hardcode `develop`.
- Hỗ trợ task dạng hybrid — Cập nhật `_TEMPLATE_M.md`, `_TEMPLATE_L.md` và các skill liên quan hỗ trợ task kết hợp workflow scaffold và logic tùy biến.

## 2026-07-01

**Added**
- Cấu trúc dự án mẫu — Thêm quy tắc định nghĩa phân chia cấu trúc dự án (framework-standard vs gameplay độc lập) cùng cấu trúc thư mục chuẩn.

**Changed**
- Tinh gọn quy tắc Claude trong DefaultSetup — Rút gọn nội dung compile-validation và định dạng đầu ra (output-format) theo dạng tổng quát hóa, không mang tính dự án cụ thể.
- DefaultSetup CLAUDE.md — Liên kết thêm quy tắc cấu trúc dự án mới để Claude/Agent nắm bắt thông tin.

## 2026-06-30

**Changed**
- unity-template.json — Nâng loạt package Unity lên bản mới nhất chạy được Unity 6.3: addressables 2.7.2→3.1.0, animation.rigging 1.3.0→1.4.1, cinemachine 2.10.4→2.10.7, collab-proxy 2.9.3→2.12.4, formats.fbx 5.1.4→5.1.6, recorder 5.1.2→5.1.6, timeline 1.8.9→1.8.12, visualscripting 1.9.7→1.9.11.
- unity-template.json — Cập nhật package mob-sakai: com.coffee.ui-particle 4.11.2→4.13.2; com.coffee.ui-effect chuyển từ branch `#upm` sang pin tag 5.11.1.
- Publish lại manifest template lên R2 (`unity-template/latest.json`) để client nhận version mới; đồng thời đồng bộ com.unity.purchasing 5.3.1 khớp bản IAP v5.

## 2026-06-27

**Added**
- backlog/_GUARDRAILS.md — Thêm tài liệu định nghĩa chi tiết và cách kiểm thử cho các thẻ guardrails để chuẩn hóa quy trình review.

**Changed**
- unity-template.json — Cập nhật com.ezg.core lên 0.1.2 và com.ezg.featurehub lên 0.1.7.
- Tối ưu hóa backlog review loop — Chỉ chạy performance-reviewer khi phát hiện thay đổi nhạy cảm về hiệu năng, đồng thời tinh gọn prompt gửi cho reviewer để tiết kiệm token.
- Cập nhật các template task — Thay đổi các file mẫu L, M, S để tham chiếu tới danh sách guardrails chung trong _GUARDRAILS.md thay vì liệt kê inline.

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
