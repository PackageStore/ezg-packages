# UI Motion — bắt đầu nhanh

Có UI là có motion và sfx. Motion đi theo **role** (Button, Popup, List…), không đi theo screen. Tool gắn component,
không phải người. Người bảo trì chỉ giữ 3 asset và một file luật riêng của project. Luật chi tiết: `UIMotion_QuyChuan.md`.
Thay đổi từng bản: `CHANGELOG.md`. Module dùng cho mọi project: không chứa gì riêng của project nào.

| Của module (một folder, chép đè khi nâng bản) | Của project (ngoài folder module) |
|---|---|
| Code, `Docs/`, `OdinStubs/`, `Skill~/ui-motion` (skill cho Claude) | Asset RoleMap / Settings / Profile trong `…/Resources/UIMotion` |
| | `ProjectSettings/UIMotionProject.json` — luật riêng: class tự có motion, keyword, folder quét, sink sfx |
| | `ProjectSettings/UIMotionProject.md` — ghi chú: audio manager, chỗ đóng screen, quyết định |
| | `ProjectSettings/UIMotionDebt.json` — nợ gate |

## Ba asset kiểm soát (tech art)

Menu **Tools/UI Motion/Tạo asset mẫu** (tạo trong folder dữ liệu của project, mặc định `Assets/Resources/UIMotion`), rồi
**Mở RoleMap / Mở Settings / Mở Profile đang dùng**.

| Asset | Chỉnh gì |
|---|---|
| **Profile Team** | Nhanh chậm, nảy mềm, mạnh nhẹ của cả game: 5 slider, tab Ghi đè, tab Giá trị suy ra |
| **RoleMap** | Role nào hiện / ẩn / phản hồi thế nào, kéo AudioClip cho từng sự kiện; component nào báo role gì; tên chứa chữ gì là role gì (có ô gõ thử tên) |
| **Settings** | Bật tắt tự gắn, folder quét, phạm vi gắn (nút, số, screen, list, slider, badge, timer, icon, ô nhập, loading, scrollbar, tiêu đề, trang trí), gate, reduce motion, sfx |

Sửa ở đây là mọi screen đổi theo, kể cả screen cũ đã gắn.

## Luật riêng của project

Class của project đã tự có motion (screen controller, nút, list animator…), thói quen đặt tên, folder UI, audio manager: khai
trong `ProjectSettings/UIMotionProject.json` (quy chuẩn mục 2.4), rồi **Tools/UI Motion/Áp luật riêng của project** (hoặc nút
trên Inspector RoleMap). Chưa có file: **Tools/UI Motion/Tạo file luật riêng mẫu**, hoặc nhờ Claude dùng skill `ui-motion`
khảo sát project và sinh file (kèm ghi chú `UIMotionProject.md`, mục cần xác nhận có bằng chứng).

## Xem thử mọi role: scene Role Sandbox

Có trong project phát triển module: **Tools/UI Motion/Dựng scene Role Sandbox**, mở scene `UIMotionRoleSandbox`, bấm Play.

- Trang **Screen giả**: top bar, counter, list, popup Shop, sheet Cài đặt — nút nối bằng OnClick, không code.
- Trang **Bảng role**: mỗi role một ô — vật mẫu thật, dòng RoleMap role đó đang dùng, nút Hiện / Ẩn / Thử. Sửa RoleMap hoặc
  Profile lúc đang Play là thấy ngay. Nút đổi profile và Reduce motion ở đầu trang.

## Screen mới

1. Ghép UI như bình thường.
2. Save prefab trong Prefab Mode: component motion còn thiếu tự gắn theo role, hiện trong Inspector, Ctrl+Z được.
3. Muốn khác mặc định: đổi dropdown trên component (Theo RoleMap = lấy theo bảng).
4. Nút đóng popup: Button OnClick kéo tới `UIMotionPanel.Hide`.
5. Nút **▶ Xem hiện / ▶ Xem ẩn** trên component chạy thử ngay trong Scene View, không cần Play. Lúc Play có thêm nút thử
   trên Card, ScreenFx, InputField, FlyTo, Title.

## Project cũ

1. Khảo sát project, lập file luật riêng + ghi chú (skill `ui-motion`, hoặc tự lập theo quy chuẩn mục 2.4). Người giữ
   project xác nhận các class "tự có motion" trước khi ghi luật.
2. Chép **một folder module** vào project (đặt đâu trong `Assets` cũng được). Đã có module bản cũ (0.3 trở về trước, asset
   nằm trong folder module): trong Unity bấm **Tools/UI Motion/Dời asset của project ra khỏi folder module** trước, rồi thay
   nguyên folder; xoá folder stub Odin riêng mà bản cũ dặn chép kèm.
3. Mở Editor: define TMP / UniTask / UIEffect / UI Extensions tự bật theo package có trong project (thiếu package nào thì
   chỉ mất tính năng dựa trên package đó). **Tools/UI Motion/Tạo asset mẫu** → **Áp luật riêng của project**.
4. Đã có module bản cũ: mở RoleMap, bấm **Cập nhật theo bản module mới** (Console cũng nhắc). Phần đã chỉnh tay giữ nguyên.
5. **Tools/UI Motion/Quét và gắn motion…** → 1. Quét (không đổi gì) → đọc báo cáo, bỏ tick dòng không muốn → 2. Gắn.
   Prefab báo **Prefab hỏng** (script mất, prefab lồng mất) thì Unity không cho lưu: sửa prefab rồi quét lại.
6. Dev đổi chỗ đóng screen sang `UIMotion.SetActiveAnimated(go, false)`. Không có UI manager thì chạy game, Console báo chỗ nào SetActive(false) thẳng.
7. Sfx: kéo clip vào RoleMap, hoặc dev viết sink nối audio manager trong code của project (`IUIMotionSfxSink`, chọn trong
   Settings; mẫu: `Docs/Samples/UIMotionSfxSink.cs.txt`).
8. **Tools/UI Motion/Kiểm tra (gate)…**: sửa bằng nút Gắn hoặc Bỏ qua kèm lý do. Sạch lỗi thì Settings > Gate = Chặn build.

Lúc chạy, `UIMotionAutoBind` tự gắn cho screen nào chưa được quét (không lưu vào prefab) — lưới an toàn.

## Ba việc tay, không cần chuyên môn

| Tình huống | Làm gì |
|---|---|
| Kiểm tra báo "chưa rõ role" (M-10) | Gắn `UI Motion Role (gắn tay)`, chọn role |
| Object này muốn khác mặc định | Đổi dropdown trên component đã gắn |
| Cố ý không motion | `UI Motion Role` = Không motion, ghi lý do |

## Cho dev

```csharp
UIMotion.SetActiveAnimated(screenGo, false, () => Destroy(screenGo)); // ẩn xong mới tắt
panel.Show(); panel.Hide(); panel.Toggle();                            // hoặc kéo vào OnClick
await panel.HideAsync();                                               // có UniTask
UIMotionSettings.ReduceMotionOverride = playerPrefersReducedMotion;    // tuỳ chọn trợ năng
UIMotionFeedback.Sink = new MyAudioSink();                             // audio manager riêng

UIMotionScreenFx.Main.Hit();                                           // rung + loé đỏ toàn màn
coinTarget.GetComponent<UIMotionFlyTo>().FlyFrom(chestRect);           // coin bay về đích, OnArrive mỗi đồng
UIMotionRecipes.OpenChest(chest, rewards, coinFly);                    // rung → bung → item pop → coin bay
inputField.GetComponent<UIMotionInputField>().ShowError();             // nhập sai: rung + đỏ
```

Code game vẫn làm như cũ: gán `text` (Counter, Badge, Timer tự chạy), đổi `sprite` (Icon tự swap), gán `slider.value`
(thanh tự đuổi theo), `SetActive(true)` toast (trượt vào; tự ẩn / chờ lượt / chạm để tắt là tuỳ chọn trên UIMotionToast).
Destroy object giữa lúc motion đang chạy vẫn an toàn: tween tự dừng theo object.

Batchmode / CI:

```
Unity -batchmode -projectPath . -executeMethod Assets._Project.Core.Modules.UIMotion.Editor.UIMotionBatch.SetupBatch
Unity -batchmode -projectPath . -executeMethod Assets._Project.Core.Modules.UIMotion.Editor.UIMotionBatch.MoveDataOutOfModuleBatch -quit
Unity -batchmode -projectPath . -executeMethod Assets._Project.Core.Modules.UIMotion.Editor.UIMotionBatch.ApplyProjectRulesBatch -quit
Unity -batchmode -projectPath . -executeMethod Assets._Project.Core.Modules.UIMotion.Editor.UIMotionBatch.ScanReportBatch -uimotionReport Logs/UIMotionScan.md -quit
Unity -batchmode -projectPath . -executeMethod Assets._Project.Core.Modules.UIMotion.Editor.UIMotionBatch.BindAllBatch -quit
Unity -batchmode -projectPath . -executeMethod Assets._Project.Core.Modules.UIMotion.Editor.UIMotionBatch.UpgradeRoleMapBatch -quit
Unity -batchmode -projectPath . -executeMethod Assets._Project.Core.Modules.UIMotion.Editor.UIMotionLint.RunCli -uimotionStrict
```

## Claude: skill `ui-motion`

Skill đi kèm module (`Skill~/ui-motion`): đọc quy chuẩn, khảo sát project, sinh file riêng của project, kiểm file luật, chép
module sang project khác. Cài cho máy (mở project nào cũng có): `node Skill~/ui-motion/scripts/uimotion.mjs install-skill`.
Skill không chứa thông tin riêng của project nào — mọi điều về project nằm trong file riêng của chính project đó.

## Giới hạn hiện tại (0.4.0)

- Hook save chỉ chạy khi save trong Prefab Mode. Prefab tạo bằng kéo từ scene / Apply override: chạy Quét và gắn.
- Screen bị `SetActive(false)` thẳng thì không có motion ẩn (chỉ có cảnh báo). Cần đổi chỗ gọi.
- Label số, icon, badge, slider trong list / prefab item không tự gắn (item có thể bị tái dùng cho dòng khác). Gắn tay nếu thật cần.
- Prefab hỏng (script mất, prefab lồng mất) không gắn được cho tới khi sửa prefab.
- Scrollbar, tiêu đề, trang trí không tự gắn trừ khi bật trong Settings (bật là screen cũ đổi cảm giác).
- Gắn clip vào RoleMap cho project mà code đã tự phát tiếng nút thì sẽ kêu hai lần — tắt tiếng cũ hoặc để trống clip.
