---
name: bughub-setup
description: Cài BugHub (Bug Logger dùng chung — server tạo GitLab Issue + thread Discord cho bug QA gửi) cho project Unity hiện tại trong một lệnh, idempotent — kiểm MCP `bughub` đã kết nối, (tài khoản admin) đăng ký/sửa project trên server qua `bughub_admin_project_upsert` (kèm gán thành viên `members` khi dev nêu "A làm lead, B QA…"), (không admin) chỉ kiểm project đã tồn tại qua `GET /v1/projects/<code>/form`, kiểm package `com.ezg.buglogger`, ghi `projectCode` vào asset `BugLoggerSettings` của game và `bugHub.projectCode` trong `.claude/project-profile.json`, kiểm gate cheat + in cách thêm máy QA vào allowlist cheat (Remote Config), nhắc cài `/auto-clear` cho `/fix-bug --watch`. Dev chỉ trả lời ID forum Discord (và xác nhận mã project nếu chưa có). Chạy lại trên project đã cài → mọi bước `ok`, không tạo trùng. Dùng khi user nói "/bughub-setup", "setup bughub", "cài bughub", "cài bug logger cho project", "đăng ký project lên BugHub", "gắn project vào BugHub". KHÔNG sửa bug (→ `/fix-bug`), KHÔNG cài/upgrade CI (→ `/auto-build-setup`), KHÔNG đổi config server ngoài `bughub_admin_project_upsert`.
---

# BugHub Setup — cài BugHub cho project hiện tại

Kiến trúc (contract, nguồn chân lý): BugHub là **một** Cloudflare Worker cho mọi project. Client game chỉ
cấu hình **mã project** (`projectCode`); mọi việc với GitLab/Discord do server làm. Máy dev nói chuyện với
server qua **MCP `bughub`** (Claude Code lộ tool dạng `mcp__bughub__bughub_*`) và đúng một endpoint public
`GET /v1/projects/<code>/form`.

**Luật cứng của skill:**

1. **Không** dùng CLI hay REST API của GitLab/Discord dưới bất kỳ hình thức nào; không đụng secret/config
   server ngoài `bughub_admin_project_upsert`. Thao tác server chỉ qua tool `bughub_*` hoặc
   `GET <endpoint>/v1/projects/<code>/form`. (`git fetch`/`git show` nhánh build ở bước 7 là git thường, không phải API.)
2. **Idempotent:** mỗi bước đọc trạng thái hiện có trước, chỉ ghi khi lệch. Lần chạy thứ hai trên project
   đã cài → mọi bước `ok`, không tạo asset/key/project trùng.
3. **Không hỏi những gì suy ra được.** Chỉ hỏi dev: xác nhận mã project (khi chưa có), ID forum Discord,
   ID kênh feedback (tuỳ chọn), `gitlabProject` (chỉ khi `origin` không phải gitlab.com) — và chỉ khi
   project **chưa** đăng ký trên server.
4. Một bước trượt **không** dừng cả skill (trừ bước 2): ghi kết quả bước đó, làm tiếp bước sau, gom vào
   bảng report bước 9.

Kết quả mỗi bước là một trong: `ok` (đã đúng sẵn) · `created` (tạo mới) · `updated` (sửa giá trị lệch) ·
`skipped (<lý do>)` · `cần dev làm: <việc cụ thể>`.

**Biến dùng xuyên suốt:**

| Biến | Lấy từ |
|---|---|
| `<endpoint>` | env `BUGHUB_ENDPOINT` nếu có; không thì dòng `DEFAULT_ENDPOINT=` trong `.claude/scripts/bughub-wait.sh` (đọc file, đừng gõ theo trí nhớ); file đó không có → `https://bug-reporter.developer-a1f.workers.dev`. Bỏ `/` cuối. Phải khớp `^https://[A-Za-z0-9.-]+(:[0-9]+)?$` (http chỉ cho `localhost`/`127.0.0.1`), sai → dừng, báo giá trị lạ. Host khác `bug-reporter.developer-a1f.workers.dev` → nêu rõ host trong mọi lệnh in ra cho dev (lệnh `claude mcp add` dẫn tới bước đăng nhập Google — dev phải thấy mình đang đăng nhập vào đâu) |
| `<code>` | bước 1 |

Lệnh trong skill viết cho bash. Windows chạy chúng trong Git Bash, thay `python3` bằng `py`; bước nào có
lệnh PowerShell riêng thì ghi kèm.

---

## Bước 1 — Mã project

```bash
python3 .claude/scripts/project_profile.py bugHub      # → {"projectCode": "..."}
```

- `projectCode` có giá trị → dùng làm `<code>`. Kiểm khớp regex `^[A-Za-z0-9_-]+$` (cùng luật với
  `bughub-wait.sh`); sai → dừng bước này, `cần dev làm: sửa bugHub.projectCode`.
- Rỗng (hoặc script `project_profile.py` không có) → **đề xuất** mã theo codename project: đoạn cuối của
  path `origin` (bỏ `.git`), không có `origin` thì tên thư mục repo — viết HOA (`sm007` → `SM007`). Nếu bước
  5 tìm thấy asset `BugLoggerSettings` đã có `projectCode` khác rỗng thì đề xuất giá trị đó. Hỏi dev xác nhận
  hoặc gõ mã khác; validate cùng regex. Không tự chốt khi dev chưa trả lời.
- Profile và asset `BugLoggerSettings` mang hai mã **khác nhau** → hỏi dev chọn mã nào, không tự đè.

Server so mã không phân biệt hoa thường; luôn ghi dạng dev đã xác nhận.

## Bước 2 — MCP `bughub` đã kết nối? (bước duy nhất được dừng cả skill)

Tìm tool `bughub_whoami` trong danh sách tool (tool deferred → ToolSearch query `bughub`). Không có, hoặc
gọi `bughub_whoami` trả lỗi xác thực / 401 → **dừng skill**, in đúng:

```
claude mcp add --transport http --scope user bughub <endpoint>/mcp
```

rồi: trong Claude Code gõ `/mcp` → chọn `bughub` → **Authenticate** (đăng nhập Google tài khoản
`@easygoing.vn`), xong chạy lại `/bughub-setup`. Thêm MCP xong phải mở lại session Claude Code thì tool mới
xuất hiện. Không tự chạy `claude mcp add` thay dev (sửa config user-level của máy dev).

Lỗi mạng / 5xx (không phải lỗi xác thực) → cũng dừng, báo server không trả lời, kèm lỗi nguyên văn.

## Bước 3 — Project trên server

`bughub_whoami` → `{email, person, isAdmin}`.

### 3a. Admin (`isAdmin = true`)

1. `bughub_admin_projects` → tìm dòng có mã trùng `<code>` (so không phân biệt hoa thường).
2. **Đã có** → `bughub_admin_project_doctor(code: <code>)` (chỉ đọc).
   - Mọi bước doctor `ok` và dòng project có intake bật → kết quả `ok`. **Không** gọi upsert, **không** hỏi dev gì.
   - Có bước lệch / `error` → gọi lại `bughub_admin_project_upsert` với `code`, `gitlabProject`,
     `forumChannelId` (và `feedbackChannelId` nếu có) **lấy từ dòng project hiện có** — không hỏi lại dev —
     và `intakeEnabled` **giữ nguyên giá trị hiện có**. Thiếu giá trị nào trong dòng đó mới hỏi dev đúng giá
     trị đó. In nguyên bảng kết quả. Kết quả `updated` (hoặc `cần dev làm` nếu còn `error`, xem bảng lỗi dưới).
   - Intake đang **tắt** → có thể admin khác cố ý tắt (spam, hết quota). Hỏi dev có bật lại không; đồng ý mới
     upsert với `intakeEnabled: true` (giá trị khác lấy từ dòng hiện có như trên), không thì kết quả
     `cần dev làm: intake <code> đang tắt — bật trong /admin khi sẵn sàng`.
3. **Chưa có** → gom input rồi `bughub_admin_project_upsert`:
   - `gitlabProject` — suy từ `git remote get-url origin`:
     ```bash
     git remote get-url origin 2>/dev/null | python3 -c "import re,sys; u=sys.stdin.read().strip(); m=re.match(r'^(?:https?://(?:[^@/]+@)?gitlab\.com/|git@gitlab\.com:|ssh://git@gitlab\.com/)(.+?)(?:\.git)?/?$', u); print(m.group(1) if m else '')"
     ```
     In ra `group/project` (vd `ezg-sm-space/sm007`). Rỗng (không có `origin`, hoặc `origin` không phải
     gitlab.com) → hỏi dev đường dẫn project GitLab dạng `group/project`. Giá trị (suy ra hay dev gõ) phải
     khớp `^[A-Za-z0-9_.-]+(/[A-Za-z0-9_.-]+)+$` và không chứa `..`; sai → hỏi lại một lần.
   - `forumChannelId` — **hỏi dev** ID forum Discord của project (chuột phải forum → Copy Channel ID, cần
     bật Developer Mode). Validate `^[0-9]{17,20}$`; sai → hỏi lại một lần.
   - `feedbackChannelId` — hỏi dev, tuỳ chọn (Enter để bỏ qua = feedback vào kênh report mặc định).
     Có giá trị thì validate như trên.
   - Gọi `bughub_admin_project_upsert(code, gitlabProject, forumChannelId, feedbackChannelId?, intakeEnabled: true)`
     (kèm `members` nếu dev đã nêu thành viên — mục 5).
   - **In nguyên bảng kết quả từng bước** server trả về (`ok | created | fixed | error: <lý do>`) — không
     tóm tắt, không bỏ dòng. Mọi dòng `ok/created/fixed` → kết quả `created`.
4. Sau 2/3: `GET /form` (lệnh ở 3b) phải ra `200` với `intakeEnabled` true (trừ khi dev vừa chọn để intake
   tắt). Không → ghi lại trong report.
5. **Thành viên** — chỉ khi dev đã nêu trong lệnh/chat ("A làm lead, B QA, C dev"); không nêu thì bỏ qua,
   **không hỏi**. Truyền `members: [{who, roles}]` vào cùng lần `bughub_admin_project_upsert` của mục 2/3
   (project đã có và doctor `ok` → gọi riêng `bughub_admin_project_upsert(code, members)`). `who` = đúng chữ
   dev gõ (tên, email, phần trước `@`, Discord id); `roles` ⊂ `lead | dev | qa | art | gd`, thay vai trò của
   người đó **trong project này** (`[]` = gỡ khỏi project), người không nêu giữ nguyên. Server tra danh bạ
   và kiểm quyền **trước** khi ghi gì:
   - `unknown_person` → `bughub_admin_people_list(query: <who>)` tìm gần đúng, đưa dev chọn; không có ai →
     `cần dev làm: owner BugHub thêm <who> vào danh bạ (bughub_admin_people_sync)`.
   - `ambiguous_person` → in `candidates` (tên + email), hỏi dev chọn rồi gọi lại với email người đó.
   - `inactive_person` → `cần dev làm: bật lại <tên> (bughub_admin_person_upsert active: true)`.
   In bảng `members[]` kết quả (`added | changed | removed | unchanged | error`). Kết quả bước: `updated` nếu
   có người thay đổi.

**Dòng `error` trong bảng upsert** — server chỉ bật intake khi bước 1–4 không lỗi, nên đây là việc tay:

| Lỗi ở bước | Kết quả ghi `cần dev làm:` |
|---|---|
| 1 (resolve GitLab / quyền) | thêm tài khoản bot BugHub làm **Maintainer** của project GitLab `<gitlabProject>`, rồi chạy lại skill |
| 2–3 (label / webhook) | thường cùng nguyên nhân quyền Maintainer ở trên |
| 4 (Discord) | cấp cho bot trên forum: View Channel, Send Messages, Send Messages in Threads, Create Public Threads, Attach Files, Manage Threads; kiểm đúng ID forum; forum còn chỗ cho tag status (tối đa 20 tag) |
| `not_admin` (tool result `isError`) | tài khoản vừa bị gỡ quyền admin → làm như 3b |
| `forbidden` `people.assign-lead` | chỉ owner/admin gán hoặc gỡ vai trò `lead` — nhờ admin BugHub |

### 3b. Không phải admin

Không gọi bất kỳ tool `bughub_admin_*` nào. Chỉ kiểm:

```bash
TMP=$(mktemp)
HTTP=$(curl -sS --max-time 15 -o "$TMP" -w '%{http_code}' "<endpoint>/v1/projects/<code>/form")
INTAKE=$(python3 -c "import json,sys; print(str(json.load(open(sys.argv[1])).get('intakeEnabled')).lower())" "$TMP" 2>/dev/null)
rm -f "$TMP"; echo "$HTTP intake=$INTAKE"
```

| Kết quả | Ghi |
|---|---|
| `200 intake=true` | `ok` |
| `200 intake=false` | `cần dev làm: nhờ admin BugHub chạy /bughub-setup (hoặc trang /admin) để bật intake cho <code>` |
| `404` | `cần dev làm: project <code> chưa đăng ký hoặc đang tắt intake (server trả 404 cho cả hai) — nhờ admin BugHub chạy /bughub-setup trên repo này` (gửi kèm `gitlabProject` suy ở 3a + ID forum Discord) |
| khác / lỗi mạng | `skipped (server không trả lời: <mã/lỗi>)` |

## Bước 4 — Package `com.ezg.buglogger`

```bash
[ -f Packages/com.ezg.buglogger/package.json ] && echo "embedded"
grep -q '"com.ezg.buglogger"' Packages/manifest.json && echo "manifest"
```

- Một trong hai có → `ok`.
- Không có:
  1. Tìm trên Feature Hub: MCP `ezg` → `search_feature_hub` (query `buglogger`).
  2. Có → `get_install_steps` cho package đó. Kiểm snippet `install` đúng là cài `com.ezg.buglogger` (nhắc
     đúng package id đó, không làm việc gì khác), cho dev xem và xác nhận một dòng, rồi mới chạy bằng
     `unity_execute_code` (cần Editor của project này mở, xem bước 5 cách chọn instance); xác nhận bằng
     snippet `verify`. Kết quả
     `created`. Không có Editor → in các bước cài, kết quả `cần dev làm: cài com.ezg.buglogger theo các bước trên`.
  3. Chưa publish (không tìm thấy) → `cần dev làm: com.ezg.buglogger chưa publish lên Feature Hub — publish
     bằng /package-module hoặc copy package embedded từ project đã có`. Bước 5 sẽ `skipped`.

Không tự sửa `Packages/manifest.json` bằng tay.

## Bước 5 — Asset `BugLoggerSettings`

Client nạp asset bằng `Resources.Load("BugLoggerSettings")`, nên asset phải tên đúng `BugLoggerSettings.asset`
và nằm **trực tiếp** trong một thư mục `Resources/`. Field serialize `projectCode` là contract của package.

**Đường dẫn mặc định khi phải tạo mới** (`<featuresRoot>` = `python3 .claude/scripts/project_profile.py featuresRoot`):
`<featuresRoot>/_Shared/BugLogger/Resources/BugLoggerSettings.asset` nếu thư mục `<featuresRoot>/_Shared/BugLogger/`
đã có (project cũ còn tự dựng màn Bug Logger ở đó — từ `com.ezg.buglogger` 0.2.0 UI nằm trong package), không thì
`Assets/Resources/BugLoggerSettings.asset`. Path này được
nhét vào chuỗi C# ở 5a nên phải khớp `^Assets/[A-Za-z0-9_./ -]+/Resources/BugLoggerSettings\.asset$`;
không khớp → dùng 5b.

### 5a. Có Unity Editor của project này (đường chính)

1. `unity_list_instances` → chọn instance có `projectPath` **trùng thư mục repo này** (`git rev-parse --show-toplevel`);
   so path, không so tên — port của Editor đã đóng có thể bị project khác chiếm. Không có instance nào khớp → 5b.
2. **Editor dùng chung session khác?** `unity_agents_list`. Có agent khác đang hoạt động → vẫn chạy được
   (tạo/sửa một asset `.asset` không import script, không domain reload), nhưng **không** dừng Play mode của
   họ, và ghi vào report là Editor đang dùng chung. `unity_editor_state` đang compile → chờ tới khi xong.
   Snippet cố ý **không** gọi `AssetDatabase.Refresh()` (Refresh nhập luôn `.cs` session khác đang sửa dở →
   compile + domain reload): thư mục tạo bằng `AssetDatabase.CreateFolder`, asset bằng `CreateAsset`.
3. Chạy `unity_execute_code` với snippet dưới, thay `__CODE__` và `__PATH__`. **Payload chỉ được chứa ký tự
   ASCII** (Unity MCP làm hỏng ký tự không phải ASCII) — mã project và path đều phải ASCII, comment trong
   snippet cũng vậy; path có ký tự lạ → đi 5b.

```csharp
// bughub-setup step 5: create or update Resources/BugLoggerSettings.asset (ASCII only)
const string CODE = "__CODE__";
const string DEFAULT_PATH = "__PATH__";
System.Type type = null;
foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
{
    type = asm.GetType("Ezg.BugLogger.BugLoggerSettings");
    if (type != null) break;
}
if (type == null) return "error: type Ezg.BugLogger.BugLoggerSettings not found (package missing or not compiled)";

var inResources = new System.Collections.Generic.List<string>();
var elsewhere = new System.Collections.Generic.List<string>();
foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:" + type.Name))
{
    var p = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
    if (p.EndsWith("/Resources/BugLoggerSettings.asset")) inResources.Add(p); else elsewhere.Add(p);
}
if (inResources.Count > 1) return "error: duplicate settings assets: " + string.Join(", ", inResources);

string path;
string result;
UnityEngine.Object asset;
if (inResources.Count == 1)
{
    path = inResources[0];
    asset = UnityEditor.AssetDatabase.LoadAssetAtPath(path, type);
    if (asset == null) return "error: cannot load " + path;
    result = "ok";
}
else
{
    path = DEFAULT_PATH;
    var parts = path.Split('/');
    var folder = parts[0];
    for (int i = 1; i < parts.Length - 1; i++)
    {
        var next = folder + "/" + parts[i];
        if (!UnityEditor.AssetDatabase.IsValidFolder(next)) UnityEditor.AssetDatabase.CreateFolder(folder, parts[i]);
        folder = next;
    }
    asset = UnityEngine.ScriptableObject.CreateInstance(type);
    UnityEditor.AssetDatabase.CreateAsset(asset, path);
    result = "created";
}

var so = new UnityEditor.SerializedObject(asset);
var prop = so.FindProperty("projectCode");
if (prop == null) return "error: field projectCode not found on " + type.FullName;
if (prop.stringValue != CODE)
{
    prop.stringValue = CODE;
    so.ApplyModifiedPropertiesWithoutUndo();
    UnityEditor.EditorUtility.SetDirty(asset);
    if (result == "ok") result = "updated";
}
UnityEditor.AssetDatabase.SaveAssets();
var warn = elsewhere.Count > 0 ? " | warn: settings outside Resources (not loaded at runtime): " + string.Join(", ", elsewhere) : "";
return result + " " + path + " projectCode=" + CODE + warn;
```

| Kết quả trả về | Ghi |
|---|---|
| `ok …` / `updated …` / `created …` | đúng chữ đó + path. `created`/`updated` → nhắc dev commit asset (và `.meta` khi `created`) |
| `error: … not found (package …)` | `skipped (package chưa compile — xem bước 4)` |
| `error: duplicate …` | `cần dev làm: xoá bớt, chỉ giữ một Resources/BugLoggerSettings.asset` — không tự xoá |
| `error: cannot load …` / `error: field projectCode …` | `cần dev làm: kiểm asset <path> (script reference hỏng?)` — không tự xoá/tạo lại |
| `… warn: settings outside Resources …` | giữ kết quả chính, nêu cảnh báo trong report — không tự xoá/di chuyển |

Chạy lại snippet trên asset đã đúng → trả `ok`, không tạo asset thứ hai, không ghi file.

### 5b. Không có Editor (MCP không kết nối / Editor chưa mở / chỉ có Editor của project khác)

1. Tìm asset sẵn có theo GUID script (không cần Editor):
   ```bash
   META=$(ls Packages/com.ezg.buglogger/Runtime/Settings/BugLoggerSettings.cs.meta \
            Library/PackageCache/com.ezg.buglogger*/Runtime/Settings/BugLoggerSettings.cs.meta 2>/dev/null | head -1)
   GUID=$([ -n "$META" ] && sed -n 's/^guid: //p' "$META")
   if [ -z "$GUID" ]; then echo "NO-META"; else grep -rl --include='*.asset' "guid: $GUID" Assets; fi
   ```
2. Tìm thấy đúng một asset tên `BugLoggerSettings.asset` trong thư mục `Resources/`:
   - Dòng `  projectCode: <code>` đã đúng → `ok`.
   - Lệch → sửa **đúng dòng đó** trong YAML (không đụng dòng khác), kết quả `updated`:
     ```bash
     python3 - "<asset path>" "<code>" <<'PY'
     import re, sys
     path, code = sys.argv[1], sys.argv[2]
     text = open(path, encoding="utf-8").read()
     new, n = re.subn(r"(?m)^(  projectCode:).*$", lambda m: m.group(1) + " " + code, text, count=1)
     if n != 1:
         sys.exit("projectCode line not found")
     open(path, "w", encoding="utf-8", newline="").write(new)
     PY
     ```
3. Không tìm thấy (hoặc in `NO-META` — không tìm được `.cs.meta`) → **không** tự viết asset YAML mới. Kết quả
   `cần dev làm: mở Editor rồi chạy lại /bughub-setup, hoặc tạo tay: Assets > Create > Ezg > Bug Logger Settings
   tại <đường dẫn mặc định>, đặt Project Code = <code>`. Nhiều hơn một asset → như dòng `duplicate` ở 5a.

## Bước 6 — `bugHub.projectCode` trong `.claude/project-profile.json`

File profile có format viết tay (dòng trống giữa nhóm, mảng gọn) — **không** `json.dump` lại cả file. Sửa
đúng chỗ, rồi kiểm lại bằng `json.loads`:

```bash
python3 - .claude/project-profile.json "<code>" <<'PY'
import json, os, re, sys
path, code = sys.argv[1], sys.argv[2]
text = open(path, encoding="utf-8").read() if os.path.exists(path) else "{\n}\n"
data = json.loads(text)
hub = data.get("bugHub")
if isinstance(hub, dict) and hub.get("projectCode") == code:
    print("ok"); sys.exit(0)
quoted = json.dumps(code)
m = re.search(r'"bugHub"\s*:\s*\{[^{}]*\}', text)
if m:
    block = m.group(0)
    if re.search(r'"projectCode"\s*:', block):
        block = re.sub(r'("projectCode"\s*:\s*)"[^"]*"', lambda x: x.group(1) + quoted, block, count=1)
    else:
        block = re.sub(r'\{', '{\n    "projectCode": ' + quoted + ',', block, count=1)
        block = re.sub(r',(\s*)\}$', r'\1}', block)
    new, result = text[:m.start()] + block + text[m.end():], "updated"
else:
    body = text.rstrip()
    if not body.endswith("}"):
        sys.exit("error: profile is not a JSON object")
    inner = body[:-1].rstrip()
    lead = "\n" if inner.endswith("{") else ",\n\n"
    new = inner + lead + '  "bugHub": {\n    "projectCode": ' + quoted + '\n  }\n}\n'
    result = "created" if os.path.exists(path) else "created (new file)"
check = json.loads(new)
if check.get("bugHub", {}).get("projectCode") != code:
    sys.exit("error: edit did not produce bugHub.projectCode")
open(path, "w", encoding="utf-8", newline="").write(new)
print(result)
PY
```

In ra `ok` / `updated` / `created` → ghi đúng chữ đó. `error: …` → không ghi file (script thoát trước khi
ghi), kết quả `cần dev làm: thêm "bugHub": {"projectCode": "<code>"} vào project-profile.json`.
Kiểm lại: `python3 .claude/scripts/project_profile.py bugHub` phải in đúng `<code>`.

## Bước 7 — Cheat cho máy QA (ai thấy Bug Logger)

Bug Logger có trong **mọi** bản build; nút nổi (UI của package, `BugLoggerUI.LauncherVisible`) chỉ hiện khi game
bật nó theo cheat (`GameSystems.isCheat` — template gán trong `DebugToolsCheatGate`). Cheat bật được ở mọi bản build khi Remote Config cho phép đúng máy đó.
Kiểm gate cheat của project còn chặn bản không phải development build không:

```bash
f=$(grep -rl "static bool CanEnableCheatFromRemoteConfig" Assets --include="*.cs" | head -1)
[ -n "$f" ] || echo "NO-GATE"
[ -n "$f" ] && awk '/static bool CanEnableCheatFromRemoteConfig/{on=1} on&&/^#if/{print "GATED: " $0; exit} on&&/^        }/{exit}' "$f"
```

| Tình huống | Kết quả |
|---|---|
| `NO-GATE` | `cần dev làm: project không có CanEnableCheatFromRemoteConfig — tự quyết cách bật icon Bug Logger cho máy QA` |
| có dòng `GATED: #if …DEVELOPMENT_BUILD…` | `cần dev làm: gate cheat chỉ mở ở Editor/development build — bản CI (không development) sẽ không bao giờ hiện Bug Logger; bỏ #if đó trong <f> (TechSpec BugHub D5)` |
| không in gì | `ok` |

Skill **không** tự sửa gate (đụng cheat của bản store, cần dev quyết). Luôn in hướng dẫn thêm máy QA:

1. Firebase Console → Remote Config: `enable_cheat_default` = `true`.
2. `enable_cheat_default_device` = `{"devices":["<deviceId máy QA>", …]}` — deviceId = `SystemInfo.deviceUniqueIdentifier`
   (xem trong game: Settings / màn cheat; hoặc log `DeviceId` lúc boot).
3. Chỉ đưa **máy nội bộ** vào danh sách — máy nào trong đó cũng thấy cheat, kể cả trên bản store.

## Bước 8 — `/auto-clear` cho `/fix-bug --watch`

`/fix-bug --watch` cần Stop hook của `/auto-clear` (một lần mỗi máy; macOS dùng iTerm2):

```bash
bash .claude/skills/auto-clear/scripts/auto-clear.sh status
# Windows: powershell -ExecutionPolicy Bypass -File .claude/skills/auto-clear/scripts/auto-clear.ps1 status
```

| Dòng in ra | Kết quả |
|---|---|
| `HOOK installed …` | `ok` |
| `HOOK not-installed` / `HOOK broken …` | `cần dev làm: /auto-clear install (một lần trên máy này)` |
| `UNSUPPORTED …` (không chạy trong iTerm2 / Windows Terminal) | `cần dev làm: chạy /fix-bug --watch trong iTerm2 (macOS) rồi /auto-clear install` |
| script không có | `cần dev làm: cài skill auto-clear từ Feature Hub (tab AI Feature)` |

Không tự chạy `install` — nó sửa `~/.claude/settings.json` của dev.

Nhánh của cửa sổ watch: `python3 .claude/scripts/bughub-watch.py config` → `mode: "follow"` (`branch` rỗng,
mặc định) thì chạy watch ở nhánh nào sửa + push lên nhánh đó. Không phải lỗi, không thành dòng trong bảng — chỉ
thêm vào danh sách việc dev: "mở pane riêng ở nhánh muốn nhận fix rồi gõ `/fix-bug --watch`; muốn bot chạy trên
nhánh riêng thì khai `bugHub.watch` (branch / baseBranch / mergeToBase / worktree) trong
`.claude/project-profile.json`". Không tự chọn nhánh hay worktree thay dev.

## Bước 9 — Report

Một bảng, đủ 8 dòng, rồi danh sách việc dev phải làm (nếu có):

```
| Bước | Kết quả |
|---|---|
| 1. Mã project | ok — SM007 |
| 2. MCP bughub | ok — <email> (admin|dev) |
| 3. Project trên server | created | ok | updated | cần dev làm: … |
| 4. Package com.ezg.buglogger | ok (embedded|manifest) | … |
| 5. BugLoggerSettings | ok | created | updated — <path> | … |
| 6. project-profile.json | ok | created | updated |
| 7. Cheat máy QA | ok | cần dev làm: … |
| 8. auto-clear | ok | cần dev làm: … |
```

- Bước 3 vừa gọi upsert → dán nguyên bảng server trả về dưới bảng trên.
- Bước 7 → luôn kèm 3 dòng hướng dẫn thêm máy QA vào allowlist cheat.
- File đã đổi (asset + `.meta` khi tạo mới, `project-profile.json`) liệt kê dạng link tuyệt đối `file://`
  theo `.claude/rules/output-format.md`. Skill **không** commit — dev tự commit (vd `/push-in-session`).
- Mọi bước `ok` → một dòng cuối: "BugHub đã sẵn sàng cho `<code>` — QA (máy trong allowlist cheat) gửi bug từ mọi bản build, dev
  sửa bằng `/fix-bug`."
