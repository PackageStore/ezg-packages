# EzgKit — cửa sổ tool tổng của dự án

`Ezg > EzgKit`

Một cửa sổ, mỗi tool là một tab ở cột dọc bên trái. Tool dùng chung cho MỌI dự án (đi qua branch
`code-template`); dữ liệu của từng dự án nằm ngoài `Assets/`, trong `ProjectSettings/`.

## Bố cục

```
┌──────────────────────────────────────────────────────────────────┐
│ EzgKit · <productName>                    [Làm mới trạng thái]   │  ← header bar
│ Android <package>  ·  iOS <bundle>  ·  v<version>                │     luôn hiện
├───────────────┬──────────────────────────────────────────────────┤
│ Tổng quan     │  Marketing                       [ Còn việc ]    │  ← page header
│ 1. Marketing ⚠│  Tải bảng thông số từ Google Sheet…               │     do cửa sổ vẽ
│ 2. Firebase  ✓│  ──────────────────────────────────────────────  │
│               │  (phần thân do page vẽ)                          │
│ Mở cửa sổ chỉ │                                                  │
│ đọc, không ghi│                                                  │
└───────────────┴──────────────────────────────────────────────────┘
```

**Header bar không cuộn.** Dòng id là id THẬT sẽ đi vào bản build: Marketing ghi nó vào
PlayerSettings, Firebase tạo app theo nó. Mọi tab đều phụ thuộc hai dòng đó nên nó nằm ở đỉnh cửa sổ
chứ không nằm riêng trong một tab — id rỗng được tô vàng ngay.

**Cột nav xếp dọc chứ không ngang:** danh sách tool còn dài ra, tab ngang thêm vài mục nữa là chữ bị
bóp lại. Mỗi mục mang icon trạng thái của tab đó, tooltip là dòng trạng thái đầy đủ — nhìn cột trái
là biết phải vào tab nào mà không cần mở từng tab.

**Cửa sổ vẽ phần đầu trang, page không vẽ lại.** Tiêu đề + một dòng mô tả + chip trạng thái đều do
`EzgKitWindow` vẽ từ `IEzgKitPage`. Nhờ vậy ba tab có đúng một kiểu phần đầu, và chip trạng thái
luôn nằm cùng một chỗ.

| Tab | Việc | Code |
|---|---|---|
| Tổng quan | checklist theo bước + nút chạy hết | `EzgKitWindow.cs` |
| Marketing | Google Sheet → PlayerSettings / AdsConfig / AppLovinSettings / FacebookSettings / AndroidManifest / GameConstant | `../Marketing/` ([README](../Marketing/README.md)) |
| Firebase | chọn file service account `.json` → tạo app Android + iOS → tải `google-services.json` / `GoogleService-Info.plist` | `../Firebase/` |

### Tab Firebase: một file key là đủ

Thứ **duy nhất** bắt buộc là file service account `.json`. `project_id` nằm sẵn trong file đó nên
tool tự lấy — không phải gõ tay, và ô Project id được đánh dấu *(tự lấy)* chứ không phải *(bắt buộc)*.

- Service account được gán role trên project **khác** với project sinh ra key → tool **không** tự ghi
  đè, chỉ báo `File key thuộc project "X"` kèm nút **Dùng project của key**.
- Không nhớ project id → nút **Dò project khả dụng** gọi `GET /v1beta1/projects` bằng chính key đó
  rồi cho chọn trong danh sách. Dò ra đúng một project thì chọn luôn.
- Còn lại đều tự có: package name / bundle id đọc từ PlayerSettings (tab Marketing ghi vào), tên app
  mặc định theo Product Name.
- Hai thứ **không** suy ra được từ file key nên vẫn là tuỳ chọn thật, bỏ trống vẫn chạy: **Apple ID**
  (chỉ để Firebase link sang App Store) và **mật khẩu keystore** (chỉ để đăng ký SHA-1 — Analytics và
  Crashlytics không cần).

Đường dẫn file key nằm ở `EditorPrefs` theo máy, **không** vào `ProjectSettings/FirebaseSource.json`
— nó là secret theo máy chứ không phải cấu hình dùng chung. `FirebaseServiceAccount.TryLoad` từ chối
thẳng file key nằm trong repo.

## Ngôn ngữ hình ảnh dùng chung

Ba tab dùng chung một bộ style để không nhìn như ba tool khác nhau:

| File | Vai trò |
|---|---|
| `EzgKitStyles.cs` | màu, style, card, chip, icon trạng thái, `KeyValue` / `DiffRow` / `Divider` / nút |
| `SetupGui.cs` | widget nhập liệu dựng trên `EzgKitStyles` — ô điền tay, ô mật khẩu, ô chọn file, bước làm tay |
| `IEzgKitPage.cs` | contract của một tab |

**Màu chỉ mang nghĩa trạng thái**, không dùng để trang trí:

| | Nghĩa |
|---|---|
| xanh | khớp / đã xong |
| vàng | còn việc phải làm |
| đỏ | hỏng, chạy tiếp là sai |
| xám | không áp dụng — **không** phải lỗi |

Hai quy tắc bố cục còn lại: **việc cần làm nằm trên, thông tin tham khảo nằm dưới**; và **đoạn chữ
dài thì gấp lại** (nút *Lấy ở đâu?* của từng ô, khối *Vì sao…?* của từng trang). Hướng dẫn cần cho
lần đầu, nhưng từ lần thứ hai nó chỉ đẩy phần việc thật xuống dưới màn hình. Trạng thái gấp/mở nhớ
theo máy qua `EditorPrefs`.

**Mọi khối gấp được đi qua đúng một hàm: `EzgKitStyles.FoldoutHeader`** (`CardFoldout` và
`CollapsibleHelp` đều chỉ gọi lại nó). Cả hàng là vùng bấm được — tiêu đề, chữ đếm, icon trạng thái,
khoảng trống ở giữa — và con trỏ đổi thành hình bàn tay khi rê qua. Đừng ghép
`EditorGUILayout.Foldout` vào một hàng ngang cạnh `GUILayout.FlexibleSpace()`: FlexibleSpace nuốt hết
chỗ trống nên foldout co lại đúng bề rộng chữ, mọi thứ khác trên hàng thành vùng chết, và khối này
hành xử khác khối kia.

Bố cục hàng tiêu đề (giống nhau cho card gấp được lẫn card thường):
`▸ Tiêu đề  ····  chữ đếm  icon`. Icon sát mép phải để quét mắt dọc một đường thẳng là thấy khối nào
còn vấn đề, bất kể chữ đếm dài ngắn.

## Thứ tự có ý nghĩa

1. **Marketing** ghi `applicationIdentifier` (Android + iOS) vào PlayerSettings.
2. **Firebase** ĐỌC hai id đó để tạo app.

Firebase **không cho sửa** `packageName` / `bundleId` của app sau khi tạo, cũng không cho tái sử dụng
id của app đã xoá. Chạy ngược thứ tự nghĩa là có một app Firebase mang id cũ nằm đó vĩnh viễn. Vì thế
tab Tổng quan chạy đúng chiều Marketing → Firebase và dừng ngay ở bước đầu tiên báo lỗi.

## Mở cửa sổ không ghi gì

Mọi page chỉ chạy dry-run khi `Reload()` — mở cửa sổ, đổi tab, bấm *Làm mới trạng thái* đều là đọc.
Ghi chỉ xảy ra khi bấm nút chạy, và bước không hoàn tác được (tạo app Firebase) vẫn hỏi lại lần nữa.

Tab Firebase còn so `project_id` trong `google-services.json` đang có ở `Assets/` với project id đang
khai — bắt đúng ca config đi theo code-template của **dự án khác**: file vẫn tồn tại, build vẫn chạy,
số liệu bắn sang project người ta. Ca đó là **đỏ** và banner của nó nằm ngoài scroll để không cuộn mất.

## Thêm một tool setup mới

1. Implement `IEzgKitPage` (đặt file cạnh code của tool đó, không phải trong thư mục này):
   `Title` (ASCII), `Subtitle` (một câu "tab này để làm gì"), `Headline` (một dòng trạng thái),
   `Status` (`EzgStatus`), `RunAllLabel`, `Reload()`, `Draw()`, `RunAll()`.
2. **`Status` và `Headline` phải RẺ — chỉ đọc field đã chụp sẵn.** Cửa sổ đọc hai getter này **mỗi
   lượt OnGUI** của **mọi** tab (để vẽ icon cột nav, chip đầu trang, thẻ bước). Đụng đĩa/mạng/
   `AssetDatabase` trong đó là biến mỗi cú nhích chuột thành hàng chục lần đọc file. Tệ hơn: nếu số
   widget vẽ ra phụ thuộc dữ liệu đọc từ đĩa, dữ liệu đổi giữa lượt Layout và lượt Repaint sẽ ném
   `ArgumentException: Getting control N's position in a group with only M controls`.
   Chụp trạng thái trong `Reload()` vào field, mọi thứ khác chỉ đọc field.
3. **Không chạy việc nặng ngay trong lúc vẽ.** Ghi file, `AssetDatabase.Refresh()`, tải mạng, mở
   dialog — bấm nút thì chỉ ghi một cờ, rồi thực thi sau khi vẽ xong (mẫu `_pendingTab` /
   `_runAllRequested` trong `EzgKitWindow`). `Refresh()` trên một file `.cs` kích hoạt recompile, mà
   domain reload xảy ra giữa một layout group đang mở là cửa sổ vỡ.
4. **Dùng `using (new EditorGUILayout.ScrollViewScope(...))`**, đừng dùng cặp `BeginScrollView` /
   `EndScrollView` thủ công: exception ném ra giữa chừng sẽ nhảy qua `End...` và để lại layout group
   mở, cửa sổ spam `Mismatched LayoutGroup` cho tới khi đóng mở lại.
5. `Draw()` chỉ vẽ phần THÂN — cửa sổ đã vẽ tiêu đề + chip trạng thái.
6. Dùng API của `EzgKitStyles` / `SetupGui`, đừng tự chế style riêng — đó chính là thứ lần bố cục lại
   này dọn đi.
7. Thêm vào `EzgKitWindow.BuildPages()` — đúng vị trí theo thứ tự phải chạy — và thêm một mục vào
   enum `EzgKitWindow.Tab`.

GUI cửa sổ không phải sửa: cột nav, tab Tổng quan và luồng chạy hết đều sinh từ danh sách page.

## Menu

Toàn bộ menu của kit nằm dưới gốc `Ezg`.

| Menu | Việc |
|---|---|
| `Ezg/EzgKit` | mở tab Tổng quan |
| `Ezg/Marketing/Bang thong so (Marketing Dashboard)` | mở tab Marketing |
| `Ezg/Firebase/Cai dat...` | mở tab Firebase |
| `Ezg/Marketing/Setup All (1 Click)` | tải sheet + ghi vào project, không mở cửa sổ |
| `Ezg/Marketing/Check Config (Dry Run)` | chỉ đối chiếu, không ghi |
| `Ezg/Marketing/Apply Config (khong tai sheet)` | ghi từ JSON hiện có |
| `Ezg/Firebase/Tao app + tai config (1 Click)` | tạo app + tải config, không mở cửa sổ |
| `Ezg/Firebase/Kiem tra (Dry Run)` | chỉ GET, không tạo gì |
