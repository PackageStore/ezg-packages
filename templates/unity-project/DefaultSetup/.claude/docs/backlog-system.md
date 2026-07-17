# Hệ thống Backlog & Autonomous Task Execution — Unity Project

> Tài liệu tham chiếu đầy đủ cho **toàn bộ vòng đời một task**: từ lúc capture ý tưởng → xếp hàng → agent tự thực thi qua các quality gate → commit. Đây là bản mô tả kiến trúc; nguồn chân lý là các `SKILL.md` / `command.md` / script được link ở mỗi mục.
>
> Bản trực quan (human-friendly, có tab diagram): [`backlog-system.html`](backlog-system.html).
> Flow planning chi tiết theo case: [`planning-task-flow.md`](planning-task-flow.md).

---

## 1. Mục tiêu thiết kế (tại sao hệ thống trông như vậy)

| Nguyên tắc | Cơ chế hiện thực | Lý do |
|---|---|---|
| **Token phẳng dù backlog lớn cỡ nào** | Split-file: mỗi task = 1 file trong `backlog/{planning,todo,in-progress,done}/`; `BACKLOG.md` chỉ là **index**. Agent đọc index + đúng 1 task nó pick. | Backlog 300 task vẫn tốn token như 5 task. |
| **Bookkeeping deterministic, không hand-edit** | Mọi chuyển trạng thái + sửa `BACKLOG.md` chạy qua `backlog-ops.py`. Mỗi lệnh mutate tự chạy `lint`. | Hand-edit từng làm hỏng index: leak tool-call markup, task dual-state, DONE bullet sai. |
| **Spec là contract, không phải ý tưởng thô** | `/planning-task` triage + spec đầy đủ (files, acceptance criteria, verify steps, guardrails) trước khi vào queue. | `run-backlog` implement đúng hướng ngay lần đầu. |
| **Không bịa quyết định rủi ro** | 5 nhóm cấm-bịa (economy · save-migration · backend/IAP/security · UX flow · acceptance criteria). Thiếu = hỏi user / marker `[DECISION NEEDED]`. | Một con số bịa trông "đã chốt" sẽ lọt mọi gate và thành code. |
| **Generate autonomous · Approve human** | Mockup UI: agent tự draft (parallel-safe), human duyệt visual gom một phiên (bất đồng bộ, không dừng planning). | Gu thị giác là quyết định con người; nhưng chờ duyệt không được block N phiên planning song song. |
| **Parallel-safe capture, serial commit** | `/planning-task` ghi file timestamp-unique (nhiều cửa sổ song song không đụng nhau); `/add-to-backlog` là bước serial gán NNN. | Nhiều người/agent draft cùng lúc; chỉ 1 người pick vào queue. |

---

## 2. Ba lớp của hệ thống

```
   CAPTURE (parallel-safe)          QUEUE (serial)              EXECUTE (autonomous)
┌──────────────────────────┐   ┌──────────────────┐   ┌─────────────────────────────┐
│ /planning-task  <intent> │   │ /add-to-backlog  │   │ /run-backlog                │
│ /planning-system <doc>   │──▶│ promote --check  │──▶│ pick→branch→implement→gates │
│ /ui-mockup (UI ground    │   │ → promote        │   │ →DONE→commit+push agent/dev │
│   truth, human approve)  │   │ (gán NNN, index) │   │                             │
└──────────────────────────┘   └──────────────────┘   └─────────────────────────────┘
   backlog/planning/*.md          backlog/todo/*.md        backlog/in-progress → done/
   (KHÔNG touch BACKLOG.md)        (BACKLOG.md updated)     (BACKLOG.md updated, git push)
```

- **Capture** viết vào `backlog/planning/` — chưa vào queue, không touch `BACKLOG.md`, không commit.
- **Queue** là bước intentional commit của user: `promote` gán `NNN`, `git mv` planning→todo, append bullet vào `BACKLOG.md`.
- **Execute** là loop tự động: mỗi lần pick task TODO đầu tiên, implement, qua gate, mark done, push.

---

## 3. File layout & vòng đời task

```
BACKLOG.md                         # INDEX — chỉ ## TODO / ## IN PROGRESS / ## DONE bullets
backlog/
├── _TEMPLATE.md                   # index template + rule
├── _TEMPLATE_{XS,S,M,L}.md        # template theo tier
├── _TEMPLATE_WF.md                # template workflow-backed (scaffold /new-*)
├── _GUARDRAILS.md                 # định nghĩa các tag [SAVE] [ASYNC]... (không paste vào task)
├── _REVALIDATION-PLAYBOOK.md      # sửa spec drafted-ahead trước khi promote
├── planning/  <timestamp>[-NN]-<TIER>-<slug>.md   # đã draft, CHƯA queue
├── todo/      NNN-<TIER>-<slug>.md                # đã queue (promote đặt vào)
├── in-progress/                   # đang chạy (run-backlog start)
└── done/                          # xong (run-backlog done) — SOURCE OF TRUTH, không có bullet
```

**Vòng đời:** `planning → todo → in-progress → done`

**Quy ước filename:**
- Planning: `<timestamp>[-<NN>]-<TIER>-<slug>.md`. `NN` = index topo của một batch `/planning-system` (encode thứ tự dependency); vắng mặt với draft lẻ.
- Queue (todo/in-progress/done): `NNN-<TIER>-<slug>.md`. `NNN` = số thứ tự tuần tự = thứ tự thực thi. File promote **trước 2026-07-13** là `NNN-<slug>.md` legacy (không có tier).

**Tier invariant** (lint enforce): `<TIER>` trong **filename** == dòng `**Tier:**` trong **body** == `[TIER]` trong **bullet BACKLOG.md**. Body là source of truth (`run-backlog` đọc nó đầu tiên để gate).

---

## 4. Task tiers & template

Tier = **implementation scope**, KHÔNG phải risk. Nó chọn template + model/effort + review-gate. **Phân vân → chọn tier LỚN hơn** (run-backlog không tự escalate lúc thực thi; tier thấp oan = bỏ qua reviewer oan).

| Tier | Tín hiệu | Cost | Subagent draft? |
|---|---|---|---|
| **XS** | CSV tweak / đổi hằng số / xóa dead code / rename 1 file / thêm 1 `EventName` const. Không logic mới. | ~1K token | Không |
| **S** | Sửa logic 1 file, bug fix ≤2 file, save field nhỏ self-contained. Không screen/event cross-system mới. | ~3K token | Không |
| **M** | Multi-file: screen/popup mới, controller mới, save reshape, 3–8 file. | ~10–15K token | task-planner **chỉ khi complex** |
| **L** | Cross-cutting: IAP flow mới, save migration đa module, tích hợp hệ thống, 9+ file. | ~25K token | task-planner + risk pass |

**Auto-bump** (risk-driven, chỉ bump khi risk thật): chạm `Purchase*`/`IAP*`/`Receipt*` → ≥M; grant/spend currency, ghi server, leaderboard → ≥M; save migration đa module → L; >2 module hoặc >8 file → L.

**WF (workflow-backed) — orthogonal với tier:** khi intent là scaffold thuần khớp một `/new-*` command (`/new-feature`, `/new-ui`), dùng `_TEMPLATE_WF.md`: bỏ qua task-planner (tiết kiệm ~15–25K token), chỉ ghi `**Backed by workflow:**` + `**Workflow args:**` + `**Custom delta:**`. Tier thật vẫn nằm trong filename để review-gating không đổi. `/run-backlog` STEP 5.0 load command đó thay vì implement free-form.

**Field batch tùy chọn** (điền thật hoặc **XÓA cả dòng**, không để placeholder):
- `**Context docs:**` — trỏ `TechSpec/<Name>-Implementation.md` (implementer đọc để không bịa lại số liệu).
- `**Depends on:**` — filename/NNN task upstream; `promote` warn khi đứt.
- `**Requires:** unity-editor` — task không chạy headless được; `run-backlog` `defer` khi Editor không sống.
- `**Needs mockup:** yes` — task M/L tự dựng screen mới (không backed `/new-ui`); `/ui-mockup` sweep.

> ⚠️ Một `**Requires:** unity-editor` placeholder sót từ template làm `run-backlog` defer oan một task headless. (Parser bỏ qua field nằm trong `<!-- HTML comment -->`, nhưng field thật ngoài comment thì tin ngay.)

---

## 5. CAPTURE — `/planning-task`

Cửa vào duy nhất cho mọi intent. [Skill](../skills/planning-task/SKILL.md) · [Flow chi tiết theo case](planning-task-flow.md).

```
[0b] NEW-SYSTEM? → cả GDD / ≥2 module MỚI tương tác? → dispatch /planning-system (0a match = nhường)
[0]  TRIAGE      → XS / S / M / L (phân vân → tier LỚN hơn)
[0a] WF-DETECT   → scaffold thuần khớp /new-*? → skip task-planner, dùng _TEMPLATE_WF
[1]  EXTRACT     → What/Why/Scope/Priority/Constraints; clarify ≤3 câu/lượt (chỉ 5 nhóm cấm-bịa)
[2]  DRAFT       → XS thẳng · S codegraph 1–3 file · M-simple main-context · M-complex/L task-planner
                   · /new-ui: resolve groundTruth (mockup pipeline)
[3]  FILENAME    → backlog-ops.py timestamp → <ts>-<TIER>-<slug>.md
[4]  WRITE       → template + **Tier:** + **Guardrails:** tags → backlog/planning/
[5]  CHECK       → checklist theo tier (path thật, criteria đo được, ≥3 verify step cho M/L)
[6]  REPORT      → tier + lý do + assumptions → trỏ /add-to-backlog
```

**STEP 0b — New-system detection** (fire khi CẢ HAI): (1) doc-scale (cả GDD nhiều section, HOẶC ≥2 module MỚI tương tác); (2) KHÔNG gói được thành 1 task WF (0a match = nhường). Chạm economy đơn thuần KHÔNG kích 0b. Có idempotency probe (`TechSpec/<Name>-*.md` tồn tại → resume) + anti-recursion (flag `origin: planning-system` → tắt 0b/0a).

**task-planner subagent** (M-complex/L): schema đầy đủ nằm trong [`agents/task-planner.md`](../agents/task-planner.md); skill chỉ truyền dynamic context. Trả 1 JSON (files_to_touch, scope_control, completion_criteria, verify_steps, applicable_guardrails, mobile_impact, open_questions) + check **no-collision / no-phantom / real-path**. Không có quyền Write — orchestrator viết file.

**7 case** (chi tiết ở [planning-task-flow.md](planning-task-flow.md)): A (new-system), B (WF pure), C (WF hybrid), D (XS), E (S), F (M-simple), G (M-complex/L).

---

## 6. CAPTURE (lớn) — `/planning-system`

Design-first pipeline cho **hệ thống mới lớn** (GDD nhiều phần). [Skill](../skills/planning-system/SKILL.md) · [Design pipeline docs](design-pipeline/README.md).

```
[0] INTAKE  → FeatureName · PROFILE (LITE|STANDARD|EPIC) · idempotency probe · guards
[1] DESIGN  → subagent per stage (docs/design-pipeline/), stage sau ăn artifact stage trước:
              LITE:     04 → 05(trim) → 06                    (bỏ 03)
              STANDARD: (03-gdd-final?) → 04 → 05 → 06
              EPIC:     (03?) → 04 + Decomposition Gate → per-module: 05 → 06 (build order)
              stage-result {status: OK|QUESTIONS|ABORT, profile_escalation?}
[2] PLAN    → parse mapping §10.1–10.7 (EPIC: merge per-module) → work-item list
              + existence probe (code có sẵn → cấm pure-WF) + ownership map + topo NN order
[3] GROUND  → 1 batch timestamp + NN → task-planner fan-out (HYBRID, delta only, ≤10/wave)
              + orchestrator viết N file + mockup-drafter fan-out (UI) → git add (KHÔNG commit)
[4] REPORT  → profile + bảng N task + promote order + mockup state
```

**Profile** (predicate máy-đọc, [README](design-pipeline/README.md)):
- **LITE**: không economy/monetization/competitive/backend-write VÀ ≤5 sub-feature → bỏ 03, 05 chỉ Lens 1+3 → cắt ~40–60% token.
- **STANDARD**: có economy HOẶC 6–15 sub-feature; **mặc định khi thiếu tín hiệu**.
- **EPIC**: backend write/PvP/social HOẶC >15 sub-feature HOẶC nhiều bounded context → Module Split Plan → per-module.

**Stage files** ([design-pipeline/](design-pipeline/)): 01-gdd-concept · 02-gdd-production · 03-gdd-final (adversarial audit) · 04-feature-analysis → `Architecture.md` · 05-tech-spec → `TechSpec.md` (4-lens audit + ABORT) · 06-implementation-mapping → `Implementation.md` (§10.1–10.7 terminal-ready). Model: 03/05 opus, 04/06 sonnet.

**Batch output đặc trưng:** MỘT timestamp + `NN` topo (`promote` sort `(timestamp, index)` → NN = thứ tự thực thi); mỗi task mang `**Context docs:**` + `**Depends on:**`; UI task xếp cuối batch + `**Requires:** unity-editor`; batch **trộn nhiều tier**.

---

## 7. Mockup pipeline (UI ground truth) — tích hợp ui-catalog

Task `/new-ui` không được build từ mô tả text — cần **ground truth hình ảnh**. [Command](../commands/ui-mockup.md) · [mockup-drafter](../agents/mockup-drafter.md) · [ui-visual-reviewer](../agents/ui-visual-reviewer.md).

```
planning (N phiên song song)            /ui-mockup (MỘT phiên interactive)         build (/new-ui)
  spawn mockup-drafter per screen        grep PENDING-* trong backlog/planning/     đọc PNG + .ui-spec.json
  → đọc ui-catalog/ui-kit.json           → draft nốt phần thiếu                      = contract
  → ghi <S>.ui-spec.json → <S>.html      → UI Review Dashboard (ui-review.py)        → ui-visual-reviewer
  → groundTruth=PENDING-APPROVAL:<html>  → user approve → export PNG 1080×2400          chụp độc lập so PNG
     (KHÔNG chờ duyệt)                    → flip groundTruth=<png> → git add             (Phase A/B/C, ≤2 vòng)
```

**Capability gate:** UI-kit **sinh từ ui-catalog của chính project hiện tại** — `ui-kit-sync.py` đọc `ui-catalog/ui-tokens.json`, extract geometry/màu từ prefab thật → `ui-catalog/ui-kit.{json,css}`. **Template name trong spec = token id** (`ui.currency.single`...) để mockup và Unity build dùng chung từ vựng. Default contract là **1080×2400**. Fresh project chưa export catalog thì dừng với prerequisite rõ ràng; không copy generated catalog từ game khác.

**4 trạng thái groundTruth** (trong `**Workflow args:**`):

| Giá trị | Nghĩa |
|---|---|
| `TechSpec/Mockups/<F>/<S>.png` | **Đã approve** — PNG đóng băng là contract |
| `PENDING-APPROVAL:<...>.html` | Draft có, chờ human duyệt |
| `PENDING-MOCKUP` | Chưa có draft — `/ui-mockup` sẽ draft nốt |
| `clone:<ExistingPrefab>` | Khỏi mockup — nhái layout prefab có sẵn (tra ui-catalog trước) |

`promote --check` xuất `mockup_warnings` (hard blocker) khi còn `PENDING-*`. Generate = autonomous (parallel-safe, drafter chỉ ghi cặp của mình); approve = HUMAN-ONLY, serial qua token-protected loopback service `ui-review.py serve`. Filesystem là persistent queue; không có external/shared server.

---

## 8. QUEUE — `/add-to-backlog`

Bước serial pick planning → todo. [Skill](../skills/add-to-backlog/SKILL.md).

```
[1] LIST     → glob backlog/planning/*.md, parse tier/priority/title/timestamp
[2] DISPLAY  → list theo TASK ORDER (timestamp, NN — oldest/lowest first)
[3] PICK     → user chọn 1 / 1,3 / 1-3 / all (là SET, không phải sequence)
[4] OVERRIDE → priority tag (metadata, KHÔNG reorder queue; tier KHÔNG đổi được)
[4.5] PRECHECK → promote --check: 3 hard blocker (tier_errors / dependency_warnings / mockup_warnings)
[5] PROMOTE  → backlog-ops promote: gán NNN + git mv + append bullet + self-lint
[6] REPORT   → liệt kê moved, vị trí queue, remaining
```

**Sort theo task order, KHÔNG bucket theo priority:** một batch roadmap là dependency chain (`N+1` depends_on `N`); bucket HIGH→LOW đảo chain, đặt task dependent trên chính dependency của nó. `run-backlog` pick **bullet đầu tiên** trong `## TODO` → index order = execution order.

---

## 9. EXECUTE — `/run-backlog`

Loop tự động: pick task đầu, implement, qua gate, mark done, push `agent/dev` (KHÔNG tạo PR). [Skill](../skills/run-backlog/SKILL.md).

```
[1]   PICK     → backlog-ops pick (todo | in-progress resume | empty → PAUSED)
[1b]  REQUIRES → **Requires:** unity-editor + Editor không sống → defer (hoặc EDITOR_REQUIRED pause)
[2]   BRANCH   → checkout agent/dev + merge $BASE_BRANCH (tạo từ $BASE_BRANCH nếu chưa có)
[3]   START    → backlog-ops start: todo → in-progress + bullet move
[4]   CONTEXT  → đọc .claude/rules/* + task file + $CONTEXT_DOCS + code liên quan
[5]   IMPLEMENT→ (5.0 WF shortcut: load command + Context docs là input ưu tiên 0; /new-ui groundTruth gate)
                 write code → git add → 5b 3-tier compile check → 5c tier guard
[6]   REVIEW   → 6b deterministic preflight → 6c/6c-bis detect $SENSITIVE/$PERF_SENSITIVE
                 → spawn code-reviewer (+ performance-reviewer IF perf · + security-auditor IF sensitive)
                 song song → auto-fix ≤2 vòng
[7]   VERIFY   → spawn qa-verifier → auto-fix ≤2 vòng → 7d final preflight
[7.5] SMOKE    → runtime smoke (M/L, orchestrator tự chạy Unity MCP): play mode + console assert
                 + $SENSITIVE invariant suite + screenshot → auto-fix ≤2 vòng
[8]   DONE     → backlog-ops done: in-progress → done + viết completion summary (gates + verify steps)
[9]   SHIP     → backlog-ops lint → commit + push agent/dev
[10]  REPORT   → summary + manual verify steps cho user
```

**Tier guard (STEP 5c)** — cắt gate theo tier:

| Tier | Gate chạy |
|---|---|
| **XS** | Preflight. `has_blocking_definite=false` → thẳng DONE. Bỏ mọi reviewer + verify + smoke. |
| **S** | Preflight + code-reviewer (+ perf IF `$PERF_SENSITIVE` + security IF `$SENSITIVE`). Bỏ qa-verifier + smoke. |
| **M/L** | Full pipeline: STEP 6 + 7 + 7.5. |

**5b — 3-tier compile check** (Unity không có `npm run lint`): (1) Unity Editor MCP → (2) `dotnet build` → (3) Unity batch mode. Dừng ở tier đầu **chạy được**. Fix loop ≤2 vòng; skip chỉ khi cả 3 không chạy được. Xem [compile-validation.md](../rules/compile-validation.md).

**Reviewers spawn khi nào:**
- `code-reviewer` — mọi task (trừ XS pass preflight).
- `performance-reviewer` — chỉ `$PERF_SENSITIVE` (diff chạm Update/loop/Instantiate/pooling/UI-list/LINQ-alloc).
- `security-auditor` — chỉ `$SENSITIVE` (Purchase*/IAP*/Auth*/Token* hoặc value-bearing write). Spawn theo diff **bất kể tier** (XS/S chạm sensitive vẫn bị audit).
- `qa-verifier` — M/L, sau khi review pass.

**Runtime smoke (7.5, M/L)**: orchestrator TỰ chạy Unity MCP (không spawn subagent) — compile settled → play mode ~20–30s → execute acceptance recipe qua `unity_execute_code` (ASCII-only payload) → `$SENSITIVE` invariant suite (currency conservation, save-load roundtrip, reset scope) → đọc console (exception/NRE từ code diff = FAIL) → screenshot → exit play mode. Skip graceful khi MCP không kết nối / diff không có runtime surface.

**Stop token (không bao giờ bypass):** `BACKLOG EMPTY` · `NO_CHANGES` · `PREFLIGHT_BLOCKED` · `COMPILE_BLOCKED` · `BRANCH_BLOCKED` · `REVIEW_BLOCKED` · `VERIFY_BLOCKED` · `RUNTIME_BLOCKED` · `MOCKUP_BLOCKED` · `EDITOR_REQUIRED`. (`DEFERRED` KHÔNG phải stop — kết thúc iteration bình thường, lần sau pick task kế.) Không có `--ship-anyway`.

**Branch model:** `agent/dev` base theo `$BASE_BRANCH` (nhánh đang đứng lúc chạy = `develop`): lần đầu tạo từ nó, các lần sau merge nó vào `agent/dev` trước khi implement (conflict → `BRANCH_BLOCKED`, dừng). Chỉ push `agent/dev`, user merge tay `agent/dev → develop` sau khi chạy manual verify.

---

## 10. Deterministic bookkeeping — `backlog-ops.py`

Mọi chuyển trạng thái + sửa `BACKLOG.md` phải qua script này. Mỗi lệnh mutate tự chạy `lint`. Model **KHÔNG hand-edit** index.

```bash
python3 .claude/scripts/backlog-ops.py lint                      # directory↔index + tier invariant
python3 .claude/scripts/backlog-ops.py pick                      # task kế tiếp cho run-backlog (JSON)
python3 .claude/scripts/backlog-ops.py start <NNN>               # todo → in-progress
python3 .claude/scripts/backlog-ops.py done <NNN>                # in-progress → done
python3 .claude/scripts/backlog-ops.py demote <NNN>              # in-progress → head of todo (abandon)
python3 .claude/scripts/backlog-ops.py defer <NNN>               # bullet TODO → cuối queue (task cần Editor)
python3 .claude/scripts/backlog-ops.py promote <planning.md>...  # planning → todo (gán NNN-TIER, block khi đứt dep / mockup PENDING)
python3 .claude/scripts/backlog-ops.py promote --check <f>...    # preflight read-only: tier/dependency/mockup warnings
python3 .claude/scripts/backlog-ops.py timestamp                 # UTC YYYYMMDDTHHmmssSSS cho filename planning
```

**Lint kiểm (E1–E8):** required sections · leaked tool-call markup · bullet well-formed + trỏ file thật · tier invariant (filename==body==bullet) · orphan queue file · dual-state file · DONE bullet cấm · duplicate NNN. Atomic write (`os.replace`) — crash giữa chừng không để index cụt.

**Deterministic preflight** — `backlog-preflight.py` (port Python từ `.ps1`, JSON giống hệt): quét staged diff theo hard rule — `data-persistence` (PlayerPrefs, `Save()` trong Update, ghi `DataManager`) · `time-manager` (`DateTime.Now`) · `ui-manager` (`SetActive` cho UI) · `mobile-performance` · `console-noise` · `credential-pattern` · `value-write` · `missing-using`. Xuất `summary.has_blocking_definite` + `sensitive.value` (feed quyết định security-auditor).

---

## 11. Subagents

**Planning** (`/planning-task` · `/planning-system` · `/ui-mockup`):

| Agent | Role | Model |
|---|---|---|
| [`task-planner`](../agents/task-planner.md) | Draft spec M/L (JSON: files/criteria/guardrails/mobile impact + no-collision/no-phantom/real-path). Read-only. | opus |
| [`mockup-drafter`](../agents/mockup-drafter.md) | Sinh cặp `<Screen>.ui-spec.json` + HTML 1080×2400 (template = ui-catalog token). Parallel-safe. | opus |

**Execution** (`/run-backlog`):

| Agent | Role | Spawn khi |
|---|---|---|
| [`code-reviewer`](../agents/code-reviewer.md) | Review diff theo conventions (FeatureBaseController, UIManager, UniTask, TigerForge, localize, magic number). | Mọi task (trừ XS pass preflight) |
| [`performance-reviewer`](../agents/performance-reviewer.md) | Audit mobile-perf: GC alloc hot path, LINQ/string trong loop, Find/GetComponent không cache, pooling, canvas rebuild, O(n²). | `$PERF_SENSITIVE` |
| [`security-auditor`](../agents/security-auditor.md) | Threat model: credential leak, IAP integrity, save tampering, input validation. | `$SENSITIVE` (bất kể tier) |
| [`qa-verifier`](../agents/qa-verifier.md) | Cross-check từng acceptance criteria với diff → `manual_verify_steps`. | M/L sau review |
| [`ui-visual-reviewer`](../agents/ui-visual-reviewer.md) | Reviewer visual/structural độc lập cho build UI: tự chụp Unity MCP so groundTruth + hard rules create-ui. | Checkpoint Phase A/B/C khi build UI |

---

## 12. Chạy loop & vận hành

**macOS:** `.claude/scripts/run-backlog-loop.sh` là **controller** — mỗi iteration mở MỘT cửa sổ Terminal riêng chạy đúng 1 task `/run-backlog`, chờ xong (flag file) rồi mở task kế. Task pass tự đóng cửa sổ; task fail/blocked giữ cửa sổ để đọc lỗi. Dừng khi backlog rỗng / gặp stop-token / CLI non-zero / hết MaxIterations.

```bash
.claude/scripts/run-backlog-loop.sh                                  # dùng CLI default
.claude/scripts/run-backlog-loop.sh --auto-model-by-tier --max-iterations 5   # model theo tier
.claude/scripts/run-backlog-loop.sh --inline                         # chạy trong cửa sổ hiện tại
```

Hoặc chạy trực tiếp skill `/run-backlog` trong Claude Code, lặp lại khi cần — pipeline tự pause (`PAUSED` vào `.agents/state`) khi TODO rỗng.

**Sync `.claude/` → `.agents/`:** `.claude/` là canonical (track git); `.agents/` chỉ là link view (gitignore) cho Codex/Gemini/Cline đọc. Sau clone chạy `bash .claude/scripts/sync-to-agents.sh` một lần.

---

## 13. Từ điển trạng thái & stop-token (tra nhanh)

| Token | Ở đâu | Nghĩa | Xử lý |
|---|---|---|---|
| `PAUSED` | STEP 1 | TODO rỗng | Add task → re-run |
| `DEFERRED` | STEP 1b | Task cần Editor, có task headless khác | Tự pick task kế (không phải stop) |
| `EDITOR_REQUIRED` | STEP 1b | MỌI task còn lại cần Editor | Mở Unity Editor, xóa `.agents/state`, re-run |
| `BRANCH_BLOCKED` | STEP 2 | merge `$BASE_BRANCH` conflict | Resolve tay, re-run |
| `NO_CHANGES` | STEP 6a | Implement không ra diff | Task có thể đã xong / bị skip |
| `COMPILE_BLOCKED` | STEP 5b | Lỗi compile sau 2 vòng | Sửa tay |
| `PREFLIGHT_BLOCKED` | STEP 6b/7d | Critical finding deterministic còn | Sửa tay |
| `MOCKUP_BLOCKED` | STEP 5.0 | `/new-ui` groundTruth còn `PENDING-*` | `/ui-mockup` approve trước |
| `REVIEW_BLOCKED` | STEP 6 | reviewer block sau 2 vòng | Sửa / `demote` |
| `VERIFY_BLOCKED` | STEP 7 | qa-verifier fail sau 2 vòng | Sửa, re-run |
| `RUNTIME_BLOCKED` | STEP 7.5 | Runtime smoke fail sau 2 vòng | Sửa, re-run |

---

## 14. Nguồn tham chiếu

- Skills: [`planning-task`](../skills/planning-task/SKILL.md) · [`planning-system`](../skills/planning-system/SKILL.md) · [`add-to-backlog`](../skills/add-to-backlog/SKILL.md) · [`run-backlog`](../skills/run-backlog/SKILL.md) · [`create-ui`](../skills/create-ui/SKILL.md)
- Commands: [`ui-mockup`](../commands/ui-mockup.md) · [`new-feature`](../commands/new-feature.md) · [`new-ui`](../commands/new-ui.md)
- Docs: [planning-task-flow](planning-task-flow.md) · [design-pipeline/](design-pipeline/README.md)
- Scripts: `backlog-ops.py` · `backlog-preflight.py` · `ui-kit-sync.py` · `ui-review.py` · `ui-spec-{render,validator,extract}.py` · `run-backlog-loop.sh`
- Templates: `backlog/_TEMPLATE*.md` · `backlog/_GUARDRAILS.md` · `backlog/_REVALIDATION-PLAYBOOK.md`
- Rules: [`.claude/rules/`](../rules/) (code-style, core-system, data-persistence, third-party, compile-validation, project-structure, output-format)
