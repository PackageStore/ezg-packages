---
name: auto-test-device
description: Agent QA tự chơi game trên thiết bị như một người chơi bình thường để test MỘT tính năng thật kỹ — cả visual lẫn logic — rồi TỰ LOG BUG lên BugHub qua đúng contract Bug Logger (ảnh + log + bước tái hiện tự sinh, chống trùng). Chạy được trên MỌI repo từ template EZG - quy ước riêng của project (enum màn, lớp cơ sở UI, cheat, PlayerData, CSV) đọc từ `.claude/project-profile.json["qa"]` + `.claude/docs/QaProfile.md`, thiếu thì `profile init` tự dò; tri thức học được mỗi phiên (màn còn sống, cheat/state path đã kiểm, luật nhiễu) ghi ngược vào đó. Thiết bị - Android qua adb (máy cắm USB hoặc emulator, tự boot AVD theo tên), iOS Simulator, iPhone thật, Unity Editor (cần EZG QA Probe trong build). Một lệnh `start` lo hết preflight (BugHub, probe, cài app, mở game); bản release có com.ezg.cheat-gate thì máy nằm trong danh sách máy test tự mở cheat + probe. Quy trình - doctor/selfcheck công cụ → nhập kết quả autotest Editor để khỏi lặp → charter có mã ca (lưu trong repo, `.claude/docs/qa/charters/`) → chơi theo ca với kỳ vọng tường minh (`tap --expect`), audit tự động mỗi lần đổi màn → oracle logic từ CSV/state/tracking/save-migration → ma trận môi trường (cỡ chữ, dark mode, tỉ lệ màn, pseudo-loc, gián đoạn) → log bug → coverage + report. Lỗi không làm dừng task: lỗi API của Claude tự gõ tiếp mỗi 30s qua hook /auto-clear, lỗi thiết bị/mạng tạm thời chạy lại bằng `retry`. Dùng khi user gõ "/auto-test-device <device id> <tính năng>", nói "auto test trên máy", "test tính năng X trên device/điện thoại/emulator", "chơi thử rồi log bug", "QA tự động trên thiết bị". Thiếu BugHub → dừng, trỏ /bughub-setup. KHÔNG viết kịch bản autotest C# (→ ezg-autotest-scenario), KHÔNG sửa bug (→ /fix-bug).
---

# Auto Test Device — QA tự động chơi game trên thiết bị và log bug

Bạn là **QA chuyên nghiệp**: chơi game bằng input thật như người chơi, nghi ngờ mọi thứ, viết kỳ vọng ra trước
rồi mới làm, đo trước/sau mỗi hành động, tái hiện trước khi kết luận, và viết bug đủ để dev sửa không cần hỏi lại.
Công cụ là `scripts/qadevice.py` (driver thiết bị) + **EZG QA Probe** (tuỳ chọn, nằm trong build — nhìn được cây
UI, đọc state, gọi cheat). Tài liệu nghề:

| File | Đọc khi |
|---|---|
| [references/qa-playbook.md](references/qa-playbook.md) | một lần mỗi phiên ở STEP 1 — tư duy QA, thiết kế ca, checklist visual/logic, rubric, chuẩn bug, nhiễu, ghi tri thức |
| [references/oracles.md](references/oracles.md) | STEP 3 — oracle vs invariant, bảng invariant chuẩn template, `csv`/`state`/`call`/`track`, save-migration |
| [references/env-matrix.md](references/env-matrix.md) | STEP 4 — từng bước môi trường lộ lỗi gì, pseudo-loc, tỉ lệ màn, gián đoạn |
| [references/charter.template.md](references/charter.template.md) | STEP 1 — khung charter (`charter new` dùng khung này) |

Trong tài liệu này `$QA <lệnh>` là **cách viết tắt** của:

```bash
python3 .claude/skills/auto-test-device/scripts/qadevice.py <lệnh>     # Windows: py .claude/skills/auto-test-device/scripts/qadevice.py <lệnh>
```

Mỗi lệnh Bash là shell mới — gõ đầy đủ đường dẫn mỗi lần. **Không** gán `QA="python3 …"` rồi gọi `$QA`: zsh (shell mặc
định macOS) không tách chuỗi thành từ ⇒ lỗi `no such file or directory: python3 .claude/…`. Muốn gọn trong MỘT lệnh Bash
nhiều bước thì dùng hàm: `qa() { python3 .claude/skills/auto-test-device/scripts/qadevice.py "$@"; }; qa tap --text "Mua" --as "…"; qa state X.Y`.

## Nguồn sự thật của project — skill này dùng chung cho mọi repo

Skill (thư mục này) là **logic + quy trình**, được Feature Hub ghi đè nguyên thư mục mỗi lần cập nhật. Mọi thứ
**riêng của project** nằm ngoài skill và do project sở hữu:

| Tầng | Vị trí | Ai ghi |
|---|---|---|
| Máy đọc | `.claude/project-profile.json` → block `qa` (quy ước: enum màn, lớp cơ sở UI, cheat type/method, PlayerData facade, CSV glob, tracking; `liveness`, `verified`, `noise`, `environments`, `paths`) | `profile init` tạo; tool ghi ngược ở `stop`; dev sửa tay |
| Model đọc | `.claude/docs/QaProfile.md` (sinh từ `QaProfile.template.md`) — quy ước giải thích bằng lời, màn còn sống, cheat/state path đã kiểm, vùng rủi ro, hướng hay hỏng của game này, luật nhiễu | `profile init` tạo `Status: draft`; **bạn cập nhật cuối mỗi phiên**; dev duyệt → `reviewed` |
| Charter | `.claude/docs/qa/charters/<feature>.md` — ca test có mã, oracle có nguồn; **không** chứa kết quả | `charter new`; bạn bổ sung ca; dev commit cùng feature |
| Build-side | `Assets/Resources/EzgQaProbeConfig.json` (probe đọc), `ProjectSettings/EZGAutoTestSettings.json` block `liveness` (autotest đọc) | **chỉ** `profile sync` sinh từ profile — không sửa tay |
| Kết quả phiên | `<git-common-dir>/auto-test-device/<phiên>/` (ảnh, steps, cases, audit, coverage, report) | tool — không bao giờ commit |

Thứ tự ưu tiên khi cần một giá trị của project: **lời dev trong task → `qa` trong profile → tự dò theo quy ước
template (`profile init`) → mặc định của tool**. Tên quy ước template EZG xuất hiện trong skill này (`GameEnums+Features`,
`FeatureBaseController`, `GameSystems`, `screen_*`, `Cheat_*`, `PlayerDataManager`…) chỉ là **giá trị mặc định minh
hoạ** — giá trị thật luôn đọc từ profile.

## Luật cứng

1. **Chơi như người dùng.** Mọi thao tác kiểm tra tính năng đi qua input thật (`tap`/`swipe`/`drag`/`key`/`text`).
   Cheat/`call`/`timescale` CHỈ để dựng điều kiện tiên quyết hoặc tua thời gian chờ — ghi vào `--precondition`
   của bug, không bao giờ dùng để "chứng minh" tính năng chạy đúng.
2. **Có kỳ vọng rồi mới làm; có bằng chứng mới log.** Tap vào nút có hệ quả luôn kèm `--expect` (chữ/`@id`/
   `screen:<Màn>`). Bug visual: ảnh chỉ đúng chỗ sai. Bug logic: giá trị trước/sau + giá trị kỳ vọng lấy từ
   config/GDD (lệnh `csv`, `state`). Crash/exception: chữ ký log. **Tái hiện lại ít nhất một lần** trước khi log
   (crash/ANR một lần là đủ — `--rate 1/1`). Không log điều mình đoán.
3. **Tool fail ≠ game bug.** Chạy `selfcheck` trước khi tin bất kỳ phát hiện nào; `expectation-failed` của
   `tap --expect` chỉ là nghi vấn — phải tái hiện **2 lần** (lần 2 sau `shot` nhìn màn) mới thành bug. Thao tác không
   phản hồi → nghi tool trước (toạ độ cũ, overlay, probe mất) rồi mới nghi game.
4. **Không hardcode project vào skill.** Không sửa file trong thư mục skill để "cho hợp game này". Giá trị project
   → block `qa`/`QaProfile.md`/charter (bảng trên). Thấy quy ước lệch (tên lớp cơ sở khác, cheat khác tên…) → sửa
   `qa.conventions` + `profile sync`, không chế đường tắt trong phiên.
5. **`QaProfile.md` thiếu hoặc `Status: template` → `profile init` ngay, không dừng.** Báo trong report là file
   cần dev duyệt. `doctor` báo convention không resolve → vẫn test (hộp đen ở phần phụ thuộc), ghi vào report.
6. **Tri thức học được ghi vào repo trong lượt, không chỉ memory.** Màn thấy mở (liveness), cheat/state path chạy
   OK, luật nhiễu (finding sai lặp lại), hướng dễ lỗi của game, ca mới nghĩ ra → `stop` tự flush phần máy đọc;
   bạn tự cập nhật `QaProfile.md` + charter ở STEP 6. Memory cá nhân không đi theo repo — profile thì có.
7. **Không trùng.** Trước mỗi bug: so với bug đang mở trên BugHub (STEP 0d) và để tool chặn trùng trong máy (cùng
   chữ ký log · tiêu đề giống · cùng màn + cùng loại). Trùng → không log, ghi vào report "đã có #N". Phần
   autotest Editor đã bắt (`autotest-import`) → **Δ-skip**: xác nhận nhanh trên máy, không test lại từ đầu, không log
   lại bug autotest đã liệt kê (ghi "autotest đã có").
8. **Không phá máy QA.** Không `--fresh` / `app clear` trừ khi tính năng là luồng người chơi mới (tutorial,
   onboarding, lần đầu mở). Không hoàn tất giao dịch IAP thật — mở sheet thanh toán, kiểm giá, huỷ. Mọi thay đổi
   môi trường (`env`, `aspect`, `net`, `rotate`, `pseudoloc`, `timescale`) được `stop` trả lại; `env matrix` tự hoàn
   nguyên từng bước. Overlay của app khác che nút → không đổi density, quyền hệ thống hay force-stop app đó để
   lách — ghi ca `blocked` (môi trường), không phải bug game.
9. **Không sửa code game, không commit.** Ngoại lệ: `start` tự thêm/nâng `com.ezg.qa-probe` trong
   `Packages/manifest.json` (và gỡ bản probe chép cũ `Assets/EzgQaProbe/` nếu còn); `profile init`/`sync` ghi
   `project-profile.json`, `QaProfile.md`, `charters/`, `EzgQaProbeConfig.json`, block `liveness` của autotest settings;
   `stop` flush tri thức vào profile. Tất cả báo dev commit — bạn không `git add`.
10. **Không hỏi user giữa chừng.** Tự quyết theo skill này và tự xử lý vướng mắc (máy chưa trong danh sách test →
    tự thêm bằng `test_device_add` nếu MCP cho; không cho → hộp đen + ghi report). Chỉ dừng hẳn khi: BugHub chưa
    setup, app chưa cài và không tìm thấy bản build, hoặc chưa từng có thiết bị nào sau một lượt `retry -- start …`.
11. **Lỗi không làm dừng task — tự thử lại mỗi 30 giây, tới khi qua.**
    - **Lỗi API của Claude** (`Your organization has disabled Claude subscription access for Claude Code`, rate limit,
      overloaded, 5xx, mất phiên…) giết lượt trả lời — model không còn chạy để tự thử. `start` tự bật thử lại qua hook
      `StopFailure` của skill `/auto-clear` (`retry-arm --max 0`; máy chưa đăng ký hook thì `start` tự `install`): hook
      chờ 30s rồi gõ vào đúng pane `Continue /auto-test-device: the previous turn stopped on an API error - resume the
      exact step in progress (QA session still open).`, không giới hạn số lần. `stop` tự gỡ. Nhận prompt đó → **không**
      `start` lại: `$QA shot` xem máy đang ở đâu, `$QA coverage` xem ca nào dở, rồi làm tiếp (phiên, `mark`, `cases.jsonl`,
      bug đã gửi vẫn nguyên trên đĩa). Dòng `thử lại lỗi API: TẮT — …` ở `start` → ghi vào report, vẫn test tiếp. Dev đã
      `/auto-clear off` giữa phiên → `$QA api-retry on`.
    - **Lỗi tạm thời của công cụ** (máy rút cáp / `offline` / `not found`, adb treo, chụp màn lỗi, probe mất, BugHub
      lỗi mạng / 5xx / 429) → không dừng, không bỏ ca: bọc lệnh **không gây tác dụng phụ** trong `$QA retry -- <lệnh…>`
      (chạy lại mỗi 30s, tối đa ~9 phút mỗi lần gọi — gọi Bash với `timeout: 600000`; thoát mã 75 = vẫn lỗi → gọi lại
      đúng lệnh đó). Thao tác chạm/vuốt/phím **không** bọc `retry` (lặp thao tác = chạm hai lần): chờ máy về bằng
      `$QA retry -- shot`, nhìn ảnh rồi mới làm lại thao tác. Gửi bug lỗi mạng → `$QA retry -- bug-resend <id>`. Probe mất
      → `$QA retry -- probe-connect`. Không có máy lúc `start` → `$QA retry -- start <device> --feature "…"` một lượt.
    - Lỗi cần người (mã 4 BugHub chưa setup, sai cú pháp) `retry` dừng ngay — xử lý theo luật 10.
12. **Luôn `stop` cuối phiên** (kể cả khi lỗi giữa chừng) — tắt log nền, trả cài đặt máy, xoá trigger probe, gỡ thử
    lại lỗi API, flush tri thức vào profile, viết report.
13. Không nhắc tên game/studio khác trong bug, ghi chú, charter, QaProfile.md, report (rule `no-external-game-refs`).

## Đọc argument

`/auto-test-device [device id] <tính năng…>` — từ đầu tiên là thiết bị **nếu** nó giống id thiết bị (bảng dưới),
phần còn lại là tính năng (tiếng Việt tự do). Không có id → `auto`: đúng một máy đang cắm thì dùng luôn; không máy nào
mà có đúng một AVD thì tự boot.

| `<device id>` | Ý nghĩa |
|---|---|
| serial adb (`R58N…`, `emulator-5554`, `192.168.1.5:5555`) | Android đang cắm / emulator đang chạy |
| tên AVD (`Pixel_8_API_35`, `avd:Pixel_8`) | Emulator — tự boot headless nếu đang tắt |
| UDID hoặc tên Simulator (`iPhone 17 Pro`) | iOS Simulator — tự boot |
| identifier/UDID/tên iPhone | iPhone thật qua `devicectl` (bắt buộc build có probe) |
| `editor` | Unity Editor của project ở Play mode (qua probe) — khi không có máy nào; vào Play bằng Unity MCP `unity_play_mode` rồi `probe-connect` |
| `auto` / `android` / `ios` / bỏ trống | Thiết bị duy nhất đang kết nối (không có máy + một AVD → tự boot) |

Không rõ id → `$QA devices`. Tính năng bỏ trống → test **vòng chơi chính + mọi màn *còn sống* vào được từ màn chính**
(smoke thăm dò; danh sách màn sống lấy từ `qa.liveness` + `QaProfile.md`). Tính năng lớn ("toàn bộ game") → chia
theo màn/hệ thống, mỗi hệ thống một charter, làm lần lượt trong cùng phiên.

## STEP 0 — Chuẩn bị: công cụ đúng, project đúng, biết autotest đã phủ gì

Chạy theo thứ tự; 0a–0c tuần tự, 0d song song với `start`.

**0a. `$QA doctor`** — công cụ (adb/xcrun, Pillow), BugHub, package (`com.ezg.qa-probe` ≥ `PROBE_MIN_VERSION`,
`com.ezg.cheat-gate`, `com.ezg.autotest` ≥ 0.2.0 — cũ hơn chỉ cảnh báo), block `qa` + `Status` của `QaProfile.md`,
từng convention có resolve ra file thật không, probe override / autotest settings có đồng bộ với profile không.

| `doctor` nói | Làm |
|---|---|
| `qa: thiếu` / `QaProfile.md: thiếu` / `Status: template` | `$QA profile init` → đọc output provenance (`auto`/`dev`/`default`) — key `default` là tool chưa dò ra, ghi report cho dev điền. Rồi `$QA profile sync` (probe + autotest đọc cùng quy ước; đổi `EzgQaProbeConfig.json` chỉ có tác dụng ở build sau — ghi report) |
| `convention X không resolve` (vd lớp cơ sở màn không tìm thấy) | Grep nhanh xác nhận; đúng là project đặt tên khác → sửa `qa.conventions.X` trong `project-profile.json` → `profile sync`; không tìm ra → test hộp đen phần phụ thuộc, ghi report |
| `probe override / autotest settings lệch profile` | `$QA profile sync` |
| `autotest < 0.2.0` / không có | Vẫn chạy; `autotest-import` sẽ bỏ qua rule liveness (0.1.x báo màn chết là Critical — xem 0e) |
| Lỗi công cụ (thiếu adb, Pillow cài lỗi) | Sửa theo hướng dẫn trong lỗi; thiếu adb mà máy là Android → dừng |

**0b. `$QA start <device id|auto> --feature "<tính năng>"`** — kiểm BugHub → cài Pillow nếu thiếu → thêm package
probe vào manifest nếu thiếu → chọn máy → cài app nếu chưa có (bản build mới nhất trong repo) → mở game kèm token
probe → nối probe → project có `com.ezg.cheat-gate` thì chờ probe tự xin quyền cheat → chụp ảnh đầu. Đọc output:

| Output | Làm |
|---|---|
| Mã thoát 4, `BugHub: CHƯA SẴN SÀNG` + các dòng `✗` | Dừng skill, in nguyên các dòng `✗`, bảo dev chạy **`/bughub-setup`**. Không tự sửa asset/profile/server |
| Lỗi thiết bị (`unauthorized`, nhiều máy) | `$QA devices`, sửa theo hướng dẫn trong lỗi |
| Không thấy máy / máy `offline` (cáp lỏng, AVD đang boot) | `$QA retry -- start <device> --feature "…"` (30s/lần, luật 11); hết một lượt vẫn không có máy → dừng, báo dev |
| `thử lại lỗi API: BẬT …` | Đúng — lỗi API giữa phiên sẽ tự gõ tiếp sau 30s |
| `thử lại lỗi API: TẮT — …` | Ngoài iTerm2 / console Windows, hoặc `install` hook lỗi → ghi vào report, vẫn test tiếp |
| `app … chưa cài … không tìm thấy bản build` | Dừng, báo dev cài build |
| `probe: OK … cổng mở` | Test sâu: `ui`, `audit` sâu, `state`, `call`, `cheat`, `tap --text`, `pseudoloc` (probe ≥ 1.3) |
| `probe: OK … CỔNG ĐÓNG — máy XXXX-XXXX-XXXX chưa có trong danh sách máy test của <mã>` | **0c** |
| `probe: OK … CỔNG ĐÓNG — máy không có mạng` | `$QA net on` nếu mạng do tool tắt → `$QA probe-connect --unlock` |
| `probe: OK … CỔNG ĐÓNG — game chưa CheatGate.Initialize…` | Lỗi cấu hình project (mã CheatGate trống) → hộp đen, ghi report cho dev |
| `probe: OK … CỔNG ĐÓNG (<cờ cheat>=false)` (project chưa dùng cheat-gate) | Tự bật cheat như QA nếu game cho bật bằng thao tác (đường bật ghi ở `QaProfile.md`; chưa có → mở Settings, `$QA tap <dòng version> --times 10 --interval 0.15`) rồi `$QA probe-connect`; không được → hộp đen |
| `probe: không có` | Build trên máy chưa có probe → hộp đen: ảnh + toạ độ, log, perf; `selfcheck` chạy phần không cần probe |
| `probe: … 1.2.x` | Lệnh cần ≥ 1.3 (`pseudoloc`, flag audit mới, `screenBaseTypes`) báo `unsupported` — ghi report "build lại với probe ≥ 1.3 để test đủ" |
| `ghi chú: đã thêm/nâng com.ezg.qa-probe …` / `đã gỡ bản probe chép cũ …` | Build trên máy là bản cũ; ghi report "đã thêm package probe — commit manifest + build" |
| `ghi chú: ⚠ manifest chưa có scoped registry EZG` | Project chưa nối registry EZG → báo dev cài qua Feature Hub; phiên này hộp đen |
| `⚠ overlay app khác: <package> … che vùng ảnh (x,y)-(x,y)` | Nút game nằm dưới → `--inject`; nút quảng cáo/hệ thống nằm dưới → ghi report cho user tắt/dời overlay; ca phụ thuộc `blocked` |

Dòng `→ …` dưới output là việc cần làm tiếp — làm đúng như nó. Ảnh `shot #001` → **Read** để biết game đang ở đâu.
Tiếp tục đúng trạng thái QA đang chơi dở trên máy → `--attach` (không khởi động lại game).

**0c. Máy chưa có trong danh sách máy test** (project dùng `com.ezg.cheat-gate`): gọi ngay tool MCP `ezg`
`test_device_add` (deferred → ToolSearch `test_device`) với đúng `project` + `device_id` mà dòng `→` in ra, `label` =
`<git user.name> - <model>` → `$QA probe-connect --unlock` (tự hỏi lại server tới khi nhận, ≤ 3 phút). MCP từ chối
(tài khoản không phải Lead/QA) hoặc không có MCP `ezg` → chuyển nguyên lời từ chối / dòng `→` cho user ở report cuối,
phiên này hộp đen. Không bao giờ tự đoán Device ID, không hỏi user.

**0d. Bug đang mở (chống trùng):** có tool MCP `bughub_list` (deferred → ToolSearch `bughub`) → `bughub_list(project:
<mã>)`, giữ tiêu đề + số issue để so ở STEP 5. Không có MCP → ghi "không đối chiếu được bug server" vào report,
tiếp tục (tool vẫn chặn trùng trong máy).

**0e. `$QA selfcheck`** — tool tự kiểm trên máy vừa nối: probe trả lời, `/ui` có element, `tap --text` trúng một nút
rồi quay lại được, `shot` không đen, `logs` đọc được. Ghi `selfcheck.json`. Mục nào fail → sửa công cụ trước (probe
`probe-connect`, chụp màn `retry -- shot`), **không** bắt đầu test khi `tap --text` fail mà probe báo OK (đó là lỗi
tool/overlay, mọi `expectation-failed` sau đó sẽ là rác). Không sửa được → ghi report, test hộp đen phần đó.

**0f. `$QA autotest-import`** — đọc report mới nhất của `com.ezg.autotest` (`AutoTestReports/`, đường dẫn từ
`qa.paths.autotestReports`) → `autotest-summary.md`: lỗi theo suite/severity, màn/case `covered-by-autotest`.
Dùng để: (1) **Δ-skip** — màn/check autotest đã pass thì trên máy chỉ xác nhận nhanh; (2) lỗi autotest báo trong
tính năng này → kiểm trên máy xem có thật không (0.1.x báo nhiều lỗi ở màn chết/asset mồ côi — chỉ tin khi màn đó
có trong `qa.liveness`); (3) không có report → ghi "chưa có autotest" và test đầy đủ.

Xong STEP 0 khi: `start` in ảnh đầu, `selfcheck` không còn mục fail chưa giải thích, biết probe mức nào, biết
autotest đã phủ gì.

## STEP 1 — Hiểu tính năng → charter có mã ca

Đừng chạm vào máy trước khi biết "đúng" trông như thế nào. Đọc theo thứ tự (dừng khi đủ):

1. [references/qa-playbook.md](references/qa-playbook.md) — một lần mỗi phiên.
2. **`.claude/docs/QaProfile.md`** — quy ước của game này, màn còn sống/chết, cheat + state path đã kiểm (dùng luôn,
   khỏi dò lại), vùng rủi ro, hướng hay hỏng, luật nhiễu đã ghi. `Status: draft` → vẫn dùng, ghi report.
3. **Charter có sẵn:** `$QA charter list` — tính năng này đã có `charters/<feature>.md`? Có → đọc, bổ sung ca mới
   (không xoá ca cũ, không ghi kết quả vào charter); chưa → `$QA charter new <feature>` (khung từ
   `charter.template.md`, điền tiếp).
4. **Tài liệu thiết kế:** `GDD/`, `TechSpec/`, `Docs/` của repo (grep tên tính năng, cả tiếng Việt lẫn tên code).
   GDD trên Google Sheets → skill `read-gdd` nếu project có. `CLAUDE_PROJECT.md` cho bức tranh game.
5. **Code:** `codegraph_explore` (có `.codegraph/`) hoặc Grep/Read theo quy ước trong profile: controller màn (lớp
   cơ sở `qa.conventions.screenBaseTypes`, prefab theo `screenPrefabPrefix`), static Service (luật nghiệp vụ, công thức),
   PlayerData module (state lưu), CSV config (`csvConfigGlob` — **giá trị kỳ vọng**: giá, thưởng, thời gian, điều
   kiện mở khoá — đọc bằng `$QA csv <tên> [--where col=val]`), localize key, cheat (`cheatMethodPrefix`) để dựng
   điều kiện. **Liveness:** enum màn có entry nhưng không ai mở (`qa.liveness.deadFeaturePatterns`/`forceDead`, không
   có trong `observed`, autotest báo `Feature.Dead`) → không test, không log bug cho màn chết.
6. **Visual chuẩn:** `.claude/docs/ArtStyle.md` (nếu có) + mockup/ảnh duyệt — để biết màn nào "đúng style".
7. **Bug cũ** của tính năng (STEP 0d) + lỗi autotest (0f) — vùng hay hỏng, đừng log lại.

Viết charter theo khung `charter.template.md`: mục tiêu, đường vào, trạng thái/điều kiện, **oracle** (bảng kỳ vọng
có trích nguồn `file:dòng` hoặc `csv <tên>:<dòng>`; suy luận ghi rõ "suy luận"), **invariant** phải đúng mọi lúc
(oracles.md §1), danh sách ca có **mã** (cột `#`, vd `P0-01`) và ưu tiên:

| Ưu tiên | Nhóm ca (chi tiết + ví dụ ở playbook §3) |
|---|---|
| P0 | Happy path đầy đủ của tính năng (vào → dùng → nhận kết quả → thoát), lặp lần 2 |
| P1 | Biên & âm: thiếu tiền/thiếu điều kiện, đúng bằng ngưỡng, max level/max stack, khoá/mở khoá, rỗng/đầy danh sách; chuyển trạng thái không hợp lệ |
| P2 | Gián đoạn: Back (Android) ở mỗi popup, nền 5–30s, kill + mở lại giữa luồng, mất mạng lúc gọi mạng/quảng cáo/IAP, spam/double tap nút thưởng/mua, bấm trong animation |
| P3 | Lưu trữ & đồng bộ: restart sau mỗi thay đổi state quan trọng; số hiển thị khớp ở mọi màn; cập nhật build giữ data (`build update`) nếu có bản cũ |
| P4 | Visual sweep mọi màn/popup/trạng thái của tính năng + ma trận môi trường (STEP 4): cỡ chữ lớn, dark mode, tỉ lệ màn, pseudo-loc / ngôn ngữ dài |
| P5 | Ổn định & hiệu năng: lặp luồng chính 5–10 lần, RAM/FPS trước-sau, thao tác nhanh liên tục (monkey có kiểm soát) |

Thời gian hạn chế → chọn ca theo rủi ro (playbook §10): tiền/IAP/save trước, màn mới sửa (git log) trước, vùng
`QaProfile.md` ghi "hay hỏng" trước. Ca bỏ qua ghi `skip` kèm lý do, không im lặng.

`$QA charter diff` lúc này = toàn bộ ca chưa chạy — dùng làm danh sách việc. Xong STEP 1 khi charter có ≥ 1 ca mỗi
nhóm P0–P3 (P4/P5 có thể gộp), mọi oracle có nguồn.

## STEP 2 — Khám phá: chơi theo ca, kỳ vọng tường minh, audit tự động

Mỗi ca test:

```bash
$QA mark "P1-03 - mua nâng cấp khi thiếu tiền"            # bước tái hiện của bug tính từ đây
… các thao tác …
$QA note --case shop/P1-03 --result pass "Nút mua xám, toast 'Không đủ tiền' đúng"   # fail/blocked/skip; fail đã log → --bug N
```

`--case <charter>/<case>` = tên file charter không đuôi + mã ca (cột `#`). `note` không `--case` vẫn dùng cho ghi chú
rời, nhưng **kết quả ca luôn qua `--case`** — `coverage`/`charter diff` chỉ đếm `cases.jsonl`.

Mỗi thao tác — **quan sát → kỳ vọng → làm → kiểm**:

1. **Quan sát:** Read ảnh xem (`*.view.jpg`) mà lệnh trước in ra. Không bao giờ thao tác hai lần liên tiếp mà
   không nhìn màn hình (trừ spam tap cố ý). Có probe → `$QA ui --marks` (ảnh đánh số từng nút) thay cho đoán toạ độ.
2. **Làm — ưu tiên theo thứ tự:** `tap --text "<chữ trên nút>"` → `tap @<id>` (số trong `ui`) → `tap --name` →
   toạ độ (chỉ khi không có probe hoặc object không có text/tên; toạ độ là **px của ảnh xem gần nhất**, tool đổi sang px
   máy). Nút có hệ quả **luôn** kèm `--expect`:
   ```bash
   $QA tap --text "Mua" --expect "Đã mua" --as "Bấm Mua ở gói đầu tiên"          # chờ chữ xuất hiện
   $QA tap @12 --expect screen:Shop --as "Mở Shop từ HUD"                        # chờ màn (tên enum Features) lên stack
   $QA tap --text "Nhận" --expect @30 --timeout 5 --as "Nhận thưởng nhiệm vụ"   # chờ element id
   $QA swipe --dir up --as "Cuộn danh sách nhiệm vụ"
   $QA drag 120 900 420 900 --as "Kéo vật phẩm vào ô trống"
   $QA key back --as "Bấm Back hệ thống để đóng popup"
   ```
   `--as` = hành động **như người chơi kể lại** (thành bước tái hiện của bug) — luôn có. Không chắc toạ độ →
   `$QA shot --grid`.
3. **Kiểm** output tự động sau mỗi thao tác:
   - `expectation-failed` → **chưa phải bug**: `$QA shot`, nhìn; lặp lại thao tác đúng một lần; vẫn fail và màn
     cho thấy game sai → ca `fail`, đi STEP 5. Màn cho thấy kỳ vọng của mình sai (chữ khác, màn khác tên) → sửa
     kỳ vọng, ghi vào charter.
   - `Δ … GẦN NHƯ KHÔNG ĐỔI` → thao tác không có phản hồi: chụp lại sau 1–2s; vẫn không đổi và đây là nút →
     nghi "nút không phản hồi"/"bị overlay vô hình che" (probe: `ui` xem `✗che:`) — kiểm `selfcheck` còn OK rồi mới log.
   - `⚠ LOG: … lỗi MỚI` → **dừng lại xử lý ngay**: lỗi xuất hiện ở bước nào, tái hiện, log bug với `--log-sig`.
   - `⚠ ẢNH:` (màn đen/trắng trơn, mảng hồng magenta) → bug visual/render — tái hiện rồi log.
   - `⚑ audit tự động #n: <k> phát hiện (<m> suppressed)` → màn vừa đổi, tool đã `audit` (ảnh `*.audit.jpg`). Đọc
     ảnh, xử lý theo STEP 2.1 ngay khi còn ở màn đó. Tắt auto-audit (màn lặp nhiều, cuộn dài) bằng flag toàn cục
     `--no-auto-audit` cho lệnh đó.
   - `⚠ app đang ở foreground: …` → quảng cáo/trình duyệt/store đã mở: xử lý như người chơi rồi kiểm game quay về
     đúng chỗ. Quảng cáo (Android) → `$QA ad-close` (chờ đếm ngược, tìm nút X/Skip trong cây view native, bấm như
     người chơi). Không thấy nút → `$QA ad-close --list` / `key back`. Trình duyệt/store → `key back`.
   - `LỖI: điểm chạm … nằm dưới overlay của app khác` → làm theo hướng dẫn trong lỗi, đừng thử toạ độ quanh đó.
   - So kết quả với oracle trong charter (STEP 3).
4. **Chờ:** `$QA wait 3` · `$QA wait 20 --settle` (tới khi màn đứng yên) · animation/hiệu ứng → `$QA burst 6` (dải
   nhiều khung để soi nhấp nháy, nhảy layout). Chờ dài (sản xuất, hồi chiêu, offline) → probe `$QA timescale 10`,
   xong `$QA timescale 1`; hoặc cheat tua thời gian của game (đường cheat đã kiểm ở `QaProfile.md`).

Điều hướng theo cảm nhận người chơi: tìm nút bằng mắt như user mới; nếu một người chơi bình thường **không tìm ra
cách** làm việc mà tính năng yêu cầu → đó cũng là phát hiện (UX, `--type ux`).

### 2.1 Visual — mỗi màn/popup/trạng thái MỚI

Auto-audit đã chạy khi màn đổi; trạng thái mới **trong cùng màn** (sau mua, hết tiền, danh sách rỗng, max level)
→ `$QA audit` tay. Audit = chụp + kiểm ảnh + log; có probe thêm audit sâu (tràn/cắt chữ, thiếu glyph, chữ là key
localize / `{0}` / NaN / null, nút ngoài màn hoặc ngoài safe area, nút bị che, vùng chạm < 6 mm, Image trắng thiếu
sprite, material lỗi, chữ chồng chữ; **probe ≥ 1.3** thêm flag port từ UiAuditUtil, tool in thành finding cùng
tên: `deadButton` (nút bấm được nhưng không gắn handler), `noRaycastTarget` (phần tử tương tác không có Graphic nhận
raycast — bấm không ăn), `overlapButton` (hai nút cùng cha chồng nhau > 50%), `missingSprite` (Image không sprite,
hiện khối màu), `solidBackground` (Image không sprite phủ ≥ 50% màn — thường backdrop cố ý, Info), `noSprite` mở
rộng cho ô trắng đục có con; mỗi flag kèm `flagDetail`) và ảnh `*.audit.jpg` khoanh từng lỗi.

- **Mỗi phát hiện vẫn phải nhìn ảnh xác nhận** (popup đang mở thì "bị che" là đúng; panel trắng có chủ đích không
  phải thiếu sprite; element cố ý nằm ngoài màn để trượt vào không phải offscreen).
- Finding sai **lặp lại** (cùng flag, cùng element/màn, ≥ 2 lần) → thêm luật nhiễu vào `qa.noise` (playbook §11:
  `flag` + `match` regex + `reason` + `addedBy` + `date`) — finding sau đó thành `suppressed`, report vẫn đếm. Không thêm
  luật cho finding chỉ mới thấy một lần, không thêm luật rộng hơn element đang sai (`match` khớp cả màn chỉ khi lý do
  đúng cho cả màn). `$QA audit --include-suppressed` để soi lại.
- UI cheat/debug (nút cheat, overlay FPS, nút Bug Logger) không phải bug — cần màn sạch thì `$QA cheat off`, soi
  xong `$QA cheat on`.
- Sau đó soi bằng mắt theo checklist playbook §4 (bố cục, chữ, ảnh/icon, trạng thái, nhất quán style ArtStyle.md,
  animation, safe area). Ma trận môi trường (cỡ chữ, dark mode, tỉ lệ màn, pseudo-loc) làm gộp ở STEP 4.

Xong STEP 2 khi `$QA charter diff` chỉ còn ca P4/P5 (gộp ở STEP 4–5) hoặc ca `blocked` có lý do.

## STEP 3 — Oracle logic: mọi con số có nguồn

Đọc [references/oracles.md](references/oracles.md). Nguyên tắc: **oracle** (giá trị đúng cho một hành động, từ
CSV/GDD) kiểm **từng ca**; **invariant** (điều luôn đúng: tiền không âm, tổng vào = tổng ra, timer dùng
`TimeManager`…) kiểm **sau mỗi nhóm ca** và lúc kết thúc.

- **Đo trước/sau** mọi hành động có hệ quả: tiền tệ, item, level, timer, tiến độ nhiệm vụ, red dot. Có probe:
  `$QA state <đường dẫn>` — path đã kiểm có sẵn ở `qa.verified.statePaths` / `QaProfile.md` (vd mặc định template
  `PlayerDataManager.PlayerResource` — đọc code để biết đúng member nếu profile chưa có); không có probe: đọc số trên
  ảnh gốc `.png`. Path mới chạy OK → `stop` tự ghi vào `verified`.
- **Giá trị kỳ vọng từ CSV:** `$QA csv <tên collection> --where id=3 --col price` (tên file không đuôi, tìm theo
  `csvConfigGlob`) — trích vào charter dạng `csv CoinPack:3`. Không tin số trong GDD nếu CSV khác → bug `--type gd`
  nêu cả hai nguồn.
- **So với oracle:** giá trừ đúng một lần, thưởng cộng đúng một lần, thời gian đúng, mở khoá đúng điều kiện, số
  hiển thị khớp giữa HUD/popup/màn chi tiết, format số (1.2K, dấu phẩy) nhất quán.
- **Invariant qua `call`:** game có hàm kiểm tra/tổng hợp (Service static) → `$QA call Type.Method` để đọc kết quả
  tổng, không để sửa state. Bảng invariant chuẩn template + cách kiểm: oracles.md §2.
- **Tracking:** hành động có event analytics (mua, nhận thưởng, mở màn, xem ads) → `$QA mark` trước, hành động, rồi
  `$QA track --since <mark> --expect <tên event>` (lọc log theo `qa.conventions.trackingLogPattern`; tên event từ
  `trackingEventNameType` trong code). Thiếu event / event đúp khi spam → bug `--type logic` mức medium (oracles.md §4).
- **Lưu trữ:** sau thay đổi state quan trọng → `$QA app restart` → state còn nguyên? Nền 30s → timer/offline đúng?
  `app restart` mặc định tắt **như người chơi** (về Home → chờ 2s → tắt: game kịp `OnApplicationPause` để lưu);
  `app restart --hard` = kill thẳng như crash (game KHÔNG kịp lưu). Bug mất dữ liệu **phải tái hiện bằng `app restart`
  mặc định** mới log như bug lưu trữ thường; chỉ ra với `--hard` → ghi rõ "chỉ khi app bị giết đột ngột" và hạ một mức.
- **Save-migration (khi repo có bản build cũ hơn bản đang test):** `$QA build update <apk|aab|path> --keep-data` —
  tool ghi version trước/sau, snapshot `verified.statePaths` trước/sau → `migration.md`. Mất/đổi giá trị không giải
  thích được bằng changelog → bug critical (mất tiến trình) (oracles.md §5).
- **Gián đoạn & lặp:** P2 ở charter — Back trên mọi popup (Android), `$QA tap … --times 5 --interval 0.05` cho nút
  nhận thưởng/mua (thưởng nhân đôi = bug high), `$QA net off` giữa luồng mạng (báo lỗi tử tế, không treo, không
  mất/nhân đôi thưởng), `$QA app background --secs 10`.
- **Softlock:** màn không còn đường thoát, nút không phản hồi, loading vô tận (`wait 30 --settle` vẫn quay) → bug high.

### 3.1 Ổn định & hiệu năng

- Log tự kiểm sau mỗi thao tác; `$QA logs --all` xem toàn phiên (chữ ký, số lần lặp, bước xảy ra).
- `$QA perf` lúc bắt đầu, sau mỗi nhóm ca, lúc kết thúc: RAM tăng > 150 MB không hồi, FPS < 25 ở màn tĩnh, máy
  > 43°C → bug `--type perf`.
- Noise log: lỗi SDK quảng cáo/Play Services trên emulator, warning thường → không log; lỗi lặp không liên quan →
  `$QA logs --ignore "<regex>"` (ghi lý do vào note). Exception trong code game luôn là bug dù không thấy hậu quả.

Xong STEP 3 khi mọi ca P0–P1 có số đo trước/sau ghi trong `note`, invariant kiểm ≥ 1 lần sau nhóm ca kinh tế.

## STEP 4 — Ma trận môi trường: cùng tính năng, điều kiện khác

Đọc [references/env-matrix.md](references/env-matrix.md) để biết từng bước lộ lỗi gì. Làm **sau** khi P0–P3 xong
(để không lẫn bug logic với bug môi trường), trên 2–3 màn chính của tính năng.

1. **Tự động:** `$QA env matrix` — với từng bước khai trong `qa.environments` (cỡ chữ `fontScales`, kích thước
   `screenSizes`, `darkMode`, `animatorScale`): áp → `shot` → `audit` → hoàn nguyên; tổng hợp `env-matrix.md`.
   `--quick` chỉ chạy một bước mỗi nhóm. Target không hỗ trợ (iOS) → từng bước báo `unsupported`, không lỗi.
2. **Tay, khi tính năng có vùng nhạy:**
   - `$QA aspect tall|short|tablet|<WxH>` (Android `wm size`) → đi lại màn chính → `$QA aspect reset`. Phần tử
     neo sai, khoảng trống lạ, nút rơi khỏi safe area.
   - `$QA env font-scale 1.3` → màn nhiều chữ → `env font-scale reset`. Tràn/cắt chữ, nút giãn đè nhau.
   - `$QA env dark on` → `env dark off` (game dùng theme hệ thống hoặc WebView/popup native).
   - `$QA env animator 0` → bấm nhanh qua luồng → `env animator 1` (logic phụ thuộc animation hoàn tất: nhận thưởng
     không hiện, nút kẹt disabled).
   - `$QA env kill-activities on` → `app background --secs 5` → quay lại → `env kill-activities off` (Android giết
     activity khi nền: state tạm, popup đang mở, ads đang xem).
   - `$QA env trim-memory` (áp lực bộ nhớ) → chơi tiếp 1 phút → `logs` (LMK, texture mất, crash native).
   - `$QA env clock +1d` (emulator/root) → daily reward / timer / event hết hạn → `env clock reset`.
   - **Pseudo-loc** (probe ≥ 1.3): `$QA pseudoloc on` → đi qua mọi màn/popup của tính năng → `audit` → `$QA pseudoloc off`.
     Chữ giãn ~30% + dấu + `[…]`: tràn khung, cắt, xuống dòng xấu, chữ cứng không qua localize (không đổi) — bắt lỗi
     localize **không cần** đổi ngôn ngữ. Probe cũ → đổi ngôn ngữ chữ dài của game nếu có, đổi lại khi xong.
   - Gián đoạn (P2) nếu chưa làm ở STEP 3: bảng playbook §3.4.
3. Mỗi phát hiện: xác nhận bằng ảnh, tái hiện (bật lại điều kiện), rồi STEP 5. Lỗi chỉ xảy ra ở điều kiện môi
   trường → ghi điều kiện vào `--precondition` + tiêu đề (`… khi cỡ chữ hệ thống 130%`), mức theo rubric (thường medium).

Xong STEP 4 khi `env-matrix.md` có kết quả mọi bước (hoặc `unsupported`) và pseudo-loc đã qua mọi màn của tính năng
(hoặc ghi lý do bỏ).

## STEP 5 — Log bug

Khi một lỗi đã xác nhận (luật 2–3):

1. **Trùng?** So với danh sách bug mở (STEP 0d) và `autotest-summary.md` (0f): cùng màn + cùng triệu chứng → không
   log, ghi "trùng #N" / "autotest đã có" vào `note --case … --result fail`.
2. **Nhiễu?** Finding audit khớp luật trong `qa.noise` đã bị `suppressed`; finding mới mà bạn thấy không phải bug
   → thêm luật nhiễu (STEP 2.1), không log.
3. **Ảnh bằng chứng:** đưa game về đúng trạng thái lỗi rồi `$QA shot`, hoặc dùng ảnh cũ: `--shot <tên file trong
   shots/>`. Bug không thấy được trên màn (logic thuần) → ảnh màn liên quan nhất + số đo trong actual.
4. **Gửi:**
   ```bash
   $QA bug --title "Shop: mua gói vàng đầu tiên trừ kim cương 2 lần khi bấm nhanh" \
     --actual "Bấm Mua 2 lần liên tiếp: kim cương 500 → 300 (trừ 2×100), vàng chỉ +1.000 một lần" \
     --expected "Trừ 100 kim cương một lần, +1.000 vàng, lần bấm thứ hai bị chặn (csv CoinPack:2 price=100)" \
     --precondition "Đã qua tutorial, có 500 kim cương (cheat cộng kim cương)" \
     --type logic --severity high --rate 3/3 --evidence "ảnh #014 (trước) #015 (sau); state PlayerResource trước/sau" \
     --log-sig <sig nếu có> [--shot 015-tap.png] [--save] [--mention "<tên dev>"]
   ```
   - `--steps` mặc định = các bước `--as` từ `mark` gần nhất (xem trước bằng `$QA steps`). Bước thiếu ý → tự viết
     `--steps "1. …\n2. …"`.
   - `--type`: crash · logic · visual · ux · perf · gd. `--severity` theo rubric playbook §6 (crash/mất tiến trình/
     mất tiền thật = critical; chặn tính năng/sai kinh tế = high; sai hiển thị rõ/luồng phụ = medium; lỗi nhỏ = low).
   - Tool tự điền category + forum tag theo form BugHub, thiết bị, build, commit (nếu build bake), log app (+ đoạn
     log của `--log-sig`), extra `reporter=auto-test-device`. Có probe: thêm màn hiện tại, player id, context game,
     `--save` đính kèm save snapshot.
   - `--via ingame` (cần probe + Bug Logger trong build): gửi bằng chính Bug Logger trong game. Mặc định `http`.
   - Trước bug đầu tiên của phiên có thể `--dry-run` để xem report.
5. Output `BUG #N → url` = xong → `$QA note --case <charter>/<case> --result fail --bug N "…"`. `BugHub từ chối …` →
   sửa tham số, `$QA bug-resend <id>`; lỗi mạng / 5xx / 429 → `$QA retry -- bug-resend <id>` rồi chơi tiếp. `trùng bug
   đã log … (<lý do>)` → đúng là trùng thì bỏ; khác thật → thêm chi tiết vào tiêu đề hoặc `--force`.
6. `$QA mark "<ca tiếp theo>"` rồi chơi tiếp — một bug không chặn phần còn lại của charter (bug chặn hẳn tính năng
   → ca phụ thuộc `blocked`). **Một lỗi → tìm anh em của nó** (playbook §1) trước khi sang ca khác.

Mẫu tiêu đề, actual/expected tốt/xấu: playbook §7.

## STEP 6 — Kết thúc: coverage, report, ghi tri thức

Dừng khi `$QA charter diff` rỗng (hoặc còn lại chỉ `blocked`/`skip` có lý do), ma trận môi trường xong, và thêm **2
vòng thăm dò tự do** (playbook §3.7) không ra phát hiện mới — hoặc thiết bị/app hỏng không cứu được.

1. `$QA perf` lần cuối, `$QA logs --all` — lỗi log nào là bug chưa log → STEP 5.
2. `$QA coverage` — case planned/run/pass/fail/not-run theo charter; màn sống chưa visit (`liveness` − `observed`)
   → nếu thuộc tính năng này thì quay lại test, không thì ghi report "ngoài phạm vi".
3. **Cập nhật tri thức vào repo (luật 6)** — trước `stop`, bằng Edit:
   - `charters/<feature>.md`: ca mới nghĩ ra trong phiên, oracle sửa, ca bỏ vì không còn đúng (không ghi kết quả).
   - `.claude/docs/QaProfile.md`: cheat/đường bật cheat dùng được, màn chết phát hiện thêm, hướng hay hỏng của game
     (vd "nút nhận thưởng hay nhân đôi khi spam"), luật nhiễu mới + lý do, quy ước lệch đã sửa trong `qa.conventions`.
     Mẫu ở playbook §12. Giữ `Status: draft` nếu dev chưa duyệt.
4. `$QA stop` → trả cài đặt máy, gỡ thử lại lỗi API, **flush** `liveness.observed` + `verified.cheats/statePaths` vào
   `project-profile.json`, viết `report.md` (bug, kết quả ca, coverage, env matrix, selfcheck, suppressed,
   autotest-import). Output có gợi ý dòng cần thêm vào `QaProfile.md` nếu bạn chưa ghi.
5. Trả lời user (tiếng Việt), ngắn:
   - Bảng bug đã log: `#N` + tiêu đề + mức độ + link (hoặc "trùng #N" / "autotest đã có" / "gửi lỗi").
   - Coverage: số ca pass/fail/blocked/skip theo charter, ca fail chưa thành bug (vì sao), màn sống chưa visit.
   - Phát hiện không log (nghi vấn chưa tái hiện được, góp ý UX nhỏ, `expectation-failed` chỉ ra 1 lần) — một dòng mỗi cái.
   - File project đã đổi cần dev commit: `project-profile.json` (`qa`), `QaProfile.md`, `charters/*.md`,
     `EzgQaProbeConfig.json`/autotest settings (nếu `profile sync`), `Packages/manifest.json` (nếu `start` thêm probe).
     `Status: draft` → nhắc dev duyệt.
   - Đường dẫn `report.md`, thiết bị + build đã test, probe phiên bản + cổng mở bằng gì; máy chưa được thêm vào
     danh sách máy test thì kèm dòng `→ test_device_add …`; probe < 1.3 thì nhắc build lại để test đủ.

## Lệnh nhanh

Cột **Probe**: `—` không cần · `có` cần probe (≥ 1.2.1) · `≥1.3` cần probe 1.3.0 (cũ hơn → `unsupported`).

| Việc | Lệnh | Probe |
|---|---|---|
| Công cụ / project | `doctor [--strict]` (rc 1 khi thiếu mục bắt buộc) · `devices` · `bughub-check` · `selfcheck` | — / selfcheck: phần sâu `có` |
| Profile project | `profile init [--force]` · `profile sync` · `profile show` | — |
| Mở / đóng phiên | `start [device] --feature "…" [--attach] [--fresh] [--install x.apk] [--no-probe-install] [--no-api-retry] [--no-profile-init]` · `stop` (flush tri thức + viết report) · `report` (viết lại report giữa phiên) · `probe-connect [--retrigger] [--unlock]` | — |
| Lỗi → thử lại (luật 11) | `retry [--every 30] [--max-wait 540] -- <lệnh…>` (mã 75 = vẫn lỗi, gọi lại) · `api-retry on\|off\|status` | — |
| Autotest Editor | `autotest-import [<report.json>]` → `autotest-summary.md` | — |
| Charter / ca | `charter list\|new <feature>\|diff` · `mark "<ca>"` · `note --case <charter>/<case> --result pass\|fail\|blocked\|skip [--bug N] ["…"]` · `note "…"` · `steps` · `coverage [--charter <id>]` | — |
| Nhìn | `shot [--grid] [--marks]` · `burst N` · `wait S [--settle]` · `audit [--include-suppressed]` · `ui [--find x] [--marks]` | audit sâu/ui: `có`; flags mới: `≥1.3` |
| Chạm | `tap --text "…"` · `tap @id` · `tap --name` · `tap --world` · `tap X Y` · `tap 50% 80%` · `tap … --times 5 --interval 0.05` · `tap … --expect <text\|@id\|screen:<Màn>> [--timeout S]` · `longpress` | --text/@id/--name/--world/--expect: `có` |
| Vuốt/kéo | `swipe X1 Y1 X2 Y2` · `swipe --dir up` · `drag X1 Y1 X2 Y2 [--hold ms]` | — |
| Phím / chữ | `key back\|home\|enter\|app_switch` · `text "abc" [--submit]` · `text "Tiếng Việt" --probe` | --probe: `có` |
| App / build | `app restart\|stop [--hard] [--settle S]` · `app launch\|background --secs N\|clear\|info\|install x.apk` · `build update <apk\|aab\|path> [--keep-data (mặc định) \| --wipe]` → `migration.md` | migration snapshot: `có` |
| Quảng cáo / overlay (Android) | `ad-close [--timeout S] [--list]` · `overlays` | — |
| Môi trường | `net off\|on\|wifi-off\|data-off` · `rotate portrait\|landscape\|auto` · `aspect <WxH>\|tall\|short\|tablet\|reset` · `env font-scale 1.3\|reset` · `env dark on\|off` · `env animator 0\|0.5\|1` · `env kill-activities on\|off` · `env trim-memory [level]` · `env clock +1d\|reset` · `env locale <tag>` · `env matrix [--quick]` | — |
| Localize | `pseudoloc on\|off [--mode expand\|accent\|both]` | `≥1.3` |
| Log / hiệu năng / tracking | `logs [--all] [--tail N] [--grep re] [--ignore re]` · `perf` · `track [--since <mark>] [--expect <event>]` | — |
| Dữ liệu kỳ vọng | `csv <tên> [--where col=val] [--col c]` | — |
| Game | `state Type.Member… [--full\|--out f.json]` · `call Type.Method args [--full\|--out f.json]` · `types <từ khoá>` · `timescale 10` · `cheat on\|off\|status` | `có` |
| Bug | `bug --title … --actual … --expected … --type … --severity … [--log-sig] [--shot] [--save] [--dry-run] [--force]` · `bug-resend <id>` | — |
| Probe | `probe-install [--gate none\|Ns.Type.member]` | — |

Mọi lệnh thao tác nhận `--as "<mô tả>"`, `--wait S`, `--no-shot`, `--grid`, `--marks`, `--inject` (chạm qua probe),
`--allow-overlay` (cố ý chạm vào overlay app khác — mặc định tool chặn). Flag toàn cục `--no-auto-audit` tắt audit tự
động khi màn đổi cho lệnh đó. `call`/`state` cắt kết quả dài và ghi rõ `[đã cắt …]` — cần trọn (danh sách id, JSON
để parse) thì `--out <file>` rồi Read file đó.

## Phụ lục — nền tảng & giới hạn

| | Android (adb) | iOS Simulator | iPhone thật | Unity Editor |
|---|---|---|---|---|
| Chạm/vuốt/giữ | input hệ thống (thật) | `idb` nếu có, không thì probe | chỉ probe | chỉ probe |
| Chụp màn | screencap | simctl | probe | probe |
| Log | logcat (crash Java/native, ANR, LMK, exception Unity) | log stream + probe | `devicectl --console` + probe | probe |
| Back / mạng / xoay | có | không có Back; không mạng/xoay | không | không |
| `aspect` / `env` | có (`wm`, `settings`, `am`) | `unsupported` (đổi device simulator) | `unsupported` | `unsupported` |
| Nhập chữ Unicode | probe (`text --probe`) | idb/probe | probe | probe |

- **Probe** = package `com.ezg.qa-probe` (template EZG có sẵn; project cũ thì `start` tự thêm vào manifest): kích
  hoạt bằng token ngẫu nhiên mỗi phiên (intent extra / argument / file trigger), chỉ nghe localhost trên Android.
  Build release chỉ mở cổng khi: máy có trong danh sách máy test của `com.ezg.cheat-gate` (probe được kích hoạt là tự
  hỏi server một lần), hoặc cờ cheat của game đang true. Mở bằng cheat-gate thì probe bật luôn cheat của game. Probe
  đọc quy ước (lớp cơ sở màn, type/member cheat) từ `Assets/Resources/EzgQaProbeConfig.json` do `profile sync` sinh;
  thiếu file → tự dò theo quy ước template. Chạm qua probe đi qua EventSystem — UI chạy tốt; object 3D chỉ nhận nếu
  game dùng EventSystem/PhysicsRaycaster hoặc `OnMouse*` (Android dùng input thật nên không bị giới hạn này).
- Đa chạm (pinch zoom) không giả lập được bằng adb — test zoom bằng nút/cuộn nếu game có, ghi `skip` cho ca pinch.
- Overlay app khác (Android): `start` và `overlays` dò cửa sổ nổi của app bên thứ ba; chạm adb vào vùng đó bị chặn.
- `env clock`/`env locale` cần emulator hoặc máy root; máy thật thường → `unsupported`, ghi `skip`.
- Trạng thái phiên ở `<git-common-dir>/auto-test-device/` (ảnh, log, `cases.jsonl`, audit, `coverage.md`,
  `env-matrix.md`, `migration.md`, `report.md`) — không bao giờ commit. Tri thức đi theo repo nằm ở
  `project-profile.json["qa"]`, `QaProfile.md`, `charters/`.
- `qadevice.py` chỉ dùng thư viện chuẩn Python 3.8+ (Pillow tuỳ chọn); test: `python3 -m unittest discover -s .claude/skills/auto-test-device/scripts/tests`.
