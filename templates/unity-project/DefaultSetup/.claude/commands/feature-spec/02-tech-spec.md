---
description: Bước 2 - Sinh Technical Specification chi tiết và Audit
---

# Workflow: Sinh Tech Spec (02-tech-spec)

Bước thứ hai. Nhận file Kiến trúc Hệ thống (từ Workflow 01) và sinh Technical Specification hoàn chỉnh kèm Audit nghiêm ngặt dựa trên `.agents/docs/feature-spec-prompt/02-tech-spec-prompt.md`.

## Các bước thực hiện:

1. **Đọc đầu vào:**
   - Đọc file Phân tích Kiến trúc (từ Bước 1) bằng `Read`.
   - Đọc file tiêu chuẩn Spec: `.agents/docs/feature-spec-prompt/02-tech-spec-prompt.md`.

2. **Sinh cấu trúc Technical Spec:**
   - Áp dụng **BƯỚC 3 (Generate Technical Spec Hoàn Chỉnh)**.
   - Bám sát 100% cấu trúc yêu cầu, chú trọng Data Model và Event Flow (TigerForge `EventManager` + `EventName` constants — xem `.agents/skills/event-manager/SKILL.md`).

3. **Technical Spec Audit (Bắt buộc — KHÔNG bỏ qua):**
   - Áp dụng **BƯỚC 3.5** qua 4 lens: Flow Logic & State Integrity, Exploit & Abuse Scan, Feasibility & Scope Check, Adversarial Reasoning.
   - **Lens 3:** nếu scope quá lớn vượt năng lực/timeline → DỪNG và xuất Warning "Yêu Cầu Tối Giản Hóa GDD" (Explicit Abort Condition).
   - Sửa ngay mọi issue `Critical`/`High` trực tiếp vào nội dung Spec.

4. **Kiểm tra Coverage & UX/UI (Bắt buộc):**
   - Đối chiếu con số trong Tech Spec với GDD gốc (nếu có).
   - Nếu GDD có Visual/Audio → tạo **Section 11. UX/UI Notes** dạng bảng.

5. **Lưu kết quả Technical Spec:**
   - Dùng `Write` lưu vào thư mục `TechSpec/`. Tên file: `[FeatureName]-TechSpec.md`.
   - Báo cáo hoàn thành để chuẩn bị sang Bước 3 (`/03-implementation-mapping`).
