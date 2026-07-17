# Design Pipeline — GDD → Technical Spec → Backlog

Bộ stage file cho pipeline thiết kế feature, đánh số liên tục theo thứ tự chạy. Mỗi stage = MỘT file tự chứa (quy trình + prompt chuẩn + stage-result contract).

| # | Stage | Input → Output | Model khuyến nghị |
|---|-------|----------------|-------------------|
| [01](01-gdd-concept.md) | GDD Concept | ý tưởng → concept doc (hook, invariants) | Gemini 3.1 Pro |
| [02](02-gdd-production.md) | GDD Production | concept → `GDD/Production/<Name>-Production.md` | Opus |
| [03](03-gdd-final.md) | GDD Final | draft → `GDD/Final/<Name>-Final.md` (adversarial audit, marker `<!-- validated: gdd-final -->`) | Opus |
| [04](04-feature-analysis.md) | Feature Analysis | GDD → `TechSpec/<Name>-Architecture.md` | Sonnet |
| [05](05-tech-spec.md) | Tech Spec | Architecture (+GDD gốc) → `TechSpec/<Name>-TechSpec.md` (4-lens audit + ABORT) | Opus |
| [06](06-implementation-mapping.md) | Implementation Mapping | TechSpec → `TechSpec/<Name>-Implementation.md` | Sonnet |

Chi tiết lý do chọn model: [model-assignment.md](model-assignment.md).

## Profile — pipeline tự co giãn theo cỡ hệ thống

`/planning-system` chọn profile ở INTAKE (Stage 0) theo **predicate máy-đọc-được**, không cảm tính. Mọi stage prompt đều mang dòng `profile: <LITE|STANDARD|EPIC>` để stage tự điều chỉnh độ sâu.

| Profile | Predicate (đo trên doc input) | Stage chạy | Audit depth |
|---|---|---|---|
| **LITE** | KHÔNG có economy/monetization/competitive/backend-write (không IAP, currency, ads-reward, shop, leaderboard, PvP) **VÀ** ước lượng ≤5 sub-feature | 04 → 05 (trim) → 06 — **bỏ hẳn 03** | 05 chỉ bắt buộc Lens 1 (Flow) + Lens 3 (Feasibility); KHÔNG simulation |
| **STANDARD** | Có economy/monetization HOẶC 6–15 sub-feature — **mặc định khi không đủ tín hiệu phân loại** | (03 nếu chưa validated) → 04 → 05 → 06 | Đủ 4 lens + simulation segment |
| **EPIC** | Có backend write / PvP / social / guild, HOẶC >15 sub-feature, HOẶC nhiều bounded context rõ rệt (nhiều save module + nhiều backend surface + nhiều UI flow độc lập) | 04 **+ Decomposition Gate** → chia module → 05+06 chạy **per module** (theo build order) → merge dependency graph | 4 lens + mục API Contract & Server Authority bắt buộc |

**Nâng/hạ profile giữa chừng:** stage sau được phép phát hiện profile sai (vd chạy LITE nhưng 04 đếm ra 12 sub-feature, hoặc lộ economy surface) → orchestrator **nâng profile và chạy bổ sung đúng stage thiếu**, không làm lại từ đầu. Không bao giờ tự hạ profile.

**Hai chế độ chạy:**
- **Automated (khuyến nghị):** `/planning-task <doc>` → STEP 0b dispatch `/planning-system` → orchestrator spawn subagent cho từng stage 03–06 (03 chỉ khi economy chưa validated), rồi batch-ground mapping thành N task trong `backlog/planning/`. Subagent nhận flag `invoked-by: planning-system` và trả **stage-result contract** `{status: OK|QUESTIONS|ABORT, artifact_path, questions[], abort_reason}` định nghĩa cuối mỗi stage file.
- **Manual:** yêu cầu Claude chạy một stage cụ thể ("chạy stage 05-tech-spec với file X") — stage file là instruction đầy đủ. Đây KHÔNG phải slash command — stage file sống trong `.claude/docs/design-pipeline/`, không phải `.claude/commands/`.

**Quy tắc xuyên suốt (mọi stage):**
1. KHÔNG bịa quyết định thuộc 4 nhóm cấm — **giá trị economy/reward · save-migration · backend/IAP/security · UX flow cốt lõi**. Giá trị thiếu = marker `[DECISION NEEDED: <câu hỏi>]` + `status=QUESTIONS`, không phải chỗ sáng tác.
2. **Section applicability:** section nào không áp dụng cho feature (vd không có economy/IAP/shop) → ghi `N/A — <lý do 1 dòng>` là **hợp lệ**. Ràng buộc "phải có số/bảng/công thức" chỉ áp cho section có thật — không bịa số liệu để lấp template, cũng không xả `[DECISION NEEDED]` cho thứ không tồn tại.
