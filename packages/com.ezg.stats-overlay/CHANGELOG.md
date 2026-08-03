# Changelog

## [0.1.0] - 2026-08-03
### Added
- Initial release extracted from sm006 `Assets/_Project/Core/Modules/StatsOverlay`.
- `StatsOverlay` static API: `VisibilityProvider` (delegate gate do game cấp), `Enabled`, `Config`,
  `Install`/`Uninstall`, `ApplyConfig`, `SetCollapsed`/`ToggleCollapsed`, `IsVisible`/`IsCollapsed`/`IsInstalled`.
- `StatsOverlayBootstrap`: tự dựng overlay lúc app khởi động (`RuntimeInitializeOnLoadMethod` +
  `DontDestroyOnLoad`) — không cần prefab hay sửa scene.
- `StatsOverlayView`: canvas riêng `sortingOrder` 32760 luôn vẽ trên cùng, không `GraphicRaycaster` nên
  không nuốt input game; tap để thu gọn, kéo để đổi vị trí, tôn trọng safe area.
- `StatsProbe`: FPS/frame time tự đo mỗi frame + đọc counter render/memory qua `ProfilerRecorder`
  (batches, saved by batching, SetPass, draw calls, tris/verts, shadow casters, skinned meshes,
  CPU main/render thread, GPU, system/total/GC/texture/mesh memory). Counter không có ở build Release
  hiện `n/a` thay vì lỗi; recorder chỉ được tạo khi overlay thật sự hiện.
- `StatsOverlayConfig`: chu kỳ refresh, cỡ chữ, góc neo, lề, màu, bật/tắt từng khối, kéo thả,
  sorting order, reference resolution, safe area.
