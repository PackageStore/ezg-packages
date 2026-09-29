# Đưa module vào một project, nâng bản cũ

Ba trường hợp, survey cho biết đang ở trường hợp nào. Mỗi bước có điểm kiểm; không sang bước sau khi điểm kiểm chưa đạt.
Các bước trong Unity đều có bản batch (`reference/unity-batch.md`) để chạy trên bản chép khi user đang mở project.

## A. Project chưa có module

1. **Khảo sát, lập ghi chú.** `survey`, rồi `init`. Đọc `UIMotionProject.md` cùng user: xác nhận mục "Cần xác nhận", chỗ
   đóng screen, audio manager, folder quét. Sửa `UIMotionProject.json`, `check-rules` sạch.
   *Kiểm:* file luật chỉ có dòng đã chắc; DOTween có trong project (module cần để compile).
2. **Chọn chỗ đặt module** cùng user (theo cách project xếp folder code, vd `Assets/<X>/Modules/UIMotion`), rồi
   `install-module --to <đường dẫn>` (nguồn: folder module đi kèm skill, hoặc `--from`). Module là một folder: code, tài
   liệu, stub Odin (tự tắt khi có Odin), skill (`Skill~`, Unity bỏ qua).
   *Kiểm:* project đã có bộ stub Odin khác và không cài Odin → trùng class: xoá một bộ (survey báo bẫy `odin-stubs`).
3. **Mở Unity** (define TMP / UniTask / UIEffect / UI Extensions tự bật theo package). Compile sạch.
4. **Tools/UI Motion/Tạo asset mẫu** → RoleMap, Settings, 5 Profile vào folder dữ liệu (ngoài module; `dataFolder` của file
   luật, không ghi thì `Assets/Resources/UIMotion`).
5. **Tools/UI Motion/Áp luật riêng của project.** Console liệt kê từng thay đổi.
   *Kiểm:* Inspector RoleMap ghi "Đã áp".
6. **Cảm giác.** Project cũ đã có motion riêng (survey mục "Số đang dùng"): chỉnh Profile (5 trục + ghi đè) cho screen mới
   giống screen cũ; ghi số đã chọn vào Quyết định của MD. Game mới: chọn preset theo thể loại (quy chuẩn 2.1).
7. **Quét trước, gắn sau.** Tools/UI Motion/Quét và gắn motion… → Quét (không đổi gì) → đọc báo cáo cùng user, bỏ tick chỗ
   không muốn → Gắn. Báo cáo sai nhiều ở một loại object: sửa luật (JSON: class tự có motion, keyword, từ nhận prefab item
   / khuôn), áp lại, quét lại — không bỏ tick từng dòng cho một lỗi có quy luật.
   *Kiểm:* không gắn lên object thuộc class tự có motion; không gắn số đếm / icon trong prefab item; prefab hỏng được liệt kê riêng.
8. **Đóng screen.** Dev đổi chỗ đóng screen (UI manager trong MD) sang `UIMotion.SetActiveAnimated(go, false)`; screen tự có
   motion ẩn của project thì implement `IUIMotionHideable`. Không có manager: chạy game, đọc cảnh báo SetActive thẳng.
9. **Sfx.** Player kèm sẵn (kéo clip vào RoleMap) hoặc sink nối audio manager (`reference/sfx-sink.md`). Coi chừng tiếng kép.
10. **Gate.** Tools/UI Motion/Kiểm tra (gate)…: sửa bằng Gắn, hoặc Bỏ qua kèm lý do (ghi nợ). Sạch (hoặc chỉ còn nợ có lý
    do) thì Settings > Gate = Chặn build.
11. Ghi vào MD: bản module, ngày, số component đã gắn, nợ còn lại, việc giao người khác (prefab hỏng…).

## B. Project đã có module bản cũ (0.3 trở về trước)

Bản cũ để RoleMap / Settings / Profile TRONG folder module, và bảng mặc định có class của một project khác.

1. `survey`: xem "Asset nằm trong module", bản RoleMap, class trong RoleMap.
2. **Dời asset ra trước khi chép bản mới.** Trong Unity: Tools/UI Motion/Dời asset của project ra khỏi folder module (có
   ở bản mới). Chưa có bản mới trong project: `install-module` tự dời asset (kèm `.meta`, giữ GUID) ra `--data-to` /
   `dataFolder` / `Assets/Resources/UIMotion` rồi mới thay folder.
3. `init` (nếu chưa có file riêng). Dòng Component cũ trong RoleMap của project (có dòng lấy từ bảng mặc định cũ) vẫn còn —
   cập nhật RoleMap không bỏ dòng nào. `init` chép dòng mang class có trong project sang file luật (giữ ghi chú), còn dòng mang
   class không có trong project thì ghi vào `componentRulesRemove` để lần áp sau bỏ.
4. `install-module` (thay nguyên folder). Project có folder stub Odin riêng ngoài module (bản cũ dặn chép kèm) mà không cài
   Odin: xoá folder đó, module mới đã có stub bên trong.
5. Mở Unity → RoleMap: **Cập nhật theo bản module mới** → **Áp luật riêng của project** → quét → gate như mục A.

## C. Project mới

Như A, bỏ bước 6 phần "giống screen cũ": chọn preset theo thể loại. Đặt tên object theo keyword mặc định (quy chuẩn 3.3) thì
gần như không cần luật keyword riêng.

## Không làm

- Không sửa file trong folder module của project game; thay cả folder khi nâng bản.
- Không "Remove Missing Scripts" hàng loạt để gắn được prefab hỏng: báo người giữ prefab.
- Không đặt `ownMotion: true` cho cả nhóm class chỉ để gate xanh.
