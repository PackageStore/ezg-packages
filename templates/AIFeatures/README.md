# AI Features — catalog cho tab "AI Feature" của Feature Hub

Thư mục này phục vụ **tab AI Feature** trong EZG Feature Hub: nơi project đang chạy cài lẻ từng
skill / command / agent / rule… của Claude, thay vì chỉ nhận trọn `.claude/` một lần lúc tạo project
qua `defaultsetup.tgz`.

Publish bằng script `scripts/upload-unity-template-ai.mjs` (xem skill `publish-ai-features`).

## Hai nguồn nội dung

| Nguồn | Dùng khi | Đích cài trong project |
|---|---|---|
| `templates/unity-project/DefaultSetup/` | item **thuộc bộ mặc định** (đa số) — script tự quét theo `KIND_MAP` | đúng path gốc, vd `.claude/skills/ui-kit` |
| `templates/AIFeatures/<Category>/<item>` | item **ngoài** bộ mặc định (thử nghiệm, chuyên biệt, hoặc cố ý override) | suy ra từ category |

Item trùng `<Category>/<name>` ở cả hai nơi → bản trong `AIFeatures/` **thắng**. Đó là mục đích của
thư mục override: sửa/thay một item mà không đụng vào bộ DefaultSetup đang ship.

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
| `Config` | `.claude/settings.json`, `.claude/project-profile.json`, `.mcp.json`, `CLAUDE.md` | đúng path gốc | 1 file |

Category lạ đặt trong `AIFeatures/` (không có trong bảng) → đích mặc định `.claude/<category viết thường>/<item>`.

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
  "overrides": {
    "Skills/ui-kit": {
      "description": "…",
      "installedByDefault": true
    }
  }
}
```

- `exclude` — bỏ item khỏi catalog. Nhận `"Category/name"` hoặc `"Category/*"`.
- `overrides` — ghi đè `description` / `installedByDefault` cho từng item. `description` mặc định lấy
  từ frontmatter `description:` của `SKILL.md` / file `.md`; item không có frontmatter thì để trống.
- `installedByDefault: true` → item nằm trong nhóm "Cài tất cả AI feature còn thiếu" chạy mặc định.

## Layout trên R2

```
unity-template/ai/index.json                          ← catalog (categories + items)
unity-template/ai/files/<Category>/<name>.zip         ← payload từng item
```

Đọc qua gateway: `https://upm-registry-worker.developer-a1f.workers.dev/template/ai/index.json`.

`index.json` bản local được script ghi lại ở `templates/AIFeatures/index.json` để soi và commit.
