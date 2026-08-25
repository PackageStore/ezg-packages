---
name: publish-ai-features
description: Đóng gói từng tài sản AI của Claude (skill, command, agent, rule, docs, script, MCP config...) thành .zip rồi deploy lên Cloudflare R2 cho tab "AI Feature" của EZG Feature Hub. Dùng khi user nói "publish AI skill", "đẩy skill/command lên server", "cập nhật AI feature", "deploy AI features", "update skill AI lên server", hoặc sau khi sửa/thêm file trong .claude/ của DefaultSetup và muốn project đang chạy nhận được.
---

# Publish AI Features lên R2

Đẩy **từng** skill / command / agent / rule / doc / script của Claude lên R2 để tab **AI Feature**
trong Feature Hub cài lẻ vào project đang chạy.

> **Khác `publish-defaultsetup` ở chỗ nào?**
> `defaultsetup.tgz` chỉ đến với project **lúc được sinh ra**. Project tạo tháng trước không có đường
> nào nhận skill viết tuần này. Skill này publish **catalog cài lẻ**: mỗi item là 1 `.zip` độc lập,
> project nào cũng cài/cập nhật/gỡ được bất cứ lúc nào qua Feature Hub.
> Sửa file trong `DefaultSetup/.claude/` thường cần **cả hai**: `publish-ai-features` cho project cũ,
> `publish-defaultsetup` cho project mới sinh sau này.

Đây là skill cấp repo cho maintainer của `ezg-packages` — KHÔNG nằm trong `DefaultSetup/`, nên project
game sinh mới không tự có nó.

> Skill này **có mặt trong tab AI Feature** (khai báo qua `sources` trong `ai-manifest.json`, publish
> thẳng từ `.agents/skills/` nên chỉ có một bản gốc duy nhất). Cài về đâu cũng được, nhưng muốn CHẠY
> thì phải đứng trong một bản clone của repo `ezg-packages`: nó gọi `scripts/upload-unity-template-ai.mjs`
> và cần R2 credentials trong `scripts/.env`. Cài vào project game chỉ để đọc/tham chiếu.

---

## Pipeline

```
[1] SOURCE   → xác định item nằm ở nguồn nào (DefaultSetup hay templates/AIFeatures)
[2] DRY-RUN  → chạy --dry-run, show output cho user
[3] UPLOAD   → upload thật (payload .zip + index.json)
[4] VERIFY   → curl index.json qua gateway, đối chiếu sha256
[5] COMMIT   → nhắc user commit templates/AIFeatures/index.json
```

---

## STEP 1 — Nguồn nội dung

| Item thuộc loại | Sửa/thêm ở đâu |
|---|---|
| Nằm trong bộ mặc định (đa số skill/command/agent hiện có) | `templates/unity-project/DefaultSetup/.claude/…` — script tự quét |
| Ngoài bộ mặc định, hoặc cố ý override 1 item | `templates/AIFeatures/<Category>/<item>` |
| Đã nằm sẵn chỗ khác trong repo (vd `.agents/skills/…`) | khai `sources` trong `ai-manifest.json`, publish tại chỗ — **không copy**, vì bản copy sẽ lệch ngay lần đầu có người sửa một trong hai |

Category hợp lệ + đích cài tương ứng: xem bảng trong
[templates/AIFeatures/README.md](../../../templates/AIFeatures/README.md).
Trùng `<Category>/<name>` → bản sau thắng theo thứ tự DefaultSetup → AIFeatures → `sources`.

Muốn bỏ item khỏi catalog hoặc sửa mô tả/`installedByDefault`: sửa
`templates/AIFeatures/ai-manifest.json` (`exclude`, `overrides`).

Xem nhanh cái gì sắp đẩy:

```bash
git status --short templates/unity-project/DefaultSetup/.claude/ templates/AIFeatures/
```

---

## STEP 2 — Dry-run (luôn làm trước)

Script cần R2 credentials trong `scripts/.env` (`R2_ACCOUNT_ID`, `R2_ACCESS_KEY_ID`,
`R2_SECRET_ACCESS_KEY`, `R2_BUCKET`). Node đọc thẳng `process.env` nên phải nạp bằng `--env-file`.
`--dry-run` KHÔNG đụng R2 lẫn đĩa.

```bash
cd scripts

node --env-file=.env upload-unity-template-ai.mjs --dry-run                  # toàn bộ
node --env-file=.env upload-unity-template-ai.mjs --dry-run --category Skills
node --env-file=.env upload-unity-template-ai.mjs --dry-run --item Skills/ui-kit
```

Show output cho user: mỗi item in ra key R2, số file, đích cài trong project, sha256; cuối cùng là
bảng tổng theo category.

---

## STEP 3 — Upload thật

Sau khi user OK:

```bash
cd scripts

node --env-file=.env upload-unity-template-ai.mjs                    # tất cả
node --env-file=.env upload-unity-template-ai.mjs --category Skills  # 1 nhóm
node --env-file=.env upload-unity-template-ai.mjs --item Skills/ui-kit
```

Ghi lên R2:
- `unity-template/ai/files/<Category>/<name>.zip` — payload từng item.
- `unity-template/ai/index.json` — catalog, **luôn phủ toàn bộ item trên đĩa** kể cả khi chỉ upload
  một category (index không bao giờ lệch thực tế).

Cờ khác:
- `--force` — đẩy lại cả item mà key đã tồn tại. **Cần khi sửa nội dung một item đã publish**: mặc
  định script bỏ qua key đã có để khỏi upload thừa.
- `--skip-files` — chỉ đẩy `index.json` (dùng khi chỉ đổi metadata trong `ai-manifest.json`).

> Zip được sinh **tất định** (timestamp cố định): item không đổi nội dung thì sha256 không đổi, nên
> Feature Hub chỉ báo "Có bản mới" khi nội dung thật sự đổi. Đừng lo `--force` làm user thấy update ảo.

---

## STEP 4 — Verify

`/template/*` yêu cầu đăng nhập — lấy token đã cache của máy này:

```bash
TOKEN=$(python3 -c "import json,os;print(json.load(open(os.path.expanduser('~/.ezg/credentials.json')))['access_token'])")

# Catalog live: số item + số category
curl -fsSL -H "Authorization: Bearer $TOKEN" \
  https://upm-registry-worker.developer-a1f.workers.dev/template/ai/index.json \
  | python3 -c "import json,sys;d=json.load(sys.stdin);print(len(d['items']),'items /',len(d['categories']),'categories')"

# Payload live phải khớp sha256 trong catalog (đổi tên item cho phù hợp)
curl -fsSL -H "Authorization: Bearer $TOKEN" \
  https://upm-registry-worker.developer-a1f.workers.dev/template/ai/files/Skills/ui-kit.zip | shasum -a 256
```

Khớp → xong. Mở `Ezg > Feature Hub > AI Feature` là thấy item mới.

---

## STEP 5 — Commit

Script ghi lại `templates/AIFeatures/index.json` (bản local của catalog). Nhắc user commit file này
cùng thay đổi nội dung để repo khớp với server.

---

## Troubleshooting

- **`Missing env var R2_...`** — chưa nạp `.env`; phải có `--env-file=.env` (Node ≥ 20.6).
- **Item sửa rồi mà Feature Hub vẫn báo "Đã cài"** — quên `--force`, payload cũ còn nguyên trên R2 nên
  sha256 trong catalog không đổi. Chạy lại với `--force`.
- **404 khi Feature Hub tải `/template/ai/index.json`** — worker gateway chưa route prefix `ai/`. Route
  nằm ở repo `ezg-scopedregister`, không sửa được từ đây; báo user để mở route như đã làm cho
  `features/`.
- **`Item không có trên đĩa: X`** — sai id. Id là `<Category>/<name>`, `<name>` bỏ đuôi `.md`
  (vd `Commands/new-ui`, không phải `Commands/new-ui.md`).
- **Muốn đổi bucket/prefix staging** — đặt `UNITY_TEMPLATE_AI_PREFIX` trước khi chạy.
