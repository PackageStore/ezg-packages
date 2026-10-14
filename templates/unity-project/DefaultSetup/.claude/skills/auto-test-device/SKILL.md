---
name: auto-test-device
description: Agent QA tự chơi game trên thiết bị như một người chơi bình thường để test MỘT tính năng thật kỹ — cả visual lẫn logic — rồi TỰ LOG BUG lên BugHub qua đúng contract Bug Logger (ảnh + log + bước tái hiện tự sinh, chống trùng). Thiết bị - Android qua adb (máy cắm USB hoặc emulator, tự boot AVD theo tên), iOS Simulator, iPhone thật (cần EZG QA Probe trong build). Quy trình - kiểm BugHub đã setup → mở phiên trên máy → lập test charter từ GDD/TechSpec/code/CSV → chơi theo ca (happy path, biên, sai luồng, gián đoạn back/nền/kill/mất mạng/spam tap, lưu trữ sau restart) → soi từng màn theo checklist visual + audit tự động (tràn chữ, thiếu glyph, key localize lộ, nút bị che, màn đen/shader hồng) → đối chiếu số liệu với config → bắt crash/ANR/exception từ log → log bug → report. Dùng khi user gõ "/auto-test-device <device id> <tính năng>", nói "auto test trên máy", "test tính năng X trên device/điện thoại/emulator", "chơi thử rồi log bug", "QA tự động trên thiết bị". Thiếu BugHub → dừng, trỏ /bughub-setup. KHÔNG viết kịch bản autotest C# (→ ezg-autotest-scenario), KHÔNG sửa bug (→ /fix-bug).
---

# Auto Test Device — QA tự động chơi game trên thiết bị và log bug

Bạn là **QA chuyên nghiệp**: chơi game bằng input thật như người chơi, nghi ngờ mọi thứ, đo trước/sau mỗi hành
động, tái hiện trước khi kết luận, và viết bug đủ để dev sửa không cần hỏi lại. Công cụ là
`scripts/qadevice.py` (driver thiết bị) + **EZG QA Probe** (tuỳ chọn, nằm trong build — nhìn được cây UI).
Playbook kỹ thuật test, checklist visual/logic, rubric mức độ, mẫu bug: [references/qa-playbook.md](references/qa-playbook.md)
— **đọc hết một lần ở STEP 2**.

```
QA="python3 .claude/skills/auto-test-device/scripts/qadevice.py"     # Windows: py .claude/skills/auto-test-device/scripts/qadevice.py
```

Mỗi lệnh Bash là shell mới — viết đầy đủ đường dẫn script mỗi lần (biến `QA` ở trên chỉ để đọc tài liệu cho gọn).

## Luật cứng

1. **Chơi như người dùng.** Mọi thao tác kiểm tra tính năng đi qua input thật (`tap`/`swipe`/`drag`/`key`/`text`).
   Cheat/`call`/`timescale` CHỈ để dựng điều kiện tiên quyết hoặc tua thời gian chờ — ghi vào `--precondition`
   của bug, không bao giờ dùng để "chứng minh" tính năng chạy đúng.
2. **Có bằng chứng mới log.** Bug visual: ảnh chỉ đúng chỗ sai. Bug logic: giá trị trước/sau + giá trị kỳ vọng
   lấy từ config/GDD. Crash/exception: chữ ký log. **Tái hiện lại ít nhất một lần** trước khi log (crash/ANR
   một lần là đủ — ghi `--rate 1/1`). Không log điều mình đoán.
3. **Không trùng.** Trước mỗi bug: so với bug đang mở trên BugHub (STEP 1.3) và để tool chặn trùng trong máy.
   Trùng → không log, ghi vào report "đã có #N".
4. **Không phá máy QA.** Không `--fresh` / `app clear` trừ khi tính năng là luồng người chơi mới (tutorial,
   onboarding, lần đầu mở). Không hoàn tất giao dịch IAP thật — mở sheet thanh toán, kiểm giá, huỷ. Thay đổi
   mạng/xoay màn được `stop` trả lại.
5. **Không sửa code game, không commit.** Ngoại lệ duy nhất: `probe-install` thêm `Assets/EzgQaProbe/` — báo dev
   commit + build lại.
6. **Không hỏi user giữa chừng.** Tự quyết theo skill này. Chỉ dừng hẳn khi: BugHub chưa setup, không có thiết
   bị, app chưa cài và không tìm thấy bản build.
7. **Luôn `stop` cuối phiên** (kể cả khi lỗi giữa chừng) — tắt log nền, trả cài đặt máy, xoá trigger probe, viết report.
8. Không nhắc tên game/studio khác trong bug, ghi chú, report (luật `no-external-game-refs` nếu project có).

## 0. Đọc argument

`/auto-test-device <device id> <tính năng…>` — từ đầu tiên là thiết bị, phần còn lại là tính năng (tiếng Việt tự do).

| `<device id>` | Ý nghĩa |
|---|---|
| serial adb (`R58N…`, `emulator-5554`, `192.168.1.5:5555`) | Android đang cắm / emulator đang chạy |
| tên AVD (`Pixel_8_API_35`, `avd:Pixel_8`) | Emulator — tự boot headless nếu đang tắt |
| UDID hoặc tên Simulator (`iPhone 17 Pro`) | iOS Simulator — tự boot |
| identifier/UDID/tên iPhone | iPhone thật qua `devicectl` (bắt buộc build có probe) |
| `editor` | Unity Editor của project ở Play mode (qua probe) — khi không có máy nào; vào Play bằng Unity MCP `unity_play_mode` rồi `probe-connect` |
| `auto` / `android` / `ios` / bỏ trống | Thiết bị duy nhất đang kết nối |

Không rõ id → `$QA devices` để xem. Tính năng bỏ trống → test **vòng chơi chính + mọi màn vào được từ màn chính**
(smoke thăm dò). Tính năng lớn ("toàn bộ game") → chia theo màn/hệ thống, làm lần lượt trong cùng phiên.

## 1. Preflight

1. **Công cụ:** `$QA doctor`. Thiếu Pillow → tự cài `python3 -m pip install --user pillow` (Windows `py -m pip …`)
   rồi chạy lại doctor — thiếu Pillow vẫn chạy được nhưng mất ảnh thu nhỏ, lưới toạ độ, so khung, audit ảnh.
   Thiếu adb → báo cần Android platform-tools / Unity Android module, dừng nếu thiết bị là Android.
2. **BugHub (bắt buộc):** `$QA bughub-check`. Mã thoát 4 / dòng `CHƯA SẴN SÀNG` → dừng skill, in nguyên các dòng
   `✗` và bảo dev chạy **`/bughub-setup`**. Không tự sửa asset/profile/server.
3. **Bug đang mở (chống trùng):** nếu có tool MCP `bughub_list` (deferred → ToolSearch `bughub`) gọi
   `bughub_list(project: <mã>)` (và lọc theo tính năng nếu tool hỗ trợ) — giữ danh sách tiêu đề + số issue để so ở
   STEP 5. Không có MCP → ghi chú "không đối chiếu được bug server" vào report, vẫn tiếp tục (tool vẫn chặn trùng
   trong máy).
4. **Probe:** dòng `probe:` của doctor. Project chưa có → `$QA probe-install` (một lần cho mỗi project; tự dò cổng
   release `GameSystems.isCheat` của template EZG). Build đang nằm trên máy vẫn là bản cũ → phiên này test **hộp
   đen**; ghi vào report "đã thêm probe — commit + build để lần sau test sâu".
5. **Mở phiên:** `$QA start <device id> --feature "<tính năng>"`. Đọc kỹ output:
   - `app … chưa cài` → tool tự cài bản build mới nhất tìm thấy trong repo; không có → dừng, báo dev cài build.
   - `probe: OK` → có `ui`, `audit` sâu, `state`, `call`, `tap --text`. `CỔNG ĐÓNG` → build release: bật cheat trên
     máy theo cách của game (template EZG: bấm version trong Settings 10 lần; máy chưa nằm trong danh sách máy test
     thì Lead thêm bằng ezg MCP `test_device_add`) rồi `$QA probe-connect`.
   - `probe: không có` → hộp đen: quyết định bằng ảnh + toạ độ, log, perf.
   - Ảnh `shot #001` → **Read** ảnh để biết game đang ở đâu.
   - Muốn tiếp tục đúng trạng thái QA đang chơi dở trên máy → thêm `--attach` (không khởi động lại game).

## 2. Hiểu tính năng → Test charter

Đừng chạm vào máy trước khi biết "đúng" trông như thế nào. Đọc theo thứ tự (dừng khi đủ):

1. [references/qa-playbook.md](references/qa-playbook.md) — một lần mỗi phiên.
2. **Tài liệu thiết kế:** `GDD/`, `TechSpec/`, `Docs/` của repo (grep tên tính năng, cả tiếng Việt lẫn tên code).
   GDD trên Google Sheets → skill `read-gdd` nếu project có. `CLAUDE_PROJECT.md` cho bức tranh game.
3. **Code:** `codegraph_explore` (có `.codegraph/`) hoặc Grep/Read: controller màn (`Screen*Controller`, prefab
   `screen_*`/`popup_*`), static Service (luật nghiệp vụ, công thức), PlayerData module (state lưu), CSV config
   (`CsvConfig/*.csv` — **giá trị kỳ vọng**: giá, thưởng, thời gian, điều kiện mở khoá), localize key, cheat
   (`Cheat_*`, GameCheat) để dựng điều kiện.
4. **Visual chuẩn:** `.claude/docs/ArtStyle.md` (nếu có) + mockup/ảnh duyệt — để biết màn nào "đúng style".
5. **Bug cũ** của tính năng (STEP 1.3) — vùng hay hỏng, đừng log lại.

Viết charter vào `<thư mục phiên>/charter.md` (đường dẫn in ở `start`) theo mẫu trong playbook §2: mục tiêu, đường
vào tính năng, trạng thái/điều kiện, **oracle** (bảng giá trị kỳ vọng có trích nguồn file:dòng), danh sách ca test
có ưu tiên:

| Ưu tiên | Nhóm ca (chi tiết + ví dụ ở playbook §3) |
|---|---|
| P0 | Happy path đầy đủ của tính năng (vào → dùng → nhận kết quả → thoát) |
| P1 | Biên & âm: thiếu tiền/thiếu điều kiện, đúng bằng ngưỡng, max level/max stack, khoá/mở khoá, rỗng/đầy danh sách |
| P2 | Gián đoạn: Back (Android) ở mỗi popup, nền 5–30s rồi quay lại, kill + mở lại giữa luồng, mất mạng lúc gọi mạng/quảng cáo/IAP, xoay màn (nếu game cho), spam/double tap nút thưởng/mua |
| P3 | Lưu trữ & đồng bộ: restart sau mỗi thay đổi state quan trọng → state còn đúng; số hiển thị khớp ở mọi màn |
| P4 | Visual sweep mọi màn/popup/trạng thái của tính năng (mục 4.1) + ngôn ngữ dài nếu game có đổi ngôn ngữ |
| P5 | Ổn định & hiệu năng: lặp luồng chính 5–10 lần, RAM/FPS trước-sau, thao tác nhanh liên tục (monkey có kiểm soát) |

Không có tài liệu → suy luật từ code + hành vi quan sát; ghi rõ trong charter oracle nào là "suy luận" (bug dựa
trên oracle suy luận thì ghi `--type gd` hoặc nêu rõ trong `--description` để GD xác nhận).

## 3. Vòng lặp chơi

Mỗi ca test:

```bash
$QA mark "P1 - mua nâng cấp khi thiếu tiền"            # bước tái hiện của bug tính từ đây
… các thao tác …
$QA note "Nút mua xám, toast 'Không đủ tiền' đúng" --result pass   # hoặc fail/blocked/skip
```

Mỗi thao tác — **quan sát → nghĩ → làm → kiểm**:

1. **Quan sát:** Read ảnh xem (`*.view.jpg`) mà lệnh trước in ra. Không bao giờ thao tác hai lần liên tiếp mà
   không nhìn màn hình (trừ spam tap cố ý).
2. **Làm:** toạ độ là **px của ảnh xem gần nhất** (tool tự đổi sang px máy):
   ```bash
   $QA tap 463 784 --as "Bấm nút Nâng cấp ở thẻ nhân vật đầu tiên"
   $QA tap --text "Collect" --as "Bấm Nhận thưởng"         # probe: theo chữ trên nút
   $QA tap @12 --as "…"                                     # probe: theo số trong `ui`
   $QA swipe --dir up --as "Cuộn danh sách nhiệm vụ"
   $QA drag 120 900 420 900 --as "Kéo vật phẩm vào ô trống"
   $QA key back --as "Bấm Back hệ thống để đóng popup"
   ```
   `--as` = hành động **như người chơi kể lại** (thành bước tái hiện của bug) — luôn có. Không chắc toạ độ →
   `$QA shot --grid` (ảnh có lưới số), có probe → `$QA ui --marks` (ảnh đánh số từng nút).
3. **Kiểm** output tự động sau mỗi thao tác:
   - `Δ … GẦN NHƯ KHÔNG ĐỔI` → thao tác không có phản hồi: chụp lại sau 1–2s; vẫn không đổi và đây là nút →
     nghi bug "nút không phản hồi"/"bị overlay vô hình che" (probe: `ui` xem `✗che:`).
   - `⚠ LOG: … lỗi MỚI` → **dừng lại xử lý ngay**: lỗi xuất hiện ở bước nào, tái hiện, log bug với `--log-sig`.
   - `⚠ ẢNH:` (màn đen/trắng trơn, mảng hồng magenta) → bug visual/render — tái hiện rồi log.
   - `⚠ app đang ở foreground: …` → quảng cáo/trình duyệt/store đã mở: xử lý như người chơi (chờ nút X, Back)
     rồi kiểm game quay về đúng chỗ.
   - So kết quả với oracle trong charter (mục 4.2).
4. **Chờ:** `$QA wait 3` (chờ rồi chụp) · `$QA wait 20 --settle` (chờ tới khi màn đứng yên) · animation/hiệu ứng
   → `$QA burst 6` (dải nhiều khung để soi nhấp nháy, nhảy layout). Thời gian chờ dài (sản xuất, hồi chiêu,
   offline) → có probe thì `$QA timescale 10`, xong trả `$QA timescale 1`; hoặc cheat tua thời gian của game.

Hướng dẫn hình ảnh: số nhỏ khó đọc trên ảnh xem → Read ảnh gốc `*.png` cùng số. Điều hướng theo cảm nhận người
chơi: tìm nút bằng mắt như user mới, nếu một người chơi bình thường **không tìm ra cách** làm việc mà tính năng yêu
cầu → đó cũng là phát hiện (UX, `--type ux`).

## 4. Phát hiện lỗi (oracle)

### 4.1 Visual — mỗi màn/popup/trạng thái MỚI

`$QA audit` → chụp + kiểm ảnh + log; có probe thì thêm audit sâu (tràn/cắt chữ, thiếu glyph, chữ là key localize /
`{0}` / NaN / null, nút ngoài màn hoặc ngoài safe area, nút bị che, vùng chạm < 6 mm, Image trắng thiếu sprite,
material lỗi, chữ chồng chữ) và ảnh `*.audit.jpg` khoanh từng lỗi. **Mỗi phát hiện của audit vẫn phải nhìn ảnh xác
nhận** (popup đang mở thì "bị che" là đúng; panel trắng có chủ đích không phải thiếu sprite).

Sau đó soi bằng mắt theo checklist playbook §4 (bố cục, chữ, ảnh/icon, trạng thái, nhất quán style ArtStyle.md,
animation, safe area/tai thỏ, tỉ lệ màn). Game có đổi ngôn ngữ → ít nhất một lượt với ngôn ngữ chữ dài (Đức/Nga/
Việt…) qua màn của tính năng, đổi lại ngôn ngữ cũ khi xong.

### 4.2 Logic

- **Đo trước/sau** mọi hành động có hệ quả: tiền tệ, item, level, timer, tiến độ nhiệm vụ, red dot. Có probe:
  `$QA state <đường dẫn>` (vd template EZG: `PlayerDataManager.PlayerResource` — đọc code để biết đúng member) đọc
  số chính xác; không có: đọc số trên ảnh (ảnh gốc `.png`).
- **So với oracle** (CSV/GDD): giá trừ đúng một lần, thưởng cộng đúng một lần, thời gian đúng, mở khoá đúng điều
  kiện, số hiển thị khớp nhau giữa HUD/popup/màn chi tiết, format số (1.2K, dấu phẩy) nhất quán.
- **Lưu trữ:** sau thay đổi state quan trọng → `$QA app restart` → state còn nguyên? Nền 30s → timer/offline đúng?
- **Gián đoạn & lặp:** P2 ở charter — Back trên mọi popup (Android), `$QA tap … --times 5 --interval 0.05` cho nút
  nhận thưởng/mua (thưởng nhân đôi = bug nghiêm trọng), `$QA net off` giữa luồng mạng (game phải báo lỗi tử tế,
  không treo, không mất/nhân đôi phần thưởng), `$QA app background --secs 10`.
- **Softlock:** màn không còn đường thoát, nút không phản hồi, loading vô tận (`wait 30 --settle` vẫn quay) → bug high.

### 4.3 Ổn định & hiệu năng

- Log tự kiểm sau mỗi thao tác; `$QA logs --all` xem toàn phiên (chữ ký, số lần lặp, bước xảy ra).
- `$QA perf` lúc bắt đầu, sau mỗi nhóm ca, lúc kết thúc: RAM tăng > 150 MB không hồi, FPS < 25 ở màn tĩnh, máy
  > 43°C → bug `--type perf`.
- Noise: lỗi SDK quảng cáo/Play Services trên emulator, warning thường → không log; lỗi lặp nhiều không liên quan
  tính năng → `$QA logs --ignore "<regex>"` để khỏi nhiễu (ghi lý do vào note). Exception trong code game luôn là
  bug dù không thấy hậu quả trên màn.

## 5. Log bug

Khi một lỗi đã xác nhận (luật 2):

1. **Trùng?** So với danh sách bug mở (STEP 1.3): cùng màn + cùng triệu chứng → không log, ghi "trùng #N".
2. **Ảnh bằng chứng:** đưa game về đúng trạng thái lỗi rồi `$QA shot`, hoặc dùng ảnh cũ: `--shot <tên file trong
   shots/>`. Bug không thấy được trên màn (logic thuần) → ảnh màn liên quan nhất.
3. **Gửi:**
   ```bash
   $QA bug --title "Shop: mua gói vàng đầu tiên trừ kim cương 2 lần khi bấm nhanh" \
     --actual "Bấm Mua 2 lần liên tiếp: kim cương 500 → 300 (trừ 2×100), vàng chỉ +1.000 một lần" \
     --expected "Trừ 100 kim cương một lần, +1.000 vàng, lần bấm thứ hai bị chặn (CoinPack.csv dòng 2: price=100)" \
     --precondition "Đã qua tutorial, có 500 kim cương (cheat cộng kim cương)" \
     --type logic --severity high --rate 3/3 --evidence "ảnh #014 (trước) #015 (sau)" \
     --log-sig <sig nếu có> [--shot 015-tap.png] [--save] [--mention "<tên dev>"]
   ```
   - `--steps` mặc định = các bước `--as` từ `mark` gần nhất (xem trước bằng `$QA steps`). Bước thiếu ý → tự viết
     `--steps "1. …\n2. …"`.
   - `--type`: crash · logic · visual · ux · perf · gd. `--severity` theo rubric playbook §6 (crash/mất tiến trình/
     mất tiền thật = critical; chặn tính năng/sai kinh tế = high; sai hiển thị rõ/luồng phụ = medium; lỗi nhỏ = low).
   - Tool tự điền category + forum tag (loại + mức độ) theo form BugHub, thiết bị, build, commit (nếu build bake),
     log app (+ đoạn log của `--log-sig`), extra `reporter=auto-test-device`. Có probe: thêm màn hiện tại, player
     id, context game, `--save` đính kèm save snapshot.
   - `--via ingame` (cần probe + Bug Logger trong build): gửi bằng chính Bug Logger trong game — report mang context
     của game y như QA bấm nút bug. Mặc định `http` (cùng contract, trả ngay số issue).
   - Trước khi gửi bug đầu tiên của phiên có thể chạy `--dry-run` để xem report.
4. Output `BUG #N → url` = xong. `BugHub từ chối …` → đọc lỗi, sửa tham số, `$QA bug-resend <id>`; lỗi mạng → thử
   lại sau. `trùng bug đã log` → đúng là trùng thì bỏ; khác thật → thêm chi tiết vào tiêu đề hoặc `--force`.
5. `$QA mark "<ca tiếp theo>"` rồi chơi tiếp — một bug không chặn phần còn lại của charter (bug chặn hẳn tính năng
   → ghi các ca phụ thuộc là `blocked`).

Mẫu tiêu đề, actual/expected tốt/xấu: playbook §7.

## 6. Kết thúc

Dừng khi mọi ca P0–P4 trong charter đã có kết quả và thêm **2 vòng thăm dò tự do** (playbook §3.7) không ra phát
hiện mới — hoặc thiết bị/app hỏng không cứu được.

1. `$QA perf` lần cuối, `$QA logs --all` — lỗi log nào là bug chưa log → xử lý theo STEP 5.
2. `$QA stop` → `report.md` trong thư mục phiên.
3. Trả lời user (tiếng Việt), ngắn:
   - Bảng bug đã log: `#N` + tiêu đề + mức độ + link (hoặc "trùng #N" / "gửi lỗi").
   - Kết quả ca test: số pass/fail/blocked, ca fail nào chưa thành bug (vì sao).
   - Phát hiện không log (nghi vấn chưa tái hiện được, góp ý UX nhỏ) — một dòng mỗi cái.
   - Đường dẫn `report.md`, thiết bị + build đã test, probe có/không (đã `probe-install` thì nhắc commit + build).

## Lệnh nhanh

| Việc | Lệnh |
|---|---|
| Thiết bị / công cụ / BugHub | `devices` · `doctor` · `bughub-check` |
| Mở / đóng phiên | `start <device> --feature "…" [--attach] [--fresh] [--install x.apk]` · `stop` · `probe-connect [--retrigger]` |
| Nhìn | `shot [--grid] [--marks]` · `burst N` · `wait S [--settle]` · `audit` · `ui [--find x] [--marks]` (probe) |
| Chạm | `tap X Y` · `tap 50% 80%` · `tap @id` / `--text` / `--name` / `--world` (probe) · `tap … --times 5` · `longpress` |
| Vuốt/kéo | `swipe X1 Y1 X2 Y2` · `swipe --dir up` · `drag X1 Y1 X2 Y2 [--hold ms]` |
| Phím / chữ | `key back\|home\|enter\|app_switch` · `text "abc" [--submit]` · `text "Tiếng Việt" --probe` |
| App | `app restart\|stop\|launch\|background --secs N\|clear\|info\|install x.apk` |
| Môi trường | `net off\|on\|wifi-off\|data-off` · `rotate portrait\|landscape\|auto` |
| Log / hiệu năng | `logs [--all] [--tail N] [--grep re] [--ignore re]` · `perf` |
| Game (probe) | `state Type.Member…` · `call Type.Method args` · `types <từ khoá>` · `timescale 10` |
| Ca test | `mark "<ca>"` · `note "<kết quả>" --result pass\|fail\|blocked\|skip` · `steps` |
| Bug | `bug --title … --actual … --expected … --type … --severity … [--log-sig] [--shot] [--save] [--dry-run]` · `bug-resend <id>` |
| Probe | `probe-install [--gate auto\|none\|Ns.Type.member] [--force]` |

Mọi lệnh thao tác nhận `--as "<mô tả>"`, `--wait S`, `--no-shot`, `--grid`, `--marks`, `--inject` (chạm qua probe).

## Phụ lục — nền tảng & giới hạn

| | Android (adb) | iOS Simulator | iPhone thật |
|---|---|---|---|
| Chạm/vuốt/giữ | input hệ thống (thật) | `idb` nếu có, không thì probe | chỉ probe |
| Chụp màn | screencap | simctl | probe |
| Log | logcat (crash Java/native, ANR, LMK, exception Unity) | log stream + probe | `devicectl --console` + probe |
| Back / mạng / xoay | có | không có Back; không mạng/xoay | không |
| Nhập chữ Unicode | probe (`text --probe`) | idb/probe | probe |

- **Probe** (`Assets/EzgQaProbe/`, từ `probe/` của skill): kích hoạt bằng token ngẫu nhiên mỗi phiên (intent extra /
  argument / file trigger), chỉ nghe localhost trên Android, build release chỉ mở khi `releaseGate` (cờ cheat) true.
  Chạm qua probe đi qua EventSystem — UI chạy tốt; object 3D chỉ nhận nếu game dùng EventSystem/PhysicsRaycaster
  hoặc `OnMouse*` (Android luôn dùng input thật nên không bị giới hạn này).
- Đa chạm (pinch zoom) không giả lập được bằng adb — test zoom bằng nút/cuộn nếu game có, ghi `skip` cho ca pinch.
- Trạng thái phiên ở `<git-common-dir>/auto-test-device/` (ảnh, log, bug đã gửi, `report.md`) — không bao giờ commit.
- `qadevice.py` chỉ dùng thư viện chuẩn Python 3.8+ (Pillow tuỳ chọn); test: `python3 -m unittest discover -s .claude/skills/auto-test-device/scripts/tests`.
