---
name: game-vfx
description: Quy chuẩn VFX 2D, 3D cho game mobile của team (GameVFX_QuyChuan.md, module Game VFX) và thư viện hiệu ứng dùng chung của module (FXEffect, shader EZG/VFX/Particle), dùng cho bất kỳ project Unity nào. Dùng khi làm, hỏi hoặc duyệt hiệu ứng hạt, flipbook, shader VFX trong thế giới game (trúng đòn, chém, đạn, nổ, vùng, cảnh báo, buff, trạng thái, xuất hiện, chết, môi trường) hoặc hạt trên UI canvas (thưởng, mở rương, UIParticle); ParticleSystem, Max Particles, stop action, pool, culling, giờ game / giờ thật, sorting layer, texture, material, additive, bloom, overdraw, ngân sách mobile theo cấp và tier máy, tên FX_, pack VFX mua sẵn; lấy hiệu ứng có sẵn của thư viện; quét VFX cả project tìm nợ; kể cả khi user không nhắc tới quy chuẩn. English triggers: "make a hit VFX", "optimize particles for mobile", "review this VFX prefab". Không dùng cho tween, rung, loé màn hình, số sát thương của UI (UI Motion) hay animation nhân vật, hitstop (Game Animation).
---

# Game VFX: làm theo quy chuẩn

Luật nằm trong `GameVFX_QuyChuan.md`; hiệu ứng có sẵn và cách dùng nằm trong `GameVFX_ThuVien.md`. File này chỉ nói cách
đọc, cách làm, cách kiểm. Luật đổi thì sửa quy chuẩn, không sửa skill.

Skill này dùng cho mọi project và không chứa thông tin riêng của project nào. Điều chỉ đúng cho một game (bảng sorting layer
và order, màu phe, shader được dùng, thư viện UI particle đã chọn, máy thử theo tier, đường spawn và pool, quyết định lệch
quy chuẩn) nằm trong file của chính game đó: xem mục 2. Không dùng trí nhớ về game khác: tên, số, quyết định của game A sai
ở game B.

`<skill>` dưới đây là folder chứa file này.

## 1. Tìm và đọc

1. Glob `**/GameVFX/Docs/GameVFX_QuyChuan.md` trong project: có thì đọc bản đó (đúng bản module của project). Không
   thấy: đọc bản đi kèm skill, `<skill>/docs/GameVFX_QuyChuan.md` (cùng bản với skill, không cần cài module chỉ để đọc
   luật). `docs/` là bản chép `Docs` của module, không sửa ở đây. Vẫn không thấy thì dừng và báo user, không làm theo trí
   nhớ. Cần `FXEffect`, shader chung hay hiệu ứng của thư viện mà project chưa có module: gói `GameVFX` ở tab Unity
   Packages của Feature Hub (chỉ phần lõi, xem mục 3).
2. Đọc từ đầu tới hết mục 0: phiên bản, cách hiểu các con số (mặc định, *(đề xuất)*, *(đo)*), bảng "Bạn là / Đọc trước",
   bảy nguyên tắc gốc.
3. Grep `^#{2,3} ` trong file để lấy danh sách mục kèm số dòng. Chọn **mọi** mục dính tới việc, không chỉ mục trùng tên
   việc: một hiệu ứng trúng đòn dính cấp (2.1), yếu tố chính / phụ (2.2), timing (3.1), giờ game (3.4), texture (4), màu
   (5), trần theo cấp (6.2), prefab và ParticleSystem (7.1–7.3), tên (8.2), checklist (9.3). Đọc các mục đó bằng Read với
   offset và limit.
4. Gặp dẫn chéo ("theo 6.2", "mục 3.4") mà mục đó ảnh hưởng tới việc đang làm thì đọc luôn.
5. Việc lớn (duyệt cả bộ hiệu ứng của một game, đưa module vào game, đặt ngân sách cả game) thì đọc hết file.
6. Cần lý do của một luật: `GameVFX_NguyenLy.md`. Hiệu ứng có sẵn, `FXEffect`, pool, UIParticle: `GameVFX_ThuVien.md`
   (ảnh xem trước của từng hiệu ứng ở `Previews~/<key>.jpg` cạnh file, Read xem được). Cả hai cùng thư mục với quy chuẩn.

## 2. Điều riêng của game

- Đọc brief VFX của game (quy chuẩn 9.2) và các quyết định đã ghi, nếu project có. Project đã có chỗ để brief thì dùng chỗ
  đó; chưa có thì đề xuất `ProjectSettings/GameVFXProject.md` (bảng sorting layer và order, màu phe, shader được dùng, thư
  viện UI particle, máy thử theo tier, đường spawn và pool, quyết định lệch quy chuẩn kèm ngày và lý do), hỏi user rồi mới
  tạo.
- Điều mới về game (user chốt một lựa chọn, lệch quy chuẩn có lý do) ghi vào file đó. Không ghi vào skill, cũng không ghi
  vào folder module: module thay nguyên folder khi nâng bản.

## 3. Làm

- Hiệu ứng mới: chốt cấp trước (2.1, GD xếp), rồi mới tới timing, trần, hình, màu. Ghi cấp đã chọn vào câu trả lời.
- Xem thư viện trước khi làm mới: danh mục ở `GameVFX_ThuVien.md` mục 3 (nhóm, cấp, dùng cho 2D / 3D, ảnh). Có hiệu ứng
  gần đúng thì dùng thẳng, hoặc chép prefab sang folder VFX của game rồi sửa (tên theo 8.2, material mới nếu đổi màu). Không
  sửa prefab, material, texture trong folder module.
- Project chỉ có gói lõi (có `Runtime/FXEffect.cs` nhưng không có `Library/World`, `Library/UI`): prefab của thư viện nằm ở
  repo phát triển module, không có trên Feature Hub vì chứa file của pack mua. Nói với user, không tự tìm nguồn khác.
- Code: mỗi hiệu ứng là một prefab có `FXEffect` trên root; phát bằng bật object hoặc `Play()`, pool thu lại khi `Finished`,
  không hẹn giờ tắt (quy chuẩn 6.8, 7.8; mẫu `ObjectPool` ở `GameVFX_ThuVien.md` mục 2). Game có sẵn đường spawn riêng thì
  nối vào đường đó, không viết đường thứ hai.
- Áp đúng luật đã đọc, kèm số mục ("theo 6.2", "gate V-3") để user dò lại được. Số ghi *(đề xuất)* thì nói là đề xuất.
- Việc dính câu hỏi còn mở (12.2): nêu các lựa chọn, để user chốt.
- Muốn làm khác quy chuẩn: nói lệch ở mục nào và vì sao, để user quyết. Không lặng lẽ làm khác. Thấy quy chuẩn tự mâu thuẫn
  hoặc thiếu: nói ra, không tự chọn một bên.

## 4. Quét VFX của cả project

Chỉ đọc file (chạy được khi Unity đang mở project), cần Node 18+:

```bash
node <skill>/scripts/vfx_scan.mjs <gốc project> <thư mục kết quả> --packs "<đoạn đường dẫn folder pack>,..."
node <skill>/scripts/report.cjs <thư mục kết quả>
node <skill>/scripts/tex_report.cjs <thư mục kết quả>
```

- `--packs`: đoạn đường dẫn (chữ thường) của các folder pack mua sẵn trong project, để tách hiệu ứng của pack khỏi hiệu ứng
  của game. Không truyền thì chỉ nhận các folder `Plugins`, `ThirdParty`, `Samples`, `Asset Store`, `Demo`, `Examples`.
- "Vào build" = đi được theo GUID từ scene đang bật trong Build Settings và folder `Resources`: có thể nạp, chưa chứng minh
  game gọi tới.
- Ghi `effects.csv` (mỗi hiệu ứng một dòng), `systems.csv`, `materials.csv`, `textures.csv`, `summary.json`. Project cỡ
  5000–6500 prefab mất khoảng 1 phút; project rất lớn chạy `node --max-old-space-size=8192`.
- Đọc số theo luật: `maxParticles` để 1000 (7.2, gate V-3), stop action None (6.8, V-10), hiệu ứng gameplay chạy giờ thật
  (3.4, V-13), Always Simulate (6.5, V-11), prewarm (7.2, V-12), sorting layer `Default` (7.3, V-14), texture nguồn > 2048
  hoặc không override Android (4.2, 4.5, V-5), material thiếu (V-6). Báo là ứng viên kèm đường dẫn prefab, không phải kết
  luận.

## 5. Trước khi trả lời xong

- Soát phần khớp với việc trong checklist duyệt (9.3), gate (10) và Định nghĩa "xong" (11). Thiếu gì thì bổ sung vào câu
  trả lời.
- Cuối câu trả lời ghi một dòng: phiên bản quy chuẩn, các mục đã áp, file riêng của game đã đọc / sửa.
