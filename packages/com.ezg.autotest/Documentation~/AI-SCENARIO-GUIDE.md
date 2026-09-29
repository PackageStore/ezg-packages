# Hướng dẫn viết kịch bản Auto Test riêng cho game (cho AI agent + dev)

Tài liệu này là **nguồn duy nhất** một AI agent (Claude Code…) cần đọc để triển khai một kịch bản test riêng
của game trên **EZG Auto Test System** (`com.ezg.autotest`). Đọc hết một lần trước khi viết code. Dev cũng
dùng được y như vậy.

> **Tóm tắt 10 dòng**
> 1. File kịch bản nằm trong `Assets/_Project/AutoTests/Scenarios/<Nhóm>/<Tên>Scenario.cs` — tạo bằng menu
>    `Assets > Create > EZG > Auto Test Scenario` hoặc `AutoTestScaffolder.CreateScenario(...)`.
> 2. Class kế thừa `AutoTestScenario`, có `[AutoTestScenario("Tên tiếng Việt", Category = …, Description = …)]`,
>    bọc trong `#if UNITY_EDITOR || EZG_AUTOTEST`.
> 3. `SetUp`: `await GameFlow.EnsureReady(ctx);` rồi chuẩn bị state (lưu giá trị gốc trước khi đổi).
> 4. `Run`: mỗi hành động người chơi là một `using (ctx.Step("Bấm nút Mua")) { … }`.
> 5. Chờ bằng `ctx.Ui.WaitFor(...)` / `ctx.WaitUntil(...)` — **không** sleep cố định.
> 6. `ctx.Check` = lỗi mềm (chạy tiếp) · `ctx.Assert` = lỗi cứng (dừng case) · `ctx.Skip` = thiếu điều kiện.
> 7. Gọi thẳng code game (Service tĩnh, `PlayerDataManager`, `DataManager`) — giá trị kỳ vọng tính từ config, không hardcode.
> 8. `TearDown`: khôi phục **đồng bộ** mọi state đã đổi, cuối cùng `if (GameFlow.IsReady(ctx.Session)) await GameFlow.ReturnToBaseline(ctx);`.
> 9. Compile sạch → chạy kịch bản → đọc report → sửa tới khi pass hoặc tới khi xác định được **bug thật** của game (báo lại, không nới kiểm tra).
> 10. Chạy lại lần 2 liên tiếp vẫn pass ⇒ dọn state đúng.

## Mục lục

1. [Khi nào viết kịch bản riêng](#1-khi-nào-viết-kịch-bản-riêng)
2. [File đặt ở đâu](#2-file-đặt-ở-đâu)
3. [Vòng đời một kịch bản](#3-vòng-đời-một-kịch-bản)
4. [Tham chiếu API](#4-tham-chiếu-api)
5. [Report / Check / Assert / Fail / Skip — chọn cái nào, severity nào](#5-report--check--assert--fail--skip--chọn-cái-nào-severity-nào)
6. [Pattern viết kịch bản](#6-pattern-viết-kịch-bản)
7. [NÊN / KHÔNG NÊN](#7-nên--không-nên)
8. [Ví dụ hoàn chỉnh](#8-ví-dụ-hoàn-chỉnh)
9. [Chạy kịch bản và đọc kết quả](#9-chạy-kịch-bản-và-đọc-kết-quả)
10. [Checklist trước khi báo xong](#10-checklist-trước-khi-báo-xong)
11. [Project hooks](#11-project-hooks)
12. [Viết adapter cho project không theo template EZG](#12-viết-adapter-cho-project-không-theo-template-ezg)
13. [Xử lý sự cố](#13-xử-lý-sự-cố)

---

## 1. Khi nào viết kịch bản riêng

Các suite chung (smoke, UI audit, button sweep, economy…) đã tự động phủ: mở/đóng mọi màn hình, bấm mọi nút,
cộng/trừ tiền, config sanity, hiệu năng. **Kịch bản riêng** dành cho những gì suite chung không biết:

- Một **luồng người chơi** có ý nghĩa nghiệp vụ: mua gói trong shop, nâng cấp trạm, nhận thưởng hằng ngày,
  hoàn thành nhiệm vụ, mở rương, đổi tên, tua nhanh thời gian offline…
- Kiểm tra **kết quả nghiệp vụ** sau hành động: tiền trừ đúng giá theo config, phần thưởng đúng bảng, trạng thái
  lưu lại sau save/load, không nhận thưởng hai lần.
- **Hồi quy** một bug đã sửa (viết kịch bản tái hiện bug, giữ lại để bug không quay lại).

Một kịch bản = **một** luồng. Không gom "test toàn bộ shop" vào một file — tách "mua gói vàng", "mua gói kim
cương thiếu tiền", "nhận quà miễn phí hằng ngày" thành ba kịch bản.

---

## 2. File đặt ở đâu

### Project có asmdef cho code game (template EZG: `Ezg.Features`)

```
Assets/_Project/AutoTests/                      (không có Assets/_Project ⇒ Assets/AutoTests/)
├── <Product>.AutoTests.asmdef                  asmdef riêng cho test
├── <Product>AutoTestHooks.cs                   project hooks (tuỳ chọn, tối đa MỘT class)
├── <Product>GameAdapter.cs                     CHỈ project không theo template EZG
└── Scenarios/
    ├── Shop/ShopBuyGoldPackScenario.cs
    └── Farm/StationHoldUpgradeScenario.cs
```

Asmdef `<Product>.AutoTests` (`<Product>` = `PlayerSettings.productName` bỏ dấu, PascalCase — vd
`MyGame.AutoTests`):

- `references`: `Ezg.AutoTest` + asmdef game + **toàn bộ reference của asmdef game** (UniTask, DOTween,
  TigerForge…) + `UnityEngine.UI` + `Unity.TextMeshPro` ⇒ kịch bản gọi được mọi thứ code game gọi được.
- `defineConstraints`: `["UNITY_EDITOR || EZG_AUTOTEST"]` ⇒ chỉ compile trong Editor và build test của suite
  device; **không bao giờ vào build release**.
- `includePlatforms: []`, `autoReferenced: false`.
- Namespace của code sinh ra: `<Product>.AutoTests`.

Game thêm reference mới sau này mà kịch bản cần ⇒ thêm tay vào asmdef AutoTests.

### Project không có asmdef (code game nằm trong `Assembly-CSharp`)

Không tạo asmdef. File đặt đâu cũng được (khuyên vẫn là `Assets/_Project/AutoTests/`) và **bắt buộc** bọc toàn
bộ file trong `#if UNITY_EDITOR || EZG_AUTOTEST … #endif` — assembly `Ezg.AutoTest` không tồn tại trong build
release, thiếu guard là build release lỗi compile. (Template sinh ra luôn có guard, kể cả khi có asmdef.)

### Tạo file

| Cách | Dùng khi |
|------|----------|
| Menu **Assets > Create > EZG > Auto Test Scenario** | Dev tạo tay. Nhập Tên / Nhóm / Mô tả → file sinh ra, được ping, prompt AI được copy vào clipboard. |
| `Ezg.AutoTest.Editor.AutoTestScaffolder.CreateScenario(name, category, description)` | Agent tạo qua Unity MCP (`unity_execute_code`). Trả asset path; file đã có ⇒ trả đường dẫn cũ, không ghi đè. |
| Viết tay | Được, miễn đúng thư mục + guard + attribute. Nhớ tạo asmdef trước (`AutoTestScaffolder.EnsureAutoTestsFolder()`). |

API scaffolder (namespace `Ezg.AutoTest.Editor`, chỉ Editor):

| Hàm | Làm gì |
|-----|--------|
| `string DetectGameAssembly()` | Tên asmdef chứa code game (ưu tiên asmdef có `UIManager`), `null` nếu game ở Assembly-CSharp. |
| `string EnsureAutoTestsFolder()` | Tạo thư mục AutoTests + asmdef (nếu game có asmdef và thư mục chưa có asmdef). Trả asset path thư mục. |
| `string GetAutoTestsFolderPath()` | Đường dẫn thư mục AutoTests (không tạo gì). |
| `string GetScenarioAssetPath(name, category)` | Đường dẫn file kịch bản sẽ tạo (không tạo gì). |
| `string CreateScenario(name, category, description)` | Sinh file kịch bản từ template (Run bắt đầu bằng `ctx.Skip("Kịch bản chưa được triển khai")`). |
| `string CreateProjectHooks()` | Sinh `<Product>AutoTestHooks.cs` nếu project chưa có class hooks; có rồi ⇒ trả đường dẫn class đó. |
| `string CreateAdapterTemplate()` | Sinh `<Product>GameAdapter.cs` (xem § 12). |
| `string BuildAiPrompt(scenarioAssetPath, featureHint)` | Prompt tiếng Việt dán cho Claude Code. |
| `bool InstallAiSkill(out string path)` | Copy skill `ezg-autotest-scenario` vào `.claude/skills/` của project. |

---

## 3. Vòng đời một kịch bản

```csharp
#if UNITY_EDITOR || EZG_AUTOTEST
using System.Threading.Tasks;
using UnityEngine;

namespace MyGame.AutoTests
{
    // using TRONG namespace: type của package thắng type global trùng tên của game
    // (vd template EZG có enum global tên GameFeature).
    using Ezg.AutoTest;

    [AutoTestScenario("Mua gói vàng trong Shop", Category = "Shop",
        Description = "Người chơi đủ kim cương mua gói vàng đầu tiên; kim cương trừ đúng giá, vàng cộng đúng.",
        TimeoutSeconds = 90)]
    public sealed class ShopBuyGoldPackScenario : AutoTestScenario
    {
        public override async Task SetUp(AutoTestContext ctx) { await GameFlow.EnsureReady(ctx); /* Arrange */ }
        public override async Task Run(AutoTestContext ctx) { /* Act + Assert theo từng ctx.Step */ }
        public override async Task TearDown(AutoTestContext ctx) { /* khôi phục state */ }
    }
}
#endif
```

**Attribute `[AutoTestScenario(name)]`** (đặt trên class, không kế thừa):

| Thuộc tính | Mặc định | Ý nghĩa |
|------------|----------|---------|
| `Name` (tham số constructor) | — | Tên hiển thị trong cửa sổ + report. Tiếng Việt, mô tả luồng: "Mua gói vàng trong Shop". |
| `Category` | `"Gameplay"` | Nhóm trong cửa sổ ("Shop", "Farm", "Daily"…). Trùng tên thư mục con trong `Scenarios/`. |
| `Description` | `""` | Mô tả luồng + kết quả mong đợi — QA đọc để hiểu case. |
| `Order` | `100` | Thứ tự trong nhóm (nhỏ trước). |
| `TimeoutSeconds` | `0` (= `general.caseTimeoutSeconds`, mặc định 120) | Timeout cả kịch bản. Giữ ≤ 120. |
| `Tags` | `[]` | Nhãn tự do (vd `"regression"`, `"bug-1234"`). |
| `RunOnDevice` | `true` | Chạy được trong build test trên device. Đặt `false` nếu dùng API chỉ có trong Editor. |
| `Disabled` | `false` | Tạm tắt (đang viết dở, chờ fix bug) — vẫn hiện, bị bỏ qua. |

Class phải `public`, không `abstract`, có constructor rỗng public (mặc định). Runner tự tìm — không đăng ký.

**Thứ tự chạy** — mỗi kịch bản là một case của suite `scenarios` (id case = tên class đầy đủ), chạy trong Play
mode (phiên Play của suite). Runner làm:

1. `Disabled = true` ⇒ Bỏ qua. Trên device: `RunOnDevice = false` ⇒ không liệt kê.
2. **`GameFlow.EnsureReady(ctx)`** — chờ game boot, chạy hooks `OnSessionStarted`, đóng popup đầu game (một lần mỗi
   phiên). Game không boot ⇒ Lỗi **Blocker**, SetUp/Run/TearDown **không** chạy.
3. Tạo **instance mới** của class kịch bản (field luôn bắt đầu sạch mỗi lần chạy).
4. **`SetUp(ctx)`** — chạy trong bước "Chuẩn bị (SetUp)". Tự gọi lại `await GameFlow.EnsureReady(ctx);` ở đầu vẫn an
   toàn (trả về ngay) và giữ được kịch bản đúng nếu runner đổi.
5. **`Run(ctx)`** — nội dung. Chỉ chạy khi SetUp không lỗi / không Skip.
6. **`TearDown(ctx)`** — nằm trong `finally`: **luôn chạy** khi đã qua bước 2, kể cả SetUp/Run lỗi, Skip, bị Dừng
   hay quá timeout. Exception trong TearDown ⇒ issue Minor "TearDown của kịch bản lỗi"; `OperationCanceledException`
   (bị Dừng giữa TearDown) bị bỏ qua. Vì vậy TearDown phải chịu được state dở dang (field còn `null`).
7. Runner gọi **`GameFlow.ReturnToBaseline(ctx)`** — đóng mọi màn kịch bản đã mở. Kịch bản tự gọi trong TearDown
   cũng không sao (nhanh hơn khi TearDown còn cần thao tác UI sau đó).

Khi case bị huỷ (Dừng / timeout), `ctx.Token` đã bị huỷ và case chỉ còn ~2 giây để dọn: mọi lệnh chờ
(`WaitSeconds`, `WaitUntil`, `ReturnToBaseline`…) ném `OperationCanceledException` ngay. ⇒ Trong TearDown
**khôi phục đồng bộ trước** (set lại tiền, cờ, level), việc cần `await` để **cuối cùng**.

**State dùng chung**: các kịch bản trong cùng suite chạy chung một phiên Play (một lần boot). Kịch bản trước
không dọn sạch ⇒ kịch bản sau hỏng lây. Sandbox chỉ khôi phục PlayerPrefs **sau cả suite**, không khôi phục
RAM giữa các kịch bản — đó là việc của TearDown.

---

## 4. Tham chiếu API

Namespace `Ezg.AutoTest` (runtime — dùng được cả trên device).

### 4.1 `AutoTestContext ctx` — mọi thứ một kịch bản cần

**Thuộc tính**

| Thành viên | Ý nghĩa |
|------------|---------|
| `Session` | Phiên chạy hiện tại (`AutoTestSession`) — dùng chung giữa các case trong một phiên Play. |
| `Result` | Kết quả case đang ghi (`TestCaseResult`) — thường không cần đụng. |
| `Token` | `CancellationToken` huỷ khi user bấm Dừng hoặc case quá timeout — truyền vào mọi lệnh chờ tự viết. |
| `Config` | Cấu hình (`AutoTestConfig`) — vd `ctx.Config.economy.testAmount`, `ctx.Config.smoke.slowOpenMs`. |
| `Game` | Adapter game (`IGameAdapter`) — màn hình, tiền tệ, save, config (§ 4.4). |
| `Hooks` | Project hooks (`IAutoTestProjectHooks`), có thể `null`. |
| `Ui` | Driver điều khiển UI như người chơi (`UiDriver`, § 4.2). |
| `IsDevice` | `true` khi đang chạy trong build test trên device thật. |
| `ElapsedMs` | Mili giây từ lúc case bắt đầu — dùng đo thời gian. |
| `LogSeverityCap` | `Severity?` — giới hạn severity của **mọi** error/exception log tự bắt trong case này (vd `Severity.Minor`). |
| `CurrentStep` | Tên bước đang mở (null nếu không có). |

**Ghi chép**

| Thành viên | Ý nghĩa |
|------------|---------|
| `void Log(string message)` | Ghi một dòng vào log của case (hiện trong report). |
| `IDisposable Step(string name, string detail = null)` | Mở một bước: `using (ctx.Step("Mở shop")) { … }`. Ghi thời lượng + trạng thái; tên bước thành "các bước tái hiện" của issue. |
| `Task Step(string name, Func<Task> body)` | Chạy một bước async có tên: `await ctx.Step("Mở shop", async () => { … });` — exception bay qua đánh bước đó Lỗi. |
| `TestIssue Report(Severity severity, string category, string title, string message = null, string location = null, string objectPath = null, string expected = null, string actual = null, string steps = null)` | Ghi một issue, **không dừng case**. `steps` null ⇒ tự điền các bước đã đi qua. Trả issue để bổ sung field (vd `.screenshot`). |
| `bool Check(bool condition, string title, string detail = null, Severity severity = Severity.Major, string location = null, string expected = null, string actual = null, string category = "Check")` | Kiểm tra mềm: sai thì ghi issue và **chạy tiếp**. Trả lại `condition`. Tham số thứ 3 là `detail` (string) — severity phải truyền **có tên**: `severity: Severity.Critical`. |
| `void Assert(bool condition, string message, Severity severity = Severity.Major)` | Kiểm tra cứng: sai thì **dừng case**, case Lỗi. |
| `void AreEqual<T>(T expected, T actual, string message, Severity severity = Severity.Major)` | Assert bằng nhau (ghi expected/actual vào issue). |
| `void AreApproximatelyEqual(double expected, double actual, string message, double epsilon = 0.0001, Severity severity = Severity.Major)` | Assert xấp xỉ (số thực, tiền kiểu double). |
| `void IsNotNull(object value, string message, Severity severity = Severity.Major)` | Assert khác null (hiểu đúng object Unity đã Destroy là null). |
| `void Fail(string message, Severity severity = Severity.Major)` | Dừng case ngay với lỗi. |
| `void Skip(string reason)` | Bỏ qua case có lý do (thiếu điều kiện chạy — **không phải bug**). |
| `void Warn(string title, string detail = null, string category = "Warning")` | Ghi issue **Minor**. |
| `void Info(string title, string detail = null, string category = "Info")` | Ghi issue **Info** (không đổi trạng thái, không trừ điểm). |
| `TestMetric Metric(string name, double value, string unit = "", double? max = null, double? min = null, Severity severity = Severity.Major)` | Ghi số đo; vượt `max` / dưới `min` ⇒ issue với `severity`. |
| `string Attach(string fileName, string content, string label = null)` | Đính kèm file văn bản (json/log/csv) vào report; trả đường dẫn tương đối. |
| `string DescribeSteps()` | Chuỗi "1. …\n2. …" các bước đã đi qua. |
| `static string Format(object value)` | Định dạng số/giá trị theo InvariantCulture (dùng cho expected/actual). |
| `static string Sanitize(string s)` | Chuỗi an toàn cho tên file. |

**Chụp màn hình**

| Thành viên | Ý nghĩa |
|------------|---------|
| `Task<string> Screenshot(string label, bool force = false)` | Chụp Game view, đính kèm vào case. Trả đường dẫn tương đối hoặc `null` (Edit mode, batchmode không GPU, tắt `captureScreenshots` — `force: true` để bỏ qua setting). |
| `Task<TestIssue> ReportWithScreenshot(Severity severity, string category, string title, string message = null, string location = null, string objectPath = null)` | Ghi issue kèm ảnh chụp ngay lúc đó (bằng chứng cho QA). |

**Chờ** (mọi lệnh chờ tôn trọng `Token` — bấm Dừng là thoát ngay)

| Thành viên | Ý nghĩa |
|------------|---------|
| `Task NextFrame()` | Chờ 1 frame. |
| `Task WaitFrames(int frames)` | Chờ N frame. |
| `Task WaitSeconds(double seconds)` | Chờ N giây thời gian thực (không bị `timeScale` ảnh hưởng). Chỉ dùng cho khoảng ngắn (≤ 1s) khi không có điều kiện nào để chờ. |
| `Task<bool> WaitUntil(Func<bool> condition, double timeoutSeconds, string description, bool failOnTimeout = true, Severity severity = Severity.Major)` | Chờ tới khi điều kiện đúng (kiểm tra mỗi frame). Hết giờ: `failOnTimeout = true` ⇒ case Lỗi "Hết Xs chờ: <description>"; `false` ⇒ trả `false` để tự xử lý. |
| `void ThrowIfCancelled()` | Ném nếu đã bị huỷ — gọi trong vòng lặp tự viết. |

### 4.2 `UiDriver ctx.Ui` — điều khiển UI như người chơi

Bấm qua EventSystem (pointer enter → down → giữ vài frame → up → click tại tâm object), không phụ thuộc Input
System — chạy được trong Editor lẫn device.

**Tìm**

| Thành viên | Ý nghĩa |
|------------|---------|
| `GameObject Find(string pathOrName, GameObject root = null, bool includeInactive = false)` | Tìm theo tên hoặc **đuôi đường dẫn** hierarchy (`"popup_confirm/btn_ok"`). Trong `root` nếu có, không thì mọi scene (kể cả DontDestroyOnLoad). Nhiều object cùng tên ⇒ trả object bất kỳ — dùng đuôi đường dẫn hoặc `root` để chắc chắn. |
| `GameObject FindByText(string text, bool contains = true, GameObject root = null)` | Tìm theo text hiển thị (Text/TMP), trả object Selectable gần nhất (nút chứa text). Phụ thuộc ngôn ngữ — hạn chế dùng. |
| `List<T> FindAll<T>(GameObject root = null, bool includeInactive = false) where T : Component` | Mọi component T trong root hoặc toàn scene. |
| `Task<GameObject> WaitFor(string pathOrName, float timeoutSeconds = 5, GameObject root = null, bool mustBeVisible = true)` | Chờ object xuất hiện (và hiển thị). Hết giờ trả **`null`** (không ném) — luôn kiểm tra kết quả. |

**Trạng thái**

| Thành viên | Ý nghĩa |
|------------|---------|
| `bool IsVisible(GameObject go)` | Active, alpha CanvasGroup > 0, Graphic không trong suốt, có kích thước, nằm trong màn hình. |
| `bool IsInteractable(GameObject go)` | Selectable interactable + CanvasGroup cho phép tương tác/raycast. |
| `bool IsClickable(GameObject go, out string reason)` | Bấm được thật: hiển thị + interactable + raycast tại tâm trúng chính nó (không bị che). `reason` mô tả lý do khi false ("bị che bởi 'Canvas/…/overlay'"). |
| `string GetText(GameObject go)` | Text hiển thị của object (Text/TMP trên chính nó hoặc con đầu tiên). |
| `Rect GetScreenRect(RectTransform rt)` | Hình chữ nhật trên màn hình (pixel, gốc dưới-trái). |
| `Vector2 GetScreenCenter(GameObject go)` | Tâm object trên màn hình (UI hoặc object 3D qua `Camera.main`). |
| `GameObject RaycastTop(Vector2 screenPos)` | Object trên cùng nhận raycast tại điểm màn hình. |
| `List<Selectable> GetClickables(GameObject root, bool onlyClickable = true)` | Các nút (Button/Toggle/IPointerClickHandler) trong root, theo thứ tự hierarchy. |

**Thao tác**

| Thành viên | Ý nghĩa |
|------------|---------|
| `Task<bool> Click(GameObject go, bool requireClickable = true)` | Bấm như ngón tay. Trả `false` và **không bấm** nếu object không bấm được (bị che, không interactable). `requireClickable: false` = bấm xuyên (không giống người chơi — chỉ dùng khi có lý do). |
| `Task<bool> Click(string pathOrName, float timeoutSeconds = 5, GameObject root = null)` | Chờ object (tối đa timeout) rồi bấm. `false` nếu không thấy hoặc không bấm được. |
| `Task<GameObject> Tap(Vector2 screenPos)` | Bấm vào object trên cùng tại điểm màn hình; trả object nhận bấm. |
| `Task<bool> Hold(GameObject go, float seconds)` | Nhấn giữ (nút giữ để nâng cấp liên tục…). |
| `Task Drag(Vector2 from, Vector2 to, float seconds = 0.3f)` | Kéo từ A tới B (cuộn list, vuốt). |
| `bool SetText(GameObject go, string text)` | Nhập text vào InputField/TMP_InputField (bắn `onValueChanged` + `onEndEdit`). |
| `bool SetToggle(GameObject go, bool isOn)` | Đặt Toggle. |
| `bool SetSlider(GameObject go, float normalizedValue)` | Đặt Slider (0–1). |
| `static string PathOf(Transform t)` | Đường dẫn hierarchy đầy đủ — dùng cho `objectPath` của issue. |
| `static float EffectiveAlpha(Transform t)` | Alpha hiệu dụng qua các CanvasGroup cha. |

### 4.3 `GameFlow` — luồng game dùng chung (static)

| Thành viên | Ý nghĩa |
|------------|---------|
| `Task EnsureReady(AutoTestContext ctx)` | Chờ game sẵn sàng (hooks/adapter báo ready ổn định ≥ 1s, tối đa `bootTimeoutSeconds`, hết giờ ⇒ Lỗi **Blocker**), chờ settle, chạy hooks `OnSessionStarted`, đóng popup đầu game, ghi màn nền. Một lần mỗi phiên. Edit mode ⇒ Skip. |
| `bool IsReady(AutoTestSession session)` | Game đã boot xong trong phiên này chưa. |
| `double BootSeconds(AutoTestSession session)` | Thời điểm boot xong (giây từ lúc vào Play), -1 nếu chưa. |
| `List<string> Baseline(AutoTestSession session)` | Tên các màn mở sẵn sau boot (HUD, thanh tiền…) — không bao giờ bị đóng. |
| `bool IsBaseline(AutoTestSession session, AutoTestFeature feature)` | Màn có thuộc màn nền không. |
| `Task DismissStartPopups(AutoTestContext ctx)` | Đóng popup khớp `smoke.dismissAtStartFeatures` + hooks `DismissBlockingPopups`. |
| `Task ReturnToBaseline(AutoTestContext ctx)` | Đóng mọi màn không thuộc màn nền (mở sau đóng trước) + hooks `DismissBlockingPopups`. Chỉ gọi khi `IsReady` (chưa boot thì màn nền rỗng ⇒ đóng cả HUD). |
| `List<AutoTestFeature> TestableFeatures(AutoTestConfig config, IGameAdapter game, IAutoTestProjectHooks hooks)` | Feature không bị loại trong settings/hooks. |
| `Task<OpenResult> OpenFeature(AutoTestContext ctx, AutoTestFeature feature, float? settleSeconds = null)` | Mở màn (data từ hooks `GetFeatureData`), đo thời gian, chờ settle. **Không ném** khi mở hỏng — trả `OpenResult { Root, OpenMs, WasAlreadyOpen, Error }`. |
| `Task<bool> CloseFeature(AutoTestContext ctx, AutoTestFeature feature)` | Đóng màn (bỏ qua nếu là màn nền), chờ tối đa 3s cho nó biến mất. `false` nếu không đóng được. |

### 4.4 `IGameAdapter ctx.Game` — cầu nối code game

Luôn kiểm tra capability trước khi dùng nhóm tương ứng: `if ((ctx.Game.Capabilities & AdapterCapabilities.Economy) == 0) ctx.Skip("…");`

| Capability | Nhóm hàm |
|------------|----------|
| `ReadyState` | `IsGameReady()`, `DescribeState()` |
| `Features` | `GetFeatures()`, `ShowFeature(feature, data, timeoutSeconds, ct)`, `CloseFeature(feature)`, `IsFeatureShowing(feature)`, `GetFeatureObject(feature)`, `GetOpenFeatures()` |
| `Economy` | `GetCurrencies()`, `GetBalance(c)`, `AddCurrency(c, amount)`, `RemoveCurrency(c, amount)` (false = game từ chối), `IsEnough(c, amount)`, `SetCurrency(c, amount)`, `BalanceType(c)` |
| `Rewards` | `GrantReward(c, amount)` (qua hệ thống reward của game, không popup; false = không hỗ trợ) |
| `PurchaseOffline` | `PurchaseOffline(c, cost, timeoutSeconds, ct)` → `Task<bool?>` (null = không hỗ trợ) |
| `PlayerData` | `SaveAll()`, `GetSaveKeys()`, `ReloadPlayerData()` (đọc lại từ storage; false = không hỗ trợ) |
| `Config` | `GetConfigCollections()` → `AutoTestConfigTable { Name, Asset, ItemType, Items }` |

Khác: `Name`, `Priority`, `Capabilities`, `Diagnostics` (ghi chú resolve type), `Initialize(config)`.

`AutoTestFeature { Name, Value, Raw }` — `Name` = tên enum, `Value` = giá trị số, `Raw` = giá trị enum gốc.
`AutoTestCurrency { Name, Value, Raw }` — tương tự cho enum tiền tệ.

**`EzgTemplateAdapter`** (project theo template EZG) có thêm:

| Thành viên | Ý nghĩa |
|------------|---------|
| `AutoTestFeature FindFeature(string name)` | Tìm feature theo tên enum (không phân biệt hoa thường). |
| `string ResolvePrefabName(AutoTestFeature f)` | Tên prefab theo quy ước `screen_<snake_case>`. |
| `static string ToSnakeCase(string s)` | `"StaffGacha"` → `"staff_gacha"`. |

Trong kịch bản của project template EZG (cùng assembly game) nên lấy feature **type-safe** từ enum thật:

```csharp
var shop = ctx.Game.GetFeatures().FirstOrDefault(f => f.Value == (long)GameEnums.Features.Shop);
```

### 4.5 Khác

| Thành viên | Ý nghĩa |
|------------|---------|
| `AutoTestSession.Set<T>(key, value)` / `Get<T>(key, fallback)` / `Has(key)` | Túi state dùng chung giữa các case trong một phiên (hiếm khi cần). |
| `AutoTestSession.OutputDir` / `IsDevice` / `DeviceName` | Thư mục report của lượt, cờ device, tên máy. |
| `AutoTestClock.Now` | Thời gian thực (giây, không bị timeScale) — đo khoảng thời gian. |
| `AutoTestClock.Until(condition, timeoutSeconds, token, pollSeconds = 0)` | Chờ điều kiện, trả bool (dùng trong adapter / hooks; kịch bản dùng `ctx.WaitUntil`). |
| `AutoTestClock.NextFrame(token)` / `Frames(n, token)` / `Seconds(s, token)` | Chờ ở mức thấp. |
| `AutoTestFilters.MatchesAny(value, patterns)` | So khớp danh sách regex như Settings. |
| `AutoTestLog.Info(msg)` / `Warn(msg)` | Log Console có tiền tố `[EZG AutoTest]` (không vào report — dùng `ctx.Log` cho report). |
| `Severity` | `Info`, `Minor`, `Major`, `Critical`, `Blocker`. |
| `TestStatus` | `Pending`, `Running`, `Passed`, `Warning`, `Failed`, `Error`, `Skipped`, `Cancelled`. |

---

## 5. Report / Check / Assert / Fail / Skip — chọn cái nào, severity nào

| API | Dừng case? | Sinh issue | Trạng thái case |
|-----|:----------:|------------|-----------------|
| `ctx.Report(severity, …)` | Không | 1 issue, severity chỉ định | theo severity (≥ `failSeverity` mặc định Major ⇒ Lỗi; Minor ⇒ Cảnh báo; Info ⇒ không đổi) |
| `ctx.Check(cond, title, …)` | Không | 1 issue khi `cond` sai (mặc định Major) | như Report |
| `ctx.Warn(title)` / `ctx.Info(title)` | Không | Minor / Info | Cảnh báo / không đổi |
| `ctx.Metric(name, v, unit, max:, min:, severity:)` | Không | khi vượt ngưỡng | như Report |
| `ctx.Assert` / `AreEqual` / `AreApproximatelyEqual` / `IsNotNull` | **Có** | 1 issue nhóm `Assert` | **Lỗi** (bất kể severity) |
| `ctx.Fail(msg, severity)` | **Có** | 1 issue | **Lỗi** |
| `ctx.WaitUntil(…, failOnTimeout: true)` hết giờ | **Có** | "Hết Xs chờ: …" | **Lỗi** |
| `ctx.Skip(reason)` | **Có** | không | **Bỏ qua** |
| Exception khác (NullReference trong test/game…) | **Có** | Critical `Exception` + stack | **Crash** |
| Error / Exception log của game trong lúc chạy | Không | Major / Critical (theo policy) | như Report |

**Chọn thế nào:**

- **`Assert`** khi bước sau **không thể** chạy có nghĩa nếu điều kiện sai: không thấy nút ⇒ không bấm được ⇒
  dừng. Không dừng thì các bước sau sinh hàng loạt lỗi "hệ quả" làm rối report.
- **`Check`** khi vẫn chạy tiếp được và muốn thu thêm thông tin: giá hiển thị sai nhưng vẫn mua được ⇒ Check giá,
  mua tiếp, Check số dư.
- **`Report`** khi cần issue giàu thông tin (`objectPath`, `location`) hoặc severity/nhóm riêng.
- **`Skip`** khi **thiếu điều kiện chạy mà không phải lỗi**: adapter thiếu capability, build chưa có tính năng,
  config rỗng. Không dùng Skip để giấu bug.
- **`Fail`** cho nhánh logic "không được phép xảy ra".
- Luôn điền `expected` / `actual` khi so giá trị — QA dán thẳng vào bug.

**Chọn severity** (cùng thang với QA):

| Tình huống trong kịch bản | Severity |
|---------------------------|----------|
| Không vào được luồng chính, mất save, game treo / không phản hồi | **Blocker** |
| Tiền trừ sai / trừ mà không nhận hàng, nhận thưởng hai lần, số dư âm, mua được khi không đủ tiền, exception | **Critical** |
| Nút không phản hồi / bị che, màn không mở, trạng thái sai sau hành động, dữ liệu không lưu | **Major** |
| Text / số hiển thị sai định dạng, giá hiển thị lệch config nhưng trừ đúng, chậm hơn ngưỡng | **Minor** |
| Số liệu tham khảo, ghi chú môi trường | **Info** |

Đừng thổi phồng: mọi thứ đều Critical thì QA không phân biệt được lỗi thật sự nặng.

---

## 6. Pattern viết kịch bản

### 6.1 Arrange → Act → Assert theo từng bước

```csharp
public override async Task Run(AutoTestContext ctx)
{
    using (ctx.Step("Bấm nút Shop trên HUD"))                          // Act
    {
        ctx.Assert(await ctx.Ui.Click("btn_shop", 5f), "Không bấm được nút Shop", Severity.Critical);
    }

    GameObject shop;
    using (ctx.Step("Chờ màn Shop mở"))                                // Assert trạng thái UI
    {
        shop = await ctx.Ui.WaitFor("screen_shop", 10f);
        ctx.Assert(shop != null, "Màn Shop không mở sau khi bấm nút", Severity.Critical);
        await ctx.Screenshot("shop_mo");
    }
    // …
}
```

Tên bước là **hành động người chơi**, tiếng Việt, cụ thể: "Bấm nút Mua gói 100 kim cương", "Kéo danh sách
xuống cuối", "Tua thời gian sang ngày hôm sau". Không đặt "Step 1", "Test shop", "Call BuyPack()". Danh sách
bước chính là "các bước tái hiện" QA dán vào bug.

### 6.2 Chờ UI — không bao giờ sleep cố định dài

```csharp
// ĐÚNG: chờ có điều kiện, có timeout, có mô tả
var popup = await ctx.Ui.WaitFor("popup_reward", 5f);
await ctx.WaitUntil(() => MyQuestService.IsCompleted(questId), 10, "nhiệm vụ chuyển sang hoàn thành");
var ok = await ctx.WaitUntil(() => ctx.Game.GetBalance(gold) > before, 3, "vàng tăng", failOnTimeout: false);

// SAI: chậm khi máy nhanh, fail ngẫu nhiên khi máy chậm
await ctx.WaitSeconds(5);
```

`ctx.WaitSeconds` chỉ dùng cho khoảng ngắn (≤ 1s) khi không có điều kiện nào để chờ (vd để animation bấm chạy
xong trước khi chụp ảnh). Vòng lặp tự viết phải có `await` mỗi vòng và kiểm tra huỷ:

```csharp
for (var i = 0; i < 10 && MyQueue.HasPending; i++)
{
    ctx.ThrowIfCancelled();
    MyQueue.ProcessNext();
    await ctx.NextFrame();
}
```

### 6.3 Tìm đúng object

- Dùng **tên object trong prefab** (đọc prefab / controller để biết), không dùng text hiển thị (đổi theo ngôn ngữ).
- Giới hạn phạm vi bằng `root` = gốc màn hình để không trúng nút trùng tên ở màn khác:
  `await ctx.Ui.WaitFor("btn_buy", 5f, shopRoot)`.
- Hoặc dùng đuôi đường dẫn: `"pack_gold_1/btn_buy"`.
- Gốc màn hình: `ctx.Game.GetFeatureObject(feature)`, `GameFlow.OpenFeature(...).Root`, hoặc `WaitFor("screen_x")`.
- Item sinh động trong list: `ctx.Ui.FindAll<MyPackItemView>(shopRoot)` (component của game) rồi lấy item đúng
  data (vd `item.PackId == pack.Id`) — chính xác hơn tìm theo tên.

### 6.4 Bấm như người chơi

- `ctx.Ui.Click(...)` trả `false` khi nút bị che / không interactable ⇒ **luôn kiểm tra** (Assert/Check). Đó thường
  là bug thật (overlay trong suốt che nút, CanvasGroup quên bật lại).
- Ưu tiên bấm nút thật trên UI. Chỉ gọi thẳng service khi hành động không phải trọng tâm của kịch bản, hoặc là
  chạm vào thế giới 3D mà driver không giả lập được (đặt tên bước vẫn theo hành động người chơi, vd "Chạm vào trạm
  thu hoạch số 1").
- Mở màn không qua nút (khi bước mở màn không phải trọng tâm): `await GameFlow.OpenFeature(ctx, feature)` — có đo
  thời gian, truyền data từ hooks, không ném.

### 6.5 Dùng code game trực tiếp

Kịch bản nằm trong assembly có reference tới code game ⇒ gọi thẳng được:

- **Service tĩnh** của feature (`Scripts/Controller/{X}Service.cs` trong template EZG) để đọc trạng thái / tính
  giá trị kỳ vọng / chuẩn bị state.
- **Player data**: `PlayerDataManager.<Module>` (template EZG). Đọc được thoải mái; ghi thì lưu giá trị gốc để
  khôi phục.
- **Config**: `DataManager.<Collection>` — **giá trị kỳ vọng lấy từ config**, không hardcode ("giá gói 1 là 100")
  — config đổi là test tự đúng theo.
- **Hàm cheat** của feature (`Cheat_*`) để nhảy state (thời gian, level, unlock).
- Game dùng UniTask: await trong kịch bản bằng `await someUniTask.AsTask();` (asmdef AutoTests đã có reference
  UniTask khi asmdef game có). Đừng `.Forget()` thứ mình cần chờ kết quả.
- **Không** gọi `UIManager.Instance` (hay singleton `Instance` nào) trước khi `EnsureReady` xong — `Instance` tự tạo
  bản rỗng khi game chưa khởi tạo, làm hỏng boot. Sau `EnsureReady` thì gọi được.

### 6.6 Chuẩn bị state + khôi phục

```csharp
AutoTestCurrency _gem;
double _gemBefore;
bool _prepared;

public override async Task SetUp(AutoTestContext ctx)
{
    await GameFlow.EnsureReady(ctx);
    _gem = ctx.Game.GetCurrencies().FirstOrDefault(c => c.Name == "Diamonds");
    if (_gem == null) ctx.Skip("Game không có tiền tệ Diamonds.");
    _gemBefore = ctx.Game.GetBalance(_gem);      // LƯU trước khi đổi
    _prepared = true;
    ctx.Game.AddCurrency(_gem, 500);
}

public override async Task TearDown(AutoTestContext ctx)
{
    if (_prepared) ctx.Game.SetCurrency(_gem, _gemBefore);                // 1) khôi phục đồng bộ trước
    if (GameFlow.IsReady(ctx.Session)) await GameFlow.ReturnToBaseline(ctx); // 2) việc cần await để cuối
}
```

Khôi phục cả: tiền, level, cờ tutorial, cờ đã nhận thưởng, offset thời gian cheat, setting đã đổi, màn đang mở.
Không cần gọi `Save()` — sandbox khôi phục PlayerPrefs sau suite; chỉ cần RAM sạch cho kịch bản sau.

### 6.7 Ảnh chụp và số đo

- Chụp ở điểm then chốt: sau khi màn mở, sau hành động chính, trạng thái cuối. Nhãn không dấu, ngắn:
  `await ctx.Screenshot("sau_khi_mua");`.
- Lỗi cần bằng chứng hình: `await ctx.ReportWithScreenshot(Severity.Major, "Shop", "Popup nhận thưởng không hiện");`.
- Đo thời gian phản hồi:

```csharp
var t0 = ctx.ElapsedMs;
await ctx.Ui.Click(buyButton);
await ctx.Ui.WaitFor("popup_reward", 5f);
ctx.Metric("Thời gian từ bấm Mua tới popup thưởng", ctx.ElapsedMs - t0, "ms", max: 1000, severity: Severity.Minor);
```

### 6.8 Đính kèm dữ liệu để debug

```csharp
ctx.Attach("player_resource.json", JsonUtility.ToJson(PlayerDataManager.PlayerResource), "Ví sau khi mua");
```

### 6.9 Thời gian

Tính năng theo thời gian (daily, cooldown, offline earning) ⇒ dùng **cheat / offset của TimeManager** của game để
tua thời gian; không đổi giờ hệ thống, không dùng `DateTime.Now` (luôn dùng TimeManager của game). Nhớ trả offset
về cũ trong TearDown.

### 6.10 Log lỗi có chủ đích

Kịch bản cố ý gây lỗi (nhập dữ liệu sai để kiểm tra game xử lý) mà game log Error ⇒ `ctx.LogSeverityCap =
Severity.Info;` (áp dụng cho **toàn bộ** log của case — chỉ dùng khi cả kịch bản đều là nhánh lỗi có chủ đích).
Nhiễu SDK chung ⇒ thêm vào `general.ignoredLogPatterns` trong Settings, không cap trong kịch bản.

### 6.11 Editor vs device

Kịch bản có `RunOnDevice = true` (mặc định) được compile vào build test trên device ⇒ **không dùng API
`UnityEditor`**. Bắt buộc dùng ⇒ bọc `#if UNITY_EDITOR` và đặt `RunOnDevice = false`. Rẽ nhánh theo môi trường:
`if (ctx.IsDevice) { … }`.

---

## 7. NÊN / KHÔNG NÊN

**NÊN**

- Một kịch bản = một luồng người chơi, tổng thời gian < 2 phút.
- Mở đầu SetUp bằng `await GameFlow.EnsureReady(ctx);`.
- Đặt tên bước bằng hành động người chơi tiếng Việt; tên kịch bản + mô tả tiếng Việt có dấu.
- Chờ có điều kiện (`WaitFor`, `WaitUntil`) với timeout hợp lý (UI 3–10s, logic 1–5s).
- Tính giá trị kỳ vọng từ config / service của game.
- Lưu state gốc trước khi đổi; khôi phục trong TearDown (đồng bộ trước, await sau); TearDown chịu được field null.
- Kiểm tra capability adapter trước khi dùng nhóm hàm; thiếu ⇒ `ctx.Skip` có lý do.
- Điền expected/actual; chọn severity theo § 5; chụp ảnh ở điểm then chốt.
- Truyền `severity:` có tên khi gọi `ctx.Check`.
- Chạy kịch bản 2 lần liên tiếp để chắc dọn state đúng.

**KHÔNG NÊN**

- Gọi `UIManager.Instance` / singleton `Instance` trước khi game sẵn sàng.
- `DateTime.Now` / `DateTime.UtcNow` — dùng TimeManager của game.
- `await ctx.WaitSeconds(5)` "cho chắc"; vòng lặp `while` không `await` / không `ThrowIfCancelled` (treo Editor).
- `Thread.Sleep`, `Task.Delay` (không bị huỷ theo nút Dừng, không bơm theo frame) — dùng lệnh chờ của `ctx`.
- Coroutine, `async void`.
- `gameObject.SetActive(...)` để mở/đóng màn — đi qua UI của game (bấm nút) hoặc `GameFlow` / adapter.
- Bấm nút mua bằng **tiền thật (IAP)**, restore purchase, xoá tài khoản, đăng xuất, mở link ngoài.
- Sửa `PlayerPrefs` trực tiếp / xoá save — dùng API của game.
- Gom nhiều luồng vào một kịch bản; phụ thuộc thứ tự chạy giữa các kịch bản.
- Hardcode số liệu config, tên hiển thị đã localize.
- Nới lỏng / xoá kiểm tra để kịch bản "pass" khi game thật sự sai — đó là bug, báo lại.
- Tạo class hooks thứ hai (chỉ một class hooks cụ thể trong project).
- Nhắc tên game khác trong tên kịch bản, comment, mô tả.

---

## 8. Ví dụ hoàn chỉnh

Ba ví dụ dưới viết trên API chung của package. Các type/hàm có tiền tố **`My…`** (`MyShopService`,
`MyStationService`, `MyDailyRewardService`, `MyTimeCheat`…) là **PLACEHOLDER** — thay bằng service thật của game
(đọc code feature để biết tên hàm). Namespace `MyGame.AutoTests` thay bằng namespace scaffolder sinh ra.

### 8.1 Mua gói vàng bằng kim cương trong Shop

```csharp
#if UNITY_EDITOR || EZG_AUTOTEST
using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace MyGame.AutoTests
{
    using Ezg.AutoTest;

    /// <summary>Người chơi đủ kim cương mua gói vàng đầu tiên trong Shop.</summary>
    [AutoTestScenario("Mua gói vàng bằng kim cương trong Shop", Category = "Shop", TimeoutSeconds = 90,
        Description = "Mở Shop từ HUD, mua gói vàng đầu tiên: kim cương trừ đúng giá config, vàng cộng đúng số " +
                      "lượng, popup nhận thưởng hiện và đóng được.")]
    public sealed class ShopBuyGoldPackScenario : AutoTestScenario
    {
        const double BALANCE_EPSILON = 0.5;

        AutoTestCurrency _gem;
        AutoTestCurrency _gold;
        double _gemOriginal;
        double _goldOriginal;
        bool _balancesSaved;
        MyGoldPackConfig _pack;                                   // PLACEHOLDER: model config gói của game

        public override async Task SetUp(AutoTestContext ctx)
        {
            await GameFlow.EnsureReady(ctx);
            if ((ctx.Game.Capabilities & AdapterCapabilities.Economy) == 0)
                ctx.Skip($"Adapter {ctx.Game.Name} không hỗ trợ Economy.");

            _gem = ctx.Game.GetCurrencies().FirstOrDefault(c => c.Name == "Diamonds");
            _gold = ctx.Game.GetCurrencies().FirstOrDefault(c => c.Name == "Gold");
            if (_gem == null || _gold == null) ctx.Skip("Game không có tiền tệ Diamonds/Gold.");

            // Giá + số lượng lấy từ config — KHÔNG hardcode.
            _pack = MyShopService.GetGoldPacks().FirstOrDefault();  // PLACEHOLDER
            if (_pack == null) ctx.Skip("Config chưa có gói vàng nào.");

            _gemOriginal = ctx.Game.GetBalance(_gem);
            _goldOriginal = ctx.Game.GetBalance(_gold);
            _balancesSaved = true;

            // Arrange: đảm bảo đủ kim cương (thêm dư 1 để không rơi vào biên).
            var missing = _pack.PriceGem + 1 - _gemOriginal;
            if (missing > 0) ctx.Game.AddCurrency(_gem, missing);
        }

        public override async Task Run(AutoTestContext ctx)
        {
            using (ctx.Step("Bấm nút Shop trên HUD"))
            {
                ctx.Assert(await ctx.Ui.Click("btn_shop", 5f), "Không bấm được nút Shop trên HUD", Severity.Critical);
            }

            GameObject shop;
            using (ctx.Step("Chờ màn Shop mở"))
            {
                shop = await ctx.Ui.WaitFor("screen_shop", 10f);
                ctx.Assert(shop != null, "Màn Shop không mở sau khi bấm nút", Severity.Critical);
                await ctx.Screenshot("shop_mo");
            }

            GameObject buyButton;
            using (ctx.Step($"Tìm gói vàng {_pack.Id} trong danh sách"))
            {
                // Item sinh động: tìm theo component + data thay vì theo tên.
                var item = ctx.Ui.FindAll<MyShopPackItemView>(shop)          // PLACEHOLDER: view item của game
                    .FirstOrDefault(v => v.PackId == _pack.Id);
                ctx.Assert(item != null, $"Shop không hiển thị gói {_pack.Id}", Severity.Critical);
                buyButton = ctx.Ui.Find("btn_buy", item.gameObject);
                ctx.Assert(buyButton != null, $"Gói {_pack.Id} không có nút Mua", Severity.Critical);

                var label = ctx.Ui.GetText(buyButton) ?? "";
                ctx.Check(label.Contains(_pack.PriceGem.ToString()), "Giá trên nút Mua lệch config",
                    severity: Severity.Minor, expected: _pack.PriceGem.ToString(), actual: label);
            }

            var gemBefore = ctx.Game.GetBalance(_gem);
            var goldBefore = ctx.Game.GetBalance(_gold);
            var clickedAt = ctx.ElapsedMs;
            using (ctx.Step($"Bấm Mua gói vàng {_pack.Id}"))
            {
                var clicked = await ctx.Ui.Click(buyButton);
                ctx.Assert(clicked, "Nút Mua bị che hoặc không bấm được", Severity.Critical);
            }

            using (ctx.Step("Kiểm tra kim cương bị trừ đúng giá"))
            {
                await ctx.WaitUntil(() => ctx.Game.GetBalance(_gem) < gemBefore, 5, "kim cương giảm sau khi mua",
                    failOnTimeout: false);
                var spent = gemBefore - ctx.Game.GetBalance(_gem);
                ctx.Check(Math.Abs(spent - _pack.PriceGem) <= BALANCE_EPSILON, "Trừ kim cương sai giá",
                    severity: Severity.Critical, expected: AutoTestContext.Format(_pack.PriceGem),
                    actual: AutoTestContext.Format(spent), category: "Shop.Price");
            }

            using (ctx.Step("Kiểm tra vàng được cộng đúng số lượng"))
            {
                var gained = ctx.Game.GetBalance(_gold) - goldBefore;
                ctx.Check(Math.Abs(gained - _pack.GoldAmount) <= BALANCE_EPSILON, "Cộng vàng sai số lượng",
                    severity: Severity.Critical, expected: AutoTestContext.Format(_pack.GoldAmount),
                    actual: AutoTestContext.Format(gained), category: "Shop.Reward");
            }

            using (ctx.Step("Đóng popup nhận thưởng"))
            {
                var popup = await ctx.Ui.WaitFor("popup_reward", 5f);
                if (!ctx.Check(popup != null, "Popup nhận thưởng không hiện sau khi mua", severity: Severity.Major))
                    return;
                ctx.Metric("Thời gian từ bấm Mua tới popup thưởng", ctx.ElapsedMs - clickedAt, "ms", max: 1500,
                    severity: Severity.Minor);
                await ctx.Screenshot("popup_thuong");
                ctx.Check(await ctx.Ui.Click("btn_claim", 3f, popup), "Không bấm được nút Nhận trên popup");
            }
        }

        public override async Task TearDown(AutoTestContext ctx)
        {
            if (_balancesSaved)
            {
                ctx.Game.SetCurrency(_gem, _gemOriginal);
                ctx.Game.SetCurrency(_gold, _goldOriginal);
            }

            if (GameFlow.IsReady(ctx.Session)) await GameFlow.ReturnToBaseline(ctx);
        }
    }
}
#endif
```

### 8.2 Giữ nút nâng cấp trạm để lên nhiều cấp

```csharp
#if UNITY_EDITOR || EZG_AUTOTEST
using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace MyGame.AutoTests
{
    using Ezg.AutoTest;

    [AutoTestScenario("Giữ nút Nâng cấp để trạm lên nhiều cấp", Category = "Farm", TimeoutSeconds = 90,
        Description = "Chọn trạm đầu tiên đã mở khoá, giữ nút Nâng cấp 2 giây: trạm lên ít nhất 2 cấp, vàng trừ " +
                      "đúng tổng chi phí các cấp theo config, không vượt cấp tối đa.")]
    public sealed class StationHoldUpgradeScenario : AutoTestScenario
    {
        const float HOLD_SECONDS = 2f;
        const int MIN_LEVELS_GAINED = 2;
        const int GOLD_BUFFER_LEVELS = 30;
        const double BALANCE_EPSILON = 0.5;

        AutoTestCurrency _gold;
        double _goldOriginal;
        int _stationId = -1;
        int _levelOriginal;

        public override async Task SetUp(AutoTestContext ctx)
        {
            await GameFlow.EnsureReady(ctx);
            if ((ctx.Game.Capabilities & AdapterCapabilities.Economy) == 0) ctx.Skip("Adapter không hỗ trợ Economy.");
            _gold = ctx.Game.GetCurrencies().FirstOrDefault(c => c.Name == "Gold");
            if (_gold == null) ctx.Skip("Game không có tiền tệ Gold.");

            _stationId = MyStationService.GetFirstUnlockedStationId();          // PLACEHOLDER
            if (_stationId < 0) ctx.Skip("Chưa có trạm nào mở khoá.");
            _levelOriginal = MyStationService.GetLevel(_stationId);             // PLACEHOLDER
            if (_levelOriginal >= MyStationService.GetMaxLevel(_stationId) - MIN_LEVELS_GAINED)
                ctx.Skip("Trạm đầu tiên đã gần cấp tối đa.");

            _goldOriginal = ctx.Game.GetBalance(_gold);
            // Đủ vàng cho nhiều cấp (tính từ config chi phí).
            var budget = 0.0;
            for (var lv = _levelOriginal; lv < _levelOriginal + GOLD_BUFFER_LEVELS; lv++)
                budget += MyStationService.GetUpgradeCost(_stationId, lv);      // PLACEHOLDER
            ctx.Game.AddCurrency(_gold, budget);
        }

        public override async Task Run(AutoTestContext ctx)
        {
            GameObject panel;
            using (ctx.Step($"Chạm vào trạm {_stationId} để mở bảng nâng cấp"))
            {
                // Chạm vào object 3D: gọi đúng hàm game dùng khi người chơi chạm trạm.
                MyStationService.SelectStation(_stationId);                     // PLACEHOLDER
                panel = await ctx.Ui.WaitFor("station_status_panel", 5f);
                ctx.Assert(panel != null, "Bảng nâng cấp trạm không hiện", Severity.Critical);
            }

            var goldBefore = ctx.Game.GetBalance(_gold);
            var levelBefore = MyStationService.GetLevel(_stationId);
            using (ctx.Step($"Giữ nút Nâng cấp {HOLD_SECONDS:0} giây"))
            {
                var button = await ctx.Ui.WaitFor("btn_upgrade", 3f, panel);
                ctx.Assert(button != null, "Không thấy nút Nâng cấp", Severity.Critical);
                ctx.Assert(await ctx.Ui.Hold(button, HOLD_SECONDS), "Nút Nâng cấp không giữ được (bị che/khoá)",
                    Severity.Critical);
                await ctx.NextFrame();
            }

            using (ctx.Step("Kiểm tra số cấp tăng và vàng trừ đúng tổng chi phí"))
            {
                var levelAfter = MyStationService.GetLevel(_stationId);
                var gained = levelAfter - levelBefore;
                ctx.Metric("Số cấp lên khi giữ nút", gained, "cấp", min: MIN_LEVELS_GAINED, severity: Severity.Major);

                var expectedCost = 0.0;
                for (var lv = levelBefore; lv < levelAfter; lv++)
                    expectedCost += MyStationService.GetUpgradeCost(_stationId, lv);
                var spent = goldBefore - ctx.Game.GetBalance(_gold);
                ctx.Check(Math.Abs(spent - expectedCost) <= BALANCE_EPSILON, "Vàng trừ lệch tổng chi phí nâng cấp",
                    severity: Severity.Critical, expected: AutoTestContext.Format(expectedCost),
                    actual: AutoTestContext.Format(spent), category: "Station.Upgrade");
                ctx.Check(levelAfter <= MyStationService.GetMaxLevel(_stationId), "Trạm vượt cấp tối đa",
                    severity: Severity.Critical);
                await ctx.Screenshot("sau_khi_nang_cap");
            }
        }

        public override async Task TearDown(AutoTestContext ctx)
        {
            if (_stationId >= 0)
            {
                MyStationService.Cheat_SetLevel(_stationId, _levelOriginal);    // PLACEHOLDER
                if (_gold != null) ctx.Game.SetCurrency(_gold, _goldOriginal);
            }

            if (GameFlow.IsReady(ctx.Session)) await GameFlow.ReturnToBaseline(ctx);
        }
    }
}
#endif
```

### 8.3 Nhận thưởng đăng nhập khi sang ngày mới (có save/load)

```csharp
#if UNITY_EDITOR || EZG_AUTOTEST
using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace MyGame.AutoTests
{
    using Ezg.AutoTest;

    [AutoTestScenario("Nhận thưởng đăng nhập khi sang ngày mới", Category = "Daily", TimeoutSeconds = 90,
        Description = "Tua thời gian sang ngày hôm sau: chấm đỏ Daily hiện, nhận được thưởng đúng bảng, không nhận " +
                      "lần hai, trạng thái đã nhận giữ nguyên sau khi lưu và đọc lại dữ liệu.")]
    public sealed class DailyRewardNextDayScenario : AutoTestScenario
    {
        const double BALANCE_EPSILON = 0.5;

        double _timeOffsetOriginal;
        MyDailyRewardSnapshot _dailyOriginal;                                   // PLACEHOLDER
        AutoTestCurrency _rewardCurrency;
        double _rewardCurrencyOriginal;
        bool _saved;

        public override async Task SetUp(AutoTestContext ctx)
        {
            await GameFlow.EnsureReady(ctx);
            if (!MyUnlockService.IsUnlocked("DailyReward"))                    // PLACEHOLDER
                ctx.Skip("Tính năng Daily chưa mở khoá ở save hiện tại.");

            _timeOffsetOriginal = MyTimeCheat.OffsetSeconds;                    // PLACEHOLDER (cheat TimeManager)
            _dailyOriginal = MyDailyRewardService.TakeSnapshot();               // PLACEHOLDER
            _saved = true;
        }

        public override async Task Run(AutoTestContext ctx)
        {
            using (ctx.Step("Tua thời gian sang ngày hôm sau"))
            {
                MyTimeCheat.AddOffsetSeconds(TimeSpan.FromDays(1).TotalSeconds);
                await ctx.WaitUntil(() => MyDailyRewardService.CanClaimToday(), 5, "game nhận ra ngày mới");
            }

            using (ctx.Step("Chờ chấm đỏ trên nút Daily"))
            {
                var dot = await ctx.Ui.WaitFor("btn_daily/red_dot", 5f);
                ctx.Check(dot != null, "Chấm đỏ Daily không hiện khi có thưởng chưa nhận", severity: Severity.Minor);
            }

            GameObject screen;
            using (ctx.Step("Bấm nút Daily trên HUD"))
            {
                ctx.Assert(await ctx.Ui.Click("btn_daily", 5f), "Không bấm được nút Daily", Severity.Critical);
                screen = await ctx.Ui.WaitFor("screen_daily_reward", 10f);
                ctx.Assert(screen != null, "Màn Daily không mở", Severity.Critical);
                await ctx.Screenshot("daily_mo");
            }

            var day = MyDailyRewardService.CurrentDayIndex();
            var reward = MyDailyRewardService.GetRewardOfDay(day);              // PLACEHOLDER: đọc từ config
            _rewardCurrency = ctx.Game.GetCurrencies().FirstOrDefault(c => c.Name == reward.CurrencyName);
            ctx.Assert(_rewardCurrency != null, $"Phần thưởng ngày {day} dùng tiền tệ lạ: {reward.CurrencyName}");
            _rewardCurrencyOriginal = ctx.Game.GetBalance(_rewardCurrency);

            GameObject claim;
            using (ctx.Step($"Bấm Nhận thưởng ngày {day + 1}"))
            {
                claim = await ctx.Ui.WaitFor("btn_claim", 5f, screen);
                ctx.Assert(claim != null, "Không thấy nút Nhận", Severity.Critical);
                ctx.Assert(await ctx.Ui.Click(claim), "Nút Nhận bị che hoặc khoá", Severity.Critical);
                await ctx.WaitUntil(() => !MyDailyRewardService.CanClaimToday(), 5, "thưởng hôm nay chuyển sang đã nhận");
            }

            using (ctx.Step("Kiểm tra nhận đúng phần thưởng theo bảng"))
            {
                var gained = ctx.Game.GetBalance(_rewardCurrency) - _rewardCurrencyOriginal;
                ctx.Check(Math.Abs(gained - reward.Amount) <= BALANCE_EPSILON, "Nhận thưởng Daily sai số lượng",
                    severity: Severity.Critical, expected: AutoTestContext.Format(reward.Amount),
                    actual: AutoTestContext.Format(gained), category: "Daily.Reward");
            }

            using (ctx.Step("Thử bấm Nhận lần nữa"))
            {
                var before = ctx.Game.GetBalance(_rewardCurrency);
                if (ctx.Ui.IsClickable(claim, out _)) await ctx.Ui.Click(claim);
                await ctx.WaitFrames(5);
                ctx.Check(Math.Abs(ctx.Game.GetBalance(_rewardCurrency) - before) <= BALANCE_EPSILON,
                    "Nhận được thưởng Daily hai lần trong một ngày", severity: Severity.Critical,
                    category: "Daily.DoubleClaim");
            }

            using (ctx.Step("Lưu rồi đọc lại dữ liệu người chơi"))
            {
                if ((ctx.Game.Capabilities & AdapterCapabilities.PlayerData) == 0 || !ReloadData(ctx))
                {
                    ctx.Info("Adapter không hỗ trợ đọc lại dữ liệu — bỏ bước save/load.");
                    return;
                }

                ctx.Check(!MyDailyRewardService.CanClaimToday(), "Mất trạng thái đã nhận Daily sau khi load lại",
                    severity: Severity.Critical, category: "Daily.SaveLoad");
            }
        }

        static bool ReloadData(AutoTestContext ctx)
        {
            ctx.Game.SaveAll();
            return ctx.Game.ReloadPlayerData();
        }

        public override async Task TearDown(AutoTestContext ctx)
        {
            if (_saved)
            {
                MyTimeCheat.OffsetSeconds = _timeOffsetOriginal;
                MyDailyRewardService.RestoreSnapshot(_dailyOriginal);
            }

            if (_rewardCurrency != null) ctx.Game.SetCurrency(_rewardCurrency, _rewardCurrencyOriginal);
            if (GameFlow.IsReady(ctx.Session)) await GameFlow.ReturnToBaseline(ctx);
        }
    }
}
#endif
```

---

## 9. Chạy kịch bản và đọc kết quả

### 9.1 Compile

Sau khi sửa `.cs`: Unity MCP `unity_execute_menu_item("Assets/Refresh")` → chờ `unity_editor_state` hết compiling
→ `unity_get_compilation_errors` (severity `error`) → sửa tới khi sạch. Kịch bản lỗi compile thì mọi kịch bản
trong cùng assembly đều biến mất khỏi cửa sổ.

### 9.2 Chạy

**Trong cửa sổ** (dev / QA): **EZG > Auto Test System** → suite **Kịch bản riêng** → chọn kịch bản → **Chạy riêng case này** (hoặc tick rồi **Chạy mục đã chọn**).

**Qua Unity MCP** (AI agent) — `unity_execute_code`:

```csharp
return Ezg.AutoTest.Editor.AutoTestRunner.Run(new[] { "scenarios" },
    new Ezg.AutoTest.Editor.AutoTestRunOptions { ScenarioFilter = "ShopBuyGoldPackScenario" });
```

Trả `true` = đã bắt đầu; `false` = đang có lượt khác chạy, Editor đang compile/import, hoặc đang ở Play mode (xem
Console, log tiền tố `[EZG AutoTest]`). `ScenarioFilter` khớp một phần (không phân biệt hoa thường) với id case (tên
class đầy đủ) hoặc tên hiển thị — truyền tên class là chắc nhất. Runner vào Play (Editor có thể reload domain — lệnh MCP trong lúc đó có
thể lỗi tạm, thử lại sau vài giây). Poll tới khi xong:

```csharp
return Ezg.AutoTest.Editor.AutoTestRunner.IsRunning;          // lặp tới khi false
```

```csharp
return Ezg.AutoTest.Editor.AutoTestRunner.LastReportFolder;   // thư mục report của lượt vừa chạy
```

Không lấy được `LastReportFolder` ⇒ lấy thư mục mới nhất trong `<project>/AutoTestReports/`.

Editor dùng chung giữa nhiều người/agent: trước khi chạy, kiểm tra không có ai đang dùng Play mode (nếu tool
MCP có `unity_agents_list` thì xem trước); không dừng phiên Play của người khác.

**CLI** (CI): `-autotestSuites scenarios` chạy cả suite (xem CI.md).

### 9.3 Đọc report

Trong thư mục report:

1. `summary.md` — xem nhanh case đạt/lỗi.
2. `report.json` — `suites[]` → suite `suiteId == "scenarios"` → `cases[]` → case có `name` = tên trong attribute.
   - `status`: `TestStatus` (2 Passed, 3 Warning, 4 Failed, 5 Error/Crash, 6 Skipped, 7 Cancelled — có thể là số
     hoặc tên).
   - `message`: lý do ngắn (thông điệp assert, "Quá thời gian … ở bước …", exception).
   - `steps[]`: bước nào `status` Failed = nơi hỏng.
   - `issues[]`: `severity` (0 Info … 4 Blocker), `category`, `title`, `message`, `expected`, `actual`,
     `stackTrace`, `location`, `screenshot`.
   - `logs[]`: log Unity của case (`type` Error/Exception là manh mối chính).
   - `metrics[]`, `attachments[]` (ảnh trong `screenshots/` — mở bằng công cụ đọc ảnh để xem UI lúc đó).
3. Case **Skipped** với message "Kịch bản chưa được triển khai" ⇒ quên xoá dòng `ctx.Skip` placeholder.

### 9.4 Lỗi của test hay bug của game?

| Dấu hiệu | Thường là | Làm gì |
|----------|-----------|--------|
| `WaitFor` trả null, tên object không có trong prefab | Lỗi test | Đọc prefab, sửa tên / root / đuôi đường dẫn. |
| Hết giờ chờ nhưng ảnh chụp cho thấy UI đã đúng | Lỗi test (điều kiện chờ sai) | Sửa điều kiện. |
| `Click` false, reason "bị che bởi …" | Có thể là bug game (overlay che nút) hoặc chưa đóng popup | Xem ảnh; popup chặn ⇒ đóng trong test; overlay vô hình ⇒ bug. |
| NullReference trong code **test** | Lỗi test | Sửa. |
| Exception trong code **game** (stack trỏ vào `Assets/_Project/…` không phải AutoTests) | Bug game | Báo lại. |
| Số liệu sai so với config (tiền, thưởng, level) sau khi xác nhận công thức | Bug game | Báo lại, **giữ** kiểm tra. |
| Chạy lần 2 mới lỗi | Lỗi test (không dọn state) | Sửa TearDown. |

Bug game thật: **không** nới kiểm tra hay đổi severity cho pass. Báo cho user: tên kịch bản, bước hỏng,
expected/actual, stack trace, đường dẫn ảnh. Tuỳ yêu cầu có thể đặt `Disabled = true` kèm comment mã bug.

---

## 10. Checklist trước khi báo xong

- [ ] Đã xoá dòng `ctx.Skip("Kịch bản chưa được triển khai")` và mọi comment `// AI:`.
- [ ] Attribute: `Name` / `Category` / `Description` tiếng Việt có ý nghĩa; `TimeoutSeconds` ≤ 120.
- [ ] Mọi bước là `ctx.Step("<hành động người chơi>")`.
- [ ] Không có `WaitSeconds` > 1s, `Task.Delay`, `Thread.Sleep`, vòng lặp không `await`.
- [ ] SetUp mở đầu bằng `GameFlow.EnsureReady`; kiểm tra capability trước khi dùng; thiếu điều kiện ⇒ `Skip` có lý do.
- [ ] Giá trị kỳ vọng tính từ config / service, không hardcode.
- [ ] Assert vs Check có chủ đích; severity theo § 5; so giá trị có expected/actual.
- [ ] Có ảnh chụp ở điểm then chốt.
- [ ] TearDown khôi phục mọi state (đồng bộ trước, await sau), chịu được field null, có guard `GameFlow.IsReady`.
- [ ] Không `DateTime.Now`, không `UIManager.Instance` trước khi ready, không API `UnityEditor` (hoặc đã guard + `RunOnDevice = false`), không bấm IAP thật.
- [ ] File nằm trong thư mục AutoTests, bọc `#if UNITY_EDITOR || EZG_AUTOTEST`, `using Ezg.AutoTest;` đặt trong namespace.
- [ ] Compile sạch (Unity MCP).
- [ ] Đã chạy kịch bản: **Passed**, hoặc chỉ fail vì bug game đã xác nhận (đã báo lại).
- [ ] Chạy lần 2 liên tiếp vẫn cho cùng kết quả (dọn state đúng).
- [ ] Báo cáo cho user: file đã tạo/sửa, kết quả chạy (trạng thái, đường dẫn report), bug phát hiện (nếu có).
- [ ] Không `git add` / commit trừ khi được yêu cầu.

---

## 11. Project hooks

Class **duy nhất** trong project kế thừa `AutoTestProjectHooks` (hoặc implement `IAutoTestProjectHooks`), đặt
trong thư mục AutoTests. Runner tự tìm (nhiều class ⇒ lấy class đầu tiên + cảnh báo). Tạo khung:
`AutoTestScaffolder.CreateProjectHooks()`.

| Hook | Khi nào gọi | Dùng để |
|------|-------------|---------|
| `Task OnSessionStarted(ctx)` | Mỗi phiên Play, sau khi game boot xong, trước case đầu tiên. | Bỏ tutorial (`MyTutorialService.DoneAll()`), tắt âm thanh, đưa game về trạng thái "đã qua đầu game". |
| `bool? IsGameReady()` | Liên tục trong lúc chờ boot. | `null` = để adapter tự quyết. Override khi adapter đoán sai (game còn màn loading riêng). Không tạo singleton khi kiểm tra. |
| `object GetFeatureData(string featureName)` | Trước khi smoke/UI audit/button sweep/`GameFlow.OpenFeature` mở một màn. | Data mẫu cho màn cần data (thiếu ⇒ NullReference khi mở). `null` = không truyền. |
| `Task DismissBlockingPopups(ctx)` | Sau boot và giữa các case (`ReturnToBaseline`). | Đóng popup tự bật (offer theo giờ, rating, level up) mà `smoke.dismissAtStartFeatures` chưa bắt. |
| `IEnumerable<string> ExtraExcludedFeatures()` | Khi liệt kê màn cho smoke/UI audit/button sweep. | Tên enum feature không mở độc lập được (bước tutorial, màn cần scene riêng). |

Hooks chỉ gọi API có sẵn của game, không sửa PlayerPrefs trực tiếp.

---

## 12. Viết adapter cho project không theo template EZG

Project theo template EZG **không cần** adapter — `EzgTemplateAdapter` tự nhận (xem dòng adapter trong cửa sổ;
tên type khác template ⇒ chỉnh mục `adapter` trong Settings thay vì viết adapter). Chỉ viết adapter khi kiến trúc
khác hẳn (UI manager, save, tiền tệ riêng).

1. Sinh khung: `Ezg.AutoTest.Editor.AutoTestScaffolder.CreateAdapterTemplate()` (qua `unity_execute_code` hoặc
   nút trong cửa sổ) → `<AutoTests>/<Product>GameAdapter.cs`: class kế thừa `GenericGameAdapter`, `Priority = 200`,
   mọi hàm có `// AI: TODO` giải thích phải trả gì. `Initialize` trả `false` cho tới khi triển khai (runner bỏ qua,
   dùng adapter khác) — **đổi thành `true`** khi xong ít nhất `IsGameReady`.
2. Triển khai theo thứ tự, mỗi nhóm xong thì bật flag tương ứng trong `Capabilities`:
   - **ReadyState**: `IsGameReady` (rời scene boot/loading, UI chính đã hiện, không khoá input), `DescribeState`.
   - **Features**: `GetFeatures` (cache trong Initialize, `Name` = tên enum/ID màn), `ShowFeature` (mở qua UI manager
     của game, **await tới khi mở xong**, tôn trọng `timeoutSeconds` + `ct`, trả root hoặc null), `CloseFeature`,
     `IsFeatureShowing`, `GetFeatureObject`, `GetOpenFeatures` (trả **cùng object** `AutoTestFeature` như `GetFeatures`).
   - **Economy**: `GetCurrencies` (bỏ tiền giả), `GetBalance`, `AddCurrency` (không animation/popup), `RemoveCurrency`
     (**false + không đổi số dư** khi thiếu tiền), `IsEnough`, `SetCurrency`, `BalanceType`. Thêm `Rewards`
     (`GrantReward`) và `PurchaseOffline` nếu game có.
   - **PlayerData**: `SaveAll`, **`GetSaveKeys`** (đủ mọi key PlayerPrefs của save — sandbox dựa vào đây để bảo vệ
     save thật của dev; gọi ở Edit mode nên chỉ tính từ type/hằng số), `ReloadPlayerData`.
   - **Config**: `GetConfigCollections`.
   - Tuỳ chọn: implement thêm `IFeaturePrefabResolver.ResolvePrefabName` để kiểm tra tĩnh phát hiện màn thiếu prefab.
3. Quy tắc: `Initialize` / `GetSaveKeys` chạy cả ở Edit mode — chỉ resolve type, không chạm singleton / scene;
   `IsGameReady` không được tạo singleton; mọi lỗi resolve ghi vào `_diagnostics` (hiện trong cửa sổ + report).
4. Kiểm tra: cửa sổ Auto Test hiện tên adapter + diagnostics → chạy `smoke` (boot + mở màn) → `economy` → sửa tới
   khi các case không còn Skip vì thiếu capability.

Adapter nằm trong asmdef AutoTests (hoặc Assembly-CSharp + guard) như kịch bản. Ép dùng một adapter cụ thể:
Settings `adapter.adapterType`.

---

## 13. Xử lý sự cố

| Triệu chứng | Nguyên nhân / cách xử lý |
|-------------|--------------------------|
| Kịch bản không hiện trong cửa sổ | Lỗi compile trong assembly AutoTests; class không `public` / `abstract` / thiếu constructor rỗng; thiếu attribute (hiện ở nhóm "Chưa phân loại"); file ngoài asmdef mà thiếu reference. |
| Type của package báo sai kiểu (CS0104 ambiguous) | Game có type global trùng tên với type của package — đặt `using Ezg.AutoTest;` **bên trong** khối namespace, hoặc ghi đầy đủ `Ezg.AutoTest.AutoTestFeature`. |
| Case Skipped "Cần Play mode." | Chạy kịch bản khi không ở Play — chạy qua cửa sổ/runner, không gọi tay ở Edit mode. |
| Lỗi Blocker "Game không sẵn sàng sau …s" | Boot kẹt (xem mô tả trạng thái trong message) hoặc adapter đoán ready sai ⇒ hooks `IsGameReady`, tăng `bootTimeoutSeconds`, chỉnh `bootScenePath`. |
| `WaitFor` trả null dù thấy object | Object inactive / alpha 0 / nằm ngoài màn hình (`mustBeVisible`), tên khác (clone "(Clone)"), nhiều object trùng tên ⇒ dùng `root` / đuôi đường dẫn. |
| `Click` false | Xem `IsClickable(go, out reason)`: bị che (popup chưa đóng, overlay), CanvasGroup chặn, nút disabled. |
| Build release lỗi compile vì `Ezg.AutoTest` | File test nằm ngoài asmdef AutoTests và thiếu `#if UNITY_EDITOR || EZG_AUTOTEST`. |
| Build test device lỗi `UnityEditor` | Kịch bản dùng API Editor — bọc `#if UNITY_EDITOR` + `RunOnDevice = false`. |
| Kịch bản pass lần đầu, lỗi lần sau / kịch bản khác lỗi lây | TearDown không khôi phục state. |
| Case "Quá thời gian" | Chờ điều kiện không bao giờ đúng, vòng lặp dài, hoặc kịch bản ôm quá nhiều luồng ⇒ tách nhỏ. |
| Error log của SDK làm case Lỗi | Thêm regex vào `general.ignoredLogPatterns` (Settings), không cap trong kịch bản. |
