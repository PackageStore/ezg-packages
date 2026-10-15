# EZG QA Probe (`com.ezg.qa-probe`)

Cầu nối trong game cho skill **`/auto-test-device`** (`.claude/skills/auto-test-device/`, tool `qadevice.py` chạy trên máy
dev): agent QA nhìn được cây UI kèm toạ độ, audit visual tự động, chạm qua EventSystem, đọc state / gọi cheat bằng
reflection, lấy log Unity kèm stack và gửi bug qua Bug Logger của game. Không có probe, skill vẫn test được kiểu hộp đen
(ảnh + toạ độ + log) — probe chỉ làm cho việc test sâu và chính xác hơn.

## Cài

Template EZG đã có sẵn. Project cũ: Feature Hub (tab UPM) hoặc để `qadevice.py start` tự thêm vào `Packages/manifest.json`:

```json
"com.ezg.qa-probe": "1.2.0"
```

Không cần code hay asset nào trong `Assets/`. Build mới sẽ có probe.

## An toàn

- **Nằm im** cho tới khi được kích hoạt bằng token ngẫu nhiên của phiên test: intent extra `ezg_qa_probe` (Android),
  argument `-ezgQaProbe` / biến môi trường `EZG_QA_PROBE` (iOS), hoặc file `<persistentDataPath>/ezg_qa_probe.txt` — chỉ
  người có adb / Xcode / quyền ghi vào dữ liệu app mới đặt được. Người chơi thường không bao giờ kích hoạt.
- Mọi request phải mang đúng token (`X-QA-Token`). Android chỉ nghe `127.0.0.1` (vào bằng `adb forward`).
- **Cổng**: Editor / Development Build / define `EZG_QA_PROBE` luôn mở. Bản release chỉ mở khi:
  - máy có trong danh sách máy test của `com.ezg.cheat-gate` (grant ký RSA từ server), hoặc
  - cờ cheat của game (`releaseGate`, mặc định `GameSystems.isCheat`) đang true.
  Cổng đóng thì chỉ `/ping`, `/logs`, `/unlock` và `/stop` trả lời; mọi endpoint khác trả 423.
- Kết thúc phiên (`qadevice.py stop` → `/stop`) thì probe thôi nghe; token của phiên đã dừng không bao giờ dùng lại. Nối
  vào game đang chạy (`start --attach`): Android gửi intent mới mang token (Android 11+ không cho app đọc file do adb ghi),
  iOS / Editor ghi file trigger; probe đổi token khi app lấy lại focus.
- Define `EZG_QA_PROBE_DISABLED` (Player Settings → Scripting Define Symbols) loại toàn bộ code khỏi build.

## Máy test của `com.ezg.cheat-gate`

Project có `com.ezg.cheat-gate` ≥ 0.1.1: probe vừa được kích hoạt là hỏi CheatGate **một lần** như QA tap version 10 lần
(lần đầu qua `GameSystems.TryEnableCheat(Action<bool>)` của game nên game tự bật cờ cheat; máy chưa có tên thấy Device ID
trên màn). Máy có trong danh sách → cổng mở + cheat của game bật luôn, không cần tap. Máy chưa có → tool in Device ID và
lệnh thêm máy (ezg MCP `test_device_add`), rồi `qadevice.py probe-connect --unlock` hỏi lại tới khi server nhận.

## Ghi đè cấu hình (tuỳ chọn)

Mặc định probe tự dò theo quy ước template EZG: static class `GameSystems` có `static bool isCheat`,
`TryEnableCheat(Action<bool>)`, `DisableCheat(...)`. Game khác quy ước → tạo TextAsset `Resources/EzgQaProbeConfig.json`
ở bất kỳ thư mục `Resources` nào, chỉ khai mục cần đổi:

```json
{
  "releaseGate": "MyGame.Debug.CheatFlags.enabled",
  "cheatOn": "MyGame.Debug.CheatFlags.Enable",
  "cheatOff": "none",
  "autoCheat": true,
  "port": 7531
}
```

| Mục | Ý nghĩa | Trống | `"none"` |
|---|---|---|---|
| `releaseGate` | bool static mở probe ở bản release | tự dò `GameSystems.isCheat` | không dùng cờ (chỉ CheatGate / dev build) |
| `cheatOn` | hàm static bật cheat game (0 tham số hoặc một `Action<bool>`) | tự dò `TryEnableCheat(Action<bool>)` | gán `releaseGate = true` |
| `cheatOff` | hàm static tắt cheat game | tự dò `DisableCheat` | gán `releaseGate = false` |
| `autoCheat` | máy có quyền CheatGate thì tự bật cheat game | `true` | — |

`/ping` trả config đang dùng (`config`), log khi kích hoạt có dòng `[EzgQaProbe] config …`.

## Test

PlayMode tests ở `Tests/Runtime` (cần `"testables": ["com.ezg.qa-probe"]` trong manifest để chạy).
