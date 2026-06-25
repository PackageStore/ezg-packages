---
description: Tự động tạo GDD production từ input concept file
---

# Workflow: GDD Production Generation (gdd-production)

Workflow này nhận một file Concept GDD (hoặc ý tưởng ban đầu) làm tham số đầu vào và thực hiện quy trình chuẩn hóa để tạo ra bản Production GDD chi tiết, áp dụng theo tài liệu `.agents/docs/gdd-prompt/02-gdd-production-generate.md`.

## Các bước thực hiện:

1. **Chuẩn hóa đầu vào:**
   - Đọc nội dung file Concept/Ý tưởng truyền vào (dùng `Read`).
   - Nếu file có đuôi `.txt`, hãy chuyển đổi nội dung sang Markdown (`.md`) với format chuẩn (Headings, Lists, Tables).
   - Tham khảo kỹ quy trình trong `.agents/docs/gdd-prompt/02-gdd-production-generate.md`.

2. **Phase 1 – System Formalization (Hệ thống hóa):**
   - Trích xuất và chốt **DESIGN INVARIANTS** (đánh tag `[INV]`).
   - Phân tích concept thành hệ thống logic: Core gameplay loop, Meta loop, State machine, Entry/Exit/Fail condition.
   - Cấu trúc các thành phần: Player progression path, Competitive metric, Monetization touchpoint.
   - Lập Data Dictionary để chuẩn hóa thuật ngữ và liệt kê Edge cases.
   - *Lưu ý: Không đi sâu vào economy chi tiết ở bước này.*

3. **Phase 2 – Full GDD Generation (Tạo GDD hoàn chỉnh):**
   - Dựa trên nền tảng của Phase 1, tạo Production GDD hoàn chỉnh với đầy đủ **16 phần cụ thể (từ I. OVERVIEW đến XVI. CONFIG TABLE)**.
   - Yêu cầu bắt buộc:
     - Phải có số liệu, bảng biểu và công thức cụ thể cho phần Economy & Monetization ([Project Name] có IAP).
     - Đảm bảo economy được kiểm soát tốt (không runaway).
     - Tuyệt đối không phá Design Invariants đã chốt.
     - Mọi con số tag nguồn gốc (`[DERIVED]`/`[BENCHMARK]`/`[ASSUMED]`), output audit-ready, không narrative dư thừa.

4. **Lưu kết quả:**
   - Dùng `Write` lưu bản Production GDD vào thư mục `GDD/Production/`.
   - Tên file: `[FeatureName]-Production.md`.
