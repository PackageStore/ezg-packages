---
name: publish-defaultsetup
description: Đóng gói templates/unity-project/DefaultSetup thành defaultsetup.tgz rồi deploy (publish) lên Cloudflare R2 để mọi máy fresh nhận bản mới ở lần build kế tiếp. Dùng khi user nói "deploy DefaultSetup", "publish DefaultSetup", "đóng gói DefaultSetup lên server/R2", "cập nhật DefaultSetup trên server", hoặc sau khi sửa file trong DefaultSetup/ và muốn đẩy lên.
---

# Publish DefaultSetup lên R2

Đóng gói `templates/unity-project/DefaultSetup/` thành `defaultsetup.tgz` và upload lên Cloudflare R2
kèm file `.sha256`. End user chỉ giữ bootstrap mỏng + manifest, nên logic build sẽ **tải
`defaultsetup.tgz` từ R2** khi máy không có `DefaultSetup/` local cạnh bootstrap. Mỗi lần sửa file trong
`DefaultSetup/` mà muốn mọi máy nhận bản mới ở lần build kế tiếp → chạy skill này.

> `DefaultSetup` được phân phối dưới dạng **tarball `.tgz`**, KHÔNG phải `.unitypackage`: nó chứa
> `.claude/`, `.agents/`, `.mcp.json`, `ProjectSettings/`… (tooling ngoài `Assets/`) — thứ mà
> `.unitypackage` không mang được. Đây là cơ chế publish đúng, xem mục "Publish DefaultSetup lên R2"
> trong [templates/unity-project/README.md](../../../templates/unity-project/README.md).

Đây là skill cấp repo cho maintainer của `ezg-packages` — KHÔNG nằm trong `DefaultSetup/`, không deploy
cho game project.

## Trước khi chạy

1. Xác nhận đã lưu mọi thay đổi trong `templates/unity-project/DefaultSetup/`. Xem nhanh cái gì sắp đẩy:
   ```bash
   git status --short templates/unity-project/DefaultSetup/
   ```
2. Script cần R2 credentials trong `scripts/.env` (`R2_ACCOUNT_ID`, `R2_ACCESS_KEY_ID`,
   `R2_SECRET_ACCESS_KEY`, `R2_BUCKET`). File `.env` đã được gitignore — **không bao giờ commit**.
   Node đọc thẳng `process.env` nên phải nạp bằng cờ `--env-file=.env` (Node ≥ 20.6).

Lưu ý script sẽ tự động khi đóng gói:
- **Dereference symlink** (`.agents/` → file thật) để tarball chạy được trên Windows.
- **Loại** `.DS_Store` và `settings.local.json` (rác/cá nhân, không phân phối).
- Giữ macOS resource fork (`._*`) ra ngoài tarball.

## Chạy

Luôn `cd scripts` trước. **Dry-run trước, show cho user, rồi mới upload thật.**

```bash
cd scripts

# 1. Dry-run: chỉ đóng gói + in size/sha256/keys, KHÔNG upload. Show output cho user.
node --env-file=.env upload-unity-template-defaultsetup.mjs --dry-run

# 2. Sau khi user OK, upload thật (đẩy defaultsetup.tgz + defaultsetup.tgz.sha256 lên R2).
node --env-file=.env upload-unity-template-defaultsetup.mjs
```

Upload thật sẽ ghi 2 key:
- `unity-template/defaultsetup.tgz` — tarball gzip của folder `DefaultSetup/`.
- `unity-template/defaultsetup.tgz.sha256` — SHA-256 (logic build verify bằng file này).

## Verify sau khi upload

Hai giá trị dưới đây phải **khớp nhau** (hash của tarball live == nội dung file `.sha256` live):

```bash
curl -fsSL https://pub-d76b7e028ac14f9bb044ebd65bccd3d9.r2.dev/unity-template/defaultsetup.tgz | shasum -a 256
curl -fsSL https://pub-d76b7e028ac14f9bb044ebd65bccd3d9.r2.dev/unity-template/defaultsetup.tgz.sha256
```

Khớp → xong: máy fresh sẽ tải bản DefaultSetup mới ở lần build kế tiếp. Lệch → upload bị lỗi/nửa chừng,
chạy lại bước upload.

## Troubleshooting

- **`DefaultSetup/ProjectSettings not found`** — đang chạy sai thư mục hoặc folder thiếu; script yêu cầu
  `templates/unity-project/DefaultSetup/ProjectSettings` tồn tại.
- **`Missing env var R2_...`** — chưa nạp `.env`. Đảm bảo có `--env-file=.env` và các key R2_* trong
  `scripts/.env`.
- Muốn override key R2 hoặc public URL (ví dụ bucket staging): đặt
  `UNITY_TEMPLATE_DEFAULT_SETUP_R2_KEY` / `UNITY_TEMPLATE_DEFAULT_SETUP_PUBLIC_URL` trước khi chạy.
