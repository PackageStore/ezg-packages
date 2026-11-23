---
name: unity-lite
description: Bật / tắt chế độ Unity Editor NHẸ RAM cho project hiện tại (Windows) — tắt Burst trong Editor (`--burst-disable-compilation`), đồ hoạ D3D11, tắt Unity Search index lúc mở, 1 AssetImportWorker + 0 worker chờ sẵn; cộng lệnh `trim` trả RAM của Editor đang rảnh cho Windows. Chỉ đụng cờ dòng lệnh + `UserSettings/` (không vào git). Dùng khi user nói "/unity-lite", "bật chế độ nhẹ", "unity lite on/off", "mở Editor bản nhẹ", "Editor tốn RAM quá", "tắt burst cho đỡ RAM", "bật lại burst / về bản thường", "trim ram unity", "mở nhiều Editor cho AI". KHÔNG dùng để build player (Burst tắt ⇒ build không có Burst) và không phải restart khi Editor treo (/restart-unity).
---

# Unity Lite — Editor nhẹ RAM, bật / tắt nhanh

Một lệnh chuyển Editor của project giữa **LITE** (cho AI dùng qua Unity MCP: compile, chạy code, chụp ảnh, sửa prefab)
và **NORMAL** (Editor như mở từ Unity Hub). Không đụng file nào git track ⇒ `git diff` không đổi, bật / tắt lúc nào cũng được.

## Chạy

```powershell
powershell -ExecutionPolicy Bypass -File .claude/skills/unity-lite/scripts/unity-lite.ps1 <lệnh> [tuỳ chọn]
```

| Lệnh | Làm gì |
|---|---|
| `status` (mặc định) | mode đã lưu + giá trị UserSettings + Editor đang chạy thật sự dùng gì (Burst / gfx / RAM / commit), kể cả clone `<project>_clone_N` |
| `on` | đóng Editor (êm, chờ ≤ 45 s) → lưu giá trị cũ → áp UserSettings lite → mở lại với cờ lite, chờ load xong, in cổng MCP |
| `off` | đóng Editor → trả UserSettings về giá trị cũ → mở lại bình thường |
| `open` | Editor chưa chạy thì mở theo mode đã lưu; đang chạy thì thôi (`ALREADY_RUNNING`), không bao giờ đóng |
| `trim` | `EmptyWorkingSet` cho Editor (+ tiến trình con) của project và các clone: bản rảnh còn vài chục MB RAM vật lý |

**Thư mục không phải project Unity** (vd một workspace chứa nhiều project con): luôn truyền `-Project <đường dẫn project>`
(thiếu thì script báo `ERROR NO_PROJECT`).

Tuỳ chọn: `-Project <path>` (project / clone khác cwd) · `-NoLaunch` (chỉ đổi cài đặt, lần `open` sau mới áp) ·
`-Force` (Editor không chịu đóng vì hộp thoại ⇒ kill, mất thay đổi chưa save) · `-NoWait` · `-WaitSec <n>` (mặc định 300).

Dòng cuối stdout: `READY pid=… mode=… port=…` · `OPENED` (`-NoWait`) · `STILL_LOADING` · `ALREADY_RUNNING` · `NOT_LAUNCHED` ·
`TRIMMED` · `ERROR <lý do>` (báo nguyên văn cho user, không tự chạy lại).

## Bật / tắt cái gì

| Thứ | LITE | NORMAL | Nằm ở | Đo trên một project thật (2026-11-10) |
|---|---|---|---|---|
| Burst Editor | `--burst-disable-compilation` | bật | cờ dòng lệnh | **−1,4 GB** (heap managed 1,38 GB → 0,14 GB), mở 40 → 26 s (reload đầu sau khi mở 26 → 14 s); các lần compile sau **không đổi** (~14 s cả hai, đo trên việc gỡ merge thật) |
| Đồ hoạ | `-force-d3d11` | mặc định (D3D12) | cờ dòng lệnh | −200 MB |
| Search index lúc mở | `indexOnEditorStartup = false` | giá trị cũ | `UserSettings/Search.settings` | ~1 s |
| AssetImportWorker | desired 1, standby 0 | giá trị cũ (3 / 2) | `UserSettings/EditorUserSettings.asset` | project đo thường không sinh worker; khi có, mỗi worker chờ sẵn giữ ~1,1 GB commit (đo ở một project khác cùng máy) |

Giá trị cũ lưu ở `UserSettings/UnityLite.json` (gitignored) để `off` trả lại đúng như trước.
Cờ dòng lệnh chỉ có khi mở bằng script này: mở lại từ Unity Hub = NORMAL cho Burst / gfx (UserSettings lite vẫn giữ tới khi `off`).

## Lưu ý — đọc trước khi bật

- **Không build player từ Editor LITE.** Burst tắt bằng cờ thì AOT Burst của bản build cũng tắt. Build ⇒ `off` trước.
- Đổi qua lại không phải compile lại (thử 2026-11-10: `on` ⇒ READY 51 s, `off` ⇒ READY 37 s). Burst có cơ chế tự compile sạch khi thấy
  assembly chưa qua IL post-processor của nó (log "Burst was previously disabled and is now enabled") — gặp dòng đó thì lần mở đó mất
  thêm ~2–3 phút, script in `NOTE`.
- Công tắc menu `Jobs > Burst > Enable Compilation` (EditorPrefs, chung cả máy) **không** tiết kiệm RAM — chỉ cờ / biến môi trường
  `UNITY_BURST_DISABLE_COMPILATION` mới bỏ được phần nạp nặng. Đừng đặt biến môi trường đó cho cả máy: mọi Editor + mọi bản build mất Burst.
- Play mode trong Editor LITE: job Burst của URP / 2D / Collections chạy bằng Mono ⇒ có thể chậm hơn; code game không dùng `[BurstCompile]`.
- `trim` không giảm commit (trang dời ra pagefile / vùng nén). Lệnh MCP đầu tiên sau trim chậm hơn một chút vì nạp lại trang.
- Editor chạy nền không tự vẽ Game view: `unity_screenshot_game` chỉ ghi file sau khi gọi
  `UnityEditorInternal.InternalEditorUtility.RepaintAllViews()` qua `unity_execute_code`.
- Hai Editor của cùng project (chính + clone) trùng tên trong Unity MCP và có thể đổi cổng cho nhau ⇒ luôn
  `unity_list_instances` rồi chọn theo `port` + `projectPath`.

## Sau khi chạy

Script đã chờ Editor load xong (`READY`) ⇒ dùng ngay `port` in ra cho mọi lệnh `unity_*`. `STILL_LOADING` ⇒ chờ
`unity_list_instances` như sau `/restart-unity`. Report một dòng: mode + status cuối + cổng MCP.
