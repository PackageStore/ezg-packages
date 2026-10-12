# Autonomous Backlog System

> Chuyển ra từ `CLAUDE.md` (mục cùng tên) để khỏi nạp vào mọi phiên và mọi subagent —
> `CLAUDE.md` chỉ còn con trỏ + các invariant. Link trong file này tương đối với `.claude/docs/`.

Cơ chế thực thi task tự động dựa trên backlog split-file (token usage phẳng dù backlog lớn cỡ nào).

**Backlog nằm NGOÀI worktree** — ở git common dir:

```bash
BACKLOG_ROOT="$(git rev-parse --git-common-dir)/backlog"   # tức <repo>/.git/backlog
bash .claude/scripts/bootstrap.sh                           # clone/generate mới: link view + git + backlog
```

Đây là bookkeeping **riêng từng dev**: không commit, không merge, không track được. Lý do: khi backlog nằm trong tree, mỗi nhánh dev mang một `BACKLOG.md` riêng → merge lên main là đụng index và trùng NNN. `.git/` được **mọi `git worktree` của cùng clone dùng chung**, nên agent chạy trong worktree thấy đúng hàng đợi của dev. Hệ quả: **KHÔNG BAO GIỜ `git add` / `git mv` / commit file trong backlog** — mọi transition đi qua `backlog-ops.py` (filesystem move); DONE summary vì thế không nằm trong commit, nên report cuối của `/run-backlog` là bản giao duy nhất cho user.

Index `BACKLOG.md` + chi tiết từng task nằm trong `$BACKLOG_ROOT/{planning,todo,in-progress,done}/`. Format task: dùng tier-specific template (`.claude/backlog-templates/_TEMPLATE_XS/S/M/L.md`) hoặc `_TEMPLATE_WF.md` cho task workflow-backed — xem index tại `.claude/backlog-templates/_TEMPLATE.md`. Mọi task có `**Tier:** XS|S|M|L`; `backlog-ops.py lint` enforce tier trong filename, body và bullet khớp nhau. Toàn cảnh các case planning: [.claude/docs/planning-task-flow.md](planning-task-flow.md).

**Giá trị per-project** (source root, base branch, threat surface, tên solution) nằm trong `.claude/project-profile.json`, KHÔNG viết cứng trong skill/agent — nhờ vậy các file đó giữ nguyên byte giữa mọi project và update được từ template. Thiếu file thì `project_profile.py` fallback về default:
```bash
python3 .claude/scripts/project_profile.py            # in toàn bộ key đã merge
python3 .claude/scripts/project_profile.py sourceRoot # một key
```

**Task lifecycle:** `planning → todo → in-progress → done`

**Field batch tùy chọn trong task:** `**Context docs:**` (TechSpec cho implementer), `**Depends on:**` (promote cảnh báo dependency), `**Requires:** unity-editor` (không có Editor thì defer; nếu toàn queue cần Editor thì pause `EDITOR_REQUIRED`), `**Needs mockup:** yes` (task UI cần visual contract). Xóa field không dùng; không để placeholder sống.

**Workflow-backed routing (hybrid):** khi intent là scaffold thuần khớp một `/new-*` command (`/new-feature`, `/new-ui`), `/planning-task` (STEP 0a) BỎ QUA Plan subagent (tiết kiệm ~15–25K token) và ghi thin-spec `_TEMPLATE_WF.md` với field `**Backed by workflow:**` + `**Workflow args:**`. Khi thực thi, `/run-backlog` (STEP 5.0) nạp đúng command đó (và skill mà nó delegate, ví dụ `/new-ui` → `create-ui`) thay vì implement free-form. Task lai (scaffold + logic custom) dùng template M/L bình thường nhưng kèm `**Backed by workflow:**`, Plan subagent chỉ plan phần delta. WF KHÔNG phải tier — tier thật vẫn nằm trong filename/bullet để review-gating của run-backlog không đổi. **Loại trừ:** `/package-module` (đóng UPM package, push thẳng monorepo `ezg-packages` — out-of-band, không qua agent/dev; chạy tay qua skill cùng tên; ĐỪNG nhầm với `/new-package`, vốn là scaffold pack IAP trong game và CÓ đi qua routing này); `/publish-feature` + `/publish-unity-package` (publish lên Feature Hub — cũng out-of-band, ghi thẳng R2 + monorepo, chạy tay); `/setup-project` (setup project mới qua cửa sổ EzgKit — ghi PlayerSettings/asset cấu hình, out-of-band, chạy tay); `/auto-build-setup` (cài/upgrade module CI `ezg-autobuild` — đụng nhánh build + biến CI/CD trên GitLab, out-of-band, chạy tay qua skill cùng tên); `/new-class` (task S, Plan subagent vốn đã skip cho S + không có checklist → cứ làm task S thường); không có `/new-skill`/`/new-enemy-skill` trong project này.

| Command | Skill File | Description |
|---------|-----------|-------------|
| `/setup-project [id \| status \| all \| artstyle]` | [.claude/skills/setup-project/SKILL.md](../skills/setup-project/SKILL.md) | Setup project mới: mở cửa sổ `Ezg > EzgKit` đúng mục, in trạng thái từng mục (✅/🟡/⬜/⏸/➖), dry-run → diff → Apply khi dev đưa giá trị trong chat, và làm phần AI theo `requests` (soạn `ArtStyle.md`, kiểm tra link localize). Out-of-band, không qua backlog. |
| `/planning-task [intent]` | [.claude/skills/planning-task/SKILL.md](../skills/planning-task/SKILL.md) | Detect new-system → dispatch `/planning-system`; nếu task đơn thì triage workflow-backed / XS/S/M/L, ground bằng `task-planner` khi cần, rồi ghi `backlog/planning/<timestamp>-<TIER>-slug.md`. Parallel-safe, KHÔNG touch BACKLOG.md. |
| `/planning-system [doc \| --from-mapping ...]` | [.claude/skills/planning-system/SKILL.md](../skills/planning-system/SKILL.md) | Design-first pipeline cho hệ thống mới lớn: validate design qua [.claude/docs/design-pipeline/](design-pipeline/README.md), map ownership/dependency/topological order, rồi batch-ground thành N planning task. Resume được từ Implementation Mapping. |
| `/ui-mockup [task \| Feature: desc]` | [.claude/commands/ui-mockup.md](../commands/ui-mockup.md) | Spec-first visual contract: ui-kit của project → `.ui-spec.json` + HTML → **auto-approve** → frozen PNG. Dev chỉ vào cuộc khi drafter để lại câu hỏi cấm-bịa hoặc muốn sửa design. Không copy generated kit từ game khác. |
| `ui-kit` (skill, không phải command) | [.claude/skills/ui-kit/SKILL.md](../skills/ui-kit/SKILL.md) | Vòng đời contract mà mockup + `/new-ui` đọc: `ui-kit-sync.py` regenerate, `--check` báo stale (exit 1), `ui-kit-usage.json` giữ luật ghép template. Chạy lại mỗi khi prefab template đổi — preflight có rule `ui-kit-stale`. |
| `/add-to-backlog` | [.claude/skills/add-to-backlog/SKILL.md](../skills/add-to-backlog/SKILL.md) | List planning tasks → user pick → `promote --check` (tier/dependency/mockup blockers) → deterministic promote (gán NNN-TIER + git mv planning→todo + update BACKLOG.md). Serial operation. |
| `/run-backlog` | [.claude/skills/run-backlog/SKILL.md](../skills/run-backlog/SKILL.md) | Pick TODO đầu → requires/defer gate → `agent/dev` → implement → compile + deterministic preflight + tiered reviewers + QA + runtime/visual gates khi áp dụng → DONE → commit theo style `/push-in-session` (chỉ file của task, `<prefix> Tag: <subject>`, không trailer), push nếu có remote. Block tokens: `COMPILE_BLOCKED`, `PREFLIGHT_BLOCKED`, `REVIEW_BLOCKED`, `VERIFY_BLOCKED`, `RUNTIME_BLOCKED`, `EDITOR_REQUIRED`, `NO_CHANGES`, `BASE_MERGE_CONFLICT`, `LOOP_BUSY`, `TASK_BUSY`, `RESUME_CONFLICT` (gate visual/mockup block qua `REVIEW_BLOCKED`, không có sentinel riêng; `BASE_UNKNOWN` đã bỏ) — dưới loop runner chúng KHÔNG dừng loop mà kích **self-heal** (xem đoạn loop bên dưới); `DEFERRED` và `TASK_CHECKPOINTED` (task cần nhiều iteration) không phải block. KHÔNG tạo PR. |

**Subagents dùng cho `/run-backlog`:**

| Agent | File | Role | Model | Spawn khi |
|-------|------|------|-------|-----------|
| `code-reviewer` | [.claude/agents/code-reviewer.md](../agents/code-reviewer.md) | Review diff theo conventions của project (FeatureBaseController, UIManager, UniTask, TigerForge, DOTween, localize, magic number). JSON verdict pass/warn/block. | opus | Mọi task trừ XS |
| `performance-reviewer` | [.claude/agents/performance-reviewer.md](../agents/performance-reviewer.md) | Audit mobile-perf của diff: GC alloc trên hot path, LINQ/string trong loop, Find/GetComponent không cache, thiếu pooling, canvas/layout rebuild mỗi frame, thuật toán O(n²). JSON verdict pass/warn/block. | opus | Khi `$PERF_SENSITIVE` (song song với code-reviewer) |
| `security-auditor` | [.claude/agents/security-auditor.md](../agents/security-auditor.md) | Audit threat model: credential leak, IAP integrity, save tampering, input validation. JSON verdict. | opus | Khi diff touches `Purchase*`, `IAP*`, `Receipt*`, `Auth*`, `Token*`, file có credential pattern, hoặc `DataPlayer*`/`SaveData*` **có cấp–tiêu tài nguyên giá trị**. Save tiến trình thuần (level, unlock flag, settings) KHÔNG kích — xem run-backlog STEP 6c |
| `qa-verifier` | [.claude/agents/qa-verifier.md](../agents/qa-verifier.md) | Cross-check từng item trong "Acceptance criteria" của task spec với diff. Output `manual_verify_steps` cho user. | opus | Chỉ M/L (sau khi review pass) |
| `ui-visual-reviewer` | [.claude/agents/ui-visual-reviewer.md](../agents/ui-visual-reviewer.md) | So live Unity UI với approved PNG + ui-spec hoặc clone-source, kèm hard rules của create-ui; phase D chấm designer pass (layout giữ nguyên theo lock report, art/FX/motion theo ArtStyle). | opus | Checkpoint Phase A/B/C + Phase D (designer pass) khi build UI |

**Subagents dùng cho planning:** `task-planner` (opus) ground spec M/L và batch item; `mockup-drafter` (opus) chỉ sinh spec+HTML khi current-project catalog/kit đã sẵn sàng. Cả hai không implement, approve hay promote task.

> **Model là một nguồn sự thật duy nhất:** cột Model ở trên phản chiếu `model:` trong frontmatter của `.claude/agents/*.md`. Nhánh **S** của `/run-backlog` override tường minh `model: "opus"` cho code/perf reviewer để ghim sàn — không còn hạ xuống sonnet (XS không spawn reviewer nào ngoài `security-auditor` khi `$SENSITIVE`). Map theo tier của loop (`--auto-model-by-tier`): **mọi tier → opus**, khai trong `.claude/scripts/run-backlog-loop.sh` và bản PowerShell tương ứng.
>
> **Sonnet đã bị loại khỏi toàn bộ pipeline** (2026-09-10) vì độ chính xác không đạt; opus là sàn ở mọi vai trò. Nâng L tier lên `fable` là **opt-in per run**, chưa phải mặc định — `--l-model fable` — vì nó làm chi phí L tăng ~70% mà chưa có số đo chứng minh chất lượng hơn opus.

**Hai mode thực thi (`--mode` / `-Mode`)** — hỏi dev trước khi chạy loop, đánh đổi là thật:

| | `current` (mặc định) | `worktree` |
|---|---|---|
| Thư mục | chính checkout này | `<repo>-agent-<base>` bên cạnh |
| Nhánh commit | nhánh đang checkout (KHÔNG checkout, KHÔNG merge) | `agent/dev-<base>` — dev tự merge |
| Dev làm việc song song | **không nên** (agent `git add -A` cho review → file dở lọt vào diff review/compile; commit chỉ lấy file của task, nhưng file hai bên cùng sửa thì commit nguyên file) | được |
| compile-check + runtime smoke | **có** | **không** (worktree không có `.sln` và không có Editor gắn vào) |

**Loop chạy thủ công khi muốn:**
```bash
# macOS / Linux — controller tự spawn 1 Terminal window mỗi task, model/effort map theo tier
bash .claude/scripts/run-backlog-loop.sh --auto-model-by-tier
bash .claude/scripts/run-backlog-loop.sh --auto-model-by-tier --mode worktree
```
```powershell
# Windows
powershell -ExecutionPolicy Bypass -File .claude/scripts/run-backlog-loop.ps1 -Mode Current -AutoModelByTier
```
> Pipeline tự pause (`PAUSED` ghi vào `$BACKLOG_ROOT/state`, nằm trong `.git/` nên không commit) khi TODO rỗng.

**Self-heal (mặc định bật):** iteration kết thúc mà task chưa xong — `*_BLOCKED` / `NO_CHANGES` / `manual intervention required` / crash / watchdog kill / silent end — KHÔNG dừng loop: iteration sau resume đúng task đó ở **recovery mode** (run-backlog §1f: brief lỗi `AGENT_RECOVERY_BRIEF` ở `logs/backlog-loop/recovery-*.md`, model mạnh nhất, budget fix mới cho mọi gate, cấm nới gate); quá `--max-recoveries`/`-MaxRecoveries` (mặc định 3) → **park** task (`backlog-ops.py park`: partial work cất vào `refs/backlog/parked/<NNN>` rồi gỡ khỏi tree, task xuống cuối TODO, lần `start` sau trả `parked_work` để khôi phục) và chạy tiếp task khác. Usage/session limit → ngủ tới giờ reset rồi chạy tiếp (tối đa `--max-usage-wait-minutes` 600); lỗi API tạm thời (rớt kết nối / 529) → retry 6 lần (30s nhân đôi, trần 15 phút) rồi chờ tiếp từng nấc 30 phút; 403 hết quota org (`organization has disabled Claude subscription access`) → retry cùng task tối đa `--max-quota-retries`/`-MaxQuotaRetries` 5 lần (30s nhân đôi) rồi mới dừng; `TASK_BUSY` → chờ session kia (`--busy-wait-minutes` 180); `EDITOR_REQUIRED` → tự mở/restart Unity qua skill `restart-unity` (tối đa 2 lần); DONE thiếu receipt reviewer → iteration audit hậu kiểm. Agent không dừng để hỏi: quyết theo *Autonomous decision policy* (ghi `[DECISION-REVIEW]`). Loop chỉ dừng khi backlog rỗng (`PAUSED`) / chỉ còn task đã park / auth hỏng / chờ limit quá budget / **push thất bại** (nhánh lệch là việc của dev — STEP 9e) / max iterations. `--no-self-heal`/`-NoSelfHeal` = hành vi cũ (dừng ở block đầu tiên). Task cần nhiều iteration (vd một đợt validation dài hơn watchdog 180 phút) kết thúc iteration bằng `TASK_CHECKPOINTED` — KHÔNG phải lỗi: work giữ staged + resume notes, iteration sau tự `resume`; prompt mỗi iteration mang `AGENT_ITERATION_DEADLINE` để agent checkpoint trước khi bị kill; quá `--max-checkpoints` (mặc định 6) iteration liên tiếp của cùng task → park (`CHECKPOINT_LIMIT`).

**Một consumer / clone + resume có journal** (`$BACKLOG_ROOT/runs/`): loop controller giữ **lease** (`runs/loop.json`, khoá theo pid + start time của controller — crash/kill -9 tự nhả), iteration chứng minh thuộc loop bằng env `BACKLOG_LOOP_TOKEN`; `/run-backlog` chạy tay khi loop đang sống → `LOOP_BUSY`, loop thứ hai → từ chối khởi động. `start` ghi **claim** (`runs/<NNN>.json`, chủ = process claude/codex/gemini đang làm) + baseline file dirty sẵn có → session khác đụng task đó → `TASK_BUSY` (exit 4, không đổi gì). Run chết giữa chừng (hết quota, crash, watchdog) → claim chết theo process → lần chạy sau `resume` nhận lại, in `partial_work` (tách khỏi dirt có sẵn) + checkpoint journal → làm TIẾP, không làm lại mù; mọi gate chạy lại. `done` mà chưa `shipped` → task **ship-pending**, `pick` trả nó đầu tiên. Commit STEP 9 vẫn **không trailer**: ngay trước `git commit`, STEP 9e ghi tree đã stage (`checkpoint <NNN> --step commit --tree "$(git write-tree)"`), nên lần sau biết chắc đã commit hay chưa bằng nội dung (commit nào có đúng tree đó) — chưa → commit trước khi task sau `git add -A` cuốn mất; rồi → chỉ push, không commit lại. Resume khác checkout/nhánh → `RESUME_CONFLICT` (exit 5).

**Deterministic preflight:**
```bash
# macOS / Linux — bản port Python, JSON output giống hệt bản .ps1
python3 .claude/scripts/backlog-preflight.py -Pretty
```
```powershell
# Windows
powershell -ExecutionPolicy Bypass -File .claude/scripts/backlog-preflight.ps1 -Pretty
```

**Deterministic backlog bookkeeping (`backlog-ops.py` — Python, mọi nền tảng):** mọi chuyển trạng thái backlog + sửa `BACKLOG.md` phải qua script này, model KHÔNG hand-edit index (hand-edit đã từng làm hỏng index: leak tool-call markup, task dual-state, DONE bullet trái quy tắc). Mỗi lệnh mutate tự chạy `lint` sau khi ghi:
```bash
python3 .claude/scripts/backlog-ops.py lint                      # directory↔index + tier invariant
python3 .claude/scripts/backlog-ops.py pick                      # task kế tiếp cho run-backlog (JSON)
python3 .claude/scripts/backlog-ops.py start <NNN>               # todo → in-progress + bullet move
python3 .claude/scripts/backlog-ops.py done <NNN>                # in-progress → done + bullet removal
python3 .claude/scripts/backlog-ops.py demote <NNN>              # in-progress → todo (abandon task blocked)
python3 .claude/scripts/backlog-ops.py defer <NNN>               # move TODO bullet to queue tail
python3 .claude/scripts/backlog-ops.py promote --check <f.md>... # read-only tier/dependency/mockup preflight
python3 .claude/scripts/backlog-ops.py promote <planning.md>...  # planning → todo (gán NNN-TIER, preserve task order)
python3 .claude/scripts/backlog-ops.py timestamp                 # timestamp UTC cho filename planning
python3 .claude/scripts/backlog-ops.py resume <NNN>              # nhận lại task IN PROGRESS / ship-pending của run đã chết + in phần việc dở nó để lại
python3 .claude/scripts/backlog-ops.py checkpoint <NNN> --step implemented|compile|preflight|review|qa|smoke
python3 .claude/scripts/backlog-ops.py checkpoint <NNN> --step commit --tree "$(git write-tree)"   # STEP 9e, ngay trước git commit (ship fence không trailer)
python3 .claude/scripts/backlog-ops.py shipped <NNN> --commit <sha> --push pushed|no-remote|failed   # sau commit STEP 9 (`--push skipped --note already-satisfied` chỉ cho task thỏa sẵn, không commit)
python3 .claude/scripts/backlog-ops.py park <NNN> --reason "..."  # self-heal hết budget: partial work → refs/backlog/parked/<NNN> + gỡ khỏi tree, task → cuối TODO (--keep-work giữ nguyên tree)
python3 .claude/scripts/backlog-ops.py task-commit <NNN>         # read-only: commit mang task (từ run record), null nếu chưa có
python3 .claude/scripts/backlog-ops.py lock status               # ai đang giữ lease loop / claim task (exit 4 = busy)
python3 .claude/scripts/backlog-ops.py lock break --yes [--task NNN]   # override tay khi holder chắc chắn đã chết
```
Regression test cho các script backlog (chạy trước khi sửa rule/transition nào): `python3 -m unittest discover -s .claude/scripts/tests -p "test_backlog_*.py"` (`test_backlog_park.py` chạy được cả Windows; `test_backlog_concurrency.py` cần process table POSIX).

**Sync `.claude/` → `.agents/`** (tạo junction/symlink một lần sau khi clone; `.agents/` chỉ là link views nên không cần chạy lại sau mỗi lần sửa file). `bootstrap` ở [§0](../../CLAUDE.md#0--bootstrap) làm cả link + backlog; hai script dưới là riêng bước link:
```bash
# macOS / Linux (symlink)
bash .claude/scripts/sync-to-agents.sh
```
```powershell
# Windows (junction)
powershell -ExecutionPolicy Bypass -File .claude/scripts/sync-to-agents.ps1
```

**Branch model:**
- Base branch = **nhánh đang mở** lúc bắt đầu loop, ghi vào git config `<gitConfigPrefix>.agentBaseBranch` (key lấy từ `project-profile.json`) để sống sót qua các vòng lặp.
- Mode `current`: KHÔNG tạo nhánh, KHÔNG checkout, KHÔNG merge — commit thẳng lên nhánh đang mở.
- Mode `worktree`: nhánh `agent/dev-<base>` là bắt buộc (git không cho checkout một nhánh ở hai worktree); lần đầu cắt từ base, các lần sau merge base vào trước khi implement (conflict → dừng pipeline, user resolve tay). User merge tay `agent/dev-<base> → base` sau khi chạy manual verify steps.
- `/run-backlog` **không** tạo PR ở cả hai mode, và **chỉ push khi repo có `origin`** — repo local-only vẫn chạy hết pipeline, chỉ bỏ bước push.

**Source of truth:** `.claude/` là canonical source (file thật, được track trong git); `.agents/` chỉ là link views trỏ ngược về `.claude/` (cho Codex/Gemini/Cline đọc) — gitignore, không track. Chỉ edit trong `.claude/`. Sau clone chạy `sync-to-agents` một lần để tạo link.

> ⚠️ **Bắt buộc chạy `sync-to-agents` sau khi clone / generate project mới.** Nếu `.agents/` là thư mục
> COPY thật (không phải symlink/junction) thì nó sẽ drift khỏi `.claude/` và agent đọc phải tài liệu cũ.
> Kiểm tra nhanh: `ls -la .agents` — mỗi entry phải là symlink (`->`) trên macOS/Linux, hoặc junction trên
> Windows. Nếu không phải, xoá `.agents/` rồi chạy lại script sync.
