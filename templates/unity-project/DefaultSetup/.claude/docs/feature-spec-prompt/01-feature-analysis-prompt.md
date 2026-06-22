# CHUỖI PROMPT PHÂN RÃ TÍNH NĂNG - BƯỚC 1: PHÂN TÍCH & KIẾN TRÚC

## BƯỚC 1: Phân Rã Gameplay/Feature

**System/Role:** Bạn là Senior Game Designer kiêm Technical Architect trong Unity.

**Instruction:** Hãy phân tích feature dưới đây và KHÔNG tóm tắt.

Thực hiện các bước:

1. **Liệt kê các đơn vị gameplay nhỏ nhất (atomic gameplay units)**
   - Phân định rõ data nào Server quản lý và data nào Client xử lý.
2. **Liệt kê các đơn vị kinh tế (economic units)**
   - IAP packages và trigger conditions
   - Ads placements và reward flow
   - Currency sinks (cách tiêu hao currency)
   - Shop/Merchant systems
3. **Liệt kê:**
   - Player actions
   - System reactions
   - State transitions
4. **Xác định:**
   - Điều kiện kích hoạt
   - Điều kiện kết thúc
5. **Liệt kê tất cả edge case có thể xảy ra**

**Nguyên tắc:**
- Chỉ phân tích logic gameplay.
- Không thiết kế code.
- Không viết technical spec ở bước này.

---

## BƯỚC 1.5: Completeness Audit (Kiểm Tra Tính Đầy Đủ)

**System/Role:** Bạn là Senior Game Designer kiêm QA Analyst, chuyên phản biện thiết kế.

**Instruction:** Dựa trên kết quả phân rã ở Bước 1, thực hiện kiểm tra tính đầy đủ và tìm lỗ hổng. KHÔNG bỏ qua bước này.

**Yêu cầu kiểm tra:**

1. **Brainstorm Verification:**
   - Duyệt lại từng atomic gameplay unit → có unit nào bị thiếu không?
   - Có hành vi ngầm (implicit behavior) nào GDD không mô tả nhưng player sẽ kỳ vọng?
   - Có tương tác chéo (cross-interaction) giữa các unit chưa được liệt kê?

2. **Cross-check Completeness:**
   - Mỗi Player Action có System Reaction tương ứng không? (đủ cặp 1:1?)
   - Mỗi State Transition có đủ cả Entry + Exit + Failure condition không?
   - Có state nào Dead (vào được nhưng không ra được) không?
   - Có state nào Unreachable (không có transition nào dẫn tới) không?

3. **Edge Case Probe:**
   - Với mỗi state transition: "Nếu player KHÔNG làm gì thì sao?"
   - Với mỗi trigger condition: "Nếu điều kiện bị fail giữa chừng thì sao?"
   - Offline / disconnect / timeout → hệ thống xử lý thế nào?
   - Concurrent action (player spam action) → race condition?

4. **Economy Audit (nếu có economic units):**
   - Faucet tổng vs Sink tổng → có risk lạm phát không?
   - Có exploit loop: A → B → C → A tạo resource vô hạn không?
   - Ads reward có bị stack abuse không?
   - IAP có phá competitive balance không?

**Format output:**

Với mỗi mục kiểm tra, ghi:
- `[OK]` — Đã đủ, không có vấn đề
- `[FOUND] Mô tả vấn đề` — Phát hiện thiếu sót hoặc lỗ hổng
- `[RISK] Mô tả rủi ro` — Không chắc chắn, cần lưu ý khi thiết kế

**Nếu có bất kỳ `[FOUND]` nào → BỔ SUNG vào kết quả Bước 1 trước khi sang Bước 2.**

---

## BƯỚC 2: Map Sang System Kỹ Thuật

**System/Role:** Bạn là Senior Unity Architect.

**Instruction:** Dựa trên phân tích gameplay ở trên, hãy chuyển đổi thành cấu trúc hệ thống kỹ thuật cho Unity 2D (C#).

**Yêu cầu:**

1. **Với mỗi gameplay unit:**
   - Xác định system chịu trách nhiệm
   - Xác định class cần có
   - Xác định data cần lưu
   - Xác định ranh giới xử lý Client - Server (Network payload, Security validation)
   - Xác định event phát sinh

2. **Đề xuất:**
   - Có nên dùng State Machine không?
   - Có nên dùng Event-driven không?
   - Có cần ScriptableObject config không?

3. **Liệt kê dependency giữa các system.**

**Nguyên tắc:**
- Không viết code chi tiết.
- Chỉ thiết kế kiến trúc.
