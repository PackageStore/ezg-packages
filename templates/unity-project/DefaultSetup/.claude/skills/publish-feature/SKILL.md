---
name: publish-feature
description: Export MỘT feature game của project hiện tại (một thư mục dưới Features/<Domain>/<Feature>) thành .unitypackage rồi publish lên tab "Features" của EZG Feature Hub — đúng catalog của project mình, kèm marker + metadata phụ thuộc trong features-manifest.json. Dùng khi user nói "publish feature X", "đẩy feature X lên hub", "đưa feature X lên server", "publish feature lên Feature Hub", "gỡ feature X khỏi hub". KHÔNG dùng cho - "đóng module X thành package UPM" → /package-module (registry com.ezg.*, sống trong Packages/); "đóng X thành unitypackage" / "đẩy X lên tab Unity Packages" → /publish-unity-package (asset-catalog.json, tab Unity Packages, không theo project); "publish AI skill/command lên server" → /publish-ai-features (tab AI Feature, chạy trong repo ezg-packages). Phân biệt - skill này là thứ DUY NHẤT publish theo MÃ DỰ ÁN (M001, …) và theo Feature bucket. Phrasing mập mờ → hỏi một lần trước khi chạy.
---

# Publish Feature — `Features/<Domain>/<Feature>` → tab **Features** của Feature Hub

Lấy **một feature** trong `Assets/_Project/Features/<Domain>/<Feature>` của repo game hiện tại,
export thành `.unitypackage` (GUID + path giữ nguyên), rồi publish vào **catalog riêng của project
này** trên R2 để project khác cài lẻ qua `Ezg > Feature Hub > Features`.

**Một feature mỗi lần.** Seed cả wave lần đầu thì dùng `--all` ở STEP 6 (và nói rõ cho user).

> **4 đường publish, đừng lẫn:** `/package-module` → UPM registry · `/publish-unity-package` → tab
> Unity Packages (asset-catalog chung) · `/publish-ai-features` → tab AI Feature (chạy trong repo
> `ezg-packages`) · **skill này** → tab Features, theo mã dự án.

---

## ⚙️ Configuration

```
MODULE_PATH   = bắt buộc — thư mục feature, vd Assets/_Project/Features/Meta/Inventory
PROJECT_ID    = .claude/project-profile.json → "featureHubProjectId" (xem STEP 0)
CATEGORY      = SUY RA, không cho override: tên folder ngay dưới Features/ (Meta, Events, System,
                Monetization, Onboarding, _Shared…)
FEATURE_NAME  = basename(MODULE_PATH)
MONOREPO_PATH = env MONOREPO_PATH, mặc định $HOME/ezg-packages (Windows %USERPROFILE%\ezg-packages)
```

Auth git + `gh`: **giống hệt `/package-module` / `/publish-unity-package`** — SSH trước, SSH hỏng thì
gọi inline skill `setup-package-push`, cùng đường cuối mới fallback PAT. Đừng bịa cơ chế thứ hai.

---

## Pipeline

```
[0] PROJECT   → chốt PROJECT_ID (profile → index.json → cho chọn → ghi ngược profile)
[1] IDENTIFY  → MODULE_PATH → CATEGORY/FEATURE_NAME, new hay update
[2] AUDIT     → self-contained? requires? requiresPackages? leak game-specific?
[3] PLAN      → show card, xin xác nhận (publish là ghi live)
[4] SYNC      → clone monorepo: checkout main + pull --ff-only, từ chối nếu dirty
[5] EXPORT    → Unity MCP ExportPackage → templates/Features/<ID>/<CATEGORY>/<NAME>.unitypackage
[6] MANIFEST  → điền features-manifest.json (description/requires/requiresPackages)
[7] PUBLISH   → route A (R2 creds) hoặc route B (staging release + upload-asset.yml)
[8] COMMIT    → catalog.json + index.json + features-manifest.json → push main
[9] VERIFY    → curl catalog live, đối chiếu sha256
```

STEP 0–3 không ghi gì. Từ STEP 4 trở đi mới đụng clone; từ STEP 7 là ghi live lên R2.

---

## STEP 0 — Chốt PROJECT_ID

1. Đọc `featureHubProjectId` trong `.claude/project-profile.json` của repo game.
2. Có giá trị → đối chiếu `templates/Features/index.json` (hoặc catalog live). Khớp → dùng luôn,
   **không hỏi gì thêm**.
3. Trống, hoặc không khớp project nào → **in danh sách project đang có** (`id` + `featureCount`),
   thêm lựa chọn *"tạo project mới"* (user nhập mã, vd `M002`). Sau khi user chọn:
   **ghi ngược `featureHubProjectId` vào `.claude/project-profile.json`** để lần sau tự động.
4. User không chọn được / không có project nào → **hard stop**. Tuyệt đối không đoán mã dự án và
   không publish nhờ catalog của project khác — đó là trộn feature của hai game vào một tab.

---

## STEP 1 — Nhận dạng feature

- `CATEGORY` = folder ngay dưới `Features/`; `FEATURE_NAME` = basename. Không cho user override
  category — nó phải khớp bucket thật để cây feature bên consumer dựng lại đúng chỗ.
- `MODULE_PATH` phải là **thư mục feature**, không phải một file lẻ (file lẻ là việc của
  `/publish-unity-package`).
- **New hay update:** đọc `templates/Features/<ID>/catalog.json`, tìm `category` + `name` khớp.
  Có rồi = update → **STEP 7 bắt buộc `--force`**, không thì R2 giữ nguyên payload cũ và consumer
  không bao giờ thấy bản mới. Không có version field: `sha256` chính là tín hiệu version.

---

## STEP 2 — Audit (dùng codegraph trước, grep sau)

**a) Self-contained — điều kiện cứng.** Mọi asset của feature phải nằm **trong** `MODULE_PATH`:
art, prefab, CSV, SO. Có asset lạc ra `Visual/ArtAsset/…` hay `Resources/` nơi khác → dừng, bảo
user gom vào `MODULE_PATH/Visuals/` rồi export lại. Lý do không thoả hiệp: `markerPaths` vừa là dấu
"đã cài" vừa là **thứ bị XÓA khi gỡ** (`FeatureHubService.UninstallUnityPackage`) — nhiều root thì
gỡ chỉ xóa một phần và để lại rác; root nông thì gỡ nhầm cả `Assets/_Project`. Script cũng chặn
cứng ở STEP 7, nên phát hiện sớm ở đây đỡ mất công export lại.

**b) `requires` — feature khác cùng project.** `codegraph_callees` / `codegraph_impact` trên các
symbol của feature → symbol nào nằm dưới `Features/<Domain>/<Other>/` thì `<Domain>/<Other>` là một
dependency. Thực tế hay gặp: event nào cũng cần `Events/_Shared`. Ghi vào manifest ở STEP 6.
`requires` chỉ trỏ tới feature **có trong catalog của chính project này** — script validate và
chặn vòng lặp.

**c) `requiresPackages` — UPM.** Đọc `.asmdef` của feature + `using` → package `com.ezg.*` nào cần
(vd `com.ezg.iap`, `com.ezg.localize`). Ghi dạng `{ "com.ezg.iap": "1.2.3" }`.

**d) Leak game-specific.** CSV key hardcode, `GameEnums.Features` số cụ thể, singleton chỉ có ở
game này → cảnh báo và ghi vào `description`. Feature vốn *là* code game nên không hard-stop như
`/package-module`, nhưng user phải biết mình đang ship gì.

> Metadata phụ thuộc hiện được **ghi vào catalog nhưng client cũ bỏ qua** (Newtonsoft bỏ field lạ).
> Nó có giá trị ngay ở khâu publish (chặn publish thiếu dependency) và sẽ tự có tác dụng khi
> `com.ezg.featurehub` bản mới đọc `requires`/`requiresPackages`.

---

## STEP 3 — Plan & confirm

```
Project        : <PROJECT_ID>            [từ project-profile.json | user vừa chọn]
Feature        : <CATEGORY>/<FEATURE_NAME>       [new | update <sha cũ 8 ký tự> → mới]
Source         : <MODULE_PATH>           (export nguyên trạng, KHÔNG sửa repo game)
Marker         : <MODULE_PATH>  (+ GUID folder, script tự trích)
requires       : <danh sách | none>
requiresPackages: <danh sách | none>
Đích           : templates/Features/<PROJECT_ID>/<CATEGORY>/<FEATURE_NAME>.unitypackage
                 + catalog.json + index.json + features-manifest.json → ezg-packages main + R2
```

Hỏi đúng một lần: **"Publish `<CATEGORY>/<FEATURE_NAME>` vào catalog `<PROJECT_ID>`? Ghi live ngay
sau khi xác nhận. (yes / plan-only / adjust)"** — chỉ chạy tiếp khi **yes**.

---

## STEP 4 — Đồng bộ clone monorepo

```bash
git -C "$MONOREPO_PATH" checkout main
git -C "$MONOREPO_PATH" fetch "$remote" --prune
git -C "$MONOREPO_PATH" pull --ff-only "$remote" main
git -C "$MONOREPO_PATH" status --short          # dirty -> show cho user, hỏi trước, KHÔNG reset --hard
```

`pull` **không** mang về `.unitypackage` (gitignore) — nó mang về `catalog.json`, `index.json`,
`features-manifest.json`, tức đúng phần metadata mà bước regenerate cần để không xoá nhầm feature
của người khác. Cần binary của project để làm việc khác (kiểm tra, re-hash) thì thêm
`--fetch-missing` ở STEP 7, script tự tải từ R2 và verify sha256.

---

## STEP 5 — Export `.unitypackage`

Cần Editor đang mở qua Unity MCP (`unity_list_instances`, nhiều instance thì `unity_select_instance`).

```csharp
// unity_execute_code, trong Editor của repo game
AssetDatabase.ExportPackage(
    new[] { "<MODULE_PATH>" },
    "<MONOREPO_PATH>/templates/Features/<PROJECT_ID>/<CATEGORY>/<FEATURE_NAME>.unitypackage",
    ExportPackageOptions.Recurse);
```

`Recurse` mà **không** `IncludeDependencies` — dependency được khai bằng `requires` chứ không nhét
vào gói (nhét vào là hai feature cùng ship một file, cài chồng nhau là hỏng).

Thư mục đích chưa có thì tạo trước.

---

## STEP 6 — Điền `features-manifest.json`

`templates/Features/<PROJECT_ID>/features-manifest.json`, key = `"<CATEGORY>/<FEATURE_NAME>"`:

```json
{
  "Meta/Inventory": {
    "description": "Kho đồ của người chơi. Cần DOTween + Odin (đã có sẵn trong base).",
    "requires": ["_Shared/IAPIntegration"],
    "requiresPackages": { "com.ezg.pooling": "1.0.4" }
  }
}
```

`markerPaths` / `markerGuids` **để trống cho script tự điền** từ chính binary (chuẩn hơn gõ tay).
Chỉ khai tường minh khi feature buộc phải nhiều root — và khi đó phải nói rõ với user rằng gỡ
feature sẽ xóa đúng những path đã khai.

Project mới chưa có manifest → sinh khung bằng:
```bash
node upload-unity-template-features.mjs --project <ID> --emit-manifest    # không cần R2 creds
```

---

## STEP 7 — Publish

**Chọn route:** `scripts/.env` có đủ `R2_ACCOUNT_ID` + `R2_ACCESS_KEY_ID` + `R2_SECRET_ACCESS_KEY`
→ **route A**. Không có → **route B**.

### Route A — đẩy thẳng R2

```bash
cd "$MONOREPO_PATH/scripts" && npm install --silent

node --env-file=.env upload-unity-template-features.mjs \
  --project <ID> --feature <CATEGORY>/<FEATURE_NAME> --dry-run     # luôn dry-run trước, show user

node --env-file=.env upload-unity-template-features.mjs \
  --project <ID> --feature <CATEGORY>/<FEATURE_NAME> [--force]     # --force BẮT BUỘC khi update
```

### Route B — không có R2 creds (ai push được repo là publish được)

```bash
node upload-unity-template-features.mjs --project <ID> \
  --feature <CATEGORY>/<FEATURE_NAME> --emit-only        # sinh catalog.json + index.json tại chỗ

tag="feature-$(git -C "$MONOREPO_PATH" rev-parse --short HEAD)-<slug>"
gh release create "$tag" \
  "$MONOREPO_PATH/templates/Features/<ID>/<CATEGORY>/<NAME>.unitypackage" \
  "$MONOREPO_PATH/templates/Features/<ID>/catalog.json" \
  "$MONOREPO_PATH/templates/Features/index.json" \
  --repo PackageStore/ezg-packages --title "Feature: <ID>/<CATEGORY>/<NAME>" \
  --notes "Staging release. Xóa được sau khi CI chạy xong."

# 3 dispatch: payload, catalog, index
gh workflow run upload-asset.yml --repo PackageStore/ezg-packages \
  -f release_tag="$tag" -f asset_name="<NAME>.unitypackage" \
  -f key="unity-template/features/<ID>/files/<CATEGORY>/<NAME>.unitypackage" \
  -f content_type="application/octet-stream" -f force=<true nếu update> -f dry_run=false
gh workflow run upload-asset.yml … -f asset_name="catalog.json" \
  -f key="unity-template/features/<ID>/catalog.json" -f content_type="application/json" -f force=true
gh workflow run upload-asset.yml … -f asset_name="index.json" \
  -f key="unity-template/features/index.json" -f content_type="application/json" -f force=true

gh release delete "$tag" --repo PackageStore/ezg-packages --yes --cleanup-tag   # sau khi cả 3 run xanh
```

Route B tốn 3 dispatch cho **mỗi** feature → seed cả wave thì dùng route A. Dispatch fail → giữ
release lại, báo user, dừng.

**Gỡ feature khỏi hub:** `--remove <CATEGORY>/<NAME>` (thêm `--purge` để xóa hẳn object trên R2 —
không undo được). Xoá file khỏi đĩa **không** còn tự gỡ khỏi catalog nữa: catalog được merge từ bản
đã commit nên phải nói tường minh.

---

## STEP 8 — Commit metadata

```bash
git -C "$MONOREPO_PATH" add templates/Features/<ID>/catalog.json \
    templates/Features/<ID>/features-manifest.json templates/Features/index.json
git -C "$MONOREPO_PATH" commit -m "feat(features): publish <ID>/<CATEGORY>/<NAME>"
git -C "$MONOREPO_PATH" push "$remote" main
```

Push bị từ chối (người khác vừa publish) → `pull --rebase` xong **chạy lại STEP 7 emit/regenerate**
rồi push, đừng resolve tay conflict trên JSON generated. Không bao giờ `--force`.

---

## STEP 9 — Verify

```bash
TOKEN=$(python3 -c "import json,os;print(json.load(open(os.path.expanduser('~/.ezg/credentials.json')))['access_token'])")

curl -fsSL -H "Authorization: Bearer $TOKEN" \
  https://upm-registry-worker.developer-a1f.workers.dev/template/features/<ID>/catalog.json \
  | python3 -c "import json,sys;d=json.load(sys.stdin);e=[a for a in d['assets'] if a['name']=='<NAME>'][0];print(e['sha256'],e['markerPaths'],e.get('requires'))"

shasum -a 256 "$MONOREPO_PATH/templates/Features/<ID>/<CATEGORY>/<NAME>.unitypackage"
```

Hai sha256 phải khớp. Lệch = payload chưa lên (quên `--force`) → chạy lại STEP 7 với `--force`,
đừng chỉ báo cáo là xong.

---

## Report

1. Project + feature vừa publish, new hay update (sha cũ → mới).
2. Commit hash đã push, R2 key của payload + catalog + index.
3. `requires` / `requiresPackages` đã khai.
4. Đường xem: `Ezg > Feature Hub > Features > <PROJECT_ID> > <CATEGORY>`.
5. **Repo game không đổi** — pipeline này chỉ đọc.

---

## Guardrails

- **Một feature mỗi lần**; `--all` chỉ cho lần seed đầu của một project, và phải nói trước với user.
- **Không đoán `PROJECT_ID`.** Không khớp thì cho chọn, không chọn được thì dừng.
- **Category suy từ đường dẫn, không cho override.**
- **Feature phải self-contained trong một folder.** Muốn nhiều root thì khai tay trong manifest và
  chấp nhận việc gỡ chỉ xóa đúng những gì đã khai.
- **Marker không bao giờ được là thư mục chứa** (`Assets/_Project`, `Assets/_Project/Features`…).
  Script chặn cứng; đừng tìm cách lách bằng `--allow-multi-root`.
- **Update = phải `--force`**, nếu không payload cũ nằm im trên R2.
- **Không sửa repo game.** `ExportPackage` chỉ đọc.
- **Clone monorepo là clone thật** — dirty thì hỏi user, không `reset --hard`.
- **Push thẳng `main`, không PR, không force-push.**
- **Không thêm workflow GitHub mới** — `upload-asset.yml` đã đủ cho route B.
