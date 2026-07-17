# Stage 02 — GDD Production (Design Pipeline)

Nhận Concept GDD (hoặc ý tưởng ban đầu, từ [01-gdd-concept.md](01-gdd-concept.md)) → chuẩn hóa thành Production GDD chi tiết. Stage sau: [03-gdd-final.md](03-gdd-final.md).

> **Chỉ chạy standalone** (user yêu cầu) — `/planning-system` KHÔNG spawn stage này; nó bắt đầu từ doc user cung cấp. Model khuyến nghị: Opus.

## Quy trình

1. **Chuẩn hóa đầu vào:**
   - Đọc nội dung file Concept/Ý tưởng truyền vào bằng công cụ `Read`.
   - Nếu file có đuôi `.txt`, hãy chuyển đổi nội dung sang Markdown (`.md`) với format chuẩn (Headings, Lists, Tables).
   - Tham khảo kỹ **Prompt chuẩn bên dưới**.

2. **Phase 1 – System Formalization (Hệ thống hóa):**
   - Trích xuất và chốt **DESIGN INVARIANTS**.
   - Phân tích concept thành hệ thống logic: Core gameplay loop, Meta loop, State machine, Entry/Exit/Fail condition.
   - Cấu trúc các thành phần: Player progression path, Competitive metric, Monetization touchpoint.
   - Lập Data Dictionary để chuẩn hóa thuật ngữ và liệt kê Edge cases.
   - *Lưu ý: Không đi sâu vào economy chi tiết ở bước này.*

3. **Phase 2 – Full GDD Generation (Tạo GDD hoàn chỉnh):**
   - Dựa trên nền tảng của Phase 1, tạo Production GDD hoàn chỉnh với đầy đủ **16 phần cụ thể (từ I. OVERVIEW đến XVI. CONFIG TABLE)**.
   - Yêu cầu bắt buộc:
     - Phải có số liệu, bảng biểu và công thức cụ thể cho phần Economy & Monetization.
     - Đảm bảo economy được kiểm soát tốt (không runaway).
     - Tuyệt đối không phá Design Invariants đã chốt.
     - Nội dung ngắn gọn, không giải thích dài dòng hay narrative dư thừa, trạng thái output phải sẵn sàng cho audit (audit-ready).

4. **Lưu kết quả:**
   - Sử dụng công cụ `Write` trực tiếp lưu bản Production GDD vào đường dẫn thư mục `GDD/Production/`.
   - Tên file lưu: Sử dụng tên của Concept đi kèm hậu tố (ví dụ: `[FeatureName]-Production.md`).

## Prompt chuẩn

# PRODUCTION STEP 1 – FORMALIZATION

**Input:** Creative Concept Document.

**NHIỆM VỤ:**

1. Trích xuất và khóa lại toàn bộ **DESIGN INVARIANTS**.
2. Chuyển concept thành hệ thống chính xác:
   - Core gameplay loop
   - Meta loop (nếu có)
   - State machine
   - Entry / Exit / Fail condition
3. Xác định:
   - Player progression path trong event
   - Competitive metric (nếu có)
   - Monetization touchpoint (chỉ ở mức cấu trúc, chưa cần số)
4. Chuẩn hóa terminology.
5. Không thêm economy chi tiết ở bước này.

**Output phải gồm:**
- Invariant Lock Section
- Core Loop Breakdown
- State Machine Table
- Player Progression Path
- Monetization Structure Map
- Data Dictionary (Standardized Naming)
- Edge Case Enumeration (No mitigation, no deep analysis)
- Invariant Lock Section phải đánh dấu từng invariant bằng tag `[INV]` ngay trước tên, ví dụ: `[INV] Core mechanic không được thay đổi dù có áp lực monetization`. Tag này được giữ nguyên xuyên suốt toàn bộ GDD output để Tech pipeline nhận diện và treat như hard constraint.

---

# PRODUCTION STEP 2 – FULL GDD GENERATION

**Input:** Output của Step 1.
Từ System Formalization bên dưới, hãy tạo GDD production-ready hoàn chỉnh.

**YÊU CẦU:**

## I. OVERVIEW

## II. CORE DESIGN

## III. GAMEPLAY SYSTEM

## IV. ECONOMY DESIGN
- Event currency
- Reward table (số cụ thể)
- Drop rate %
- Scaling formula
- Daily cap
- Hard cap
- Expected value F2P vs Dolphin vs Whale
- Faucet vs Sink analysis

## V. MONETIZATION
- IAP bundles (price tier cụ thể)
- Value ratio
- Whale pressure mechanic
- Conversion funnel logic

## VI. EVENT SHOP

## VII. UI/UX REQUIREMENT
- User Flow Diagram
- Danh sách Screen / Popup / Tooltip / Widget cần thiết
- Trạng thái UI (Empty state, Error state, Loading)

## VIII. TECHNICAL DESIGN
- Client/server responsibility
- Save data JSON sample
- Remote config keys
- Reset logic
- Edge Cases & Error Handling

## IX. LIVEOPS CONFIG

## X. DATA TRACKING
- Core events (enter, clear, fail)
- Monetization events
- Custom parameters

## XI. PRODUCTION REQUIREMENT
- Asset List (UI, VFX, SFX, 2D/3D)
- Timeline estimation

## XII. RISK & MITIGATION

## XIII. DESIGN ASSUMPTIONS

## XIV. DEPENDENCIES

## XV. QA / TESTING CHECKLIST

## XVI. CONFIG TABLE (EXPORT-READY)

**RÀNG BUỘC:**
- Không được phá Design Invariants.
- Phải có số cụ thể.
- Phải có bảng.
- Phải có công thức.
- Phải đảm bảo economy không runaway.
- **Section applicability:** section không áp dụng cho feature này (vd feature không có economy → IV/V/VI; không LiveOps → IX) → ghi `N/A — <lý do 1 dòng>` là hợp lệ. Ba ràng buộc "phải có số/bảng/công thức" chỉ áp cho section CÓ THẬT — KHÔNG bịa số liệu để lấp template.
- Output phải audit-ready cho gdd-final-generate.
- Không narrative dư thừa.
- Không giải thích ngoài GDD.

- Mọi con số trong GDD phải được tag nguồn gốc ngay sau giá trị theo format: `[DERIVED]`, `[BENCHMARK]`, hoặc `[ASSUMED]`.
  - `[DERIVED]` — tính toán từ số khác đã có trong GDD.
  - `[BENCHMARK]` — dựa trên market data hoặc tham chiếu rõ ràng.
  - `[ASSUMED]` — AI tự đặt, chưa có cơ sở xác thực, cần human review.
- Cuối Section XIII. DESIGN ASSUMPTIONS, phải có bảng tổng hợp toàn bộ tag `[ASSUMED]`:

| # | Số / Tham số | Giá trị | Lý do assumed | Cần validate bởi |
|---|---|---|---|---|