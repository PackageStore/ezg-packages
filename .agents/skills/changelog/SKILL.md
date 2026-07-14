---
name: changelog
description: Sinh changelog từ các thay đổi gần đây theo định dạng Added/Changed/Fixed, show cho user, rồi (nếu user đồng ý) prepend vào CHANGELOG.md phù hợp. Dùng khi user nói "cho tôi changelog", "changelog những gì vừa fix/làm", "generate changelog", "what changed", "ghi changelog".
---

# Changelog — Generate & Show

Sinh changelog dễ đọc cho công việc gần đây theo định dạng **Added / Changed / Fixed**, show cho user, và (khi user xác nhận) prepend một entry có ngày vào `CHANGELOG.md` phù hợp.

Đây là skill cấp repo cho maintainer của `ezg-packages` — KHÔNG nằm trong `DefaultSetup/`, không deploy cho game project.

---

## Định dạng output (PHẢI khớp chính xác)

```
**Added**
- <thứ vừa thêm> — <giải thích ngắn what/why>

**Changed**
- <thứ vừa đổi> — <giải thích ngắn what/why>

**Fixed**
- <thứ vừa sửa> — <giải thích ngắn what/why>
```

Quy tắc:
- Header in đậm `**Added**` / `**Changed**` / `**Fixed**` (KHÔNG dùng `### Added`).
- Bỏ hẳn section nào không có mục (không in header rỗng).
- Mỗi thay đổi 1 bullet; dùng em-dash `—` ngăn giữa chủ thể và phần giải thích.
- Mô tả **thật ngắn gọn, súc tích (tối đa 1 câu duy nhất)** theo ý đồ thay đổi (WHAT + WHY). Tuyệt đối không viết dài dòng. KHÔNG liệt kê "đã sửa file X.md".
- Gộp các edit nhỏ cùng mục đích thành 1 bullet.
- Trả lời cùng ngôn ngữ với request của user (mặc định tiếng Việt cho repo này).

Phân loại:
- **Added** — tính năng/file/khả năng mới chưa từng có.
- **Changed** — hành vi/cấu hình/cách dùng của thứ đã tồn tại bị thay đổi.
- **Fixed** — sửa lỗi, sai sót, thứ trước đó hỏng/sai.

---

## Pipeline

```
[1] SCOPE     → xác định phạm vi (toàn repo / 1 package / unity-template / range commit)
[2] GATHER    → thu thập thay đổi (git status + diff chưa commit + git log kể từ entry gần nhất)
[3] CLASSIFY  → quy mỗi thay đổi về Added / Changed / Fixed
[4] SHOW      → in changelog theo đúng định dạng trên
[5] PERSIST   → (hỏi trước) prepend entry có ngày vào CHANGELOG.md phù hợp
[6] DISCORD   → Tự động gửi changelog lên Discord thread
```

---

## STEP 1 — Scope

Suy ra phạm vi từ lời user. Nếu không rõ, mặc định: **mọi thay đổi kể từ entry changelog gần nhất** của khu vực liên quan.

Chọn `CHANGELOG.md` đích theo nơi thay đổi nằm:
- Thay đổi dưới `templates/unity-project/**` → `templates/unity-project/CHANGELOG.md`
- Thay đổi dưới `packages/<pkg>/**` → `packages/<pkg>/CHANGELOG.md`
- Khác / trải nhiều khu vực → `CHANGELOG.md` ở repo root (tạo nếu chưa có)

Nếu thay đổi trải nhiều khu vực có CHANGELOG riêng → hỏi user muốn ghi vào đâu (hoặc tách entry cho từng khu vực).

---

## STEP 2 — Gather changes

Chạy (không cần xin phép — chỉ đọc):

```bash
git status --short
git diff --stat                 # thay đổi chưa stage
git diff --cached --stat        # thay đổi đã stage
```

Lấy mốc "lần changelog trước" để biết phạm vi commit cần tổng hợp:
- Đọc entry trên cùng của CHANGELOG.md đích để lấy ngày/commit gần nhất.
- Nếu có mốc commit:
  ```bash
  git log <last>..HEAD --oneline
  ```
- Nếu không, hỏi user phạm vi (vd "kể từ commit nào" / "trong session này").

Đọc nội dung diff khi cần để mô tả chính xác **what + why**, không chỉ tên file.

---

## STEP 3 — Classify

Quy mỗi thay đổi về đúng 1 nhóm Added/Changed/Fixed theo định nghĩa ở trên. Gộp các edit cùng ý đồ. Bỏ thay đổi nhiễu (đổi whitespace, bump file rác, file tạm).

---

## STEP 4 — Show

In changelog ra cho user theo đúng định dạng output. Đây là phần "show cho tôi" — luôn làm bước này kể cả khi không persist.

---

## STEP 5 — Persist (hỏi trước)

Hỏi: *"Ghi entry này vào `<đường-dẫn CHANGELOG.md>` không?"*

Nếu đồng ý:
1. Đọc CHANGELOG.md đích (tạo mới với header `# Changelog` nếu chưa có).
2. Prepend một entry mới ngay dưới phần mô tả đầu file, **trên** các entry cũ:
   ```
   ## <YYYY-MM-DD>

   **Added**
   - ...
   ```
   Dùng ngày hiện tại (lấy từ context `currentDate`, KHÔNG đoán).
3. Nếu đã có entry cùng ngày → gộp bullet vào entry đó thay vì tạo header trùng.
4. Ghi file 1 lần (single Write/Edit).

Nếu CHANGELOG.md đích nằm trong `DefaultSetup/` (sẽ deploy R2) thì nhắc user là cần deploy lại; còn `templates/unity-project/CHANGELOG.md` và CHANGELOG của package thì không cần.

KHÔNG tự commit/push trừ khi user yêu cầu.

---

## STEP 6 — Discord

Sau khi xuất changelog (bước SHOW), **TỰ ĐỘNG** gửi nội dung changelog đó vào Discord thread bằng lệnh `curl`.
Không cần hỏi ý kiến user cho bước này, luôn tự động thực thi.

**Thông tin kết nối**:
- **Bot Token**: đọc từ biến môi trường `$DISCORD_BOT_TOKEN`. File này được commit lên GitHub — KHÔNG hardcode token vào đây.
- **Thread ID (Channel ID)**: `1518955197345435809`

**Lệnh thi hành**:
Dùng `jq` để escape JSON an toàn:
```bash
jq -n --arg content "NỘI DUNG CHANGELOG" '{content: $content}' | curl -X POST "https://discord.com/api/v10/channels/1518955197345435809/messages" \
     -H "Authorization: Bot $DISCORD_BOT_TOKEN" \
     -H "Content-Type: application/json" \
     -d @-
```

Nếu `$DISCORD_BOT_TOKEN` chưa set: bỏ qua bước này, báo user một dòng, KHÔNG coi là lỗi của cả pipeline.
