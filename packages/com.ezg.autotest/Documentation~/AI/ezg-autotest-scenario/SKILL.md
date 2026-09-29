---
name: ezg-autotest-scenario
description: Write, implement or fix a game-specific auto test scenario for the EZG Auto Test System (Unity package com.ezg.autotest) — scaffold the scenario file, study the feature code, implement user-flow steps with waits/checks/cleanup, compile-check and run it through Unity MCP, read the report and iterate until it passes or a real game bug is confirmed. Also covers writing the project's test hooks (skip tutorial, sample feature data, dismiss popups) and a custom game adapter for projects not built on the EZG template. Use when asked to "write/implement an auto test scenario", "viết kịch bản auto test", "kịch bản test cho tính năng X", "viết auto test cho màn X", "thêm test tự động cho luồng mua gói", "fix failing auto test scenario", "sửa kịch bản auto test đang fail", "add test hooks/adapter", "viết project hooks / adapter cho auto test". NOT for running only the built-in suites (smoke/static/ui-audit…) without writing code, and NOT for Unity Test Framework (NUnit) tests.
---

# EZG Auto Test — viết / sửa kịch bản test riêng của game

Triển khai một kịch bản gameplay cho suite **Kịch bản riêng** (`scenarios`) của EZG Auto Test System: một class
kế thừa `AutoTestScenario` mô phỏng **một luồng người chơi**, kiểm tra kết quả nghiệp vụ, dọn state, và phải
chạy được — không chỉ compile được.

**Tài liệu gốc (bắt buộc đọc):** `Packages/com.ezg.autotest/Documentation~/AI-SCENARIO-GUIDE.md`. Skill này là quy
trình; API, pattern, ví dụ đầy đủ nằm trong guide. Package cài từ registry (không embedded) ⇒ đường dẫn thật là
`Library/PackageCache/com.ezg.autotest@<version>/Documentation~/AI-SCENARIO-GUIDE.md`.

---

## STEP 0 — Đọc guide

Đọc hết `AI-SCENARIO-GUIDE.md` một lần (đặc biệt § 3 vòng đời, § 4 API, § 5 Check/Assert/severity, § 7
NÊN/KHÔNG NÊN, § 10 checklist). Đọc thêm luật của project nếu có (`CLAUDE.md`, `.claude/rules/`) — luật project
(output format, compile-check, không nhắc tên game khác…) vẫn áp dụng.

Xác định loại việc:

| Yêu cầu | Đi tới |
|---------|--------|
| Kịch bản mới cho tính năng/luồng X | STEP 1 → 6 |
| Sửa kịch bản đang fail | Chạy nó trước (STEP 5) để xem report, rồi STEP 2 → 6 |
| Viết project hooks | Guide § 11; sinh khung bằng `AutoTestScaffolder.CreateProjectHooks()`, rồi STEP 4 → 6 (chạy `smoke` để kiểm) |
| Viết adapter (project không theo template EZG) | Guide § 12; sinh khung bằng `AutoTestScaffolder.CreateAdapterTemplate()` |

## STEP 1 — Tìm hoặc tạo file kịch bản

1. Có sẵn file (user đưa đường dẫn, hoặc dev đã tạo bằng menu `Assets > Create > Ezg > Auto Test Scenario`) ⇒ dùng
   luôn. File mới tạo có `ctx.Skip("Kịch bản chưa được triển khai")` ở đầu `Run` và các comment `// AI:` hướng dẫn.
2. Chưa có ⇒ tạo qua Unity MCP `unity_execute_code`:

   ```csharp
   return Ezg.AutoTest.Editor.AutoTestScaffolder.CreateScenario(
       "Mua gói vàng bằng kim cương trong Shop",   // tên hiển thị, tiếng Việt, mô tả luồng
       "Shop",                                     // nhóm = thư mục con trong Scenarios/
       "Người chơi đủ kim cương mua gói vàng đầu tiên; kim cương trừ đúng giá, vàng cộng đúng.");
   ```

   Trả asset path (`Assets/_Project/AutoTests/Scenarios/Shop/MuaGoiVangBangKimCuongTrongShopScenario.cs`…). Hàm tự
   tạo thư mục AutoTests + asmdef `<Product>.AutoTests` (khi game nằm trong asmdef), không ghi đè file có sẵn.
   Muốn tên class gọn ⇒ truyền tên ngắn không dấu làm `name` rồi sửa `Name` trong attribute sau.
3. Không có Unity MCP ⇒ viết tay theo template trong guide § 3: đúng thư mục, bọc
   `#if UNITY_EDITOR || EZG_AUTOTEST`, `using Ezg.AutoTest;` đặt **trong** namespace. Thư mục chưa có asmdef mà game
   nằm trong asmdef ⇒ nhờ user chạy menu tạo kịch bản một lần (sinh asmdef), đừng tự đoán danh sách reference.

Một kịch bản = một luồng. Yêu cầu gồm nhiều luồng ⇒ tạo nhiều kịch bản.

## STEP 2 — Nghiên cứu code tính năng

Trước khi viết bước nào, biết chính xác game làm gì:

- **Codegraph trước** nếu project có `.codegraph/` (`codegraph_explore` với tên controller/service của feature);
  không có thì Grep/Read.
- Cần tìm: controller màn hình (tên prefab `screen_*`, tên nút/object người chơi bấm), static Service (hàm đọc
  trạng thái, hàm cheat `Cheat_*`), module player data (`PlayerDataManager.<Module>`), config CSV
  (`DataManager.<Collection>`) để tính **giá trị kỳ vọng**, event/UI flow sau hành động (popup nào hiện, màn nào mở).
- Tên object: mở prefab (file `.prefab` YAML, hoặc `unity_prefab_info` / `unity_gameobject_info` qua MCP) — đừng
  đoán tên nút.
- Ghi lại: điều kiện tiên quyết (unlock, tiền, level), trạng thái cần lưu để khôi phục, những gì phải kiểm tra
  sau mỗi hành động.

## STEP 3 — Triển khai

Theo guide § 6 (pattern) và § 8 (ví dụ):

- **SetUp**: `await GameFlow.EnsureReady(ctx);` → kiểm tra capability adapter / điều kiện (thiếu ⇒ `ctx.Skip("lý do")`)
  → **lưu giá trị gốc** → chuẩn bị state (thêm tiền, unlock, cheat).
- **Run**: mỗi hành động người chơi là `using (ctx.Step("<hành động tiếng Việt>")) { … }` — tên bước chính là các
  bước tái hiện QA đọc. Chờ bằng `ctx.Ui.WaitFor` / `ctx.WaitUntil` (không `WaitSeconds` dài, không
  `Task.Delay`). Bấm bằng `ctx.Ui.Click` và **kiểm tra kết quả trả về**. `ctx.Assert` khi bước sau không chạy nổi
  nếu sai; `ctx.Check` (truyền `severity:` có tên) khi vẫn chạy tiếp được. Giá trị kỳ vọng tính từ config/service,
  điền `expected`/`actual`. Chụp ảnh ở điểm then chốt.
- **TearDown**: khôi phục **đồng bộ** mọi state đã đổi (chịu được field null), cuối cùng
  `if (GameFlow.IsReady(ctx.Session)) await GameFlow.ReturnToBaseline(ctx);`.
- Xoá dòng `ctx.Skip("Kịch bản chưa được triển khai")` và mọi comment `// AI:`; giữ comment tiếng Việt giải thích
  nghiệp vụ.
- Không: `DateTime.Now`, `UIManager.Instance` trước khi ready, API `UnityEditor` (trừ khi guard + `RunOnDevice = false`),
  bấm IAP thật, sửa PlayerPrefs trực tiếp, hardcode số liệu config, nhắc tên game khác.

## STEP 4 — Compile check (Unity MCP)

1. `unity_list_instances` → chọn đúng instance của project (nhiều instance ⇒ `unity_select_instance`, giữ `port`).
   Không có Editor ⇒ ghi `compile-check: skipped (Unity MCP không kết nối)` và nói rõ kịch bản **chưa được chạy**.
2. `unity_execute_menu_item("Assets/Refresh")` (bắt buộc — đọc lỗi mà không refresh là đọc kết quả cũ).
3. Poll `unity_editor_state` tới khi hết compiling.
4. `unity_get_compilation_errors` severity `error` → sửa → lặp (tối đa 2 vòng; vẫn lỗi ⇒ báo `COMPILE_BLOCKED` kèm lỗi).

Lỗi hay gặp: type của package bị type global trùng tên của game che (vd enum `AutoTestFeature`) ⇒ `using Ezg.AutoTest;`
phải nằm trong namespace; thiếu using namespace của game; `ctx.Check(cond, "title", Severity.X)` (tham số 3 là
string) ⇒ dùng `severity: Severity.X`.

## STEP 5 — Chạy kịch bản và đọc report

1. Editor có thể đang được người/agent khác dùng: nếu có `unity_agents_list` thì xem trước; không vào/dừng Play khi
   người khác đang chạy.
2. Chạy qua `unity_execute_code`:

   ```csharp
   return Ezg.AutoTest.Editor.AutoTestRunner.Run(new[] { "scenarios" },
       new Ezg.AutoTest.Editor.AutoTestRunOptions { ScenarioFilter = "<TênClassKịchBản>" });
   ```

   `true` = đã bắt đầu. `false` = đang có lượt khác chạy / Editor đang compile / đang ở Play mode — đọc Console (log
   `[EZG AutoTest]`), xử lý rồi thử lại. `ScenarioFilter` khớp một phần với id case (tên class đầy đủ) hoặc tên hiển thị.

3. Chờ xong: poll `return Ezg.AutoTest.Editor.AutoTestRunner.IsRunning;` (cách vài giây) tới khi `false`. Vào Play có
   thể reload domain — lệnh MCP lỗi tạm thì chờ `unity_editor_state` ổn định rồi thử lại.
4. Lấy thư mục report: `return Ezg.AutoTest.Editor.AutoTestRunner.LastReportFolder;` — không có ⇒ thư mục mới nhất
   trong `<project>/AutoTestReports/`.
5. Đọc `summary.md`, rồi `report.json`: suite `scenarios` → case có `name` = tên trong attribute → `status`,
   `message`, bước `steps[]` bị Failed, `issues[]` (severity, title, expected, actual, stackTrace), `logs[]` loại
   Error/Exception, ảnh trong `screenshots/` (mở ảnh để xem UI lúc hỏng).
6. Phân loại theo guide § 9.4:
   - **Lỗi của test** (sai tên object, điều kiện chờ sai, thiếu dọn state, NullReference trong code test) ⇒ sửa, quay
     lại STEP 4.
   - **Bug của game** (exception trong code game, số liệu sai so với config đã xác nhận, nút bị overlay che) ⇒
     **không** nới kiểm tra / hạ severity để pass. Giữ kiểm tra, ghi lại bằng chứng, báo user ở STEP 6.
7. Pass rồi ⇒ **chạy lại lần 2** liên tiếp; lần 2 lỗi = TearDown chưa khôi phục đủ.

Runner API không tồn tại (package bản cũ) ⇒ mở cửa sổ `unity_execute_menu_item("Ezg/Auto test system")` và nhờ user
bấm chạy suite Kịch bản riêng, sau đó đọc report như bước 4–6.

## STEP 6 — Báo cáo

Theo format output của project nếu có (vd `.claude/rules/output-format.md`); không có thì báo ngắn:

- File đã tạo/sửa (đường dẫn tuyệt đối).
- Kết quả chạy: trạng thái kịch bản (Đạt / Lỗi / Bỏ qua), đường dẫn thư mục report, số lần chạy.
- Bug game phát hiện (nếu có): kịch bản + bước hỏng, expected / actual, stack trace rút gọn, đường dẫn ảnh — đủ để
  QA/dev log bug.
- Việc còn lại (vd kịch bản chưa chạy được vì không có Editor).

Không `git add` / commit trừ khi user yêu cầu.

---

## Checklist nhanh (đối chiếu guide § 10)

- [ ] Không còn `ctx.Skip("Kịch bản chưa được triển khai")` / comment `// AI:`.
- [ ] Tên, nhóm, mô tả tiếng Việt; `TimeoutSeconds` ≤ 120; một luồng.
- [ ] Bước = hành động người chơi; chờ có điều kiện; kết quả `Click` được kiểm tra.
- [ ] Kỳ vọng từ config/service; expected/actual; severity đúng thang.
- [ ] TearDown khôi phục đồng bộ + guard `GameFlow.IsReady`.
- [ ] Compile sạch; đã chạy, Passed (hoặc fail vì bug game đã báo); chạy lần 2 vẫn vậy.
