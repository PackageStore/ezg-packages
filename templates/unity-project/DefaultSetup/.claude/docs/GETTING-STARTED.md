# Getting Started — agent system cho project mới

Tài liệu này dành cho lần đầu mở một project vừa sinh từ base template. Nó chỉ nói về **agent
system** (planning → backlog → run-backlog): những gì phải chạy một lần, những gì phải điền tay,
và cách biết mọi thứ đã đúng. Kiến trúc code/gameplay xem [CLAUDE.md](../../CLAUDE.md).

Thứ tự dưới đây là bắt buộc: bước 2 cần bước 1 đã xong.

---

## 1. Bootstrap (đã chạy sẵn nếu sinh bằng build script)

`build_unity_template` gọi bootstrap ngay sau khi copy `.claude/` vào project mới, nên **project vừa
generate đã xong bước này**. Kiểm tra nhanh: `ls -la .agents` thấy mũi tên `->`, và
`.git/backlog/BACKLOG.md` tồn tại.

Chạy tay khi **clone** project về máy khác (link view và backlog đều không đi theo git), hoặc để sửa
một checkout hỏng nửa vời:

```bash
bash .claude/scripts/bootstrap.sh                                          # macOS / Linux
```
```powershell
powershell -ExecutionPolicy Bypass -File .claude/scripts/bootstrap.ps1     # Windows
```

Bốn việc nó làm, và lý do không việc nào ship kèm được trong git:

| Bước | Tạo ra | Vì sao không nằm trong git |
|------|--------|-----------------------------|
| Link view | `.agents/{agents,rules,skills,workflows,scripts,docs,backlog-templates,ui-kit}` → `.claude/…` | symlink được track làm hỏng `git switch` trên Windows; link đi qua tarball thì biến thành **bản copy** rồi drift khỏi `.claude/` |
| Git repo | `.git/` nếu chưa có | project vừa generate chưa `git init` |
| Backlog | `.git/backlog/{planning,todo,in-progress,done}` + `BACKLOG.md` | bookkeeping riêng từng dev; để trong tree thì mỗi nhánh mang một index riêng và merge là đụng NNN |
| UI kit | `.claude/ui-kit/{ui-kit.json,ui-kit.css,kit-preview.html}` — chỉ sinh khi chưa có | trích từ prefab template của **chính project này** và mang hash của chúng; kit copy từ project khác mô tả sai game và `/ui-mockup` chặn ngay là `kit_stale` |

Chạy lại bất cứ lúc nào — đây cũng là cách sửa một checkout hỏng nửa vời (ai đó xoá `.agents/`,
hoặc `.agents/` biến thành thư mục thật).

**Kiểm tra link view đúng:** `ls -la .agents` phải thấy mũi tên `->` ở mọi entry (macOS/Linux),
hoặc `<JUNCTION>` trong `dir .agents` (Windows). Nếu là thư mục thật → xoá và chạy lại bootstrap;
nếu không, agent sẽ đọc tài liệu cũ đã drift.

**Codegraph index là bước riêng.** Bootstrap in cảnh báo `.codegraph/ index: chưa build` — đúng như
vậy, db bị gitignore nên project mới không thể có sẵn. Nó KHÔNG chặn bootstrap. Build khi cần:

```bash
codegraph init                                       # ở repo root
bash .claude/scripts/codegraph-doctor.sh --fix       # hoặc để doctor tự cài + index
```

---

## 2. Điền `.claude/project-profile.json`

File này là **chỗ duy nhất** mang giá trị riêng của project. Mọi skill/agent/script còn lại giữ
nguyên byte giữa các project — nhờ vậy template ra bản mới là update lại được, không phải fork.

Build script điền sẵn `projectName` / `solutionFile` / `gitConfigPrefix` lúc generate (`gitConfigPrefix`
= tên project viết thường, bỏ hết ký tự không phải chữ-số — git không cho section name có dấu cách).
Nếu bạn thấy `__PROJECT_NAME__` còn nguyên — project không sinh qua build script — bootstrap sẽ cảnh
báo và bạn điền tay.

Những key còn lại build script **cố ý không đoán**: layout code, threat surface và backend là quyết
định của project, không suy ra được từ cái tên. Bảng dưới là chỗ đọc trước khi giao task đầu tiên.

> Chạy lại build script trên project đã có **không** ghi đè file này (`.mcp.json` cũng vậy) — hai file
> đó được seed một lần rồi thuộc về project. Phần còn lại của `.claude/` thì có refresh.

| Key | Ý nghĩa | Sửa khi |
|-----|---------|---------|
| `projectName`, `solutionFile` | tên project + file `.sln` cho compile-check tier 2 | luôn (bootstrap cảnh báo nếu còn placeholder) |
| `gitConfigPrefix` | namespace git config (`<prefix>.agentBaseBranch`) | luôn |
| `defaultBaseBranch` | base cuối cùng khi HEAD đang ở nhánh agent và git config rỗng | nhánh chính không phải `main` |
| `sourceRoot`, `featuresRoot`, `gameplayRoot` | layout code; reviewer + task-planner đọc để biết file mới đi đâu | đổi cấu trúc thư mục |
| `uiTemplatesRoot` | nơi `ui-kit-sync.py` đọc prefab template màn hình | đổi chỗ để prefab template |
| `sensitiveGlobs` | glob filename khiến diff bị coi là nhạy cảm → tự spawn `security-auditor` | **cắt bớt** khi project chưa có backend/leaderboard, **thêm** khi mọc thêm bề mặt mới |
| `backend.kind`, `backend.directWriteBanned`, `backend.directWritePattern`, `backend.directWriteAdvice` | rule cấm client ghi thẳng datastore | khi có backend thật; base template để `kind: "none"` và tắt rule |

Key nào bỏ trống thì rơi về default trong `project_profile.py`. Xem giá trị đã merge:

```bash
python3 .claude/scripts/project_profile.py              # tất cả key
python3 .claude/scripts/project_profile.py sourceRoot   # một key
```

> `sensitiveGlobs` rộng quá thì **mọi** task đều kéo `security-auditor` vào review thứ nó không có
> gì để xem — tốn token và làm nhờn verdict. Cắt cho khớp bề mặt thật của project.

---

## 3. Remote: tùy chọn, không phải điều kiện

Chưa có `origin` vẫn chạy được đủ pipeline. `/run-backlog` dò remote một lần rồi bỏ qua mọi lệnh
network nếu không có: task vẫn implement, vẫn qua gate, vẫn commit — chỉ không push, và report ghi
`committed locally (no remote — push skipped)`.

Tạo remote lúc nào cũng được; không cần chạy lại bootstrap.

---

## 4. Kiểm tra nhanh trước khi giao việc thật

```bash
python3 .claude/scripts/backlog-ops.py lint                       # index ↔ directory nhất quán
python3 -m unittest discover -s .claude/scripts/tests             # regression suite của toolchain
```

Suite sẽ **skip** phần UI-kit khi project chưa export ui-kit (xem mục 5) — skip là đúng, không phải lỗi.

---

## 5. Vòng làm việc đầu tiên

```
/planning-task <mô tả việc cần làm>     # → .git/backlog/planning/<timestamp>-<TIER>-slug.md
/add-to-backlog                          # planning → todo (gán NNN, FIFO)
/run-backlog                             # làm task đầu TODO: implement → gates → DONE → commit
```

Hoặc chạy loop nhiều task liên tiếp:

```bash
bash .claude/scripts/run-backlog-loop.sh --auto-model-by-tier
```

**Task UI đọc ui-kit.** Pipeline mockup so UI dựng ra với một PNG contract sinh từ ui-kit của
**chính project này**. Bootstrap (bước 1) đã sinh kit nếu prefab template có sẵn; nếu chúng về sau
mới có, sinh tay:

```bash
python3 .claude/scripts/ui-kit-sync.py            # sinh kit
python3 .claude/scripts/ui-kit-sync.py --check    # exit 1 = kit đã lệch prefab
```

**Chạy lại mỗi khi thêm/sửa/đổi tên prefab template trong `uiTemplatesRoot`** — kit không có
watcher, và kit stale hỏng cả `/ui-mockup` lẫn `/new-ui` **trong im lặng** (test UI skip chứ không
fail). Preflight của `/run-backlog` có rule `ui-kit-stale` bắt commit sửa prefab mà quên regenerate.
Luật ghép template mà prefab không tự nói được (vd tab toggle phải nằm trong tab bar) viết vào
`.claude/ui-kit/ui-kit-usage.json`. Vòng đời đầy đủ: skill [ui-kit](../skills/ui-kit/SKILL.md).

---

## 6. Gặp lỗi

| Triệu chứng | Nguyên nhân | Sửa |
|-------------|-------------|-----|
| `backlog not initialised` (exit 3) | chưa bootstrap trên máy/clone này | chạy lệnh in trong field `fix` của JSON, hoặc bootstrap lại |
| `Agent type 'code-reviewer' not found`, hoặc run-backlog dừng ở `GATE_RECEIPT_MISSING` | link view chưa tạo → Claude không thấy `.claude/agents` | bootstrap lại |
| `.agents/` là thư mục thật, nội dung cũ | tarball/copy đã dereference link | xoá `.agents/` rồi bootstrap lại |
| Windows: script Python im lặng không làm gì, exit 9009 | `python3` trỏ vào Microsoft Store stub | dùng `py`, hoặc tắt App execution alias cho python/python3 |
| Task UI bị `defer` liên tục | task có `**Requires:** unity-editor` mà Editor không sống | mở Unity Editor rồi chạy lại loop ở mode `current` |
| Toàn bộ TODO đều cần Editor → `EDITOR_REQUIRED` | như trên, nhưng không còn task headless nào | mở Editor, xoá `$BACKLOG_ROOT/state`, chạy lại |
