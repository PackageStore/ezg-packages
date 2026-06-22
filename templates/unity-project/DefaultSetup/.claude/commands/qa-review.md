---
description: QA audit toàn bộ pipeline trước khi ship
---

## Đầu vào yêu cầu:
- GDD Final
- TechSpec + Implementation Mapping
- TestReport (từ `/tester`)
- Code (thư mục feature)

## Các bước:

### Phase 1 – Traceability Audit
Kiểm tra chain: GDD ──► TechSpec ──► Code
- Mỗi Design Invariant trong GDD Final → có tương ứng trong TechSpec không? `[OK/MISSING]`
- Mỗi sub-feature trong Implementation Mapping → có file code tương ứng không? `[OK/MISSING]`
- Mỗi Event Definition (TigerForge) trong TechSpec → có implementation không? `[OK/MISSING]`
> Ưu tiên `codegraph_explore`/`codegraph_search` để truy vết, fallback `Grep`/`Read`.

### Phase 2 – Test Coverage Audit
Đọc TestReport:
- Tất cả sub-features có test coverage không?
- Tất cả Exploit scenarios đã được test chưa?
- Có FAIL nào chưa được fix không?
→ Nếu có gap → `[BLOCK]` và liệt kê rõ.

### Phase 3 – Design Invariant Final Check
- Chạy lại 3 Design Invariant quan trọng nhất trực tiếp trên code (không phải spec).
- Verify Economy numbers trong code khớp GDD Final tables: dùng `Grep`/`Read` (hoặc `codegraph`) lấy con số thực tế, đối chiếu từng giá trị.

### Phase 4 – Sign-off Decision
**APPROVED** nếu:
- 0 FAIL Critical/High trong TestReport
- 0 MISSING trong Traceability Audit
- Design Invariants intact trong code

**REJECTED** nếu không đạt → sinh Rejection Report với danh sách cụ thể, assign đúng role (Designer / Developer / Tester):
```
## Rejection Report – [FeatureName]
**Date:** [ngày]
**Reason:** REJECTED

### Các mục cần fix:
| # | Vấn đề | Role xử lý | Độ ưu tiên |
|---|--------|-----------|------------|
| 1 | [mô tả] | Developer | High |

### Ghi chú:
[context thêm nếu cần]
```

### Lưu kết quả:
- Dùng `Write` lưu vào `QA/[FeatureName]-QAReport.md`.

---

## Full pipeline sau khi hoàn thiện
```
/gdd-production ──► /gdd-final
                         │
                         ▼
              /01-feature-analysis
                         │
                         ▼
                  /02-tech-spec
                         │
                         ▼
            /03-implementation-mapping
                         │
                         ▼
                    [Code sinh ra: /new-feature, /new-ui]
                         │
                         ▼
                     /tester ──► FAIL ──► Developer
                         │
                       PASS
                         │
                         ▼
                   /qa-review ──► REJECTED ──► đúng role
                         │
                      APPROVED
                         │
                         ▼
                       SHIP 🚀
```
