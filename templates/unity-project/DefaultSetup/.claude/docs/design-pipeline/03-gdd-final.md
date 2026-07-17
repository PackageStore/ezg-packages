# Stage 03 — GDD Final (Design Pipeline)

Nhận GDD draft (từ [02-gdd-production.md](02-gdd-production.md) hoặc doc user đưa) → validation đa tầng, loại bỏ lỗi logic, exploit và lỗ hổng kinh tế → Final GDD. Stage sau: [04-feature-analysis.md](04-feature-analysis.md).

> **Hai chế độ gọi:**
> - **Standalone** (user yêu cầu chạy stage này): chạy như Quy trình bên dưới, xuất Final GDD cho user.
> - **Dưới `/planning-system`** (prompt có `invoked-by: planning-system`): tuân thủ **stage-result contract** — kết thúc bằng đúng MỘT JSON object (xem cuối file). Lưu ý: các mitigation cho issue High/Critical mà bản chất là **quyết định economy/reward, save-migration, backend/IAP/security** thì KHÔNG tự quyết — trả `status=QUESTIONS` thay vì tự rewrite theo phương án tự bịa.

## Quy trình

1. **Chuẩn hóa đầu vào:**
   - Đọc file GDD được truyền vào bằng công cụ `Read`.
   - Nếu file có đuôi `.txt`, hãy chuyển đổi nội dung sang Markdown (`.md`) với format chuẩn (Headings, Lists, Tables).
   - Nội dung draft này sẽ là cơ sở cho các bước tiếp theo.

2. **Phase 0 – System Extraction (Bóc tách hệ thống):**
   - Áp dụng các mục 0.1, 0.2, 0.3 của **Prompt chuẩn bên dưới**.
   - Xác định: Feature Classification, System Decomposition, State Integrity Mapping.
   - *Lưu ý: Không được rewrite GDD ở bước này.*

3. **Phase 1 – Single Deep Adversarial Audit (Audit chuyên sâu):**
   - Chạy MỘT vòng Audit toàn diện và sâu nhất với Adversarial Role (đóng vai cheater đang nỗ lực bẻ gãy hệ thống).
   - Yêu cầu bắt buộc (Forced Constraint — **ép nỗ lực, không ép kết quả**): quét đủ TẤT CẢ các lens với nỗ lực adversarial thật:
     - Flow Logic & State Integrity.
     - Exploit & Optimization Abuse Risk.
     - Retention & Burnout Risk.
     - Economy Stability & Monetization Integrity (nếu có).
     - Production & Technical Feasibility.
   - Lens nào KHÔNG tìm ra issue Medium+ → PHẢI ghi giải trình 1–2 dòng vì sao lens đó trống (thiết kế nào đã chặn nó). Kết luận "GDD hoàn hảo" không kèm giải trình per-lens là không được phép — nhưng **bịa issue để đủ chỉ tiêu cũng không được phép**.
   - Thể hiện mỗi issue theo "ISSUE FORMAT" yêu cầu (Category, Severity, Affected Player Segment, v.v...).
   - *Lưu ý: Tuyệt đối không được rewrite GDD trong phase này.*

4. **Phase 2 – Simulation & Stress Test** (CHỈ khi feature có economy HOẶC competitive layer — nếu không có cả hai → ghi `simulation: skipped (no economy/competitive surface)` và sang Phase 3):
   - Giả lập Player Segment (F2P, Dolphin, Whale) trong 7 ngày đầu và cả lifecycle.
   - Thử nghiệm Adversarial Optimization (người chơi tối ưu hóa cực đoan để phá game/kinh tế).
   - *Lưu ý: Không rewrite GDD.*

5. **Phase 3 – Issue Consolidation (Tổng hợp Issue):**
   - Gộp toàn bộ issue từ Phase 1 và 2.
   - Loại bỏ trùng lặp, phân nhóm theo Severity.
   - Không được rewrite trước khi hoàn thành bước này và có đủ Mitigation cho các issue High/Critical.

6. **Phase 4 – Final GDD Generation (Rewrite duy nhất):**
   - Dựa trên danh sách issue đã hợp nhất và các mitigation đã đề xuất, thực hiện rewrite GDD **duy nhất một lần**.
   - Tích hợp toàn bộ giải pháp vào nội dung GDD.
   - Loại bỏ toàn bộ các comment giải thích, danh sách issue và nội dung audit.

7. **Yêu cầu Output:**
   - Cấu trúc GDD phải rõ ràng, logic chặt chẽ, thuật ngữ nhất quán.
   - Xuất toàn bộ Final GDD trong một block markdown duy nhất để User có thể copy dễ dàng.

## Lưu kết quả

- Dùng công cụ `Write` để lưu bản Final GDD vào thư mục `GDD/Final/`.
- Tên file: `[FeatureName]-Final.md`.
- **Dòng ĐẦU TIÊN của file phải là marker:** `<!-- validated: gdd-final -->` — đây là predicate máy-đọc-được để `/planning-system` biết economy/monetization của doc đã qua adversarial audit và bỏ qua stage này (idempotency).

## Stage-result contract (chỉ khi `invoked-by: planning-system`)

Kết thúc bằng đúng MỘT JSON object, không kèm prose:

```json
{
  "status": "OK | QUESTIONS | ABORT",
  "artifact_path": "GDD/Final/[FeatureName]-Final.md",
  "questions": ["chỉ khi status=QUESTIONS — mỗi câu là một quyết định economy/save/backend/IAP mà mitigation cần nhưng draft không trả lời"],
  "abort_reason": "chỉ khi status=ABORT — input không phải GDD hợp lệ"
}
```

## Prompt chuẩn

# FINAL – UNIVERSAL FEATURE GDD VALIDATION FRAMEWORK
## (Multi-Round Audit → Single Rewrite Architecture)

---

# OVERVIEW PRINCIPLE

- Không rewrite trong các vòng Audit.
- Thực hiện MỘT vòng Audit duy nhất nhưng sâu sắc và gay gắt nhất.
- Bắt buộc sử dụng Adversarial Role (đóng vai người chơi cố tình phá game).
- Chỉ thực hiện 1 lần Rewrite duy nhất sau khi Issue List đã ổn định.
- Human review diễn ra SAU khi có Final GDD.

---

# PHASE 0 – SYSTEM EXTRACTION (BẮT BUỘC – KHÔNG REWRITE)

Trước khi đánh giá, phải bóc tách hệ thống.

## 0.1 Feature Classification
Xác định:
- Feature type (PvE / PvP / Social / Meta / Event / Monetization-driven / Hybrid / Khác)
- Có economy hay không
- Có competitive layer hay không
- Có social dependency hay không
- Có seasonal lifecycle hay không

## 0.2 System Decomposition
Trích xuất:
- Core gameplay loop
- Meta loop (nếu có)
- Player progression path
- Resource input/output (faucet & sink)
- Monetization touchpoints (nếu có)
- Social interaction layer (nếu có)

## 0.3 State Integrity Mapping
- Liệt kê toàn bộ state chính
- Liệt kê trigger giữa các state
- Biểu diễn dưới dạng state-transition list hoặc table
- Xác định:
  - Entry condition
  - Exit condition
  - Failure condition

KHÔNG đánh giá ở Phase này.
KHÔNG rewrite.

---

# PHASE 1 – SINGLE DEEP ADVERSARIAL AUDIT (BẮT BUỘC)

Thực hiện MỘT vòng Audit toàn diện và sâu nhất dựa trên các Lenses định sẵn.
Bắt buộc sử dụng Adversarial Role: đóng vai một cheater/griefer đang nỗ lực bẻ gãy hệ thống.
YÊU CẦU BẮT BUỘC (Forced Constraint — ép NỖ LỰC, không ép kết quả): Bạn PHẢI quét đủ TẤT CẢ lens 1.1–1.7 với nỗ lực adversarial thật. Lens nào không tìm ra issue Medium+ → PHẢI kèm giải trình 1–2 dòng vì sao lens đó trống (cơ chế nào trong design đã chặn nó). Kết luận "GDD hoàn hảo" không kèm giải trình per-lens là không được phép — và BỊA issue cho đủ chỉ tiêu cũng không được phép (issue bịa phá tin cậy của cả Issue List).
KHÔNG được rewrite GDD.

---

## 1.1 Flow Logic & State Integrity
- Dead state
- Unreachable state
- Infinite loop
- Missing transition
- Condition conflict
- System contradiction

## 1.2 Exploit & Optimization Abuse Risk
- Abuse stacking
- Reward farming loophole
- Infinite scaling
- Pay-to-win bypass skill ceiling
- Edge-case abuse

## 1.3 Retention & Burnout Risk
- Session spike quá cao
- Không có breathing space
- Early drop-off risk
- Late fatigue accumulation
- Repetition fatigue

## 1.4 Economy Stability (Nếu có economy)
- Inflation risk
- Resource faucet > sink
- Runaway advantage
- Late lifecycle imbalance
- Catch-up mechanic thiếu hoặc dư

## 1.5 Monetization Integrity (Nếu có IAP/Ads)
- Competitive integrity bị phá
- Frustration-driven toxic monetization
- Whale dominance không thể counter
- F2P choke point
- Over-dependence vào Ads

## 1.6 Social / PvP / Guild Risk (Nếu có)
- Toxic peer pressure
- Whale stacking dominance
- Collusion exploit
- Matchmaking distortion
- Social exclusion effect

## 1.7 Production & Technical Feasibility
- Scope vượt năng lực team
- Dependency chain phức tạp
- High QA surface
- Risk bug cascade
- Maintenance cost cao

---

# ISSUE FORMAT (BẮT BUỘC)

Mỗi issue phải có:

- Category:
- Severity: (Low / Medium / High / Critical)
- Affected Player Segment: (F2P / Dolphin / Whale / All)
- Time Horizon: (Early / Mid / Late lifecycle)
- Description:
- Why it is a problem:
- Suggested Mitigation (không rewrite toàn bộ GDD, chỉ đề xuất hướng xử lý)

**SEVERITY LOCK RULE (BẮT BUỘC):**
- AI KHÔNG được tự downgrade severity của một issue sau khi đã xác định.
- Issue được xác định là High hoặc Critical chỉ được đóng (mark as resolved) khi mitigation được ghi rõ ràng trong phần Suggested Mitigation VÀ được tham chiếu lại trong Phase 3.
- Nếu không tìm được mitigation hợp lý cho một Critical issue → PHẢI dừng, KHÔNG được tiến vào Phase 4, và xuất cảnh báo: `[BLOCKED] Critical issue chưa có mitigation — yêu cầu human input trước khi rewrite.`

---

# PHASE 2 – SIMULATION & STRESS TEST (KHÔNG REWRITE)

> **Điều kiện chạy:** feature có economy HOẶC competitive layer. Không có cả hai → ghi `simulation: skipped (no economy/competitive surface)` và chuyển thẳng Phase 3 — KHÔNG mô phỏng segment cho feature không có gì để mô phỏng.

## 2.1 Player Segment Simulation
Mô phỏng:
- Pure F2P
- Dolphin
- Whale

Phân tích:
- 7 ngày đầu
- Xu hướng toàn lifecycle/season

Tập trung phát hiện:
- Progression stall
- Power spike
- Monetization pressure spike
- Competitive distortion

---

## 2.2 Adversarial Optimization Test

Giả lập người chơi tối ưu hóa cực đoan:
- Abuse mechanic stacking
- Abuse revive / stamina / multiplier
- Abuse guild coordination
- Abuse reward scaling

Xác định:
- Infinite scaling khả thi không?
- Competitive distortion có xảy ra không?
- Economy break có thể xảy ra không?

---

# PHASE 3 – ISSUE CONSOLIDATION

Sau khi hoàn thành vòng Audit và Simulation:

1. Gộp toàn bộ issue.
2. Loại bỏ trùng lặp.
3. Phân nhóm theo Severity.
4. Chỉ giữ lại issue hợp lệ và có logic rõ ràng.
5. Nếu còn Critical chưa có mitigation hợp lý → yêu cầu bổ sung trước khi rewrite.

KHÔNG rewrite trước khi hoàn thành bước này.

---

# PHASE 4 – FINAL GDD GENERATION (CHỈ 1 LẦN DUY NHẤT)

Chỉ được rewrite khi:

- Không còn issue mức Critical chưa có hướng xử lý.
- Issue mức High đã có mitigation rõ ràng.
- Không tồn tại dead state.
- Không tồn tại exploit obvious.
- Competitive integrity được giữ (nếu có cạnh tranh).
- Economy không runaway (nếu có economy).
- Scope phù hợp năng lực production.

---

# FINAL GDD OUTPUT REQUIREMENT

Final GDD phải:

- Cấu trúc rõ ràng
- Logic chặt chẽ
- Terminology nhất quán
- Đã tích hợp toàn bộ mitigation
- Không chứa comment giải thích
- Không chứa danh sách issue
- Không chứa nội dung audit

Xuất toàn bộ Final GDD trong một block duy nhất để có thể copy một lần.

Ngay sau Final GDD, xuất thêm một HANDOFF BLOCK theo đúng format sau (không được bỏ qua, không được viết narrative):

---
## HANDOFF BLOCK — GDD → TECH

### Locked Invariants
- [Liệt kê từng invariant đã được lock, mỗi dòng một item]

### Locked Terminology
- [Term]: [Definition ngắn gọn, 1 dòng]

### Mitigated Issues (Tech pipeline KHÔNG được raise lại)
- [Issue ID hoặc mô tả ngắn]: [Resolution đã áp dụng]

### Open Assumptions (Cần human validate trước khi dùng làm baseline)
- [Assumption]: [Basis — DERIVED / BENCHMARK / ASSUMED]
---

HANDOFF BLOCK phải được paste vào đầu window Tech pipeline như system context trước khi chạy bất kỳ bước nào.

---

# ARCHITECTURE SUMMARY

- Phase 0
- Phase 1 (1 vòng audit sâu sắc và adversarial)
- Phase 2
- Phase 3

Single Execution:
- Phase 4 (Rewrite duy nhất)

Human Review:
- Sau Final GDD
- Nếu cần chỉnh sửa → quay lại Phase 1 với delta change