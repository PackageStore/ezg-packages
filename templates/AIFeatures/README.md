# AI Features — catalog cho tab "AI Feature" của Feature Hub

Thư mục này phục vụ **tab AI Feature** trong EZG Feature Hub: nơi project đang chạy cài lẻ từng
skill / command / agent / rule… của Claude, thay vì chỉ nhận trọn `.claude/` một lần lúc tạo project
qua `defaultsetup.tgz`.

Publish: **push lên `main`** là xong — workflow `.github/workflows/publish-ai.yml` chạy
`scripts/upload-unity-template-ai.mjs` bằng R2 secrets của repo, nên người đóng góp chỉ cần quyền
push, không cần R2 credentials. Xem skill `publish-ai-features`. Chạy tay vẫn được (cần `scripts/.env`).

## Ba nguồn nội dung

| Nguồn | Dùng khi | Đích cài trong project |
|---|---|---|
| `templates/unity-project/DefaultSetup/` | item **thuộc bộ mặc định** (đa số) — script tự quét theo `KIND_MAP` | đúng path gốc, vd `.claude/skills/ui-kit` |
| `templates/AIFeatures/<Category>/<item>` | item **ngoài** bộ mặc định (thử nghiệm, chuyên biệt, hoặc cố ý override). Hiện **trống**: 7 skill từng ở đây (auto-clear, blender-rig-animate, game-animation, game-vfx, refactor-ui, restart-unity, ui-motion) đã chuyển vào DefaultSetup ngày 2026-10-06 | suy ra từ category |
| `ai-manifest.json` → `sources` | thứ **đã nằm sẵn chỗ khác trong repo**, publish tại chỗ | suy ra từ category |

Trùng `<Category>/<name>` thì bản sau thắng theo thứ tự **DefaultSetup → AIFeatures → `sources`**.
Đó là mục đích của thư mục override: sửa/thay một item mà không đụng vào bộ DefaultSetup đang ship.

## Category → đích cài

| Category | Nguồn trong DefaultSetup | Đích | Đơn vị 1 item |
|---|---|---|---|
| `Skills` | `.claude/skills/` | `.claude/skills/<name>` | 1 thư mục skill |
| `Commands` | `.claude/commands/` | `.claude/commands/<name>.md` | 1 file `.md` |
| `Agents` | `.claude/agents/` | `.claude/agents/<name>.md` | 1 file `.md` |
| `Rules` | `.claude/rules/` | `.claude/rules/<name>.md` | 1 file `.md` |
| `Docs` | `.claude/docs/` | `.claude/docs/<name>` | file hoặc thư mục con |
| `Scripts` | `.claude/scripts/` | `.claude/scripts/<name>` | file hoặc thư mục con |
| `Harness` | `.claude/harness/` | `.claude/harness/<name>` | file hoặc thư mục con |
| `Templates` | `.claude/backlog-templates/` | `.claude/backlog-templates/<name>` | file hoặc thư mục con |
| `UiKit` | `.claude/ui-kit/` | `.claude/ui-kit/<name>` | file hoặc thư mục con |
| `Config` | `.claude/settings.json`, `.mcp.json`, `CLAUDE.md` | đúng path gốc | 1 file |

Category lạ đặt trong `AIFeatures/` (không có trong bảng) → đích mặc định `.claude/<category viết thường>/<item>`.

> `CLAUDE.md` (Config) là phần **dùng chung** — template quản lý, project không sửa, cài là ghi đè. Phần
> riêng của từng game nằm ở `CLAUDE_PROJECT.md` (CLAUDE.md import nó), file này **không bao giờ** có trong
> catalog: script publish báo lỗi nếu gặp. Catalog chỉ có khung `Docs/CLAUDE_PROJECT.template`, bootstrap
> copy thành `CLAUDE_PROJECT.md` khi project chưa có.

> `.agents/` **không** nằm trong catalog: nó chỉ là link view trỏ về `.claude/` do
> `bootstrap.sh` / `bootstrap.ps1` tạo trong project. Cài vào `.claude/` là `.agents/` thấy ngay.

## Đóng gói

Mỗi item = **1 file `.zip`**, entry bên trong là path **tương đối project root**
(vd `.claude/skills/ui-kit/SKILL.md`). Feature Hub chỉ việc verify SHA-256 rồi ghi từng entry
xuống project root — không phải suy diễn đường dẫn.

Zip được sinh **tất định** (timestamp cố định, deflate mức 9): item không đổi nội dung thì sha256
không đổi, nên Feature Hub chỉ báo "Có bản mới" khi nội dung thật sự đổi.

## `ai-manifest.json` (tùy chọn)

```json
{
  "exclude": ["Scripts/tests", "Docs/*"],
  "sources": {
    "Skills/publish-ai-features": ".agents/skills/publish-ai-features"
  },
  "overrides": {
    "Skills/ui-kit": {
      "description": "…",
      "installedByDefault": true
    }
  }
}
```

- `sources` — publish một thứ đã nằm chỗ khác trong repo mà **không copy** vào `AIFeatures/`:
  map `"Category/name"` → path tương đối repo root. Copy thứ hai sẽ lệch ngay lần đầu có người sửa
  một trong hai bản, nên khi nguồn đã tồn tại ở chỗ khác thì luôn dùng `sources`.
  Ví dụ đang dùng: `"Skills/publish-ai-features": ".agents/skills/publish-ai-features"` — đưa chính
  skill publish (skill cấp maintainer, **cố ý không nằm trong DefaultSetup**) lên catalog để cài về
  xem/dùng, trong khi file gốc vẫn chỉ có một bản.
- `exclude` — bỏ item khỏi catalog. Nhận `"Category/name"` hoặc `"Category/*"`.
- `overrides` — ghi đè `description` / `installedByDefault` cho từng item. `description` mặc định lấy
  từ frontmatter `description:` của `SKILL.md` / file `.md`; item không có frontmatter thì để trống.
- `installedByDefault` — chỉ là metadata: Feature Hub hiện **không** đọc field này cho AI item (nút "Cài N mục…"
  cài mọi item còn thiếu đang hiển thị). Muốn một item có sẵn trong project mới thì đặt nó vào
  `templates/unity-project/DefaultSetup/` (`defaultsetup.tgz`), không phải bật cờ này.

## Quyền sở hữu file (rule `base-ownership`)

| Loại | Gồm | Khi project cập nhật (`/update-ai`, Feature Hub) |
|---|---|---|
| **base** | mọi file của catalog trừ 2 file dưới | thay bằng bản mới — **chỉ khi project chưa sửa**; file đã sửa được giữ, `/update-ai` chuyển phần sửa sang override rồi mới thay |
| **merge** | `Config/settings.json`, `Config/.mcp.json` | gộp JSON 3 chiều, giữ key/hook/MCP server project thêm (`ownership: "merge"` trong index) |
| **project** | `CLAUDE_PROJECT.md`, `.claude/project/**`, item `local-*`, `ArtStyle.md`, `QaProfile.md`, `project-profile.json`, lock | không bao giờ ship, không bao giờ đụng — script publish **báo lỗi** nếu gặp trong catalog |

`project-profile.json` đã rời catalog (2026-10): builder seed một lần, sau đó project sở hữu; key mới
đến qua `project_profile.DEFAULTS`.

## Layout trên R2

```
unity-template/ai/index.json                          ← catalog (categories + items; mỗi item có
                                                         fileHashes {path: nsha} + ownership)
unity-template/ai/files/<Category>/<name>.zip         ← payload từng item
unity-template/ai/history.json                        ← { files: { path: [nsha, ...] } } — mọi bản
                                                         từng publish (gồm cả lịch sử git)
unity-template/ai/blobs/<nsha>                        ← nội dung từng phiên bản, đánh địa chỉ theo hash
```

`nsha` = sha256 của nội dung, file text thì gộp CRLF → LF (không có byte NUL trong 8000 byte đầu) —
cùng một định nghĩa ở `scripts/ai-catalog-lib.mjs`, `ezg_base.py` (skill update-ai) và `EzgBaseLock.cs`
(Feature Hub). `history.json` cho biết file trên đĩa của một project là "một bản base cũ" (cập nhật an
toàn) hay "project đã sửa"; `blobs/` cho `/update-ai` lấy đúng bản gốc để diff 3 chiều. Script publish
lấy lịch sử từ git nên CI phải checkout `fetch-depth: 0`. `defaultsetup.tgz` mang sẵn
`.claude/ezg-base.lock.json` (sinh lúc publish, không commit) nên project mới được bảo vệ ngay.

Thử offline: `node upload-unity-template-ai.mjs --local-out <dir>` rồi
`python3 .claude/skills/update-ai/scripts/ezg_base.py --source <dir> status` trong một project.

Đọc qua gateway: `https://upm-registry-worker.developer-a1f.workers.dev/template/ai/index.json`.

`index.json` bản local được script ghi lại ở `templates/AIFeatures/index.json` để soi và commit.
