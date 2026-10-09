---
name: restart-unity
description: Ép restart Unity Editor của project hiện tại NGAY LẬP TỨC — kill -9 Editor (kèm AssetImportWorker / build -batchmode đang giữ project), gỡ lockfile, rồi mở lại đúng bản Editor đó với project này. KHÔNG hỏi confirm, KHÔNG save scene/prefab, KHÔNG kiểm tra gì trước — thay đổi chưa save trong Editor sẽ mất, đó là chủ đích. Dùng khi user gõ "/restart-unity", nói "restart unity", "khởi động lại Unity", "reset editor", "Unity treo / đơ / kẹt compile, mở lại đi", "Unity MCP mất kết nối, restart editor". `--no-launch` để chỉ kill, `--dry-run` để xem trước; agent dùng `--open-only` (mở nếu chưa có, không kill) và `--status`. KHÔNG phải compile check (/compile-check) và không phải build.
---

# Restart Unity

Kill Editor đang mở project này rồi mở lại **ngay**. Không có bước nào để hỏi, để save hay để cân nhắc
— user gọi skill này là đã chấp nhận mất mọi thay đổi chưa save trong Editor.

## Luật

- **Chạy script ngay trong tool call đầu tiên.** Không hỏi confirm, không `AskUserQuestion`, không
  cảnh báo "sẽ mất thay đổi chưa save" trước khi chạy.
- **Không save gì trước khi kill** — không `unity_scene_save`, không `Assets/Save Project`, không
  thoát Play Mode, không chờ compile/import xong. Editor đang treo thì mấy thứ đó cũng không chạy được.
- **Không kiểm tra gì trước** — không `git status`, không probe Unity MCP, không đọc log. Script tự lo
  tìm process và binary Editor.
- Chỉ đụng Editor của **project hiện tại** (khớp `-projectPath`); Editor mở project khác trên máy giữ
  nguyên.

## Chạy

macOS / Linux:
```bash
bash .claude/skills/restart-unity/scripts/restart-unity.sh
```
Windows:
```powershell
powershell -ExecutionPolicy Bypass -File .claude/skills/restart-unity/scripts/restart-unity.ps1
```

| Arg của skill | sh | ps1 | Làm gì |
|---|---|---|---|
| (trống) | — | — | kill + mở lại |
| `no-launch` / `kill` | `--no-launch` | `-NoLaunch` | chỉ kill, không mở lại |
| `open-only` / `open` | `--open-only` | `-OpenOnly` | **không kill**: Editor GUI đã mở thì thôi, chưa có thì mở (kèm `-ignoreCompilerErrors` để không kẹt dialog Safe Mode). Dùng cho agent — `bughub-watch.py editor open` |
| `status` | `--status` | `-Status` | chỉ in trạng thái, không đụng gì |
| `dry-run` | `--dry-run` | `-DryRun` | in PID sẽ kill + Editor sẽ mở, không đụng gì |
| `<path>` | `--project <path>` | `-Project <path>` | project khác cwd |

Script làm:
1. Dò project root (thư mục có `ProjectSettings/ProjectVersion.txt`, từ cwd đi lên).
2. Tìm mọi process Unity có `-projectPath` = project đó (Editor GUI + `AssetImportWorker*` + build
   `-batchmode` đang giữ lock; **không** tính Unity Hub dù Hub cũng mang `-projectPath`) → `kill -9`
   (Windows: `taskkill /F /T`), chờ chết hẳn (≤10s). `--open-only` bỏ qua bước này: có GUI → `ALREADY_RUNNING`,
   chỉ có process batchmode → `ERROR BUSY …`.
3. Gỡ `Temp/UnityLockfile`; dời `Temp/__Backupscenes` sang `Logs/restart-unity/__Backupscenes-<ts>`
   để Editor mới không dừng ở hộp thoại khôi phục scene (vẫn giữ bản backup nếu cần lục lại).
4. Mở lại **đúng binary Editor vừa chạy** (macOS `open -n -a <Unity.app> --args -projectPath …`).
   Không có Editor nào đang chạy → lấy `m_EditorVersion` trong `ProjectVersion.txt` → thư mục cài
   Unity Hub (`secondaryInstallPath.json` → `/Applications/Unity/Hub/Editor` /
   `C:\Program Files\Unity\Hub\Editor` / `~/Unity/Hub/Editor`).

Dòng cuối stdout là status:

| Status | Nghĩa |
|---|---|
| `RESTARTED pid=<n>` | Editor mới đã lên process (đang load project — chưa chắc đã vào xong) |
| `KILLED` | `--no-launch`: đã kill, không mở lại |
| `OPENED pid=<n>` / `ALREADY_RUNNING pid=<n>` | `--open-only`: vừa mở / đã mở sẵn (không đụng tới) |
| `RUNNING gui=<pid\|none> pids=[…]` / `NOT_RUNNING` | `--status` |
| `DRY_RUN kill=[…] launch=…` | chỉ xem trước |
| `ERROR <lý do>` | báo nguyên văn cho user, **không** tự chạy lại vòng hai |

`ERROR … VERSION_NOT_INSTALLED:<ver>` = không có Editor nào đang chạy và máy chưa cài đúng version
trong `ProjectVersion.txt` → bảo user mở project qua Unity Hub một lần.

## Sau khi restart

- Việc tiếp theo **không cần** Unity MCP → trả lời ngay, không chờ Editor load xong.
- Việc tiếp theo **cần** Unity MCP (compile check, dựng UI, chụp màn…) → chờ Editor load: gọi
  `unity_list_instances` (hoặc `unity_editor_ping`) cách nhau ~15s, tối đa ~5 phút (project lớn
  reimport lâu). Có instance thì `unity_editor_state` tới khi hết compiling rồi mới làm tiếp. Hết giờ
  vẫn không thấy → báo user Editor chưa lên (có thể đang kẹt ở dialog Safe Mode do lỗi compile —
  cần người bấm), đừng restart lần nữa.

## Report

Một dòng: status của script (+ PID mới), kèm đường dẫn scene backup nếu script có dời.
