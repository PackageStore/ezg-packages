# CHUỖI PROMPT PHÂN RÃ TÍNH NĂNG - BƯỚC 2: GENERATE & AUDIT TECH SPEC

## BƯỚC 3: Generate Technical Spec Hoàn Chỉnh

**System/Role:** Bạn là Senior Unity Technical Architect.

**Instruction:** Dựa trên đầu vào phân tích kiến trúc (Bước 1 và 2), hãy tạo Technical Specification hoàn chỉnh cho feature này.

**Yêu cầu cấu trúc:**

```markdown
# 1. Overview

# 2. Gameplay Flow

# 3. State Machine (nếu có)
- Danh sách state
- Bảng transition
- Sơ đồ logic mô tả bằng text

# 4. System Architecture
- Danh sách class
- Responsibility của từng class
- Quan hệ giữa các class

# 5. Data Model
- Runtime data
- Config data (ScriptableObject)
- Save data (nếu cần)
- Server/Client Data (nếu cần)

# 6. Event & Network Flow
- Internal Events (Publisher, Subscriber)
- Network Payload (Request/Response)
- Thứ tự event và API calls

# 6.5. Economy & Monetization
- IAP Packages (tên, giá, nội dung, trigger)
- Ads Placements (loại, reward, giới hạn/ngày)
- Currency Flow (source → sink diagram)
- Shop/Merchant System (nếu có)

# 7. Edge Cases

# 8. Performance Considerations (Mobile)

# 9. Testing Strategy
```

---

## BƯỚC 3.5: Technical Spec Audit (Tự Kiểm Tra Spec)

**System/Role:** Bạn là Senior QA Architect kiêm Adversarial Tester.

**Instruction:** Dựa trên Technical Spec vừa sinh ở Bước 3, thực hiện 1 vòng audit toàn diện. KHÔNG được bỏ qua. Nếu phát hiện issue Critical/High → phải sửa Spec ở phía trên thay vì chỉ ghi nhận lỗi.

**Audit Lenses:**

### Lens 1: Flow Logic & State Integrity
- Dead state (vào được, không ra được)
- Unreachable state (không ai trigger)
- Infinite loop (state cycle không có exit)
- Missing transition (2 state không có đường nối)
- Condition conflict (2 transition cùng trigger nhưng target khác nhau)
- System contradiction (2 system mâu thuẫn logic)

### Lens 2: Exploit & Abuse Scan
- Abuse stacking (combo buff/item/skill vượt dự kiến)
- Reward farming loophole (lặp action để farm vô hạn)
- Infinite scaling (không có diminishing returns / cap)
- Pay-to-win bypass (IAP cho phép skip toàn bộ skill ceiling)
- Edge-case abuse (sử dụng edge case để gain advantage)

### Lens 3: Feasibility & Scope Check
- Scope có vượt năng lực team không?
- Dependency chain có quá phức tạp không?
- QA surface area có quá lớn không?
- Risk bug cascade (1 bug gây chain reaction)?
- Maintenance cost có hợp lý không?
**[EXPLICIT ABORT CONDITION]**: Trường hợp Lens 3 thất bại nặng (Scope vượt xa timeline/năng lực Unity), PHẢI DỪNG LẠI toàn bộ quy trình và xuất Warning `Yêu Cầu Tối Giản Hóa GDD` thay vì cố ép generate config.

### Lens 4: Adversarial Reasoning
Giả lập **player tối ưu hóa cực đoan** cố tình phá hệ thống:
- Tìm mọi cách abuse mechanic stacking
- Tìm mọi cách farm resource nhanh nhất
- Tìm mọi cách bypass intended progression
- Nếu có PvP/competitive: tìm cách dominate bằng exploit

Xác định:
- Infinite scaling có khả thi không?
- Economy break có thể xảy ra không?
- Competitive distortion có xảy ra không?

**Format output:**

Mỗi issue phát hiện được nhưng không thể tự động sửa trong Spec phải có:
```
[SEVERITY] Category — Description — Mitigation
```
Severity: `Critical` | `High` | `Medium` | `Low`

**Quy tắc:**
- Bắt buộc chỉnh sửa các issue `Critical` và `High` trực tiếp vào nội dung Spec phần 1 đến 9.
- Không được trả về spec đầy lỗi rồi mặc kệ.
- Trả về Full markdown Tech Spec kèm phần Audit Notes đính kèm ở cuối.
