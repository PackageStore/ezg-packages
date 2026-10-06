---
name: setup-project
description: Setup một project Unity vừa sinh từ template — mở cửa sổ Ezg > EzgKit (menu trái là các mục setup có trạng thái, form bên phải để điền) rồi làm phần cần AI mà form không tự làm được (soạn ArtStyle.md từ art của game, kiểm tra link localize). Dùng khi dev gõ /setup-project, nói "setup project mới", "điền bundle id / marketing / ads / localize cho project", hoặc hỏi project còn thiếu setup gì. Chạy tay, KHÔNG đi qua backlog.
---

# /setup-project — setup project mới qua cửa sổ EzgKit

Nguồn sự thật của setup là **cửa sổ `Ezg > EzgKit`** (package `com.ezg.ezgkit` ≥ 1.0). Dev điền field trong
cửa sổ; skill này chỉ **mở cửa sổ đúng mục, đọc trạng thái, và làm phần cần AI**. Không dựng lại form trong
chat, không tự điền giá trị thay dev khi dev chưa đưa giá trị.

## Các mục setup (trang của cửa sổ)

| id | Trang | Dev điền gì | Phần AI (skill này) |
|----|-------|-------------|---------------------|
| `overview` | Tổng quan | — | đọc trạng thái, tóm tắt việc còn thiếu |
| `project` | Thông tin dự án | tên dự án, company, product name, bundle id Android/iOS | — |
| `marketing` | Marketing & AppSecrets | link Google Sheet marketing (+ prefix), xem diff rồi Áp dụng; AppsFlyer/iOS App ID/URL privacy-terms/Discord | — |
| `ads` | Ads & Privacy | MAX key + ad unit, debug ads, consent, ATT, đối tượng người chơi | — |
| `iap` | Gói bán (IAP) | chỉ xem danh sách gói + cảnh báo; tạo/sửa gói bằng MCP gói bán | — |
| `artstyle` | ArtStyle | thư mục art cho board, bấm "Nhờ Claude soạn" | **soạn `.claude/docs/ArtStyle.md`** (STEP 4) |
| `localize` | Localization | link file localize, service account, tab | kiểm tra link đọc được, tải thử |
| `firebase`, `publisher`, `social` | Nhóm "Nâng cao" | tạo app Firebase, đổi nhà phát hành, link social | — |

## Tham số

- `/setup-project` — mở cửa sổ ở **Tổng quan** + in trạng thái.
- `/setup-project <id>` — mở thẳng trang đó (vd `/setup-project localize`).
- `/setup-project status` — chỉ in trạng thái, không mở cửa sổ.
- `/setup-project all` — mở cửa sổ và bấm "Setup tất cả" (cửa sổ tự đi lần lượt qua các trang chưa xong).
- `/setup-project artstyle` — làm luôn phần AI của ArtStyle (STEP 4).

## STEP 0 — Kết nối đúng Editor

1. `unity_list_instances` → chọn instance có `projectPath` == repo root hiện tại. **Không bao giờ** gọi tool
   Unity vào instance của project khác; mọi lệnh sau đều truyền `port` của instance này.
2. Không có instance nào của project này:
   - Mở Editor bằng launcher ở repo root: `bash ./<projectName>.command` (macOS) / `<projectName>.bat`
     (Windows); `projectName` lấy từ `python3 .claude/scripts/project_profile.py projectName`.
   - Chờ tới khi `unity_list_instances` thấy instance và `unity_editor_state` báo `isCompiling: false`.
   - Không có Unity MCP: nói dev tự mở `Ezg > EzgKit` trong Editor, dừng ở đây.

## STEP 1 — Kiểm tra EzgKit ≥ 1.0

Gọi API qua reflection (snippet của `unity_execute_code` không thêm được `using`, và không phải project nào
cũng reference assembly `Ezg.EzgKit.Editor`):

```csharp
// unity_execute_code (port của project) — đổi dòng cuối theo việc cần gọi
var api = System.AppDomain.CurrentDomain.GetAssemblies()
  .Select(a => a.GetType("Ezg.EzgKit.EzgKitApi", false)).FirstOrDefault(x => x != null);
if (api == null) return "NO_API";
return (string)api.GetMethod("GetStatusJson").Invoke(null, null);
```

API (namespace `Ezg.EzgKit`, class tĩnh `EzgKitApi`; mọi hàm trả JSON string, lỗi → `{"ok":false,"error":…}`):

| Hàm | Dùng để |
|-----|---------|
| `GetStatusJson()` | trạng thái mọi trang + `requests` |
| `GetPageValuesJson(pageId)` | giá trị hiện tại của một trang (secret đã che) |
| `Apply(pageId, valuesJson, dryRun)` | ghi giá trị (`dryRun: true` chỉ trả diff `rows[]`) — trang `project`, `marketing`, `ads`, `localize`, `artstyle`, `social` |
| `SetMarker(pageId, marker)` | `done` / `deferred` / `na` / `""` (bỏ marker) |
| `ClearRequest(id)` | gỡ yêu cầu "Nhờ Claude…" đã xử lý |
| `Open(pageId)` / `SetupAll()` | mở cửa sổ ở trang / chạy luồng "Setup tất cả" (void) |

- `NO_API` → kiểm `Packages/manifest.json` key `com.ezg.ezgkit`. Bản < 1.0.0 → báo dev cập nhật qua
  `Ezg > Feature Hub` (tab Unity Packages) hoặc sửa version trong manifest, rồi chạy lại. Có bản ≥ 1.0 mà vẫn
  `NO_API` → `unity_get_compilation_errors` (có lỗi compile thì sửa/hỏi trước, cửa sổ chỉ chạy khi compile sạch).
- **Không** gọi `InternalEditorUtility.ReadScreenPixel` hay tool chụp màn hình Editor qua `unity_execute_code`
  — trên macOS nó crash Editor. Cần nhìn cửa sổ thì nhờ dev chụp, hoặc dùng `screencapture -l <windowId>` của OS.

## STEP 2 — Mở cửa sổ

- `/setup-project all` → `EzgKitApi.SetupAll()` (mở cửa sổ, nhảy tới trang Setup đầu tiên còn việc; mỗi trang
  có nút "Tiếp →").
- Có id → `EzgKitApi.Open("<id>")`. Không có → `EzgKitApi.Open("overview")`.
- Fallback khi không gọi được API: `unity_execute_menu_item("Ezg/EzgKit")`.

## STEP 3 — In trạng thái

Từ JSON của `GetStatusJson()` (`pages[]`: `id`, `title`, `group` = overview/setup/advanced, `status`, `marker`,
`summary`, `todos[]{level,text,fix}`; `progress{done,total}` chỉ đếm nhóm setup) in **một** bảng: trang |
trạng thái (`done` ✅ · `partial` 🟡 · `todo` ⬜ · `deferred` ⏸ · `na` ➖ · `error` ⚠ · `info` ℹ) | việc còn
thiếu (`todos`, tối đa 3 dòng/trang, ưu tiên `level: error`). Sau bảng nói ngắn: "Điền trong cửa sổ
EzgKit; xong mục nào nó tự đổi trạng thái." Không lặp lại hướng dẫn mà cửa sổ đã hiển thị.

Dev đưa giá trị ngay trong chat ("bundle id là com.x.y") → **dry-run trước**:
`EzgKitApi.Apply("<page>", "<json values>", true)` → in diff → dev đồng ý → `Apply(..., false)`. Không bao giờ ghi
secret (token, key bí mật, mật khẩu keystore) vào chat, commit hay file state — chúng chỉ đi vào field của cửa sổ.

## STEP 4 — Phần AI theo `requests`

`GetStatusJson()` có mảng `requests` (cửa sổ thêm vào khi dev bấm "Nhờ Claude…"). Xử lý từng cái, xong gọi
`EzgKitApi.ClearRequest("<id>")`:

- **`artstyle`** — làm đúng **§ Bootstrap** trong `.claude/docs/ArtStyle.template.md` (đọc file đó trước):
  1. Đọc `ArtStyle.md` hiện có (không có → copy từ template). Status `approved` → hỏi dev trước khi sửa.
  2. Nguồn art = các thư mục dev khai trong block `art-style-boards` (trang ArtStyle của cửa sổ ghi vào đó).
     Hỏi dev nếu block rỗng. **Pack GUI mua sẵn của template không mặc nhiên là art của game** — hỏi dev nếu
     chỉ có pack đó.
  3. Chạy `python3 .claude/scripts/art-style-board.py` (cần Pillow; psd-tools cho PSD), `Read` các board PNG,
     đo màu bằng Pillow, đọc font/cỡ chữ từ prefab Text template.
  4. Điền §1–§6 + §11 mặc định, `Status: draft`, ghi ngày. Theo rule `art-style.md` + `no-external-game-refs.md`
     (tả style bằng lời trung tính).
  5. Báo dev: file cần duyệt, những gì đã suy ra và những gì còn trống.
- **`localize`** — đọc `localize.sheet` trong `.claude/project-profile.json`: tải bản CSV export (như trang
  Localization làm) xác nhận đọc được + liệt kê tab/ngôn ngữ thấy được; lỗi quyền thì nói dev share sheet
  "Anyone with the link" (đọc) và cho `client_email` của service account quyền Editor (ghi qua `/add-localize`).

## STEP 5 — Kết thúc

In lại bảng trạng thái (STEP 3) sau khi xử lý. Mục chờ người (👤: store console, Firebase console, keystore…)
liệt kê cuối cùng kèm trang tương ứng của cửa sổ. Không commit — dev tự commit khi xong.

## Không làm

- Không sửa prefab/scene/code của game trong skill này (setup chỉ ghi config/asset cấu hình qua EzgKit).
- Không gọi `Apply` khi dev chưa xác nhận diff; không bấm "Chuyển nhà phát hành" thay dev.
- Không chạy qua `/planning-task` / `/run-backlog` (out-of-band như `/auto-build-setup`).
