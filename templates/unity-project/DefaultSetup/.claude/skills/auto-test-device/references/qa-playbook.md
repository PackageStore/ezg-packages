# QA playbook — /auto-test-device

Tài liệu nghề cho agent QA: lập charter, thiết kế ca test, checklist visual/logic/ổn định, cách tiếp cận theo thể
loại gameplay, rubric mức độ, chuẩn viết bug. Áp dụng cho mọi game; chỗ nào nhắc template EZG là ví dụ.

---

## 1. Tư duy QA

- **Người chơi thật làm gì, mình làm đó — rồi làm thêm điều người chơi vụng/hấp tấp/tò mò làm.** Bấm hai lần,
  bấm trong lúc animation chạy, thoát giữa chừng, quay lại sau một lúc, mất mạng, xoay máy, bấm Back.
- **Mỗi hành động có một kỳ vọng viết ra trước.** Không có kỳ vọng thì không phát hiện được sai.
- **Đo, đừng ước.** Ghi số trước/sau. "Có vẻ đúng" không phải kết quả.
- **Một lỗi → tìm anh em của nó.** Thấy tràn chữ ở một nút → kiểm mọi nút cùng kiểu. Thấy thưởng nhân đôi khi spam
  → thử mọi nút nhận thưởng khác.
- **Tái hiện tối thiểu.** Trước khi log, rút ngắn bước tới ít nhất có thể vẫn ra lỗi.
- **Phân biệt bug với thiết kế.** Nghi là thiết kế (không có tài liệu nói) → log `--type gd` với câu hỏi rõ ràng,
  mức độ low/medium, đừng khẳng định là sai.

## 2. Mẫu test charter (`charter.md` trong thư mục phiên)

```markdown
# Charter — <tính năng>
Mục tiêu: <tính năng làm gì cho người chơi, 1-2 câu>
Đường vào: <từ màn chính bấm gì để tới; điều kiện mở khoá>
Trạng thái/điều kiện liên quan: <tiền, level, item, flag tutorial, thời gian…>
Nguồn: <GDD/TechSpec/file code/CSV đã đọc>

## Oracle (giá trị kỳ vọng)
| Hành động | Kỳ vọng | Nguồn |
|---|---|---|
| Mua gói 1 | -100 kim cương, +1.000 vàng | CoinPack.csv:2 |
| … | … | … (suy luận → ghi "suy luận") |

## Ca test
| # | Ưu tiên | Ca | Kết quả |
|---|---|---|---|
| 1 | P0 | … | |
```

## 3. Thiết kế ca test

### 3.1 Happy path (P0)
Đi trọn vòng: vào tính năng từ đường người chơi thật dùng → thực hiện hành động chính → nhận kết quả (thưởng, mở
khoá, chuyển màn) → thoát về. Kiểm mọi con số trên đường đi với oracle. Lặp lại một lần nữa (lần 2 hay lộ lỗi
state không reset).

### 3.2 Biên & phân lớp tương đương (P1)
- Tài nguyên: **0**, **ít hơn giá 1 đơn vị**, **đúng bằng giá**, **dư**, rất lớn (format số 1K/1M/1B, tràn chữ).
- Level/tiến độ: level 1, level ngay trước mốc mở khoá, đúng mốc, max level (nút nâng cấp phải đổi trạng thái), vượt max.
- Danh sách: rỗng, 1 phần tử, đầy, nhiều hơn một trang (cuộn tới cuối, phần tử cuối không bị che).
- Thời gian: vừa bắt đầu, sắp hết (còn vài giây), đúng lúc hết, đã hết khi app ở nền.
- Lượt/giới hạn ngày: dùng hết lượt, lượt cuối, sang ngày mới (nếu tua được bằng cheat).

### 3.3 Chuyển trạng thái (P1)
Vẽ nhanh trong đầu các trạng thái (khoá → mở → đang chạy → xong → nhận → cooldown). Thử **mọi chuyển hợp lệ** và
**chuyển không hợp lệ** (bấm nhận khi chưa xong, bấm mua khi đang xử lý, mở popup khi popup khác đang mở).

### 3.4 Gián đoạn (P2) — ma trận cho mỗi màn/popup chính
| Gián đoạn | Lệnh | Kỳ vọng |
|---|---|---|
| Back hệ thống (Android) | `key back` | đóng popup trên cùng / hỏi thoát ở màn gốc; không đóng nhầm 2 lớp, không kẹt |
| Nền ngắn | `app background --secs 5` | quay lại đúng màn, state giữ, nhạc/timer tiếp tục đúng |
| Nền dài | `app background --secs 60` | timer/offline tính đúng thời gian thực |
| Kill + mở lại | `app restart` | state đã xác nhận được lưu; luồng dở dang xử lý hợp lý (không mất tiền/nhân đôi thưởng) |
| Mất mạng | `net off` (… `net on`) | thông báo lỗi tử tế, nút thử lại, không treo loading, không trao thưởng khi request chưa thành |
| Spam / double tap | `tap … --times 5 --interval 0.05` | chỉ xử lý 1 lần |
| Bấm trong animation | tap ngay khi popup đang mở / số đang chạy | không kẹt, không nhận thưởng hai lần |
| Xoay màn (game cho xoay) | `rotate landscape` | layout không vỡ |
| Thông báo hệ thống/cuộc gọi | (giả lập bằng nền ngắn) | như nền ngắn |

### 3.5 Lưu trữ & nhất quán (P3)
- Sau mỗi thay đổi state quan trọng → `app restart` → so state.
- Cùng một con số hiển thị ở nhiều nơi (HUD, popup, màn chi tiết, tooltip) phải khớp và cập nhật cùng lúc.
- Red dot/badge: xuất hiện khi có việc, biến mất khi xong, không còn sau restart nếu đã xử lý.

### 3.6 Ổn định & hiệu năng (P5)
- Lặp vòng chính 5–10 lần, `perf` đầu/cuối: RAM không tăng mãi, FPS không tụt dần.
- Mở/đóng cùng một popup 10 lần (rò rỉ, chồng popup, chậm dần).
- Monkey có kiểm soát: 20–30 chạm nhanh vào các vùng khác nhau của màn tính năng (`tap … --no-shot`), rồi `shot` +
  `logs` — game không crash/kẹt.

### 3.7 Thăm dò tự do (2 vòng cuối)
Chọn một "tour": **tour tiền** (theo dòng tiền vào/ra), **tour hậu đậu** (làm sai mọi bước), **tour quay lại**
(rời đi, quay lại theo đường khác), **tour cực đoan** (giá trị lớn nhất/nhỏ nhất), **tour người mới** (không biết
gì, chỉ theo chỉ dẫn trên màn). Ghi `mark "Thăm dò — tour …"`.

## 4. Checklist visual (mỗi màn/popup/trạng thái)

**Bố cục:** phần tử lệch tâm/lệch hàng so với anh em; khoảng cách không đều; phần tử chồng lên nhau; bị cắt bởi mép
màn, tai thỏ, thanh điều hướng, viền bo; nút đóng/back khó thấy hoặc khó bấm; nội dung cuối danh sách bị che.

**Chữ:** tràn/cắt/xuống dòng xấu; chữ ra ngoài khung/nút; font sai hoặc thiếu ký tự (ô vuông, dấu tiếng Việt rơi);
còn key localize (`shop_title`), placeholder (`New Text`, `Button`), `{0}`, `NaN`, `null`, số âm vô lý; sai chính
tả, trộn ngôn ngữ; số định dạng không nhất quán (1000 vs 1K vs 1,000); chữ quá nhỏ để đọc; tương phản kém.

**Hình ảnh:** ô trắng/hồng (thiếu sprite/shader), ảnh kéo giãn sai tỉ lệ, mờ/vỡ pixel, icon sai item, viền
răng cưa, nền trong suốt bị đen, z-order sai (hiệu ứng nằm dưới nền, popup dưới HUD).

**Trạng thái:** nút disabled trông như enabled (và ngược lại); không có phản hồi khi bấm (không nhấn lún/âm
thanh/hiệu ứng); loading không có chỉ báo; trạng thái rỗng không có thông báo; số không cập nhật sau hành động.

**Style:** đối chiếu `.claude/docs/ArtStyle.md` (palette, kit nút, font, bố cục màn mẫu) — màn lạc tông, nút khác
kit, font lạ.

**Chuyển động:** `burst` khi mở/đóng popup, nhận thưởng, chuyển màn: nhấp nháy một khung, nhảy layout, hiệu ứng
kẹt lại sau khi đóng, đếm số chạy sai giá trị cuối.

**Tỉ lệ màn:** máy dài (20:9), máy ngắn (16:9), tablet (nếu có emulator) — phần tử neo đúng, không khoảng trống lạ.

Audit tự động (`audit`, có probe) phủ một phần danh sách trên — vẫn phải nhìn ảnh.

## 5. Checklist logic theo hệ thống thường gặp

| Hệ thống | Kiểm |
|---|---|
| Tiền tệ / kinh tế | trừ/cộng đúng số, đúng một lần; không âm; số lớn không tràn; nguồn/đích khớp GDD |
| Phần thưởng | nhận đúng loại + số lượng; popup thưởng khớp số thực nhận; không nhận lại sau restart/spam |
| Mua bằng tiền ảo | thiếu tiền → chặn + thông báo/điều hướng shop; đủ tiền → trừ + giao hàng; giá hiển thị = giá trừ |
| IAP | sheet hiện đúng giá/tên gói; huỷ → không trao, không kẹt loading; (không hoàn tất giao dịch) |
| Quảng cáo thưởng | xem hết → trao thưởng đúng; đóng sớm/không có quảng cáo → không trao + báo; nút ads ẩn khi đã mua no-ads (nếu có) |
| Nâng cấp / level | chỉ số tăng đúng bảng; giá bậc sau đúng; max level khoá nút; hiệu ứng nâng cấp áp vào gameplay thật |
| Mở khoá | đúng điều kiện; trước điều kiện thì thấy khoá + lý do; mở xong lưu lại |
| Timer / sản xuất / offline | đếm đúng; nền/kill vẫn tính đúng thời gian thật; trần offline đúng; không đổi giờ máy được lợi (nếu game chống) |
| Nhiệm vụ / thành tựu | tiến độ tăng đúng hành động; hoàn thành → nhận được; reset đúng chu kỳ |
| Tutorial | không kẹt bước; bấm ngoài vùng hướng dẫn không phá; kill giữa tutorial → tiếp tục hợp lý; không hiện lại khi đã xong |
| Red dot / thông báo | đúng lúc hiện/ẩn, đúng màn |
| Cài đặt | âm thanh/rung/ngôn ngữ áp dụng ngay và giữ sau restart |
| Lưu / đồng bộ tài khoản | restart giữ tiến trình; (nếu có) đăng nhập/đồng bộ không mất dữ liệu |
| Bảng xếp hạng / sự kiện | thời gian bắt đầu/kết thúc; hết hạn đóng sạch; phần thưởng mốc |
| Localization | đổi ngôn ngữ → mọi chữ của tính năng đổi; không còn chữ cứng tiếng Anh |

## 6. Rubric mức độ

| Mức | Khi nào | `--type` hay gặp |
|---|---|---|
| **critical** | crash, ANR, treo cứng, mất tiến trình/save, mất tiền thật/IAP không giao hàng, softlock không thoát được, exploit tiền tệ vô hạn | crash, logic |
| **high** | tính năng chính không dùng được hoặc sai kết quả kinh tế (trừ/cộng sai, thưởng nhân đôi), exception trong code game, nút chính không phản hồi, chặn tiến độ | logic |
| **medium** | sai hiển thị rõ ràng (tràn chữ, thiếu sprite, key localize lộ), luồng phụ sai, gián đoạn làm mất trạng thái tạm, perf tụt rõ | visual, logic, perf |
| **low** | lệch vài px, animation chưa mượt, chính tả, góp ý UX nhỏ, vùng chạm hơi nhỏ | visual, ux |

Tag forum BugHub tự gán theo loại (Logic/Visual/Performance/Improvement) + mức (High/Medium/Low) nếu project có.

## 7. Chuẩn viết bug

**Tiêu đề** (≤100 ký tự): `<Màn/tính năng>: <triệu chứng cụ thể> [khi <điều kiện>]`.
- Tốt: `Shop: mua gói vàng trừ kim cương 2 lần khi bấm nhanh` · `Popup nhiệm vụ: chữ nút "Nhận" tràn khung ở tiếng Đức`
- Xấu: `Bug shop` · `Lỗi hiển thị` · `Game bị lỗi khi bấm nút`

**Actual:** điều đã xảy ra, có số đo/nguyên văn chữ/thông báo lỗi. **Expected:** điều đúng ra phải xảy ra + nguồn
(CSV/GDD/hành vi chuẩn ở màn khác). **Precondition:** state cần có trước bước 1 (level, tiền, cheat đã dùng, ngôn
ngữ). **Steps:** đánh số, mỗi bước một hành động người chơi, đủ để người khác làm lại trên máy khác. **Rate:**
`<số lần ra lỗi>/<số lần thử>`. **Evidence:** ảnh số mấy, chữ ký log, giá trị đo.

Một bug = một vấn đề. Hai triệu chứng khác nguyên nhân → hai bug. Cùng một nguyên nhân hiện ở nhiều màn → một bug
liệt kê các màn.

## 8. Không phải bug (đừng log)

- Nút nổi Bug Logger, overlay FPS/console debug, menu cheat — công cụ QA.
- Lỗi chỉ có trên emulator do thiếu Google Play Services / GPU giả lập (ads không load, đăng nhập Google lỗi,
  shader nhẹ khác máy thật) — ghi chú "chỉ emulator", kiểm lại trên máy thật nếu có.
- Quảng cáo không có (no fill) trên máy test — trừ khi game xử lý sai trường hợp này (kẹt, vẫn trao thưởng).
- Thiết kế có chủ đích đã nêu trong GDD (vd giá tăng theo cấp số nhân).
- Log warning/info của SDK bên thứ ba không gây hậu quả.

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
| **Event / live-ops / pass** | vào sự kiện → làm nhiệm vụ → nhận mốc | hết hạn khi đang mở, mốc nhận hai lần, pass trả phí/miễn phí tách đúng, đồng hồ đếm ngược |

Gameplay 3D/world (chạm vật thể trong cảnh): Android dùng toạ độ trên ảnh như UI; có probe thì `ui` liệt kê object
chạm được trong mục World, `tap --world <tên>` chạm theo tên. Camera kéo/zoom: `swipe`/`drag` để kéo; pinch không
giả lập được.
