# ezg.base.default — Package Info & Publish Guide

## Mô tả

`ezg.base.default.unitypackage` chứa toàn bộ cấu hình mặc định (Default Setup) cho một Unity project mới:

| File/Folder      | Nội dung |
|------------------|----------|
| `ProjectSettings/`| Tags, Layers, Build settings, v.v... mặc định của project |
| `.gitignore`     | Các quy tắc bỏ qua file của Git |
| `.agents/`       | Antigravity agents, docs, rules, scripts, skills, workflows (symlinks → `.claude/`) |
| `.claude/`       | Claude agents, commands, docs, harness, rules, scripts, skills |
| `.env.example`   | Template cấu hình biến môi trường (Discord bot, developer IDs) |
| `.mcp.json`      | Cấu hình các MCP server (Unity, CodeGraph, GitLab) |
| `CLAUDE.md`      | Tài liệu hướng dẫn AI coding assistant cho dự án |
| `backlog/`       | Thư mục quản lý backlog các task phát triển tính năng |
| `BACKLOG.md`     | File cấu trúc và theo dõi tổng quan backlog |

## Nguồn

Các file được lấy từ:

```text
templates/unity-project/DefaultSetup/
```

**Loại trừ:** `.DS_Store`, `settings.local.json`, `settings.json`, `worktrees/`

## Catalog Entry (asset-catalog.json)

```json
{
  "name": "ezg.base.default",
  "fileName": "ezg.base.default.unitypackage",
  "url": "https://pub-d76b7e028ac14f9bb044ebd65bccd3d9.r2.dev/unity-template/files/ezg.base.default.unitypackage",
  "category": "EZG Base",
  "sha256": "aba253f0b47c3f66fb2f088511bc12e9a5f75ea1504684026ae80d81c775cc93",
  "installedByDefault": true,
  "description": "Default setup package including ProjectSettings, .gitignore, AI tooling configuration, and backlog."
}
```

## Khi nào cần update?

Update và publish lại khi có thay đổi trong bất kỳ file nào thuộc thư mục `DefaultSetup/`.

## Quy trình update & publish

### Bước 1: Tạo lại .unitypackage

```bash
cd scripts
node create-unity-default-package.mjs --dry-run   # xem trước các file sẽ đóng gói
node create-unity-default-package.mjs             # tạo thật
```

Script sẽ in ra SHA-256 mới ở cuối. **Copy SHA-256 đó.**

### Bước 2: Cập nhật sha256 trong asset-catalog.json

Mở `templates/unity-project/asset-catalog.json`, tìm entry `"name": "ezg.base.default"` và cập nhật `"sha256"`:

```json
"sha256": "<SHA-256 MỚI>"
```

### Bước 3: Upload lên R2

```bash
cd scripts
node --env-file=.env upload-unity-template-catalog.mjs --force   # --force để ghi đè file cũ trên R2
```

### Bước 4: Verify

Kiểm tra file đã có trên R2:

```bash
curl -sI "https://pub-d76b7e028ac14f9bb044ebd65bccd3d9.r2.dev/unity-template/files/ezg.base.default.unitypackage" | grep content-length
```

## Lịch sử publish

| Ngày       | SHA-256 | Ghi chú |
|------------|---------|---------|
| 2026-07-01 | `aba253f0b47c3f66fb2f088511bc12e9a5f75ea1504684026ae80d81c775cc93` | Initial release (đóng gói toàn bộ DefaultSetup) |

## Script tạo package

Script đóng gói nằm tại:

```text
scripts/create-unity-default-package.mjs
```
