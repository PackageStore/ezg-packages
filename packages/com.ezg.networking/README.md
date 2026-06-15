# EZG Networking (`com.ezg.networking`)

Namespace: `Ezg.Core.Networking` · Assembly: `Ezg.Core.Networking` (+ `Ezg.Core.Networking.Editor`)

Lớp hạ tầng networking cho game, gồm 2 backend độc lập:

- **Cloudflare Worker** — REST endpoint dạng key-value/collection, query bằng LINQ expression.
- **Supabase** — wrapper quanh client chính thức (auth + realtime + DB), kèm cơ chế lưu session local.

Mọi cấu hình (URL, key) đều nạp từ **ScriptableObject asset trong `Resources/`**, không hard-code trong code → đổi endpoint/key không cần build lại.

---

## Install

`Packages/manifest.json`:

```json
"scopedRegistries": [
  { "name": "Easygoing code base", "url": "https://upm-registry-worker.developer-a1f.workers.dev", "scopes": ["com.ezg"] }
],
"dependencies": {
  "com.ezg.networking": "0.1.0"
}
```

### Dependencies (auto-resolved from the registry)

| Package | Version | Why |
|---|---|---|
| `com.ezg.singleton` | `0.1.1` | base `Singleton<T>` for `SupabaseManager<T>` |
| `com.ezg.supabase` | `0.1.0` | vendored Supabase DLL bundle (Supabase, GoTrue, PostgREST, Realtime, Storage, Functions) |

### Peer requirements (consumer MUST already provide — NOT bundled)

| Lib | Suggested package |
|---|---|
| **UniTask** | `com.cysharp.unitask` |
| **Newtonsoft.Json** | `com.unity.nuget.newtonsoft-json` (13.x) |

These are referenced by assembly name and resolved from the consuming project. If they are missing, `Ezg.Core.Networking` will not compile.

---

## Package ↔ source mapping

```
Runtime/
├── Cloudflare/
│   ├── CloudflareSettings.cs   # ScriptableObject config (WorkerURL, ApiSecretKey)
│   ├── CloudflareDB.cs         # static entry point + ApiResponse<T> wrapper
│   └── CloudflareQuery.cs      # query builder (Where / Get / Upsert / Delete)
└── Supabase/
    ├── SupabaseSettings.cs     # ScriptableObject config (SupabaseURL, SupabaseAnonKey)
    ├── SupabaseManager.cs      # Singleton<T> quản lý Supabase.Client
    └── UnitySession.cs         # lưu/đọc session ra persistentDataPath
Editor/
└── NetworkingProjectSetup.cs   # Create ▸ Ezg ▸ Networking ▸ Project setup (sinh GameNetworkManager)
```

---

## 1. Cấu hình (bắt buộc làm trước)

Cả 2 service đều load asset theo **tên cố định** trong thư mục `Resources/` của project tiêu thụ:

| Service    | Asset path                          | Load bằng                                           |
|------------|-------------------------------------|-----------------------------------------------------|
| Cloudflare | `Assets/Resources/Cloudflare.asset` | `Resources.Load<CloudflareSettings>("Cloudflare")`  |
| Supabase   | `Assets/Resources/Supabase.asset`   | `Resources.Load<SupabaseSettings>("Supabase")`      |

> ⚠️ Tên file asset phải đúng `Cloudflare` và `Supabase` (không đuôi). Đổi tên = `Resources.Load` trả `null`.

Tạo asset nhanh nhất bằng **Create ▸ Ezg ▸ Networking ▸ Project setup** (xem mục 4) — sinh sẵn cả hai asset đúng path. Hoặc tạo tay: chuột phải Project window → **Create ▸ Ezg ▸ Networking ▸ Cloudflare ▸ Cloudflare Settings** (hoặc **… ▸ Supabase ▸ Supabase Settings**), đặt vào thư mục `Resources/`.

> 🔐 **Bảo mật:** asset nằm trong `Resources/` nên được đóng gói vào build và **có thể bị trích xuất**. Chỉ để key dạng public/anon. Kiểm soát quyền phải ở phía server (Worker auth, Supabase Row Level Security).

---

## 2. Cloudflare

Không cần init thủ công — `CloudflareDB` là static, tự lazy-load asset config ở lần dùng đầu tiên. Map field theo JSON key của Worker bằng `[JsonProperty]`.

```csharp
// Đọc
List<PlayerScore> mine = await CloudflareDB
    .Endpoint<PlayerScore>("scores")
    .Where(x => x.UserId == myId && x.Score == 100)
    .WithTimeout(5)                       // mặc định 3s
    .Get(onFail: () => Debug.LogError("Tải bảng điểm thất bại"));

// Ghi (upsert 1 hoặc nhiều record)
await CloudflareDB.Endpoint<PlayerScore>("scores")
    .Upsert(new PlayerScore { UserId = myId, Score = 250, Name = "Alice" });

// Xóa
await CloudflareDB.Endpoint<PlayerScore>("scores")
    .Where(x => x.UserId == myId)
    .Delete();
```

`Where` chỉ hỗ trợ `==` và `&&` (toán tử khác → `NotSupportedException`); vế so sánh phải là **property** (nên có `[JsonProperty]`). `Get` log lỗi + gọi `onFail` (không throw, trả `null` khi response rỗng); `Upsert`/`Delete` ném `Exception` khi HTTP fail — bọc `try-catch`.

---

## 3. Supabase

`SupabaseManager<T>` kế thừa `Singleton<T>` — tạo một MonoBehaviour cụ thể trong scene/bootstrap:

```csharp
public class GameSupabase : SupabaseManager<GameSupabase> { }

await GameSupabase.Instance.Init();
if (GameSupabase.Instance.IsOnline)
{
    var client = GameSupabase.Instance.Supabase();   // Supabase.Client, có thể null
}
```

`Init()` tự bỏ qua khi không có mạng (`IsOnline = false`) và bọc sẵn `try-catch` quanh `InitializeAsync`. `Supabase()` trả `null` nếu chưa init/init lỗi — luôn null-check. Client tự dọn trong `OnApplicationQuit`.

### Lưu session local — `UnitySession`

`UnitySession` hiện thực `IGotrueSessionPersistence<Session>`, ghi session ra `Application.persistentDataPath/gotrue.cache` qua `System.IO` (an toàn với background refresh thread — **không** dùng PlayerPrefs). Gắn vào `SupabaseOptions.SessionHandler` khi muốn persist đăng nhập giữa các phiên (mặc định `Init()` chưa gắn).

---

## 4. Project setup — sinh `GameNetworkManager`

**Create ▸ Ezg ▸ Networking ▸ Project setup** làm 2 việc:

1. Sinh file facade `GameNetworkManager` (`Ezg.Feature.Networking`) kế thừa `SupabaseManager<GameNetworkManager>` và expose `Endpoint<T>` của Cloudflare.
2. Tạo sẵn 2 asset config mặc định **đúng path** `Assets/_Project/Resources/Cloudflare.asset` và `Assets/_Project/Resources/Supabase.asset`.

Mỗi mục đều kiểm tra file/asset đã tồn tại và **hỏi xác nhận trước khi ghi đè** (chọn giữ nguyên thì giá trị hiện tại không bị đụng tới).

> ⚠️ **Known assumption / debt:** tool ghi ra path cố định `Assets/_Project/Features/_Shared/GameNetworkManager.cs` — giả định cấu trúc thư mục của project gốc. Tool chỉ sinh code (không phụ thuộc runtime vào singleton/CSV game-specific); đổi project khác có thể cần sửa path trong `NetworkingProjectSetup.cs`.
