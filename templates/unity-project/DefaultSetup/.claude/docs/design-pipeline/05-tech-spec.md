# Stage 05 — Tech Spec (Design Pipeline)

Nhận Architecture doc (từ [04-feature-analysis.md](04-feature-analysis.md)) → Technical Specification hoàn chỉnh kèm audit 4 lens. Stage sau: [06-implementation-mapping.md](06-implementation-mapping.md).

> **Hai chế độ gọi:**
> - **Standalone** (user yêu cầu chạy stage này): chạy như Quy trình bên dưới, báo cáo cho user ở bước cuối.
> - **Dưới `/planning-system`** (prompt có `invoked-by: planning-system`): tuân thủ **stage-result contract** (JSON duy nhất, xem cuối file).

## Quy trình

1. **Đọc đầu vào:**
   - Đọc nội dung file Phân tích Kiến trúc được sinh ra từ stage 04 bằng công cụ `Read` (mặc định `TechSpec/[FeatureName]-Architecture.md`).
   - **Đọc cả file GDD gốc nếu được cung cấp trong prompt** — bước 4 (Coverage) chỉ chạy được khi có GDD gốc; thiếu nó thì ghi rõ `coverage-check: skipped (no original GDD provided)` thay vì âm thầm bỏ qua.
   - **Ground vào repo (bắt buộc, trước khi vẽ class):** đọc danh sách skill trong `.claude/skills/`, các rule core/data/project-structure, rồi khảo sát code thật. Architecture PHẢI slot vào hệ thống có sẵn (`FeatureBaseController`, `UIManager`, `PlayerDataManager`, `DataManager`, event/time/scene/config/reward/backend services khi chúng thật sự tồn tại). Với economy, progression, competitive state hoặc backend, truy ra funnel và authority model hiện hành từ code/skill tương ứng; KHÔNG phát minh manager, event bus, save layer, currency path hoặc backend abstraction song song. Class nào kế thừa/gọi hệ thống có sẵn phải ghi rõ tên hệ thống thật trong Section 4.
   - Prompt chuẩn của stage này nằm ngay **bên dưới** trong file này.
   - **EPIC mode:** khi được chạy per-module, prompt truyền `[FeatureName]` dạng `<Name>-<Module>` — giữ nguyên mọi placeholder; artifact tự thành `TechSpec/<Name>-<Module>-TechSpec.md`. Đọc thêm TechSpec của các module TRƯỚC nó (build order) nếu prompt liệt kê, để interface chéo module khớp nhau.

2. **Sinh cấu trúc Technical Spec:**
   - Áp dụng **BƯỚC 3 (Generate Technical Spec Hoàn Chỉnh)** từ Prompt chuẩn.
   - Chắc chắn rằng form output bám sát 100% cấu trúc yêu cầu, đặc biệt chú trọng phần Data Model (Client-Server Data) và Event/Network Flow.
   - **KHÔNG bịa quyết định thuộc nhóm cấm:** nếu input (Architecture + GDD gốc) **không** chứa một giá trị thuộc nhóm **economy/reward (giá IAP, số lượng reward, tỉ lệ drop), save-migration, backend/IAP/security** — KHÔNG tự phát minh con số. Chèn marker `[DECISION NEEDED: <câu hỏi cụ thể>]` vào đúng vị trí trong Spec và ghi nhận thành câu hỏi. Mục 6.5 (Economy & Monetization) chỉ điền số liệu ĐÃ có trong input; số liệu thiếu = câu hỏi, không phải chỗ để sáng tác.
   - **Section applicability:** feature KHÔNG có economy/monetization → toàn bộ 6.5 = `N/A — no economy surface` (hợp lệ, KHÔNG cần `[DECISION NEEDED]` cho thứ không tồn tại). Tương tự cho State Machine (Section 3) khi feature không có state phức tạp.
   - **EPIC — Section 6b bắt buộc khi có backend write:** thêm mục `6b. API Contract & Server Authority` — từng backend surface, payload đọc/ghi, validation/error/fallback theo `backend-communication` và data-persistence rules nếu có, ai là authority cho từng giá trị, cùng anti-cheat model cho value-bearing data. Không mặc định Firebase/Supabase/Cloudflare khi repo không dùng chúng.

3. **Technical Spec Audit (Bắt buộc — KHÔNG được bỏ qua):**
   - Áp dụng **BƯỚC 3.5 (Technical Spec Audit)** từ Prompt chuẩn lên Technical Spec vừa sinh.
   - **Độ sâu theo profile** (prompt ghi `profile: ...`): `LITE` → bắt buộc Lens 1 (Flow) + Lens 3 (Feasibility), Lens 2/4 chỉ chạy khi spec lộ surface tương ứng; `STANDARD`/`EPIC` → đủ 4 lens.
   - **Đặc biệt lưu ý Lens 3:** Nếu quy mô (scope) quá lớn vượt khả năng hoặc timeline: profile `STANDARD` → **đề xuất nâng EPIC + Module Split trước** (trả `status=ABORT`, `abort_reason` bắt đầu bằng `suggest EPIC split: ...` — orchestrator sẽ nâng profile và chạy lại từ Decomposition Gate của stage 04 thay vì dừng hẳn); chỉ xuất Warning "Yêu Cầu Tối Giản Hóa GDD" (abort hẳn) khi ngay cả chia module cũng không cứu được scope, hoặc đã ở profile `EPIC` rồi mà một module đơn lẻ vẫn vượt năng lực.
   - Sửa ngay lập tức mọi issue `Critical` hoặc `High` trực tiếp vào nội dung Spec — NGOẠI TRỪ issue mà cách sửa đòi một quyết định thuộc nhóm cấm: những issue đó trở thành `[DECISION NEEDED]` + câu hỏi, không tự quyết.

4. **Kiểm tra Coverage & UX/UI (Bắt buộc khi có GDD gốc):**
   - Đối chiếu các con số cụ thể trong Tech Spec với GDD gốc.
   - Nếu GDD gốc có section mô tả Visual, Audio → tạo **Section 11. UX/UI Notes** trong TechSpec và liệt kê các yêu cầu visual/audio/animation theo format bảng.

5. **Lưu kết quả Technical Spec:**
   - Dùng công cụ `Write` để lưu bản Specification hoàn chỉnh vào **đúng đường dẫn cố định**: `TechSpec/[FeatureName]-TechSpec.md` (KHÔNG đổi thư mục/tên — stage sau parse theo đường dẫn này).
   - Standalone: báo cáo hoàn thành cho User để chuẩn bị sang stage [06-implementation-mapping.md](06-implementation-mapping.md).

## Stage-result contract (chỉ khi `invoked-by: planning-system`)

Kết thúc bằng đúng MỘT JSON object, không kèm prose:

```json
{
  "status": "OK | QUESTIONS | ABORT",
  "artifact_path": "TechSpec/[FeatureName]-TechSpec.md",
  "questions": ["chỉ khi status=QUESTIONS — mỗi câu hỏi tương ứng một marker [DECISION NEEDED] trong artifact"],
  "abort_reason": "chỉ khi status=ABORT — hai dạng: 'suggest EPIC split: <scope vượt ở đâu>' (STANDARD, orchestrator nâng profile + chạy lại Decomposition Gate) HOẶC 'Yêu Cầu Tối Giản Hóa GDD: <lý do>' (abort hẳn)",
  "profile_escalation": "OPTIONAL — khi phát hiện profile prompt đưa vào là SAI (vd 'LITE → STANDARD: spec lộ economy surface X'). Orchestrator nâng profile và chạy bổ sung stage thiếu."
}
```

- `status=QUESTIONS`: artifact vẫn được ghi, mỗi giá trị thiếu là một marker `[DECISION NEEDED]`. Khi được re-spawn kèm câu trả lời của user: thay toàn bộ marker bằng giá trị thật, chạy lại audit (bước 3) trên phần bị ảnh hưởng, trả `status=OK`.
- `status=ABORT` (Lens 3): artifact viết dở phải có banner đầu file `<!-- STALE: aborted at 05-tech-spec — do not consume -->` để lần chạy sau không dùng nhầm.

## Prompt chuẩn

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

> **QUY TẮC KHÔNG BỊA (bắt buộc):** section này chỉ điền số liệu ĐÃ tồn tại trong input
> (GDD/Architecture). Giá IAP, số lượng reward, giới hạn ads, tỉ lệ drop... mà input KHÔNG
> có → chèn marker `[DECISION NEEDED: <câu hỏi cụ thể>]` thay vì tự phát minh con số.
> Một con số bịa trông "đã chốt" sẽ lọt qua mọi gate phía sau và thành code — tệ hơn
> nhiều so với một câu hỏi. Quy tắc này áp dụng cho MỌI giá trị thuộc nhóm:
> economy/reward, save-migration, backend/IAP/security.

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
- **NGOẠI LỆ duy nhất:** issue mà cách sửa đòi một quyết định thuộc nhóm cấm-bịa (economy/reward, save-migration, backend/IAP/security) → KHÔNG tự quyết; chèn `[DECISION NEEDED: <câu hỏi>]` tại vị trí đó và liệt kê thành câu hỏi ở Audit Notes.
- Không được trả về spec đầy lỗi rồi mặc kệ.
- Trả về Full markdown Tech Spec kèm phần Audit Notes đính kèm ở cuối.
