---
name: sync-upm-template-deps
description: Đồng bộ version của package trong packages/ vào "dependencies" của templates/unity-project/unity-template.json rồi publish lên R2, để tab "UPM Packages" của Feature Hub thấy package mới/bản cập nhật. Từ 2026-07-14 việc này đã TỰ ĐỘNG chạy trong publish.mjs (cả local lẫn CI) nên KHÔNG cần gọi skill này sau khi publish nữa. Chỉ dùng khi cần sync độc lập với publish: sửa tay unity-template.json, bước sync trong publish bị lỗi, publish chạy với --no-template-sync, hoặc Feature Hub không thấy package tuy đã có trên UPM registry.
---

# Đồng bộ UPM dependencies vào template Feature Hub

## Vì sao tồn tại bước này

Publish package lên UPM registry và "khai báo package đó trong base template" là **hai đích
khác nhau**:

- Registry publish (`packages/<pkg>/package.json` → CI `publish.yml` → `publish.mjs`): đưa
  package + version lên Cloudflare R2 qua `upm-registry-worker`. Giao thức npm chuẩn — chỉ tra
  được từng package theo tên, **không có endpoint "list all"**.
- Base template (`templates/unity-project/unity-template.json` → `dependencies`, publish lên
  `unity-template/latest.json`): tab "UPM Packages" của Feature Hub
  (`FeatureHubModels.cs` → `TEMPLATE_URL`) chỉ đọc file này, không bao giờ tự query registry —
  vì Feature Hub chạy trên máy user, không có credentials để liệt kê registry.

=> Package published mà template chưa khai báo thì vẫn "vô hình" với Feature Hub.

## Bước này giờ đã tự động — đừng chạy tay sau khi publish

`scripts/publish.mjs` tự chain `sync-unity-template-deps.mjs` ở cuối mỗi lần publish thật, nên
mọi đường publish đều tự sync:

- **Local**: `node --env-file=.env publish.mjs` → publish xong tự sync + upload template.
- **CI**: `publish.yml` chạy chính `publish.mjs` (đã có sẵn R2 creds + `R2_BUCKET`), sau đó
  commit ngược `unity-template.json` về repo để file trong git không lệch với R2.

Chi tiết đáng biết:
- Sync **luôn chạy**, kể cả khi publish không đẩy version mới nào (mọi package đều "skip" vì đã
  published) — vì template có thể stale độc lập với registry. Không có gì đổi thì in
  "Nothing to sync" rồi dừng, không upload.
- Sync **bị bỏ qua khi có package publish lỗi** (`failures > 0`): template không được phép trỏ
  tới version chưa thật sự live trên registry.
- `publish.mjs --dry-run` sẽ forward `--dry-run` xuống sync → xem trước diff template mà không
  ghi/upload gì.
- Muốn publish registry mà không đụng template: `publish.mjs --no-template-sync`.

## Khi nào vẫn cần gọi skill này

Chỉ khi cần sync **tách rời khỏi publish**:
- Vừa sửa tay `unity-template.json` (thêm entry ngoài scope `com.ezg.*`) và muốn đẩy lên R2.
- Bước sync trong publish fail (vd. lệch SHA-256 một file template) và muốn chạy lại riêng sau
  khi đã xử lý xong.
- Publish đã chạy với `--no-template-sync` và giờ muốn sync bù.
- Feature Hub không thấy package tuy registry đã có → chạy `--dry-run` để xem lệch chỗ nào.

Chạy **luôn, không hỏi xác nhận** — khác `package-unpublish`/`package-rollback` (phá huỷ, cần
dry-run + confirm), việc này chỉ thêm/cập nhật version nên an toàn.

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
  manifest lên R2 cho tới khi xử lý xong file bị lệch hash đó. Lưu ý các file này **không được
  git-track**: trên CI thư mục `PackageTemplate/` rỗng nên mọi entry rơi vào nhánh "external
  url" và không bị check SHA — lỗi lệch hash chỉ xảy ra ở máy local có file thật.
