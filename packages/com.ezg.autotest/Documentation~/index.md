# EZG Auto Test System — Hướng dẫn sử dụng

Tài liệu cho **QA** và **dev**: mỗi suite kiểm tra gì, trạng thái và điểm được tính ra sao, đọc báo cáo,
sandbox dữ liệu, toàn bộ cấu hình và quy trình QA đề xuất. Viết kịch bản riêng cho game: xem
[AI-SCENARIO-GUIDE.md](AI-SCENARIO-GUIDE.md). Chạy trên CI: xem [CI.md](CI.md).

## Mục lục

1. [Khái niệm](#1-khái-niệm)
2. [Chạy test trong Editor](#2-chạy-test-trong-editor)
3. [Severity — mức nghiêm trọng](#3-severity--mức-nghiêm-trọng)
4. [Trạng thái case được tính thế nào](#4-trạng-thái-case-được-tính-thế-nào)
5. [Điểm sức khoẻ](#5-điểm-sức-khoẻ)
6. [Chi tiết từng suite](#6-chi-tiết-từng-suite)
7. [Đọc báo cáo](#7-đọc-báo-cáo)
8. [Dừng lượt chạy](#8-dừng-lượt-chạy)
9. [Sandbox dữ liệu người chơi](#9-sandbox-dữ-liệu-người-chơi)
10. [Tham chiếu Settings](#10-tham-chiếu-settings)
11. [Quy trình QA đề xuất](#11-quy-trình-qa-đề-xuất)
12. [Mở rộng hệ thống](#12-mở-rộng-hệ-thống)

---

## 1. Khái niệm

| Khái niệm | Ý nghĩa |
|-----------|---------|
| **Suite** | Một nhóm test hiện thành một mục trong cửa sổ (Kiểm tra tĩnh, Smoke…). Có `id` ổn định dùng trong CLI và Settings (`static`, `smoke`, `ui-audit`…). |
| **Case** | Một đơn vị chạy trong suite — vd mỗi màn hình là một case của Smoke, mỗi luật là một case của Kiểm tra tĩnh, mỗi kịch bản là một case của Kịch bản riêng. Case có timeout riêng. |
| **Bước (step)** | Các bước bên trong một case ("Mở màn Shop", "Bấm nút Mua"). Report hiện case dừng ở bước nào; danh sách bước đã đi qua được dùng làm **các bước tái hiện** của mỗi lỗi. |
| **Issue** | Một lỗi / phát hiện cụ thể — đơn vị QA log bug. Có severity, nhóm (`category`), tiêu đề, mô tả, vị trí (asset / file:dòng), đường dẫn object, expected / actual, các bước tái hiện, ảnh chụp, stack trace và một **fingerprint** ổn định giữa các lượt chạy. |
| **Số đo (metric)** | Giá trị có đơn vị + ngưỡng (ms, FPS, MB…). Vượt ngưỡng ⇒ sinh issue. |
| **Adapter** | Cầu nối giữa hệ thống test và code game: biết game đã boot chưa, danh sách màn hình, mở/đóng màn, đọc/cộng/trừ tiền, key save, bảng config. Package có sẵn `EzgTemplateAdapter` (template EZG) và `GenericGameAdapter` (chỉ biết scene + EventSystem). Adapter báo **capabilities**; suite tự bỏ qua case cần capability mà adapter không có (kèm lý do). |
| **Project hooks** | Điểm móc tuỳ chọn của từng project: bỏ tutorial khi bắt đầu, data mẫu khi mở màn cần data, đóng popup chặn, loại thêm feature. |
| **Phiên (session)** | Một lần vào Play. Mặc định mỗi suite Play mode chạy trong một phiên Play riêng để state không rò giữa các suite. |
| **Lượt chạy (run)** | Một lần bấm Chạy / một lần gọi CLI. Mỗi lượt có `runId` và một thư mục report riêng. |

---

## 2. Chạy test trong Editor

1. **Ezg > Auto test system** (`Ctrl+Shift+T` / `Cmd+Shift+T`).
2. Chọn suite (và case bên trong nếu muốn chạy lẻ). Case bị tắt trong Settings hiện nhưng không chạy.
3. **Chạy mục đã chọn** (hoặc **Chạy tất cả** — mọi suite đang bật trừ Device / E2E, hoặc **Chạy riêng** một suite / case). Suite Edit mode (`static`) chạy ngay trong Editor; suite Play mode tự vào Play từ scene boot
   (`general.bootScenePath`, rỗng = scene đầu tiên trong Build Settings), chờ game boot xong, chạy các case,
   rồi thoát Play. Suite `device` build + cài + chạy trên máy thật.
4. Cửa sổ hiện tiến độ: case đang chạy + bước hiện tại. Không thao tác vào Game view trong lúc chạy (driver
   bấm như ngón tay thật, thao tác tay chen vào làm case sai lệch).
5. Xong lượt: report mở tự động (`general.openReportWhenFinished`). Mở lại report cũ từ thư mục
   `AutoTestReports/`.

Trước mỗi phiên Play, runner **chờ game sẵn sàng** (`GameFlow.EnsureReady`): hooks `IsGameReady()` hoặc
adapter báo ready liên tục ≥ 1 giây (tối đa `general.bootTimeoutSeconds`), chờ thêm
`general.settleSecondsAfterBoot` cho popup đầu game hiện hết, gọi hooks `OnSessionStarted`, đóng các popup
khớp `smoke.dismissAtStartFeatures`, rồi ghi lại **màn nền** (các màn đang mở sau boot, vd HUD / thanh tiền)
— màn nền không bao giờ bị đóng; giữa các case game được đưa về màn nền.

Game không sẵn sàng sau `bootTimeoutSeconds` ⇒ case đầu tiên Lỗi với issue **Blocker** kèm mô tả trạng thái
(scene hiện tại, UIManager có chưa, cờ loading, màn đang mở) để dev biết boot kẹt ở đâu.

---

## 3. Severity — mức nghiêm trọng

Map thẳng sang thang severity của QA. Cùng thang dùng cho mọi suite, kịch bản riêng và bug tracker.

| Severity | Ý nghĩa | Ví dụ | Xử lý |
|----------|---------|-------|-------|
| **Blocker** | Không chơi / không test tiếp được. | Game không boot; scene chính không load; mất save; crash khi mở app. | Chặn release. Sửa ngay. |
| **Critical** | Tính năng chính hỏng, mất tiền/tài nguyên, exception. | Exception khi mở màn; trừ tiền mà không nhận hàng; số dư âm; mua được khi không đủ tiền; secret hardcode. | Chặn release. |
| **Major** | Tính năng sai nhưng có đường vòng / ảnh hưởng rõ tới trải nghiệm. | Màn không mở được từ nút; error log; nút không bấm được vì bị che; vượt ngưỡng hiệu năng; timeout. | Sửa trước release. Mặc định ≥ Major ⇒ case **Lỗi**. |
| **Minor** | Lỗi nhỏ, hiển thị, không chặn luồng. | Text tràn khung; vùng chạm hơi nhỏ; màn mở chậm; lộ key localize; luật code (DateTime.Now). | Lên lịch sửa. Case **Cảnh báo**. |
| **Info** | Thông tin, không phải lỗi. | Số liệu tham khảo; case bị giới hạn khả năng adapter; gợi ý tối ưu. | Không tính điểm, không đổi trạng thái. |

Ngưỡng "thành Lỗi" chỉnh bằng `general.failSeverity` (mặc định Major).

---

## 4. Trạng thái case được tính thế nào

| Trạng thái | Nhãn | Khi nào |
|------------|------|---------|
| **Error** | Crash | Code test ném exception **không phải** assertion (NullReference, InvalidOperation…) — case sập giữa chừng. Tự sinh issue **Critical** nhóm `Exception` kèm stack trace. Thường là lỗi của game (exception trong code game được gọi) hoặc của kịch bản test. |
| **Failed** | Lỗi | (a) Assertion thất bại (`ctx.Assert`, `AreEqual`, `IsNotNull`, `Fail`, `WaitUntil` hết giờ, game không boot) — case dừng ngay, issue nhóm `Assert` với severity chỉ định; **luôn là Lỗi bất kể severity**. (b) Quá timeout của case — issue Major nhóm `Timeout`, ghi bước đang chạy. (c) Case chạy hết nhưng có issue severity ≥ `failSeverity`. |
| **Warning** | Cảnh báo | Case chạy hết, có issue ≥ Minor nhưng < `failSeverity`. |
| **Passed** | Đạt | Case chạy hết, không có issue hoặc chỉ có issue Info. |
| **Skipped** | Bỏ qua | Case tự bỏ qua có lý do (`ctx.Skip`: adapter thiếu capability, thiếu điều kiện, cần Play mode…), hoặc case bị tắt trong Settings. Không tính là lỗi. |
| **Cancelled** | Đã dừng | Người dùng bấm Dừng khi case đang chạy, hoặc case chưa kịp chạy khi lượt bị dừng. |

**Log Unity trong lúc case chạy** được gắn vào case và quy thành issue theo policy:

| Log | `FailCase` (mặc định) | `WarnCase` | `Ignore` |
|-----|----------------------|------------|----------|
| Exception (`general.exceptionPolicy`) | issue **Critical** | issue Minor | chỉ đính kèm log |
| Error / Assert (`general.errorLogPolicy`) | issue **Major** | issue Minor | chỉ đính kèm log |

- Log khớp `general.ignoredLogPatterns` vẫn hiện trong log của case (tiền tố "(bỏ qua)") nhưng không sinh issue.
- Cùng một log lặp lại chỉ sinh một issue, ghi "(lặp N lần)".
- Case có thể giới hạn severity của log tự bắt (`ctx.LogSeverityCap`) — vd case load scene ngoài luồng game.
- Warning chỉ được đính kèm khi `general.captureWarnings = true`, không bao giờ sinh issue.

**Trạng thái suite** = trạng thái nặng nhất của các case: Crash > Lỗi > Đã dừng > Cảnh báo > Đạt > Bỏ qua.

---

## 5. Điểm sức khoẻ

Con số 0–100 một dòng cho dashboard / tin nhắn CI:

```
Tỉ lệ case = (Đạt + 0.7 × Cảnh báo) / (Đạt + Cảnh báo + Lỗi + Crash) × 100
Phạt       = min(40, 15 × issue Blocker + 4 × issue Critical)
Điểm       = max(0, Tỉ lệ case − Phạt)
```

Case Bỏ qua / Đã dừng không tính. Số lượng lỗi Major/Minor không trừ trực tiếp (để điểm không bão hoà về 0
khi project có hàng trăm lỗi nhỏ) — xem bảng đếm severity bên cạnh để biết khối lượng.

Gợi ý đọc: **≥ 90** tốt · **70–89** cần xem các Major · **< 70** có lỗi nặng, không nên phát hành.
Điểm chỉ so sánh được giữa các lượt chạy **cùng bộ suite**.

---

## 6. Chi tiết từng suite

### 6.1 Kiểm tra tĩnh (`static`) — Edit mode

Quét asset, cấu hình và code mà **không vào Play**, không sửa/lưu asset nào. Phạm vi: `staticCheck.includeFolders`
(mặc định `Assets/_Project`) trừ `staticCheck.excludeFolders` (Plugins, ThirdParty, Samples…). Mỗi luật là
một case:

| Nhóm | Kiểm tra | Cấu hình liên quan |
|------|----------|-------------------|
| Asset | Missing Script trong prefab và scene (script đã xoá, đổi GUID, class lỗi compile). | — |
| Asset | Missing reference (field trỏ tới object đã mất). | — |
| Asset | Material hỏng: shader lỗi / không tồn tại / không hợp render pipeline (hiện magenta). | — |
| Asset | Texture quá lớn (cạnh > `maxTextureSize`) hoặc tốn bộ nhớ (> `maxTextureMemoryMb`). | `maxTextureSize`, `maxTextureMemoryMb` |
| Asset | File audio quá nặng; asset bất kỳ quá lớn. | `maxAudioFileMb`, `maxAssetFileMb` |
| Asset | GUID trùng giữa các file `.meta`. | — |
| Dữ liệu | CSV config: dòng lệch số cột, id trùng. | `csvFolders` (rỗng = mọi thư mục `CsvConfig`) |
| Dữ liệu | Localize: key thiếu ở một ngôn ngữ, placeholder (`{0}`…) lệch giữa các ngôn ngữ, text rỗng. | `localizationFolder` |
| Cấu hình | Player Settings cho release: Android target API ≥ `androidTargetApiMin`, min API ≥ `androidMinApiMin`, iOS min ≥ `iosMinVersion`, bundle id, define cấm trong release (`forbiddenReleaseDefines`). | các field tương ứng |
| Cấu hình | Feature thiếu prefab màn hình (adapter biết quy ước tên, vd template EZG `screen_<snake_case>`). | — |
| Code | Luật regex trên `.cs`: `DateTime.Now`, `async void`, `GameObject.Find`, secret hardcode… | `codeRules` |

Mỗi issue có `location` = asset path (bấm được trong report) và `objectPath` = đường dẫn hierarchy trong
prefab/scene. Luật mới: implement `IStaticRule` (xem § 12).

### 6.2 Smoke PlayMode (`smoke`)

Câu hỏi: *game có mở lên và đi qua được mọi màn hình không lỗi?*

- **Boot**: thời gian từ lúc vào Play tới khi game sẵn sàng; mọi error/exception log phát sinh trong lúc boot
  (kể cả trước khi case bắt đầu) được gắn vào case boot.
- **Mỗi màn hình một case** (danh sách từ adapter, trừ `smoke.excludedFeatures` + hooks `ExtraExcludedFeatures`):
  mở màn (data lấy từ hooks `GetFeatureData`), chờ tối đa `screenOpenTimeoutSeconds`, giữ
  `screenSettleSeconds` cho animation/logic chạy, đóng lại (`closeScreenAfterOpen`) và kiểm tra màn đã đóng.
  Mở không được ⇒ Lỗi; mở chậm hơn `slowOpenMs` ⇒ Minor; error/exception log trong lúc mở/đóng ⇒ theo policy log.
- **Mỗi scene trong Build Settings** (`loadEveryBuildScene`): load scene, chờ `sceneSettleSeconds`, bắt lỗi log.
- **Ngân sách lỗi**: tổng error/exception của cả phiên.

Cần adapter có capability `Features` để mở màn; adapter chung chỉ chạy được phần boot + scene.

### 6.3 UI Audit (`ui-audit`)

Mở từng màn (như smoke) rồi soi cây UI đang hiển thị. Object khớp `uiAudit.ignoredObjectPatterns` bị bỏ qua.

| Kiểm tra | Bật/tắt | Lỗi khi |
|----------|---------|---------|
| Vùng chạm | luôn bật | Nút nhỏ hơn `minTouchTargetDp` (tính theo DPI màn hình). |
| Text tràn | `checkTextOverflow` | Text / TMP bị cắt hoặc tràn khỏi khung. |
| Sprite thiếu | `checkMissingSprite` | Image đang hiện nhưng không có sprite (ô trắng) hoặc sprite mất reference. |
| Ngoài màn hình | `checkOffScreen` | Phần tử tương tác nằm (một phần) ngoài màn hình. |
| Safe area | `checkSafeArea` | Nút / text quan trọng lấn vào vùng tai thỏ / thanh home. |
| Raycast bị chặn | `checkRaycastBlocked` | Nút hiển thị nhưng raycast tại tâm trúng object khác (bị che). |
| Lộ key localize | `checkLocalizationLeak` | Text hiển thị khớp `localizationKeyPattern` (vd `txt_buy_now`). |
| Nút chết | `checkDeadButtons` | Button không có listener nào (bấm không làm gì). |

Mỗi issue kèm `objectPath` và (nếu bật) ảnh chụp màn đang audit.

### 6.4 Button Sweep (`button-sweep`)

Mở từng màn, lần lượt bấm từng nút đang bấm được (tối đa `maxButtonsPerScreen`), **trừ** nút có tên / text khớp
`buttonSweep.blacklistPatterns` (mua thật, restore, xoá data, logout, mở link / fanpage / email, rating, cheat…).
Sau mỗi lần bấm chờ `waitAfterClickSeconds`, bắt exception / error log, rồi đóng các màn mới mở ra
(`closeSpawnedScreens`) để quay về màn đang test. Issue ghi rõ nút nào (đường dẫn hierarchy) gây lỗi, kèm
các bước "Mở màn X → Bấm nút Y".

### 6.5 Monkey / Stress (`monkey`)

Thao tác ngẫu nhiên trong `durationSeconds` giây với tốc độ `actionsPerSecond`: `buttonTapRatio` phần là bấm
vào nút bấm được, phần còn lại là tap / kéo ngẫu nhiên lên màn hình; thỉnh thoảng bấm Back (`allowBackKey`).
`respectBlacklist` = không bấm nút trong blacklist của Button Sweep. Bắt exception, error log, frame treo.
**Seed** luôn được ghi vào report: đặt `monkey.seed` bằng seed đó để chạy lại đúng chuỗi thao tác khi tái hiện
lỗi (seed 0 = ngẫu nhiên theo thời gian).

### 6.6 Logic / Economy (`economy`)

Cần capability `Economy` (và các capability con tương ứng). Tiền tệ lấy từ adapter, bỏ loại giả
(`adapter.pseudoCurrencies`) và `economy.skipCurrencies`.

| Case | Bật/tắt | Kiểm tra |
|------|---------|----------|
| Cộng / trừ khứ hồi | luôn bật | Cộng `testAmount` rồi trừ lại ⇒ số dư khớp từng bước; `IsEnough` đúng. |
| Trừ quá số dư | luôn bật | Game phải từ chối, số dư không đổi, không âm (Critical nếu sai). |
| Tràn số | `testOverflow` | Cộng tới gần giới hạn của kiểu số lưu số dư (int/long/double) — không quay vòng âm. |
| Save → load | `testSaveLoad` | Lưu, đọc lại dữ liệu người chơi từ storage ⇒ số dư giữ nguyên (cần `PlayerData`). |
| Phát thưởng | `testRewards` | Phát thưởng qua hệ thống reward của game (không popup) ⇒ số dư tăng đúng. Loại bỏ tiền tệ trong `rewardSkipCurrencies` (game định tuyến thưởng sang ví khác). Cần `Rewards`. |
| Mua bằng tiền mềm | `testPurchaseOffline` | Đủ tiền ⇒ mua thành công, trừ đúng; thiếu tiền ⇒ thất bại, không trừ. Cần `PurchaseOffline`. |
| Config sanity | `testConfigSanity` | Mọi bảng config đã load: field số khớp `nonNegativeFieldPattern` (price, cost, reward…) không âm; field khớp `idFieldPattern` không trùng trong bảng; bảng load lỗi / rỗng. Cần `Config`. |

Số dư gốc luôn được khôi phục sau mỗi case; sandbox khôi phục thêm PlayerPrefs sau suite.

### 6.7 Performance (`performance`)

Ba case:

1. **Đứng yên ở màn chính** (`idle`) — sau boot, đóng hết popup, chờ `warmupSeconds` rồi lấy mẫu `sampleSeconds`:

| Số đo | Ngưỡng |
|-------|--------|
| FPS trung bình | ≥ 90% FPS mục tiêu (`Application.targetFrameRate` của game nếu có, không thì `targetFps`) |
| Frame time P95 / P99 | `maxP95FrameMs` / `maxP99FrameMs` (tự nới theo khung thời gian của FPS mục tiêu) |
| Số lần giật (frame > `hitchThresholdMs`) | `maxHitches` |
| GC alloc mỗi frame | `maxGcAllocPerFrameKb` |
| Bộ nhớ đã cấp | `maxTotalMemoryMb` |
| Draw call / SetPass call | `maxDrawCalls` / `maxSetPassCalls` |

   Kèm `frame-times.csv` (frame time từng frame) để vẽ biểu đồ.
2. **Chi phí mở từng màn hình** (`screen-cost`) — thời gian mở, GC alloc và frame dài nhất trong 1,5 s sau khi
   mở; top 5 màn chậm / tốn GC nhất thành số đo, đủ bảng ở `screen-cost.csv`.
3. **Rò bộ nhớ khi mở/đóng lặp** (`leak-check`) — mỗi màn mở/đóng 3 lần (sau một lượt làm nóng cache), so số
   GameObject + bộ nhớ trước/sau (đã GC + `UnloadUnusedAssets`); tăng bất thường ⇒ issue "Nghi rò".

Exception khi mở màn trong suite này chỉ tính Minor (Smoke đã báo mức nặng).

**Trong Editor số đo chỉ dùng để so tương đối** giữa các lượt trên cùng máy (bắt regression). Đánh giá tuyệt
đối phải chạy trên device (`device` + `performance`).

### 6.8 Visual Regression (`visual`)

Mở từng màn, ẩn các object khớp `visual.maskObjectPatterns` (đồng hồ, countdown, FPS… — nội dung đổi liên tục),
chụp và so với ảnh baseline trong `visual.baselineFolder` (mặc định `AutoTestBaselines/`, **commit vào git**).
Pixel lệch mỗi kênh ≤ `pixelTolerance` coi như giống (khử răng cưa, nén); % pixel khác > `maxDiffPercent` ⇒
issue kèm 3 ảnh: baseline, hiện tại, diff (tô đỏ vùng khác). Màn chưa có baseline ⇒ case ghi Info "chưa có
baseline" và lưu ảnh hiện tại làm ứng viên.

Chỉ so **vùng so sánh**, để gameplay chạy phía sau popup không gây lỗi giả:

- **Vùng UI của chính màn đang test**: hợp các hình chữ nhật Graphic đang hiển thị của màn đó. Không tính
  Graphic phủ từ nửa màn trở lên (lớp nền mờ, nền toàn màn làm lộ gameplay phía sau). HUD, overlay debug và
  gameplay nằm ngoài vùng này.
- **Bỏ pixel động**: chụp 2 khung cách nhau 0,4 giây, pixel đổi màu giữa 2 khung (animation, particle, số
  nhảy) không tính.

Ảnh diff tô xanh tối phần bị bỏ qua. Report có thêm số đo "Vùng so sánh" và "Pixel động bỏ qua" (% ảnh).
Không đụng camera hay object nào của game.

Baseline phụ thuộc độ phân giải Game view và GPU: luôn chạy cùng độ phân giải (vd 1080×1920) và cùng loại máy.

**Cập nhật baseline**: chạy suite → mở report, xem ảnh diff từng màn → nếu thay đổi là **cố ý** (UI mới) bấm
**Chấp nhận ảnh hiện tại làm baseline** (trong cửa sổ Auto Test, ở từng case của suite Visual) → commit thư mục baseline cùng commit đổi UI. Nếu thay đổi
**không** cố ý ⇒ đó là bug, log bằng nút Sao chép bug.

### 6.9 Device / E2E (`device`)

Editor điều phối máy Android thật qua adb:

1. Build APK test (`buildBeforeRun`, vào `buildOutputFolder`) với define `EZG_AUTOTEST` — chỉ thêm cho đúng
   build này qua `extraScriptingDefines`, không đổi Player Settings; hoặc dùng APK có sẵn (`apkPath`).
2. Tìm adb (`adbPath` hoặc SDK của Unity / PATH), cài (gỡ trước nếu `uninstallBeforeInstall`), khởi động
   (`packageName` / `launchActivity`, rỗng = tự resolve).
3. Trên máy, runner chạy các suite trong `device.suites` (chỉ suite runtime Play mode; kịch bản riêng có
   `RunOnDevice = false` bị bỏ qua), tối đa `runTimeoutSeconds`.
4. Kéo report + ảnh về, gộp vào report của lượt; kèm logcat (`collectLogcat`) và thời gian cold start
   (`measureColdStart`).

Mỗi case chạy trên device ghi tên máy trong cột `device`. Cần máy bật USB debugging và đã tin cậy máy tính.

APK test còn chạy được trên **Firebase Test Lab** (chế độ Game Loop): case `firebase-test-lab` của suite sinh sẵn
lệnh `gcloud` (file đính kèm `ftl-command.txt`). Chi tiết: [CI.md § 7](CI.md#7-firebase-test-lab-game-loop).

### 6.10 Kịch bản riêng (`scenarios`)

Kịch bản gameplay đặc thù của từng game, viết trong `Assets/_Project/AutoTests/` (class kế thừa
`AutoTestScenario` có attribute `[AutoTestScenario]`), tự xuất hiện — không cần đăng ký. Mỗi kịch bản là một
case, nhóm theo `Category`. Kịch bản đánh `Disabled = true` hiện nhưng bị bỏ qua.

Tạo mới: **Assets > Create > Ezg > Auto Test Scenario**. Cách viết (cho dev và AI):
[AI-SCENARIO-GUIDE.md](AI-SCENARIO-GUIDE.md).

---

## 7. Đọc báo cáo

Mỗi lượt chạy ghi vào `<project>/<general.reportFolder>/<runId>/` (mặc định `AutoTestReports/`, chỉ giữ
`general.keepReports` lượt gần nhất — nên đưa `AutoTestReports/` vào `.gitignore`):

| File | Dùng cho |
|------|----------|
| `report.html` | Báo cáo đọc bằng trình duyệt (tự chứa, gửi được qua chat). |
| `report.json` | Toàn bộ dữ liệu máy đọc được (dashboard, AI agent, script). |
| `junit.xml` | CI hiển thị test (GitLab `artifacts:reports:junit`). |
| `issues.csv` | Mỗi issue một dòng — import vào bảng tính / bug tracker. |
| `summary.md` | Tóm tắt ngắn (điểm, số case theo trạng thái, top lỗi) — dán vào chat / comment MR. |
| `screenshots/` | Ảnh chụp theo case (`<suite>_<case>_<số>_<nhãn>.png`, thu nhỏ theo `screenshotScale`). |
| `attachments/` | File đính kèm (log, json, ảnh diff…). |

### report.html

- **Đầu trang**: điểm sức khoẻ, số case theo trạng thái, số issue theo severity, thời lượng, môi trường
  (product, bundle id, version/build, Unity, platform, render pipeline, git branch/commit/dirty, máy, device,
  adapter, version package).
- **So với lượt trước** (cùng bộ suite): số issue **mới**, đã **sửa**, còn **tồn**; case mới bắt đầu lỗi / đã hết
  lỗi. Issue mới có nhãn **MỚI**. So sánh dựa trên fingerprint của issue (suite + case + nhóm + tiêu đề + vị trí
  + object, bỏ các con số thay đổi giữa lượt) nên ổn định giữa các máy.
- **Danh sách suite → case**: trạng thái, thời lượng, thông điệp; mở case để xem các bước (bước hỏng tô đỏ),
  issue, số đo so với ngưỡng, log Unity (lọc theo loại), ảnh chụp và file đính kèm.
- **Mỗi issue**: severity, nhóm, tiêu đề, mô tả, vị trí, object, expected / actual, **các bước tái hiện**, ảnh,
  stack trace. Nút **Sao chép bug** copy sẵn một bug report hoàn chỉnh (tiêu đề + severity, môi trường,
  các bước tái hiện, expected / actual, đường dẫn ảnh) để dán vào bug tracker.

### issues.csv

Mỗi dòng một issue với các cột chính: fingerprint, severity, nhóm, tiêu đề, mô tả, vị trí, object, expected,
actual, các bước tái hiện, ảnh, suite, case, cờ mới. Lọc cột "mới" để chỉ log bug chưa từng thấy; dùng
fingerprint làm khoá để không log trùng.

### Trạng thái trong report.json

`status` theo enum `TestStatus` và `severity` theo enum `Severity` (xem § 3–4). Trường `summary` chứa các con số
tổng + `healthScore`; `diff` chứa kết quả so sánh với lượt trước.

---

## 8. Dừng lượt chạy

- Nút **Dừng** trong cửa sổ huỷ lượt chạy ngay: case đang chạy nhận tín hiệu huỷ, `TearDown` vẫn được gọi
  để dọn state, case chuyển **Đã dừng**, các case chưa chạy cũng ghi **Đã dừng**. Report vẫn được ghi (đánh dấu
  `cancelled`), sandbox vẫn khôi phục dữ liệu, Editor tự thoát Play.
- Mỗi case có timeout (`general.caseTimeoutSeconds` hoặc timeout riêng của case / kịch bản): quá hạn ⇒ case
  Lỗi, lượt chạy tiếp case sau.
- Trên CLI, quá thời gian tổng hoặc runner lỗi ⇒ exit code `3`.
- Thoát Play tay giữa chừng tương đương Dừng.

---

## 9. Sandbox dữ liệu người chơi

Suite có sửa dữ liệu người chơi (mọi suite Play mode ngoài `static`) chạy trong sandbox khi
`general.sandboxPlayerData = true`:

1. **Trước suite** (ở Edit mode, trước khi vào Play): runner hỏi adapter danh sách key PlayerPrefs của save —
   `EzgTemplateAdapter` tự tính từ mọi module `DataPlayerBase` (key = tên type đầy đủ) + key gộp `ALL_DATA` —
   cộng `general.extraSaveKeys`, rồi sao lưu giá trị các key đó (bản sao được ghi ra đĩa).
2. **Sau suite** (kể cả khi Lỗi / bị Dừng): khôi phục đúng giá trị cũ, xoá key test tạo mới.
3. **Crash-safe**: Editor crash hoặc bị kill giữa chừng ⇒ lần mở Editor sau hệ thống phát hiện bản sao lưu chưa
   khôi phục và tự khôi phục (có log `[EZG AutoTest]`).

Giới hạn: chỉ bảo vệ **PlayerPrefs**. Save ghi file riêng / cloud / server không được sandbox — thêm key vào
`extraSaveKeys` nếu là PlayerPrefs, hoặc tự sao lưu trong project hooks; đừng chạy test trên máy chứa save
quan trọng mà sandbox không che được. Tắt sandbox chỉ khi cố ý muốn giữ lại state test.

---

## 10. Tham chiếu Settings

Project Settings > **Ezg > Auto Test**, lưu ở `ProjectSettings/EZGAutoTestSettings.json` (commit vào git).
Build test trên device được bake cùng cấu hình này. Danh sách "regex" không phân biệt hoa thường, khớp một phần
(dùng `^…$` để khớp nguyên tên); regex sai cú pháp được coi là chuỗi con thường.

### Chung (`general`)

| Field | Mặc định | Ý nghĩa |
|-------|----------|---------|
| `reportFolder` | `AutoTestReports` | Thư mục report, tương đối với thư mục project. |
| `keepReports` | `30` | Số lượt report giữ lại; cũ hơn bị xoá. |
| `openReportWhenFinished` | `true` | Tự mở `report.html` khi xong (Editor). |
| `caseTimeoutSeconds` | `120` | Timeout mặc định mỗi case (giây). Case / kịch bản có timeout riêng thì dùng của nó. |
| `failSeverity` | `Major` | Issue từ mức này trở lên ⇒ case Lỗi; thấp hơn ⇒ Cảnh báo. |
| `exceptionPolicy` | `FailCase` | Cách xử lý Exception log trong lúc case chạy (`FailCase` / `WarnCase` / `Ignore`, xem § 4). |
| `errorLogPolicy` | `FailCase` | Cách xử lý Error / Assert log. |
| `captureWarnings` | `true` | Đính kèm Warning log vào case (không sinh issue). |
| `ignoredLogPatterns` | Firebase chưa khởi tạo, AppsFlyer, Ads không hỗ trợ trong Editor, MaxSdk, "referenced script (Unknown) … missing" | Regex log nhiễu đã biết — không sinh issue. |
| `isolateSuitesInPlaySessions` | `true` | Mỗi suite Play mode chạy trong một phiên Play riêng (cô lập state). Tắt để chạy nhanh hơn (chung một phiên). |
| `bootScenePath` | `""` | Scene khởi động khi vào Play; rỗng = scene đầu tiên trong Build Settings. Cũng dùng để nhận biết "đang ở scene boot". |
| `bootTimeoutSeconds` | `90` | Thời gian tối đa chờ game boot xong. |
| `settleSecondsAfterBoot` | `3` | Chờ thêm sau khi game báo sẵn sàng để popup đầu game hiện hết. |
| `sandboxPlayerData` | `true` | Sao lưu + khôi phục PlayerPrefs quanh suite có sửa dữ liệu (§ 9). |
| `autoUnpause` | `true` | Tự bỏ Pause khi Console bật "Error Pause" (nếu không test sẽ treo). |
| `captureScreenshots` | `true` | Cho phép chụp màn hình (tắt để report nhẹ / chạy nhanh). |
| `extraSaveKeys` | `[]` | Key PlayerPrefs sao lưu thêm ngoài key adapter tự biết. |
| `screenshotScale` | `0.5` | Tỉ lệ thu nhỏ ảnh chụp (1 = gốc). |

### Adapter (`adapter`)

Tên type để `EzgTemplateAdapter` phản chiếu — chỉ đổi khi project đặt tên khác template. Tên dạng ngắn
(`UIManager`), đầy đủ (`Ns.UIManager`) hoặc lồng (`GameEnums+Features`).

| Field | Mặc định | Ý nghĩa |
|-------|----------|---------|
| `adapterType` | `""` | Ép dùng adapter theo tên type (đầy đủ hoặc ngắn). Rỗng = tự chọn: adapter Priority cao nhất khởi tạo thành công (adapter của project → EZG template → Generic). |
| `uiManagerType` | `UIManager` | Class có `Show(enum…)`, `CloseFeature`, `GetFeatureObject`… |
| `featuresEnumType` | `GameEnums+Features` | Enum màn hình (thực tế lấy theo kiểu tham số của `Show`; lệch thì ghi chú). |
| `playerDataManagerType` | `PlayerDataManager` | Facade dữ liệu người chơi (tìm module PlayerResource). |
| `playerResourceType` | `PlayerResource` | Class có `IsEnough / AddCurrency / RemoveCurrency / GetCurrencyValue / SetCurrency`. |
| `moneyTypesEnumType` | `EnumBase+MoneyTypes` | Enum tiền tệ. |
| `dataManagerType` | `DataManager` | Facade config — mọi property static trả ScriptableObject được coi là bảng config. |
| `rewardsServiceType` | `RewardsService` | Service phát thưởng (`ReceiveReward`). |
| `purchaseManagerType` | `PurchaseManager` | Service mua (`PurchaseOffline`). |
| `timeManagerType` | `TimeManager` | Service thời gian của game. |
| `pseudoCurrencies` | `None, Ads, Cash, Free, Iap, IAP` | Giá trị enum tiền tệ giả (không phải số dư thật) — bỏ qua khi test economy. |

### Kiểm tra tĩnh (`staticCheck`)

| Field | Mặc định | Ý nghĩa |
|-------|----------|---------|
| `includeFolders` | `Assets/_Project` | Thư mục quét (rỗng = cả `Assets`). |
| `excludeFolders` | `Assets/Plugins`, `Assets/ThirdParty`, `Assets/3rdParty`, `Assets/_Project/3rdParty`, `Assets/Samples`, `Assets/TextMesh Pro/Examples & Extras` | Thư mục bỏ qua. |
| `maxTextureSize` | `2048` | Cạnh texture tối đa (px). |
| `maxTextureMemoryMb` | `16` | Bộ nhớ tối đa một texture (MB). |
| `maxAudioFileMb` | `5` | Dung lượng file audio tối đa (MB). |
| `maxAssetFileMb` | `50` | Dung lượng một asset bất kỳ tối đa (MB). |
| `androidTargetApiMin` | `35` | Target API Android tối thiểu. |
| `androidMinApiMin` | `24` | Min API Android tối thiểu. |
| `iosMinVersion` | `13.0` | Phiên bản iOS tối thiểu. |
| `forbiddenReleaseDefines` | `EZG_AUTOTEST, ENABLE_CHEAT, CHEAT, DEBUG_MENU, ENABLE_LOG_ALL` | Scripting define không được bật trong Player Settings (build release). |
| `codeRules` | 4 luật (xem dưới) | Luật regex trên file `.cs`. |
| `csvFolders` | `[]` | Thư mục CSV config (rỗng = tự tìm mọi thư mục tên `CsvConfig`). |
| `localizationFolder` | `""` | Thư mục dữ liệu localize chứa `<lang>/<tab>.csv` (rỗng = tự dò). |

Mỗi `codeRules` có: `id`, `pattern` (regex tìm), `excludePattern` (regex trên đường dẫn/dòng để bỏ qua),
`message`, `severity`, `enabled`. Mặc định:

| id | Tìm | Severity |
|----|-----|----------|
| `no-datetime-now` | `DateTime.Now / UtcNow` (trừ file TimeManager, thư mục Editor) | Minor |
| `no-async-void` | `async void` không phải handler `On…` | Minor |
| `no-find-in-update` | `GameObject.Find(` | Info |
| `no-hardcoded-secret` | `api_key / secret / password / token = "…16+ ký tự…"` | Critical |

### Smoke (`smoke`) — dùng chung cho UI Audit, Button Sweep, Visual

| Field | Mặc định | Ý nghĩa |
|-------|----------|---------|
| `excludedFeatures` | `^none$, ^Tut\d, ^Tutorial, _Retired$, ^Toast, ^Loading, ^Splash, ^ForceUpdate$, ^OverviewCanvas$, ^CurrencyBar$` | Regex feature KHÔNG mở (không phải màn hình, cần data đặc biệt, bước tutorial…). |
| `dismissAtStartFeatures` | `^OfflineEarning, ^Rating$, ^RewardPopup$, ^LevelUp$, ^DailyReward, ^DailyBonus, Offer$, Pack$` | Popup đang mở ngay sau boot cần đóng trước khi test. |
| `screenOpenTimeoutSeconds` | `10` | Thời gian tối đa chờ một màn mở xong. |
| `screenSettleSeconds` | `1.2` | Giữ màn sau khi mở cho animation / logic chạy. |
| `slowOpenMs` | `1500` | Mở chậm hơn ngưỡng này ⇒ Minor. |
| `loadEveryBuildScene` | `true` | Load thử mọi scene trong Build Settings. |
| `sceneSettleSeconds` | `3` | Chờ sau khi load scene. |
| `closeScreenAfterOpen` | `true` | Đóng màn sau khi mở (và kiểm tra đóng được). |

### UI Audit (`uiAudit`)

| Field | Mặc định | Ý nghĩa |
|-------|----------|---------|
| `minTouchTargetDp` | `44` | Kích thước chạm tối thiểu (dp) — Material 48dp, Apple 44pt. |
| `checkTextOverflow` | `true` | Kiểm tra text tràn. |
| `checkMissingSprite` | `true` | Kiểm tra Image thiếu sprite. |
| `checkOffScreen` | `true` | Kiểm tra phần tử ngoài màn hình. |
| `checkSafeArea` | `true` | Kiểm tra lấn safe area. |
| `checkRaycastBlocked` | `true` | Kiểm tra nút bị che raycast. |
| `checkLocalizationLeak` | `true` | Kiểm tra lộ key localize. |
| `checkDeadButtons` | `true` | Kiểm tra nút không có listener. |
| `localizationKeyPattern` | `^[a-z0-9]+(_[a-z0-9]+){1,}$` | Regex nhận diện text là key localize bị lộ. |
| `ignoredObjectPatterns` | `psd, cheat, debug, ^ignore_, stats, StatusBar, game_speed, IngameDebugConsole` | Regex tên object bỏ qua khi audit — kể cả khi object đó (hoặc cha của nó) đè lên nút: lớp debug/cheat chỉ có ở bản dev không tính là "nút bị che". |

### Button Sweep (`buttonSweep`)

| Field | Mặc định | Ý nghĩa |
|-------|----------|---------|
| `blacklistPatterns` | `buy, purchase, iap, restore, pay, price, delete, reset, logout, log_out, quit, exit, link, url, facebook, fanpage, discord, rate, review, privacy, terms, support, mail, cheat, debug` | Nút có tên/text khớp sẽ KHÔNG bấm. |
| `maxButtonsPerScreen` | `40` | Số nút tối đa bấm mỗi màn. |
| `waitAfterClickSeconds` | `0.6` | Chờ sau mỗi lần bấm. |
| `closeSpawnedScreens` | `true` | Đóng màn mới mở ra sau mỗi lần bấm để về màn đang test. |

### Monkey (`monkey`)

| Field | Mặc định | Ý nghĩa |
|-------|----------|---------|
| `durationSeconds` | `60` | Thời lượng chạy. |
| `actionsPerSecond` | `6` | Số thao tác mỗi giây. |
| `seed` | `0` | Seed ngẫu nhiên; 0 = theo thời gian (seed thực tế ghi trong report để tái hiện). |
| `buttonTapRatio` | `0.7` | Tỉ lệ thao tác là bấm vào nút (còn lại tap / kéo ngẫu nhiên). |
| `allowBackKey` | `true` | Cho phép bấm Back. |
| `respectBlacklist` | `true` | Không bấm nút trong blacklist của Button Sweep. |

### Economy (`economy`)

| Field | Mặc định | Ý nghĩa |
|-------|----------|---------|
| `testAmount` | `1234` | Lượng dùng khi cộng / trừ. |
| `testOverflow` | `true` | Chạy case tràn số. |
| `testSaveLoad` | `true` | Chạy case save → load. |
| `testRewards` | `true` | Chạy case phát thưởng. |
| `testPurchaseOffline` | `true` | Chạy case mua bằng tiền mềm. |
| `testConfigSanity` | `true` | Chạy case kiểm tra bảng config. |
| `rewardSkipCurrencies` | `[]` | Tiền tệ bỏ qua khi test phát thưởng (game định tuyến sang ví khác). |
| `skipCurrencies` | `[]` | Tiền tệ bỏ qua hoàn toàn. |
| `nonNegativeFieldPattern` | `(?i)(price\|cost\|amount\|reward\|value\|quantity\|qty\|count\|gold\|gem\|diamond\|coin)` | Regex tên field số không được âm trong config. |
| `idFieldPattern` | `^(?i)(id\|key\|productid\|product_id)$` | Regex tên field id (phải duy nhất trong bảng). |

### Performance (`performance`)

| Field | Mặc định | Ý nghĩa |
|-------|----------|---------|
| `warmupSeconds` | `3` | Chờ trước khi đo. |
| `sampleSeconds` | `20` | Thời gian lấy mẫu. |
| `targetFps` | `60` | FPS mục tiêu. |
| `maxP95FrameMs` | `33.4` | Frame time P95 tối đa (ms). |
| `maxP99FrameMs` | `50` | Frame time P99 tối đa (ms). |
| `hitchThresholdMs` | `100` | Frame dài hơn ngưỡng này tính là một lần giật. |
| `maxHitches` | `3` | Số lần giật tối đa trong thời gian đo. |
| `maxGcAllocPerFrameKb` | `4` | GC alloc trung bình mỗi frame tối đa (KB). |
| `maxTotalMemoryMb` | `1200` | Bộ nhớ tổng tối đa (MB). |
| `maxDrawCalls` | `250` | Draw call tối đa. |
| `maxSetPassCalls` | `100` | SetPass call tối đa. |

### Visual Regression (`visual`)

| Field | Mặc định | Ý nghĩa |
|-------|----------|---------|
| `baselineFolder` | `AutoTestBaselines` | Thư mục baseline, tương đối với project (commit vào git). |
| `pixelTolerance` | `16` | Sai khác mỗi kênh màu (0–255) dưới ngưỡng coi như giống. |
| `maxDiffPercent` | `1.5` | % pixel khác vượt ngưỡng ⇒ lỗi. |
| `maskObjectPatterns` | `time, timer, countdown, clock, fps` | Regex tên object bị ẩn trước khi chụp. |

### Device (`device`)

| Field | Mặc định | Ý nghĩa |
|-------|----------|---------|
| `adbPath` | `""` | Đường dẫn adb (rỗng = SDK Android của Unity / PATH). |
| `packageName` | `""` | Package name (rỗng = `PlayerSettings.applicationIdentifier`). |
| `launchActivity` | `""` | Activity khởi động (rỗng = tự resolve bằng `cmd package resolve-activity`). |
| `apkPath` | `""` | APK có sẵn để cài (rỗng = build mới). |
| `buildOutputFolder` | `Builds/AutoTest` | Thư mục ra của build test. |
| `buildBeforeRun` | `true` | Build APK trước khi chạy. |
| `uninstallBeforeInstall` | `false` | Gỡ app trước khi cài (xoá cả data trên máy). |
| `runTimeoutSeconds` | `900` | Thời gian tối đa của lượt chạy trên device. |
| `suites` | `smoke, ui-audit, economy, performance, scenarios` | Suite chạy trên device. |
| `collectLogcat` | `true` | Kéo logcat về đính kèm. |
| `measureColdStart` | `true` | Đo thời gian khởi động lạnh. |

### Chung toàn cục

| Field | Mặc định | Ý nghĩa |
|-------|----------|---------|
| `disabledSuites` | `[]` | Id suite tắt hẳn (không nằm trong "chạy tất cả"). |
| `disabledCases` | `[]` | Case tắt, dạng `suiteId/caseId` (vd `smoke/Shop`) — hiện nhưng ghi Bỏ qua. |

---

## 11. Quy trình QA đề xuất

**Hằng ngày / mỗi merge request** (CI, ~10–20 phút): `static` + `smoke` (+ `ui-audit`, `economy` nếu thời gian
cho phép), `-autotestFailOn major`. Lỗi mới (nhãn MỚI) ⇒ người tạo MR xử lý trước khi merge.

**Hằng đêm** (CI): toàn bộ suite Editor gồm `button-sweep`, `monkey`, `performance`, `visual`, `scenarios`.
Sáng hôm sau QA đọc report: lọc issue MỚI → log bug bằng **Sao chép bug** / `issues.csv`; theo dõi xu hướng
điểm sức khoẻ và số đo performance.

**Trước khi release**: chạy full trong Editor + `device` trên ít nhất một máy Android cấu hình thấp và một máy
tầm trung (performance tuyệt đối, cold start, logcat). Mục tiêu: 0 Blocker, 0 Critical, không Major mới so với
bản trước. `static` phải sạch phần Player Settings / define cấm.

**Khi đổi UI có chủ đích**: chạy `visual` → xem diff → **Chấp nhận ảnh hiện tại làm baseline** cho các màn đã đổi → commit baseline
cùng commit đổi UI (reviewer thấy ảnh baseline mới trong MR).

**Khi có bug từ report**:

1. Mở case → xem các bước tái hiện, ảnh, log, stack trace.
2. Chạy lại riêng case đó trong cửa sổ để xác nhận (monkey: đặt đúng seed).
3. Log bug bằng **Sao chép bug** (đã có môi trường + bước tái hiện + expected/actual).
4. Sau khi dev sửa: lượt chạy kế tiếp hiện issue đó trong mục "đã sửa".

**False positive**: không sửa code test để "cho qua" — thêm vào danh sách loại trừ phù hợp trong Settings
(§ 10) và commit `EZGAutoTestSettings.json` để cả team cùng hưởng.

---

## 12. Mở rộng hệ thống

- **Suite mới**: kế thừa `AutoTestSuite` (constructor rỗng) — tự xuất hiện. Khai `Id`, `DisplayName`,
  `Description`, `Mode` (Edit / Play / Device), `BuildCases` (phải rẻ, không side effect), `MutatesPlayerData`
  nếu sửa dữ liệu người chơi (để được sandbox).
- **Luật tĩnh mới**: implement `Ezg.AutoTest.Editor.IStaticRule` (constructor rỗng) — tự thành một case của
  Kiểm tra tĩnh. Ghi lỗi bằng `ctx.Report(...)` với `location` = asset path, `objectPath` = hierarchy path;
  vòng lặp dài `await StaticCheckUtil.Yield(ctx, i)` để UI còn phản hồi và nút Dừng có tác dụng. Tuyệt đối
  không sửa/lưu asset.
- **Kịch bản riêng, project hooks, adapter**: [AI-SCENARIO-GUIDE.md](AI-SCENARIO-GUIDE.md).
