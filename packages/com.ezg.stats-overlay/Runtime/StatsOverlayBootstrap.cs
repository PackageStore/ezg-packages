using UnityEngine;

namespace EZG.StatsOverlay
{
    /// <summary>
    /// Tự dựng overlay lúc app khởi động — không cần kéo prefab vào scene, không cần sửa scene khởi động.
    ///
    /// Chỉ TẠO object; có hiện hay không do <see cref="StatsOverlay.VisibilityProvider"/> quyết định
    /// (mặc định: chỉ hiện trong Editor). Khi ẩn, overlay không tạo ProfilerRecorder nào.
    /// </summary>
    public static class StatsOverlayBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoInstall()
        {
            StatsOverlay.Install();
        }
    }
}
