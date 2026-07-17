# Planning Task — Flow & Cases (Unity Project)

Tài liệu mô tả cách hệ thống planning hoạt động: `/planning-task` là **cửa vào duy nhất** cho mọi intent, tự phân loại và route xuống đường xử lý phù hợp — từ CSV tweak 1 dòng cho tới cả một GDD hệ thống mới.

Nguồn chi tiết (đây chỉ là bản mô tả, không phải nguồn chân lý):
- Skill chính: [.claude/skills/planning-task/SKILL.md](../skills/planning-task/SKILL.md)
- Orchestrator new-system: [.claude/skills/planning-system/SKILL.md](../skills/planning-system/SKILL.md)
- Stage thiết kế: [.claude/docs/design-pipeline/](design-pipeline/README.md)
- Bookkeeping: `.claude/scripts/backlog-ops.py`
- Mockup pipeline: [.claude/commands/ui-mockup.md](../commands/ui-mockup.md) + [.claude/agents/mockup-drafter.md](../agents/mockup-drafter.md) + UI-kit `ui-catalog/ui-kit.json` (sinh từ **ui-catalog** — sync: `.claude/scripts/ui-kit-sync.py`)

---

## Flow tổng

```
/planning-task <intent | doc>
      │
      ▼
┌─ STEP 0b — NEW-SYSTEM? ──────────────────────────────────────────────┐
│ Fire khi CẢ HAI:                                                     │
│  (1) doc-scale: cả GDD/design doc nhiều section, HOẶC ≥2 module MỚI  │
│      tương tác nhau                                                  │
│  (2) KHÔNG gói được thành 1 task workflow-backed (0a match = nhường) │
│ Chạm economy đơn thuần KHÔNG kích 0b — chỉ là tín hiệu tier          │
└──────┬────────────────────────────────────┬──────────────────────────┘
       │ CÓ                                 │ KHÔNG
       ▼                                    ▼
   CASE A: dispatch /planning-system   STEP 0 — TIER TRIAGE + STEP 0a — WF?
   (xem chi tiết bên dưới)                  │
                                   ┌────────┴─────────┐
                                   │ match registry   │ không match
                                   ▼                  ▼
                          PURE → CASE B         XS/S/M-simple/M-complex/L
                          HYBRID → CASE C       → CASE D/E/F/G
       ▼ (mọi case B–G)
STEP 1 EXTRACT  → parse What/Why/Scope/Priority/Constraints; clarify ≤3 câu/lượt —
                  chỉ hỏi 5 nhóm cấm-bịa, KHÔNG hỏi cái codegraph/grep được
STEP 2 DRAFT    → theo case: XS draft thẳng · S codegraph 1–3 file · M-simple main context ·
                  M-complex/L spawn task-planner → present bản rút gọn 2b → user chỉnh ·
                  WF đọc đúng 1 command file (+ skill nó delegate), lift checklist thành criteria ·
                  riêng /new-ui (2b): resolve groundTruth — ảnh user đưa / clone:<Prefab> (tra
                  ui-catalog trước) / fast-lane kit-composition|custom → spawn mockup-drafter
                  → PENDING-APPROVAL:<html> (KHÔNG chờ duyệt)
STEP 3 FILENAME → backlog-ops.py timestamp → <ts>-<TIER>-<slug>.md (không NNN, không race check)
STEP 4 WRITE    → template theo tier/WF → backlog/planning/ + dòng **Tier:** (source of truth)
                  + dòng **Guardrails:** tags (định nghĩa tra ở backlog/_GUARDRAILS.md)
STEP 5 CHECK    → checklist theo tier: path thật · criteria đo được · scope-control ·
                  mobile impact · ≥3 manual verify steps (M/L) · batch field điền thật hoặc XÓA
STEP 6 REPORT   → tier + lý do chọn + assumptions + file → trỏ /add-to-backlog
```

---

## Bảng case

| Case | Kích hoạt (ví dụ) | Đường đi | Subagent? | Template | Output |
|---|---|---|---|---|---|
| **A — New-system** | Ném cả GDD: *"làm hệ thống Guild War theo doc này"* | 0b → `/planning-system`: stage-path co giãn theo profile — STANDARD `(03?)→04→05→06` · LITE bỏ 03 · EPIC per-module (chi tiết dưới) → batch-ground | 1 subagent/stage + task-planner cho item HYBRID | `_TEMPLATE_WF` + M/L | **N file** `<batchTS>-<NN>-<TIER>-<slug>.md` theo topo order + bộ `TechSpec/<Name>-*.md` |
| **B — WF pure** | *"tạo feature module Achievements"*, *"tạo màn hình EventShop"* | 0a match + scaffold thuần → skip task-planner, đọc đúng 1 command file | Không (tiết kiệm ~15–25K token) — riêng `/new-ui`: spawn `mockup-drafter` cho groundTruth | `_TEMPLATE_WF` | 1 file, exec tier từ registry (thường M) |
| **C — Hybrid** | *"tạo feature Achievements VÀ wire vào QuestService + rebalance CSV"* | 0a match + logic thêm → tier triage (hầu như M/L) → task-planner **plan delta only** | task-planner (delta) | `_TEMPLATE_M/L` + `Backed by workflow` | 1 file M/L |
| **D — XS** | *"đổi hằng số spawn 5→7"*, xóa dead code | Draft thẳng từ intent, không grep | Không | `_TEMPLATE_XS` | 1 file (~1K token) |
| **E — S** | Sửa logic 1 file, bug fix ≤2 file, thêm EventName const | codegraph/Grep 1–3 file xác nhận path rồi draft | Không | `_TEMPLATE_S` | 1 file (~3K token) |
| **F — M simple** | 1 save field mới / 1 screen theo pattern có sẵn, 3–8 file, không wiring chéo | Draft ở main context (1–2 pass codegraph), tự sinh JSON như task-planner | Không | `_TEMPLATE_M` | 1 file |
| **G — M complex / L** | Nhiều subsystem tương tác, dependency chưa rõ, IAP flow, save migration | Spawn task-planner (JSON: files/criteria/guardrails/mobile impact) → present bản rút gọn (2b) → user chỉnh → ghi | task-planner (opus) | `_TEMPLATE_M/L` | 1 file (~15–25K token) |

**Registry WF mặc định (STEP 0a):** chỉ `/new-feature` (M, L nếu cross-cutting) và `/new-ui` (M, UI-scoped, args `FeatureName | groundTruth=<value>`). `/new-package` out-of-band (skill `package-module`, không qua backlog); `/new-class` = task S thường; không có `/new-skill`/`/new-enemy-skill`.

---

## Case A chi tiết — các nhánh con của `/planning-system`

```
/planning-system <doc>   (auto-dispatch từ 0b · gọi trực tiếp · --from-mapping → vào thẳng [2])
[0] INTAKE   → FeatureName, PROFILE detection (LITE|STANDARD|EPIC), idempotency probe, guards
[1] DESIGN   → subagent per stage, tuần tự — stage sau ăn artifact stage trước
               (model: 03 opus · 04 sonnet · 05 opus · 06 sonnet)
               LITE:     04 → 05(trim: Lens 1+3 bắt buộc) → 06                  (bỏ hẳn 03)
               STANDARD: (03? — economy && chưa validated) → 04 → 05(4 lens) → 06
               EPIC:     (03?) → 04 + Decomposition Gate (Module Split Plan)
                                → per-module theo build order: 05 → 06 → merge dependency graph
               stage-result {status: OK|QUESTIONS|ABORT, profile_escalation?}:
                 QUESTIONS → hỏi user ≤3 câu/lượt (max 2 vòng/stage) → re-spawn ĐÚNG stage đó
                 ABORT "suggest EPIC split" → nâng EPIC, re-spawn 04, KHÔNG dừng
                 ABORT "Yêu Cầu Tối Giản Hóa GDD" → banner <!-- STALE --> → DỪNG cả pipeline
[2] PLAN     → parse mapping §10.1–10.7 → existence probe từng sub-feature
               (Features/<Domain>/<SubFeature>/ có code sẵn → CẤM pure-WF, hạ HYBRID)
               → route + exec tier PER ITEM · UI screen (§10.4) tách task /new-ui riêng, xếp CUỐI
               batch, groundTruth probe (png → approved · html → PENDING-APPROVAL · chưa có →
               PENDING-MOCKUP · token ui-catalog khớp layout → clone:<Prefab>)
               → localize fold vào task feature · ownership map + topo order (§10.6)
[3] GROUND   → MỘT batch timestamp + NN topo → HYBRID: task-planner fan-out song song ≤10/wave
               (>20 item → dừng hỏi chia phase) · pure-WF: orchestrator draft _TEMPLATE_WF
               → orchestrator TỰ viết N file → mockup-drafter fan-out cho UI item PENDING-MOCKUP
               (KHÔNG chờ approval) → git add (KHÔNG commit)
[4] REPORT   → profile + bảng task + promote order + mockup state + câu hỏi/giả định
```

**Profile (chọn ở INTAKE, predicate máy-đọc-được — bảng đầy đủ ở [design-pipeline/README.md](design-pipeline/README.md)):**

| Profile | Khi nào | Khác gì |
|---|---|---|
| **LITE** | Không economy/monetization/competitive/backend-write VÀ ≤5 sub-feature | Bỏ hẳn 03; 05 chỉ bắt buộc Lens 1 (Flow) + Lens 3 (Feasibility); không simulation — cắt ~40–60% token design |
| **STANDARD** | Có economy/monetization HOẶC 6–15 sub-feature; **mặc định khi thiếu tín hiệu** | Flow đầy đủ |
| **EPIC** | Backend write/PvP/social/guild HOẶC >15 sub-feature HOẶC nhiều bounded context | 04 xuất Module Split Plan → 05+06 per-module → merge graph; 05 thêm API Contract & Server Authority |

**Đặc điểm batch output (case A):**
- MỘT timestamp chung + `NN` = thứ tự topo → `promote` sort `(timestamp, index)` nên **NN chính là thứ tự thực thi**.
- Mỗi task mang: `**Tier:**` (body = source of truth), `**Context docs:**` (trỏ TechSpec — implementer không bịa lại số liệu), `**Depends on:**` (promote warn khi đứt), `**Requires:** unity-editor` (chỉ UI task), và riêng UI task: `groundTruth=` trong `**Workflow args:**`.
- Batch **trộn nhiều tier** — exec tier thật per item trong filename; `/run-backlog` key review-gate theo tier TỪNG task (security-auditor theo `$SENSITIVE` của diff bất kể tier; runtime smoke chỉ M/L).

---

## Mockup pipeline (UI ground truth)

Task `/new-ui` không được build từ mô tả text — nó cần ground truth hình ảnh. Nguyên tắc xuyên suốt: **generate = autonomous (parallel-safe) · approve = human (serial, bất đồng bộ)** — duyệt visual là quyết định gu con người nhưng KHÔNG dừng planning.

```
planning (N phiên song song)                   /ui-mockup (MỘT phiên interactive, lúc nào tiện)
  phát hiện task UI                              grep PENDING-* trong backlog/planning/
  → spawn mockup-drafter per screen              → draft nốt phần thiếu (PENDING-MOCKUP)
    đọc ui-catalog/ui-kit.json                   → MỞ UI REVIEW DASHBOARD KHI REVIEW
    (kit SINH TỪ ui-catalog — template = token)  → user duyệt cả loạt / yêu cầu sửa spec
    ghi <S>.ui-spec.json → generate <S>.html     → tick approve → local script export PNG
    (chỉ ghi pair — filesystem là queue)           1080×2400 → flip groundTruth=<png>
  → task: groundTruth=PENDING-APPROVAL:<html>    → git add (KHÔNG commit)
```

| groundTruth trong `**Workflow args:**` | Nghĩa |
|---|---|
| `TechSpec/Mockups/<F>/<S>.png` | **Đã approve** — PNG là contract đóng băng; sửa `.ui-spec.json` rồi re-render/re-export |
| `PENDING-APPROVAL:<...>.html` | Draft có rồi, chờ human duyệt ở `/ui-mockup` |
| `PENDING-MOCKUP` | Chưa có draft (drafter fail/skip) — `/ui-mockup` sẽ draft nốt |
| `clone:<ExistingPrefab>` | Fast lane: khỏi mockup — prefab đã khớp hierarchy, chỉ khác binding/minor styling |

- `backlog-ops.py promote --check` xuất `mockup_warnings` khi task còn `PENDING-*`; đây là **hard blocker**. Phải approve thành PNG hoặc chuyển sang `clone:<Prefab>` hợp lệ trước khi `/add-to-backlog` mutate.
- Màn chưa có prefab khớp được phân loại tiếp: `kit-composition` khi toàn bộ block có token trong UI-kit, `custom` khi cần layout/art direction mới. Dashboard áp dụng option có structured patch tức thì; chỉ free-form/custom edit mới gọi AI regenerate.
- Task non-clone giữ `**Mockup lane:**` ngay cả khi draft lỗi, nên `/ui-mockup` retry không phải phân loại lại; spec thành công lưu cùng giá trị ở `mockupLane`.
- Review mặc định chạy `python3 .claude/scripts/ui-review.py serve`: dashboard refresh/approve/approve-all trực tiếp qua API loopback tuần tự; chỉ yêu cầu thay đổi thiết kế bằng ngôn ngữ tự nhiên mới chạy AI agent.
- **UI-kit sinh từ ui-catalog khi project hỗ trợ:** `python3 .claude/scripts/ui-kit-sync.py` đọc `ui-catalog/ui-tokens.json` và extract geometry/màu từ prefab thật → `ui-catalog/ui-kit.{json,css}` + `kit-preview.html`. Fresh project chưa có catalog phải export catalog trước; không dùng token data của project khác.
- Design resolution lấy từ `ui-kit.json._meta.designResolution`; tooling dùng 1080×2400 làm fallback tương thích với shared `screen_template` mặc định.
- Khi build (`/run-backlog` STEP 5.0 → create-ui): PNG + `.ui-spec.json` là contract; agent `ui-visual-reviewer` chụp màn độc lập so với PNG tại checkpoint Phase A/B/C (max 2 vòng fix/phase).
- Task lai M/L tự dựng màn hình mới (không backed `/new-ui`) → field `**Needs mockup:** yes`, `/ui-mockup` sweep cùng cơ chế.

---

## Khi nào hệ thống DỪNG HỎI user (mọi case)

Chỉ đúng các nhóm sau — mọi thứ khác tự quyết + ghi assumption:

1. Giá trị **economy/reward** (giá IAP, số lượng, tỉ lệ drop)
2. **Save data / migration / persist-restart**
3. **Backend / auth / IAP / security**
4. **UX flow / product behavior cốt lõi**
5. Acceptance criteria / verify steps bị mơ hồ

→ Docs đầu vào đã chốt đủ số liệu = chạy **0 câu hỏi**, full auto tới N task. Giá trị thiếu thuộc nhóm cấm = marker `[DECISION NEEDED]` + câu hỏi — **không bao giờ tự bịa**.

→ Riêng **duyệt mockup UI** (visual composition — mở rộng của nhóm 4): xử lý **bất đồng bộ**, KHÔNG dừng pipeline — generate tự động lúc planning, approve gom một phiên tại `/ui-mockup`.

---

## Sau khi chạy xong (mọi case)

- File nằm ở `backlog/planning/` — **chưa** vào queue, **không** touch `BACKLOG.md`, **không** commit (case A có `git add` để promote không chết trên file untracked).
- User review → **task UI: `/ui-mockup`** (duyệt UI Review Dashboard + approve → PNG → groundTruth; màn approved tự biến mất; còn `PENDING-*` sẽ bị `mockup_warnings` block) → `/add-to-backlog` (STEP 4.5 chạy `promote --check` trước; batch case A: chọn all trong 1 lần promote là giữ đúng thứ tự; promote lẻ sẽ được warn `dependency_warnings` nếu đứt `Depends on`) → `/run-backlog`.
- Task UI (`Requires: unity-editor`) khi loop chạy headless sẽ tự `defer` về cuối TODO; nếu TOÀN BỘ task còn lại đều cần Editor → loop pause với `EDITOR_REQUIRED`. Mở Unity Editor rồi chạy tiếp.

---

## Điểm dễ nhầm

- **"Tạo 1 feature module lẻ" KHÔNG phải new-system** — 0b nhường 0a, nó thành CASE B (WF pure `/new-feature`) chứ không kéo cả pipeline thiết kế ra. Chạm economy chỉ là tín hiệu auto-bump tier, không phải tín hiệu new-system.
- **WF không phải tier** — task workflow-backed vẫn mang exec tier thật (XS/S/M/L) trong filename + dòng `**Tier:**` body; tier quyết định review-gating của run-backlog — riêng **security-auditor spawn theo `$SENSITIVE` của diff, bất kể tier**; WF chỉ quyết định "load command trước khi implement".
- **Profile không phải Tier** — hai trục co giãn ĐỘC LẬP: profile (LITE/STANDARD/EPIC) scale độ sâu pipeline THIẾT KẾ của cả hệ thống (chỉ Case A); tier (XS/S/M/L) scale độ sâu spec + review-gate của TỪNG task. Hệ thống EPIC vẫn đẻ ra task S; batch nào cũng trộn nhiều tier.
- **Phân vân tier → chọn tier LỚN hơn** — run-backlog KHÔNG tự escalate tier lúc thực thi; tier thấp oan = task bỏ qua reviewer (và runtime-smoke M/L) oan.
- **Batch mode không đệ quy** — khi `/planning-system` tái sử dụng drafting path của planning-task (flag `origin: planning-system`), STEP 0b/0a-dispatch bị tắt hoàn toàn; depth cap = 1.
- **Priority là tag, không phải thứ tự** — queue chạy theo task-order (timestamp + NN), không reorder theo HIGH/MEDIUM/LOW.
- **Field batch điền thật hoặc XÓA** — một dòng `**Requires:** unity-editor` placeholder sót lại từ template làm run-backlog defer oan một task headless (backlog-ops/run-backlog bỏ qua field nằm trong HTML comment, nhưng field thật ngoài comment thì tin ngay).
