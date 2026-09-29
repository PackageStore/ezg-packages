# Chạy lệnh của module không cần bấm menu

Mọi menu Tools/UI Motion có điểm vào batchmode trong `UIMotionBatch` / `UIMotionLint` (namespace của module, xem
`UIMotionBatch.cs`). Dạng lệnh:

```bash
"<Unity.exe>" -batchmode -projectPath "<project>" -executeMethod <Namespace>.Editor.UIMotionBatch.<Method> -logFile "<log>" -quit
```

| Method | Làm gì | Ghi chú |
|---|---|---|
| `UIMotionBatch.SetupBatch` | Đồng bộ define, tạo asset còn thiếu, áp file luật riêng nếu có, import TMP Essentials | KHÔNG kèm `-quit` (tự thoát sau import) |
| `UIMotionBatch.ApplyProjectRulesBatch` | Áp `ProjectSettings/UIMotionProject.json` | `-uimotionProjectRules <file>` để dùng file khác; file lỗi → mã 1 |
| `UIMotionBatch.MoveDataOutOfModuleBatch` | Dời asset của project ra khỏi folder module | |
| `UIMotionBatch.UpgradeRoleMapBatch` | Gộp bảng mặc định bản module mới vào RoleMap (giữ chỉnh tay) | |
| `UIMotionBatch.ScanReportBatch` | Quét + gate, KHÔNG đổi gì; báo cáo Markdown + TSV | `-uimotionReport <file.md>` |
| `UIMotionBatch.BindAllBatch` | Gắn mọi component còn thiếu | chạy sau khi đã đọc báo cáo quét |
| `UIMotionLint.RunCli` | Gate; mã 1 khi còn vi phạm và gate = Chặn build | `-uimotionStrict` = mã 1 khi còn vi phạm bất kể chế độ |

## Project đang mở trong Editor của user

Unity từ chối batchmode trên project đang mở (có `Temp/UnityLockfile`). Không tắt Editor của user. Hai cách:

1. Nhờ user bấm menu tương ứng (nhanh nhất cho một lệnh).
2. Chạy trên **bản chép**: chép project (bỏ `Temp/`, `Logs/`, file `.csproj` / `.sln`; giữ `Library/` cho khỏi import lại),
   xoá `Library/ilpp.pid` trong bản chép trước mỗi lần chạy (bản chép mang theo file này có thể làm tắt tiến trình ILPP của
   Editor user). Chạy lệnh, đọc log / báo cáo. Lệnh có ghi (Apply, Bind, Move) thì chép file kết quả (asset + `.meta`) về
   project thật khi user đồng ý — hoặc để user chạy menu trong Editor cho chắc.
   Đường dẫn bản chép nên ngắn (Windows giới hạn 260 ký tự: DLL sâu trong `Library/PackageCache` có thể báo
   DirectoryNotFound, không liên quan module).

Import package chạy bất đồng bộ: `-quit` có thể thoát trước khi import xong mà log vẫn báo thành công — đọc kỹ log.

## Test Play mode của module

- `yield return new EnterPlayMode()` phải nằm ngay trong hàm `[UnityTest]`; yield từ hàm con thì runner bỏ qua, test treo.
- Phần sau `EnterPlayMode` chạy trong coroutine con: vào Play mode là reload domain, lambda bắt biến từ đầu hàm → NullReference.
- Game giới hạn FPS: chờ `Time.frameCount` đổi, không tin một lần `yield return null`.
- Hook Editor / SDK của project chen vào Play mode (survey mục Bẫy): ghi vào `UIMotionProject.md`; tắt tạm CHỈ trên bản chép.
