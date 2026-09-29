# EZG Auto Test System (`com.ezg.autotest`)

Hệ thống auto test dùng chung cho mọi game Unity dựng trên **template EZG** (và cả project kiến trúc khác
qua adapter tự viết). Một cửa sổ Editor chạy được từ kiểm tra tĩnh asset/cấu hình, smoke test mở mọi màn
hình, audit UI, bấm thử mọi nút, monkey stress, logic tiền tệ, đo hiệu năng, so ảnh với baseline, tới chạy
trên device thật qua adb và các **kịch bản riêng** của từng game. Mỗi lần chạy xuất báo cáo HTML/JSON/JUnit/CSV
cho QA và CI.

- Mở: **Ezg > Auto test system** (`Ctrl+Shift+T` / `Cmd+Shift+T`).
- Cấu hình: **Project Settings > Ezg > Auto Test** (lưu ở `ProjectSettings/EZGAutoTestSettings.json`,
  commit vào git để cả team dùng chung).
- Báo cáo: `<project>/AutoTestReports/<runId>/`.

## Các suite

| Suite (id) | Kiểm tra gì | Chạy ở đâu | Sửa dữ liệu người chơi |
|------------|-------------|------------|:----------------------:|
| **Kiểm tra tĩnh** (`static`) | Missing script / reference trong prefab + scene, material hỏng (magenta) / shader lỗi, texture & audio quá nặng, asset quá lớn, CSV lệch cột / id trùng, localize thiếu key hoặc lệch placeholder, Player Settings cho release (target/min API Android, iOS min version, define cấm), GUID trùng, luật quét code (DateTime.Now, async void, secret hardcode…). | Edit mode (không vào Play) | Không |
| **Smoke PlayMode** (`smoke`) | Game boot được (thời gian boot, lỗi log khi boot), từng màn hình mở + đóng được không lỗi, màn mở chậm, load được mọi scene trong Build Settings, ngân sách error/exception log. | Play mode (Editor + device) | Có (sandbox) |
| **UI Audit** (`ui-audit`) | Mở từng màn và soi: vùng chạm quá nhỏ, text tràn khung, Image thiếu sprite, object nằm ngoài màn hình, lấn safe area, nút bị object khác che raycast, lộ key localize (`txt_buy`…), nút "chết" không có listener. | Play mode (Editor + device) | Có (sandbox) |
| **Button Sweep** (`button-sweep`) | Mở từng màn và bấm lần lượt từng nút (trừ blacklist mua thật / xoá data / mở link), bắt exception + error log sau mỗi lần bấm, đóng màn mới mở ra để quay về. | Play mode | Có (sandbox) |
| **Monkey / Stress** (`monkey`) | Bấm / kéo ngẫu nhiên trong N giây (seed ghi lại để tái hiện), bắt crash, exception, treo frame. | Play mode | Có (sandbox) |
| **Logic / Economy** (`economy`) | Cộng / trừ tiền khứ hồi, trừ quá số dư, tràn số, save → load giữ số dư, phát thưởng, mua bằng tiền mềm, config CSV không âm / id không trùng. | Play mode (Editor + device) | Có (sandbox) |
| **Performance** (`performance`) | FPS (theo FPS mục tiêu của game), frame time P50/P95/P99, số lần giật (hitch), GC alloc mỗi frame, bộ nhớ, draw call / SetPass ở màn chính; chi phí mở từng màn (ms, GC, frame spike); mở/đóng lặp để phát hiện rò object/bộ nhớ. | Play mode (Editor + device) | Có (sandbox) |
| **Visual Regression** (`visual`) | Chụp từng màn và so pixel với ảnh baseline trong `AutoTestBaselines/` (bỏ qua vùng đồng hồ / số nhảy liên tục), vượt ngưỡng % pixel khác ⇒ lỗi kèm ảnh diff. | Play mode | Có (sandbox) |
| **Device / E2E** (`device`) | Build APK test (define `EZG_AUTOTEST`), cài qua adb, khởi động, chạy các suite runtime trên máy thật, kéo report + logcat về, đo cold start. | Editor điều phối device thật | Trên device |
| **Kịch bản riêng** (`scenarios`) | Kịch bản gameplay đặc thù từng game (mua gói, nâng cấp trạm, nhận thưởng…) do dev/AI viết trong `Assets/_Project/AutoTests/`. | Play mode (Editor + device) | Có (sandbox) |

## Bắt đầu nhanh

1. Mở **Ezg > Auto test system**. Cột trái là danh sách suite; phần thông tin adapter cho biết hệ thống đã
   nhận ra kiến trúc game chưa (vd *EZG Template: capabilities = ReadyState, Features, Economy…*).
2. Tick các suite muốn chạy (lần đầu nên chọn `static` + `smoke`) rồi bấm **Chạy mục đã chọn** (hoặc **Chạy tất cả**). Suite Play mode tự vào
   Play, chờ game boot, chạy xong tự thoát Play. Bấm **Dừng** bất cứ lúc nào — report vẫn được ghi.
3. Xong lượt, `report.html` tự mở (tắt được trong Settings). Đọc **điểm sức khoẻ** (0–100), các case Lỗi /
   Crash, từng issue kèm bước tái hiện + ảnh chụp. Nút **Sao chép bug** copy sẵn nội dung để dán vào bug tracker;
   `issues.csv` import thẳng vào bảng tính / bug tracker.

## Chạy từ dòng lệnh / CI

```bash
Unity -batchmode -projectPath <project> \
  -executeMethod Ezg.AutoTest.Editor.AutoTestCli.Run \
  -autotestSuites static,smoke -autotestOut <thư-mục-report> -autotestFailOn major \
  -logFile -
```

**Không** truyền `-quit` — CLI tự thoát Editor khi chạy xong. Exit code: `0` đạt, `2` có lỗi ≥ ngưỡng
`-autotestFailOn`, `3` lỗi runner / quá thời gian, `4` tham số sai. Mẫu job GitLab + lịch chạy đề xuất:
[Documentation~/CI.md](Documentation~/CI.md).

## Cài vào project khác

**Cách 1 — registry Easygoing (khuyên dùng).** Thêm scoped registry (project EZG thường đã có sẵn) và dependency
vào `Packages/manifest.json`:

```json
{
  "scopedRegistries": [
    {
      "name": "Easygoing",
      "url": "https://upm-registry-worker.developer-a1f.workers.dev",
      "scopes": ["com.ezg"]
    }
  ],
  "dependencies": {
    "com.ezg.autotest": "0.1.1"
  }
}
```

**Cách 2 — copy thư mục.** Copy nguyên thư mục `com.ezg.autotest` vào `Packages/` của project (embedded
package). Tiện khi cần sửa package tại chỗ; nhớ đồng bộ lại lên registry.

Sau khi cài:

1. Mở cửa sổ, kiểm tra dòng adapter. Project theo template EZG tự dùng `EzgTemplateAdapter`. Project kiến
   trúc khác chỉ có adapter chung (scene + EventSystem) — viết adapter riêng theo
   [AI-SCENARIO-GUIDE § Adapter](Documentation~/AI-SCENARIO-GUIDE.md#12-viết-adapter-cho-project-không-theo-template-ezg).
2. Chạy `static` + `smoke` một lượt, thêm các lỗi "biết rồi / không phải lỗi" vào danh sách loại trừ trong
   Settings (xem FAQ).
3. (Tuỳ chọn) Import sample *Example Scenarios* trong Package Manager để xem kịch bản mẫu.

## Yêu cầu

- Unity **6000.0** trở lên (đã dùng trên 6000.2).
- UGUI (`com.unity.ugui` 2.0.0, gồm TextMeshPro). UI Toolkit runtime chưa được driver hỗ trợ.
- Suite `device`: Android SDK (adb) — dùng SDK Unity cài kèm hoặc chỉ đường dẫn trong Settings; máy Android
  bật USB debugging.
- Không phụ thuộc UniTask / Input System — package dùng `Task` + bộ đếm frame riêng nên chạy ở mọi project.
- Assembly `Ezg.AutoTest` có define constraint `UNITY_EDITOR || EZG_AUTOTEST`: **không bao giờ lọt vào build
  release**. Define `EZG_AUTOTEST` chỉ được thêm vào đúng build test của suite `device` (qua
  `extraScriptingDefines`), không sửa Player Settings.

## Viết kịch bản riêng cho game

- Menu **Assets > Create > Ezg > Auto Test Scenario** → nhập tên / nhóm / mô tả → file sinh trong
  `Assets/_Project/AutoTests/Scenarios/<Nhóm>/`, kèm asmdef tham chiếu sẵn assembly game. Prompt cho AI được
  copy vào clipboard — dán cho Claude Code là nó biết phải làm gì.
- Hướng dẫn cho dev + AI agent: [Documentation~/AI-SCENARIO-GUIDE.md](Documentation~/AI-SCENARIO-GUIDE.md).
- Skill Claude Code: `Documentation~/AI/ezg-autotest-scenario/SKILL.md` (cửa sổ tạo kịch bản tự cài vào
  `.claude/skills/` khi project có thư mục `.claude`).

## FAQ

**Test có làm mất save của mình không?**
Không, nếu để mặc định `sandboxPlayerData = true`. Trước mỗi suite có sửa dữ liệu người chơi, runner sao lưu
các key PlayerPrefs của save (adapter tự biết key của từng module `DataPlayerBase`, cộng `extraSaveKeys` trong
Settings) và khôi phục khi suite xong — kể cả khi bị Dừng. Bản sao lưu được ghi ra đĩa trước khi vào Play:
Editor crash / bị kill giữa chừng thì lần mở Editor sau hệ thống tự khôi phục. Game lưu save ngoài PlayerPrefs
(file riêng, cloud) ⇒ tự thêm cơ chế sao lưu trong project hooks, hoặc đừng chạy trên máy có save quan trọng.

**Console đang bật "Error Pause" thì sao?**
Error Pause làm Editor pause ở lỗi đầu tiên và test treo. Mặc định `autoUnpause = true` tự bỏ pause và lỗi đó
vẫn được ghi vào report. Tắt tuỳ chọn này nếu muốn tự dừng lại soi lỗi.

**Chạy batchmode trên CI thì ảnh chụp đen / không có ảnh?**
`-nographics` không có GPU ⇒ không chụp được (case vẫn chạy, chỉ thiếu ảnh). Suite Play mode trên CI không
truyền `-nographics`. Một số máy build headless vẫn cho ảnh đen vì không có Game view render — Visual
Regression nên chạy trên runner có màn hình (hoặc chạy tay trong Editor). Xem [CI.md](Documentation~/CI.md).

**Số đo Performance trong Editor có đáng tin không?**
Chỉ để **so tương đối** giữa các lượt chạy trên cùng máy (phát hiện regression). Editor có overhead lớn,
không đại diện cho máy thật. Ngưỡng tuyệt đối (FPS, bộ nhớ) chỉ có ý nghĩa khi chạy suite `performance` qua
`device`.

**Báo lỗi sai (false positive) thì làm sao?**
Không sửa code test — thêm vào danh sách loại trừ trong Project Settings > Ezg > Auto Test:
`smoke.excludedFeatures` (màn không mở độc lập được), `general.ignoredLogPatterns` (log nhiễu của SDK),
`uiAudit.ignoredObjectPatterns`, `buttonSweep.blacklistPatterns`, `visual.maskObjectPatterns`,
`staticCheck.excludeFolders`, hoặc tắt riêng case (`disabledCases`). Project cần logic loại trừ riêng ⇒ project
hooks `ExtraExcludedFeatures()`.

**Có cần Unity Test Framework (NUnit) không?**
Không. Hệ thống độc lập với Test Runner; dùng song song được.

## Tài liệu

| Tài liệu | Cho ai |
|----------|--------|
| [Documentation~/index.md](Documentation~/index.md) | QA + dev: chi tiết từng suite, severity, cách tính trạng thái & điểm, đọc report, sandbox, toàn bộ Settings, quy trình QA. |
| [Documentation~/AI-SCENARIO-GUIDE.md](Documentation~/AI-SCENARIO-GUIDE.md) | Dev + AI agent viết kịch bản riêng, hooks, adapter. |
| [Documentation~/CI.md](Documentation~/CI.md) | DevOps: job GitLab CI, exit code, lịch chạy, Firebase Test Lab. |
| [CHANGELOG.md](CHANGELOG.md) | Lịch sử phiên bản. |
