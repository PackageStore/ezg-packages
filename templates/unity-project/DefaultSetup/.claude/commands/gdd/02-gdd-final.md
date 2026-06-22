---
description: Tự động hoàn thiện bản draft GDD thành bản final theo chuẩn validation
---

# Workflow: GDD Finalization (gdd-final)

Workflow này nhận một file GDD draft làm tham số và thực hiện quy trình validation đa tầng để sinh ra bản Final GDD hoàn chỉnh, loại bỏ các lỗi logic, exploit và lỗ hổng kinh tế.

## Các bước thực hiện:

1. **Chuẩn hóa đầu vào:**
   - Đọc file GDD được truyền vào bằng `Read`.
   - Nếu file có đuôi `.txt`, hãy chuyển đổi nội dung sang Markdown chuẩn.
   - Nội dung draft này là cơ sở cho các bước tiếp theo.

2. **Phase 0 – System Extraction (Bóc tách hệ thống):**
   - Áp dụng các mục 0.1, 0.2, 0.3 của `.agents/docs/gdd-prompt/03-gdd-final-generate.md`.
   - Xác định: Feature Classification, System Decomposition, State Integrity Mapping.
   - *Lưu ý: Không được rewrite GDD ở bước này.*

3. **Phase 1 – Single Deep Adversarial Audit (Audit chuyên sâu):**
   - Chạy MỘT vòng Audit toàn diện nhất với Adversarial Role (đóng vai cheater bẻ gãy hệ thống).
   - Forced Constraint: tìm ra ít nhất 1-2 lỗ hổng (Exploit) mức Medium/High về: Flow Logic & State Integrity, Exploit & Optimization Abuse, Retention & Burnout, Economy Stability & Monetization Integrity, Production & Technical Feasibility.
   - Thể hiện mỗi issue theo "ISSUE FORMAT" (Category, Severity, Affected Player Segment, ...). Tuân thủ SEVERITY LOCK RULE.
   - *Lưu ý: Tuyệt đối không rewrite GDD trong phase này.*

4. **Phase 2 – Simulation & Stress Test:**
   - Giả lập Player Segment (F2P, Dolphin, Whale) trong 7 ngày đầu và cả lifecycle.
   - Thử nghiệm Adversarial Optimization. *Không rewrite GDD.*

5. **Phase 3 – Issue Consolidation:**
   - Gộp toàn bộ issue từ Phase 1 và 2, loại trùng, phân nhóm theo Severity.
   - Không rewrite trước khi có đủ Mitigation cho các issue High/Critical.

6. **Phase 4 – Final GDD Generation (Rewrite duy nhất):**
   - Rewrite GDD **một lần duy nhất**, tích hợp toàn bộ mitigation, loại bỏ comment/issue/audit.
   - Xuất kèm **HANDOFF BLOCK — GDD → TECH** theo đúng format trong doc.

7. **Lưu kết quả:**
   - Dùng `Write` lưu bản Final GDD vào thư mục `GDD/Final/`. Tên file: `[FeatureName]-Final.md`.
