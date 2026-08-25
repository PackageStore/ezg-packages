---
name: publish-ai-features
description: Đưa một tài sản AI của Claude (skill, command, agent, rule, docs, script, MCP config...) lên server dùng chung — copy vào monorepo ezg-packages rồi commit + push main, GitHub Actions (publish-ai.yml) tự publish lên Cloudflare R2 cho tab "AI Feature" của EZG Feature Hub và cho defaultsetup.tgz. Chỉ cần quyền push vào PackageStore/ezg-packages, KHÔNG cần R2 credentials; nếu máy chưa có SSH thì tự gọi skill setup-package-push inline. Dùng khi user nói "đẩy skill X lên server", "publish AI skill", "cập nhật AI feature", "deploy AI features", "chia sẻ command này cho các project khác", hoặc sau khi sửa file trong .claude/ của DefaultSetup.
---

# Publish AI Features — đưa skill/command lên server dùng chung

Đưa **từng** tài sản AI của Claude lên R2 để mọi project cài lẻ qua tab **AI Feature** của Feature Hub.

**Cơ chế: push là deploy.** Bạn không upload gì cả — copy file vào monorepo `ezg-packages`, commit,
push `main`. GitHub Actions (`.github/workflows/publish-ai.yml`) chạy `upload-unity-template-ai.mjs`
+ `upload-unity-template-defaultsetup.mjs` bằng R2 secrets của repo. Giống hệt cách `/package-module`
publish UPM package.

> **Cần đúng một thứ: quyền push vào `PackageStore/ezg-packages`.**
> Không cần tài khoản Cloudflare, không cần `scripts/.env`, không cần R2 key trên máy. Máy chưa có
> SSH thì STEP 2 tự xử lý.

Hai kênh phân phối được publish cùng lúc, và đó là lý do phải chọn đích ở STEP 1:
- `ai/index.json` + `ai/files/**` → tab **AI Feature**, cho project **đang tồn tại** cài lẻ.
- `defaultsetup.tgz` → builder, cho project **sinh mới từ giờ trở đi**.

---

## Pipeline

```
[0] RESOLVE  → tìm/clone monorepo ezg-packages, xác định item cần đẩy
[1] TARGET   → chọn đích: DefaultSetup (bộ mặc định) hay AIFeatures (chỉ cài lẻ) hay sources
[2] AUTH     → ssh -T git@github.com; hỏng thì gọi setup-package-push INLINE
[3] COPY     → đặt file vào đúng chỗ trong monorepo
[4] PUSH     → commit + push main  → CI publish
[5] VERIFY   → theo dõi run, đối chiếu catalog live
```

---

## ⚙️ Configuration

```
MONOREPO_REMOTE  = git@github.com:PackageStore/ezg-packages.git   (SSH; HTTPS chỉ khi dùng PAT)
MONOREPO_PATH    = env MONOREPO_PATH, mặc định theo OS:
                   Windows:      %USERPROFILE%\ezg-packages
                   macOS/Linux:  $HOME/ezg-packages
                   # Đã có clone ở đây thì dùng tại chỗ; chỉ clone khi thiếu.
ITEM             = thứ cần đẩy (thư mục skill / file command / agent / rule...)
CATEGORY         = Skills | Commands | Agents | Rules | Docs | Scripts | Harness | Templates | UiKit | Config
```

---

## STEP 0 — Resolve monorepo + item

Hai tình huống:

**a) Đang đứng trong clone `ezg-packages`** (có `packages/` + `templates/unity-project/`): dùng luôn
repo hiện tại, `MONOREPO_PATH` = repo root. Item thường là file bạn vừa sửa —
`git status --short templates/unity-project/DefaultSetup/.claude/ templates/AIFeatures/ .agents/skills/`.

**b) Đang ở project game** (skill này được cài về qua Feature Hub): item nằm ở
`.claude/skills/<name>/` của project. Cần một clone monorepo để commit vào:

```bash
# macOS/Linux
[ -d "$MONOREPO_PATH/.git" ] || git clone git@github.com:PackageStore/ezg-packages.git "$MONOREPO_PATH"
git -C "$MONOREPO_PATH" checkout main && git -C "$MONOREPO_PATH" pull --rebase
```

Clone bằng SSH mà lỗi → làm STEP 2 trước rồi quay lại.

---

## STEP 1 — Chọn đích

| Đích | Đặt ở đâu trong monorepo | Ai nhận được |
|---|---|---|
| **DefaultSetup** — thứ mọi project nên có | `templates/unity-project/DefaultSetup/.claude/<loại>/<name>` | project **mới** có sẵn; project **cũ** cài qua tab AI Feature |
| **AIFeatures** — chuyên biệt / thử nghiệm | `templates/AIFeatures/<Category>/<name>` | chỉ ai chủ động cài qua tab AI Feature |
| **sources** — thứ đã nằm sẵn chỗ khác trong repo | khai trong `templates/AIFeatures/ai-manifest.json` → `sources` | như trên, **không copy file** |

Không chắc thì hỏi user một câu: *"skill này nên có sẵn ở mọi project mới, hay chỉ ai cần thì cài?"*

Quy tắc đặt tên + bảng category → đích cài: xem
[templates/AIFeatures/README.md](../../../templates/AIFeatures/README.md).

> **Đừng bao giờ copy một thứ đã tồn tại chỗ khác trong repo** vào `AIFeatures/` — dùng `sources`.
> Hai bản sẽ lệch nhau ngay lần đầu có người sửa một trong hai.

---

## STEP 2 — Git auth (tự động, không bắt user đi làm riêng)

Kiểm tra trước:

```bash
ssh -o BatchMode=yes -o ConnectTimeout=5 -T git@github.com 2>&1 | grep -qi 'successfully authenticated' \
  && echo SSH_OK || echo SSH_MISSING
```

- `SSH_OK` → sang STEP 3.
- `SSH_MISSING` → **gọi skill `setup-package-push` INLINE ngay trong lượt chạy này**, đừng dừng lại
  bảo user tự đi chạy. Nó cài `gh` nếu thiếu, chạy `gh auth login --git-protocol ssh --web`, sinh +
  đăng ký SSH key, rồi verify lại. Có đúng **một bước cần người**: duyệt code trên trình duyệt.
  - Tìm file skill đó theo thứ tự: `.claude/skills/setup-package-push/SKILL.md` (khi đang ở project
    game) → `templates/unity-project/DefaultSetup/.claude/skills/setup-package-push/SKILL.md` (khi
    đang ở clone monorepo — nó nằm sẵn trong repo này). **Không tìm thấy** → làm thẳng phần lõi:
    ```bash
    gh auth login --git-protocol ssh --web     # user duyệt code trên browser
    ssh-keygen -t ed25519 -C "<email>"         # chỉ khi máy chưa có key
    gh ssh-key add ~/.ssh/id_ed25519.pub
    ssh -T git@github.com                      # verify lại
    ```
  - Trong Claude Code, `gh auth login --web` chặn tool call chờ browser → nhờ user gõ lệnh đó ở
    terminal của họ (hoặc dán sau `!` trong prompt), xong thì tiếp tục.
- Vẫn hỏng sau khi onboarding → fallback PAT: `EZG_PACKAGES_PAT` + remote HTTPS. Báo user rõ vì sao
  phải fallback.

Cuối cùng xác nhận quyền ghi (skill trên chỉ *chứng minh* được quyền, không *cấp* được):

```bash
gh api repos/PackageStore/ezg-packages --jq '.permissions'
```

`push: true` là đủ. `false` → user chưa được cấp quyền; báo admin repo, dừng ở đây.

---

## STEP 3 — Copy vào monorepo

Ví dụ đẩy skill `psd-to-feature` từ project game thành **bộ mặc định**:

```bash
rsync -a --delete \
  ".claude/skills/psd-to-feature/" \
  "$MONOREPO_PATH/templates/unity-project/DefaultSetup/.claude/skills/psd-to-feature/"
```

Nếu chỉ muốn cài lẻ (không vào bộ mặc định) thì đích là
`$MONOREPO_PATH/templates/AIFeatures/Skills/psd-to-feature/`.

Rà lại trước khi commit: bỏ `.DS_Store`, `settings.local.json`, path tuyệt đối của máy, secret,
tên project riêng. Thứ này sẽ chạy trên máy người khác.

---

## STEP 4 — Commit + push (đây là bước deploy)

```bash
cd "$MONOREPO_PATH"
git checkout main
git pull --rebase                      # CI có commit ngược index.json, kéo trước cho khỏi lệch
git add -A templates/ .agents/
git status --short                     # show cho user trước khi commit
git commit -m "feat(ai): add psd-to-feature skill"
git push origin main
```

Push xong là xong phần của bạn. Workflow `publish-ai.yml` trigger khi push chạm vào
`templates/unity-project/DefaultSetup/**`, `templates/AIFeatures/**`, `.agents/skills/**`, hoặc 2
script publish — nó sẽ:
1. upload payload `.zip` của **những item đổi nội dung** (so sha256 với catalog live, không đụng phần còn lại),
2. sinh lại + upload `ai/index.json`,
3. đóng gói + upload `defaultsetup.tgz` (+ `.sha256`),
4. commit ngược `templates/AIFeatures/index.json` vào `main`.

---

## STEP 5 — Verify

```bash
gh run list  -R PackageStore/ezg-packages --workflow publish-ai.yml --limit 3
gh run watch -R PackageStore/ezg-packages          # theo dõi run vừa tạo
```

**Run xanh là đã publish xong** — bước dưới chỉ để đối chiếu thêm, và cần máy đã đăng nhập Feature Hub
một lần (`Ezg > Đăng nhập EZG` trong Unity, hoặc `./build_unity_template.sh --login`). Chưa đăng nhập
thì bỏ qua, không phải lỗi:

```bash
TOKEN=$(python3 -c "import json,os;print(json.load(open(os.path.expanduser('~/.ezg/credentials.json')))['access_token'])")
curl -fsSL -H "Authorization: Bearer $TOKEN" \
  https://upm-registry-worker.developer-a1f.workers.dev/template/ai/index.json \
  | python3 -c "import json,sys;d=json.load(sys.stdin);print([i['id'] for i in d['items'] if 'psd-to-feature' in i['id']])"
```

Báo user: mở `Ezg > Feature Hub > AI Feature`, bấm ↻ là thấy item mới (hoặc trạng thái **"Có bản mới"**
nếu là cập nhật).

---

## Phụ lục — publish tay (chỉ maintainer có `scripts/.env`)

Cần thấy trước cái gì sẽ đổi, hoặc cần publish gấp không chờ CI:

```bash
cd "$MONOREPO_PATH/scripts"
node --env-file=.env upload-unity-template-ai.mjs --dry-run             # so với catalog live, chỉ in item đổi
node --env-file=.env upload-unity-template-ai.mjs                       # upload thật
node --env-file=.env upload-unity-template-ai.mjs --item Skills/ui-kit  # 1 item
node --env-file=.env upload-unity-template-defaultsetup.mjs             # tarball cho project sinh mới
```

Cờ: `--dry-run`, `--category <Id>`, `--item <Cat/name>`, `--skip-files`, `--force`.
`--force` chỉ cần khi có ai xoá tay object trên R2 — bình thường script tự so sha256 với catalog live
nên item đổi nội dung luôn được đẩy lại, item không đổi thì bỏ qua.

**Publish tay xong vẫn phải commit + push** `templates/AIFeatures/index.json`: index luôn được sinh
lại từ những gì có trên đĩa, nên lần publish sau từ máy khác (hoặc từ CI) sẽ **xoá item của bạn khỏi
catalog** nếu repo không có nó.

---

## Troubleshooting

- **`Permission denied (publickey)`** — máy chưa có SSH key đăng ký với GitHub. Quyền admin trên repo
  KHÔNG tự sinh key: làm STEP 2.
- **`push: false` từ `gh api`** — chưa được cấp quyền ghi. Nhờ admin repo add collaborator.
- **Push bị từ chối (non-fast-forward)** — CI vừa commit `index.json`. `git pull --rebase` rồi push lại.
- **Workflow không chạy** — thay đổi nằm ngoài `paths` của `publish-ai.yml` (vd chỉ sửa `scripts/`
  khác, hay `packages/`). Chạy tay: `gh workflow run publish-ai.yml -R PackageStore/ezg-packages`.
- **Run đỏ ở bước upload** — thường là secret repo bị xoay/thiếu (`R2_*`). Kiểm tra
  `gh secret list -R PackageStore/ezg-packages`.
- **Item sửa rồi mà Feature Hub vẫn báo "Đã cài"** — bấm ↻ trong Feature Hub để tải lại catalog;
  trạng thái so theo sha256 nên nội dung đổi thật là sẽ thành "Có bản mới".
- **`Missing env var R2_...`** — chỉ xảy ra ở phụ lục publish tay; luồng chính không cần `.env`.
