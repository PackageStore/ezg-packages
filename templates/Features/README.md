# templates/Features — catalog feature game theo dự án

Nguồn cho tab **Features** của EZG Feature Hub: mỗi dự án một catalog riêng, mỗi feature một
`.unitypackage` cài lẻ được vào project khác cùng dòng template.

```text
templates/Features/
  index.json                       # generated — danh sách dự án (COMMIT)
  <PROJECT>/
    features-manifest.json         # authored — metadata do người viết (COMMIT)
    catalog.json                   # generated — catalog của dự án (COMMIT)
    <Category>/<Feature>.unitypackage   # payload (GITIGNORE — chỉ nằm trên đĩa + R2)
```

`<Category>` = bucket domain trong game (`Meta`, `Events`, `System`, `Monetization`, `Onboarding`,
`_Shared`), suy từ đường dẫn `Assets/_Project/Features/<Category>/<Feature>` — không đặt tay.

Publish bằng skill **`/publish-feature`** trong repo game (một feature mỗi lần, tự audit dependency
+ tự chọn route). Phần dưới là hợp đồng dữ liệu mà skill đó dựa vào.

## features-manifest.json — vì sao phải có

`.unitypackage` không mang metadata gì ngoài đường dẫn asset, nên bản script đầu tiên **đoán**
`markerPaths` bằng path nông nhất trong tarball. Cái đoán đó quyết định thứ Feature Hub **XÓA khi
gỡ feature** — đoán trúng `Assets/_Project` là mất cả project. Từ nay đoán chỉ còn là fallback:

```jsonc
{
  "schemaVersion": 1,
  "project": "M001",
  "displayName": "M001",
  "features": {
    "Events/BattleRoyale": {
      "description": "Sự kiện đua top theo mùa.",
      "markerPaths": ["Assets/_Project/Features/Events/BattleRoyale"],
      "markerGuids": ["9ecdfa8e78ada5a4d96474ef488f762d"],
      "requires": ["Events/_Shared"],
      "requiresPackages": { "com.ezg.iap": "1.2.3" }
    }
  }
}
```

| Field | Ý nghĩa |
|---|---|
| `markerPaths` | Dấu "đã cài" **và** thứ bị xóa khi gỡ. Phải là folder của chính feature, tối thiểu 4 cấp, không bao giờ là thư mục chứa (`Assets/_Project`…). Script chặn cứng. |
| `markerGuids` | Bền hơn path (consumer đổi tên folder vẫn nhận ra). Trích thẳng từ tarball — cứ để trống, script điền. |
| `requires` | Feature khác **trong cùng catalog**. Script validate tồn tại + chặn vòng lặp. |
| `requiresPackages` | UPM id → version. |
| `description` | Ghi peer requirement / nợ kỹ thuật đã biết. |

`requires` / `requiresPackages` / `description` hiện được ghi vào `catalog.json` nhưng
`com.ezg.featurehub` bản cũ **bỏ qua field lạ** (Newtonsoft) — nên khai sớm là an toàn, và có tác
dụng ngay ở khâu publish (chặn publish thiếu dependency).

Sinh khung manifest cho dự án chưa có (không cần R2 credentials):

```bash
cd scripts && node upload-unity-template-features.mjs --project M001 --emit-manifest
```

## Merge, không rescan

`*.unitypackage` bị gitignore, nên một clone chỉ có binary mà chính người đó build. Trước đây
catalog/index được dựng lại **sạch từ đĩa** → publish một feature từ clone mới là **xoá sạch** mọi
feature/dự án nằm ở máy khác. Giờ mỗi entry được merge theo thứ tự:

```
authored (features-manifest.json)  >  tính từ binary local  >  entry trong catalog.json đã commit
```

Hệ quả cần nhớ: **xoá file khỏi đĩa không còn gỡ được feature khỏi catalog** — phải nói tường minh
`--remove <Category>/<Name>` (thêm `--purge` để xoá luôn object trên R2, không undo được).

## Lệnh

```bash
cd scripts && npm install

# publish 1 feature (update thì BẮT BUỘC --force, không thì payload cũ nằm im trên R2)
node --env-file=.env upload-unity-template-features.mjs --project M001 --feature Meta/Inventory --force

# seed cả dự án lần đầu
node --env-file=.env upload-unity-template-features.mjs --project M001 --all

# không có R2 credentials: sinh JSON tại chỗ rồi publish qua Actions → upload-asset.yml
node upload-unity-template-features.mjs --project M001 --feature Meta/Inventory --emit-only

# kéo binary mà máy này thiếu (verify sha256 theo catalog)
node --env-file=.env upload-unity-template-features.mjs --project M001 --fetch-missing --all

# gỡ khỏi catalog
node --env-file=.env upload-unity-template-features.mjs --project M001 --remove Meta/Inventory [--purge]
```

Cờ khác: `--dry-run` (không đụng R2 lẫn đĩa), `--skip-files` (chỉ đẩy catalog + index),
`--allow-multi-root` (cho package trải nhiều thư mục gốc — gỡ sẽ chỉ xoá một phần, tránh dùng).

## R2 layout

```text
unity-template/features/index.json
unity-template/features/<PROJECT>/catalog.json
unity-template/features/<PROJECT>/files/<Category>/<Feature>.unitypackage
```

Client đọc qua worker: `https://upm-registry-worker.developer-a1f.workers.dev/template/features/…`
(yêu cầu đăng nhập). Dự án 0 feature bị loại khỏi `index.json` thay vì hiện một tab rỗng.
