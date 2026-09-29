---
name: auto-clear
description: Push phần việc của session qua /push-in-session rồi tự /clear chính session Claude Code đang chạy ngay sau khi lượt trả lời kết thúc — push xong mới bật cờ cho đúng pane iTerm2 (macOS) / console Windows Terminal (Windows, thử nghiệm), Stop hook đọc cờ và gõ "/clear" + Enter vào đúng pane đó. Dùng khi user nói "/auto-clear", "xong việc thì tự clear", "clear sau khi xong", hoặc prompt/goal có bước "/clear" sau khi hoàn thành. "/auto-clear # UI:" để chỉ định prefix + tag commit, "/auto-clear off" để hủy, "/auto-clear install" để đăng ký hook trên máy mới.
---

# Auto Clear

**Push rồi mới clear:** chạy `/push-in-session` để commit + push đúng những file session này sửa, push
xong mới tự `/clear` session Claude Code **đang chạy** ngay sau khi lượt trả lời kết thúc — dev không
phải gõ tay, không mở session mới, không mất việc chưa push. Claude Code không có cơ chế clear có sẵn
(skill/hook không gọi được lệnh built-in), nên skill này chỉ **bật cờ**; Stop hook (đăng ký một lần mỗi
máy) đọc cờ rồi **gõ `/clear` + Enter vào đúng pane** đang chạy Claude.

| | macOS (iTerm2) | Windows (Windows Terminal / console) — **THỬ NGHIỆM** |
|---|---|---|
| Script | `bash .claude/skills/auto-clear/scripts/auto-clear.sh <sub>` | `powershell -ExecutionPolicy Bypass -File .claude/skills/auto-clear/scripts/auto-clear.ps1 <sub>` |
| Nhắm pane bằng | UUID trong `ITERM_SESSION_ID` + so tty với process `claude` | PID process `claude` → `AttachConsole` + `WriteConsoleInput` |
| Cờ | `~/.claude/auto-clear/<uuid>.flag` | `~/.claude/auto-clear/pid-<pid>.flag` |

Cả hai nhắm theo pane/process, **không** theo cửa sổ đang focus — dev đang đứng ở pane khác cũng không
bị gõ nhầm. Dưới đây `<script>` là dòng "Script" đúng OS trong bảng trên.

## 0. Đọc argument

| Arg | Làm gì |
|---|---|
| (trống) | Push rồi bật cờ — theo mục 1–4 |
| prefix và/hoặc `Tag:` (vd `# UI:`, `* Play:`, `Bal:`) | Như (trống); prefix (`+`/`*`/`#`) + tag chuyển nguyên văn cho `/push-in-session` (mục 3.6 bên đó). Tag không nằm trong bảng tag của push-in-session → dừng hỏi, không tự chế |
| `off` | `<script> off` — gỡ cờ, báo lại. **Không** push |
| `status` | `<script> status` |
| `probe` | `<script> probe` — dò pane/console, **không** gõ gì (kiểm tra cài đặt) |
| `install` | `<script> install` — đăng ký Stop hook vào `~/.claude/settings.json`. Sửa settings user-level nên **hỏi dev trước** nếu dev chưa yêu cầu rõ |
| `uninstall` | `<script> uninstall` — gỡ hook |

Chỉ nhánh (trống) / prefix-tag mới push; các sub-command còn lại chỉ chạy script.

## 1. Thứ tự: việc khác → push → bật cờ

- `/auto-clear` đi kèm việc khác trong prompt/goal → làm hết việc khác trước (code, compile-check
  theo `.claude/rules/compile-validation.md`, report). Sau đó `/push-in-session` (mục 2). Bật cờ
  (mục 3) là **tool call cuối cùng** của lượt; sau đó chỉ viết câu trả lời cuối rồi dừng — không gọi
  thêm tool nào.
- Dưới `/goal`: chỉ push + bật cờ khi điều kiện goal **đã đạt trọn vẹn**. Bật sớm mà goal chặn dừng thì
  `/clear` bị gõ vào lúc đang làm tiếp.
- Task còn dở / bị block / compile hoặc test fail (`COMPILE_BLOCKED`…) → **không** push, **không** bật
  cờ; báo dev như bình thường (dev cần đọc lỗi trước khi mất context).
- Prompt đã yêu cầu `/push-in-session` riêng và lượt này đã push xong → không push lần hai; chạy lại
  chỉ ra `NO_CHANGES` nếu từ đó tới giờ không sửa thêm file nào.
- Đang trong `/run-backlog` → **không** dùng skill này: loop tự commit (STEP 9) và tự mở session mới
  cho mỗi task.

## 2. `/push-in-session` TRƯỚC khi bật cờ (bắt buộc)

Đọc và làm đúng [`push-in-session`](../push-in-session/SKILL.md) — danh sách file dựng từ transcript,
`git_prepare_scoped` → message `<prefix> Tag: <subject>` → `git_push`. Không tự `git add` / commit /
push bằng cách khác, không thêm `git status` / `git diff` ngoài ngân sách lệnh của skill đó. File Unity
tự dirty (scene/SO bị tool sync lại sau `Assets/Refresh`) không thuộc session thì để nguyên — push-in-session
đã liệt kê chúng ở `--- DIRTY OUTSIDE SESSION ---`.

| Kết quả push-in-session | Bật cờ? |
|---|---|
| Push thành công (kể cả sau `git push -u origin HEAD` khi nhánh chưa có upstream) | **Có** |
| `NO_CHANGES` (session chỉ đọc/phân tích, hoặc đã push hết trước đó) | **Có** — report ghi "không có gì để push" |
| Không có `origin` — đã commit local, bỏ push | **Có** — report ghi rõ commit chưa push |
| Push lỗi (nhánh tụt hậu, auth, network, hook reject) / commit lỗi | **Không** — báo lỗi nguyên văn, dừng |
| Phải dừng hỏi dev (tag không nằm trong bảng, không chắc file nào thuộc session…) | **Không** — hỏi dev, chờ trả lời rồi mới làm tiếp |

## 3. Bật cờ kèm report

Report = đúng nội dung câu trả lời cuối: danh sách file (theo `.claude/rules/output-format.md`), report
của push-in-session (message commit, file đã commit, file dirty ngoài session, trạng thái push), việc
dev cần làm. Truyền qua stdin để lưu lại — sau `/clear` report trên màn hình có thể mất.

macOS:
```bash
bash .claude/skills/auto-clear/scripts/auto-clear.sh arm --stdin <<'EOF'
<report>
EOF
```

Windows:
```bash
powershell -ExecutionPolicy Bypass -File .claude/skills/auto-clear/scripts/auto-clear.ps1 arm --stdin <<'EOF'
<report>
EOF
```

## 4. Đọc output

| Output | Làm gì |
|---|---|
| `ARMED ...` | Câu trả lời cuối = report + đúng 1 dòng: "Session sẽ tự /clear sau khi mình dừng — report lưu ở `~/.claude/auto-clear/last-report.md`." **Không** nói "đã clear" — clear xảy ra sau khi lượt kết thúc. |
| `NOT_INSTALLED ...` | Hook chưa đăng ký trên máy này (hoặc trỏ tới script đã bị xoá/dời) → hỏi dev có cho chạy `install` không. Đồng ý → `install` rồi `arm` lại (**không** push lại); không → nhắc dev tự gõ `/clear`. |
| `UNSUPPORTED ...` | Không chạy trong iTerm2 (macOS) / không tìm thấy process `claude` → nhắc dev tự gõ `/clear` (push đã xong). |

## Cài trên máy mới

1. `<script> install` — idempotent, backup `~/.claude/settings.json.bak-auto-clear`, giữ nguyên mọi hook
   khác. Hook nằm ở settings **user-level**: `.claude/settings.json` của project được commit cho cả team,
   còn hook gõ phím vào pane là chuyện của từng máy. Một hook phục vụ **mọi project** trên máy — cài ở
   project nào cũng được, không cần cài lại cho từng project.
2. `<script> probe` — macOS phải ra `ok-dry tty=/dev/ttysN`; Windows phải ra `ok-dry attached pid=N`.
   Lần đầu trên macOS hệ điều hành hỏi quyền điều khiển iTerm2 → Allow.
3. Hook mới thường có hiệu lực ngay; lượt đầu không clear → mở `/hooks` một lần hoặc khởi động lại
   Claude Code.

`install` copy script sang `~/.claude/auto-clear/auto-clear.sh` (Windows: `.ps1`) và cho hook chạy bản
copy đó, **không** trỏ vào repo — xoá/dời/copy project sang chỗ khác không làm hook chết. Mỗi lần `arm`
script tự đồng bộ lại bản copy, nên update skill qua Feature Hub là hook dùng bản mới luôn. Máy đã cài
bản cũ (hook trỏ thẳng vào repo, vd `.agents/scripts/auto-clear.sh` của project khác) vẫn chạy được;
chạy `install` một lần để chuyển sang bản copy.

Log: `~/.claude/auto-clear/auto-clear.log` — `hook ... -> ok` / `tty-mismatch` / `pane-not-found` /
`attach-failed`.

## Giới hạn

- macOS: chỉ iTerm2. Claude chạy trong tmux/ssh bên trong iTerm → tty lệch → hook bỏ qua (không clear,
  không gõ nhầm). Terminal.app / VS Code / app desktop → `UNSUPPORTED`.
- Windows: **thử nghiệm**, chưa chạy trên máy thật. Claude chạy trong WSL không được hỗ trợ.
- Dev gõ phím đúng lúc hook chờ (~1 giây) → chữ có thể lẫn với `/clear`. Đổi độ trễ bằng env
  `AUTO_CLEAR_DELAY`.
