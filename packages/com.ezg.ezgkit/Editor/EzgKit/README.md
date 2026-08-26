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
| Social | Discord invite / support link / email → `ProjectSettings/SocialConfig.json` → `GameConstant`; kiểm mọi link đi vào build, link hardcode, webhook/bot token Discord; nút *Kiểm Discord* | `../Social/` |
| Readiness | bảng Ready / Warning / Error chỉ đọc cho PM: SKU IAP, Firebase config, key SDK, keystore/link store; nút *Tra App Store* + *Copy báo cáo* | `../Readiness/` |
| **Nhà phát hành** → Ezg (trong nhà), Neptune, SayGame, … | mỗi tab = một BỘ SDK: cần gắn thêm / đã gắn — ID thay ở đâu / thừa; nút *Chuyển sang {X}* cài–gỡ–ghi ID–gắn define trong một lần bấm | `../Publisher/` |

Bốn tab đầu là nhóm **Setup Ezg** (dựng dự án theo hạ tầng Ezg — làm tuần tự). Nhóm **Nhà phát hành**
đứng riêng: mỗi publisher là một **bộ SDK trọn gói**, và bản "trong nhà" của Ezg cũng là một bộ như thế.
Đi CPI test với Neptune → bấm *Chuyển sang Neptune*; xong → bấm *Chuyển sang Ezg (trong nha)* là về bản cũ.
Header bar có ô *Phát hành* cho biết bộ SDK đang áp.

### Nhóm Nhà phát hành: một profile = một bộ SDK

Với dev, đi với một publisher chỉ có một câu hỏi: **họ đòi SDK gì · mình đã gắn cái nào · cái nào thừa ·
cái nào phải gắn thêm · ID nào phải thay ở đâu.** Tab trả lời bằng ba nhóm card:

1. **Cần gắn thêm** — publisher đòi, project chưa có: nguồn cài theo thứ tự file kéo vào → cache → **tự
   tải** (`SdkDownloader`: GitHub Releases cho Meta/GA/MAX, zip Google cho Firebase — đúng version + bộ
   product đang cài) → UPM spec. Chỉ Google Play plugins không có nguồn tải → kéo file tay.
   + danh sách ID sẽ phải điền.
2. **Đã gắn — ID** — từng ID: giá trị hiện tại → giá trị phải có (publisher cấp thì phải KHỚP, lệch là đỏ;
   game tự tạo thì trống là vàng), *Thay ở:* file/menu trong project, nút mở thẳng. Custom event bắt buộc
   (AppsFlyer) kiểm tên + tham số + chỗ bắn.
3. **SDK nền tảng — luôn giữ** — Apple GameKit (Game Center / Sign in with Apple), Google Play plugins (Play
   Asset Delivery / In-App Review): của bản build, không thuộc publisher nào (`SdkInstallSpec.IsPlatform`),
   mọi profile mặc nhiên giữ, không cần khai.
4. **Thừa** — project có, publisher không đòi → *Chuyển sang* sẽ gỡ.

Card SDK thiếu có ô tick **Import**, card SDK thừa có ô **Gỡ** — mặc định tick hết (một nút là xong), bỏ
tick mục nào thì mục đó vào "Bỏ qua"; SDK bị chặn thì ô khoá kèm lý do.

**Chuyển sang {X}** (`SdkSwitcher`) lập kế hoạch trước (card nào cũng in dòng "Sẽ cài / Sẽ gỡ / Chặn / Bỏ qua"),
hỏi lại kèm toàn bộ kế hoạch, rồi thi hành theo thứ tự: export SDK sắp gỡ ra `.unitypackage` trong cache
theo máy (`LocalApplicationData/Ezg/SdkCache/{game}/`, spec UPM vào `upm.json`) → xoá thư mục → import
package cài thêm → gắn/gỡ define `EZG_SDK_*` (Android + iOS) → ghi ID publisher cấp (`GameConstant.cs` +
field tương ứng trong `MarketingConfig.json`; Google Sheet vẫn phải đổi tay) → lưu `activePublisher` →
`Client.AddAndRemove` (UPM, cuối cùng vì kéo theo resolve + domain reload).

**Chặn:** switcher KHÔNG gỡ SDK mà code game (ngoài thư mục SDK) còn gọi thẳng — gỡ là vỡ compile và
Editor không mở được tool nữa. Card báo số file, tên file, và define cần bọc. Trên template hiện tại
Firebase / MAX / IAP / PAD / Meta / AppsFlyer đều còn tham chiếu thẳng (GameInitialize, AdsController,
PurchaseManager…) → *Chuyển sang Neptune* hôm nay ghi được dev key + gắn GA + gỡ GameKit, còn 4 SDK kia
"chặn". Muốn switch sạch: bọc lời gọi SDK trong `#if EZG_SDK_*` (task backlog riêng) — define đã được
switcher gắn sẵn theo bộ SDK.

`IPublisherProfile` (`../Publisher/IPublisherProfile.cs`) là DỮ LIỆU: `SdkRequirement[]` — mỗi SDK một
`Why`, các `SdkIdSlot` (`Given(key, label, value)` = publisher cấp sẵn, `Own(key, label, howTo)` = game tự
tạo), `RequiredEvent`. `SdkCatalog` là phần dùng chung: dò SDK, đọc ID theo key (`FacebookSettings.asset`,
`GameConstant.cs`, `GA Settings.asset`), kiểm event, và `SdkInstallSpec` (UPM name/spec, thư mục Assets,
trang release, regex tham chiếu code, define). Thêm publisher: `Profiles/{X}Profile.cs` theo mẫu
`NeptuneProfile`, thêm vào `PublisherRegistry.Profiles`, thêm `EzgKitWindow.Tab` (+ menu). Thêm SDK catalog
chưa biết: `SdkKind` + `Detect` + `ReadSlot`/`WhereOf` + `SpecOf`.

Profile hiện có: **Ezg (trong nhà)** — Meta, AppsFlyer, Firebase, MAX, Unity IAP, Google Play plugins,
Apple GameKit (ID từ `MarketingConfig.json`); **Neptune (Flick Different)** — Meta + AppsFlyer (dev key của
Neptune, event `f_custom_playtime`) + GameAnalytics; **SayGame** — chưa có tài liệu, chỉ liệt kê SDK đang có.

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
2. **Quét source dùng `SourceIndex`** (`EzgKit/SourceIndex.cs`), không tự `EnumerateFiles` + `ReadAllText`:
   text .cs cache trong RAM, kết quả dẫn xuất cache theo khoá (`SourceIndex.Get`), tự vô hiệu khi .cs đổi.
   Mỗi bộ kiểm tự đọc đĩa là mỗi lần đổi tab thêm nửa giây.
3. **`Status` và `Headline` phải RẺ — chỉ đọc field đã chụp sẵn.** Cửa sổ đọc hai getter này **mỗi
   lượt OnGUI** của **mọi** tab (để vẽ icon cột nav, chip đầu trang, thẻ bước). Đụng đĩa/mạng/
   `AssetDatabase` trong đó là biến mỗi cú nhích chuột thành hàng chục lần đọc file. Tệ hơn: nếu số
   widget vẽ ra phụ thuộc dữ liệu đọc từ đĩa, dữ liệu đổi giữa lượt Layout và lượt Repaint sẽ ném
   `ArgumentException: Getting control N's position in a group with only M controls`.
   Chụp trạng thái trong `Reload()` vào field, mọi thứ khác chỉ đọc field.
4. **Không chạy việc nặng ngay trong lúc vẽ.** Ghi file, `AssetDatabase.Refresh()`, tải mạng, mở
   dialog — bấm nút thì chỉ ghi một cờ, rồi thực thi sau khi vẽ xong (mẫu `_pendingTab` /
   `_runAllRequested` trong `EzgKitWindow`). `Refresh()` trên một file `.cs` kích hoạt recompile, mà
   domain reload xảy ra giữa một layout group đang mở là cửa sổ vỡ.
5. **Dùng `using (new EditorGUILayout.ScrollViewScope(...))`**, đừng dùng cặp `BeginScrollView` /
   `EndScrollView` thủ công: exception ném ra giữa chừng sẽ nhảy qua `End...` và để lại layout group
   mở, cửa sổ spam `Mismatched LayoutGroup` cho tới khi đóng mở lại.
6. `Draw()` chỉ vẽ phần THÂN — cửa sổ đã vẽ tiêu đề + chip trạng thái.
7. Dùng API của `EzgKitStyles` / `SetupGui`, đừng tự chế style riêng — đó chính là thứ lần bố cục lại
   này dọn đi.
8. Thêm vào `EzgKitWindow.BuildPages()` — đúng vị trí theo thứ tự phải chạy — và thêm một mục vào
   enum `EzgKitWindow.Tab`. (Tab cho một NHÀ PHÁT HÀNH thì không đi đường này: viết profile + đăng ký
   `PublisherRegistry`, xem mục "Nhóm Nhà phát hành".)

GUI cửa sổ không phải sửa: cột nav, tab Tổng quan và luồng chạy hết đều sinh từ danh sách page.

## Menu

Toàn bộ menu của kit nằm dưới gốc `Ezg`.

| Menu | Việc |
|---|---|
| `Ezg/EzgKit` | mở tab Tổng quan |
| `Ezg/Marketing/Bang thong so (Marketing Dashboard)` | mở tab Marketing |
| `Ezg/Firebase/Cai dat...` | mở tab Firebase |
| `Ezg/Social (Discord - Support - Rating)` | mở tab Social — điền + ghi link cộng đồng/hỗ trợ, kiểm link |
| `Ezg/Readiness (IAP - Firebase - SDK)` | mở tab Readiness — bảng trạng thái cho PM, chỉ đọc |
| `Ezg/Nha phat hanh/Ezg (mac dinh trong nha)` | mở tab Ezg — bộ SDK mặc định, bấm về sau khi đi test với publisher |
| `Ezg/Nha phat hanh/Neptune (CPI Test)` | mở tab Neptune — bộ SDK Neptune đòi, nút Chuyển sang Neptune |
| `Ezg/Nha phat hanh/SayGame` | mở tab SayGame — placeholder chờ tài liệu |
| `Ezg/Marketing/Setup All (1 Click)` | tải sheet + ghi vào project, không mở cửa sổ |
| `Ezg/Marketing/Check Config (Dry Run)` | chỉ đối chiếu, không ghi |
| `Ezg/Marketing/Apply Config (khong tai sheet)` | ghi từ JSON hiện có |
| `Ezg/Firebase/Tao app + tai config (1 Click)` | tạo app + tải config, không mở cửa sổ |
| `Ezg/Firebase/Kiem tra (Dry Run)` | chỉ GET, không tạo gì |
