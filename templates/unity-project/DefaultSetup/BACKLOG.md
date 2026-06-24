# Backlog

Tasks live as **individual files** in `backlog/{todo,in-progress,done}/`. This file is just the **index** — the agent reads only this file + the one task it picks, so tokens stay flat regardless of how many tasks accumulate.

## Cách dùng

- Agent đọc file này, lấy task **đầu tiên** trong mục `## TODO` (link đến file riêng)
- Agent đọc đúng 1 file task đó từ `backlog/todo/`
- Khi bắt đầu làm: di chuyển file `backlog/todo/<name>.md` → `backlog/in-progress/<name>.md` và cập nhật mục `## IN PROGRESS` ở đây
- Khi xong: di chuyển file `backlog/in-progress/<name>.md` → `backlog/done/<name>.md` và cập nhật cả 2 mục
- Format chi tiết: xem `backlog/_TEMPLATE.md`

## Quy tắc thứ tự trong TODO

- Task xếp theo **thứ tự thêm vào** (FIFO): task thêm trước ở trên, task thêm sau ở dưới — **KHÔNG** sắp xếp theo priority/rarity
- Nhãn `[PRIORITY]` (HIGH/MEDIUM/LOW) chỉ để **tham khảo**, KHÔNG ảnh hưởng vị trí trong hàng đợi
- NNN tăng dần theo thứ tự thêm; agent luôn lấy task **đầu tiên** (trên cùng) trong `## TODO`
- Filename `NNN-slug.md` chỉ để sort hiển thị trong file explorer; **thứ tự thực sự là thứ tự trong file này**

---

## TODO

- [HIGH] [L] [Refactor UIManager sang string key — bỏ EnumBase.Features dependency](backlog/todo/005-refactor-uimanager-string-key.md)

## IN PROGRESS

- (none)

## DONE

Xem `backlog/done/` — mỗi task đã hoàn thành là 1 file riêng có tóm tắt và link commit.
