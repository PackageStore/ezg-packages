# EZG Stats Overlay

Overlay thống kê runtime luôn vẽ **trên cùng** mọi UI của app — bản chạy-được-trên-device của cửa sổ
*Statistics* trong Unity Editor: FPS, CPU main / render thread, GPU, batches, saved by batching, SetPass,
draw calls, tris/verts, shadow casters, độ phân giải + memory (system / total / GC / texture / mesh).

Package **tự đóng, không tham chiếu code game**. Việc "khi nào được hiện" do phía dùng cấp qua một delegate.

| | |
|---|---|
| Package | `com.ezg.stats-overlay` |
| Assembly | `Ezg.StatsOverlay` (Runtime) — không có Editor assembly |
| Nguồn | sm006 · `Assets/_Project/Core/Modules/StatsOverlay` |
| Unity tối thiểu | 2022.3 |

## Cài đặt

```json
"scopedRegistries": [
  { "name": "Easygoing code base", "url": "https://upm-registry-worker.developer-a1f.workers.dev", "scopes": ["com.ezg"] }
],
"dependencies": { "com.ezg.stats-overlay": "0.1.0" }
```

## Dùng nhanh

Không cần kéo prefab hay sửa scene — `StatsOverlayBootstrap` tự dựng overlay lúc app khởi động
(`RuntimeInitializeOnLoadMethod` + `DontDestroyOnLoad`). Chỉ cần cắm cổng hiển thị:

```csharp
using StatsOverlayApi = EZG.StatsOverlay.StatsOverlay;

[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
private static void Bind()
{
    StatsOverlayApi.VisibilityProvider = () => MyGame.IsCheatEnabled;   // gate của riêng game bạn
}
```

Không set `VisibilityProvider` thì mặc định **chỉ hiện trong Unity Editor** — cố ý như vậy để không lỡ
ship overlay cho người chơi.

## Tương tác

| Thao tác | Kết quả |
|---|---|
| **Tap** vào panel | thu gọn / mở rộng (thu gọn còn 1 dòng header FPS) |
| **Kéo** panel | đổi vị trí, tự kẹp trong màn hình |

Overlay **không có `GraphicRaycaster`** và mọi Graphic đều `raycastTarget = false` ⇒ không bao giờ nuốt
input của game. Kéo thả tự đọc `Input` + test điểm chạm trong rect panel (cần `ENABLE_LEGACY_INPUT_MANAGER`;
project chỉ dùng Input System mới thì phần kéo thả tự tắt, các phần còn lại vẫn chạy).

## API

```csharp
StatsOverlay.VisibilityProvider     // Func<bool> — cổng hiển thị do game cấp
StatsOverlay.Enabled                // công tắc tổng, cắt trên cả provider
StatsOverlay.Config                 // xem bảng dưới; sửa xong gọi ApplyConfig()
StatsOverlay.ApplyConfig()
StatsOverlay.Install() / Uninstall()
StatsOverlay.SetCollapsed(bool) / ToggleCollapsed()
StatsOverlay.IsVisible / IsCollapsed / IsInstalled
```

`StatsOverlayConfig`: `RefreshInterval` (0.5s), `FontSize`, `Corner`, `Margin`, `BackgroundColor`,
`TextColor`, `HeaderColor`, `ShowGraphics`, `ShowMemory`, `Draggable`, `StartCollapsed`, `SortingOrder`
(32760), `ReferenceResolution` (1080×1920), `RespectSafeArea`.

## Giới hạn theo loại build (quan trọng)

Số render/memory lấy từ `ProfilerRecorder`, mà Unity **chỉ bơm counter trong Editor và Development Build**.

| Build | FPS / frame ms / độ phân giải | Batches, SetPass, tris/verts, memory |
|---|---|---|
| Editor | ✅ | ✅ |
| Development Build | ✅ | ✅ |
| Release Build | ✅ (tự đo, không qua profiler) | **`n/a`** — Unity strip counter |

Overlay tự in `n/a` + một dòng ghi chú thay vì báo lỗi. Tên counter đổi theo phiên bản Unity cũng chỉ ra
`n/a`, không ném exception.

## Chi phí khi TẮT

Khi `VisibilityProvider` trả `false`: panel `SetActive(false)`, **toàn bộ `ProfilerRecorder` được Dispose**,
mỗi frame chỉ còn 1 lần gọi delegate. Bật lại thì recorder được tạo lại.

## Phụ thuộc

- **Package dependencies:** không có.
- **Peer requirements:** không có gì ngoài Unity + `com.unity.ugui` (uGUI, có sẵn trong mọi project Unity).
  Assembly `UnityEngine.UI` được khai báo trong asmdef.

## Ghi chú kỹ thuật

- `saved by batching` tính đúng công thức cửa sổ Statistics của Editor: tổng `(batched draw calls − batches)`
  của static + dynamic + instancing.
- `Screen: WxH - N MB` ước lượng backbuffer 12 byte/pixel (color front + back + depth/stencil), khớp số
  Editor hiển thị.
- Khối **Audio** của cửa sổ Statistics (dB, DSP load, clipping) **không có** ở đây: nguồn của nó là
  `UnityEditor.UnityStats`, runtime không truy cập được.
- Font: Unity 6 bỏ `Arial.ttf` (gọi là **ném exception**) → dùng `LegacyRuntime.ttf`, có fallback font OS.

## Known debt

Không có. Module chỉ `using` System / `Unity.Profiling` / `UnityEngine[.UI]`; không đụng singleton, CSV
path hay enum của game nào.
