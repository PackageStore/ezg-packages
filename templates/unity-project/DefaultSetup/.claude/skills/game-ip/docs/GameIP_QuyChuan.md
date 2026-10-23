# Quy chuẩn bản quyền và IP cho game (Game IP)

Phiên bản 0.0.1 (nháp, chờ công ty duyệt) · 2026-10-23 · Gói `com.ezg.game-ip`, skill Claude `game-ip`. Lý do của từng luật,
luật theo thị trường, án lệ và nguồn: `GameIP_NguyenLy.md` (gọi tắt NL). Khi bị khiếu nại hay bị nhái: `GameIP_UngPho.md`.

Áp dụng cho game công ty làm ở Việt Nam và phát hành toàn cầu (App Store, Google Play, Steam, web), cho mọi thứ đi ra ngoài:
bản build, store listing, creative UA (video, ảnh, playable), trailer. Ba loại IP: bản quyền, nhãn hiệu, bí mật kinh doanh.
Không bàn: sáng chế (patent, NL 1.4), quy định nội dung theo độ tuổi, loot box, quyền riêng tư dữ liệu.

**Không phải tư vấn pháp lý.** Tài liệu nói cách kiểm và mức rủi ro; kết luận pháp lý và mọi quyết định "vẫn ship" ở mức Chặn
là của người duyệt công ty chỉ định **[DUYỆT]**.

Người đọc: **[ART]** art, animation, sound; **[UA]** UA, marketing, agency; **[PD]** product, game design; **[DEV]** dev, tech
art; **[REL]** producer, người chịu trách nhiệm release; **[DUYỆT]** người duyệt pháp lý của công ty. Không ghi là việc chung.

| Bạn là | Đọc trước |
|---|---|
| Art, sound | 0, 3, 4.1–4.9, 5.1 |
| UA, marketing | 0, 3.4, 4.14–4.17, 5.2, 8 |
| Product, game design | 0, 4.10–4.15, 5.3, 6, 7 |
| Dev, tech art | 0, 3, 4.1, 4.5, 4.6, 4.18, 5.4 |
| Producer, release | 0, 2, 4, 5.5, 9, 10, `GameIP_UngPho.md` |

Số và ngưỡng ghi *(đề xuất)* là điểm bắt đầu chưa có nguồn chính thức; pilot sẽ chỉnh.

---

## 0. Tám nguyên tắc gốc

1. **Mọi thứ trong build có chủ.** Bản build là cái vỏ chứa nhiều lớp quyền của nhiều chủ (NL 2.1). Không biết chủ của một
   asset thì asset đó không ship.
2. **Quyền có phạm vi.** Được dùng trong game không có nghĩa là được dùng trong quảng cáo, trailer, store, merch. Phạm vi ghi
   ra từ license, không suy đoán (NL 4).
3. **Lấy ý tưởng, không lấy cách thể hiện.** Luật chơi, cơ chế, thể loại là của chung; hình, chữ, âm thanh, bố cục, chuỗi lựa
   chọn và sắp xếp cụ thể là của người làm ra (NL 2.2). Quy tắc gương: thứ mình coi là IP của mình cũng là thứ không được lấy
   của người khác.
4. **Kiểm chỗ dễ chứng minh nhất trước.** Bên khiếu nại đánh vào chỗ dễ chứng minh nhất (asset chép nguyên, nhạc, font, tên,
   icon, creative), không phải chỗ giống nhất (NL 6.1).
5. **Thứ store nhìn phải sạch nhất.** Store xử trong vài ngày bằng cách nhìn icon, tên, screenshot, 30 giây đầu; toà xử trong
   vài năm (NL 5).
6. **Ghi lại lúc làm, không ghi lại lúc bị hỏi.** Nguồn, hợp đồng, hoá đơn, prompt, file gốc, ngày. Bằng chứng dựng sau khi có
   tranh chấp ít giá trị và có thể bị coi là xoá, sửa chứng cứ.
7. **Thứ không tự có thì phải đăng ký.** Bản quyền tự có ở mọi nước Berne; nhãn hiệu chỉ có ở nước đã đăng ký, và phần lớn thị
   trường theo nguyên tắc nộp đơn trước (NL 3).
8. **Có chuyện thì không tự xử.** Không trả lời thư đe doạ, không xoá hay sửa bằng chứng, không gửi counter-notice khi chưa có
   [DUYỆT]: làm theo `GameIP_UngPho.md`.

---

## 1. Thuật ngữ

| Từ | Nghĩa |
|---|---|
| Asset vào build | Asset Unity thật sự nằm trong bản build: scene trong Build Settings, `Resources`, nhóm Addressables, `StreamingAssets` và mọi thứ chúng tham chiếu; cộng plugin native, font nguồn của font asset TMP |
| Nguồn | Nơi một asset đến từ: tự làm, pack mua, thư viện mở, stock, công cụ AI, vẽ thuê, SDK của đối tác |
| Sổ nguồn gốc (sổ) | `ProjectSettings/GameIPLedger.json` của project: mỗi mục là một nguồn, phủ một tập asset (mục 3) |
| Phạm vi | Những cách được dùng: trong game, quảng cáo trả tiền, trailer, store, phân phối file gốc, merch |
| Creative | Mọi thứ UA chạy ngoài game: video, ảnh tĩnh, playable, banner, end card |
| Game tham chiếu | Game có thật mà một feature, màn hình hay art của mình học theo |
| Thẻ so sánh | Biên bản so một phần của game mình với game tham chiếu theo mục 6 |
| Nợ | Vi phạm gate đã được [DUYỆT] chấp nhận tạm, có lý do và hạn (mục 10.2) |
| Lớp L1–L4 | Nơi công ty (Việt Nam), nền tảng, thị trường phát hành, nơi bị kiện (NL 1) |

---

## 2. Thị trường của game

File riêng của project `ProjectSettings/GameIPProject.json` ghi:

```json
{
  "markets": ["VN", "US", "EU", "UK", "JP", "KR", "SEA"],
  "stores": ["appstore", "googleplay"],
  "adNetworks": ["meta", "tiktok", "googleads", "applovin", "unity"],
  "referenceGames": [],
  "competitors": [],
  "credits": { "screen": "", "noticesFile": "" },
  "strict": false
}
```

- Chuẩn để kiểm là mức chặt nhất trong các thị trường và nền tảng của game. Với game phát hành toàn cầu, thực tế là: chính sách
  store và mạng quảng cáo (nhanh nhất), luật Mỹ (nơi dễ bị kiện nhất), EU (DSA, AI Act), cộng chỗ Việt Nam khác (ai là chủ
  sở hữu, đăng ký).
- Nhãn hiệu kiểm theo đúng danh sách `markets` (4.14). Thêm thị trường là phải chạy lại 4.14.
- `referenceGames`, `competitors`: tên game và công ty mà project học theo hoặc cạnh tranh; dùng cho 4.10, 4.13, 4.14.

---

## 3. Sổ nguồn gốc

### 3.1 Cái gì phải có trong sổ

- Mọi asset vào build (gate 4.1).
- Mọi thứ trong creative, trailer, store listing (4.16, 4.17).
- Mọi công cụ, middleware, SDK có license gắn với sản phẩm (4.5): kể cả thứ không nằm trong build như công cụ AI đã tạo
  asset.

Một mục phủ nhiều asset (cả một pack, cả một folder tự làm). Asset tự làm cũng có mục (`inhouse`): nó là bằng chứng ngày
tạo và người tạo khi mình đi khiếu nại người khác.

### 3.2 Trường

| Trường | Bắt buộc | Nghĩa |
|---|---|---|
| `id` | có | Tên ngắn, không đổi |
| `paths`, `guids` | một trong hai | Glob đường dẫn, hoặc GUID (pack Asset Store nhận theo GUID: dời folder vẫn khớp) |
| `kind` | có | Loại nguồn (3.3) |
| `title`, `author`, `source` | có | Tên gói / tác phẩm, tác giả hoặc publisher, URL |
| `license` | có | Mã license (`unity-asset-store-eula`, `OFL-1.1`, `MIT`, `Apache-2.0`, `CC0-1.0`, `CC-BY-4.0`, `custom`…) |
| `licenseFile` | khi có | Đường dẫn file license, hợp đồng, hoá đơn (trong repo hoặc kho tài liệu công ty) |
| `scope` | có | `game`, `ads`, `trailer`, `store`, `redistributeSource`, `merch`: `true` / `false` / `"unknown"` |
| `proof` | theo loại | Tài khoản mua, số đơn, số hợp đồng, ngày tải |
| `attribution` | khi license đòi | Câu ghi công đúng như license yêu cầu (4.6) |
| `limits` | khi có | Ngưỡng của license: doanh thu, seat, CCU, hạn dùng, lãnh thổ |
| `expires` | khi có | Ngày hết hạn license |
| `ai` | với `kind: ai` | `tool`, `plan`, `date`, `prompt`, `references`, `humanEdit`, `by` (4.7) |
| `status` | có | `draft` (máy điền, chưa ai xác nhận) · `confirmed` (người có trách nhiệm đã xác nhận) |
| `confirmedBy`, `confirmedAt` | khi `confirmed` | Ai, ngày nào |
| `notes` | không | Ghi chú |

```json
{
  "id": "layerlab-gui-pro-supercasual",
  "kind": "asset-store",
  "guids": ["…lấy từ cache Asset Store hoặc .unitypackage…"],
  "paths": ["Assets/_Project/Visual/ArtAsset/Package/Layer Lab/GUI Pro-SuperCasual/**"],
  "title": "GUI Pro - Super Casual",
  "author": "Layer Lab",
  "source": "https://assetstore.unity.com/…",
  "license": "unity-asset-store-eula",
  "scope": { "game": true, "ads": "unknown", "trailer": "unknown", "store": "unknown", "redistributeSource": false },
  "proof": "Tài khoản Asset Store của công ty, đơn #…",
  "status": "confirmed",
  "confirmedBy": "…",
  "confirmedAt": "2026-10-23"
}
```

### 3.3 Loại nguồn và bằng chứng cần có

| `kind` | Ví dụ | Bằng chứng để `confirmed` |
|---|---|---|
| `inhouse` | Art, code, nhạc nhân viên làm | Người làm, lịch sử git, file gốc (.psd, .spine, .blend, project nhạc) |
| `commission` | Vẽ thuê, outsource, composer | Hợp đồng có điều khoản chuyển quyền (4.9), file gốc đã giao |
| `asset-store` | Pack Unity Asset Store | Tài khoản mua (của công ty, không phải tài khoản cá nhân đã nghỉ), số đơn; seat với Extension Asset |
| `marketplace` | itch.io, CGTrader, TurboSquid, Sketchfab, Envato | Trang license đúng bản lúc mua / tải, hoá đơn; loại license (Standard / Editorial…) |
| `stock` | Ảnh, footage, nhạc thư viện | Số license, gói (plan), phạm vi gói |
| `oss` | Thư viện code mở, package UPM | File LICENSE, phiên bản |
| `font` | File .ttf / .otf | License trong file (bảng `name`) hoặc file license / hoá đơn foundry |
| `ai` | Ảnh, video, nhạc, voice do AI tạo | Công cụ, gói tài khoản lúc tạo, ngày, prompt, ảnh tham chiếu và nguồn của chúng, phần người sửa |
| `publisher-sdk` | SDK, font, UI của publisher | Hợp đồng với publisher, thời hạn |
| `public-domain` | CC0, tài liệu hết hạn bảo hộ | Link, ngày tải, bản chụp trang license |
| `licensed-ip` | Nhân vật, thương hiệu được cấp phép | Hợp đồng license, phạm vi, thời hạn, lãnh thổ, duyệt mẫu |

### 3.4 Phạm vi mặc định khi license không nói rõ

| `kind` | game | ads, trailer, store | redistributeSource |
|---|---|---|---|
| `inhouse` | true | true | theo công ty |
| `commission` | theo hợp đồng | theo hợp đồng | false |
| `asset-store` | true | `"unknown"` (EULA không có điều khoản cho phép rõ quảng cáo số, NL 4.1) | false |
| `marketplace`, `stock` | theo license | `"unknown"` cho tới khi đọc license | false |
| `oss` | theo license | true | theo license |
| `font` | theo license | theo license (OFL: true) | false |
| `ai` | theo điều khoản công cụ | theo điều khoản công cụ | false |
| `publisher-sdk` | trong hợp đồng | `"unknown"` | false |

`"unknown"` ở `ads` không chặn release game, nhưng chặn creative (4.17).

### 3.5 Ai xác nhận

- [ART] xác nhận nguồn art, sound; [DEV] code, middleware, font kỹ thuật; [UA] creative; [REL] hợp đồng, tài khoản mua.
- Mục `ai`, `commission`, `licensed-ip` và mọi mục `custom` license: [DUYỆT] xem ít nhất một lần.
- Skill không bao giờ tự đặt `confirmed`.

---

## 4. Gate

Kết quả mỗi dòng: `✓` đạt · `✗` hỏng · `!` cảnh báo · `·` thông tin · `–` không áp dụng. Mức: **Chặn** (`✗`: không ship, không
chạy creative cho tới khi sửa hoặc [DUYỆT] cho ghi nợ), **Cảnh báo** (`!`: đọc rồi quyết, quyết thế nào ghi vào sổ hoặc nợ),
**Ghi chú** (`·`). Dòng `✓` quan trọng ngang `✗`: không có nó thì không biết đã kiểm hay chưa kiểm.

"Máy" là phần skill tự kiểm; phần còn lại là người kiểm theo checklist.

### Nhóm A: nguồn gốc

#### 4.1 IP-1: mọi asset vào build có nguồn **[ART] [DEV] [REL]**

- Đạt: mỗi asset vào build được ít nhất một mục `confirmed` phủ.
- Máy: tính danh sách asset vào build (Unity BuildReport nếu có, không thì đọc tham chiếu GUID trong YAML), so với sổ. Gói Unity
  `com.ezg.game-ip` ghi danh sách thật sau mỗi lần build, hiện nguồn của asset trên Inspector, và có **chặn build** theo gate này:
  mặc định tắt; project nào muốn thì bật ở Project Settings → Game IP, chỉ chặn bản release (Development Build bỏ qua trừ khi bật).
- Mức: không mục nào phủ → Chặn. Chỉ có mục `draft` → Cảnh báo. Asset không vào build (Demo, Editor, pack chưa dùng) → Ghi
  chú, không chặn, nhưng nên dời khỏi project hoặc ghi sổ để khỏi lọt vào build sau này.
- Sửa: thêm mục; không tìm ra nguồn thì thay asset (vẽ lại, mua) hoặc bỏ khỏi build.

#### 4.2 IP-2: pack mua đúng người mua, không phân phối lại **[ART] [DEV] [REL]**

- Mỗi pack mua có bằng chứng mua của **công ty** (không phải tài khoản cá nhân của người đã nghỉ).
- Extension Asset của Asset Store (công cụ Editor, scripting, services) tính theo seat: mỗi người dùng cần seat riêng (NL 4.1).
- Không đưa file gốc của pack mua ra nơi người ngoài tải được: repo công khai, registry hay gateway tải không cần token, gói
  chia cho studio khác, project mẫu gửi đối tác. Bản build thì được.
- Máy: GUID của pack mua (từ cache Asset Store hoặc sổ) xuất hiện trong gói sắp publish (UPM, `.unitypackage`, repo công khai)
  → Chặn.
- Asset từ marketplace có loại license "Editorial" (thường gặp với xe, súng, máy bay có thương hiệu) → Chặn trong game
  thương mại.
- Asset CC-BY-SA hoặc GPL (hay gặp ở thư viện asset mở): copyleft, có thể kéo theo nghĩa vụ với phần mình làm → Cảnh báo,
  [DUYỆT] quyết. CC-BY-NC, CC-BY-NC-SA → Chặn (NL 4.2).

#### 4.3 IP-3: font được phép nhúng vào app **[ART] [DEV]**

- Máy đọc trong file font: bản quyền (`name` 0), nhà cung cấp (8), mô tả license (13), URL license (14), `OS/2.fsType`; và font
  nguồn của mọi font asset TMP.
- SIL OFL, Apache 2.0, Ubuntu Font License: được nhúng; nghĩa vụ ghi công ở 4.6. Không sửa file font rồi giữ tên gốc khi font
  có "Reserved Font Name".
- `fsType` = 2 (Restricted) → Chặn trừ khi có license app của foundry. 4 hoặc 8 với font thương mại → Cảnh báo: cần license
  "App / Embedding" riêng, license Desktop không đủ (NL 4.2).
- Font không ghi license trong file và không có mục sổ → Chặn.
- Font lấy từ Adobe Fonts (đồng bộ qua Creative Cloud) không được đóng gói vào app, game → Chặn; cần license app của foundry.
- Nhập font vào font asset TextMesh Pro không đổi license của font gốc: kiểm font nguồn, không kiểm font asset.
- Font của publisher, khách hàng, đối tác (vd font riêng trong SDK publisher) chỉ dùng trong phạm vi hợp đồng; dùng cho UI
  của mình hay sau khi hết hợp đồng → Chặn.

#### 4.4 IP-4: âm thanh và nhạc có đủ hai lớp quyền **[ART] [UA]**

- Nhạc có hai lớp: bản nhạc (sáng tác) và bản thu (master). Cần quyền cả hai (NL 2.3).
- Sổ ghi phạm vi (game, ads, trailer, store video, livestream), hạn, lãnh thổ.
- Máy đọc tag ID3 (TCOP, TPUB, WXXX, COMM), RIFF INFO (ICOP, IART, ISFT): tag ghi nguồn khác với sổ (vd tên thư viện, URL
  website âm thanh, tên kênh) → Chặn tới khi giải thích được.
- Bài hát thương mại không có license → Chặn. License hết hạn → Chặn (một lớp hết hạn có thể làm cả game dừng phân phối).
- Nhạc CC có điều khoản NC (phi thương mại) → Chặn trong game có kiếm tiền.
- Gói thuê bao thư viện nhạc thường **không** phủ việc dùng trong game ở gói cá nhân (nhiều thư viện chỉ cho dùng trong game ở
  gói doanh nghiệp, có thư viện bắt hợp đồng riêng); license từng bài có thể giới hạn số bản sao (vd 10.000), không đủ cho game
  mobile (NL 4.2). Sổ ghi đúng gói đã mua; gói không phủ game → Chặn.
- Composer thuê: hợp đồng chuyển (hoặc cấp phép đủ phạm vi) cả bản nhạc lẫn bản thu.

#### 4.5 IP-5: code, middleware, SDK **[DEV] [REL]**

- Thư viện mở: MIT, BSD, Apache 2.0, zlib → ✓, nghĩa vụ ghi công ở 4.6. LGPL → Cảnh báo (khó tuân thủ trên mobile khi liên
  kết tĩnh, như iOS). GPL, AGPL → Chặn. Code không có license (copy từ repo không ghi license, từ trang hỏi đáp) → Cảnh báo.
- Middleware có ngưỡng doanh thu, seat, CCU, hạng giá: sổ ghi `limits`; [REL] cập nhật doanh thu, vốn gọi, ngân sách, CCU mỗi
  quý; vượt ngưỡng mà chưa nâng hạng → Chặn (NL 4.4). Ngưỡng hay gặp: Unity Personal và Odin Personal (200.000 USD doanh thu +
  vốn / 12 tháng), Spine Essential / Professional (500.000 USD), Live2D (10 triệu yên doanh thu cả công ty), FMOD, Wwise (theo
  ngân sách dự án), Photon (CCU). Ngưỡng tính theo **công ty**, nên một game thành công kéo mọi project khác qua ngưỡng.
- SDK, công cụ của publisher: chỉ trong hợp đồng.
- Máy: đọc `Packages/manifest.json`, `packages-lock.json`, file LICENSE trong `Assets/` và `Packages/`, header license trong
  source; tìm từ khoá GPL, AGPL.

#### 4.6 IP-6: nghĩa vụ ghi công có trong bản build **[DEV] [REL]**

- Game có màn Credits / Licenses (thường trong Settings → About) hoặc file notice mở được từ trong game.
- Nội dung sinh từ sổ (lệnh `notices` của skill), gồm đủ:
  - OFL: dòng bản quyền của font + toàn văn hoặc link license.
  - MIT, BSD: dòng bản quyền + nội dung giấy phép.
  - Apache 2.0: license + nội dung file NOTICE nếu có.
  - CC-BY: tên tác giả, tên tác phẩm, nguồn, link license, có sửa đổi hay không.
  - Yêu cầu riêng của tác giả (vd "credit tác giả X").
- Máy: mục sổ có `attribution` mà không có trong file notice của build → Chặn. Game chưa có màn Credits trong khi sổ có nghĩa
  vụ ghi công → Chặn.

#### 4.7 IP-7: asset do AI tạo **[ART] [UA] [DUYỆT]**

- Mỗi lô asset AI có mục sổ `kind: ai`: công cụ, gói tài khoản lúc tạo (gói miễn phí và gói trả tiền của nhiều công cụ cho quyền
  thương mại khác nhau, NL 4.3), ngày, prompt, ảnh tham chiếu và nguồn của chúng, phần người sửa, người làm.
- Prompt và ảnh tham chiếu không gọi tên, không dùng hình của game, nhân vật, thương hiệu, nghệ sĩ cụ thể. Dùng art của game khác
  làm ảnh tham chiếu (img2img, style reference) cho asset sẽ ship → Chặn.
- Kết quả giống nhân vật, logo, hình đặc trưng của IP có sẵn → Chặn, dù prompt không gọi tên.
- Ở Mỹ đầu ra thuần AI không được bảo hộ bản quyền; chỉ phần người làm được bảo hộ (NL 4.3). Nhân vật chính, key art, icon nên
  có người vẽ hoặc sửa đáng kể và ghi lại phần sửa, để còn quyền mà khiếu nại khi bị chép *(đề xuất)*.
- Quyền với đầu ra khác nhau theo công cụ và theo gói (NL 4.3): có công cụ chỉ cấp license giới hạn, có thể thu hồi, không
  chuyển quyền sở hữu; có công cụ bắt gói cao hơn khi doanh thu công ty vượt 1 triệu USD / năm; gói miễn phí thường không cho dùng
  thương mại. Asset AI cho nhân vật chính, key art, icon chỉ làm bằng công cụ cho quyền sở hữu hoặc quyền dùng không thu hồi
  *(đề xuất)*.
- Phát hành trên Steam: khai báo nội dung AI trên trang store.
- EU (AI Act Điều 50, từ 2/8/2026): creative, nội dung có người, nơi, sự kiện thật làm bằng AI theo kiểu ảnh thật có thể phải ghi
  rõ cho người xem → [DUYỆT] xem trước khi chạy ở EU. Art hư cấu, cách điệu có lẽ không bị (NL 4.3).
- Máy: đọc metadata ảnh (C2PA, IPTC `DigitalSourceType`, tham số sinh ảnh trong PNG) để tìm asset AI chưa có trong sổ → Cảnh báo.

#### 4.8 IP-8: không asset lấy từ game khác, từ web không rõ nguồn **[ART]**

- Không dùng asset trích từ bản build của game khác (công cụ tách asset), ảnh lưu từ web, mạng xã hội, Pinterest, ảnh chụp
  màn hình game hay phim, kể cả làm "tạm" (tạm thường ship).
- Ảnh tham chiếu để vẽ: để ngoài project Unity (thư mục tài liệu, moodboard), không để trong `Assets/`.
- Máy: tên file và cấu trúc thư mục kiểu ảnh web hay công cụ tách asset (`Screenshot-…`, `download (n)`, `image (n)`, tên
  atlas nội bộ của game khác), metadata EXIF / IPTC / XMP ghi tác giả, bản quyền, credit, website không phải của công ty → Chặn
  tới khi có nguồn. Watermark: người xem ảnh do máy chọn.
- Mức: Chặn.

#### 4.9 IP-9: người làm ra đã chuyển quyền cho công ty **[REL] [DUYỆT]**

- Nhân viên: hợp đồng lao động hoặc quy chế có điều khoản tác phẩm làm theo nhiệm vụ thuộc công ty. Luật Việt Nam mặc định
  vậy "trừ khi thoả thuận khác" (NL 2.5); vẫn ghi rõ trong hợp đồng.
- Freelancer, outsource, composer, agency: hợp đồng ghi rõ: chuyển quyền tài sản (hoặc license độc quyền đủ phạm vi), phạm vi
  gồm cả quảng cáo, merch, phần tiếp theo; đồng ý cho công ty sửa, cắt, phóng tác (quyền nhân thân về sự toàn vẹn tác phẩm ở
  Việt Nam không chuyển được, nên cần sự đồng ý trước); giao file gốc; cam kết không dùng tài sản bên thứ ba hay AI khi không
  khai báo; bên làm chịu trách nhiệm nếu vi phạm.
- Máy: không kiểm được hợp đồng; sổ phải có `proof` với mục `commission`, thiếu → Cảnh báo.

### Nhóm B: giống game khác

#### 4.10 IP-10: thẻ so sánh với game tham chiếu **[PD] [ART] [DUYỆT]**

- Khi nào: brief nói "giống game X", feature học theo một game cụ thể, 4.13 báo, hoặc người duyệt thấy giống.
- Cách làm: mục 6. Kết quả Xanh / Vàng / Đỏ.
- Mức: Đỏ → Chặn, chuyển [DUYỆT]. Vàng → Cảnh báo, sửa theo đề xuất của thẻ.

#### 4.11 IP-11: ảnh của mình so với ảnh của game tham chiếu **[ART]**

- Team đưa ảnh của game tham chiếu (screenshot, icon, nhân vật) vào thư mục tham chiếu ngoài `Assets/`.
- Máy (lệnh `compare --mine … --theirs …` của skill): perceptual hash từng sprite, texture, icon của mình với ảnh tham chiếu, cả
  bản lật ngang; đưa ra các cặp gần nhất (khoảng cách hash nhỏ hơn ngưỡng *(đề xuất: 10 / 64 bit)*) để người xem cạnh nhau. Chỉ bắt
  ảnh gần như cùng ảnh; ảnh vẽ lại cùng bố cục, ảnh bị cắt ghép thì người phải xem.
- Người kết luận: giống vì cùng thể loại (ô vuông, viên ngọc) → bỏ; giống ở cách vẽ cụ thể → Chặn.

#### 4.12 IP-12: tổ hợp chọn và sắp xếp **[PD]**

- Từng luật có thể là của chung, nhưng chuỗi lựa chọn cụ thể (chuỗi tiến hoá vật phẩm, bộ luật đặc biệt, thứ tự giới thiệu luật
  qua các màn, bố cục màn hình tổng thể) giống gần nguyên một game là biểu đạt bị chép (NL 5.4: Triple Town, Farm Heroes Saga,
  Tetris).
- Kiểm trong thẻ so sánh (6.3). Mức: Đỏ.

#### 4.13 IP-13: tên game khác trong project **[PD] [DEV]**

- Máy: tìm tên trong `referenceGames`, `competitors` (và danh sách game lớn của skill) ở tên folder, class, asset, scene, tài liệu
  thiết kế trong repo.
- Nghĩa: feature đó đang bám một game cụ thể, và nếu có tranh chấp thì đó là bằng chứng về ý đồ. Việc cần làm là chạy 4.10 cho
  phần đó và sửa thiết kế nếu Vàng / Đỏ, đặt tên theo chức năng cho phần mới.
- Không đổi tên, xoá file cũ để che: khi đã có hoặc thấy trước tranh chấp, sửa xoá tài liệu là tự làm hại mình (UngPho).
- Mức: Cảnh báo.

### Nhóm C: nhãn hiệu, store, creative

#### 4.14 IP-14: tên game, tên feature, icon **[PD] [UA] [REL]**

- Độ mạnh của tên: tự bịa > từ có sẵn không liên quan > gợi ý > mô tả (khó đăng ký) > tên chung của thể loại (không bao giờ được
  bảo vệ) (NL 3.2).
- Không chứa tên, brand, tên sản phẩm của developer khác trong tên app, subtitle, icon (Apple 4.1(c), 2.3.7; Google Play
  Impersonation, Metadata).
- Icon không dùng nhân vật, biểu tượng đặc trưng của game khác; không phỏng bố cục, màu, nhân vật của icon game đang top cùng thể
  loại.
- Tra trùng ở từng thị trường trong `markets` và trên store (mục 7). Game mobile ở ngưỡng gây nhầm lẫn cao nhất: cùng store,
  cùng mục, cài nhanh, miễn phí (NL 3.3).
- Đăng ký nhãn hiệu tên game (và logo) ở thị trường chính trước khi scale (mục 7.3).
- Máy (lệnh `name`): chấm độ mạnh, so với `referenceGames`, `competitors`, danh sách game lớn, thương hiệu (trùng từ, viết gần
  giống, đọc gần giống), giới hạn 30 ký tự; sinh link tra cứu cho người mở (không tự cào cơ sở dữ liệu nhãn hiệu, NL 7). Tên chưa
  công bố là bí mật kinh doanh.
- Mức: trùng hoặc gần trùng nhãn hiệu đã đăng ký cùng nhóm hàng (game, phần mềm, giải trí) → Chặn, [DUYỆT] quyết. Tên mô tả,
  tên yếu → Cảnh báo.

#### 4.15 IP-15: thương hiệu thật trong nội dung game **[PD] [ART]**

- Tên, logo, hình dạng đặc trưng của xe, súng, máy bay, giải đấu, đội thể thao, người nổi tiếng, game, phim, thương hiệu tiêu
  dùng.
- Mặc định dùng tên và hình hư cấu đủ khác. Logo thật → Chặn. Tên, hình dạng đặc trưng thật → Cảnh báo, [DUYỆT] quyết (NL 3.5:
  ở Mỹ tác phẩm nghệ thuật có lá chắn nhưng hẹp dần và vẫn tốn tiền kiện; người nổi tiếng còn quyền hình ảnh).
- Máy (`survey`, `check`): quét chữ trong CSV / JSON localize, ScriptableObject, prefab, scene (không quét code, folder bên thứ
  ba) với danh sách thương hiệu của skill + `brands` của project; phân biệt hoa thường, nguyên từ. Tên nền tảng dùng để đăng nhập,
  chia sẻ (Facebook, Google…) không tính. Logo, hình dạng đặc trưng: người xem.

#### 4.16 IP-16: store listing **[UA] [REL]**

- Icon, tên, subtitle, mô tả, keyword, screenshot, video preview, feature graphic đều là thứ store xem đầu tiên.
- Apple: video preview chỉ là quay màn hình của chính app (2.3.4), có thể thêm lời dẫn, lớp phủ; tên ≤ 30 ký tự, không nhồi tên
  app khác, nhãn hiệu vào keyword (2.3.7); không mô tả sai app (2.3.1). Google Play: Metadata, Impersonation.
- Screenshot, feature graphic chỉ chứa art có `scope.store` true.
- Máy: tên app qua lệnh `name`; video preview App Store qua lệnh `creative` với `storePreview: true`. Mô tả, keyword, screenshot:
  người kiểm *(đọc metadata store trong repo: bản sau)*.
- Mức: tên game hay brand khác trong tên, subtitle, keyword → Chặn. Preview Apple có cảnh dựng không có trong game → Chặn.

#### 4.17 IP-17: creative UA, trailer, playable **[UA]**

- Mỗi creative có sổ riêng `creative.json` trong folder của creative (lệnh `creative --init` sinh nháp; lệnh `creative` kiểm), gồm:
  gameplay quay từ game, art, nhạc, sfx, font, footage, voice, phần AI, người đóng (creator, diễn viên), playable code; ghi mạng sẽ
  chạy (`networks`) và có dùng làm preview App Store không (`storePreview`).
- Mọi nguồn có `scope.ads` = true (trailer: `scope.trailer`). `"unknown"` → Chặn creative cho tới khi hỏi được bên cấp phép.
- Nhạc: license ghi rõ dùng cho quảng cáo trả tiền, trên đúng nền tảng sẽ chạy. Nhạc thư viện "thương mại" của nền tảng (Meta
  Sound Collection, TikTok Commercial Music Library) chỉ dùng cho quảng cáo trên chính nền tảng đó; thư viện nhạc thường / trending
  của mạng xã hội không dùng được cho quảng cáo → Chặn (NL 5.6).
- Trước khi chạy video trên YouTube / Google: tải lên ở chế độ riêng tư để xem có claim Content ID không.
- Không dùng footage, nhân vật, UI, nhạc của game khác hay phim, kể cả cảnh "game khác chán, game mình hay" → Chặn. Mạng quảng
  cáo cấm đích danh việc dùng clip từ nguồn không được phép và IP giải trí lớn.
- Chữ quảng cáo không chứa tên game, nhãn hiệu của đối thủ → Chặn. Đặt giá từ khoá là tên đối thủ: Google Ads, Apple Ads cho phép
  theo chính sách nền tảng; [DUYỆT] quyết theo thị trường.
- Người thật, người nổi tiếng, giọng giống người nổi tiếng (kể cả do AI) → cần hợp đồng quyền hình ảnh → không có thì Chặn.
- Gameplay không đúng với game thật: mạng quảng cáo đều cấm "gameplay giả, UI giả, phần thưởng giả", vi phạm lặp lại có thể mất
  cả tài khoản quảng cáo; ASA (Anh) đã gỡ nhiều quảng cáo game theo năm kiểu ở NL 5.6; Apple xét cả quảng cáo ngoài store (2.3.1).
  → Cảnh báo; quảng cáo minigame như game chính, cinematic như gameplay, footage của game khác → Chặn.
- Playable mang asset và code thật, tải về được: chỉ chứa asset có `scope.ads` true; agency làm playable phải giao sổ kèm file.
- Agency, creator: hợp đồng giao quyền (4.9), cam kết nguồn, sổ đi kèm mỗi file giao.

### Nhóm D: bí mật kinh doanh, ứng phó

#### 4.18 IP-18: không mang tài sản của công ty khác vào **[DEV] [PD] [REL]**

- Không đưa vào repo, kho tài liệu: tài liệu, số liệu, code, build của công ty cũ hay đối thủ; code dịch ngược từ game khác; file
  cài đặt game khác (.apk, .ipa, .obb).
- Nghiên cứu đối thủ được: chơi, đọc số liệu công khai, đo, ghi nhận xét bằng lời của mình (quy tắc gương). Không: dịch ngược,
  trích asset, chép tài liệu nội bộ của họ.
- Máy: tìm file .apk, .ipa, .obb; code có dấu vết dịch ngược (chú thích kiểu `// Token: 0x…`, `// RVA: 0x…`, `// Decompiled
  with …`); tài liệu có chữ "Confidential", "Internal" kèm tên công ty khác → Chặn (code, build), Cảnh báo (tài liệu).
- Nhân sự mới ký cam kết không mang tài sản công ty cũ (NL 8: Zynga v. Patmore, Nexon v. Ironmace).

#### 4.19 IP-19: giữ bí mật của mình **[REL] [DEV]**

- Bí mật kinh doanh chỉ được bảo vệ khi công ty có biện pháp giữ hợp lý. Checklist: quyền repo, kho tài liệu theo người, thu
  hồi khi nghỉ; điều khoản bảo mật trong hợp đồng; tài liệu kinh tế, drop rate, số liệu UA đánh dấu mật; không gửi qua kênh cá
  nhân.
- Máy: bản build không mang tài liệu thiết kế, bảng kinh tế (file .md, .docx, .xlsx, .pdf, .csv thiết kế trong `StreamingAssets`,
  `Resources`) → Cảnh báo.
- Mức: Ghi chú (checklist), Cảnh báo (phần máy).

#### 4.20 IP-20: sẵn sàng ứng phó **[REL]**

- Có người đầu mối và người thay; email developer trên Apple, Google, Steam, mạng quảng cáo được đọc hằng ngày (thông báo gỡ
  có hạn tính bằng ngày, NL 6.3).
- Biết làm gì theo `GameIP_UngPho.md`; có sẵn gói bằng chứng của game (mục 9.1).
- Mức: Ghi chú (checklist trước release đầu tiên).

---

## 5. Việc theo vai trò

### 5.1 Art, animation, sound **[ART]**

| Lúc | Làm |
|---|---|
| Nhập pack, tải asset | Ghi sổ ngay (hoặc chạy `survey` để máy điền `draft`), đọc loại license (Standard / Editorial / NC…) |
| Vẽ theo tham chiếu | Tham chiếu để ngoài `Assets/`; học ý tưởng, không trace, không phỏng nét vẽ, bảng màu, tỷ lệ nhân vật của game cụ thể |
| Dùng AI | Ghi công cụ, gói, prompt, ảnh tham chiếu, phần sửa; không dùng art game khác làm tham chiếu |
| Giao outsource | Hợp đồng mẫu của công ty (4.9); yêu cầu khai nguồn và file gốc |
| Lưu trữ | Giữ file gốc (.psd, .spine, .blend, project nhạc) trong kho của công ty: là bằng chứng khi đi khiếu nại |

### 5.2 UA, marketing **[UA]**

| Lúc | Làm |
|---|---|
| Brief creative | Không lấy footage, nhân vật, UI, nhạc của game khác; ghi game tham chiếu của creative |
| Chọn nhạc, footage, font, voice | Chỉ nguồn có license quảng cáo trả tiền trên đúng nền tảng |
| Trước khi chạy | Chạy `creative` của skill trên thư mục creative; sổ creative đủ, không `"unknown"` ở `ads` |
| Store listing | 4.16; preview Apple quay từ app thật |
| Agency | Hợp đồng giao quyền, sổ kèm mỗi file giao |

### 5.3 Product, game design **[PD]**

| Lúc | Làm |
|---|---|
| Brief feature | Ghi game tham chiếu; nếu feature bám một game cụ thể thì làm thẻ so sánh (mục 6) trước khi art làm |
| Đặt tên feature, class | Theo chức năng, không theo tên game khác |
| Đặt tên game | Mục 7 trước khi tên ra khỏi công ty (kể cả trên store test) |
| Nội dung | Tên, hình hư cấu cho xe, súng, giải đấu, người thật (4.15) |
| Tài liệu kinh tế | Đánh dấu mật, giữ trong kho có phân quyền (4.19) |

### 5.4 Dev, tech art **[DEV]**

| Lúc | Làm |
|---|---|
| Thêm thư viện, package, middleware | Đọc license, ghi sổ; GPL / AGPL không vào app; ghi ngưỡng doanh thu, seat |
| Màn Credits | Có màn Credits / Licenses đọc nội dung sinh từ sổ (`notices`) |
| Build | Cài gói Unity `com.ezg.game-ip`; build một lần để có danh sách build thật; muốn thì bật chặn build (mặc định tắt, chỉ chặn bản release) |
| Không | Code dịch ngược, code copy không license |

### 5.5 Producer, release **[REL]**

| Lúc | Làm |
|---|---|
| Đầu project | Tạo `GameIPProject.json` (thị trường, đối thủ); sổ lần đầu (`survey`, `init`) |
| Trước soft launch | Đăng ký nhãn hiệu tên game ở thị trường chính (mục 7.3); gói bằng chứng (9.1) |
| Mỗi release | `check`; mọi Chặn đã sửa hoặc có nợ được duyệt; màn Credits cập nhật |
| Mỗi quý | Doanh thu, số người dùng so với ngưỡng license (4.5); license sắp hết hạn |
| Hằng ngày | Email developer trên store, mạng quảng cáo có người đọc (4.20) |

---

## 6. Thẻ so sánh với game tham chiếu

Theo cách toà xét (trừu tượng hoá → lọc → so sánh, NL 5.2) cộng lớp "người duyệt store" (NL 5.1). Một thẻ cho một phần (một
feature, một màn hình, một nhân vật, hoặc cả game ở giai đoạn concept).

### 6.1 Lớp store (một phút)

Đặt cạnh nhau icon, tên, ba screenshot đầu, 30 giây đầu video của hai game. Câu hỏi: người chưa biết cả hai game có nghĩ đây là
cùng một game, hay cùng một nhà làm, không? **Có** → Đỏ ngay, không cần lớp sau.

### 6.2 Trừu tượng hoá và lọc

Liệt kê các yếu tố giống nhau theo tầng, từ chung tới cụ thể: ý tưởng, thể loại; vòng lặp chính; luật; cấu trúc màn,
progression; bố cục màn hình, HUD; nhân vật, vật phẩm (hình); bảng màu, tỷ lệ; animation, hiệu ứng phản hồi; âm thanh; chữ, tên.

Với mỗi yếu tố giống, hỏi theo thứ tự và ghi bộ lọc loại nó:

| Hỏi | Có thì | Mã |
|---|---|---|
| Đây là luật, ý tưởng, cơ chế (viết được thành câu "nếu… thì…") hay là hình ảnh, cách thể hiện của luật đó? | Luật, ý tưởng → loại | L |
| Có cách làm khác không? | Không có (hoặc rất ít) → loại | M (merger) |
| Ai làm thể loại này cũng làm thế (do thể loại, đề tài, nền tảng, kỹ thuật)? | Có → loại | S (scènes à faire) |
| Nó tồn tại vì công dụng (nút ở chỗ ngón cái với tới, thanh máu ở góc)? | Có → loại | F |

Lọc từng yếu tố, không lọc cả game một lần ("match-3 là thể loại chung nên lọc hết" là sai). Kiểm tra nhanh: yếu tố còn lại sau
lọc là những gì game kia chọn trong khi còn nhiều cách khác.

### 6.3 So phần còn lại

- So bằng mắt người chơi bình thường, trên tổng thể (total concept and feel), không soi chi tiết nhỏ.
- Tổ hợp: nhiều yếu tố riêng lẻ đều bị lọc, nhưng cách chọn và xếp chúng thành chuỗi (chuỗi tiến hoá, bộ luật đặc biệt, thứ tự
  giới thiệu luật, bố cục tổng thể) có còn giống gần nguyên không (4.12)?
- Phép thử: nếu phải nheo mắt mới thấy khác, hoặc bảng "điểm khác" cần dài nhiều trang để thuyết phục, là đang giống.

### 6.4 Kết luận

| Mức | Khi |
|---|---|
| **Đỏ** | Lớp store: có nhầm; hoặc có asset, hình vẽ chép / phỏng (4.11); hoặc phần còn lại sau lọc giống đáng kể trên tổng thể; hoặc tổ hợp chọn và sắp xếp giống gần nguyên |
| **Vàng** | Phần còn lại có vài yếu tố biểu đạt giống, riêng lẻ, đổi được mà không đổi gameplay |
| **Xanh** | Mọi yếu tố giống đều bị lọc (L, M, S, F) |

Khi cách làm trùng một vụ đã xử ở NL 5.4 (vd chuỗi tiến hoá kiểu Triple Town / Yeti Town), ghi **Đỏ, trùng án lệ <vụ>**: rủi
ro nặng hơn Đỏ do nhận định, [DUYỆT] xem trước.

Thẻ luôn kết thúc bằng hai phần:

1. **"Nếu ai muốn dựng hồ sơ chống lại mình, họ tìm thấy gì dễ nhất?"**: một đến ba thứ cụ thể.
2. **Đề xuất đổi**: cụ thể tới mức art, design làm được (đổi hình dáng nhân vật chính, bảng màu, thứ tự giới thiệu luật, chuỗi
   tiến hoá…), không chỉ "làm khác đi".

Thẻ lưu ở `Docs/GameIP/compare/<phần>.md` của project, có ngày và người làm. Thẻ Đỏ chuyển [DUYỆT].

---

## 7. Tên game, nhãn hiệu

### 7.1 Chọn tên

1. Ưu tiên tên tự bịa hoặc gợi ý; tránh tên chỉ mô tả gameplay ("Merge Farm", "Idle Miner") vì khó đăng ký và khó ngăn người khác
   dùng.
2. Không chứa tên game, nhân vật, brand của người khác; không chơi chữ gần với tên game nổi tiếng cùng thể loại.
3. Đọc to: so cách đọc, cách viết, cảm giác chung với tên game cùng thể loại, không chỉ so từng chữ.

### 7.2 Tra trùng (người làm, có chủ đích)

- Store: App Store, Google Play, Steam: tên gần giống trong cùng thể loại.
- Cơ sở dữ liệu nhãn hiệu của từng thị trường trong `markets` (skill sinh link; danh sách và cách tra ở NL 7). Tra ở nhóm hàng của
  game (NL 3.4).
- Kết quả và ngày tra ghi vào `Docs/GameIP/names/<tên>.md` của project.

### 7.3 Đăng ký

- Nhãn hiệu: tên game, logo, tên studio; ở Việt Nam và các thị trường chính; nhóm 9, 41 (thêm 28 nếu có merch). Nộp trước
  khi công bố, trước soft launch, vì phần lớn thị trường theo nguyên tắc nộp đơn trước và game thành công sẽ bị người khác nộp tên
  trước (NL 3.1).
- Madrid: một đơn qua Cục SHTT chỉ định nhiều nước, nhưng phụ thuộc đơn gốc Việt Nam 5 năm: đơn gốc yếu (tên mô tả) thì cả hệ
  thống yếu (NL 3.4).
- Trung Quốc (kể cả khi chưa phát hành ở đó): nộp sớm cả tên Latin và tên chữ Hán (NL 3.4).
- Mỹ: bắt buộc qua luật sư được cấp phép ở Mỹ (NL 3.4).
- Bản quyền: tự có ở mọi nước Berne; ở Mỹ đăng ký trước khi bị chép hoặc trong 3 tháng sau khi phát hành mới đòi được bồi thường
  luật định và phí luật sư (NL 2.6).
- [DUYỆT] quyết thị trường nào, khi nào, qua đại diện nào.

---

## 8. Creative UA: checklist trước khi chạy

1. Sổ creative (`creative.json`) đủ mục cho mọi thứ trong file (4.17); lệnh `creative` của skill không còn ✗.
2. Không mục nào `ads` là `false` hay `"unknown"`.
3. Nhạc: license quảng cáo trả tiền, đúng nền tảng, còn hạn; không dùng thư viện nhạc "chỉ trong nền tảng" cho quảng cáo chạy
   nơi khác.
4. Không footage, nhân vật, UI, logo, nhạc của game, phim, thương hiệu khác.
5. Không người thật, giọng người nổi tiếng khi không có hợp đồng.
6. Gameplay đúng với game: không quảng cáo minigame như game chính, không cinematic như gameplay, không footage của game khác;
   dòng chú thích nhỏ không cứu được ấn tượng sai (NL 5.6). Không dùng làm preview App Store khi không phải quay từ app.
7. Playable: chỉ asset có `scope.ads` true.
8. Lưu bản cuối + sổ + ngày chạy: là bằng chứng khi creative của mình bị chép.

---

## 9. Giữ IP của mình

### 9.1 Gói bằng chứng của game

Giữ, cập nhật mỗi release lớn: lịch sử git (ngày tạo nhân vật, màn, cơ chế), file gốc art, nhạc; ngày phát hành từng nước;
ảnh chụp store listing theo thời gian; creative đã chạy kèm ngày; sổ nguồn gốc; hợp đồng chuyển quyền. Khi bị chép, đây là thứ
luật sư cần đầu tiên.

### 9.2 Đăng ký và giám sát

- Nhãn hiệu: mục 7.3.
- Bản quyền: đăng ký key art, nhân vật chính, game ở Mỹ trong 3 tháng sau phát hành (đăng ký theo nhóm cho art 2D rẻ), và ở
  Việt Nam làm chứng cứ (NL 2.6) *(chờ [DUYỆT] chọn)*.
- Giám sát: tìm tên, icon của mình trên store và thư viện quảng cáo công khai định kỳ *(đề xuất: mỗi tháng sau soft launch)*.

### 9.3 Khi bị nhái

Làm theo `GameIP_UngPho.md` phần "khi mình bị nhái": chọn đúng kênh theo loại vi phạm (bản quyền, nhãn hiệu, mạo danh), gửi qua
[DUYỆT].

---

## 10. Quy trình

### 10.1 Khi nào chạy

| Lúc | Gate |
|---|---|
| Nhập asset, pack, thư viện | 4.1–4.9 cho phần mới |
| Brief feature, đặt tên | 4.10–4.15 |
| Mỗi release | Toàn bộ nhóm A, C; nhóm B cho feature mới; 4.18, 4.19 phần máy |
| Mỗi creative | 4.17 |
| Đổi thị trường, nền tảng | 4.14, 4.16, 4.17 |

### 10.2 Nợ

Vi phạm chưa sửa được ghi vào `ProjectSettings/GameIPDebt.json`: gate, asset, lý do, người [DUYỆT] chấp nhận, hạn. Hết hạn mà
chưa sửa thì quay lại mức gốc. Không có nợ cho: asset không rõ nguồn (4.1) ở bản phát hành, GPL / AGPL (4.5), logo thật (4.15),
footage của game khác trong creative (4.17).

### 10.3 Phiên bản tài liệu

Luật store, điều khoản công cụ, vụ việc thay đổi. Mỗi luật trong NL ghi ngày tra; rà lại toàn bộ ít nhất mỗi sáu tháng và khi
store đổi guideline. Thay đổi ghi ở CHANGELOG của gói.
