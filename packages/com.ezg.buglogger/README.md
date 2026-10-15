# EZG Bug Logger

`com.ezg.buglogger` · namespace `Ezg.BugLogger`

Bug Logger dùng chung mọi project EZG. QA bấm nút nổi trong game → package chụp màn hình, mở form báo lỗi;
report được ghi vào **outbox trên disk trước** rồi một uploader gửi lần lượt lên BugHub Worker
(`POST /v1/reports`), thử lại có backoff. Report gửi lúc offline, hoặc còn dở khi tắt game, sẽ được gửi ở
lần mở sau.

Package gồm **lõi gửi report** (outbox, upload, context, lựa chọn form) và **UI dùng chung** (nút nổi, form,
toast, xem ảnh). Mọi sprite + font của UI nằm trong package và UI chạy trên canvas overlay riêng, không dựa
vào UI framework / template / art style / localize của game ⇒ form trông **giống hệt nhau ở mọi project**.
Game chỉ quyết định lúc nào hiện nút báo lỗi.

---

## Có mặt trong mọi bản build — host quyết định lúc nào hiện

| Assembly | Nội dung |
|---|---|
| `Ezg.BugLogger` | toàn bộ API + code gửi report |
| `Ezg.BugLogger.Settings` | `BugLoggerSettings` (ScriptableObject) |
| `Ezg.BugLogger.UI` | UI dùng chung (`BugLoggerUI`) — uGUI thuần, không phụ thuộc thư viện tween / inspector của game |
| `Ezg.BugLogger.Editor` | baker danh tính build + builder prefab UI |

Package không gate theo define: mọi bản build đều chứa code gửi report. **Host** quyết định ai thấy UI báo lỗi
(ví dụ chỉ hiện nút khi cheat bật cho máy QA). `Initialize()` lúc outbox rỗng không gọi mạng, không poll. Overlay UI
chỉ được dựng (`Resources.Load`) ở lần đầu bật nút — bản người chơi không bao giờ bật thì không tốn CPU/RAM, chỉ tốn
dung lượng build cho prefab + sprite + 3 font trong `Resources` (cỡ 1,8 MB trước nén: sprite RGBA32 ~1,4 MB + font ~0,4 MB).

---

## Cài đặt

1. Thêm package (embedded trong `Packages/` hoặc từ registry `com.ezg.*`). Phụ thuộc: `com.cysharp.unitask`
   2.5.11 + module `jsonserialize`, `unitywebrequest`, `imageconversion`.
2. Tạo asset **Create → Ezg → Bug Logger Settings**, đặt tại `Resources/BugLoggerSettings.asset`:

   | Field | Ý nghĩa |
   |---|---|
   | `projectCode` | **Bắt buộc.** Mã project đã đăng ký trên BugHub (ví dụ `SM007`). Trống → report chỉ nằm trong outbox. |
   | `endpointOverride` | Trống = `https://bug-reporter.developer-a1f.workers.dev`. Dùng cho server local khi mock test. |

   Đó là **toàn bộ** cấu hình client: không key, không token, không webhook.

> `endpointOverride` dạng `http://` (ví dụ server local) **chỉ chạy trong Editor**. Trên thiết bị package
> tự bỏ mọi endpoint không phải `https://` (log cảnh báo, report nằm chờ trong outbox) — kể cả khi project
> bật "Allow downloads over HTTP".

> Endpoint và `projectCode` đọc được từ APK store. Intake chống spam ở phía server (rate limit, quota ngày,
> công tắc intake) — BugHub contract §10.

---

## UI dùng chung (`Ezg.BugLogger.UI`)

```csharp
using Ezg.BugLogger.UI;

// Lúc boot (sau BugLogger.Initialize), tuỳ chọn:
BugLoggerUI.FeatureKeyProvider = () => MyUI.TopScreenName;   // điền sẵn ô "Màn hình / tính năng"; null → tên scene
BugLoggerUI.ResumeGame = () => Time.timeScale = MyGame.Speed; // đóng form trả về tốc độ game; null → timeScale cũ

// Hiện/ẩn nút nổi — ví dụ theo cheat của máy QA:
BugLoggerUI.LauncherVisible = isCheatOn;
```

| Member | Ghi chú |
|---|---|
| `bool LauncherVisible` | Hiện/ẩn nút nổi. Lần đầu bật dựng overlay (`DontDestroyOnLoad`, canvas overlay trên cùng: sorting layer cuối của project, order 32767). |
| `void OpenForm()` / `CloseForm()` | Mở form từ nút/cử chỉ riêng của game (vẫn tự chụp màn hình trước) / đóng form. |
| `bool IsFormOpen` | Tính cả lúc đang chụp và lúc chạy animation đóng — game dùng để chặn phím Back của mình. |
| `Func<string> FeatureKeyProvider` | Gọi lúc QA bấm nút, trước khi chụp. |
| `bool PauseGameWhileOpen` | Mặc định `true`: form mở → `Time.timeScale = 0`. |
| `Action ResumeGame` | Cách resume khi form đóng. |
| `event FormOpened` / `FormClosed` | |
| `UniTask<byte[]> CaptureScreenshotAsync()` / `void CaptureScreenshot(Action<byte[]>)` | Chụp màn hình **không kèm UI Bug Logger** (nút nổi, toast, form): tắt overlay đúng frame chụp rồi bật lại, trả JPG đã nén như ảnh form gửi (null khi lỗi). Công cụ tự gửi report (QA tự động, cheat) dùng hàm này thay vì `ScreenCapture`. Bản callback gọi qua reflection được. |
| `void HideForCapture(float seconds)` | Ẩn UI Bug Logger cho công cụ chụp từ ngoài app (adb screencap, simctl): gọi, chờ 1–2 frame, chụp, gọi lại với `0`. Hết hạn tự hiện lại. |

Form gồm: ảnh chụp (xem lớn, bật/tắt đính kèm), tiêu đề, mô tả (bắt buộc — gửi vào `actual`), phân loại, tỉ lệ
tái hiện (`x/y`), màn hình / tính năng, tag, người được tag (lựa chọn + giới hạn lấy từ form của server, giới hạn
độ dài áp thẳng vào ô nhập). Đóng form khi chưa gửi thì nội dung đang gõ được giữ cho lần mở sau trong phiên.
Phím Back (Android) đóng ảnh lớn / form. Nếu host chưa gán `BugLoggerConfig.Notify`, thông báo của package hiện
bằng toast của Bug Logger; report gửi xong / thất bại cũng hiện toast khi nút nổi đang bật.

Chữ trên UI cố ý chỉ có tiếng Việt và không đi qua localize của game (công cụ nội bộ QA). Font Be Vietnam Pro
(SIL OFL 1.1, xem `Third Party Notices.md`) đóng kèm package.

Phím Back: chạy với Input Manager cũ và với Input System mới (`com.unity.inputsystem` có trong project thì asmdef tự
bật define `EZG_BUGLOGGER_INPUT_SYSTEM`). Màu và độ trong suốt được chỉnh trên project Gamma color space; project
Linear blend các lớp mờ (scrim, bóng, nút nổi lúc rảnh) trong không gian tuyến tính nên các lớp đó sáng hơn một chút.

### Sửa UI

Prefab `Runtime/UI/Resources/EzgBugLogger/BugLoggerOverlay.prefab` là **output** của
`Editor/BugLoggerUIPrefabBuilder.cs`: đổi bố cục / kích thước thì sửa builder rồi chạy
**Tools → EZG → Bug Logger → Rebuild UI Prefab**, đừng sửa tay prefab. Màu nằm ở `BugLoggerPalette`, chữ ở
`BugLoggerStrings`, sprite sinh sẵn ở `Runtime/UI/Sprites` (trắng, tint bằng màu). Test EditMode
`BugLoggerUIPrefabTests` chặn prefab tham chiếu asset ngoài package hoặc thiếu field.

---

## Gọi API lõi từ host (UI riêng)

Chỉ cần khi game muốn tự dựng UI báo lỗi thay vì dùng `BugLoggerUI`. Code host nằm trong namespace `Ezg.*`, ở đó
tên `BugLogger` resolve ra **namespace** `Ezg.BugLogger` chứ không phải class (lỗi CS0234 / CS0118). Luôn đặt alias:

```csharp
using Ezg.BugLogger;
using BugLoggerApi = Ezg.BugLogger.BugLogger;

// Lúc boot, một lần
BugLoggerApi.Initialize();
BugLoggerConfig.PlayerIdProvider = () => MyGame.PlayerId;
BugLoggerConfig.ExtraContextProvider = () => new Dictionary<string, string> { ["stage"] = MyGame.Stage.ToString() };
BugLoggerConfig.SaveSnapshotProvider = async () =>
{
    await UniTask.SwitchToThreadPool();
    return MySave.ToJson();
};
BugLoggerConfig.Notify = message => MyToast.Show(Localize(message));   // message là một hằng NOTIFY_*

// Mở form: nạp lựa chọn (server → cache disk → mặc định)
var options = await BugLoggerApi.GetFormOptionsAsync();
// options.Categories, options.Tags, options.People, options.Limits (validate độ dài trước khi gửi)

// Chụp màn hình (game tự chụp, package mã hoá)
var texture = ScreenCapture.CaptureScreenshotAsTexture();
var jpg = BugLoggerApi.EncodeScreenshot(texture);
Object.Destroy(texture);

// Gửi
var id = BugLoggerApi.Submit(new BugReportDraft
{
    Kind = BugReportKind.Bug,
    Title = titleInput.text,
    FeatureKey = currentScreenName,
    Category = selectedCategory,
    ReproduceRate = "3/10",
    Actual = actualInput.text,
    Expected = expectedInput.text,
    Steps = stepsInput.text,
    TagIds = selectedTagIds,
    MentionIds = selectedPeopleIds,
}, jpg);
```

---

## API (`Ezg.BugLogger`)

### `BugLogger` (static)

| Member | Ghi chú |
|---|---|
| `void Initialize()` | Gọi một lần lúc boot: đọc settings, gắn log ring buffer, quét outbox, gửi tiếp report còn lại. Gọi lại vô hại; `Submit` / `GetFormOptionsAsync` tự gọi nếu host quên. |
| `UniTask<BugFormOptions> GetFormOptionsAsync(CancellationToken = default)` | `GET /v1/projects/<code>/form`. Lỗi mạng → bản cache disk → mặc định (category `Visual/Logic/GD`, tag/người rỗng). Không ném vì mạng. |
| `string Submit(BugReportDraft draft, byte[] screenshotJpg)` | Ghi outbox (nguyên tử), trả `id` (uuid v4) ngay, đánh thức uploader. Không ném vì mạng. Bug thiếu `Title` hoặc `Actual` → `ArgumentException` (lỗi caller). Lỗi ghi disk → `Notify(NOTIFY_SAVE_FAILED)` + `ReportFailed`. |
| `byte[] EncodeScreenshot(Texture2D)` | JPEG cạnh dài ≤ 1280 px, quality 75; blit qua RenderTexture tạm, huỷ texture tạm. Không huỷ texture nguồn. |
| `int PendingCount` | Số report đang chờ gửi. |
| `event Action ReportQueued` | Report đã nằm an toàn trên disk. |
| `event Action<int> ReportSent` | Worker đã nhận (mới hoặc trùng); số issue — 0 với feedback. |
| `event Action<string, string> ReportFailed` | Thất bại vĩnh viễn (400/404/413, bỏ cuộc, không ghi được disk): id + lý do. |

Mọi member gọi trên **main thread**; mọi event raise trên main thread.

### `BugLoggerConfig` (static, tuỳ chọn)

| Member | Ghi chú |
|---|---|
| `Func<string> PlayerIdProvider` | → `player.id`. |
| `Func<IReadOnlyDictionary<string, string>> ExtraContextProvider` | → `extra` (≤ 32 cặp, giá trị ≤ 256). Gọi trên main thread lúc submit — phải rẻ. |
| `Func<UniTask<string>> SaveSnapshotProvider` | → `save.json` (≤ 5 MiB, hạn 10 s realtime). Null = không đính kèm. |
| `Action<string> Notify` | Nhận `NOTIFY_SAVED_OFFLINE` / `NOTIFY_SAVED_SENDING` / `NOTIFY_SAVE_FAILED` (host tự localize). |
| `IBugReportTransport Transport` | Seam thay transport (test, backend khác). Null = `BugHubTransport`. |

Provider ném exception thì bị bắt và ghi chú vào `extra` của report, không làm hỏng report.

### `BugReportDraft`

`Kind` (`Bug` \| `Feedback`), `Title`, `FeatureKey`, `Category`, `ReproduceRate`, `Actual`, `Expected`, `Steps`,
`Description`, `TagIds`, `MentionIds`. Package tự cắt theo giới hạn contract: title 100, actual / expected / steps /
description 4000, reproduceRate 16, tagIds 4, mentionIds 5. Feedback không cần title.

### `BugFormOptions`

`Schema`, `Code`, `IntakeEnabled`, `Categories`, `Tags` / `People` (`BugFormOption { Id, Name }`), `Limits`
(`TitleMax`, `FieldMax`, `MaxTags`, `MaxMentions`, `ScreenshotMaxBytes` — đã kẹp không vượt giới hạn package áp),
`Source` (`Server` \| `Cache` \| `Default`).

---

## Report đi thế nào

```
Submit (main thread: chụp context) ─► (thread pool) dựng report.json + cắt kích thước ─► outbox/<id>/ ─► ReportQueued, Notify
                                                                                              │
uploader (single flight) ── chưa cấu hình → giữ ── offline → 2 s xem lại cờ mạng              ▼
   │ gửi report cũ nhất chưa phải chờ (timeout 15 s, không theo redirect, header X-BugHub-Project)
   ├─ 201 / 200 duplicate / 202 feedback → xoá folder, ReportSent(number)
   ├─ 400 / 404 / 413 (4xx khác)        → failed/<id>/ + failure.json, LogError, ReportFailed
   └─ 403 intake_disabled / 429 / 5xx / 3xx / mạng / timeout / lỗi disk
          → attempt.json += 1, chờ max(5 s × 2^(n-1) ≤ 300 s, Retry-After) — trigger cắt ngắn phần backoff,
            không cắt phần Retry-After
          → server có trả lời (5xx / 403): chỉ report đó chờ, vòng gửi đi tiếp sang report sau nó (một report
            server cứ từ chối tạm không chặn bug mới); không có response (mạng / timeout): cả vòng chờ chung
          → bỏ cuộc sau 50 lần hoặc 24 h kể từ lần thất bại đầu → failed/<id>/ + ReportFailed
```

- **Outbox:** `persistentDataPath/BugLogger/outbox/<id>/` (`report.json`, `screenshot.jpg`, `log.txt`,
  `save.json`, `attempt.json`). File ghi `*.tmp` rồi rename, `report.json` ghi cuối — crash giữa chừng không
  tạo report dở. Tối đa 50 report / 50 MB (bỏ cái cũ nhất). Không dùng PlayerPrefs / save game, nên reset save
  không xoá report. `failed/` giữ tối đa 50 report để soi, không gửi lại.
- **Trigger** khởi chạy/đánh thức uploader: `Initialize`, `Submit`, app quay lại foreground
  (`OnApplicationPause(false)`), mạng có lại khi còn report chờ. Outbox rỗng = không poll, không `Update`.
- Mọi lần chờ dùng **realtime** (form thường đặt `timeScale = 0`).
- **Form cache:** `persistentDataPath/BugLogger/form/<CODE>.json`, ghi mỗi lần tải thành công.

### Context tự lấy

`app` (version, build, commitSha, branch, platform, Unity version) · `device` (id, model, OS, RAM, screen) ·
scene đang mở (`extra.scene`) · 200 dòng log cuối + stack trace exception (`log.txt`, ≤ 1 MiB phần đuôi).

### Danh tính build

Baker Editor (`BuildPlayerProcessor`) chạy với mọi build player: ghi `commitSha`
(`git rev-parse HEAD` → `CI_COMMIT_SHA`) và `branch` (git → `CI_COMMIT_REF_NAME` khi CI checkout detached
HEAD) cùng số build vào `Temp/BugLogger/buglogger_build.json`, rồi thêm vào StreamingAssets của build.
Không ghi gì dưới `Assets/`. Trong Editor, runtime lấy HEAD + branch hiện tại trực tiếp từ git.

---

## Test

`Packages/manifest.json` cần `"testables": ["com.ezg.buglogger"]`.

- **EditMode** `Ezg.BugLogger.Tests.Editor`: backoff + bỏ cuộc, phân loại response (403 tạm, 400/404/413
  vĩnh viễn, Retry-After), builder (đủ field schema, giới hạn), outbox, ring buffer, form (server → cache →
  mặc định), baker (commit/branch), settings, mã hoá screenshot, prefab UI chỉ dùng asset của package + nối đủ field,
  lượt tải lựa chọn form dùng chung cho nhiều chỗ chờ.
- **PlayMode** `Ezg.BugLogger.Tests.Runtime`: uploader với transport giả (gửi, trùng, từ chối, backoff 5/10/20 s
  khi `timeScale = 0`, Retry-After, bỏ cuộc 50 lần / 24 h, giữ khi chưa cấu hình), form với host không tồn tại.

---

## Ghi chú

- Giờ trong metadata (`createdAtUtc`, giờ dòng log, sổ sách outbox) là giờ UTC của thiết bị: package không
  tham chiếu được đồng hồ của game. Không dùng cho luật game nào. Hệ quả: QA chỉnh giờ máy tiến lên quá
  24 h trong lúc một report đang thử lại thì report đó bị bỏ cuộc (→ `failed/`) ở lần thử kế.
- Log ring buffer chỉ giữ tham chiếu vào mảng cấp phát sẵn (không cấp phát trên log callback). Nhưng khi đã
  có subscriber `logMessageReceivedThreaded`, Unity tạo bản sao chuỗi managed cho **mỗi** dòng log — build QA
  trả thêm chừng đó GC mỗi dòng. Tránh `Debug.Log` mỗi frame trong gameplay; muốn giảm thêm, host có thể gọi
  `Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None)` (và `Warning`) ở build QA — buffer
  chỉ giữ stack trace của exception.
- Tên category / tag / người trong `BugFormOptions` là dữ liệu từ server: UI của package hiển thị bằng label tắt
  rich text; host tự dựng UI thì cũng tắt rich text (`TMP_Text.richText = false`) để chuỗi lạ không render thành markup.
- `SaveSnapshotProvider` và log được upload nguyên văn: host chỉ đưa vào snapshot các module gameplay cần để
  tái hiện bug, không đưa dữ liệu tài khoản (email, token).
