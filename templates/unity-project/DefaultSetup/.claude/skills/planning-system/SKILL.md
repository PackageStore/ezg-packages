---
name: planning-system
description: Design-first pipeline cho HỆ THỐNG MỚI LỚN (GDD/design doc nhiều phần) — validate thiết kế qua các stage trong .claude/docs/design-pipeline/ (04-feature-analysis → 05-tech-spec → 06-implementation-mapping, kèm 03-gdd-final khi economy chưa validated), rồi batch-ground mapping thành N task trong backlog/planning/ theo dependency order. Được dispatch tự động bởi /planning-task STEP 0b khi phát hiện new-system; user cũng có thể gọi trực tiếp với một doc, hoặc resume bằng "--from-mapping TechSpec/{Name}-Implementation.md". KHÔNG touch BACKLOG.md, KHÔNG tạo todo, KHÔNG commit.
---

# Planning System — Design-first Batch Orchestrator

Biến một **hệ thống mới lớn** (GDD / design doc) thành N task file grounded trong `backlog/planning/`, qua 4 stage:

```
[0] INTAKE   → normalize input, FeatureName, PROFILE detection (LITE|STANDARD|EPIC), idempotency probe, guards
[1] DESIGN   → subagent per stage (docs/design-pipeline/), theo profile:
               LITE:     04 → 05(trim) → 06                       (bỏ 03)
               STANDARD: (03-gdd-final?) → 04 → 05 → 06
               EPIC:     (03?) → 04 + Decomposition Gate → per-module: 05 → 06 (theo build order)
               mỗi stage trả {status: OK|QUESTIONS|ABORT, profile_escalation?} — QUESTIONS: hỏi user rồi re-spawn stage đó
[2] PLAN     → parse mapping §10.1/10.2/10.3/10.4/10.5/10.6/10.7 (EPIC: merge các mapping per-module) → work-item list + ownership map + topo order
[3] GROUND   → 1 batch timestamp + NN index → task-planner fan-out (delta only) → orchestrator TỰ viết N file
               → mockup-drafter fan-out cho UI item (ui-spec + generated HTML, KHÔNG chờ approval) → git add
[4] REPORT   → danh sách task + promote order + câu hỏi/giả định còn lại → trỏ /add-to-backlog
```

Đầu ra cuối **giống hệt** `/planning-task` thường: file `backlog/planning/<batchTS>-<NN>-<TIER>-<slug>.md`, tier XS/S/M/L thật trong filename, đi tiếp qua `/add-to-backlog` → `/run-backlog` không đổi. Khác biệt: thêm bộ artifact `TechSpec/<Name>-*.md` và các field `**Context docs:** / **Depends on:** / **Requires:**` trong body.

---

## Guards (đọc trước, áp dụng xuyên suốt)

- **Depth cap = 1:** skill này KHÔNG bao giờ dispatch chính nó, và mọi lần tái sử dụng drafting path của `/planning-task` phải mang flag `origin: planning-system` để STEP 0b/0a-dispatch bên đó bị tắt (chống đệ quy).
- **Không chạy đè loop:** nếu `backlog/in-progress/` có task (run-backlog đang chạy) → cảnh báo user và chỉ tiếp tục khi user xác nhận. `git add` cuối Stage 3 đụng index chung với loop.
- **Câu hỏi — chỉ 4 nhóm cấm-bịa:** pipeline chỉ dừng hỏi user cho quyết định thuộc: **giá trị economy/reward · save-migration · backend/IAP/security · UX flow cốt lõi** (đúng nhóm STEP 1 của `/planning-task` cấm đoán). Mọi thứ khác (path, class, pattern, tier, thứ tự) tự quyết và ghi thành assumption. Hỏi tối đa 3 câu/lượt.
- **KHÔNG touch `BACKLOG.md`, KHÔNG tạo file trong `backlog/todo/`, KHÔNG commit.** Duy nhất một ngoại lệ bookkeeping: `git add` (không commit) ở cuối Stage 3 — bắt buộc để `backlog-ops.py promote` không chết `git mv` trên file untracked.
- **Timestamp:** chỉ orchestrator mint (Stage 3), đúng MỘT lần cho cả batch. Cấm mọi subagent tự gọi `backlog-ops.py timestamp` — timestamp riêng lẻ phá dependency order khi promote sort theo `(timestamp, index)`.

---

## STAGE 0 — Intake

1. **Input:** một file doc (GDD concept/production/final, hoặc design doc tự do), hoặc `--from-mapping TechSpec/<Name>-Implementation.md` (bỏ qua Stage 1, vào thẳng Stage 2).
2. **FeatureName:** PascalCase, lấy từ tên file/heading doc. Đây là khóa cho mọi artifact path.
2b. **Profile detection** (predicate máy-đọc-được — bảng đầy đủ ở [design-pipeline/README.md](../../docs/design-pipeline/README.md)):
   - **LITE:** doc KHÔNG có economy/monetization/competitive/backend-write (không IAP/currency/ads-reward/shop/leaderboard/PvP) VÀ ước lượng ≤5 sub-feature.
   - **EPIC:** doc có backend write / PvP / social / guild, HOẶC >15 sub-feature, HOẶC nhiều bounded context rõ rệt.
   - **STANDARD:** còn lại — và là **mặc định khi không đủ tín hiệu** (an toàn hơn LITE).
   - Ghi profile đã chọn + lý do vào report. Mọi stage prompt từ đây mang dòng `profile: <LITE|STANDARD|EPIC>`.
   - **Escalation giữa chừng:** stage trả `profile_escalation` (vd LITE chạy 04 lộ ra economy/12 sub-feature) → nâng profile, chạy **bổ sung đúng stage/độ sâu còn thiếu** (vd LITE→STANDARD sau 04: chạy predicate gdd-final; nếu cần thì chạy 03 rồi re-spawn 04 với Final GDD, không thì chỉ nâng audit 05 lên 4 lens). KHÔNG bao giờ tự hạ profile.
3. **Idempotency probe:** Glob `TechSpec/<FeatureName>-*.md`:
   - Có `-Implementation.md` (không mang banner STALE) → đề nghị resume từ Stage 2 (đỡ chạy lại design đắt đỏ).
   - Có `-Architecture.md`/`-TechSpec.md` nhưng thiếu mapping → resume Stage 1 từ stage đầu tiên còn thiếu.
   - Artifact có banner `<!-- STALE: aborted ... -->` → coi như không tồn tại (chạy lại stage đó).
   - Đồng thời Glob `backlog/planning/*-<slug-của-feature>*.md` — nếu batch cũ còn dở, liệt kê và hỏi user: tiếp tục phần thiếu / làm lại.
4. **Loop guard** (xem Guards).

## STAGE 1 — Design-validate (subagent per stage, tuần tự)

Spawn **một general-purpose subagent cho mỗi stage**, tuần tự (stage sau ăn artifact của stage trước). Stage file nằm tại [.claude/docs/design-pipeline/](../../docs/design-pipeline/) (xem README của thư mục đó). Model per stage theo [.claude/docs/design-pipeline/model-assignment.md](../../docs/design-pipeline/model-assignment.md): feature-analysis → `sonnet`, tech-spec → `opus`, mapping → `sonnet`, gdd-final → `opus`.

**Stage list theo profile (từ STAGE 0):**
- `LITE`: 04 → 05 → 06 — **KHÔNG chạy 03** (đã chứng minh không có economy/competitive); 05 tự trim audit theo dòng `profile: LITE`.
- `STANDARD`: (03 theo predicate dưới) → 04 → 05 → 06.
- `EPIC`: (03 theo predicate) → 04 (Decomposition Gate bật, trả Module Split Plan) → **lặp per module theo build order**: 05 → 06 cho từng module, `[FeatureName]` truyền dạng `<Name>-<Module>`, prompt module sau kèm đường dẫn TechSpec các module trước (interface chéo khớp nhau) + danh sách tên module khác (cho §10.6 cross-refs `[<Module>] ...`). Kết quả: mỗi module một bộ `TechSpec/<Name>-<Module>-{TechSpec,Implementation}.md`.

**Stage tùy chọn — gdd-final (predicate cụ thể, không cảm tính; chỉ STANDARD/EPIC):** chạy TRƯỚC stage 04 khi thỏa CẢ HAI:
- Doc có nội dung economy/monetization (IAP/shop/currency/ads-reward), VÀ
- Doc CHƯA validated: không nằm trong `GDD/Final/` VÀ không chứa marker `<!-- validated: gdd-final -->` ở đầu file.
Khi chạy: subagent theo [.claude/docs/design-pipeline/03-gdd-final.md](../../docs/design-pipeline/03-gdd-final.md); output `GDD/Final/<Name>-Final.md` (có marker) **trở thành input** cho stage 04.

**Ba stage chính** — mỗi subagent nhận prompt gồm:
1. Dòng đầu: `invoked-by: planning-system` + `profile: <LITE|STANDARD|EPIC>` (bật stage-result contract + độ sâu audit trong stage file).
2. Lệnh đọc và tuân theo đúng stage file:
   - [.claude/docs/design-pipeline/04-feature-analysis.md](../../docs/design-pipeline/04-feature-analysis.md) → input: doc (hoặc Final GDD); output: `TechSpec/<Name>-Architecture.md` (EPIC: kèm `## Module Split Plan`)
   - [.claude/docs/design-pipeline/05-tech-spec.md](../../docs/design-pipeline/05-tech-spec.md) → input: Architecture **+ đường dẫn GDD gốc** (bắt buộc truyền — coverage check cần nó); output: `TechSpec/<Name>-TechSpec.md`
   - [.claude/docs/design-pipeline/06-implementation-mapping.md](../../docs/design-pipeline/06-implementation-mapping.md) → input: TechSpec; output: `TechSpec/<Name>-Implementation.md`
3. Nhắc lại 4 nhóm cấm-bịa: giá trị thiếu thuộc nhóm đó = marker `[DECISION NEEDED]` + `status=QUESTIONS`, không phải chỗ sáng tác. Section không áp dụng = `N/A — <lý do>`, hợp lệ.

**Xử lý stage-result:**
- `OK` → stage kế. Có kèm `profile_escalation` → nâng profile theo STAGE 0.2b rồi mới đi tiếp.
- `QUESTIONS` → gom câu hỏi, hỏi user (≤3 câu/lượt), rồi **re-spawn ĐÚNG stage đó** với câu trả lời nối vào prompt ("USER DECISIONS: ..."). Không restart pipeline. Tối đa 2 vòng hỏi/stage — còn thiếu nữa nghĩa là doc chưa chín: dừng, báo user bổ sung doc.
- `ABORT` với `abort_reason` bắt đầu `suggest EPIC split:` (từ 05 khi đang STANDARD) → nâng profile EPIC, re-spawn 04 chỉ để bổ sung `## Module Split Plan` (input: Architecture hiện có), rồi tiếp tục nhánh EPIC per-module. KHÔNG dừng hẳn.
- `ABORT` dạng **"Yêu Cầu Tối Giản Hóa GDD"** (scope vô cứu, hoặc 1 module đơn lẻ của EPIC vẫn vượt năng lực) → đảm bảo artifact dở có banner `<!-- STALE: aborted at <stage> -->`, báo user nguyên văn Warning + lý do, DỪNG toàn pipeline. Không ground gì cả.
- Subagent trả JSON sai format → re-spawn 1 lần với nhắc contract; vẫn sai → dừng, báo user.

## STAGE 2 — Plan (parse mapping, chuẩn bị batch)

Đọc `TechSpec/<Name>-Implementation.md`, parse **đủ các section** (thiếu section nào phải nêu rõ, không âm thầm bỏ).

**EPIC — merge trước khi parse:** đọc TẤT CẢ `TechSpec/<Name>-<Module>-Implementation.md`, ghép thành một work-list chung: (a) §10.1/10.4 nối theo build order của Module Split Plan; (b) dependency chéo module (format `[<Module>] <SubFeature>` trong §10.6) resolve thành edge thật giữa các item; (c) một item trùng deliverable giữa 2 module = lỗi merge → dừng hỏi user (ownership map bên dưới là công cụ kiểm). NN đánh liên tục xuyên toàn hệ thống (module 1 trước, trong module theo §10.6 nội bộ), batch-ground chạy theo **wave = module**.

1. **§10.1 Sub-Features** → mỗi dòng = 1 work item code. Route:
   - **Existence probe (bắt buộc, trước khi cho route pure-WF):** Glob/`codegraph_search` folder `Assets/_Project/Features/<Domain>/<SubFeature>/` + class `<SubFeature>Controller|<SubFeature>Service`. **Có hit → CẤM pure-WF**: hạ xuống HYBRID M (task-planner sẽ chạy check de-dup 2a của nó) hoặc nêu collision cho user quyết.
   - Không hit + mapping không kèm logic ngoài scaffold → **pure-WF** `/new-feature` (exec tier M; L nếu save field + cross-system events per registry 0a).
   - Có logic/wiring/balance ngoài scaffold → **HYBRID M/L** (task-planner plan delta).
2. **§10.4 UI Screens** → mỗi screen = 1 work item `/new-ui`-backed riêng (exec tier S–M), **kể cả root screen**: task `/new-feature` tương ứng sẽ ghi rõ trong Custom delta "SKIP workflow step 7 (UI prefab — `/new-ui`) — prefab do task <NN> đảm nhận". Mọi UI item: `**Required skills:** /create-ui /compile-check`, `**Requires:** unity-editor`, `depends_on` feature sở hữu nó, và xếp **cuối batch** (sau toàn bộ code item) để loop headless chạy hết phần code trước.
   - **groundTruth + fast lane** — probe idempotent trước khi gán: PNG đã có → approved; chỉ có HTML → `PENDING-APPROVAL`. Khi `ui-catalog/ui-tokens.json` + `ui-kit.json` tồn tại, phân loại clone / kit-composition / custom như `/planning-task`. Fresh project chưa export catalog chỉ được dùng supplied image hoặc clone prefab resolve được dưới `Assets/`; trường hợp còn lại giữ `PENDING-MOCKUP`, lane `custom`, và báo prerequisite export catalog + chạy `ui-kit-sync.py`. Không bao giờ copy catalog từ game khác.
3. **§10.2/10.3/10.5/10.7** → cắt đúng các dòng thuộc từng sub-feature, sẽ paste vào body task tương ứng (đường dẫn số liệu từ TechSpec vào task — implementer không phải bịa lại).
4. **§10.6 Dependency Graph** → thứ tự topo cho code items; UI items nối đuôi. Gán `NN` = 01..N theo thứ tự này. UI screens cũng phải nằm trong graph — thiếu thì tự suy từ "screen thuộc feature nào".
5. **Ownership map:** mỗi deliverable (class/CSV/prefab/EventName) thuộc đúng MỘT item; item khác chỉ *reference*. Map này inject vào mọi prompt task-planner và dùng cho sweep cuối Stage 3.
6. **Localize:** KHÔNG bao giờ là task riêng (không tạo git diff độc lập đáng tin) — fold thành criterion trong task feature sở hữu string.
7. **Fan-out cap:** >10 HYBRID item → chia wave ≤10, chạy wave tuần tự. >20 item tổng → dừng hỏi user có nên chia phase nhỏ hơn không.

## STAGE 3 — Ground (batch-write)

1. **Mint MỘT batch timestamp:** `python3 .claude/scripts/backlog-ops.py timestamp` → `batchTS`. Pre-assign toàn bộ filename: `<batchTS>-<NN>-<TIER>-<slug>.md` (NN từ Stage 2 — `promote` sort `(timestamp, index)` nên NN **chính là** thứ tự thực thi).
2. **HYBRID items — task-planner fan-out:** orchestrator (chính context này) spawn các [`task-planner`](../../agents/task-planner.md) subagent trong **một tool-use block song song** (≤10/wave). Mỗi prompt gồm: TIER + USER INTENT (từ mapping), các dòng §10.x của riêng sub-feature đó, ownership map, và clause: *"Artifact do task <NN'> trong batch này tạo ra được phép reference như 'produced by a named upstream task' — KHÔNG tính là phantom; ghi rõ nguồn."* Task-planner chỉ trả JSON — **orchestrator là người viết file** (task-planner không có quyền Write; đây là chủ đích).
3. **Pure-WF items:** orchestrator draft trực tiếp bằng `_TEMPLATE_WF.md` (không subagent) — lift checklist của `/new-feature` thành criteria.
4. **Viết N file tuần tự** (orchestrator, không ủy quyền). Mỗi file bắt buộc có:
   - `**Tier:**` khớp TIER trong filename.
   - `**Backed by workflow:**` + `**Workflow args:**` (WF/HYBRID).
   - `**Mockup lane:** kit-composition|custom` — trên UI item non-clone chưa có supplied PNG, kể cả khi còn `PENDING-MOCKUP`.
   - `**Context docs:**` → `TechSpec/<Name>-Implementation.md`, `TechSpec/<Name>-TechSpec.md`.
   - `**Depends on:**` → filename planning của (các) task upstream, hoặc `none`.
   - `**Requires:** unity-editor` — CHỈ trên UI item.
   - Description/Custom delta có paste sẵn các dòng mapping §10.x liên quan (Domain bucket, Manager Type, CSV columns + 6 resource fields, event rows, registration points) — số liệu economy lấy nguyên văn TechSpec.
   - `**Guardrails:**` tags per quy tắc `/planning-task` STEP 4.
4b. **Mockup-drafter fan-out (UI item còn `PENDING-MOCKUP`):** spawn [`mockup-drafter`](../../agents/mockup-drafter.md) song song ≤10/wave (cùng cap 3.2) — prompt gồm featureName/screenName/branch/**lane** + outputPath `TechSpec/Mockups/<F>/<S>.html` + path task file + `**Context docs:**`. Drafter phải phát structured patch cho lựa chọn rời rạc khi có thể để dashboard áp dụng tức thì; HTML chỉ là render artifact, không phải file user phải author. **Generate = autonomous, approve = human:** KHÔNG chờ user duyệt ở đây. Chỉ `created`/`recovered`/`exists` (validated spec+HTML pair) hoặc `legacy-exists` (validated legacy HTML), sau khi confirm HTML tồn tại, mới được đổi `PENDING-MOCKUP` → `PENDING-APPROVAL:<path.html>`; `error` giữ `PENDING-MOCKUP`.
5. **Batch quality check** (thay cho STEP 2b/5 per-task của `/planning-task` — batch mode KHÔNG check-in user từng draft):
   - Per-file: đúng bộ check tier tương ứng trong `/planning-task` STEP 5 (WF/M/L).
   - Batch-level: (a) sweep ownership — không hai file nào cùng declare một deliverable; (b) mọi `**Depends on:**` trỏ tới filename có thật trong batch hoặc task đã tồn tại; (c) NN liên tục 01..N, không trùng.
6. **Stage only paths that exist:** `git add backlog/planning/ TechSpec/`, và chỉ `git add GDD/` khi pipeline thực sự tạo artifact ở đó. KHÔNG commit. `backlog-ops promote` có fallback cho planning draft untracked, nhưng staging design artifacts vẫn tránh để run-backlog `git add -A` quét nhầm về sau.

## STAGE 4 — Report

Báo user, theo thứ tự:
0. **Profile** đã chọn (LITE/STANDARD/EPIC) + lý do (predicate nào match) + escalation giữa chừng nếu có.
1. Bảng N task: `NN · TIER · slug · backed-by · depends-on · requires` (EPIC: nhóm theo module).
2. **Promote order:** một lệnh `/add-to-backlog` chọn cả batch là đủ — `promote` sort `(timestamp, NN)` tự giữ đúng thứ tự. Cảnh báo: promote lẻ tẻ có thể đứt dependency (script sẽ warn qua `dependency_warnings`).
3. **UI tasks:** cần Unity Editor sống; loop headless sẽ tự `defer` chúng về cuối TODO — mở Editor rồi chạy tiếp là xong. Kèm bảng **mockup state** từng screen (approved / `PENDING-APPROVAL` / `PENDING-MOCKUP` / `clone:`) + nhắc: chạy `/ui-mockup` duyệt UI Review Dashboard **trước** `/add-to-backlog` — `mockup_warnings` là hard blocker nếu còn `PENDING-*`.
4. **Revalidation:** batch dùng ngay theo thứ tự → không cần; nếu để lâu hoặc promote từng phần sau khi code base đổi → chạy `backlog/_REVALIDATION-PLAYBOOK.md` trước khi promote.
5. Câu hỏi đã hỏi + trả lời, và mọi assumption đã tự quyết.
6. Artifact design: `TechSpec/<Name>-Architecture.md` / `-TechSpec.md` / `-Implementation.md` (+ `GDD/Final/<Name>-Final.md` nếu có) để user review thiết kế.

DO NOT commit. DO NOT touch `BACKLOG.md`. DO NOT create anything in `backlog/todo/`.
