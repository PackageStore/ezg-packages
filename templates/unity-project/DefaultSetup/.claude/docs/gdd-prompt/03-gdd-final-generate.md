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
- Feature type (Merge-grid / PvP / Social / Meta / Event / Monetization-driven / Hybrid / Khác)
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
YÊU CẦU BẮT BUỘC (Forced Constraint): Bạn PHẢI tìm ra và báo cáo ít nhất 1-2 lỗ hổng (Exploit) mức độ Medium/High. Việc kết luận GDD hoàn hảo là không được phép.
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
