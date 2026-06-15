# EZG RPG Stats

Hệ thống chỉ số (stat) RPG **generic, tái sử dụng được** cho Unity: stat có modifier xếp chồng,
vital (máu/HP), linker, scale theo level, giới hạn min/max theo config, và hệ thống level/exp.

- **Package id:** `com.ezg.rpg-stats`
- **Assembly:** `Ezg.RpgStats` (+ `Ezg.RpgStats.Editor`)
- **Namespace:** `Ezg.Package.RpgStats`
- **Unity:** 2022.3+ (đã test trên 6000.2)

---

## 1. Ý tưởng cốt lõi — generic theo `TKey`

Package **không** chứa danh sách stat của riêng game nào. Toàn bộ API generic theo `TKey` —
là **khóa stat do project tự định nghĩa** (thường là một `enum`). Nhờ đó cùng một package dùng
được cho nhiều game với bộ stat khác nhau, không cần fork.

```
PACKAGE (generic, dùng chung)          PROJECT (mỗi game tự cung cấp)
────────────────────────────           ──────────────────────────────
RPGStatCollection<TKey>                enum RPGStatType { None = 0, ... }
RPGStat<TKey> / Modifiable / Attribute RpgStatsConfig : RpgStatsConfigBase<RPGStatType>
RPGVital<TKey>                         (lớp "manager"/init helper của game)
StatConfigs / StatConfigs<TKey>
RpgStatsConfigBase<TKey>
Modifiers, Linkers, Leveling, Editor tool
```

> ⚠️ Quy ước bắt buộc: enum khóa stat **phải có `None = 0`** (vì `RPGStat<TKey>` khởi tạo `StatType = default`).

---

## 2. Cài đặt

`Packages/manifest.json`:

```json
{
  "scopedRegistries": [
    {
      "name": "Easygoing code base",
      "url": "https://upm-registry-worker.developer-a1f.workers.dev",
      "scopes": ["com.ezg"]
    }
  ],
  "dependencies": {
    "com.ezg.rpg-stats": "0.1.0"
  }
}
```

**Peer dependency tự động:** `com.ezg.instance-factory` được khai báo trong `package.json` của
package này nên UPM tự kéo về — không cần thêm tay.

Trong asmdef của project tiêu thụ, thêm `"Ezg.RpgStats"` vào `references` (nếu project không dùng
asmdef thì package đã `autoReferenced` nên vẫn thấy).

---

## 3. Bắt đầu nhanh

### Cách A — Tự động (khuyên dùng cho project mới)

Menu **Create > Ezg > Rpg Stats > Project Setup** sinh sẵn mọi thứ per-project:

- `RPGStatType.cs` — enum khóa stat (stub, bạn sửa lại theo game)
- `RpgStatsConfig.cs` — lớp config concrete
- `RpgStatsBootstrap.cs` — loader gọi `Apply()` lúc boot
- `RpgStatsConfig.asset` — asset config (tạo tự động sau khi recompile)

Sửa đường dẫn/namespace sinh ra trong `RpgStatsProjectSetup.cs` nếu cần.

### Cách B — Thủ công

**B1. Khai báo enum khóa stat**
```csharp
namespace MyGame
{
    public enum RPGStatType { None = 0, Attack = 1, Health = 2, Defense = 3, CritRate = 4 }
}
```

**B2. Khai báo lớp config concrete** (đóng `TKey`; Unity không tạo asset từ generic được)
```csharp
using Ezg.Package.RpgStats;
using UnityEngine;

[CreateAssetMenu(menuName = "MyGame/Stats Config")]
public class MyStatsConfig : RpgStatsConfigBase<MyGame.RPGStatType> { }
```

**B3. Tạo asset** trong một thư mục `Resources/` rồi điền số trong Inspector.

**B4. Nạp config lúc khởi động** (một lần):
```csharp
Resources.Load<MyStatsConfig>("MyStatsConfig")?.Apply();
```

---

## 4. Dùng API

```csharp
using Ezg.Package.RpgStats;
using TKey = MyGame.RPGStatType;             // alias cho gọn (tùy chọn)

var stats = new RPGStatCollection<TKey>();

// Stat thường (Attribute)
var atk = stats.CreateOrGetStat<RPGAttribute<TKey>>(TKey.Attack);
atk.StatType = TKey.Attack;
atk.StatBaseValue = 100;

// Thêm modifier (update = true để tính lại ngay)
stats.AddStatModifier(TKey.Attack, new RPGStatModTotalAdd(20), update: true);   // +20 phẳng
stats.AddStatModifier(TKey.Attack, new RPGStatModTotalPercent(0.5f), true);     // +50%

float dmg = stats.GetStat(TKey.Attack).StatValue;   // đã gộp modifier + clamp theo config

// Vital (máu)
var hp = stats.CreateOrGetStat<RPGVital<TKey>>(TKey.Health);
hp.StatBaseValue = 500;
hp.SetCurrentValueToMax();
hp.StatCurrentValue -= 30;            // tự kẹp trong [0, StatValue]
hp.OnCurrentValueChange += (s, e) => UpdateHpBar();
```

### Các kiểu stat

| Kiểu | Mục đích |
|---|---|
| `RPGStat<TKey>` | base — `StatType`, `StatValue`, `StatBaseValue` |
| `RPGStatModifiable<TKey>` | thêm modifier xếp chồng + `OnValueChange` |
| `RPGAttribute<TKey>` | thêm linker + scale theo level (`ScaleStat(level)`) |
| `RPGVital<TKey>` | thêm `StatCurrentValue` (HP), `SetCurrentValueToMax()` |

### Modifier (thứ tự áp dụng theo `Order`)

| Lớp | Order | Công thức |
|---|---|---|
| `RPGStatModBasePercent` | 1 | `% trên (base)` |
| `RPGStatModBaseAdd` | 2 | cộng phẳng (sớm) |
| `RPGStatModTotalPercent` | 3 | `% trên (base + add trước đó)` |
| `RPGStatModTotalAdd` | 4 | cộng phẳng (cuối) |

Constructor: `(value)`, `(value, stacks)`, `(value, stacks, name)`.
`stacks = true` → cộng dồn các modifier cùng order; `false` → lấy modifier lớn nhất.

### Hệ thống level/exp

```csharp
public class HeroLevel : RPGEntityLevel
{
    public override int GetExpRequiredForLevel(int level) => level * 100 + 100;
}
// hero.ModifyExp(50); hero.SetLevel(10); hero.OnEntityLevelUp += ...;
```
`RPGEntityLevel` độc lập hoàn toàn với `TKey` — dùng được riêng.

---

## 5. Config & giới hạn stat

`RpgStatsConfigBase<TKey>` (asset) gồm các trường, nạp vào runtime qua `Apply()`:

| Trường | Tác dụng |
|---|---|
| `isCompactPercent` | `1 = 100%`. Modifier % chia `1` (true) hay `100` (false) |
| `forceLimitStat` | bật/tắt kẹp min/max |
| `listStatsPercentOnly` | danh sách stat chỉ tính dạng % |
| `statLimits` | mảng `StatLimitModel<TKey>` (type, minValue, maxValue) |

Runtime: `StatConfigs.IsCompactPercent` (toàn cục), `StatConfigs<TKey>.ValidStat(type, value)` (kẹp),
`StatConfigs<TKey>.ListStatsPercentOnly`. `StatValue` của stat tự gọi `ValidStat` nên giá trị trả về
đã được kẹp theo config.

---

## 6. Project phải tự cung cấp (KHÔNG có trong package)

| Thứ | Lý do |
|---|---|
| `enum RPGStatType` (khóa stat) | mỗi game có bộ stat riêng |
| Lớp config concrete + asset | đóng `TKey`; Unity cần type non-generic để tạo asset |
| Lớp "manager"/init helper | việc tạo bộ stat từ CSV, load icon, map dữ liệu… gắn chặt convention từng game |

(Trong Merge Two, các file này nằm ở `Assets/_Project/Features/_Shared/`:
`RPGStatType.cs`, `StatManager.cs`, `CharacterStats.cs`, `RpgStatsConfig.cs`.)

---

## 7. Phụ thuộc

| Loại | Tên |
|---|---|
| Scoped registry dependency | `com.ezg.instance-factory` (assembly `Ezg.InstanceFactory`) |
| Unity | `UnityEngine` (+ `UnityEditor` cho Editor tool) |

Không dùng Odin, không `Reflection.Emit` (an toàn IL2CPP/AOT — iOS).

---

## 8. Lưu ý kỹ thuật

- `RPGStatCollection<TKey>.Clone()` deep-copy theo **type thật** của stat (`is RPGVital<TKey>`),
  không hardcode khóa nào là vital.
- `CreateStat<T>` dùng `Ezg.InstanceFactory` để khởi tạo (không `Activator`/`Emit`).
- `StatConfigs` là static global — gọi `Apply()` một lần lúc boot; nhớ reset nếu cần giữa các scene/test.

---

## 9. Known limitations / debt

- **Scaffolder default path:** `Editor/RpgStatsProjectSetup.cs` mặc định sinh vào
  `Assets/_Project/Features/_Shared` (đường dẫn của project gốc Merge Two). Project tiêu thụ
  khác nên đổi `ROOT_FOLDER` (và `ASSET_FOLDER`) trong file đó sang đường dẫn của mình
  (vd `Assets/RpgStats`) trước khi chạy **Create > Ezg > Rpg Stats > Project Setup**.
