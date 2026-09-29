---
trigger: always_on
---

# KHÔNG REFERENCE GAME KHÁC VÀO PROJECT

Không nhắc tên, không comment, không lưu ảnh/video của **bất kỳ game nào khác** vào project. Người viết
prompt, GDD hay TechSpec hay mượn game cùng thể loại làm mẫu ("làm shop giống game X") — ý đó được phép
dùng để hiểu yêu cầu, nhưng **thứ ghi vào repo phải trung tính**.

**Phạm vi — mọi thứ đi vào repo:** code, comment, tên class/biến/asset/folder, CSV, localize, prefab,
`GDD/`, `TechSpec/`, `Docs/`, task backlog, mockup (`.ui-spec.json`/HTML/PNG), commit message, prompt
gửi cho subagent (vì output của nó sẽ ghi vào repo).

**Cấm:**
1. **Tên:** tên tựa game, studio/publisher, nhân vật/IP đặc trưng của game bên thứ ba — trong text,
   comment, identifier hay tên file.
2. **Media:** ảnh chụp màn hình, video/GIF gameplay, audio, asset rip, art vẽ lại theo game khác — không
   lưu trong repo, kể cả thư mục tạm, `Docs/`, `GDD/`, thư mục mockup.
3. **So sánh:** "giống game X", "clone X", "như màn shop của X", "X-like" trong GDD/spec/task/comment.

**Làm thay:**
- Tả cơ chế bằng lời trung tính: "vòng lặp idle: thu → nâng cấp → mở trạm mới", "shop chia tab, pack gem
  giá leo thang" — không cần tên game vẫn đủ để implement.
- Số liệu `[BENCHMARK]`: nguồn ghi theo thể loại/thị trường ("mobile idle tycoon, D1 ~40%"), không tên tựa game.
- Input của user có tên game/ảnh game khác → dịch sang mô tả trung tính trước khi ghi. Ảnh tham khảo để
  ngoài repo (scratchpad, máy dev), không copy vào `Assets/`/`Docs/`/`GDD/`, không commit.
- Nhận ra repo đang có vi phạm → báo cho dev (file + dòng). Sửa text/comment được; **xoá file media thì hỏi trước**.
- **Không tạo denylist tên game trong repo** (chính cái list đã là vi phạm) — kiểm bằng reviewer, không bằng grep list.

**Không tính là vi phạm:**
- Engine/SDK/thư viện/plugin: Unity, DOTween, Odin, Firebase, AppLovin, asset pack mua trên Asset Store… — không phải game.
- Project nội bộ EZG gọi bằng **codename** (`sm00x`…) trong tài liệu dev (`CLAUDE.md`, `.claude/`,
  GDD/TechSpec khi cần ngữ cảnh port). Nhưng **tên thương mại** của game EZG khác không được lọt vào nội
  dung người chơi thấy (text UI, localize, email/subject, store metadata).

**Lý do:** rủi ro bản quyền/IP; store review có thể từ chối khi metadata hoặc nội dung nhắc app khác;
repo được chia sẻ (Feature Hub, package) nên reference nội bộ sẽ đi theo ra ngoài.
