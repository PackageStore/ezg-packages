# Example Scenarios — kịch bản mẫu

Ba file minh hoạ API viết kịch bản riêng của **EZG Auto Test System**. Chỉ dùng API chung của package
(không gọi code game) nên import vào project nào cũng compile được.

| File | Minh hoạ |
|------|----------|
| `ExampleShopScenario.cs` | Mở màn theo tên enum feature (`EzgTemplateAdapter.FindFeature`), đo thời gian mở (`ctx.Metric`), kiểm tra nút hiển thị có bấm được không (`ctx.Ui.IsClickable`), chụp màn hình, đóng màn, dọn trong `TearDown`. Adapter không phải template EZG ⇒ tự `Skip`. |
| `ExampleEconomyScenario.cs` | Cộng / trừ / trừ quá số dư qua `ctx.Game` (chạy với mọi adapter có capability `Economy`), `ctx.Check` kèm expected/actual, lưu số dư gốc trong `SetUp` và khôi phục trong `TearDown`. |
| `ExampleProjectHooks.cs` | Khung `AutoTestProjectHooks` (bỏ tutorial, data mẫu khi mở màn, đóng popup, loại feature). Class để `abstract` nên runner không dùng — copy sang project rồi bỏ `abstract`. |

## Import

Window > Package Manager > **EZG Auto Test System** > tab *Samples* > **Import** "Example Scenarios".
Unity copy vào `Assets/Samples/EZG Auto Test System/<version>/Example Scenarios/`.

Hai kịch bản mẫu hiện trong suite **Kịch bản riêng** (nhóm "Mẫu") của cửa sổ `Ezg > Auto test system`.

## Lưu ý

- Mọi file bọc `#if UNITY_EDITOR || EZG_AUTOTEST` — assembly `Ezg.AutoTest` chỉ tồn tại trong Editor
  và build test (define `EZG_AUTOTEST`), build release không có nó.
- Sample nằm trong `Assembly-CSharp` nên **không gọi được code game nằm trong asmdef**. Kịch bản thật của
  game nên tạo bằng menu `Assets > Create > Ezg > Auto Test Scenario` — file sinh ra nằm trong
  `Assets/_Project/AutoTests/` với asmdef tham chiếu sẵn assembly game.
- Xoá thư mục sample khi đã hiểu cách viết — hai kịch bản mẫu sẽ chạy mỗi lần chạy suite *Kịch bản riêng*.
- Hướng dẫn đầy đủ (kể cả cho AI agent): `Packages/com.ezg.autotest/Documentation~/AI-SCENARIO-GUIDE.md`.
