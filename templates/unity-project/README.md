# EzgFeatureHub

EzgFeatureHub là bộ công cụ tạo nhanh một Unity project template từ các package và asset đã chuẩn bị sẵn. Mục tiêu chính là giúp tạo một project Unity mới chỉ với vài bước nhập liệu, không cần tự mở Unity Hub, tạo project thủ công, rồi import từng package một.

Khi chạy script, người dùng chỉ cần nhập tên project, chọn phiên bản Unity đã cài trên máy, sau đó script sẽ tự tạo hoặc cập nhật Unity project tương ứng.

## Dự án này làm gì?

Script chính của dự án là `build_unity_template.sh`. Script này tự động:

- Hỏi tên project cần tạo.
- Tạo project nằm ngang hàng với file `.sh`.
- Detect hệ điều hành Windows hoặc macOS.
- Detect các Unity version đã cài trên máy.
- Cho người dùng chọn Unity version bằng danh sách ngắn gọn.
- Tạo Unity project mới nếu project chưa tồn tại.
- Cập nhật `Packages/manifest.json`.
- Thêm package và scoped registry được khai báo trong `unity-template.json`.
- Chuẩn bị các file được khai báo trong `unity-template.json`, tải về cache nếu thiếu và có URL.
- Copy các local `.tgz` package đã khai báo vào thư mục `Packages`.
- Import đúng các file `.unitypackage` đã khai báo trong `unity-template.json`.

## Kiến trúc: bootstrap + logic

Để end user **không bao giờ phải tải lại file** mỗi khi logic build được cải tiến, script được tách làm hai phần:

- `build_unity_template.sh` — **bootstrap mỏng** (~150 dòng), dùng trên Windows (Git Bash) và macOS (terminal). Đây là file end user giữ trên máy, gần như không bao giờ thay đổi. Mỗi lần chạy, nó tải logic mới nhất từ server, verify SHA-256, rồi bàn giao quyền điều khiển.
- `build_unity_template.command` — **bản mirror của `.sh` dành riêng cho macOS**. Nội dung gần như giống hệt `.sh` nhưng bỏ các đoạn code Windows, mặc định `PAUSE_ON_EXIT=always` để Terminal window không đóng ngay sau khi double-click từ Finder.
- `build_unity_template.logic.sh` — **toàn bộ logic build**. File này sống trên Cloudflare R2 (không nằm trong thư mục của end user). Mọi cải tiến/sửa lỗi chỉ cần publish lại file này; end user tự động nhận bản mới ở lần chạy kế tiếp.

> **Quan trọng:** `build_unity_template.sh` và `build_unity_template.command` là hai bản bootstrap song song. Khi sửa một file, phải sửa cả file còn lại để giữ chúng đồng bộ.

Luồng hoạt động khi end user chạy `./build_unity_template.sh`:

```text
build_unity_template.sh (bootstrap)
  │  tải build_unity_template.logic.sh  -> .ezg-bootstrap/
  │  tải build_unity_template.logic.sh.sha256
  │  verify SHA-256  (sai hash -> dừng, không chạy)
  ▼
exec bash .ezg-bootstrap/build_unity_template.logic.sh  (toàn bộ công việc thật)
```

Đặc điểm:

- Bootstrap truyền thư mục thật của user cho logic qua biến `EZG_SCRIPT_DIR`/`EZG_SCRIPT_NAME`, nên logic vẫn tạo project ngang hàng với bootstrap (không phải trong cache).
- Nếu mất mạng nhưng đã có bản logic tải trước đó trong `.ezg-bootstrap`, bootstrap chạy ở chế độ offline với bản cache gần nhất.
- Nếu file logic tải về không khớp SHA-256 đã publish (hỏng khi tải hoặc bị can thiệp trên CDN), bootstrap dừng ngay vì lý do an toàn.
- Override URL logic khi cần (ví dụ bucket staging): `UNITY_TEMPLATE_SCRIPT_URL=...`.

> Muốn chỉnh logic build? Sửa `build_unity_template.logic.sh` rồi publish lại (xem mục [Publish logic build lên R2](#publish-logic-build-lên-r2)). Không cần đụng tới `build_unity_template.sh`.

## Quy trình resolve package & DefaultSetup

Sau khi dựng skeleton, ghi `manifest.json` và extract toàn bộ `.unitypackage` thẳng vào `Assets/`
(không khởi động Unity), logic mới gọi Unity **một lần ở chế độ headless** để resolve UPM package
(scoped registry + git) + import asset + compile. Vì mọi thứ phụ thuộc lẫn nhau (UPM ↔ asset trong
unitypackage) nên buộc phải có mặt cùng lúc và compile chung một pass.

Pass headless này được xử lý như **resolver/warm-up, không phải cổng compile cứng**:

- Chạy lại tối đa **3 pass** (`RESOLVE_MAX_PASSES`). Verify bằng cách grep `: error CS` trong log,
  **không tin exit code** (Unity batch đôi khi exit 0 dù có compiler error).
- **Lỗi resolve package** (thiếu package / sai version / registry / không có `git`) → **fatal**, dừng
  kèm chẩn đoán. Đây là lỗi mở Editor cũng không tự chữa được.
- **Compiler error (CS)** → coi là **race import lần đầu** (Unity compile trước khi AssetDatabase đăng
  ký xong asmdef từ các unitypackage). Pass kế tiếp — đúng bằng việc "mở Editor lần 2" — thường sạch.
  Nếu sau cả 3 pass vẫn còn thì **chỉ cảnh báo + in dòng lỗi, KHÔNG dừng**: lần auto-mở Unity ở cuối
  (`run_launcher`) sẽ biên dịch lại cho sạch. Nhờ vậy end user luôn nhận được project + Unity tự mở.

`DefaultSetup/` được **force-copy** vào project mới: `DefaultSetup/ProjectSettings/` (tags/layers/build
settings baked-in) đè lên `ProjectSettings`, còn các mục còn lại ở gốc `DefaultSetup/` (`.claude/`,
`.agents/`, `.mcp.json`…) được copy thẳng vào **root project** để project mới có sẵn bộ tooling. Thứ tự
ưu tiên nguồn:

1. `DefaultSetup/` đặt cạnh bootstrap (dev override) — dùng nếu có.
2. Nếu không có → tải `defaultsetup.tgz` từ R2 vào `.ezg-cache`, verify SHA-256, giải nén (xem
   [Publish DefaultSetup lên R2](#publish-defaultsetup-lên-r2)). Đây là cách máy fresh (chỉ có
   bootstrap + manifest) vẫn nhận được DefaultSetup.

Việc overwrite được áp dụng **cả trước và sau** pass resolve (cộng backstop ở EXIT trap), nên baked-in
settings luôn thắng kể cả khi Unity ghi lại ProjectSettings trong lúc resolve.

Khi script thoát (qua EXIT trap, áp dụng cho mọi đường thoát kể cả khi compile lỗi):

- **Auto-mở project:** chỉ cần file launcher (`<ProjectName>.command`/`.bat`) tồn tại là chạy luôn —
  không phụ thuộc compile có sạch hay không (Editor là trình biên dịch cuối). Tắt bằng `--no-launch`
  / `AUTO_LAUNCH=0`.
- **Dọn cache:** `.ezg-cache` bị xoá ở **mọi** lần thoát, **trừ** khi resolve thất bại vì lỗi package
  (giữ lại để lần chạy sau không phải tải lại). Tắt dọn bằng `--keep-cache` / `KEEP_DOWNLOAD_CACHE=1`.

Cả hai có cờ one-shot nên không chạy lặp; khi thành công đường chính đã làm thì backstop bỏ qua.

### Popup khi mở Unity & cờ command line

Pass resolve headless và lệnh auto-mở dùng các cờ Unity sau:

- `-accept-apiupdate`: chạy API Updater ngay trong pass headless → khi GUI mở, script đã được migrate
  nên **không hiện popup "Script updating consent"**.
- `-buildTarget <target>`: pass headless compile **cùng build target** với lúc launcher mở, để GUI
  không switch target rồi recompile lại (tránh tái phát race import → giảm khả năng hiện Safe Mode).
- `-ignoreCompilerErrors` (chỉ trên lệnh mở GUI): best-effort để Unity tiếp tục mở dù còn compile error.

> **Lưu ý về popup "Enter Safe Mode":** Unity **không** có CLI flag hay EditorPrefs nào để tự chọn
> "Ignore" (preference `Show Enter Safe Mode Dialog` nếu tắt thì tự *vào* Safe Mode — ngược lại).
> Cách duy nhất chắc chắn né được là project compile sạch. Các cờ trên giảm khả năng popup xuất hiện
> nhưng không đảm bảo 100% khi vẫn còn compile error thật.

Auto-mở trên Windows dùng thẳng Unity executable đã resolve (`$UNITY_EXECUTABLE`), không qua bước tự dò
lại của file `.bat` — nên hoạt động cả khi Unity cài ở thư mục custom (vd `D:\Unity Hub\Editor`).

## Cấu trúc thư mục

```text
EzgFeatureHub/
├─ build_unity_template.sh        # bootstrap — Windows/macOS terminal
├─ build_unity_template.command   # bootstrap — macOS Finder double-click (mirror của .sh)
├─ unity-template.json
├─ .ezg-bootstrap/                # cache logic tải từ server (tự tạo, ẩn)
├─ .ezg-cache/
├─ PackageTemplate/
│  ├─ com.apple.unityplugin.core-3.2.0.tgz
│  ├─ com.apple.unityplugin.gamekit-4.0.1.tgz
│  ├─ Odin Inspector 4.0.1.3.unitypackage
│  └─ Ultimate Editor Enhancer.unitypackage
└─ <ProjectName>/
```

Trong đó:

- `build_unity_template.sh`: bootstrap mỏng — tải và chạy logic build mới nhất từ server (xem mục [Kiến trúc: bootstrap + logic](#kiến-trúc-bootstrap--logic)). Khi sửa file này phải sửa luôn `build_unity_template.command`.
- `build_unity_template.command`: bản macOS của bootstrap, dành cho double-click từ Finder. Nội dung mirror với `.sh`, bỏ phần Windows. Khi sửa file này phải sửa luôn `build_unity_template.sh`.
- `unity-template.json`: file cấu hình trung tâm chứa danh sách package, scoped registry và file asset cần dùng.
- `.ezg-bootstrap`: cache chứa `build_unity_template.logic.sh` (logic build) đã tải từ server, cùng file `.sha256`. Thư mục này được tạo tự động và cố gắng ẩn; có thể xóa bất cứ lúc nào, lần chạy sau sẽ tải lại.
- `.ezg-cache`: cache tạm cho remote template JSON và các file tải từ server. Script cố gắng ẩn thư mục này và mặc định sẽ xóa sau khi chạy thành công.
- `PackageTemplate`: nơi chứa sẵn các package local, `.tgz`, `.unitypackage`.
- `<ProjectName>`: thư mục Unity project được tạo sau khi chạy script. Thư mục này nằm ngang hàng với file `.sh`.

Ví dụ nếu nhập project name là `Tank`, project sẽ được tạo tại:

```text
EzgFeatureHub/Tank
```

## Yêu cầu

### Windows

Cần có:

- Git Bash hoặc môi trường bash tương đương.
- Unity đã được cài qua Unity Hub hoặc có registry Unity Installer.
- PowerShell, thường đã có sẵn trên Windows.

Script sẽ tìm Unity theo nhiều cách:

- `C:\Program Files\Unity\Hub\Editor`
- `C:\Program Files (x86)\Unity\Hub\Editor`
- `D:\Unity Hub\Editor` nếu Unity được khai báo trong Windows Registry.
- Registry:
  - `HKLM\SOFTWARE\Unity Technologies\Installer`
  - `HKCU\SOFTWARE\Unity Technologies\Installer`
  - `HKLM\SOFTWARE\WOW6432Node\Unity Technologies\Installer`
  - `HKCU\SOFTWARE\WOW6432Node\Unity Technologies\Installer`

### macOS

Cần có:

- Bash.
- Unity cài qua Unity Hub.

Script sẽ tìm Unity trong:

```text
/Applications/Unity/Hub/Editor
~/Applications/Unity/Hub/Editor
```

## Cách chạy nhanh

Mở Git Bash tại thư mục `EzgFeatureHub`, sau đó chạy:

```bash
./build_unity_template.sh
```

Flow mặc định:

```text
Project name [UnityTemplateProject]:
Detected Unity versions:
  1) 6000.2.6f2
  2) 6000.3.16f1
Select Unity number [2]:
```

Nếu nhấn Enter ở `Project name`, script dùng tên mặc định:

```text
UnityTemplateProject
```

Nếu nhấn Enter ở `Select Unity number`, script chọn Unity version mới nhất trong danh sách.

## Chạy với tham số

Tạo project bằng tên cụ thể:

```bash
./build_unity_template.sh --project-name Tank
```

Chọn Unity version cụ thể:

```bash
./build_unity_template.sh --unity-version 6000.3.16f1
```

Chỉ định trực tiếp Unity executable:

```bash
./build_unity_template.sh --unity-path "D:\Unity Hub\Editor\6000.3.16f1\Editor\Unity.exe"
```

Bỏ qua bước import `.unitypackage`, chỉ tạo project và cập nhật manifest:

```bash
./build_unity_template.sh --skip-import
```

Chỉ định file template JSON khác:

```bash
./build_unity_template.sh --template-file ./unity-template-dev.json
```

Đọc template JSON từ server:

```bash
./build_unity_template.sh --template-url "https://pub-d76b7e028ac14f9bb044ebd65bccd3d9.r2.dev/unity-template/latest.json"
```

Khi dùng `--template-url`, script sẽ tải JSON về:

```text
.ezg-cache/unity-template.remote.json
```

Sau đó mọi bước cài đặt sẽ đọc từ file vừa tải thay vì `unity-template.json` local.

Chỉ định thư mục cache cho file được tải từ URL:

```bash
./build_unity_template.sh --download-cache-dir ./.ezg-cache
```

Giữ lại cache sau khi chạy thành công để debug hoặc dùng lại file đã tải:

```bash
./build_unity_template.sh --keep-cache
```

Không giữ cửa sổ lại sau khi chạy:

```bash
./build_unity_template.sh --no-pause
```

Mặc định, sau khi build xong script sẽ tự động mở project trong Unity bằng cách chạy launcher vừa tạo
(`<ProjectName>.command` trên macOS, `<ProjectName>.bat` trên Windows). Tắt hành vi này (ví dụ CI/headless):

```bash
./build_unity_template.sh --no-launch
```

Xem help:

```bash
./build_unity_template.sh --help
```

## Cách cấu hình template

File `unity-template.json` là cấu hình trung tâm cho toàn bộ package và registry sẽ được merge vào `Packages/manifest.json`.

Ví dụ rút gọn:

```json
{
  "schemaVersion": 1,
  "templateVersion": "local-dev",
  "dependencies": {
    "com.ezg.ads": "0.1.0",
    "com.unity.addressables": "2.7.2",
    "com.apple.unityplugin.core": "file:com.apple.unityplugin.core-3.2.0.tgz"
  },
  "files": {
    "localPackages": [
      {
        "fileName": "com.apple.unityplugin.core-3.2.0.tgz",
        "url": "https://server/packages/com.apple.unityplugin.core-3.2.0.tgz",
        "sha256": "..."
      }
    ],
    "unityPackages": [
      {
        "fileName": "Odin Inspector 4.0.1.3.unitypackage",
        "url": "https://server/unitypackages/Odin%20Inspector%204.0.1.3.unitypackage",
        "sha256": "..."
      }
    ]
  },
  "scopedRegistries": [
    {
      "name": "Easygoing",
      "url": "https://upm-registry-worker.developer-a1f.workers.dev",
      "scopes": [
        "com.ezg",
        "com.cysharp",
        "com.google",
        "com.coffee"
      ]
    }
  ]
}
```

`dependencies` hỗ trợ các dạng package Unity đang dùng:

- Package version từ Unity registry:

```json
"com.unity.addressables": "2.7.2"
```

- Package từ Git URL:

```json
"com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask"
```

- Package local dạng `.tgz`:

```json
"com.apple.unityplugin.core": "file:com.apple.unityplugin.core-3.2.0.tgz"
```

Với package local dạng `file:`, file tương ứng cần được khai báo trong `files.localPackages`. Script sẽ tìm file trong `PackageTemplate`, nếu thiếu thì tải từ `url` vào cache, verify `sha256` nếu có, rồi copy sang `<ProjectName>/Packages`.

Nếu trong manifest đã có scoped registry cùng `name`, script sẽ thay thế registry cũ bằng registry mới.

## Template remote hiện tại

Template JSON hiện đang được publish trên Cloudflare R2 tại:

```text
https://pub-d76b7e028ac14f9bb044ebd65bccd3d9.r2.dev/unity-template/latest.json
```

Các file asset đi kèm nằm dưới:

```text
https://pub-d76b7e028ac14f9bb044ebd65bccd3d9.r2.dev/unity-template/files/
```

Khi một file trong `files.localPackages` hoặc `files.unityPackages` không có trong `PackageTemplate`, script sẽ tải từ `url` về `.ezg-cache`, kiểm tra `sha256`, rồi dùng file cache đó để copy/import. Sau khi install thành công, cache mặc định sẽ bị xóa.

## Cách thêm file .unitypackage

Đặt file `.unitypackage` vào thư mục:

```text
PackageTemplate/
```

Sau đó khai báo file trong `files.unityPackages` của `unity-template.json`:

```json
"files": {
  "unityPackages": [
    {
      "fileName": "Odin Inspector 4.0.1.3.unitypackage",
      "url": "",
      "sha256": "d7cea1c6e769a28e6573bb9593bd39187ff6272c303920bce90bc6011b9b67d2"
    }
  ]
}
```

Khi chạy script, Unity sẽ được gọi ở batch mode để import từng file `.unitypackage` được khai báo. File nằm trong `PackageTemplate` nhưng không được khai báo sẽ không bị import.

Log import được tạo trong thư mục project:

```text
<ProjectName>/unity-import-<PackageName>.log
```

## Cách thêm package local .tgz

Đặt file `.tgz` vào `PackageTemplate`, sau đó khai báo trong cả `dependencies` và `files.localPackages` của `unity-template.json`.

Ví dụ file tồn tại:

```text
PackageTemplate/com.apple.unityplugin.core-3.2.0.tgz
```

Khai báo trong `unity-template.json`:

```json
"dependencies": {
  "com.apple.unityplugin.core": "file:com.apple.unityplugin.core-3.2.0.tgz"
},
"files": {
  "localPackages": [
    {
      "fileName": "com.apple.unityplugin.core-3.2.0.tgz",
      "url": "",
      "sha256": "e1eed58efb19ed7bc448775a2a3407e4b5f1259697a46492f3b7446690ca7251"
    }
  ]
}
```

Sau khi chạy script, file sẽ được copy sang:

```text
<ProjectName>/Packages/com.apple.unityplugin.core-3.2.0.tgz
```

Manifest sẽ giữ đường dẫn:

```json
"com.apple.unityplugin.core": "file:com.apple.unityplugin.core-3.2.0.tgz"
```

## Publish template lên R2

Repo có script hỗ trợ upload asset và manifest template:

```bash
cd ../../scripts
npm install
npm run upload-unity-template-assets -- --dry-run
```

Upload thật cần các biến môi trường R2. Credentials được lưu trong file `scripts/.env`
(file này đã được gitignore — **không bao giờ commit**). Nội dung mẫu:

```bash
R2_ACCOUNT_ID=...
R2_ACCESS_KEY_ID=...
R2_SECRET_ACCESS_KEY=...
R2_BUCKET=company-upm-registry
UNITY_TEMPLATE_PUBLIC_BASE_URL=https://pub-d76b7e028ac14f9bb044ebd65bccd3d9.r2.dev/unity-template/files
UNITY_TEMPLATE_MANIFEST_PUBLIC_URL=https://pub-d76b7e028ac14f9bb044ebd65bccd3d9.r2.dev/unity-template/latest.json
```

Vì các script đọc thẳng từ `process.env` (không tự load `.env`), nạp file `.env` bằng
cờ `--env-file` của Node (Node ≥ 20.6):

```bash
cd ../../scripts
node --env-file=.env upload-unity-template-assets.mjs --update-urls
```

Trên Git Bash cũng có thể export thủ công trước khi chạy:

```bash
cd ../../scripts
set -a; source .env; set +a
npm run upload-unity-template-assets -- --update-urls
```

Script upload sẽ:

- Đọc `templates/unity-project/unity-template.json`.
- Verify `sha256` của từng file trong `PackageTemplate`.
- Upload file trong `files.localPackages` và `files.unityPackages` lên R2 dưới key `unity-template/files/<fileName>`.
- Cập nhật `url` trong `unity-template.json` khi có `--update-urls`.
- Upload manifest lên key `unity-template/latest.json`.

Sau khi upload, lệnh install remote chuẩn là:

```bash
./build_unity_template.sh --template-url "https://pub-d76b7e028ac14f9bb044ebd65bccd3d9.r2.dev/unity-template/latest.json"
```

Không commit R2 credentials vào repo. Nếu credentials từng bị paste ra ngoài, hãy rotate token trong Cloudflare.

## Asset catalog (asset optional)

Ngoài bộ asset bootstrap khai báo trong `unity-template.json`, repo còn có `asset-catalog.json`
— danh sách **mọi `.unitypackage`** dùng được, kèm `category`, để một Unity Editor tool nội bộ
hiển thị và cài lẻ khi cần (không phải lúc nào cũng cài hết như bootstrap).

Mỗi entry:

```json
{
  "name": "AllIn1Bundle",
  "fileName": "AllIn1Bundle.unitypackage",
  "url": "https://pub-d76b7e028ac14f9bb044ebd65bccd3d9.r2.dev/unity-template/files/AllIn1Bundle.unitypackage",
  "category": "VFX & Shader",
  "sha256": "7d50f3bb…",
  "installedByDefault": false
}
```

- `installedByDefault: true` — file đã khai báo trong `unity-template.json`, bootstrap cài sẵn
  (Firebase, Google Play, AppLovin, DOTween, Odin…). Đa số host sẵn trên R2/server ngoài.
- `installedByDefault: false` — asset optional nằm trong `PackageTemplate/` (AllIn1*, Feel, GUI*…),
  chỉ cài khi chọn. `installedByDefault` **chỉ là metadata phân loại**, không điều khiển logic
  bootstrap; bootstrap vẫn chạy theo `unity-template.json`.

Catalog được publish trên R2 tại:

```text
https://pub-d76b7e028ac14f9bb044ebd65bccd3d9.r2.dev/unity-template/asset-catalog.json
```

### Thêm asset optional + publish catalog

1. Đặt file `.unitypackage` vào `PackageTemplate/`.
2. Thêm entry vào `asset-catalog.json` (đặt `url` theo `unity-template/files/<fileName>` đã URL-encode;
   `sha256` để trống cũng được — script tự verify từ file local).
3. Upload file + catalog lên R2:

```bash
cd ../../scripts
node --env-file=.env upload-unity-template-catalog.mjs --dry-run   # xem trước
node --env-file=.env upload-unity-template-catalog.mjs              # upload thật
```

Script `upload-unity-template-catalog.mjs` sẽ:

- Đọc `templates/unity-project/asset-catalog.json`.
- Với asset có file local trong `PackageTemplate/`: verify `sha256` rồi upload lên
  `unity-template/files/<fileName>` (đã tồn tại thì skip; `--force` để ghi đè).
- Asset không có file local (đã host ngoài như Firebase) → bỏ qua, giữ nguyên `url`.
- Upload chính `asset-catalog.json` lên key `unity-template/asset-catalog.json` (`--skip-catalog` để bỏ).

Dùng chung `scripts/.env` với các script publish khác.

## Publish logic build lên R2

Khi sửa logic build (`build_unity_template.logic.sh`), publish lại để mọi end user nhận bản mới ở lần chạy kế tiếp — **không cần** họ tải lại `build_unity_template.sh`.

```bash
cd ../../scripts
npm install
node --env-file=.env upload-unity-template-script.mjs --dry-run   # xem trước
node --env-file=.env upload-unity-template-script.mjs              # upload thật
```

Script này sẽ:

- Tính SHA-256 của `build_unity_template.logic.sh` và ghi lại vào `build_unity_template.logic.sh.sha256`.
- Upload logic lên key `unity-template/build_unity_template.logic.sh`.
- Upload SHA-256 lên key `unity-template/build_unity_template.logic.sh.sha256` (bootstrap dùng file này để verify).

Dùng chung file `scripts/.env` với các script publish khác. Sau khi upload, kiểm tra nhanh:

```bash
curl -fsSL https://pub-d76b7e028ac14f9bb044ebd65bccd3d9.r2.dev/unity-template/build_unity_template.logic.sh | shasum -a 256
curl -fsSL https://pub-d76b7e028ac14f9bb044ebd65bccd3d9.r2.dev/unity-template/build_unity_template.logic.sh.sha256
```

Hai giá trị phải khớp nhau.

## Publish DefaultSetup lên R2

`DefaultSetup/` chứa cấu hình baked-in được **force-copy** vào mọi project tạo ra: `ProjectSettings/`
(tags/layers/build settings) đè lên `ProjectSettings` của project, còn `.claude/`, `.agents/`, `.mcp.json`…
được copy vào root project. End user chỉ giữ bootstrap mỏng + manifest, nên folder này **phải tự đến được
mọi máy**: logic build sẽ tải `defaultsetup.tgz` từ R2 khi máy không có `DefaultSetup/` local cạnh bootstrap.

> Thứ tự ưu tiên trong logic: nếu có `DefaultSetup/` cạnh bootstrap (dev override) thì dùng nó; nếu không,
> tải `defaultsetup.tgz` từ R2 vào `.ezg-cache`, verify SHA-256, giải nén rồi áp dụng.

Mỗi khi sửa file trong `DefaultSetup/`, publish lại:

```bash
cd ../../scripts
npm install
node --env-file=.env upload-unity-template-defaultsetup.mjs --dry-run   # xem trước
node --env-file=.env upload-unity-template-defaultsetup.mjs              # upload thật
```

Script này sẽ:

- Đóng gói `templates/unity-project/DefaultSetup/` thành tarball (gốc archive là folder `DefaultSetup/`),
  **dereference symlink** (`.agents/` → file thật để chạy được trên Windows) và **loại** `.DS_Store` +
  `settings.local.json` (rác/cá nhân, không phân phối).
- Upload tarball lên key `unity-template/defaultsetup.tgz`.
- Upload SHA-256 lên key `unity-template/defaultsetup.tgz.sha256` (logic dùng file này để verify).

Dùng chung file `scripts/.env` với các script publish khác. Sau khi upload, kiểm tra nhanh:

```bash
curl -fsSL https://pub-d76b7e028ac14f9bb044ebd65bccd3d9.r2.dev/unity-template/defaultsetup.tgz | shasum -a 256
curl -fsSL https://pub-d76b7e028ac14f9bb044ebd65bccd3d9.r2.dev/unity-template/defaultsetup.tgz.sha256
```

Hai giá trị phải khớp nhau.

> Chi tiết logic chọn nguồn DefaultSetup, thứ tự overwrite, và hành vi retry/verify của pass resolve:
> xem mục [Quy trình resolve package & DefaultSetup](#quy-trình-resolve-package--defaultsetup).

## Kết quả sau khi chạy

Sau khi chạy thành công, project Unity sẽ có cấu trúc cơ bản:

```text
<ProjectName>/
├─ Assets/
├─ Packages/
│  ├─ manifest.json
│  ├─ com.apple.unityplugin.core-3.2.0.tgz
│  └─ com.apple.unityplugin.gamekit-4.0.1.tgz
├─ ProjectSettings/
├─ unity-create-project.log
└─ unity-import-*.log
```

Nếu project đã tồn tại, script không xóa project. Script sẽ cập nhật `Packages/manifest.json` và import lại các `.unitypackage` được cấu hình.

## Biến môi trường hỗ trợ

Có thể dùng biến môi trường để override hành vi:

```bash
UNITY_PATH="/path/to/Unity"
UNITY_VERSION="6000.3.16f1"
UNITY_TEMPLATE_URL="https://pub-d76b7e028ac14f9bb044ebd65bccd3d9.r2.dev/unity-template/latest.json"
KEEP_DOWNLOAD_CACHE=1
UNITY_HUB_EDITORS_DIR="/path/to/Unity/Hub/Editor"
AUTO_LAUNCH=0
PAUSE_ON_EXIT="always"
```

Ý nghĩa:

- `UNITY_PATH`: đường dẫn chính xác tới Unity executable.
- `UNITY_VERSION`: version Unity muốn chọn.
- `UNITY_TEMPLATE_URL`: URL remote `unity-template.json`, tương đương `--template-url`.
- `KEEP_DOWNLOAD_CACHE`: đặt `1` để giữ lại `.ezg-cache` sau khi chạy thành công, tương đương `--keep-cache`.
- `UNITY_HUB_EDITORS_DIR`: thư mục Unity Hub Editor custom.
- `AUTO_LAUNCH`: đặt `0` để không tự động mở project trong Unity sau khi build, tương đương `--no-launch` (mặc định `1`).
- `PAUSE_ON_EXIT`: điều khiển việc giữ cửa sổ lại sau khi chạy. Giá trị hỗ trợ: `always`, `auto`, `never`.

## Troubleshooting

### ERROR: No Unity installation detected

Script không tìm thấy Unity.

Cách xử lý:

- Kiểm tra Unity đã được cài chưa.
- Kiểm tra Unity có nằm trong Unity Hub không.
- Trên Windows, kiểm tra registry có key Unity Installer không.
- Chạy bằng cách chỉ định trực tiếp Unity:

```bash
./build_unity_template.sh --unity-path "D:\Unity Hub\Editor\6000.3.16f1\Editor\Unity.exe"
```

### Unity version không hiện trong danh sách

Có thể Unity được cài ở thư mục custom.

Cách xử lý:

```bash
UNITY_HUB_EDITORS_DIR="D:\Unity Hub\Editor" ./build_unity_template.sh
```

### Package local file not found

Lỗi này xuất hiện khi `unity-template.json` có package dạng `file:...` nhưng file tương ứng không nằm trong `PackageTemplate` hoặc download cache.

Ví dụ khai báo:

```json
"dependencies": {
  "com.example.package": "file:com.example.package-1.0.0.tgz"
},
"files": {
  "localPackages": [
    {
      "fileName": "com.example.package-1.0.0.tgz",
      "url": "https://server/packages/com.example.package-1.0.0.tgz",
      "sha256": "..."
    }
  ]
}
```

Nếu `url` để trống, file này phải tồn tại:

```text
PackageTemplate/com.example.package-1.0.0.tgz
```

Nếu `url` có giá trị, script sẽ tải file thiếu vào `.ezg-cache`. Cache này mặc định bị xóa sau khi script chạy thành công.

### Cửa sổ tắt quá nhanh

Script mặc định sẽ giữ cửa sổ lại trên Windows để bạn đọc lỗi. Nếu vẫn muốn ép giữ cửa sổ:

```bash
PAUSE_ON_EXIT=always ./build_unity_template.sh
```

Nếu chạy trong terminal và không muốn pause:

```bash
./build_unity_template.sh --no-pause
```

## Ghi chú quan trọng

- Tên project không được chứa các ký tự: `/ \ : * ? " < > |`.
- Project được tạo ngang hàng với `build_unity_template.sh`.
- Script không tự xóa project cũ.
- Nếu chạy lại cùng tên project, script sẽ update project hiện có.
- Các project được tạo như `Tank`, `UnityTemplateProject`, hoặc tên khác là output của tool, không phải cấu hình template gốc.
