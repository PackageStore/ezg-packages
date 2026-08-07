# Planning Task — Flow & Cases

Tài liệu mô tả cách hệ thống planning hoạt động: `/planning-task` là **cửa vào duy nhất** cho mọi intent, tự phân loại và route xuống đường xử lý phù hợp — từ CSV tweak 1 dòng cho tới cả một GDD hệ thống mới.

Nguồn chi tiết (đây chỉ là bản mô tả, không phải nguồn chân lý):
- Skill chính: [.claude/skills/planning-task/SKILL.md](../skills/planning-task/SKILL.md)
- Orchestrator new-system: [.claude/skills/planning-system/SKILL.md](../skills/planning-system/SKILL.md)
- Stage thiết kế: [.claude/docs/design-pipeline/](design-pipeline/README.md)
- Bookkeeping: `.claude/scripts/backlog-ops.py`
- Mockup pipeline: [.claude/commands/ui-mockup.md](../workflows/ui-mockup.md) + [.claude/agents/mockup-drafter.md](../agents/mockup-drafter.md) + UI-kit `.claude/ui-kit/` (sync: `.claude/scripts/ui-kit-sync.py`)

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
   CASE A: dispatch /planning-system    STEP 0a — WORKFLOW-BACKED?
   (xem chi tiết bên dưới)                  │
                                   ┌────────┴─────────┐
                                   │ match registry   │ không match
                                   ▼                  ▼
                          PURE → CASE B         STEP 0 — TIER TRIAGE
                          HYBRID → vẫn qua      XS/S/M-simple/M-complex/L
                          TIER TRIAGE → CASE C  → CASE D/E/F/G
       ▼ (mọi case B–G)
STEP 1 EXTRACT  → parse What/Why/Scope/Priority/Constraints; clarify ≤3 câu/lượt —
                  chỉ hỏi 5 nhóm cấm-bịa, KHÔNG hỏi cái grep/read được
STEP 2 DRAFT    → theo case: XS draft thẳng · S grep 1–3 file · M-simple main context ·
                  M-complex/L spawn task-planner → present bản rút gọn 2b → user chỉnh ·
                  WF đọc đúng 1 workflow file, lift checklist thành criteria ·
                  riêng /new-ui (2b): resolve groundTruth — ảnh user đưa / clone:<Prefab> /
                  spawn mockup-drafter → PENDING-APPROVAL:<html> (KHÔNG chờ duyệt)
STEP 3 FILENAME → backlog-ops.py timestamp → <ts>-<TIER>-<slug>.md (không NNN, không race check)
STEP 4 WRITE    → template theo tier/WF → backlog/planning/ + dòng **Guardrails:** tags
                  (định nghĩa tra ở .claude/backlog-templates/_GUARDRAILS.md, không paste block)
STEP 5 CHECK    → checklist theo tier: path thật · criteria đo được · scope-control ·
                  mobile impact · ≥3 manual verify steps (M/L)
STEP 6 REPORT   → tier + lý do chọn + assumptions + file → trỏ /add-to-backlog
```

---

## Bảng case

| Case | Kích hoạt (ví dụ) | Đường đi | Subagent? | Template | Output |
|---|---|---|---|---|---|
| **A — New-system** | Ném cả GDD: *"làm hệ thống Guild War theo doc này"* | 0b → `/planning-system`: stage-path co giãn theo profile — STANDARD `(03?)→04→05→06` · LITE bỏ 03 · EPIC per-module (chi tiết dưới) → batch-ground | 1 subagent/stage + task-planner cho item HYBRID | `_TEMPLATE_WF` + M/L | **N file** `<batchTS>-<NN>-<TIER>-<slug>.md` theo topo order + bộ `TechSpec/<Name>-*.md` |
| **B — WF pure** | *"tạo package WeeklyGemPack"*, *"tạo skill 15 bắn 3 đạn homing"* | 0a match + scaffold thuần → skip planner, đọc đúng 1 workflow file | Không (tiết kiệm ~15–25K token) — riêng `/new-ui`: spawn `mockup-drafter` cho groundTruth | `_TEMPLATE_WF` | 1 file, exec tier từ registry (thường M) |
| **C — Hybrid** | *"tạo skill 15 VÀ wire vào evolution pool + rebalance CSV"* | 0a match + logic thêm → tier triage (hầu như M/L) → planner **plan delta only** | task-planner (delta) | `_TEMPLATE_M/L` + `Backed by workflow` | 1 file M/L |
| **D — XS** | *"đổi hằng số spawn 5→7"*, xóa dead code | Draft thẳng từ intent, không grep | Không | `_TEMPLATE_XS` | 1 file (~1K token) |
| **E — S** | Sửa logic 1 file, bug fix ≤2 file | Grep/Read 1–3 file xác nhận path rồi draft | Không | `_TEMPLATE_S` | 1 file (~3K token) |
| **F — M simple** | 1 save field mới / 1 screen theo pattern có sẵn, 3–8 file, không wiring chéo | Draft ở main context (1–2 pass Grep/Read), tự sinh JSON như planner | Không | `_TEMPLATE_M` | 1 file |
| **G — M complex / L** | Nhiều subsystem tương tác, dependency chưa rõ, IAP flow, save migration | Spawn task-planner (JSON: files/criteria/guardrails/mobile impact) → present bản rút gọn (2b) → user chỉnh → ghi | task-planner (opus) | `_TEMPLATE_M/L` | 1 file (~15–25K token) |

---

## Case A chi tiết — các nhánh con của `/planning-system`

```
/planning-system <doc>   (auto-dispatch từ 0b · gọi trực tiếp · --from-mapping → vào thẳng [2])
[0] INTAKE   → FeatureName, PROFILE detection (LITE|STANDARD|EPIC), idempotency probe, guards
[1] DESIGN   → subagent per stage, tuần tự — stage sau ăn artifact stage trước
               (model: 03 opus · 04 sonnet · 05 opus · 06 sonnet)
               LITE:     04 → 05(trim: Lens 1+3 bắt buộc, Lens 2/4 theo surface) → 06   (bỏ hẳn 03)
               STANDARD: (03? — economy && chưa validated) → 04 → 05(đủ 4 lens) → 06
               EPIC:     (03?) → 04 + Decomposition Gate (Module Split Plan)
                                → per-module theo build order: 05 → 06 → merge dependency graph
               ┌─ stage-result {status, profile_escalation?} — nhánh xử lý cho TỪNG stage ────────┐
               │ OK          → stage kế; kèm profile_escalation → nâng profile, chạy bổ sung      │
               │               stage thiếu (không bao giờ tự hạ)                                  │
               │ QUESTIONS   → hỏi user ≤3 câu/lượt, max 2 vòng/stage → re-spawn ĐÚNG stage đó;   │
               │               quá 2 vòng = doc chưa chín → dừng, báo user bổ sung doc            │
               │ ABORT "suggest EPIC split" (05@STANDARD) → nâng EPIC → re-spawn 04 (chỉ bổ sung  │
               │               Module Split Plan) → tiếp nhánh EPIC per-module — KHÔNG dừng       │
               │ ABORT "Yêu Cầu Tối Giản Hóa GDD" → banner <!-- STALE --> → DỪNG cả pipeline      │
               │ JSON sai format → re-spawn 1 lần kèm nhắc contract; vẫn sai → dừng               │
               └──────────────────────────────────────────────────────────────────────────────────┘
[2] PLAN     → parse mapping §10.1–10.7 (EPIC: merge per-module, resolve [Module] cross-refs)
               → existence probe từng sub-feature (có code sẵn → CẤM pure-WF, hạ HYBRID/hỏi user)
               → route + gán exec tier PER ITEM: pure-WF (tier theo registry — thường M, L nếu
                 save field + cross-system) · HYBRID M/L (planner plan delta) · UI /new-ui S–M
                 xếp cuối batch + groundTruth probe (png có sẵn → approved · html → PENDING-APPROVAL ·
                 không có → PENDING-MOCKUP · clone:<Prefab> khi chỉ nhái layout cũ)
                 ⇒ một batch TRỘN nhiều tier
               → localize fold vào task feature · ownership map + topo order (§10.6)
[3] GROUND   → MỘT batch timestamp + NN topo → HYBRID: task-planner fan-out song song ≤10/wave
               (>20 work item → dừng hỏi chia phase) · pure-WF: orchestrator draft _TEMPLATE_WF
               → orchestrator TỰ viết N file → mockup-drafter fan-out cho UI item PENDING-MOCKUP
               (≤10/wave, KHÔNG chờ approval — draft park ở PENDING-APPROVAL) → git add (KHÔNG commit)
[4] REPORT   → profile + bảng task + promote order + câu hỏi/giả định
```

**Profile (chọn ở INTAKE, predicate máy-đọc-được — bảng đầy đủ ở [design-pipeline/README.md](design-pipeline/README.md)):**

| Profile | Khi nào | Khác gì |
|---|---|---|
| **LITE** | Không economy/monetization/competitive/backend-write VÀ ≤5 sub-feature (vd tutorial system, photo mode) | Bỏ hẳn 03-gdd-final; 05 bắt buộc 2 lens (Flow + Feasibility) — Lens 2/4 chỉ chạy khi spec lộ surface tương ứng; không simulation — cắt ~40–60% token design |
| **STANDARD** | Có economy/monetization HOẶC 6–15 sub-feature; **mặc định khi thiếu tín hiệu** | Flow đầy đủ như mô tả trên |
| **EPIC** | Backend write/PvP/social/guild HOẶC >15 sub-feature HOẶC nhiều bounded context | 04 xuất Module Split Plan → 05+06 chạy per-module theo build order → merge dependency graph; 05 thêm mục API Contract & Server Authority (bắt buộc khi có backend write) |

Stage được phép báo `profile_escalation` khi phát hiện profile sai (vd LITE lộ economy) → orchestrator nâng profile, chạy bổ sung stage thiếu — không làm lại từ đầu, không bao giờ tự hạ. 05 ở STANDARD gặp scope quá lớn → `suggest EPIC split` (nâng EPIC + split) thay vì abort hẳn; chỉ còn "Yêu Cầu Tối Giản Hóa GDD" khi chia module cũng không cứu được.

| Tình huống lúc dispatch | Hành vi |
|---|---|
| Chưa có artifact nào | Chạy full Stage 0→4 |
| `TechSpec/<Name>-Implementation.md` đã có (không STALE) | **Resume Stage 2** — bỏ qua toàn bộ design (đỡ tốn nhất) |
| Có Architecture/TechSpec nhưng thiếu mapping | Resume Stage 1 từ stage thiếu |
| Artifact mang banner `<!-- STALE: aborted -->` | Coi như chưa có → chạy lại stage đó |
| Batch planning cũ còn dở (trùng slug) | Liệt kê + hỏi: tiếp phần thiếu / làm lại |
| `backlog/in-progress/` có task (loop đang chạy) | Cảnh báo, chỉ tiếp khi user xác nhận |
| Stage trả `QUESTIONS` | Hỏi user ≤3 câu/lượt (max 2 vòng/stage) → re-spawn đúng stage đó với câu trả lời |
| Stage 05 trả `ABORT` (Lens 3 — scope vượt năng lực) | STANDARD: `suggest EPIC split` → nâng EPIC + re-spawn 04 (Decomposition Gate), KHÔNG dừng; chỉ dừng hẳn với **"Yêu Cầu Tối Giản Hóa GDD"** khi chia module cũng không cứu (hoặc đã EPIC mà 1 module vẫn vượt) — không đẻ task rác |
| Subagent trả JSON sai format | Re-spawn 1 lần kèm nhắc contract; vẫn sai → dừng, báo user |
| Mapping có >20 work item (kể cả UI) | Dừng hỏi có nên chia phase nhỏ hơn (fan-out cap: 10 HYBRID/wave) |

**Đặc điểm batch output (case A):**
- MỘT timestamp chung cho cả batch + `NN` = thứ tự topo từ Dependency Graph §10.6 → `promote` sort `(timestamp, index)` nên **NN chính là thứ tự thực thi**.
- Mỗi task mang: `**Context docs:**` (trỏ TechSpec — implementer không bịa lại số liệu), `**Depends on:**` (promote warn khi đứt dependency), `**Requires:** unity-editor` (chỉ UI task), và riêng UI task: `groundTruth=` trong `**Workflow args:**` (mockup pipeline — xem mục dưới).
- Batch **trộn nhiều tier** — exec tier thật per item nằm trong filename; về sau `/run-backlog` key review-gate theo tier TỪNG task (XS bỏ code-reviewer; security-auditor theo `$SENSITIVE` bất kể tier) và loop `--auto-model-by-tier` chọn model theo tier (XS/S → sonnet, M/L → opus).
- UI screen (§10.4) tách thành task `/new-ui` riêng, xếp **cuối batch**; task `/new-feature` tương ứng skip step 8 (prefab do task UI đảm nhận).
- Localize KHÔNG bao giờ là task riêng (không tạo git diff → run-backlog chết `NO_CHANGES`) — fold vào task feature sở hữu string.

---

## Mockup pipeline (UI ground truth)

Task `/new-ui` không được build từ mô tả text — nó cần ground truth hình ảnh (new-ui-guide.md §0a). Nguyên tắc xuyên suốt: **generate = autonomous (parallel-safe) · approve = human (serial, bất đồng bộ)** — duyệt visual là quyết định gu con người nhưng KHÔNG dừng planning.

```
planning (N phiên song song)                    /ui-mockup (MỘT phiên interactive, lúc nào tiện)
  phát hiện task UI                              grep PENDING-* trong backlog/planning/
  → spawn mockup-drafter per screen              → draft nốt phần thiếu (PENDING-MOCKUP)
    đọc .claude/ui-kit/ (v0 wireframe)           → MỞ UI REVIEW DASHBOARD KHI REVIEW
    ghi <S>.ui-spec.json → generate <S>.html     → user duyệt cả loạt / yêu cầu sửa spec
    (chỉ ghi pair — không review dashboard,      → tick approve → validate → export PNG
     không shared state — filesystem là queue)     bằng local script, không cần AI agent
  → task: groundTruth=PENDING-APPROVAL:<html>    → git add (KHÔNG commit)
```

| groundTruth trong `**Workflow args:**` | Nghĩa |
|---|---|
| `TechSpec/Mockups/<F>/<S>.png` | **Đã approve** — PNG là contract đóng băng; v1 sửa `.ui-spec.json` rồi generate HTML (legacy sửa HTML) |
| `PENDING-APPROVAL:<...>.html` | Draft có rồi, chờ human duyệt ở `/ui-mockup` |
| `PENDING-MOCKUP` | Chưa có draft (drafter fail/skip) — `/ui-mockup` sẽ draft nốt |
| `clone:<ExistingPrefab>` | Khỏi mockup — màn chỉ nhái layout prefab có sẵn (đường spec-sheet §0a) |

- `backlog-ops.py promote --check` xuất `mockup_warnings` khi task còn `PENDING-*`; đây là hard blocker. Phải approve thành PNG hoặc chuyển sang `clone:<ExistingPrefab>` hợp lệ trước khi `/add-to-backlog` mutate.
- Mockup v1 dùng `.ui-spec.json` làm nguồn duy nhất → generate HTML → `/new-ui` dùng cùng spec; task vẫn giữ path HTML/PNG nên planning/backlog contract không đổi. Legacy embedded spec tiếp tục được đọc.
- Review mặc định chạy `python3 .claude/scripts/ui-review.py serve`: dashboard gọi API loopback để refresh/approve/approve-all trực tiếp; chỉ change request bằng ngôn ngữ tự nhiên mới mở AI agent.
- UI-kit v0 extract từ template prefab YAML (không cần Unity sống): `python3 .claude/scripts/ui-kit-sync.py` — chạy lại khi `Assets/Resources/Prefabs/Templates/` đổi; v1 (PNG chụp thật qua MCP) thay CSS background mà không đổi class/JSON shape.
- Task lai M/L tự dựng **màn hình mới** → **TÁCH màn thành task `/new-ui` riêng** (giống `/planning-system`). Task `/new-ui` đi qua item 2b → `mockup-drafter` spawn **ngay lúc planning** → draft park `PENDING-APPROVAL` sẵn để approve; task logic mang `**Depends on:**` màn đó. KHÔNG gộp màn vào task logic (gộp thì không spawn drafter → không có gì để approve — chính là bug đã fix). Escape hatch (màn không tách được): giữ 1 task + dòng `**Mockup:** groundTruth=PENDING-MOCKUP (screen=…)`. Backstop cả hai đường: `backlog-ops promote` chặn cứng task nhắc `/new-ui` mà thiếu token `groundTruth=`. (Chuỗi cũ `Needs mockup: yes` là no-op.)

---

## Khi nào hệ thống DỪNG HỎI user (mọi case)

Chỉ đúng các nhóm sau — mọi thứ khác tự quyết + ghi assumption:

1. Giá trị **economy/reward** (giá IAP, số lượng, tỉ lệ drop)
2. **Save data / migration / persist-restart**
3. **Backend / auth / IAP / security / leaderboard**
4. **UX flow / product behavior cốt lõi**
5. Acceptance criteria / verify steps bị mơ hồ

→ Docs đầu vào đã chốt đủ số liệu = chạy **0 câu hỏi**, full auto tới N task. Giá trị thiếu thuộc nhóm cấm = marker `[DECISION NEEDED]` + câu hỏi — **không bao giờ tự bịa**.

→ Riêng **duyệt mockup UI** (visual composition — nhóm cấm-bịa thứ 5, mở rộng của nhóm 4): xử lý **bất đồng bộ**, KHÔNG dừng pipeline — generate tự động lúc planning, approve gom một phiên tại `/ui-mockup`.

---

## Sau khi chạy xong (mọi case)

- File nằm ở `backlog/planning/` — **chưa** vào queue, **không** touch `BACKLOG.md`, **không** commit (case A có `git add` để promote không chết trên file untracked).
- User review → **task UI: `/ui-mockup`** (duyệt UI Review Dashboard + approve → PNG → groundTruth; màn approved tự biến mất; còn `PENDING-*` sẽ bị `mockup_warnings` block) → `/add-to-backlog` (batch case A: chọn all trong 1 lần promote là giữ đúng thứ tự; promote lẻ sẽ được warn `dependency_warnings` nếu đứt `Depends on`) → `/run-backlog`.
- Task UI (`Requires: unity-editor`) khi loop chạy headless sẽ tự `defer` về cuối TODO; nếu TOÀN BỘ task còn lại đều cần Editor → loop pause với `EDITOR_REQUIRED`. Mở Unity Editor rồi chạy tiếp.

---

## Điểm dễ nhầm

- **"Tạo 1 package IAP" KHÔNG phải new-system** — 0b nhường 0a, nó thành CASE B (WF pure, tier M, sensitive) chứ không kéo cả pipeline thiết kế ra. Chạm economy chỉ là tín hiệu auto-bump tier, không phải tín hiệu new-system.
- **WF không phải tier** — task workflow-backed vẫn mang exec tier thật (XS/S/M/L) trong filename; tier quyết định review-gating của run-backlog (code-reviewer, qa-verifier) — riêng **security-auditor spawn theo `$SENSITIVE` của diff, bất kể tier** (XS/S chạm Purchase*/Auth*/value-bearing vẫn bị audit); WF chỉ quyết định "load workflow trước khi implement".
- **Profile không phải Tier** — hai trục co giãn ĐỘC LẬP: profile (LITE/STANDARD/EPIC) scale độ sâu pipeline THIẾT KẾ của cả hệ thống (chỉ Case A); tier (XS/S/M/L) scale độ sâu spec + review-gate của TỪNG task, áp cả trong batch lẫn ngoài. Hệ thống EPIC vẫn đẻ ra task S; batch nào cũng trộn nhiều tier.
- **Phân vân tier → chọn tier LỚN hơn** — run-backlog KHÔNG tự escalate tier lúc thực thi; tier thấp oan = task bỏ qua reviewer oan (gate key theo tier).
- **Batch mode không đệ quy** — khi `/planning-system` tái sử dụng drafting path của planning-task (flag `origin: planning-system`), STEP 0b/0a-dispatch bị tắt hoàn toàn; depth cap = 1.
- **Priority là tag, không phải thứ tự** — queue chạy theo FIFO/task-order (timestamp + NN), không reorder theo HIGH/MEDIUM/LOW.
