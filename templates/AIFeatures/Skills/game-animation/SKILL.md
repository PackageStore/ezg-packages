---
name: game-animation
description: Quy chuẩn Rigging và Animation 2D, 3D của team (GameAnimation_QuyChuan.md, module Game Animation), dùng cho bất kỳ project nào. Dùng khi làm, hỏi hoặc duyệt việc animation trong thế giới game (nhân vật, quái, prop, xe) như rig, skin, xương, clip, loop, đặt tên, xuất FBX từ Blender, import FBX / PSD / Aseprite, Mixamo, mocap, pack animation mua sẵn, Animator Controller, blend tree, frame data, hitstop, event, IK, kể cả khi user không nhắc tới quy chuẩn. Không dùng cho UI (nút, popup, màn hình): đó là việc của UI Motion.
---

# Game Animation: làm theo quy chuẩn

Luật nằm trong `GameAnimation_QuyChuan.md`, không nằm ở đây. File này chỉ nói cách đọc và cách kiểm. Luật đổi thì sửa
file quy chuẩn, không sửa skill.

Skill này dùng cho mọi project và không chứa thông tin riêng của project nào. Điều chỉ đúng cho một game (preset cảm giác
đã chọn, tên xương theo 5.2 hay Mixamo, cỡ nhân vật, bộ vũ khí, brief từng clip, quyết định lệch quy chuẩn) nằm trong
file của chính game đó: xem mục 2. Không dùng trí nhớ về game khác: tên, số, quyết định của game A sai ở game B.

## 1. Tìm và đọc

1. Glob `**/GameAnimation/Docs/GameAnimation_QuyChuan.md` trong project. Không thấy: skill nằm trong `Skill~` của module
   thì đọc ở module nguồn, `<skill>/../../Docs/` (`<skill>` là folder chứa file này). Vẫn không thấy thì dừng và báo
   user là project chưa có module Game Animation (cài gói `.unitypackage` của module hoặc chép folder module), không làm
   theo trí nhớ.
2. Đọc từ đầu tới hết mục 0: phiên bản, cách hiểu các con số (mặc định, *(đề xuất)*, *(đo)*), bảng "Bạn là / Đọc
   trước", nguyên tắc gốc.
3. Grep `^#{2,3} ` trong file để lấy danh sách mục kèm số dòng. Chọn **mọi** mục dính tới việc, không chỉ mục trùng tên
   việc: một clip dính cả tên, fps và độ dài, loop, tại chỗ, import, event. Đọc các mục đó bằng Read với offset và limit.
4. Gặp dẫn chéo ("theo 5.6", "mục 3.1") mà mục đó ảnh hưởng tới việc đang làm thì đọc luôn.
5. Việc lớn (duyệt cả một nhân vật, cả controller, cả pack) thì đọc hết file.
6. Cần lý do của một luật: `GameAnimation_NguyenLy.md`. Việc dính pack mua sẵn: `GameAnimation_PackExplosiveLLC.md`.
   Cả hai cùng thư mục với quy chuẩn.

## 2. Điều riêng của game

- Đọc brief của game (quy chuẩn 9.2) và các quyết định đã ghi, nếu project có. Project đã có chỗ để brief thì dùng chỗ
  đó; chưa có thì đề xuất `ProjectSettings/GameAnimationProject.md` (preset cảm giác 2.1, tên xương, cỡ nhân vật, bộ vũ
  khí, chỗ để spec rigkit của từng nhân vật, brief clip, quyết định lệch quy chuẩn kèm ngày và lý do), hỏi user rồi
  mới tạo.
- Điều mới về game (user chốt một lựa chọn, lệch quy chuẩn có lý do) ghi vào file đó. Không ghi vào skill, cũng không
  ghi vào folder module: module thay nguyên folder khi nâng bản.

## 3. Làm

- Việc là rig, skin, pose hay animate thật trong Blender: dùng kèm skill `blender-rig-animate` (nếu đã cài) để làm và đo.
  Skill này vẫn lo phần đọc và áp quy chuẩn.
- Áp đúng luật đã đọc, kèm số mục ("theo 7.2") để user dò lại được.
- Số ghi *(đề xuất)* thì nói là đề xuất.
- Việc dính câu hỏi còn mở (mục "Còn mở"): nêu các lựa chọn, để user chốt.
- Muốn làm khác quy chuẩn: nói lệch ở mục nào và vì sao, để user quyết. Không lặng lẽ làm khác.
- Thấy quy chuẩn tự mâu thuẫn hoặc thiếu: nói ra, không tự chọn một bên.

## 4. Trước khi trả lời xong

- Soát phần khớp với việc trong mục Định nghĩa "xong" và bảng rule gate. Thiếu gì thì bổ sung vào câu trả lời.
- Cuối câu trả lời ghi một dòng: phiên bản quy chuẩn, các mục đã áp, file riêng của game đã đọc / sửa.
