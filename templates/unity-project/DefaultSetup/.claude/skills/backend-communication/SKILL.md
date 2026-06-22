---
name: backend-communication
description: Client-backend communication using Supabase (direct read) and Cloudflare Workers (validated write) via GameNetworkManager. Use when a feature reads server data, upserts/deletes records, or claims rewards with server-side validation.
---

# Backend Communication System

## Architecture Overview

```
Client ──READ──► Supabase (direct, read-only)
Client ──WRITE──► Cloudflare Workers ──► Supabase (write via proxy + validation)
```

- **READ**: client reads **directly from Supabase** via `GameNetworkManager.Instance.Supabase()`.
- **WRITE** (Upsert/Delete/Claim): client calls **Cloudflare Workers** through `GameNetworkManager.Endpoint<T>(...)`, which validates and writes to Supabase.
- Client **NEVER** writes directly to Supabase, and **NEVER** reads through Cloudflare.

The networking layer ships as the UPM package **`com.ezg.networking`** (namespace `Ezg.Core.Networking`).

## Core Components

### 1. GameNetworkManager (`Assets/_Project/Features/_Shared/Networking/GameNetworkManager.cs`)
- `public class GameNetworkManager : SupabaseManager<GameNetworkManager>` — Singleton, namespace `Ezg.Feature.Networking`.
- `Instance.Supabase()` → the Supabase `Client` (read side).
- `static Endpoint<T>(string endPoint)` → a `CloudflareQuery<T>` (write side).
- `async UniTask<bool> ValidActionFunction()` → checks internet, login (`ProfileManager.IsLogon()`), account id, and online status before any operation.
- `bool IsOnline` — current Supabase session status.

### 2. CloudflareQuery<T> (package: `Ezg.Core.Networking`)
Fluent builder for Cloudflare Worker requests.

| Method | Verb | Purpose |
|--------|------|---------|
| `Get(Action onFail = null)` | POST | Write-with-response — server runs logic and returns a `List<T>` (e.g. claim rewards). NOT a read. |
| `Upsert(T data)` | PUT | Insert/update a single record |
| `Upsert(List<T> data)` | PUT | Insert/update many records |
| `Delete()` | PUT | Delete records (use with `Where`) |
| `Where(Expression<Func<T,bool>>)` | — | Filter; supports `==` and `&&` only. Keys use the `[JsonProperty]` name. |
| `WithTimeout(int seconds)` | — | Request timeout (default 3s) |

`Endpoint<T>(...)` resolves to `{CloudflareDB.BaseUrl}/{endpoint}` where `BaseUrl => Settings.WorkerURL` (configured in `CloudflareSettings`, not hardcoded).

### 3. ApiResponse<T>
Workers wrap responses as `{ int Status; T Data; string Error; }` (json keys `status`/`data`/`error`). `Get()` auto-unwraps `ApiResponse<List<T>>` and returns `List<T>`.

## Data Model Requirements

### Supabase direct read (Postgrest model)
Pattern from `Assets/_Project/Features/Social/GiftCode/Scripts/Data/GiftCodeModel.cs`:

```csharp
using Postgrest.Attributes;
using Postgrest.Models;

[Table("gift_codes")]
public class GiftCodeModel : BaseModel
{
    [PrimaryKey("id", true)]
    public string Id { get; set; }

    [Column("res_type")]
    public string ResType { get; set; }

    [Column("expired_time")]
    public long ExpiredTime { get; set; }
}
```

Required: inherit `BaseModel`, `[Table("...")]`, `[PrimaryKey("...")]` on the key, `[Column("...")]` (snake_case DB columns).

### Cloudflare-only payloads (no direct read)
Plain DTOs with `[JsonProperty("snake_case")]` on every field; `[Serializable]` recommended.

## Usage Patterns

### Pattern 1 — READ from Supabase (direct)
From `GiftCodeManager.ClaimGiftCode`:

```csharp
var supabase = GameNetworkManager.Instance.Supabase();
if (supabase == null)
{
    GameSystems.ShowSimpleMessage("gift_code_wrong");
    return;
}

var response = await supabase
    .From<GiftCodeModel>()
    .Where(x => x.Id == accountId && x.ExpiredTime > now)   // or .Filter("id", Constants.Operator.Equals, code)
    .Get()
    .AsUniTask()
    .Timeout(TimeSpan.FromSeconds(10));

var result = response?.Models?.FirstOrDefault();
```

### Pattern 2 — WRITE via Cloudflare (Upsert)
```csharp
private const string EndPoint_Update = "features/myfeature/update";

model.SomeField = newValue;
await GameNetworkManager.Endpoint<MyModel>(EndPoint_Update).Upsert(model);

// multiple
await GameNetworkManager.Endpoint<MyModel>(EndPoint_Update).Upsert(new List<MyModel> { a, b });
```

### Pattern 3 — DELETE via Cloudflare
```csharp
private const string EndPoint_Delete = "features/myfeature/del";

await GameNetworkManager.Endpoint<MyModel>(EndPoint_Delete)
    .Where(x => x.Id == targetId)
    .Delete();
```

### Pattern 4 — WRITE with server validation (Claim)
Use `Endpoint.Get()` when the server must run logic, write, and return confirmation (anti-cheat claims). This is a **write that returns data**, not a read.

```csharp
private const string EndPoint_Claim = "features/myfeature/claim";

var result = await GameNetworkManager.Endpoint<MyModel>(EndPoint_Claim)
    .Where(x => x.Id == targetId)
    .Get(() => GameSystems.ShowSimpleMessage("error_message"));

if (result.Any() /* && result[0].IsSuccess */)
{
    // apply server-confirmed result to local data
}
```

## Implementation Checklist

1. **Endpoints** — `features/{feature}/{get|update|del|claim}` as `const string`.
2. **Validate first** (skip in Editor for dev convenience):
   ```csharp
#if !UNITY_EDITOR
   if (!await GameNetworkManager.Instance.ValidActionFunction()) return;
#endif
   ```
3. **UniTask** — all network calls are `UniTask`/`UniTask<T>`; `async/await` throughout. Wrap reads in `try/catch` + `.Timeout(...)`.
4. **Loading UI** for writes — `GameSystems.ShowWaitingScreen(true/false)`.
5. **Emit events** after data changes — `EventManager.EmitEvent(nameof(EventName.MyEvent))` (TigerForge) for UI refresh.

## Real-World References
- `Assets/_Project/Features/Social/GiftCode/Scripts/Controller/GiftCodeManager.cs` — Supabase read + claim flow.
- `Assets/_Project/Features/System/Admin/Scripts/Controller/AdminManager.cs` — `From<AdminModel>().Where(...).Get()` reads.
- `Assets/_Project/Features/Social/Account/Scripts/Controller/ProfileManager.cs` / `PlayerDataSyncManager.cs` — profile read/upsert + sync.

## Important Notes
- `Where()` supports only `==` and `&&`; keys come from `[JsonProperty]`.
- Default timeout 3s — raise with `WithTimeout()` for long ops.
- `Get()` is **write-with-response**; all plain reads go through `Supabase().From<T>()`.
- `Upsert()`/`Delete()` return `UniTask` with no payload.
- Always guard with `#if !UNITY_EDITOR` around `ValidActionFunction()`.
