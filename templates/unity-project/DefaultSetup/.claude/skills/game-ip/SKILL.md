---
name: game-ip
description: "Kiểm bản quyền, nhãn hiệu, bí mật kinh doanh cho game Unity làm ở Việt Nam, phát hành toàn cầu, theo quy chuẩn Game IP của team (GameIP_QuyChuan.md, gói com.ezg.game-ip): nguồn gốc và license của asset vào build (pack Asset Store, font, nhạc, sfx, stock, thư viện code, middleware có ngưỡng doanh thu, asset AI, vẽ thuê), sổ nguồn gốc GameIPLedger.json, gate IP-1 … IP-20, màn Credits / Licenses; so feature, màn hình, art với game khác (giống tới mức bị gỡ, bị kiện không); đặt tên game, icon, đăng ký nhãn hiệu; store listing; creative UA, nhạc quảng cáo, playable; khi nhận DMCA, bị gỡ app, thư luật sư, hoặc bị game khác nhái. Dùng khi nghe \"check bản quyền\", \"kiểm license\", \"asset này dùng được không\", \"font này nhúng được không\", \"nhạc này chạy ads được không\", \"giống game X quá không\", \"đặt tên game\", \"bị DMCA\", \"bị clone\", \"DMCA\", \"IP\", \"copyright\", \"trademark\", kể cả khi user không nhắc tới quy chuẩn. English triggers: \"copyright check\", \"license audit\", \"is this asset safe to ship\", \"we got a DMCA notice\", \"someone cloned our game\". Không dùng cho tối ưu asset, animation, VFX, UI motion, hay tư vấn pháp lý cho một vụ cụ thể thay luật sư."
argument-hint: "[việc cần kiểm] [file / folder / tên game]"
---

# Game IP: kiểm bản quyền và IP của game, ở mọi project

**Skill không phải luật sư.** Nó tìm và xếp rủi ro, đưa bằng chứng, nói rõ chỗ nào máy chắc (file, metadata, license) và chỗ nào
là nhận định (giống hay không). Kết luận pháp lý và mọi quyết định "vẫn ship" ở mức Chặn là của người duyệt công ty chỉ định
**[DUYỆT]**. Không bao giờ nói "an toàn", "không vi phạm": nói mức (✓ ✗ !, Xanh / Vàng / Đỏ), căn cứ, và việc tiếp theo.

Skill **không chứa thông tin của project nào**. Ba nguồn:

| Nguồn | Giữ gì | Ở đâu |
|---|---|---|
| Quy chuẩn (QC), nguyên lý (NL), sổ tay ứng phó | Luật chung, luật theo thị trường, án lệ, nguồn | Project có gói: `Packages/com.ezg.game-ip/Documentation~/` (hoặc `Library/PackageCache/com.ezg.game-ip@*/Documentation~/`). Không có gói: `<skill>/docs/` (bản chép, cùng bản). Đọc `GameIP_QuyChuan.md` trước; tra lý do ở `GameIP_NguyenLy.md`; khiếu nại ở `GameIP_UngPho.md` |
| File riêng của project | Thị trường, game tham chiếu, đối thủ, tên của mình, sổ nguồn gốc, nợ | `ProjectSettings/GameIPProject.json`, `GameIPProject.md`, `GameIPLedger.json`, `GameIPDebt.json` |
| Script | Khảo sát, sinh sổ, chạy gate, sinh notice | `scripts/gameip.mjs` (Node 18+, không cần cài gì) |

`<skill>` là folder chứa file này. Lệnh chạy từ gốc project Unity hoặc truyền `--project <gốc>`.

## 1. Luật làm việc

1. **Chỉ kiểm project user nhờ.** Project user đưa để tham khảo (xem cấu trúc, xem cách làm) không phải đối tượng kiểm.
2. **Đọc thì tự làm, ghi thì hỏi.** `survey`, `check` chỉ ghi báo cáo vào `Logs/` của project (hoặc `--out`). `init`, `notices` ghi
   vào `ProjectSettings/` hay `Assets/`: hỏi user trước ở project chưa có file Game IP.
3. **Không bao giờ đặt `status: "confirmed"`** trong sổ. Máy chỉ điền `draft`; người có trách nhiệm (QC 3.5) xác nhận. Điền `proof`,
   `scope.ads` theo lời user thì được, kèm ghi chú ai nói.
4. **Không gửi gì của project ra ngoài**: tên game chưa công bố, asset, prompt không đem đi tra web, dịch vụ ngoài khi user chưa
   cho từng lần (tên chưa công bố là bí mật kinh doanh). Tra nhãn hiệu: đưa link để người mở (NL 7).
5. **Khi có khiếu nại**: làm theo `GameIP_UngPho.md`; mục 0 (năm việc không làm) nói trước mọi thứ. **Không soạn thư trả lời bên
   khiếu nại** (kể cả "bản nháp" gửi bên kia): việc đó là của [DUYỆT] / luật sư. Counter-notice, khiếu nại của mình gửi nền tảng:
   soạn được **bản nháp** cho [DUYỆT], ghi rõ NHÁP, CHƯA GỬI; không khuyên gửi ngay.
6. **Không giúp che giấu**: không đề xuất xoá, sửa, đổi tên file hay lịch sử để giấu dấu vết; sửa là sửa thiết kế, thay asset.
7. **Luật có ngày tra**: chỗ NL ghi *(chưa đối chiếu gốc)* thì nói vậy khi dùng; luật thay đổi thì nói ngày tra trong NL.

## 2. Theo việc

### 2.1 "Check bản quyền project", trước release (QC 4 nhóm A, C; QC 10.1)

```bash
node <skill>/scripts/gameip.mjs survey --project <gốc>
# user dặn không đụng project, hoặc project chưa có file Game IP: ghi mọi thứ ra folder khác
node <skill>/scripts/gameip.mjs survey --project <gốc> --out <folder tạm>
```

Với `--out`, mọi lệnh sau cũng truyền cùng `--out` (và `--settings <folder tạm>/ProjectSettings` khi đã `init` vào đó). Đọc
`Logs/GameIPSurvey.md`: asset vào build, pack Asset Store khớp GUID (cache của máy; máy art có thêm: `--asset-store-cache <folder>`),
folder bên thứ ba, package, font, tag âm thanh, metadata ảnh, tên đáng ngờ, code dịch ngược, tên game khác, yêu cầu ghi công, màn
Credits.

- **Chưa có sổ**: hỏi user rồi `init` (tạo `GameIPProject.json`, `.md`, sổ draft). Sổ đã có: `init` ghi mục mới ra
  `GameIPLedger.new.json`; `--merge` gộp thẳng. Giúp user xác nhận từng mục: hỏi đúng thứ máy không biết (ai mua, tài khoản nào, số
  đơn, gói nhạc nào, có được dùng cho ads không, hợp đồng vẽ thuê). Cách đọc sổ: `reference/ledger.md`.
- Rồi:

```bash
node <skill>/scripts/gameip.mjs check --project <gốc> [--fails-only] [--strict]
node <skill>/scripts/gameip.mjs notices --project <gốc> [--to <file màn Credits đọc>]
```

- Báo user theo mẫu mục 3. Mỗi ✗: nói asset nào, vì sao (mã gate + mục QC), sửa thế nào; ✗ không sửa được ngay thì [DUYỆT] quyết
  nợ (`reference/ledger.md`, QC 10.2; có loại không được nợ).

### 2.2 "Feature / màn hình / art này giống game X quá không" (IP-10, IP-11, IP-12; QC 6)

1. **Đối chiếu án lệ trước** (bảng NL 5.4): game X hay cách làm của user trùng một vụ đã xử (vd Triple Town / Yeti Town, Tetris /
   Mino, Farm Heroes Saga / Forest Mania) thì nêu tên vụ ngay đầu kết luận. Mô tả game X trong NL 5.4 là nguồn dùng được.
2. Sinh thẻ và (khi có ảnh của cả hai bên) so ảnh:

   ```bash
   node <skill>/scripts/gameip.mjs compare "<phần của mình>" --ref "<game tham chiếu>" [--brief "<lời brief nói học theo>"] \
     [--mine <ảnh / folder của mình>]… [--theirs <ảnh / folder tham chiếu, để ngoài Assets/>]…
   ```

   Không cần project Unity (duyệt art, tên trước khi có project vẫn chạy được; khi đó file ghi vào thư mục hiện tại hoặc `--out`).
   Tên game tham chiếu không khớp vụ nào thì thêm `--genre <thể loại>` (match3, merge, puzzle, shooter, rpg…) để xem án lệ cùng
   thể loại. Lệnh in án lệ trùng (data/cases.json, từ NL 5.4), ghi `Docs/GameIP/compare/<phần>.md` (thẻ có rồi thì `.new.md`) và
   `<phần>.pairs.md`: cặp ảnh pHash ≤ 10 / 64 bit, cả bản lật ngang; đọc PNG, JPEG baseline (PSD, JPEG progressive: lưu lại PNG).
   pHash chỉ bắt ảnh gần như cùng ảnh; không bắt ảnh vẽ lại cùng bố cục, ảnh bị cắt ghép: người vẫn xem cạnh nhau. Ghi vào project
   khi user đồng ý; không thì `--out`.
3. Điền thẻ theo QC 6 cùng user, hai lớp: lớp store (một phút, người chưa biết có nhầm không) và lớp toà (trừu tượng hoá → bốn bộ
   lọc L / M / S / F cho **từng** yếu tố → so phần còn lại trên tổng thể, kể cả tổ hợp chọn và sắp xếp). Mẫu:
   `reference/compare-card.md`.
4. Game X không có trong NL 5.4: hỏi user ảnh, video, mô tả của cả hai bên; không đoán gameplay của game X từ trí nhớ khi user đưa
   được nguồn.
5. Ghi nguyên văn chỗ brief / user nói học theo game nào: đó là thứ bên kia dùng làm bằng chứng ý đồ (QC 4.13). Không khuyên xoá;
   khuyên sửa thiết kế.
Kết luận Xanh / Vàng / Đỏ, câu "họ tìm thấy gì dễ nhất", và đề xuất đổi cụ thể. Đỏ → [DUYỆT]. Lưu thẻ ở
`Docs/GameIP/compare/<phần>.md` của project khi user đồng ý.

### 2.3 Đặt tên game, icon, nhãn hiệu (IP-14; QC 7; NL 3, 7)

```bash
node <skill>/scripts/gameip.mjs name "<tên>" [--markets VN,US,EU,UK,JP,KR,CN] [--save]
```

Lệnh chấm độ mạnh (từ mô tả / thể loại ở `data/terms/generic_words.txt`), so với tên game lớn, thương hiệu (`data/terms/`), game
tham chiếu và đối thủ của project (trùng từ, viết gần giống, đọc gần giống), giới hạn 30 ký tự của Apple, và in link tra theo
`markets`; mã thoát 1 khi có ✗. Danh sách của skill có hạn: thêm game cùng thể loại user biết vào `competitors` rồi chạy lại.
Không tự tra web tên chưa công bố. Nhắc: nộp đơn trước khi công bố ở thị trường nộp-trước-được-trước; Madrid phụ thuộc đơn gốc
Việt Nam 5 năm; Trung Quốc nộp cả tên Latin và chữ Hán; Mỹ bắt buộc luật sư Mỹ. Icon: người so (QC 4.14), hoặc `compare` với ảnh
icon.

### 2.4 Creative UA, trailer, playable, store listing (IP-17, IP-16; cùng IP-4, 7, 8, 15 cho file trong creative; QC 4.16, 4.17, 8)

```bash
node <skill>/scripts/gameip.mjs creative <folder creative> --init   # lần đầu: sổ nháp creative.json trong folder (không ghi đè)
node <skill>/scripts/gameip.mjs creative <folder creative>          # kiểm; ghi GameIPCreative.md vào folder (hoặc --out)
```

Sổ `creative.json`: `networks` (mạng sẽ chạy), `storePreview` (dùng làm preview App Store), `entries` (mỗi nguồn: `kind` như
`gameplay`, `music`, `platform-music` + `platforms`, `footage`, `actor`, `voice`, `ai`, `asset`, `font`; `scope.ads`, `proof`,
`notInGame`, `paths` tương đối với folder). `--name "<tên>"`: tên creative ghi trong sổ, báo cáo (mặc định tên folder). Mục `--init`
chỉ gộp theo đuôi file: tách cùng user thành từng nguồn thật. Chữ quảng cáo (playable) nhắc tên game khác, đối thủ → ✗. Lệnh bắt: file
chưa có nguồn, nguồn chưa được dùng cho ads, nhạc của nền tảng chạy ngoài nền tảng, người thật không hợp đồng, tag âm thanh,
metadata ảnh, dấu hiệu AI, cảnh không có trong game (Chặn khi `storePreview`), playable gọi mạng ngoài, quá 5 MB, nhắc tên game /
thương hiệu. Video máy không xem được: đi checklist QC 8 với user (footage, người thật, gameplay đúng với game: năm kiểu ASA ở NL 5.6).

### 2.5 Asset AI (IP-7; QC 4.7; NL 4.3)

Hỏi công cụ, gói tài khoản lúc tạo, prompt, ảnh tham chiếu. Prompt hay tham chiếu gọi tên, dùng hình game / nhân vật / nghệ sĩ cụ
thể → Chặn. Quyền với đầu ra theo công cụ và theo gói (NL 4.3). Ghi mục sổ `kind: ai`.

### 2.6 Bị khiếu nại, bị gỡ, bị nhái (IP-20; `GameIP_UngPho.md`)

Đọc hết `GameIP_UngPho.md` trước khi trả lời, không chỉ đoạn này. Luôn nói ngay ba điều:

- Không trả lời trực tiếp bên khiếu nại, không lên tiếng công khai (UngPho 0.1, 0.4).
- Không gửi counter-notice khi chưa có [DUYỆT]: đó là cam đoan dưới hình phạt khai man và đồng ý cho toà liên bang Mỹ xét xử (UngPho
  0.3, 3.1).
- Không xoá, sửa file, lịch sử git, listing cũ; khoá bằng chứng (UngPho 0.2, 2).

Rồi phân loại theo bảng mục 1, làm mục 2 (gói hồ sơ: sổ nguồn gốc, file gốc, lịch sử tạo), rồi mục 3 hoặc 4. Nói hạn thật (vd
Google khôi phục sau 10–14 ngày làm việc nếu bên kia không kiện). Nhắc user: lời "chắc chắn tự làm" cần bằng chứng (`proof` của mục
`inhouse` trong sổ, `reference/ledger.md`).

## 3. Báo kết quả

```
Kiểm Game IP: <game> (<ngày>), skill game-ip <bản>
Chặn (✗): <số> · Cảnh báo (!): <số> · sổ phủ <x>% asset vào build (<n> đã xác nhận)
1. ✗ IP-3 Assets/…/Locked.ttf: fsType Restricted, không được nhúng → mua license app hoặc đổi font OFL (QC 4.3)
…
Không chạy trong check: IP-10, 11, 12 (compare), 14 (name), 16, 17 (creative), 20 → <việc người phải làm>
Không phải tư vấn pháp lý; ✗ còn lại cần [DUYỆT] quyết.
```

Ưu tiên theo QC 0 (nguyên tắc 4): thứ dễ chứng minh nhất trước (asset chép, nhạc, font, tên, icon, creative), rồi tới "giống".
Dòng ✗ có chữ "không được ghi nợ" (IP-1 thiếu nguồn, IP-5 copyleft mạnh…): nói rõ là phải sửa, không xin nợ được.

## 4. Lệnh

| Lệnh | Làm gì | Ghi |
|---|---|---|
| `survey` | Khảo sát, chỉ đọc | `Logs/GameIPSurvey.md`, `.json` |
| `init [--merge]` | Survey + tạo file riêng, sổ draft (không ghi đè; sổ có rồi thì `.new.json`) | `ProjectSettings/GameIP*` |
| `check [--strict] [--fails-only]` | Gate máy kiểm được: IP-1 … 9, 13, 15, 18, 19; mã thoát 1 khi có ✗ (`--strict`: cả !) | `Logs/GameIPCheck.md`, `.json` |
| `notices [--to <file>]` | Nội dung màn Credits / Licenses từ sổ | `credits.noticesFile` hoặc `Logs/ThirdPartyNotices.txt` |
| `compare <phần> --ref <game> [--mine …] [--theirs …] [--brief …]` | Thẻ so sánh có án lệ trùng; cặp ảnh pHash | `Docs/GameIP/compare/` |
| `name "<tên>" [--markets …] [--save]` | Chấm tên, gần tên nào, link tra nhãn hiệu | in ra; `--save`: `Docs/GameIP/names/` |
| `creative <folder> [--init]` | Kiểm creative theo `creative.json` | `<folder>/GameIPCreative.md` |
| `selftest [--keep]` | Tự kiểm trên project giả | không để lại gì |

Chung: `--project <gốc>`, `--out <folder>` (ghi mọi thứ ra folder khác, để chạy không đụng project), `--settings <folder>` (chỗ file
riêng, mặc định `ProjectSettings/`), `--asset-store-cache <folder>` (lặp được), `--no-default-cache`, `--quiet`.

**Gói Unity `com.ezg.game-ip`** (phần Editor, project có thể chưa cài): nhãn nguồn gốc trên Inspector; sau mỗi lần build ghi
`Library/GameIP/build_assets.json` (danh sách thật, `survey` / `check` tự gộp vào, báo cáo ghi lần build nào); chặn build theo IP-1,
**mặc định tắt**, bật ở Project Settings → Game IP hoặc `buildGate` trong `GameIPProject.json` (chỉ bản release, trừ khi bật
`developmentBuilds`; `failOnDraft` chặn cả mục draft). Bật / tắt chặn build là quyết định của project: hỏi user, không tự bật. Menu
Tools → Game IP → Kiểm asset vào build so với sổ: cùng kiểm IP-1 mà không cần build.

Giới hạn của bản này: phần nhận định (lọc, so tổng thể, kết luận của thẻ so sánh; icon; store listing; nội dung video; ứng phó) là
của người theo QC. IP-15 chỉ bắt tên thương hiệu trong chữ, không bắt logo, hình dạng đặc trưng. Phần "vào build" đọc YAML theo
GUID: gần đúng, trừ khi project có gói Unity và đã build ít nhất một lần.
