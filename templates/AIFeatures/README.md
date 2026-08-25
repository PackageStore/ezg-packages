# AI Features — catalog cho tab "AI Feature" của Feature Hub

Thư mục này phục vụ **tab AI Feature** trong EZG Feature Hub: nơi project đang chạy cài lẻ từng
skill / command / agent / rule… của Claude, thay vì chỉ nhận trọn `.claude/` một lần lúc tạo project
qua `defaultsetup.tgz`.

Publish bằng script `scripts/upload-unity-template-ai.mjs` (xem skill `publish-ai-features`).

## Ba nguồn nội dung

| Nguồn | Dùng khi | Đích cài trong project |
|---|---|---|
| `templates/unity-project/DefaultSetup/` | item **thuộc bộ mặc định** (đa số) — script tự quét theo `KIND_MAP` | đúng path gốc, vd `.claude/skills/ui-kit` |
| `templates/AIFeatures/<Category>/<item>` | item **ngoài** bộ mặc định (thử nghiệm, chuyên biệt, hoặc cố ý override) | suy ra từ category |
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
- `installedByDefault: true` → item nằm trong nhóm "Cài tất cả AI feature còn thiếu" chạy mặc định.

## Layout trên R2

```
unity-template/ai/index.json                          ← catalog (categories + items)
unity-template/ai/files/<Category>/<name>.zip         ← payload từng item
```

Đọc qua gateway: `https://upm-registry-worker.developer-a1f.workers.dev/template/ai/index.json`.

`index.json` bản local được script ghi lại ở `templates/AIFeatures/index.json` để soi và commit.
