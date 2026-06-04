# ezg-packages — Easygoing UPM Monorepo

Nguồn sự thật cho mọi Unity package nội bộ (`com.ezg.*`). Mỗi thư mục con trong `packages/` là một UPM package hợp lệ. Khi merge vào `main`, GitHub Actions tự đóng gói `.tgz` + sinh metadata và đẩy lên **Cloudflare R2**; Unity project cài qua scoped registry `com.ezg`.

- **Registry:** https://upm-registry-worker.developer-a1f.workers.dev
- **Storage:** Cloudflare R2 bucket `company-upm-registry`
- **Worker source + spec:** repo `ezg-scopedregister` (`worker/`, `upm-scoped-registry-requirements.md`)

## Layout

```text
packages/
  com.ezg.sample/            # package mẫu smoke-test (xoá sau khi verify)
    package.json
    Runtime/Ezg.Sample.asmdef
  com.ezg.core/              # do skill /package-module đẩy vào
scripts/
  registry-lib.mjs           # helper R2/util dùng chung
  publish.mjs                # pack + gen metadata + upload R2
  validate.mjs               # lint package.json trước publish (gác cổng CI)
  list.mjs                   # xem package/version trên registry
  unpublish.mjs              # gỡ version/package khỏi metadata (mặc định giữ tarball)
  rollback.mjs               # hạ dist-tags.latest về version cũ (không xóa gì)
  deprecate.mjs              # đánh dấu version deprecated
  package.json
.github/workflows/publish.yml   # tự publish khi push packages/**
.github/workflows/admin.yml     # quản trị thủ công (workflow_dispatch)
```

## Thêm / cập nhật một package

1. Đặt package vào `packages/com.ezg.<name>/` (có `package.json` đúng chuẩn UPM + asmdef).
2. Bump `version` trong `package.json` (semver). **Version đã publish là immutable** — phải tăng version mới.
3. Commit → PR → merge `main`. CI publish version mới. Version đã có sẵn trên R2 sẽ được **skip**.

> Bình thường package được tạo & đẩy vào đây tự động bởi skill `/package-module` trong repo game (mở PR). Thêm tay chỉ khi cần.

## Publish hoạt động thế nào

`scripts/publish.mjs` duyệt mọi `packages/*/package.json`:

1. Tarball version đó đã có trên R2 → skip.
2. `npm pack --json` → `.tgz` + `integrity` (sha512) + `shasum` (sha1).
3. Tải metadata cũ từ R2 (chưa có → tạo mới), merge version mới, set `dist-tags.latest`.
4. Upload `.tgz` (key `<name>/-/<file>.tgz`) + metadata (key `<name>`) lên R2.

`dist.tarball` trong metadata trỏ về URL Worker, nên UPM tải `.tgz` qua Worker.

## Chạy local (dry-run, không cần R2 credentials)

```bash
cd scripts
npm install
node publish.mjs --dry-run
```

Dry-run chỉ pack + dựng metadata rồi in ra, không gọi R2.

## CI secrets (GitHub repo → Settings → Secrets → Actions)

| Secret | Ý nghĩa |
|---|---|
| `R2_ACCOUNT_ID` | Cloudflare account id (endpoint S3 của R2) |
| `R2_ACCESS_KEY_ID` | R2 **S3 API** access key id |
| `R2_SECRET_ACCESS_KEY` | R2 **S3 API** secret |

`R2_BUCKET` và `REGISTRY_URL` cấu hình thẳng trong `.github/workflows/publish.yml`.

> Tạo R2 S3 token: Cloudflare → R2 → *Manage R2 API Tokens* → *Create API Token* (Object Read & Write trên bucket `company-upm-registry`). Token này khác với Worker token.

## Quản trị registry (xóa / hạ version / deprecate)

> ⚠️ Version đã publish bình thường **immutable**. Các lệnh dưới là **thao tác admin** ghi đè quy ước đó.
> Luôn chạy `--dry-run` trước. Mặc định **giữ tarball** (undo được). Chạy được local (cần R2 credentials)
> hoặc qua GitHub Actions → workflow **Registry admin** (`workflow_dispatch`, mặc định dry-run).

Local: `cd scripts && npm install`, đặt sẵn `R2_ACCOUNT_ID` / `R2_ACCESS_KEY_ID` / `R2_SECRET_ACCESS_KEY`.

| Lệnh | Tác dụng |
|---|---|
| `node list.mjs` | Bảng mọi package local + trạng thái trên R2 (`latest`, số version, tarball) |
| `node list.mjs <pkg>` | Lịch sử version chi tiết của 1 package |
| `node list.mjs --remote` | Liệt kê package bằng cách quét R2 (bỏ qua thư mục local) |
| `node validate.mjs` | Lint mọi `package.json` theo chuẩn UPM (CI chạy trước publish) |
| `node rollback.mjs <pkg> <version>` | **An toàn nhất** khi bản mới lỗi: trỏ `latest` về version cũ, không xóa gì, undo được |
| `node deprecate.mjs <pkg> <version> "lý do"` | Đánh dấu deprecated; consumer vẫn cài được, có cảnh báo (`--undo` để gỡ) |
| `node unpublish.mjs <pkg> <version>` | Gỡ 1 version khỏi metadata, **giữ tarball**; tự tính lại `latest` |
| `node unpublish.mjs <pkg>` | Gỡ cả package (xóa metadata), tarball vẫn giữ |
| `… --purge-tarball` | Thêm bước **xóa cứng** `.tgz` — **không undo được** |

Mọi lệnh ghi đều có `--dry-run` (xem trước, không ghi) và `--yes` (bỏ qua xác nhận, CI dùng).

**Khi nào dùng gì:**
- Bản mới lỗi, cần consumer quay về bản cũ ngay → `rollback` (nhanh, an toàn, undo được).
- Muốn ngăn dùng bản cũ nhưng không xóa → `deprecate`.
- Thật sự muốn gỡ version/package → `unpublish` (mặc định vẫn giữ `.tgz` để cứu vãn).

CI: vào **Actions → Registry admin → Run workflow**, chọn `action`, nhập `package`/`version`,
để `dry_run = true` xem trước rồi chạy lại với `dry_run = false`.

### Skill cho AI agent

`.agents/skills/` chứa skill để Claude Code tự thực hiện thao tác registry:

- `package-unpublish` — xóa package/version (gọi `unpublish.mjs`, hoặc `gh` qua `admin.yml`).
- `package-rollback` — hạ `latest` về version cũ (gọi `rollback.mjs`, hoặc `gh`).

Skill ưu tiên chạy script local (cần R2 creds), fallback sang GitHub Actions khi không có creds.

> Skill **nguồn** nằm ở `.agents/skills/` (commit vào repo). `.claude/skills` chỉ là **junction**
> (Windows) / symlink (mac/Linux) trỏ vào đó — git không biểu diễn được nên đã gitignore. Sau khi
> clone, tạo lại link bằng: `node scripts/link-skills.mjs` (hoặc `npm run link-skills` trong `scripts/`).

## Dùng package trong Unity (consumer)

`Packages/manifest.json`:

```json
{
  "scopedRegistries": [
    { "name": "Easygoing code base", "url": "https://upm-registry-worker.developer-a1f.workers.dev", "scopes": [ "com.ezg" ] }
  ],
  "dependencies": { "com.ezg.sample": "0.0.1" }
}
```
