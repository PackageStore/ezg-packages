---
name: push-in-session
description: Commit and push ONLY the files changed during this session, with a prefix + tag commit message
---

// turbo-all
# Push In Session

Commit **chỉ những gì chính session này thay đổi**, rồi push. Mọi file dirty khác — dev sửa
tay song song, Unity tự dirty (scene/ScriptableObject bị editor tool sync lại sau `Assets/Refresh`),
rác `Library/` — **không được đụng vào**.

> **Khác gì `/push`:**
>
> | | `/push` | `/push-in-session` |
> |---|---|---|
> | Stage | `git add .` — cả working tree | `git add -- <đúng path của session>` |
> | Diff đọc lại | 80 dòng đầu | **không đọc** (agent đã biết mình sửa gì) |
> | Message | `<prefix> <subject> <suffix>` | `<prefix> [Tag] <subject>` — đúng 1 tag |

> **Cross-platform:** chọn lệnh theo OS.
> - **Windows:** `powershell -ExecutionPolicy Bypass -File .claude/scripts/<name>.ps1`
> - **macOS / Linux:** `bash .claude/scripts/<name>.sh`
>
> Hai bản `.ps1` / `.sh` giữ hành vi y hệt nhau.

## 1. DỰNG DANH SÁCH FILE CỦA SESSION (không tốn thêm token)

Danh sách này lấy **từ chính transcript đang nằm trong context** — KHÔNG chạy `git diff`,
KHÔNG `git status` dò tìm, KHÔNG đọc lại file để "xem mình đã sửa gì". Đó là toàn bộ lý do
skill này tồn tại: agent đã biết sẵn, mọi lệnh dò lại là token đốt vô ích.

**Đưa vào danh sách** — file mà trong session này bạn đã:
- tạo/sửa bằng `Write` / `Edit` / `NotebookEdit`;
- ghi bằng `Bash` (heredoc, `sed -i`, `cp`, `mv`, script Python…);
- **sinh ra gián tiếp do một lệnh bạn cố ý chạy** — ví dụ CSV importer regenerate
  `DataManager.Generated.cs` + `.asset`, `ui-kit-sync.py` regenerate kit, `/add-localize`
  ghi `LocalizationData/<lang>/<tab>.csv` + `.asset`. File `.meta` đi kèm asset mới cũng tính.

**Loại khỏi danh sách:**
- file chỉ `Read` / `Grep` / `codegraph_explore` mà không sửa;
- file đã dirty từ trước khi session bắt đầu (việc đang làm dở của dev);
- file Unity tự dirty sau `Assets/Refresh` mà không phải mục tiêu của task — kinh điển là scene
  và ScriptableObject bị editor tool tự sync lại (project này: xem CLAUDE.md § Gotchas). Chỉ
  commit chúng khi task đúng là sửa scene/SO đó;
- artefact tạm trong scratchpad, `Library/`, `Temp/`, log.

Không chắc một file có thuộc session hay không → **để ngoài** và nêu trong report cuối, đừng
commit đại.

Danh sách rỗng (session chỉ đọc/phân tích) → dừng, báo `NO_CHANGES`, không chạy script nào.

## 2. STAGE

Chạy script **scoped**, truyền từng path thành một argument riêng (quote path có khoảng trắng):

- Windows: `powershell -ExecutionPolicy Bypass -File .claude/scripts/git_prepare_scoped.ps1 "<path1>" "<path2>"`
- macOS / Linux: `bash .claude/scripts/git_prepare_scoped.sh "<path1>" "<path2>"`

Đọc output:
- `NO_CHANGES` → dừng, báo user (không có path nào thật sự dirty).
- `--- SKIPPED (not found) ---` → path không tồn tại; kiểm lại có gõ sai không, nêu trong report.
- `--- DIRTY OUTSIDE SESSION (n) ---` → **đúng như mong đợi**, đây là phần cố tình không commit.
  Liệt kê lại cho user ở report cuối để họ tự quyết.
- `--- REMOTE ---` → `none` nghĩa là repo chưa có `origin`: vẫn commit, **bỏ bước push**.

## 3. SOẠN MESSAGE

Format bắt buộc — một dòng subject duy nhất:

```
<prefix> [Tag] <subject>
```

**Đúng MỘT tag, không bao giờ hai.** `[Bug][UI]` là sai format.

### 3.1 Prefix (bắt buộc)

| Prefix | Dùng cho |
|---|---|
| `+` | thêm mới (feature, màn hình, asset, config, key mới) |
| `*` | chỉnh sửa / cải tiến thứ đã có |
| `#` | fix bug |

### 3.2 Tag (bắt buộc, đúng MỘT)

Một danh sách phẳng — chọn **đúng một** tag mô tả phần việc **chiếm tỷ trọng cao nhất** trong
commit (đo bằng lượng diff + trọng tâm của task, không phải số file).

**Nhóm loại việc:**

| Tag | Nghĩa | Tag | Nghĩa |
|---|---|---|---|
| `Feat` | Feature (xây mới) | `Perf` | Perf |
| `Enh` | Enhancement | `Clean` | Cleanup |
| `Bug` | Bug | `Pol` | Polish |
| `Ref` | Refactor | `Sec` | Security |
| `Bal` | Balance (số liệu CSV) | `Cont` | Content |

**Nhóm vùng:**

| Tag | Nghĩa | Tag | Nghĩa |
|---|---|---|---|
| `UI` | UI | `Loc` | Localize |
| `Play` | Gameplay | `Track` | Analytics |
| `Meta` | Meta | `Ads` | Ads |
| `Mon` | Monetization | `Bundle` | AssetBundle |
| `Save` | Data/Save | `Editor` | Editor Tool |
| `BE` | Backend | `CI` | Build/CI |
| `Audio` | Audio | `AI` | AI agent tooling |

> **`AI` = hạ tầng cho AI agent** (Claude Code / Codex / Gemini…): mọi thứ trong `.claude/` —
> skill, command, agent, rule, script backlog, hook, MCP config — và `CLAUDE.md`/`.agents/`.
> **KHÔNG phải** AI logic trong game (enemy AI, pathfinding, behaviour tree): thứ đó là `Play`.

**Chọn tag nào khi cả hai nhóm đều đúng** — tránh tag nói lại thứ prefix đã nói:

1. Tag loại việc **không suy ra được từ prefix** thì nó thắng: `Bal`, `Perf`, `Ref`, `Clean`,
   `Pol`, `Sec`, `Cont`. Ví dụ `* [Bal] raise station upgrade cost` — `Bal` mang thông tin
   thật, `Gameplay` thì prefix `*` không nói được nhưng cũng không quan trọng bằng.
2. Còn lại — `Feat` ≈ `+`, `Bug` ≈ `#`, `Enh` ≈ `*` — là **dư thừa với prefix**, nên nhường
   cho tag vùng: `# [UI] fix shop pack refresh`, không phải `# [Bug] ...`.
3. Diff trải nhiều vùng, không vùng nào trội, và cũng không rơi vào (1) → dùng tag loại việc
   (`Feat` / `Bug` / `Enh`).

**Ràng buộc prefix ↔ tag loại việc** (chỉ áp dụng khi tag chọn được thuộc nhóm loại việc;
lệch thì sửa prefix cho khớp tag):

| Prefix | Tag loại việc hợp lệ |
|---|---|
| `+` | `Feat`, `Cont` |
| `*` | `Enh`, `Ref`, `Bal`, `Perf`, `Clean`, `Pol` |
| `#` | `Bug`, `Sec` |

### 3.3 Subject

- **Tiếng Anh**, imperative, **≤50 ký tự** (không tính prefix + tag); cả dòng ≤72.
- Tả *cái gì đổi*, không tả file: `fix shop pack refresh`, không phải
  `update ShopController.cs and MoneyBarView.cs`.
- Không `feat:` / `fix:` / `refactor:` — prefix + tag đã làm việc đó rồi.

### 3.4 Description (body) — tuỳ chọn

Chỉ thêm khi task thật sự có điều phải lưu ý (breaking change, phải re-import CSV, phải chạy
lại `codegraph init`, giá trị tạm chờ design chốt…). Tối đa **2 dòng tiếng Anh**, cách subject
một dòng trống. **Không** `Co-Authored-By`, không trailer, không liệt kê file — đây là luật
của project, cố ý ngược với attribution mặc định của Claude Code.

### 3.5 Session lẫn nhiều loại việc → vẫn MỘT commit, vẫn MỘT tag

Không tách commit, không nhét thêm tag thứ hai. Chọn prefix + tag theo **phần việc chiếm tỷ
trọng cao nhất**, subject mô tả phần đó; phần phụ (nếu đáng nhắc) cho xuống body một dòng.

### 3.6 Dev override

Mọi text dev gõ thêm quanh lệnh đều là chỉ thị, không phải rác — đọc kỹ prompt gốc:

- Ký tự `*` / `#` / `+` → **prefix**, thắng suy đoán của agent.
- Text trong ngoặc vuông → **tag**, thắng suy đoán của agent (`/push-in-session # [UI]`).
  Tag không nằm trong 2 bảng trên → **dừng và hỏi**, tuyệt đối không tự chế tag mới.
- Dev gõ nhiều hơn một tag → dùng **tag đầu tiên**, bỏ phần còn lại và nói rõ trong report
  (format chỉ cho phép một tag).
- Ví dụ: `+ /push-in-session [Mon]` → agent chỉ sinh phần subject.

### 3.7 Ví dụ

```
+ [Play] add staff gacha pity counter
* [Audio] loop rotor SFX while flying
# [UI] fix shop pack list not refreshing
* [Bal] raise station upgrade cost curve
# [Sec] validate coin delta before save
+ [Cont] add 3 tutorial strings
* [AI] tighten run-backlog review gating
* [Perf] pool damage popups
+ [Feat] add offline earning screen
```

## 4. COMMIT + PUSH

Kiểm lại `[Final Message]` một lần: prefix/tag dev gõ có nằm nguyên văn trong đó không.

- Có `origin` (output `--- REMOTE ---` là `origin`):
  - Windows: `powershell -ExecutionPolicy Bypass -File .claude/scripts/git_push.ps1 "[Final Message]"`
  - macOS / Linux: `bash .claude/scripts/git_push.sh "[Final Message]"`
- Không có `origin`: chỉ `git commit -m "[Final Message]"`, không push, báo rõ cho user.

Push lỗi vì nhánh tụt hậu → **không** tự `--force`, không tự rebase; báo lỗi nguyên văn cho dev.

## 5. REPORT

Báo gọn, tiếng Việt:
1. Message cuối cùng.
2. Danh sách file đã commit.
3. File dirty **không** commit (từ `--- DIRTY OUTSIDE SESSION ---`) — để dev tự xử.
4. Trạng thái push (đã push / bỏ qua vì không có remote / lỗi).

## Ngân sách lệnh

Đúng **2 lệnh git** cho cả quy trình: `git_prepare_scoped` rồi `git_push`. Thêm `git status`,
`git diff`, `git log` để "kiểm tra cho chắc" là vi phạm mục đích của skill.

## Được `/run-backlog` dùng lại

STEP 9 của [`run-backlog`](../run-backlog/SKILL.md) commit + push theo đúng §1–§4 của skill này
(9a–9e), nên **sửa format message / luật chọn file ở đây là đổi luôn cách loop commit** — giữ
hai file khớp nhau. Khác biệt duy nhất của bản loop: index đã bị `git add -A` cho review từ
STEP 5, nên nó chạy thêm `git reset -q` **trước** bước 2 (`git_prepare_scoped` chỉ stage thêm,
không bỏ stage); push lỗi thì in `manual intervention required …` để dừng loop.
