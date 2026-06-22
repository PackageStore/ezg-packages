---
description: Kiểm thử tính năng dựa trên GDD Final và TechSpec
---

## Đầu vào yêu cầu:
- File GDD Final → tìm trong `GDD/Final/[FeatureName]-Final.md`
- File TechSpec → tìm trong `TechSpec/[FeatureName]-TechSpec.md`
- Thư mục hoặc file code cần test (user chỉ định hoặc lấy từ Implementation Mapping)

## Các bước:

### Phase 1 – Contract Extraction (ngầm trong thought)
- Đọc GDD Final → trích xuất Design Invariants, Economy rules, Player Segment behavior.
- Đọc TechSpec → trích xuất: Event Definitions (TigerForge), State machine, Data Model, Edge cases đã documented.

### Phase 2 – Test Case Generation
Sinh test cases theo 4 nhóm bắt buộc:

1. **Happy Path Tests** — Core gameplay loop đúng flow; mỗi sub-feature theo Dependency Graph.
2. **Edge Case Tests** — Tất cả edge cases trong TechSpec; boundary values của Economy (min/max resource); state transition hợp lệ và không hợp lệ.
3. **Exploit / Abuse Tests** — Kế thừa trực tiếp từ Adversarial Audit của gdd-final; replay scenario F2P/Dolphin/Whale từ Simulation Phase.
4. **Regression Tests** — CSV field validation (6 resource fields bắt buộc theo skill `.agents/skills/csv-config/SKILL.md`); data consistency cross-system.

### Phase 3 – Test Execution Report
Format mỗi kết quả:
```
[PASS] / [FAIL] / [SKIP] - Tên test
  Severity: Critical / High / Medium / Low
  Expected: ...
  Actual: ...
  Evidence: file:line hoặc log snippet
```
> Để dò evidence trong code, ưu tiên `codegraph_explore`/`codegraph_search`, fallback `Grep`/`Read` (xem `.agents/rules/core-system.md`).

### Lưu kết quả:
- Dùng `Write` lưu vào `TechSpec/[FeatureName]-TestReport.md`.
- Nếu có FAIL Critical/High → KHÔNG tiếp tục QA → trả về Developer kèm link file TestReport.
- Nếu tất cả PASS (hoặc chỉ FAIL Low/Medium đã ghi nhận) → thông báo User và đề xuất chạy `/qa-review`.
