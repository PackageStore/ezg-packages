# Stage 04 — Feature Analysis (Design Pipeline)

Bước đầu của khối Technical Spec: đọc GDD (ưu tiên bản Final từ [03-gdd-final.md](03-gdd-final.md)) → phân rã gameplay + completeness audit → Architecture doc. Stage sau: [05-tech-spec.md](05-tech-spec.md).

> **Hai chế độ gọi:**
> - **Standalone** (user yêu cầu chạy stage này): chạy như Quy trình bên dưới, báo cáo cho user ở bước cuối.
> - **Dưới `/planning-system`** (orchestrator spawn như một stage subagent, prompt có `invoked-by: planning-system`): tuân thủ **stage-result contract** — kết thúc bằng đúng MỘT JSON object (xem cuối file), KHÔNG báo cáo tự do.

## Quy trình

1. **Đọc đầu vào:**
   - Đọc nội dung file GDD được truyền vào bằng công cụ `Read` (cần đường dẫn tuyệt đối hoặc nhận từ tham số).
   - Prompt chuẩn của stage này nằm ngay **bên dưới** trong file này.

2. **Phân tích nháp — Phân Rã Gameplay (Bắt buộc trong Thought Process):**
   - Áp dụng **BƯỚC 1 (Phân Rã Gameplay)** từ Prompt chuẩn lên bản GDD.
   - *Lưu ý: Không in ra màn hình các bước nháp này, chỉ xử lý ngầm khi suy luận.*

3. **Completeness Audit (Bắt buộc — KHÔNG được bỏ qua):**
   - Áp dụng **BƯỚC 1.5 (Completeness Audit)** từ Prompt chuẩn lên kết quả Bước 2.
   - Kiểm tra: Brainstorm Verification, Cross-check Completeness, Edge Case Probe, Economy Audit.
   - Format output: `[OK]` / `[FOUND]` / `[RISK]` cho từng mục.
   - Nếu có `[FOUND]` → bổ sung vào kết quả phân rã trước khi tiếp tục.

4. **Map Sang System Kỹ Thuật (Bắt buộc trong Thought Process):**
   - Áp dụng **BƯỚC 2 (Map Sang System Kỹ Thuật)** từ Prompt chuẩn lên kết quả đã audit.

5. **KHÔNG bịa quyết định thuộc nhóm cấm (Bắt buộc):**
   - Nếu GDD **thiếu** một quyết định thuộc nhóm: **giá trị economy/reward, save-migration, backend/IAP/security, UX flow cốt lõi** — KHÔNG tự bịa. Ghi nhận thành câu hỏi (đưa vào `questions[]` khi chạy dưới `/planning-system`, hoặc hỏi user khi standalone) rồi mới tiếp tục.
   - Chi tiết ít rủi ro không đổi outcome (tên class dự kiến, cách đặt slug) được phép giả định và ghi chú rõ.

5b. **Decomposition Gate (chỉ profile EPIC, hoặc khi tự phát hiện scope EPIC):**
   - Kích hoạt khi: prompt ghi `profile: EPIC`, HOẶC kết quả phân rã đếm ra **>15 sub-system**, HOẶC lộ **nhiều bounded context rõ rệt** (nhiều save module + nhiều backend surface + nhiều UI flow độc lập). Tự phát hiện trong lần chạy LITE/STANDARD → ghi nhận trong stage-result (`profile_escalation`, xem contract) để orchestrator nâng profile.
   - Khi kích hoạt, artifact PHẢI có thêm section **`## Module Split Plan`**:
     - Danh sách module (bounded context), mỗi module: tên PascalCase + scope 1 dòng + sub-system thuộc về nó.
     - **Cross-module interfaces:** module nào gọi/nghe module nào, qua surface gì (event, save module, backend endpoint).
     - **Build order** (module nào trước) + **MVP cut** (bộ module tối thiểu chạy được).
   - Orchestrator sẽ chạy stage 05+06 **per module** theo build order — mỗi module một bộ artifact `[FeatureName]-[Module]-*`.

6. **Lưu kết quả Phân tích Kiến trúc:**
   - Dùng công cụ `Write` để lưu kết quả phân tích hệ thống vào **đúng đường dẫn cố định**: `TechSpec/[FeatureName]-Architecture.md` (KHÔNG đổi thư mục/tên theo ý riêng — stage sau parse theo đường dẫn này).
   - Standalone: báo cáo hoàn thành cho User để chuẩn bị sang stage [05-tech-spec.md](05-tech-spec.md).

## Stage-result contract (chỉ khi `invoked-by: planning-system`)

Kết thúc bằng đúng MỘT JSON object, không kèm prose:

```json
{
  "status": "OK | QUESTIONS | ABORT",
  "artifact_path": "TechSpec/[FeatureName]-Architecture.md",
  "questions": ["chỉ khi status=QUESTIONS — mỗi câu là một quyết định thuộc nhóm cấm mà GDD không trả lời"],
  "abort_reason": "chỉ khi status=ABORT",
  "profile_escalation": "OPTIONAL — chỉ khi phát hiện profile prompt đưa vào là SAI (vd 'LITE → STANDARD: lộ economy surface X' / 'STANDARD → EPIC: 18 sub-system, có Module Split Plan'). Orchestrator nâng profile và chạy bổ sung stage thiếu."
}
```

- `status=QUESTIONS`: vẫn ghi artifact với phần đã chắc chắn, đánh dấu chỗ thiếu bằng `[DECISION NEEDED: <câu hỏi>]` trong body. Orchestrator sẽ hỏi user rồi re-spawn stage này kèm câu trả lời — khi đó thay các marker và trả `status=OK`.
- `status=ABORT`: chỉ dùng khi input không phải GDD/feature doc hợp lệ.

## Prompt chuẩn

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
