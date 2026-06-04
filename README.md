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
  publish.mjs                # pack + gen metadata + upload R2
  package.json
.github/workflows/publish.yml
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
