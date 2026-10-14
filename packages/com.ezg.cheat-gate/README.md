# EZG Cheat Gate (`com.ezg.cheat-gate`)

Chỉ máy test có tên trong danh sách mới mở được cheat trên bản release. Thay cho kiểu "tap 10 lần vào
version" mở cho mọi máy, hay whitelist nằm trong Firebase Remote Config (máy nào cũng tải về đọc được).

## Cách hoạt động

```
Mở game            -> không gọi gì. Máy đang giữ grant còn hạn -> mở cheat được ngay, kể cả offline.
Tap version 10 lần -> GET https://ezg-cheat-gate.developer-a1f.workers.dev/v1/check?project=SM007&device=<id>&n=<nonce>  (1 lần)
                      có trong danh sách -> grant ký RSA (kèm đúng nonce vừa gửi), hạn 7 ngày, lưu PlayerPrefs
                      không có           -> game hiện Device ID XXXX-XXXX-XXXX để gửi QA lead
Máy đang giữ grant -> lúc mở game và lúc quay lại game (tối đa 30 phút/lần) kiểm lại ngầm:
                      bị xoá khỏi danh sách -> mất cheat (Changed(false))
```

- **Người chơi thường không gửi request nào**: số request theo số máy test, không theo DAU.
- Danh sách không bao giờ rời server: máy chỉ biết về chính nó.
- Grant gắn với project + device id và có chữ ký: sửa PlayerPrefs hay chặn sửa response không tự cấp cheat được.
  Mỗi lần hỏi kèm một nonce ngẫu nhiên và chỉ tin grant ký đúng nonce đó, nên phát lại một câu trả lời cũ qua proxy
  không giữ được cheat sau khi máy bị xoá (tối đa tới khi grant đang giữ hết hạn).
- Grant nhận trong phiên chạy có hiệu lực đúng thời lượng của nó (7 ngày) tính từ lúc nhận theo đồng hồ máy: tester
  chỉnh giờ máy để test event vẫn dùng được, nhưng không kéo dài vô hạn.
- Device ID = 12 ký tự đầu của SHA-256(`"ezg-cheat-gate:" + SystemInfo.deviceUniqueIdentifier`). **Android 8+: mỗi game
  một ID khác nhau trên cùng một máy** (ANDROID_ID theo khoá ký app); iOS: chung cho các game cùng tài khoản developer.
  Luôn thêm đúng ID mà game đó hiện. APK cài tay (ký bằng upload key) và bản từ Google Play (ký bằng app signing key)
  của **cùng một game** cũng ra ID khác nhau — thêm cả hai nếu QA dùng cả hai.

## Tích hợp

1. Cài package (Feature Hub / UPM `com.ezg.cheat-gate`).
2. Lúc khởi động (splash / bootstrap), một lần:

   ```csharp
   using Ezg.CheatGate;

   CheatGate.Initialize("SM007");                 // mã project của game trong BugHub
   CheatGate.Changed += unlocked =>
   {
       if (!unlocked) GameSystems.DisableCheat();  // máy vừa bị xoá khỏi danh sách
   };
   ```

3. Thay **mọi** đường bật cheat cũ. Tìm mọi chỗ gán `isCheat = true` (hoặc cờ cheat tương đương) — có game có tới hai
   màn Settings, một màn bật cheat sau 10 tap **không kiểm gì cả** (vd. SM007 `SettingController`), và đoạn đồng bộ
   cờ cheat từ Remote Config sau mỗi lần fetch (vd. SM007 `GameRemoteConfig` → `SyncCheatStateFromRemoteConfig`) sẽ ghi
   đè lên kết quả của CheatGate. Bỏ hết, chỉ để lại một đường:

   ```csharp
   private void OnVersionTapped()
   {
       if (++_countCheat < 10) return;
       _countCheat = 0;
       CheatGate.TryUnlock(result =>
       {
           switch (result)
           {
               case CheatGateResult.Unlocked:
                   GameSystems.isCheat = true;          // cờ cheat của game (bật kèm event nếu game có)
                   break;
               case CheatGateResult.NotListed:
               case CheatGateResult.NoDeviceId:
                   GUIUtility.systemCopyBuffer = CheatGate.DeviceIdDisplay;
                   GameSystems.ShowSimpleMessage($"Máy chưa có quyền cheat.\nDevice ID: {CheatGate.DeviceIdDisplay} (đã copy)");
                   break;
               case CheatGateResult.Offline:
                   GameSystems.ShowSimpleMessage("Cần mạng để kiểm tra máy test.");
                   break;
               default:
                   GameSystems.ShowSimpleMessage($"Không kiểm tra được ({result}). Thử lại sau.");
                   break;
           }
       });
   }
   ```

   Chỗ nào hiện / dùng cheat thì kiểm thêm `CheatGate.IsUnlocked` (grant có thể hết hạn khi game nằm nền lâu ngày;
   `Changed(false)` báo lúc game quay lại).

4. Xoá phần whitelist cũ (key Remote Config, bảng Supabase `test_device`…) sau khi QA đã được thêm vào danh sách mới.

| | |
|---|---|
| `CheatGate.UnlockInEditor` | mặc định `true`: trong Editor luôn mở |
| `CheatGate.UnlockInDevelopmentBuild` | mặc định `false`: Development Build vẫn phải có tên trong danh sách |
| `CheatGate.IsChecking` | đang chờ server (để hiện loading nếu muốn) |
| `CheatGateResult.InvalidGrant` | server cấp nhưng chữ ký không khớp `CheatGateKeys.Trusted` (sai khoá — báo admin), hoặc câu trả lời không đúng nonce vừa gửi (bị phát lại) |
| `Initialize` lần 2 | cùng project: bỏ qua; project khác: bị từ chối (log error) trừ khi mã lần đầu không hợp lệ |

## Thêm / xoá máy test

Qua MCP `ezg` (Claude Code), tài khoản Lead / QA của project trong BugHub (hoặc owner/admin BugHub):

```
"Thêm máy 7F3A-9C21-B04E của QA Lan (Pixel 7) vào SM007"      -> test_device_add
"Xoá máy 7F3A-9C21-B04E khỏi SM007"                           -> test_device_remove
"Danh sách máy test của SM007"                                -> test_device_list
```

Có hiệu lực sau ~1-2 phút. Máy bị xoá mất cheat lần kế tiếp game kiểm lại khi có mạng (mở hoặc quay lại game, tối đa
30 phút/lần); offline thì khi grant đang giữ hết hạn (7 ngày). Sửa label một máy có hạn thì hạn được giữ nguyên
(`expires_in_days` để đặt hạn mới, `permanent` để bỏ hạn).

## Khoá ký

`CheatGateKeys.Trusted` giữ khoá công khai; khoá riêng là secret `GRANT_PRIVATE_KEY` của Worker
`ezg-cheat-gate` (repo `ezg-scopedregister/cheat-gate`, tạo bằng `scripts/keygen.mjs`). Kiểm hai bên khớp nhau:
`GET https://ezg-cheat-gate.developer-a1f.workers.dev/v1/keys` phải cùng modulus với `CheatGateKeys.cs`.
Đổi khoá: thêm khoá mới vào `Trusted`, release, rồi mới đổi secret của Worker, sau cùng mới bỏ khoá cũ.

## Test

EditMode tests (`Tests/Editor`, cần `"testables": ["com.ezg.cheat-gate"]` trong manifest để chạy): grant thật do
code Worker ký (`CheatGateTestVectors.cs`, sinh bằng `cheat-gate/scripts/make-test-vectors.mjs`), device id so với
node:crypto, toàn bộ state machine với transport / storage / đồng hồ giả.

## Giới hạn

Đây là cổng phía client: máy đã root / jailbreak sửa được code game thì vượt được mọi kiểm tra kiểu này. Mục tiêu là
người chơi bình thường không mở được cheat và QA không phải đụng Remote Config.
