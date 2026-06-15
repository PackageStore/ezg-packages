# EZG Firebase (`com.ezg.firebase`)

Lớp hạ tầng Firebase tái sử dụng cho game Unity: **Authentication** (Google / Apple / Game Center), **Firestore**, **Cloud Functions**, **Remote Config** và **Storage** (đồng bộ save data). Phần đặc thù game (key Remote Config, mapping...) được giữ **ngoài** package.

| Thuộc tính | Giá trị |
|---|---|
| Package id | `com.ezg.firebase` |
| Display name | EZG Firebase |
| Runtime asmdef | `Ezg.Core.Firebase` |
| Editor asmdef | `Ezg.Core.Firebase.Editor` |
| Namespace (runtime) | `Ezg.Core.Firebase` |
| Namespace (editor) | `Ezg.Core.Firebase.Editor` |
| Unity tối thiểu | `2022.3` |

---

## Package ↔ source folder

Package này được trích từ `Assets/_Project/Core/Infrastructure/Firebase` của project game. Map thư mục:

| Trong package | Nguồn |
|---|---|
| `Runtime/*.cs` | các file `.cs` ở gốc thư mục Firebase |
| `Editor/FirebaseConfigCreator.cs` | `Firebase/Editor/FirebaseConfigCreator.cs` |

---

## Dependencies

### Scoped registry (`com.ezg.*`)

**Không có.** Package này không phụ thuộc package `com.ezg.*` nào. (Hai `using Ezg.Core.Extensions` / `using Ezg.Core.Utils` trong `FirebaseStorageManager` của bản gốc là leftover không được dùng — đã loại bỏ khi đóng package.)

### Peer requirements — consumer phải tự cung cấp

Các lib sau **không** nằm trong `package.json.dependencies`; project tiêu thụ phải có sẵn (thường là precompiled DLL được Auto Reference, hoặc asmdef tương ứng):

| Lib | Dùng cho | Ghi chú |
|---|---|---|
| **UniTask** (Cysharp) | async toàn module | khai báo trong asmdef `references` (`UniTask`). |
| **Firebase SDK** (DLL) | tất cả manager | `Firebase.App`, `Firebase.Auth`, `Firebase.Firestore`, `Firebase.Functions`, `Firebase.RemoteConfig`, `Firebase.Storage`, `Firebase.TaskExtension` — auto-referenced. |
| **Newtonsoft.Json** | serialize save (`FirebaseStorageManager`) | auto-referenced. |
| **Google.Play.Games** | đăng nhập Google | chỉ compile khi bật define `GPG_LOGIN`. |
| **AppleAuth** (lupidan/apple-signin-unity) | Sign in with Apple (iOS) | chỉ compile dưới `#if UNITY_IOS`. |
| **Apple.GameKit** (`com.apple.unityplugin.gamekit`) | đăng nhập Game Center (iOS) | chỉ compile dưới `#if UNITY_IOS`. |

> Khi build iOS hoặc bật `GPG_LOGIN`, đảm bảo các lib tương ứng đã được cài và auto-referenced trong project (vì asmdef của package là immutable, các provider có guard sẽ chỉ biên dịch khi assembly liên quan hiện diện).

### Cấu hình Firebase phía app (bắt buộc khi tích hợp)

- `google-services.json` (Android) / `GoogleService-Info.plist` (iOS) của project Firebase tương ứng.
- Gọi `Firebase.FirebaseApp.CheckAndFixDependenciesAsync()` trước khi dùng các manager.

---

## Cấu hình — `FirebaseConfig`

Mọi giá trị tuning nằm trong ScriptableObject `FirebaseConfig`. Mỗi project tạo 1 asset đặt trong thư mục `Resources` tên `FirebaseConfig` để custom **mà không sửa code package**. Nếu không tìm thấy asset, package dùng instance default.

| Nhóm | Field | Mặc định |
|---|---|---|
| Storage | `StorageBucketUrl` | `gs://m1-food-merge.firebasestorage.app` ⚠️ đổi cho project khác |
| | `PlayerDataFolder` | `PlayersData` |
| | `MaxDownloadSizeBytes` | `1048576` (1 MB) |
| Auth | `SignInTimeoutSeconds` | `30` |
| | `AppleUserIdPrefsKey` | `AppleUserId` |
| Game Center (iOS) | `MaxCredentialAttempts` | `6` |
| | `CredentialRetryDelayMs` | `1000` |
| | `GameCenterAuthTimeoutSeconds` | `15` |
| | `PlayerAuthWaitTimeoutSeconds` | `5` |
| | `PlayerAuthPollDelayMs` | `150` |
| | `PostNativeAuthSettleDelayMs` | `750` |
| | `PostProviderReadySettleDelayMs` | `500` |
| Remote Config | `MinimumFetchIntervalMs` | `0` |

```csharp
var bucket = FirebaseConfig.Instance.StorageBucketUrl;
```

---

## Editor tooling

**Create > Ezg > Firebase > Firebase Config** (`FirebaseConfigCreator`):
1. Tạo asset `FirebaseConfig.asset` (mặc định tại `Assets/_Project/Resources`).
2. Scaffold `GameRemoteConfig.cs` skeleton (mặc định tại `Assets/_Project/Features/_Shared`) — file game-specific, **không** đi kèm package.

> Hai đường dẫn mặc định trên theo layout project gốc; đổi trong `FirebaseConfigCreator` nếu project có cấu trúc khác.

---

## API & cách dùng

### Authentication — `FirebaseLoginManager`
```csharp
FirebaseLoginManager.SignInWithGoogle(onSuccess, onFail).Forget();
FirebaseLoginManager.OnGoogleLogout(onLoggedOut);

var user = FirebaseLoginManager.GetUserData();
bool valid = await FirebaseLoginManager.ValidateCurrentUserSession();

#if UNITY_IOS
FirebaseLoginManager.InitAppleAuthManager();   // gọi 1 lần
FirebaseLoginManager.TickAppleAuthManager();   // gọi mỗi frame (Update)
FirebaseLoginManager.SignInWithApple(onSuccess, onFail).Forget();
FirebaseLoginManager.SignInWithGameCenter(onSuccess, onFail).Forget();
#endif
```

### Firestore — `FirebaseFirestoreManager`
```csharp
FirebaseFirestoreManager.Init();
var db = FirebaseFirestoreManager.GetSource();
```

### Cloud Functions — `FirebaseFunctionManager`
```csharp
var callable = FirebaseFunctionManager.Function.GetHttpsCallable("checkEmailExists");
var result = await callable.CallAsync(payload);
```

### Storage / save-sync — `FirebaseStorageManager` (`ISyncData`)
```csharp
ISyncData sync = new FirebaseStorageManager();
await sync.PushPlayerData(json, fileName, onFail, onSuccess);
byte[] data = await sync.GetPlayerData(fileName);
await sync.DeleteData(fileName, onFail, onSuccess);
DateTime savedAt = sync.GetTimeCreateData();
```

### Nonce
```csharp
string nonce = Nonce.GenerateNonce(256);
```

---

## Remote Config — tách 2 lớp

- **`FirebaseRemoteManager`** (trong package) — *generic*: init, fetch & activate, getter theo type. **Không** biết key game.
  ```csharp
  FirebaseRemoteManager.InitRemoteConfig();
  int   a = FirebaseRemoteManager.GetInt("some_key", defaultValue: 0);
  bool  b = FirebaseRemoteManager.GetBool("some_flag");
  string c = FirebaseRemoteManager.GetString("some_text");
  bool has = FirebaseRemoteManager.HasKey("some_key");
  ```
- **`GameRemoteConfig`** (phía game, **ngoài package**) — đọc key đặc thù game, map vào hệ thống game. Wire vào callback trước khi fetch:
  ```csharp
  FirebaseRemoteManager.OnRemoteConfigApplied = GameRemoteConfig.Apply;
  FirebaseRemoteManager.InitRemoteConfig();
  ```

---

## Platform & define symbols

| Symbol / guard | Ảnh hưởng |
|---|---|
| `#if UNITY_IOS` | Bật `FirebaseAppleLoginProvider`, `FirebaseGameCenterLoginProvider` và API Apple/Game Center. Cần **Apple.GameKit** + **AppleAuth**. |
| `#if GPG_LOGIN` | Bật luồng Google Play Games. Cần **Google.Play.Games**. |

---

## Khởi tạo (bootstrap)

```csharp
await Firebase.FirebaseApp.CheckAndFixDependenciesAsync();
FirebaseFirestoreManager.Init();
FirebaseRemoteManager.OnRemoteConfigApplied = GameRemoteConfig.Apply;
FirebaseRemoteManager.InitRemoteConfig();
```

`FirebaseLoginManager`, `FirebaseFunctionManager`, `FirebaseStorageManager` dùng static constructor nên tự khởi tạo `DefaultInstance` ở lần truy cập đầu.

---

## Cài đặt qua scoped registry

`Packages/manifest.json`:
```json
"scopedRegistries": [
  { "name": "Easygoing code base", "url": "https://upm-registry-worker.developer-a1f.workers.dev", "scopes": ["com.ezg"] }
],
"dependencies": { "com.ezg.firebase": "0.1.0" }
```
