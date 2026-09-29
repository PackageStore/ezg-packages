# File riêng của project

Ba file nằm trong `ProjectSettings/` của từng project game (vào VCS, không vào build). Skill và module không chứa những gì
ghi ở đây; project khác có bộ file của nó.

| File | Ai ghi | Ai đọc | Nội dung |
|---|---|---|---|
| `UIMotionProject.json` | `init` sinh lần đầu; sau đó Claude / người sửa | module (menu Áp luật riêng), skill, `check-rules` | Luật máy đọc: class tự có motion, keyword, từ nhận prefab, folder quét, sink sfx, folder dữ liệu |
| `UIMotionProject.md` | `init` sinh lần đầu; sau đó Claude / người sửa | skill, người | Điều thật của project: audio manager, chỗ đóng screen, list pool, bẫy môi trường, mục cần xác nhận, quyết định kèm ngày |
| `UIMotionDebt.json` | module (cửa sổ Kiểm tra, nút Bỏ qua…) | module (gate), skill | Nợ gate: vi phạm được chấp nhận kèm lý do, người, ngày |

`Logs/UIMotionSurvey.md` / `.json` là báo cáo khảo sát, ghi đè mỗi lần chạy, không phải file của project.

## UIMotionProject.json

```json
{
  "schema": 1,
  "project": "TênGame",
  "dataFolder": "Assets/Game/Resources/UIMotion",
  "componentRules": [
    { "type": "Game.UI.PopupBase", "role": "Popup", "ownMotion": true, "note": "tự tween mở / đóng + dim" },
    { "type": "TapArea", "role": "Button", "note": "vùng chạm tự viết, chưa có motion" }
  ],
  "componentRulesRemove": [ "OldButtonFx" ],
  "keywords": {
    "add": [ { "word": "pnl", "role": "Panel", "before": "Screen" } ],
    "remove": [ "fullscreen" ]
  },
  "prefabWords": { "item": [ "item", "cell", "slot", "row", "entry", "element", "card", "view" ], "template": [ "template", "layout", "tpl" ] },
  "settings": { "scanFolders": [ "Assets/Game" ], "excludePathContains": [ "/ThirdParty/", "/Vendor/" ], "sfxSinkType": "Game.Audio.UISfxSink" }
}
```

| Khoá | Nghĩa | Áp vào |
|---|---|---|
| `dataFolder` | Folder asset của project (`…/Resources/UIMotion`, không ghi đuôi thì module tự thêm). Chỉ dùng khi project chưa có asset nào | chỗ Tạo asset mẫu / dời asset |
| `componentRules[]` | `type`: tên class (hoặc có namespace; khớp cả lớp con). `role`: tên trong enum `UIRole` (`Unknown` = giữ role, chỉ báo đã có motion). `ownMotion`: class tự lo motion → tool không gắn chồng. Thứ tự = ưu tiên, đứng trên bảng mặc định | bảng Component của RoleMap |
| `componentRulesRemove[]` | Bỏ dòng Component theo tên (kể cả dòng mặc định) | bảng Component |
| `keywords.add[]` | `word` (một từ, chữ thường), `role`, `before` (tên role → đứng trước keyword đầu của role đó; hoặc một keyword). Không `before` = cuối bảng (ưu tiên thấp nhất). Keyword đã có mà khác role thì đổi role tại chỗ | bảng Keyword |
| `keywords.remove[]` | Bỏ keyword | bảng Keyword |
| `prefabWords.item` / `.template` | THAY danh sách từ nhận prefab item của list / prefab khuôn | RoleMap |
| `settings.scanFolders` / `.excludePathContains` | THAY folder quét / chuỗi loại trừ | Settings |
| `settings.sfxSinkType` | Tên đầy đủ class implement `IUIMotionSfxSink`; `""` = về player kèm sẵn | Settings |
| `notes` | Chuỗi tự do, module bỏ qua | — |

Luật đọc: khoá thiếu = giữ nguyên asset; danh sách có mặt = thay nguyên danh sách. Khoá lạ bị bỏ qua kèm cảnh báo (bắt lỗi
gõ). Áp là idempotent: áp lại không đổi gì thêm; RoleMap nhớ dấu file đã áp, file đổi mà chưa áp thì Inspector RoleMap và
Console nhắc. **Khôi phục bảng mặc định** trên RoleMap xoá luật của project: áp lại file sau đó.

Chỉ ghi luật đã chắc. Một dòng `ownMotion: true` sai làm object đó mất motion của module mà gate vẫn pass. Dòng chưa chắc để
trong mục "Cần xác nhận" của `UIMotionProject.md` cho tới khi người giữ project gật.

Sửa xong: `node <skill>/scripts/uimotion.mjs check-rules`, rồi trong Unity **Tools/UI Motion/Áp luật riêng của project**.

## UIMotionProject.md

`init` sinh các mục; giữ nguyên tên mục để lần sau tìm được:

| Mục | Ghi gì |
|---|---|
| Project | Unity, đường dẫn module, folder dữ liệu, folder quét, package, list pool, input |
| Class của project tự có motion | Luật đã ghi vào file JSON, vì sao |
| Cần xác nhận | Ứng viên chưa chắc kèm bằng chứng; xử lý xong thì chuyển lên mục trên hoặc ghi lý do bỏ vào Quyết định |
| Âm thanh | Audio manager, cách gọi, bảng map sự kiện UI Motion → tiếng của project, chỗ code cũ đã tự phát tiếng |
| Mở / đóng screen | UI manager, chỗ đổi sang `UIMotion.SetActiveAnimated`, số chỗ `SetActive(false)` |
| Bẫy môi trường | Hook Editor chen vào Play mode, giới hạn FPS, SDK log lỗi khi thoát Play… — những gì làm test / batch hiểu nhầm là module hỏng |
| Quyết định | Bảng ngày · quyết định · lý do: lệch quy chuẩn, bỏ ứng viên, bật / tắt phạm vi gắn |

Không chép lại quy chuẩn vào đây; chỉ ghi điều riêng của project và dẫn số mục quy chuẩn khi cần.

## Khi nào cập nhật

- Sau khi thêm / đổi screen controller, nút, list animator tự có motion, audio manager: chạy `survey`, xem "ứng viên mới chưa
  có trong file luật", cập nhật JSON + MD, áp lại.
- Nâng bản module: đọc `CHANGELOG.md` của module, chạy `survey` (bản RoleMap, asset nằm đâu), cập nhật MD nếu bẫy / chỗ gọi đổi.
- Mỗi quyết định lệch quy chuẩn: một dòng trong Quyết định, cùng lúc với việc làm.
