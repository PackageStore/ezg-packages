---
description: Bước 1 - Phân tích tính năng và thiết kế hệ thống
---

# Workflow: Phân tích Tính năng (01-feature-analysis)

Bước đầu tiên trong quy trình sinh Technical Specification. Đọc file GDD, phân rã gameplay và tự động audit tính đầy đủ dựa trên `.agents/docs/feature-spec-prompt/01-feature-analysis-prompt.md`.

## Các bước thực hiện:

1. **Đọc đầu vào:**
   - Đọc nội dung file GDD truyền vào workflow bằng `Read`.
   - Đọc file tiêu chuẩn phân tích: `.agents/docs/feature-spec-prompt/01-feature-analysis-prompt.md`.

2. **Phân tích nháp — Phân Rã Gameplay (ngầm trong Thought):**
   - Áp dụng **BƯỚC 1 (Phân Rã Gameplay)** từ prompt lên bản GDD.
   - *Không in các bước nháp này, chỉ xử lý ngầm.*

3. **Completeness Audit (Bắt buộc — KHÔNG bỏ qua):**
   - Áp dụng **BƯỚC 1.5 (Completeness Audit)** lên kết quả Bước 2.
   - Kiểm tra: Brainstorm Verification, Cross-check Completeness, Edge Case Probe, Economy Audit.
   - Format output: `[OK]` / `[FOUND]` / `[RISK]` cho từng mục.
   - Nếu có `[FOUND]` → bổ sung vào kết quả phân rã trước khi tiếp tục.

4. **Map Sang System Kỹ Thuật (ngầm trong Thought):**
   - Áp dụng **BƯỚC 2** lên kết quả đã audit. Cân nhắc State Machine, Event-driven (TigerForge), ScriptableObject config.

5. **Lưu kết quả Phân tích Kiến trúc:**
   - Dùng `Write` lưu kết quả vào thư mục `TechSpec/`. Tên file: `[FeatureName]-Architecture.md`.
   - Báo cáo hoàn thành để chuẩn bị sang Bước 2 (`/02-tech-spec`).
