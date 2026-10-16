<!--
QaProfile.template.md — KHUNG dùng chung, đi theo bộ AI của template (Feature Hub cập nhật được file này).
KHÔNG điền giá trị của game vào đây. File thật của project là `.claude/docs/QaProfile.md`:
  - bootstrap.sh / bootstrap.ps1 copy khung này thành QaProfile.md khi project chưa có;
  - `qadevice.py profile init` (skill /auto-test-device) cũng tạo nó, tự dò quy ước code rồi điền các vùng
    <!-- auto:…:start/end --> — chữ NGOÀI vùng auto là của dev/agent, tool không bao giờ ghi đè.
QaProfile.md là file CỦA PROJECT: Feature Hub không ghi đè, commit cùng repo. Bản máy đọc của cùng nội dung là
block "qa" trong `.claude/project-profile.json` (`profile show`); hai bên lệch → `profile init` làm mới vùng auto.
Giữ nguyên tên + SỐ của các mục (§0 … §11): skill dùng chung tham chiếu theo số.
-->
# QA Profile — <Tên game>

**Status:** template
<!-- template = chưa điền (skill QA phải chạy § Bootstrap / `profile init` trước) · draft = tool tự dò, dev chưa
     duyệt (dùng được, nhưng report phải báo dev) · reviewed = dev đã đọc và sửa (nguồn chuẩn) -->
**Cập nhật:** <YYYY-MM-DD> · **Người duyệt:** <dev>

## 0. File này là gì

Nguồn chuẩn **duy nhất** về cách test game này cho agent QA (`/auto-test-device`) và dev: quy ước code mà tool bám
vào (enum màn, lớp cơ sở UI, cheat, PlayerData, CSV, tracking), màn nào còn sống / đã bỏ, cheat và state path đã
kiểm, luật nhiễu đã xác nhận, môi trường cần thử, hướng test riêng của thể loại, lịch sử phiên.

**Thứ tự ưu tiên khi mâu thuẫn:** lời dev trong task → file này (`reviewed`) → block `qa` trong
`project-profile.json` → tự dò theo quy ước template EZG → mặc định của skill. Skill **không** chứa giá trị riêng
của game; thiếu gì ở đây thì thêm vào đây, không sửa skill.

## 1. Bootstrap (khi Status còn `template`)

1. `python3 .claude/skills/auto-test-device/scripts/qadevice.py profile init` — tự dò quy ước, điền §2/§3/§5/§7/§8/§10,
   đổi Status → `draft`, tạo `.claude/docs/qa/charters/README.md`.
2. `… profile sync` — đẩy quy ước xuống `Assets/Resources/EzgQaProbeConfig.json` (probe trong build) và block
   `liveness` của `ProjectSettings/EZGAutoTestSettings.json` (autotest Editor bỏ qua màn chết).
3. `… doctor --strict` — mọi quy ước phải resolve ra file thật trong code.
4. Dev đọc §2 (dò sai → sửa trong `project-profile.json["qa"].conventions` rồi `profile init` lại), điền §4, §6, §9,
   đổi Status → `reviewed`.

## 2. Quy ước code (tool tự dò — sửa ở `project-profile.json["qa"].conventions`, không sửa bảng này)

<!-- auto:conventions:start -->
| Key | Giá trị | Nguồn | Bằng chứng |
|---|---|---|---|
| (chưa chạy `profile init`) | | | |
<!-- auto:conventions:end -->

Ghi chú của dev về quy ước (chỗ dò sai, ngoại lệ, màn không theo chuẩn):
- <vd: màn Battle không kế thừa FeatureBaseController, mở bằng scene riêng>

## 3. Màn / feature: sống – chết – đã quan sát

Tool phân loại từ enum Features + tham chiếu tĩnh + màn đã thấy mở lúc chạy. **Chết** = khớp mẫu
(`liveness.deadFeaturePatterns`) hoặc `forceDead` → autotest và skill bỏ qua. **Không thấy code gọi** chưa chắc chết
(có thể mở qua prefab/enum serialize) — xác nhận bằng quan sát hoặc `forceLive`/`forceDead`.

<!-- auto:features:start -->
- (chưa chạy `profile init`)
<!-- auto:features:end -->

Lý do dev đặt `forceLive` / `forceDead` (để người sau không hỏi lại):
- <Feature> → <live|dead> vì <lý do, ngày>

## 4. Đường vào & điều kiện mở khoá của các màn chính (dev/agent điền)

| Màn / feature | Đường vào từ Home | Điều kiện mở khoá | Ghi chú |
|---|---|---|---|
| <Shop> | <nút góc dưới phải> | <không> | |
| <Feature X> | <…> | <level ≥ N / xong tutorial> | <cheat mở nhanh: …> |

## 5. Cheat & state path đã kiểm (tool ghi lúc `stop`)

<!-- auto:verified:start -->
| Loại | Tên / đường dẫn | Lần cuối OK |
|---|---|---|
| — | chưa có | |
<!-- auto:verified:end -->

Cheat hay dùng khi test (dev gợi ý — tên hàm static, tham số):
- <`Cheat_AddGold(long)`> — <ghi chú>
- <`Cheat_SetLevel(int)`> — <ghi chú>

## 6. Oracle & nguồn số liệu

- GDD: <link / đường dẫn bản đọc-hiểu>; TechSpec: <đường dẫn>
- CSV quyết định số liệu: <`CoinPack.csv` → giá/gói gem; `UnlockFeature.csv` → mốc mở màn; …> (`qadevice.py csv <tên>`)
- Công thức cần kiểm tay (không có trong CSV): <vd: thu nhập offline = rate × giây × 0.2, trần 8h>
- Tracking event bắt buộc theo luồng (`qadevice.py track --expect <event>`): <mua gói → `iap_success`; …>

## 7. Luật nhiễu đã xác nhận (finding audit bỏ qua)

Mỗi luật: `flag` (tên finding hoặc `*`), `match` (regex trên path/text của phần tử, hoặc `screen:<tên màn>`), lý do,
ai thêm, ngày. Thêm vào `project-profile.json["qa"].noise` rồi `profile init` để làm mới bảng. Chỉ thêm khi đã
**nhìn ảnh xác nhận không phải bug** (vd nút nền toàn màn bị popup đè là thiết kế).

<!-- auto:noise:start -->
| Flag | Khớp | Lý do | Ai / ngày |
|---|---|---|---|
| — | chưa có luật nào | | |
<!-- auto:noise:end -->

## 8. Môi trường & thiết bị

<!-- auto:environments:start -->
- (chưa chạy `profile init`)
<!-- auto:environments:end -->

- Máy test đang có (serial/AVD, ai cầm, đã vào danh sách cheat-gate chưa): <…>
- Lấy build ở đâu (CI/AAB/APK, cài qua Play hay adb): <…>
- Lưu ý khi chạy trên emulator (ads test không đóng, GPU…): <…>

## 9. Hướng test riêng của game này

- Thể loại / vòng lặp cốt lõi → ca P0 bắt buộc mỗi phiên: <…>
- Vùng rủi ro cao (hay hỏng khi đổi): <…>
- Không phải bug (thiết kế, đã chốt với GD): <…>
- Lỗi đã biết đang chờ sửa (không log lại — dedup theo BugHub): <#id — mô tả ngắn>

## 10. Đường dẫn

<!-- auto:paths:start -->
- (chưa chạy `profile init`)
<!-- auto:paths:end -->

## 11. Lịch sử phiên (agent ghi 1 dòng cuối mỗi phiên)

| Ngày | Tính năng | Máy | Bug | Coverage | Ghi chú |
|---|---|---|---|---|---|
| <YYYY-MM-DD> | <feature> | <máy> | <#…> | <n/m ca · k màn> | <điều học được / cần dev quyết> |
