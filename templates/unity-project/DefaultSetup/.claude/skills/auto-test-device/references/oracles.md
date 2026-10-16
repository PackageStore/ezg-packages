# Oracle & invariant — làm sao biết "đúng" là gì

Đọc ở STEP 3 của SKILL.md. Mục tiêu: mọi kết luận logic có **số đo** và **nguồn**. Tên quy ước template EZG ở đây
(`PlayerDataManager`, `TimeManager`, `UnlockFeature`, `CoinPack`…) là ví dụ mặc định — tên thật của project đọc từ
`qa.conventions` / `QaProfile.md` / code.

## 1. Oracle vs invariant

| | Oracle | Invariant |
|---|---|---|
| Là gì | Giá trị đúng cho **một hành động cụ thể** ("mua gói 1 → −100 kim cương, +1.000 vàng") | Điều **luôn đúng** bất kể hành động ("tiền không âm", "tổng tiền vào − ra = số dư") |
| Nguồn | CSV (`csv`), GDD, TechSpec, công thức trong Service | Thiết kế hệ thống template + lẽ thường kinh tế game |
| Kiểm khi | Từng ca, ngay sau hành động | Sau mỗi **nhóm** ca (kinh tế, nâng cấp…), đầu và cuối phiên |
| Sai → | Bug logic của hành động đó | Bug nghiêm trọng hơn: có hành động nào đó phá luật mà ca riêng lẻ không bắt được |

Charter liệt kê **cả hai**: bảng oracle theo ca, danh sách invariant của tính năng.

## 2. Bảng invariant chuẩn cho game trên template EZG

| Invariant | Cách kiểm (có probe) | Không probe | Sai = |
|---|---|---|---|
| **Tiền tệ không âm, không NaN** | `state PlayerDataManager.PlayerResource` (hoặc path trong `qa.verified.statePaths`) sau mỗi nhóm ca | số trên HUD | high (exploit/underflow) |
| **Giá hiển thị = giá trừ = giá CSV** | `csv <PackCsv> --where id=<n> --col price` ↔ chữ trên nút ↔ Δ `state` | chữ trên nút ↔ Δ số HUD | high |
| **Thưởng nhận = thưởng CSV, đúng một lần** | `csv <RewardCsv>` ↔ Δ `state` trước/sau; spam `--times 5` Δ vẫn bằng 1 phần | Δ số HUD | high |
| **Giá IAP trên sheet = CSV pack** | `csv <IapPackCsv>` (productId, giá tham chiếu) ↔ sheet store (mở, đọc, **huỷ**) | nt. | high (sai gói) / medium (sai tên) |
| **Mở khoá đúng điều kiện** | `csv UnlockFeature` (hoặc bảng tương đương) ↔ `state <PlayerData unlock module>` ↔ nút khoá/mở trên màn | nút khoá/mở + level hiện tại | high |
| **Thời gian dùng đồng hồ game (`TimeManager`), không `DateTime.Now`** | `env clock +1d` (emulator) → timer/daily không nhảy nếu game chống; `app background --secs 60` → timer tiến đúng 60s | nt. | medium (gd nếu thiết kế cho phép) |
| **Save round-trip** | `state` X → `app restart` → `state` X không đổi; `build update --keep-data` giữ nguyên | số HUD trước/sau | critical (mất) / high (lệch) |
| **Số hiển thị nhất quán giữa màn** | cùng một giá trị ở HUD/popup/chi tiết sau một hành động — `ui --find <text>` | ảnh các màn | medium |
| **Red dot = có việc** | red dot hiện ↔ `state` module tương ứng có mục chưa nhận; làm xong → tắt | nt. | low/medium |
| **Tracking 1 hành động = 1 event** | `track --since <mark> --expect <event>`; spam → vẫn 1 | — | medium |
| **Không exception trong code game** | `logs --all` cuối nhóm ca | logcat | high |

Game thêm invariant riêng (năng lượng ≤ max, kho ≤ sức chứa, sao của map ≤ tổng trạm…) → ghi vào charter +
`QaProfile.md` để phiên sau dùng lại.

## 3. Lấy giá trị kỳ vọng — `csv`, `state`, `call`

**`csv`** — đọc đúng file CSV mà game import (theo `qa.conventions.csvConfigGlob`), không đọc GDD thay thế:

```bash
$QA csv CoinPack                              # cả bảng (tên file không đuôi, không phân biệt hoa thường)
$QA csv CoinPack --where id=2 --col price     # một ô — trích vào charter dạng "csv CoinPack:2"
$QA csv UnlockFeature --where feature=Shop    # điều kiện mở khoá
```

GDD khác CSV → không tự chọn bên nào: bug `--type gd` nêu cả hai nguồn, tính năng kiểm theo CSV (đó là thứ game chạy).

**`state`** — đọc state thật, trước và sau:

```bash
$QA state PlayerDataManager.PlayerResource                       # snapshot module (tool cắt dài — --out f.json nếu cần parse)
$QA state PlayerDataManager.PlayerResource.Gold                  # một giá trị
```

- Path đã kiểm ở phiên trước: `qa.verified.statePaths` / `QaProfile.md` — dùng luôn. Path mới chạy OK → `stop` ghi vào.
- Đọc code module PlayerData (`Scripts/Data/Player*.cs`) để biết đúng member; đừng đoán.
- Snapshot trước hành động → hành động → snapshot sau → Δ chỉ đúng những trường hành động đó phải đổi. Trường khác
  đổi theo = phát hiện (side effect).

**`call`** — chỉ gọi hàm **đọc**/tổng hợp (Service static: `GetTotalX`, `CanAffordY`, `IsUnlocked`) để kiểm invariant;
hàm ghi (cộng tiền, mở khoá) chỉ dùng dựng điều kiện (luật 1 SKILL.md) và ghi vào `--precondition`.

## 4. Tracking / analytics

Hành động quan trọng (mua, nhận thưởng, mở màn, xem ads, hoàn thành level) phải phát event đúng tên, đúng số lần:

```bash
$QA mark "P0-02 - mua gói 1"
$QA tap --text "Mua" --expect "Đã mua" --as "Bấm Mua gói 1"
$QA track --since "P0-02 - mua gói 1" --expect purchase_pack       # tên event lấy từ TrackingEventName (hoặc type trong qa.conventions.trackingEventNameType)
```

- `track` lọc log theo `qa.conventions.trackingLogPattern` (mặc định `Tracking|Firebase|AppsFlyer`); game log tracking
  kiểu khác → sửa pattern trong profile, ghi `QaProfile.md`.
- Thiếu event → medium. Event đúp khi spam → medium (ảnh hưởng số liệu). Tham số chính sai (pack id, giá) → medium.
- Build release tắt log tracking → `track` không thấy gì: ghi "không kiểm được tracking (build release)", không phải bug.

## 5. Save-migration — cập nhật build giữ data

Khi repo/máy có bản build **cũ hơn** bản đang test (artifact cũ, bản store đang cài):

```bash
# đang chạy bản cũ với save đã có tiến trình (hoặc dựng bằng cheat)
$QA build update <apk|aab|path> --keep-data      # cài đè, ghi version trước/sau, snapshot verified.statePaths trước/sau → migration.md
```

Đọc `migration.md`: mọi trường trong snapshot phải **bằng** hoặc **đổi có giải thích** (changelog/CSV mới đổi
công thức → giá trị suy ra đổi). Mất tiến trình, về mặc định, mất item/currency → **critical**. Màn đầu sau cập
nhật (popup "có gì mới", tutorial lặp lại, loading dài) → kiểm bằng mắt. Không có bản cũ → ghi `skip` ca migration,
nhắc dev giữ artifact bản trước trong repo/CI.

## 6. Khi không có probe

Vẫn có oracle: `csv` đọc từ repo, số trên ảnh gốc `.png` (Read ảnh gốc, không ảnh xem thu nhỏ), `logs` cho
exception/tracking. Mất: `state` chính xác, `call`, `tap --text`. Ghi "hộp đen" trong report để dev build lại có probe.
