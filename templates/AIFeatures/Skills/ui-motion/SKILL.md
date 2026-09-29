---
name: ui-motion
description: Motion, sfx và feedback cho UI Unity (uGUI) theo role bằng module UI Motion (UIMotion) và quy chuẩn đi kèm module, dùng cho bất kỳ project nào. Dùng khi làm, hỏi hoặc duyệt motion / sfx / feedback của screen, popup, nút, list, toggle, tab, card, dropdown, slider, sheet, toast, tooltip, counter, badge, timer, thanh HP; role UIRole, Profile, RoleMap, Settings, jump in / out, đóng screen, SetActiveAnimated, gate M-1 … M-12, nợ gate; khảo sát project để sinh luật riêng (UIMotionProject.json), đưa module vào project chưa có hoặc nâng bản cũ, quét và gắn motion cho prefab, kể cả khi user không nhắc tới quy chuẩn. English triggers: "add UI motion", "button press feedback", "popup open / close animation", "UI juice", "UI sfx", "retrofit UI motion into an old project". Không dùng cho animation nhân vật, quái, prop trong thế giới game.
argument-hint: [việc cần làm] [prefab / screen]
---

# UI Motion: làm theo quy chuẩn, ở mọi project

Skill này **không chứa thông tin riêng của project nào**. Có ba nguồn, mỗi nguồn giữ một loại:

| Nguồn | Giữ gì | Ở đâu |
|---|---|---|
| Quy chuẩn của module | Luật chung: role, motion mặc định, sfx, quy tắc kỹ thuật, gate | `<module>/Docs/UIMotion_QuyChuan.md` |
| File riêng của project | Điều chỉ đúng cho project đang làm: class tự có motion, audio manager, chỗ đóng screen, thói quen đặt tên, quyết định, bẫy | `ProjectSettings/UIMotionProject.json` (luật máy đọc) và `ProjectSettings/UIMotionProject.md` (ghi chú) |
| Script của skill | Khảo sát project, sinh hai file trên, kiểm file luật, cài module | `scripts/uimotion.mjs` (Node 18+, không cần cài gì) |

Điều về project đang làm chỉ lấy từ file riêng của nó hoặc từ chính project (khảo sát, đọc code). **Không dùng trí nhớ về
project khác**: tên class, số, keyword của project A sai ở project B. Thiếu thì khảo sát rồi hỏi user, rồi ghi vào file
riêng của project. Không ghi thông tin project vào skill này, cũng không ghi vào folder module.

`<skill>` dưới đây là folder chứa file này. Lệnh chạy từ gốc project Unity hoặc truyền `--project <gốc>`.

## 1. Định vị

```bash
node <skill>/scripts/uimotion.mjs survey --project <gốc project>
```

In: Unity, module UI Motion (đường dẫn, bản), quy chuẩn, asset của project (có nằm trong folder module không), file riêng,
ứng viên (class tự có motion, class bấm được, audio manager), bẫy môi trường. Báo cáo đủ: `Logs/UIMotionSurvey.md`
(ghi đè mỗi lần chạy, không sửa tay). Project lớn mất vài chục giây. Chỉ đọc file, chạy được khi Unity đang mở project.

- **Chưa có module**: hỏi user có đưa module vào không, rồi làm theo `reference/retrofit.md`.
- **Asset nằm trong folder module** (bản 0.3 trở về trước): dời ra trước khi làm gì khác (`reference/retrofit.md`, bước 3).
- **Bản quy chuẩn lệch bản module** (`Phiên bản` đầu quy chuẩn ≠ `UIMotionDefaults.Version`): báo user, tài liệu và code không cùng bản.

## 2. File riêng của project

- **Đã có** `UIMotionProject.md`: đọc hết (ngắn). Có `UIMotionProject.json`: đọc. Survey báo "ứng viên mới chưa có trong
  file luật" hay "file luật nhắc class không còn" thì nêu ra với user.
- **Chưa có**: `node <skill>/scripts/uimotion.mjs init`. Script ghi vào file luật: dòng class của project đã có trong RoleMap
  (tuning đang chạy) và ứng viên độ tin cao; dòng RoleMap mang class không có trong project thì vào `componentRulesRemove`;
  còn lại đưa vào mục "Cần xác nhận" của ghi chú. Đọc lại cùng user những gì script đã ghi, đi qua mục "Cần xác nhận" (mỗi
  dòng có bằng chứng), sửa file luật, kiểm: `node <skill>/scripts/uimotion.mjs check-rules`. Rồi áp vào Unity: menu
  **Tools/UI Motion/Áp luật riêng của project** (hoặc batch `UIMotionBatch.ApplyProjectRulesBatch` trên bản chép, xem
  `reference/unity-batch.md`).
- Schema, ai ghi gì, khi nào cập nhật: `reference/project-files.md`. Đọc trước khi sửa một trong hai file.

## 3. Đọc quy chuẩn

1. Quy chuẩn: `<module>/Docs/UIMotion_QuyChuan.md` (survey in đường dẫn). Project chưa có module: đọc ở module nguồn —
   `<skill>/../..` khi skill nằm trong `Skill~` của module, hoặc `moduleSource` trong `<skill>/install.json`. Không thấy ở
   đâu thì dừng và báo user, không làm theo trí nhớ.
2. Đọc từ đầu tới hết mục 0: phiên bản, phần "Đổi so với", năm nguyên tắc gốc.
3. Grep `^#{2,3} ` lấy danh sách mục kèm số dòng. Chọn **mọi** mục dính tới việc (một cái nút dính role, keyword, luật gắn,
   motion mặc định, sfx, gate), đọc bằng Read với offset / limit. Gặp dẫn chéo ảnh hưởng tới việc thì đọc luôn.
4. Việc lớn (đưa module vào project, duyệt cả game, thêm role mới cho module): đọc hết file.
5. Người ghép UI cần các bước ngắn: `README.md` cùng thư mục. Thay đổi từng bản: `CHANGELOG.md`.

## 4. Làm

- Luật chung theo quy chuẩn, ghi số mục ("theo 6.6", "gate M-11"). Điều riêng của project theo file riêng, ghi rõ
  ("theo UIMotionProject.md: đóng popup ở UIManager.ClosePopup").
- Chỉ dùng cơ chế phía Unity của module (component, RoleMap, Profile, Settings, menu Tools/UI Motion, batch). Không viết
  tween tay cho từng screen khi module đã có role / component cho việc đó.
- **Không sửa folder module trong project game**: module thay nguyên folder khi nâng bản, sửa tay là mất. Cần module làm
  khác thì sửa ở nguồn module (repo phát triển module), tăng bản, rồi cài lại.
- Code riêng của project (sink sfx nối audio manager, chỗ đóng screen) đặt trong code của project. Mẫu sink và cách map:
  `reference/sfx-sink.md`.
- Điều mới về project (class mới tự có motion, keyword theo cách đặt tên, quyết định lệch quy chuẩn, bẫy môi trường): luật
  máy đọc vào `UIMotionProject.json` rồi áp lại; ghi chú, quyết định kèm ngày vào `UIMotionProject.md`.
- Muốn làm khác quy chuẩn: nói lệch mục nào và vì sao, để user quyết, ghi quyết định vào `UIMotionProject.md`. Không lặng
  lẽ làm khác. Thấy quy chuẩn tự mâu thuẫn hoặc lệch code module: nói ra, không tự chọn một bên.

## 5. Kiểm trước khi trả lời xong

- Gắn / sửa motion cho prefab, screen: soát rule gate liên quan; chạy được thì chạy gate (Tools/UI Motion/Kiểm tra (gate),
  hoặc batch `UIMotionLint.RunCli` / `UIMotionBatch.ScanReportBatch` trên bản chép — `reference/unity-batch.md`).
- Sửa file luật: `check-rules` sạch, nhắc áp lại trong Unity.
- Thêm role / component cho module (ở nguồn module): soát mục Định nghĩa "xong" của quy chuẩn.
- Cuối câu trả lời một dòng: phiên bản quy chuẩn, các mục đã áp, file riêng của project đã đọc / sửa.

## Script

| Lệnh | Làm gì |
|---|---|
| `survey [--out <folder>] [--json]` | Khảo sát, ghi `Logs/UIMotionSurvey.md` + `.json`, in tóm tắt |
| `init [--force]` | Sinh `UIMotionProject.json` + `.md` nếu chưa có; `--force` ghi bản `.new` bên cạnh để so, không ghi đè |
| `check-rules [--file <json>]` | Kiểm file luật như module sẽ đọc: khoá, kiểu, tên role theo enum UIRole của module trong project |
| `install-module --to <Assets/…/UIMotion> [--from <folder module>] [--data-to <Assets/…>]` | Chép / thay module; asset của project còn nằm trong module thì dời ra trước (giữ `.meta`) |
| `install-skill [--to <folder>]` | Cài skill này vào `~/.claude/skills/ui-motion`, ghi `install.json` (nguồn module) |
| `selftest [--keep]` | Tự kiểm script trên project giả |

Khảo sát đọc regex trên file text (`.cs`, `.prefab`, `.unity`, `.meta`), không biên dịch: mọi kết quả là **ứng viên kèm bằng
chứng**, không phải sự thật. Cách đọc và bẫy: `reference/survey.md`.
