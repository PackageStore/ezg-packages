# QA playbook — /auto-test-device

Tài liệu nghề cho agent QA: tư duy, lập charter, thiết kế ca test, checklist visual/logic/ổn định, rubric mức độ,
chuẩn viết bug, phân biệt nhiễu, chọn ca theo rủi ro, ghi tri thức về repo. Áp dụng cho mọi game; chỗ nào nhắc
tên quy ước template EZG (`PlayerDataManager`, `TimeManager`, `UnlockFeature`…) là **ví dụ mặc định** — giá trị thật
đọc từ `.claude/project-profile.json["qa"]` + `.claude/docs/QaProfile.md`. Oracle/invariant chi tiết:
[oracles.md](oracles.md). Ma trận môi trường: [env-matrix.md](env-matrix.md).

---

## 1. Tư duy QA

- **Người chơi thật làm gì, mình làm đó — rồi làm thêm điều người chơi vụng/hấp tấp/tò mò làm.** Bấm hai lần,
  bấm trong lúc animation chạy, thoát giữa chừng, quay lại sau một lúc, mất mạng, xoay máy, bấm Back.
- **Mỗi hành động có một kỳ vọng viết ra trước** — thành `--expect` của lệnh `tap`. Không có kỳ vọng thì không
  phát hiện được sai, và không phân biệt được "game sai" với "mình đoán sai".
- **Đo, đừng ước.** Ghi số trước/sau (`state`, số trên ảnh gốc). "Có vẻ đúng" không phải kết quả.
- **Nghi công cụ trước, nghi game sau.** `selfcheck` pass rồi mới tin finding. Một `expectation-failed` là nghi
  vấn; hai lần, có ảnh, mới là bug.
- **Một lỗi → tìm anh em của nó.** Thấy tràn chữ ở một nút → kiểm mọi nút cùng kiểu (pseudo-loc giúp quét nhanh).
  Thấy thưởng nhân đôi khi spam → thử mọi nút nhận thưởng khác.
- **Tái hiện tối thiểu.** Trước khi log, rút ngắn bước tới ít nhất có thể vẫn ra lỗi.
- **Phân biệt bug với thiết kế.** Nghi là thiết kế (không có tài liệu nói) → log `--type gd` với câu hỏi rõ ràng,
  mức độ low/medium, đừng khẳng định là sai.
- **Không test màn chết.** Enum có entry ≠ màn còn sống. Màn không ai mở (`qa.liveness`, autotest `Feature.Dead`)
  không có bug — chỉ ghi "màn chết, nên dọn" một dòng trong report.
- **Tri thức là của repo.** Điều học được về game này (cheat nào dùng được, màn nào hay hỏng, finding nào là nhiễu)
  ghi vào `QaProfile.md`/charter/profile ngay trong lượt — phiên sau (có thể là agent khác) dùng lại được.

## 2. Charter — ca có mã, oracle có nguồn, lưu trong repo

Khung: [charter.template.md](charter.template.md) (`$QA charter new <feature>` tạo từ khung này vào
`qa.paths.chartersDir`, mặc định `.claude/docs/qa/charters/<feature>.md`). Charter là **tài sản của project**: dev
commit cùng feature, phiên sau đọc lại và bổ sung. Vì vậy:

- **Chỉ ca + oracle, không kết quả.** Kết quả nằm trong `cases.jsonl` của phiên (`note --case`). Không ghi "pass
  ngày X", không ghi số bug vào charter.
- **Mã ca ổn định:** cột `#` dạng `<P?>-<nn>` (vd `P0-01`, `P2-07`). Ca bỏ → giữ dòng, ghi "bỏ — lý do", không đánh
  lại số (phiên cũ tham chiếu mã đó). Ca mới thêm vào cuối nhóm.
- **Tên charter** = tên file không đuôi, snake/kebab theo tên tính năng trong code (`shop`, `daily-quest`). Mã ca
  đầy đủ dùng cho `note --case` là `<charter>/<#>`, vd `shop/P1-03`.
- **Oracle có nguồn kiểm được:** `csv CoinPack:2` (lệnh `csv` đọc được), `file.cs:123`, `GDD/x.md §3`. Suy luận →
  ghi "suy luận" để bug dựa vào đó mang `--type gd`.
- **Invariant** của tính năng (điều luôn đúng mọi lúc, oracles.md §1) liệt kê riêng — kiểm sau mỗi nhóm ca.
- Tính năng lớn → nhiều charter nhỏ theo màn/hệ thống, không một charter 60 ca.

## 3. Thiết kế ca test

### 3.1 Happy path (P0)
Đi trọn vòng: vào tính năng từ đường người chơi thật dùng → thực hiện hành động chính → nhận kết quả (thưởng, mở
khoá, chuyển màn) → thoát về. Kiểm mọi con số trên đường đi với oracle. Lặp lại một lần nữa (lần 2 hay lộ lỗi
state không reset). Mỗi tap có hệ quả kèm `--expect`.

### 3.2 Biên & phân lớp tương đương (P1)
- Tài nguyên: **0**, **ít hơn giá 1 đơn vị**, **đúng bằng giá**, **dư**, rất lớn (format số 1K/1M/1B, tràn chữ).
- Level/tiến độ: level 1, level ngay trước mốc mở khoá, đúng mốc, max level (nút nâng cấp phải đổi trạng thái), vượt max.
- Danh sách: rỗng, 1 phần tử, đầy, nhiều hơn một trang (cuộn tới cuối, phần tử cuối không bị che).
- Thời gian: vừa bắt đầu, sắp hết (còn vài giây), đúng lúc hết, đã hết khi app ở nền.
- Lượt/giới hạn ngày: dùng hết lượt, lượt cuối, sang ngày mới (tua bằng cheat hoặc `env clock +1d` trên emulator).

### 3.3 Chuyển trạng thái (P1)
Vẽ nhanh trong đầu các trạng thái (khoá → mở → đang chạy → xong → nhận → cooldown). Thử **mọi chuyển hợp lệ** và
**chuyển không hợp lệ** (bấm nhận khi chưa xong, bấm mua khi đang xử lý, mở popup khi popup khác đang mở).

### 3.4 Gián đoạn (P2) — ma trận cho mỗi màn/popup chính
| Gián đoạn | Lệnh | Kỳ vọng |
|---|---|---|
| Back hệ thống (Android) | `key back` | đóng popup trên cùng / hỏi thoát ở màn gốc; không đóng nhầm 2 lớp, không kẹt |
| Nền ngắn | `app background --secs 5` | quay lại đúng màn, state giữ, nhạc/timer tiếp tục đúng |
| Nền dài | `app background --secs 60` | timer/offline tính đúng thời gian thực |
| Nền + OS giết activity | `env kill-activities on` → `app background --secs 5` → `env kill-activities off` | như nền ngắn; popup/ads đang mở không kẹt (env-matrix.md) |
| Kill + mở lại (như người chơi) | `app restart` (Home → chờ → tắt) | state đã xác nhận được lưu; luồng dở dang xử lý hợp lý (không mất tiền/nhân đôi thưởng, phần thưởng đang chờ nhận vẫn còn) |
| Crash / OS giết app | `app restart --hard` | không hỏng save, không nhân đôi thưởng; mất vài giây tiến trình gần nhất có thể là thiết kế (lưu theo chu kỳ) — chỉ log khi mất thứ người chơi đã "chốt" và ghi rõ "chỉ khi crash" |
| Mất mạng | `net off` (… `net on`) | thông báo lỗi tử tế, nút thử lại, không treo loading, không trao thưởng khi request chưa thành |
| Spam / double tap | `tap … --times 5 --interval 0.05` | chỉ xử lý 1 lần; tracking chỉ 1 event |
| Bấm trong animation | tap ngay khi popup đang mở / số đang chạy; `env animator 0` để lộ logic chờ animation | không kẹt, không nhận thưởng hai lần |
| Xoay màn (game cho xoay) | `rotate landscape` | layout không vỡ |
| Thông báo hệ thống/cuộc gọi | (giả lập bằng nền ngắn) | như nền ngắn |

### 3.5 Lưu trữ & nhất quán (P3)
- Sau mỗi thay đổi state quan trọng → `app restart` → so state (`state` trước/sau).
- Cùng một con số hiển thị ở nhiều nơi (HUD, popup, màn chi tiết, tooltip) phải khớp và cập nhật cùng lúc.
- Red dot/badge: xuất hiện khi có việc, biến mất khi xong, không còn sau restart nếu đã xử lý.
- Có bản build cũ hơn trong repo/máy → `build update --keep-data`: save của bản cũ nạp vào bản mới không mất gì
  (oracles.md §5).

### 3.6 Ổn định & hiệu năng (P5)
- Lặp vòng chính 5–10 lần, `perf` đầu/cuối: RAM không tăng mãi, FPS không tụt dần.
- Mở/đóng cùng một popup 10 lần (rò rỉ, chồng popup, chậm dần).
- Monkey có kiểm soát: 20–30 chạm nhanh vào các vùng khác nhau của màn tính năng (`tap … --no-shot --no-auto-audit`),
  rồi `shot` + `logs` — game không crash/kẹt.
- `env trim-memory` rồi chơi tiếp 1 phút — texture mất, crash native, LMK.

### 3.7 Thăm dò tự do (2 vòng cuối)
Chọn một "tour": **tour tiền** (theo dòng tiền vào/ra), **tour hậu đậu** (làm sai mọi bước), **tour quay lại**
(rời đi, quay lại theo đường khác), **tour cực đoan** (giá trị lớn nhất/nhỏ nhất), **tour người mới** (không biết
gì, chỉ theo chỉ dẫn trên màn). Ghi `mark "Thăm dò — tour …"`; phát hiện ra ca mới → thêm vào charter.

## 4. Checklist visual (mỗi màn/popup/trạng thái)

**Bố cục:** phần tử lệch tâm/lệch hàng so với anh em; khoảng cách không đều; phần tử chồng lên nhau; bị cắt bởi mép
màn, tai thỏ, thanh điều hướng, viền bo; nút đóng/back khó thấy hoặc khó bấm; nội dung cuối danh sách bị che.

**Chữ:** tràn/cắt/xuống dòng xấu; chữ ra ngoài khung/nút; font sai hoặc thiếu ký tự (ô vuông, dấu tiếng Việt rơi);
còn key localize (`shop_title`), placeholder (`New Text`, `Button`), `{0}`, `NaN`, `null`, số âm vô lý; sai chính
tả, trộn ngôn ngữ; số định dạng không nhất quán (1000 vs 1K vs 1,000); chữ quá nhỏ để đọc; tương phản kém. **Chữ
không đổi khi `pseudoloc on`** = chữ cứng không qua localize.

**Hình ảnh:** ô trắng/hồng (thiếu sprite/shader), ảnh kéo giãn sai tỉ lệ, mờ/vỡ pixel, icon sai item, viền
răng cưa, nền trong suốt bị đen, z-order sai (hiệu ứng nằm dưới nền, popup dưới HUD).

**Trạng thái:** nút disabled trông như enabled (và ngược lại); không có phản hồi khi bấm (không nhấn lún/âm
thanh/hiệu ứng); loading không có chỉ báo; trạng thái rỗng không có thông báo; số không cập nhật sau hành động.

**Style:** đối chiếu `.claude/docs/ArtStyle.md` (palette, kit nút, font, bố cục màn mẫu) — màn lạc tông, nút khác
kit, font lạ.

**Chuyển động:** `burst` khi mở/đóng popup, nhận thưởng, chuyển màn: nhấp nháy một khung, nhảy layout, hiệu ứng
kẹt lại sau khi đóng, đếm số chạy sai giá trị cuối.

**Môi trường (STEP 4, env-matrix.md):** cỡ chữ hệ thống 130%, dark mode, máy dài (20:9) / ngắn (16:9) / tablet,
pseudo-loc — phần tử neo đúng, không khoảng trống lạ, chữ không tràn.

**Audit tự động** (`audit`, auto-audit khi đổi màn; có probe) phủ một phần danh sách trên: tràn/cắt chữ, thiếu
glyph, key localize / `{0}` / NaN / null, nút ngoài màn / ngoài safe area, nút bị che, vùng chạm < 6 mm, Image
thiếu sprite, material lỗi, chữ chồng chữ (finding kebab-case của tool: `text-overflow`, `missing-glyph`,
`missing-sprite`, `missing-material`, `text-outside`, `text-content`, `offscreen`, `unsafe-area`, `blocked`,
`tiny-target`, `text-overlap`). **Probe ≥ 1.3** thêm flag port từ UiAuditUtil của autotest — tool in thành finding
cùng tên (camelCase), kèm `flagDetail`, cùng cơ chế noise:

| Flag probe 1.3 | Nghĩa | Mức gợi ý |
|---|---|---|
| `deadButton` | nút bấm được (interactable) nhưng không gắn handler nào — bấm không làm gì | high nếu là nút người chơi cần; low nếu decor |
| `noRaycastTarget` | phần tử tương tác không có Graphic nhận raycast — bấm không ăn | high |
| `overlapButton` | hai nút cùng cha chồng nhau > 50% — bấm trúng nút nào tuỳ thứ tự | medium |
| `missingSprite` | Image không sprite, hiện khối màu | medium (ô trắng ở chỗ icon/ảnh) |
| `solidBackground` | Image không sprite phủ ≥ 50% màn — thường backdrop cố ý | Info — chỉ bug khi không phải backdrop |
| `noSprite` (mở rộng) | ô trắng đục có con (panel không sprite chứa nội dung) | medium nếu không đúng kit ArtStyle |
| `overflow` · `missingGlyph` · `outsideContainer` · missing material | có từ 1.2.1, đã map vào finding kebab-case ở trên | theo finding |

Với mọi finding: nhìn ảnh `*.audit.jpg` → đúng thì bug, sai thì xem §11 (nhiễu).

## 5. Checklist logic theo hệ thống thường gặp

Chi tiết cách đo + invariant: oracles.md.

| Hệ thống | Kiểm |
|---|---|
| Tiền tệ / kinh tế | trừ/cộng đúng số, đúng một lần; không âm; số lớn không tràn; nguồn/đích khớp GDD; tracking 1 event/giao dịch |
| Phần thưởng | nhận đúng loại + số lượng theo CSV; popup thưởng khớp số thực nhận; không nhận lại sau restart/spam |
| Mua bằng tiền ảo | thiếu tiền → chặn + thông báo/điều hướng shop; đủ tiền → trừ + giao hàng; giá hiển thị = giá trừ = giá CSV |
| IAP | sheet hiện đúng giá/tên gói khớp CSV pack; huỷ → không trao, không kẹt loading; (không hoàn tất giao dịch) |
| Quảng cáo thưởng | xem hết → trao thưởng đúng; đóng sớm/không có quảng cáo → không trao + báo; nút ads ẩn khi đã mua no-ads (nếu có) |
| Nâng cấp / level | chỉ số tăng đúng bảng; giá bậc sau đúng; max level khoá nút; hiệu ứng nâng cấp áp vào gameplay thật |
| Mở khoá | đúng điều kiện (bảng UnlockFeature hoặc tương đương); trước điều kiện thì thấy khoá + lý do; mở xong lưu lại |
| Timer / sản xuất / offline | đếm đúng; nền/kill vẫn tính đúng thời gian thật; trần offline đúng; đổi giờ máy (`env clock`) không được lợi nếu game chống |
| Nhiệm vụ / thành tựu | tiến độ tăng đúng hành động; hoàn thành → nhận được; reset đúng chu kỳ |
| Tutorial | không kẹt bước; bấm ngoài vùng hướng dẫn không phá; kill giữa tutorial → tiếp tục hợp lý; không hiện lại khi đã xong |
| Red dot / thông báo | đúng lúc hiện/ẩn, đúng màn |
| Cài đặt | âm thanh/rung/ngôn ngữ áp dụng ngay và giữ sau restart |
| Lưu / đồng bộ tài khoản | restart giữ tiến trình; cập nhật build giữ tiến trình; (nếu có) đăng nhập/đồng bộ không mất dữ liệu |
| Bảng xếp hạng / sự kiện | thời gian bắt đầu/kết thúc; hết hạn đóng sạch; phần thưởng mốc |
| Localization | `pseudoloc on` mọi chữ đổi; đổi ngôn ngữ → mọi chữ của tính năng đổi; không còn chữ cứng |
| Tracking / analytics | event đúng tên, đúng số lần, đúng tham số chính (oracles.md §4) |

## 6. Rubric mức độ

| Mức | Khi nào | `--type` hay gặp |
|---|---|---|
| **critical** | crash, ANR, treo cứng, mất tiến trình/save (kể cả sau cập nhật build), mất tiền thật/IAP không giao hàng, softlock không thoát được, exploit tiền tệ vô hạn | crash, logic |
| **high** | tính năng chính không dùng được hoặc sai kết quả kinh tế (trừ/cộng sai, thưởng nhân đôi), exception trong code game, nút chính không phản hồi, chặn tiến độ | logic |
| **medium** | sai hiển thị rõ ràng (tràn chữ, thiếu sprite, key localize lộ), luồng phụ sai, gián đoạn làm mất trạng thái tạm, lỗi chỉ ở điều kiện môi trường (cỡ chữ lớn, tỉ lệ màn), tracking thiếu/đúp, perf tụt rõ | visual, logic, perf |
| **low** | lệch vài px, animation chưa mượt, chính tả, góp ý UX nhỏ, vùng chạm hơi nhỏ | visual, ux |

Tag forum BugHub tự gán theo loại (Logic/Visual/Performance/Improvement) + mức (High/Medium/Low) nếu project có.

## 7. Chuẩn viết bug

**Tiêu đề** (≤100 ký tự): `<Màn/tính năng>: <triệu chứng cụ thể> [khi <điều kiện>]`.
- Tốt: `Shop: mua gói vàng trừ kim cương 2 lần khi bấm nhanh` · `Popup nhiệm vụ: chữ nút "Nhận" tràn khung khi cỡ chữ hệ thống 130%`
- Xấu: `Bug shop` · `Lỗi hiển thị` · `Game bị lỗi khi bấm nút`

**Actual:** điều đã xảy ra, có số đo/nguyên văn chữ/thông báo lỗi. **Expected:** điều đúng ra phải xảy ra + nguồn
(`csv X:n`, GDD, hành vi chuẩn ở màn khác). **Precondition:** state cần có trước bước 1 (level, tiền, cheat đã dùng,
ngôn ngữ, điều kiện môi trường). **Steps:** đánh số, mỗi bước một hành động người chơi, đủ để người khác làm lại
trên máy khác. **Rate:** `<số lần ra lỗi>/<số lần thử>`. **Evidence:** ảnh số mấy, chữ ký log, giá trị đo
(`state` trước/sau).

Một bug = một vấn đề. Hai triệu chứng khác nguyên nhân → hai bug. Cùng một nguyên nhân hiện ở nhiều màn → một bug
liệt kê các màn.

## 8. Không phải bug (đừng log)

- Nút nổi Bug Logger, overlay FPS/console debug, menu cheat — công cụ QA.
- Lỗi chỉ có trên emulator do thiếu Google Play Services / GPU giả lập (ads không load, đăng nhập Google lỗi,
  shader nhẹ khác máy thật) — ghi chú "chỉ emulator", kiểm lại trên máy thật nếu có.
- Quảng cáo không có (no fill) trên máy test — trừ khi game xử lý sai trường hợp này (kẹt, vẫn trao thưởng).
- Overlay của app khác trên máy QA che nút game/quảng cáo — lỗi môi trường: ca phụ thuộc `blocked`, nêu trong report.
- Mất vài giây tiến trình khi app bị kill thẳng (`app restart --hard`) mà `app restart` thường vẫn lưu đúng.
- Thiết kế có chủ đích đã nêu trong GDD (vd giá tăng theo cấp số nhân).
- Log warning/info của SDK bên thứ ba không gây hậu quả.
- Màn **chết** (không đường vào, `qa.liveness` chết, autotest `Feature.Dead`) có lỗi visual/asset — một dòng
  "nên dọn" trong report, không phải bug.
- Lỗi autotest 0.1.x báo Critical ở màn chết / Major ở asset mồ côi không nạp — xem §13.
- `expectation-failed` ra một lần, lần hai pass, ảnh không thấy gì sai — ghi "nghi vấn" trong report.
- Finding audit đã có luật nhiễu (`suppressed`) — trừ khi nhìn lại ảnh thấy luật đó sai (sửa/xoá luật, log bug).

## 9. Cách tiếp cận theo thể loại

| Thể loại | Vòng lõi cần đi | Điểm hay hỏng |
|---|---|---|
| **Idle / tycoon / quản lý** | thu → nâng cấp → mở trạm/khu mới → đổi map/prestige | offline earning, số rất lớn & format, nâng cấp hàng loạt (x10/max), tốc độ sản xuất sau nâng cấp, nhân viên/buff chồng nhau, timer khi nền |
| **Merge** | kéo-thả ghép → vật phẩm cấp cao → đơn hàng | kéo thả lệch ô, ghép sai cấp, kho đầy, vật phẩm mất khi kill app giữa lúc kéo |
| **Match-3 / puzzle** | vào level → nước đi → thắng/thua → thưởng/sao | hết nước, booster, combo dây chuyền, shuffle khi không còn nước, mục tiêu đếm sai, thua → hồi sinh/ads |
| **Runner / action / arcade** | bắt đầu → điều khiển (vuốt/giữ) → va chạm → kết thúc → thưởng | độ trễ điều khiển, va chạm xuyên vật, pause/resume giữa trận, revive, FPS khi đông vật thể |
| **Tower defense / chiến thuật** | chọn màn → đặt/nâng tháp → sóng quái → thắng/thua | đặt chồng vị trí, bán/hoàn tiền, tăng tốc x2, sóng cuối, máu căn cứ âm |
| **RPG / chiến đấu / auto battle** | đội hình → trận → kết quả → nâng cấp nhân vật | chỉ số áp dụng đúng, kỹ năng/hồi chiêu, trang bị tháo/lắp, auto battle, so sánh lực chiến |
| **Gacha / sưu tầm / thẻ** | quay → nhận → nâng/ghép | tỉ lệ hiển thị, pity, trùng lặp đổi tài nguyên, x10 trừ tiền đúng, hoạt ảnh quay bỏ qua được |
| **Mô phỏng / xây dựng** | đặt công trình → vận hành → thu | đặt chồng/ngoài biên, xoay/di chuyển công trình, đường đi NPC, lưu bố cục sau restart |
| **Casual / hyper-casual** | một chạm → điểm → chơi lại | spam chơi lại, ads xen giữa (interstitial) tần suất, điểm cao lưu đúng |
| **Event / live-ops / pass** | vào sự kiện → làm nhiệm vụ → nhận mốc | hết hạn khi đang mở (`env clock`), mốc nhận hai lần, pass trả phí/miễn phí tách đúng, đồng hồ đếm ngược |

Gameplay 3D/world (chạm vật thể trong cảnh): Android dùng toạ độ trên ảnh như UI; có probe thì `ui` liệt kê object
chạm được trong mục World, `tap --world <tên>` chạm theo tên. Camera kéo/zoom: `swipe`/`drag` để kéo; pinch không
giả lập được.

## 10. Chọn ca khi thời gian hạn chế (risk-based)

Không bao giờ cắt ngẫu nhiên. Xếp ca theo **tác động × khả năng sai**, chạy từ trên xuống, ca bỏ ghi `skip` có lý do:

| Hạng | Chạy trước | Vì sao |
|---|---|---|
| 1 | Ca chạm **tiền thật / tiền ảo / save** (mua, nhận thưởng lớn, restart sau giao dịch, `build update`) | sai = mất tiền người chơi, không hồi được |
| 2 | Ca ở **vùng vừa sửa** — `git log --since=14.days -- <thư mục feature>` + bug mở trên BugHub của tính năng | code mới sai nhiều nhất; bug cũ hay tái phát |
| 3 | Ca ở vùng `QaProfile.md` ghi **"hay hỏng"** | lịch sử của chính game này |
| 4 | Happy path P0 còn lại + biên P1 của luồng chính | nền tảng, rẻ |
| 5 | Gián đoạn P2 trên popup có giao dịch | spam/kill + tiền là tổ hợp nguy hiểm nhất |
| 6 | Visual sweep P4: màn **không** có trong `covered-by-autotest` trước, pseudo-loc một lượt | autotest đã phủ phần còn lại |
| 7 | Ma trận môi trường `--quick`, P5 | ít gặp hơn, lỗi thường medium |

Smoke thăm dò (không có tính năng): hạng 1–2 trên **mọi màn sống** vào được từ màn chính, mỗi màn ≤ 5 phút, rồi
`coverage` để thấy màn chưa visit.

## 11. Phân biệt nhiễu & ghi luật nhiễu (`qa.noise`)

Finding sai của audit lặp lại làm hỏng tỉ lệ tín hiệu — nhưng một luật nhiễu quá rộng che bug thật. Quy tắc:

| Finding trông như | Thường là nhiễu khi | Là bug khi |
|---|---|---|
| `blocked` / "bị che" | popup/backdrop đang mở phủ ≥ 1/3 màn (probe báo `occluded`); element dưới một lớp loading tạm | không popup nào mở mà nút không nhận chạm; lớp che vô hình (raycast target thừa) |
| `offscreen` / ngoài màn | element đang trượt vào (animation, `burst` thấy nó đi vào); panel ẩn cố ý đặt ngoài màn | đứng yên ngoài màn sau `wait --settle`; nút người chơi cần bấm |
| `text-overflow` | text đang chạy số (đếm), đang scale animation; chữ cố ý tràn làm hiệu ứng | chữ đứng yên mà vẫn tràn; lộ hơn khi pseudo-loc/font-scale |
| `missing-sprite` / Image trắng | panel trắng có chủ đích (màu phẳng, không cần sprite) đúng kit ArtStyle | ô trắng ở chỗ icon/ảnh phải có |
| `tiny-target` < 6 mm | element decor có raycast tắt; nút trong danh sách cuộn dày đặc theo thiết kế | nút chính người chơi phải bấm |
| `text-content` (key/NaN/`{0}`) | key cố ý hiển thị trong màn debug/cheat | mọi màn người chơi thấy |
| `deadButton` (probe 1.3) | nút gắn handler lúc runtime bằng code sau khi probe quét (kiểm bằng cách bấm thật: có phản hồi → nhiễu); nút trong prefab template chưa dùng | bấm thật không phản hồi |
| `noRaycastTarget` (probe 1.3) | phần tử tương tác cố ý tắt raycast để chạm xuyên (vd label trên nút cha) | nút/ô mà người chơi phải bấm trực tiếp |
| `overlapButton` (probe 1.3) | hai nút chồng nhau nhưng chỉ một active tại một thời điểm (toggle on/off) — `burst` thấy đổi | cả hai bấm được cùng lúc |
| `missingSprite` / `noSprite` (probe 1.3) | panel màu phẳng cố ý đúng kit ArtStyle; mask/hitbox trong suốt | ô trắng ở chỗ icon/ảnh phải có |
| `solidBackground` (probe 1.3) | backdrop popup cố ý (Info mặc định) | nền phủ màn mà không có popup nào mở (lớp che sót lại) |

Ghi luật khi **cùng flag + cùng element/màn sai ≥ 2 lần** và đã nhìn ảnh khẳng định không phải bug. Thêm vào
`.claude/project-profile.json` → `qa.noise[]`:

```json
{ "flag": "blocked", "match": "screen:Shop/.*/Backdrop", "reason": "backdrop popup cố ý phủ toàn màn", "addedBy": "auto-test-device", "date": "2026-01-15" }
```

- `match` là regex trên đường dẫn element (`screen:<Màn>/<path>`); hẹp nhất có thể — một element hoặc một họ element
  cùng tên, không `.*` cả màn trừ khi lý do đúng cho cả màn (vd backdrop).
- `reason` đủ để dev/agent sau đọc hiểu vì sao không phải bug. Một dòng.
- Luật sai (che bug thật) → **xoá** luật + log bug, ghi vào `QaProfile.md`.
- Finding `suppressed` vẫn hiện trong report (đếm riêng); `audit --include-suppressed` để soi lại định kỳ.
- Không ghi luật cho: nhiễu của một phiên (emulator không có GPU), finding chỉ thấy một lần, lỗi tool (probe cũ).

## 12. Ghi tri thức sau phiên — `QaProfile.md`, charter, profile

Phần máy đọc (`liveness.observed`, `verified.cheats`, `verified.statePaths`) `stop` tự flush. Phần bạn tự ghi
(Edit, trước `stop`), mẫu từng mục trong `QaProfile.md` (vị trí mục theo khung của file):

```markdown
### Cheat / đường dựng điều kiện đã dùng được
- `<cheatMethodPrefix>AddGold(1000)` — qua `call`, cổng cheat-gate; `<cheatMethodPrefix>SkipTutorial` cần restart mới áp. (2026-01-15) ← tên ví dụ, thay bằng cheat thật của game

### Màn / hệ thống hay hỏng
- Shop → popup gói: nút Mua nhận double tap khi mạng chậm (bug #41, #57). Luôn chạy P2 spam ở đây.
- Daily reward: timer dùng giờ máy, `env clock +1d` nhận lại được (gd #60 đang chờ GD).

### Màn chết / không test
- `GiftCode`, `Admin`: enum còn, không đường vào từ UI (liveness 2026-01-15) — autotest `Feature.Dead` đã liệt kê.

### Luật nhiễu đã thêm (và lý do)
- `blocked` trên `screen:Shop/.*/Backdrop` — backdrop cố ý.

### Quy ước lệch template đã sửa trong `qa.conventions`
- Lớp cơ sở màn là `UIScreenBase` (không phải `FeatureBaseController`); cheat type `DebugCheats`.
```

- Ngắn, có ngày, có số bug nếu liên quan. Không ghi kết quả ca (nằm ở report phiên).
- Charter: thêm ca phát hiện trong tour thăm dò, sửa oracle sai, đánh dấu ca "bỏ — lý do".
- `Status` của `QaProfile.md` giữ `draft` cho tới khi dev duyệt thành `reviewed` — bạn không tự đổi.
- Không ghi vào skill, không chỉ ghi vào memory cá nhân.

## 13. Dùng kết quả autotest Editor (Δ-skip) đúng cách

`autotest-import` tóm tắt report của `com.ezg.autotest` (Smoke mở mọi màn, UI Audit, Button Sweep, Economy, Visual,
Static). Đọc `autotest-summary.md` rồi:

- **Đã pass ở autotest** (màn mở được, không finding UI Audit, visual diff không đổi) → trên máy chỉ xác nhận nhanh
  bằng mắt khi đi qua, dồn thời gian cho logic/gián đoạn/môi trường — thứ autotest không làm được.
- **Autotest báo lỗi trong tính năng này** → kiểm trên máy thật: có thật → bug (ghi "autotest cũng báo" trong
  evidence, không log hai lần nếu autotest đã đẩy lên BugHub); không thấy → ghi "autotest báo nhưng không tái hiện
  trên máy" trong report (khác biệt Editor/device là thông tin cho dev).
- **Autotest < 0.2.0** không có liveness: Critical ở màn không đường vào, Major MissingReference ở asset không nạp
  → bỏ qua nếu màn/asset không thuộc `qa.liveness` sống; ghi một dòng "noise autotest 0.1.x" trong report.
- **Autotest 0.2.0** ghi `Feature.Dead` → copy vào `QaProfile.md` mục màn chết (nếu chưa có), không test chúng.
- Không có report → không suy luận gì, test đầy đủ; nhắc dev chạy autotest trong report.
