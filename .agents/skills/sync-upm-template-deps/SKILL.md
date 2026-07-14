---
name: sync-upm-template-deps
description: Đồng bộ version của package trong packages/ (com.ezg.*, com.google.play.*, ...) vào "dependencies" của templates/unity-project/unity-template.json rồi publish file này lên R2, để tab "UPM Packages" của Feature Hub thấy được package mới/bản cập nhật. Chạy NGAY sau khi publish một package mới hoặc bump version một package có sẵn — không cần hỏi lại user, không cần dry-run gate. Dùng khi user nói "publish package X", "cập nhật version package Y", hoặc khi Feature Hub không thấy package tuy đã có trên UPM registry.
---

# Đồng bộ UPM dependencies vào template Feature Hub

## Vì sao cần skill này

Publish package lên UPM registry và "khai báo package đó trong base template" là **hai bước
độc lập, hai đích khác nhau**:

- Registry publish (`packages/<pkg>/package.json` → CI `publish.yml` → `publish.mjs`): đưa
  package + version lên Cloudflare R2 qua `upm-registry-worker`. Giao thức npm chuẩn — chỉ tra
  được từng package theo tên, **không có endpoint "list all"**.
- Base template (`templates/unity-project/unity-template.json` → `dependencies`, publish lên
  `unity-template/latest.json`): danh sách version đã **curate thủ công**. Tab "UPM Packages"
  của Feature Hub (`FeatureHubModels.cs` → `TEMPLATE_URL`) chỉ đọc file này, không bao giờ tự
  query registry — vì Feature Hub chạy trên máy user, không có credentials để liệt kê registry.

=> Publish package xong mà quên sync 2 phần này thì package đã published vẫn "vô hình" với
Feature Hub. Đây là lý do `com.ezg.power-rename` (và `com.ezg.csv-reader` bump 0.2.4→0.2.5,
`com.ezg.editor-ui`, `com.ezg.supabase`) publish xong không hiện trong tab UPM cho tới khi được
sync thủ công lần đầu (phiên 2026-07-14).

## Khi nào chạy

Ngay sau khi:
- Publish package UPM mới (thêm folder mới dưới `packages/`), hoặc
- Bump version một package đã có trong `packages/`.

Chạy **luôn, không hỏi xác nhận trước** — khác với `package-unpublish`/`package-rollback`
(thao tác phá huỷ, cần dry-run + confirm), việc này chỉ thêm/cập nhật version nên an toàn để tự
động hoá hoàn toàn.

## Chạy

```bash
cd scripts
node --env-file=.env sync-unity-template-deps.mjs
```

Script tự động:
1. Đọc `package.json` của mọi thư mục trong `packages/*`.
2. **Version bump của package đã có sẵn trong `dependencies`** → luôn tự sync, bất kể scope.
3. **Package hoàn toàn mới** (chưa từng có trong `dependencies`) → chỉ tự thêm nếu thuộc scope
   `com.ezg.*` VÀ không nằm trong danh sách loại trừ (`SKIP_NEW_PACKAGES` trong script, hiện có
   `com.ezg.sample` — package smoke-test, mô tả ghi rõ "safe to delete"). Package mới ngoài
   scope này (vd. họ `com.google.play.*` / `com.google.android.appbundle`) **cố ý không tự
   thêm**: các plugin đó đã được base template cài qua `.unitypackage` (`files.unityPackages`)
   rồi — tự thêm bản UPM nữa sẽ cài trùng 2 lần cùng 1 SDK. Script in ra danh sách bị skip để
   biết mà thêm tay nếu thực sự cần.
4. Nếu có thay đổi: ghi lại file local, rồi tự chạy `upload-unity-template-assets.mjs` để
   publish file lên `unity-template/latest.json` trên R2 (cần R2 creds — xem `scripts/.env`,
   [[r2-publish-config]]).
5. Nếu KHÔNG có gì thay đổi: in "Nothing to sync" và dừng, không upload lại.

Cờ tuỳ chọn:
- `--dry-run`: chỉ in ra sẽ thay đổi/skip gì, KHÔNG ghi file / KHÔNG upload.
- `--skip-upload`: chỉ cập nhật file local, không đẩy lên R2 (hiếm khi cần).

## Package mới ngoài scope com.ezg.* muốn đưa vào template

Nếu một package như `com.google.play.games` hoặc `com.ezg.sample` thực sự cần vào base
template (quyết định editorial, không phải mechanical), thêm tay entry đó vào `dependencies`
trong `templates/unity-project/unity-template.json` rồi chạy lại script — từ lần đó về sau nó
đã "có sẵn" nên mọi version bump tiếp theo tự sync bình thường.

## Verify sau khi upload

```bash
curl -fsSL https://pub-d76b7e028ac14f9bb044ebd65bccd3d9.r2.dev/unity-template/latest.json \
  | node -pe "JSON.parse(require('fs').readFileSync(0,'utf8')).dependencies['<package-name>']"
```

Phải in ra đúng version vừa publish.

## Troubleshooting

- **`Missing env var R2_...`** — chưa nạp `.env` (`cd scripts && node --env-file=.env ...`).
- Script upload nội bộ (`upload-unity-template-assets.mjs`) sẽ báo lỗi và dừng nếu một file
  khác trong `files.localPackages`/`files.unityPackages` bị lệch SHA-256 — không liên quan tới
  phần dependencies vừa sync (file local đã được ghi trước bước upload), nhưng sẽ chặn việc đẩy
  manifest lên R2 cho tới khi xử lý xong file bị lệch hash đó.
