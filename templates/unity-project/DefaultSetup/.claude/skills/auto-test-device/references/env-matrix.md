# Ma trận môi trường — cùng tính năng, điều kiện khác

Đọc ở STEP 4 của SKILL.md. Mỗi bước là một điều kiện máy thật của người chơi mà máy QA mặc định không có. Làm sau
P0–P3, trên 2–3 màn chính của tính năng; `env matrix` tự chạy các bước khai trong `qa.environments`, phần còn lại
làm tay. Mọi bước **tự hoàn nguyên** (`env matrix`) hoặc bạn hoàn nguyên ngay sau khi xong; `stop` dọn phần sót.

| Bước | Lệnh | Người chơi nào gặp | Lộ lỗi gì | Target |
|---|---|---|---|---|
| **Cỡ chữ hệ thống lớn** | `env font-scale 1.3` → đi màn nhiều chữ → `audit` → `env font-scale reset` | người lớn tuổi, cài đặt trợ năng (rất phổ biến) | TMP không `autoSize` tràn nút; LayoutGroup giãn đè nhau; số tiền dài vỡ HUD; nút hai dòng | Android |
| **Dark mode** | `env dark on` → màn có WebView/popup native/consent → `env dark off` | người chơi ban đêm | WebView/consent/ads native chữ đen trên nền đen; splash/theme hệ thống | Android |
| **Tỉ lệ màn dài / ngắn / tablet** | `aspect tall` (20:9+) · `aspect short` (16:9) · `aspect tablet` · `aspect <WxH>` → đi màn → `aspect reset` | đa dạng máy | neo sai (safe area, tai thỏ), khoảng trống lạ, nút rơi khỏi màn, background không phủ, CanvasScaler match sai | Android (`wm size`) |
| **Tắt animation hệ thống** | `env animator 0` → bấm nhanh qua luồng → `env animator 1` | máy yếu, trợ năng; cũng lộ logic chờ animation | callback `OnComplete` của tween không chạy → nút kẹt disabled, thưởng không hiện, popup không đóng; đếm số nhảy thẳng sai | Android |
| **OS giết activity khi nền** | `env kill-activities on` → `app background --secs 5` → quay lại → `env kill-activities off` | máy ít RAM, đa nhiệm | mất popup đang mở, ads đang xem không trả thưởng, state tạm mất, màn đen khi quay lại | Android |
| **Áp lực bộ nhớ** | `env trim-memory [level]` → chơi 1 phút → `logs`, `perf` | máy yếu, nhiều app nền | texture/atlas mất (ô trắng/đen), crash native, LMK giết app giữa giao dịch | Android |
| **Đổi giờ máy** | `env clock +1d` → daily/timer/event → `env clock reset` | người gian lận thời gian | daily reward nhận lại, timer về 0, event hết hạn khi đang mở, offline earning khổng lồ — game chống thì phải chặn hoặc dùng giờ server | emulator / root |
| **Ngôn ngữ hệ thống** | `env locale de-DE` (emulator) → mở lại app → chữ | người chơi nước khác | auto-detect ngôn ngữ sai, chữ chưa dịch, format số/ngày | emulator |
| **Pseudo-loc** | `pseudoloc on` → mọi màn/popup → `audit` → `pseudoloc off` | mọi ngôn ngữ chữ dài (Đức, Nga, Việt có dấu) | tràn/cắt/xuống dòng xấu; chữ **không đổi** = chữ cứng không qua localize; thiếu glyph dấu | probe ≥ 1.3 |
| **Mất mạng** | `net off` lúc gọi mạng/ads/IAP → `net on` | tàu điện, thang máy | treo loading, trao thưởng khi request chưa thành, không có nút thử lại | Android |
| **Xoay màn** | `rotate landscape` → `rotate portrait` | game cho xoay | layout vỡ, popup lệch | Android |

Bước không hỗ trợ trên target → tool in `unsupported`, ghi `skip` ca tương ứng; iOS Simulator đổi device để thay
`aspect`, iPhone thật không đổi được.

## Pseudo-loc — vì sao và cách đọc kết quả

Pseudo-loc biến mọi chữ qua hệ localize thành bản giãn ~30% + dấu + ngoặc `[Ṁüä Ğöĺď…]`. Lợi:

- **Không cần đổi ngôn ngữ của game** (không đụng save/cài đặt người chơi) mà vẫn thấy chữ dài nhất.
- **Chữ cứng lộ ngay:** chữ nào không đổi là không qua localize (hardcode trong prefab/code) → bug medium
  (`--type visual`, "chữ cứng không localize"). Trừ: số, tên riêng, ký hiệu.
- Dấu lộ glyph thiếu trong font (ô vuông) → thiếu fallback font.
- `mode expand` chỉ giãn (soi layout), `accent` chỉ dấu (soi font), `both` mặc định.

Cách làm: bật → đi qua **mọi** màn/popup/trạng thái của tính năng (kể cả toast, tooltip, popup lỗi) → `audit` ở mỗi
màn (auto-audit đã chạy khi đổi màn) → tắt. Mỗi finding `text-overflow` trong pseudo-loc: xem có xảy ra ở ngôn ngữ
thật dài nhất game hỗ trợ không — pseudo-loc giãn 30% là xấp xỉ tiếng Đức; game chỉ có Anh + Việt thì tràn nhẹ
dưới 10% có thể hạ low. Chữ tràn ở ngôn ngữ mặc định (không pseudo-loc) luôn là bug.

Probe < 1.3 không có pseudo-loc: dùng đổi ngôn ngữ trong game (ngôn ngữ chữ dài nhất), đổi lại khi xong, ghi report
"build lại với probe ≥ 1.3".

## Ghi bug môi trường

- Tiêu đề kèm điều kiện: `Popup nhiệm vụ: nút "Nhận" tràn khung khi cỡ chữ hệ thống 130%`.
- `--precondition` ghi lệnh/điều kiện (`font-scale 1.3`, `aspect 720x1280`, `kill-activities on`).
- Tái hiện: hoàn nguyên → bật lại → làm lại một lần (lỗi do điều kiện hay do màn?). Lỗi có cả khi hoàn nguyên →
  không phải bug môi trường, bỏ điều kiện khỏi tiêu đề.
- Mức: thường **medium** (sai hiển thị rõ ở điều kiện thật); kẹt luồng/mất thưởng ở `kill-activities`/`animator 0`
  → **high**; được lợi khi đổi giờ máy mà game có chống → **high**, game không chống → `gd`.
- `env-matrix.md` của phiên liệt kê bước × màn × kết quả — dán tóm tắt vào report.
