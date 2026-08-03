using System;
using UnityEngine;

namespace EZG.StatsOverlay
{
    /// <summary>
    /// Điểm vào duy nhất của module: overlay thống kê runtime (FPS, CPU/GPU, batches, tris/verts, memory…)
    /// luôn vẽ trên cùng mọi UI, giống cửa sổ Statistics của Unity Editor nhưng chạy được trên device.
    ///
    /// GATE HIỂN THỊ do phía dùng cấp qua <see cref="VisibilityProvider"/> — module KHÔNG biết gì về code game.
    /// Ví dụ trong sm006: <c>StatsOverlay.VisibilityProvider = () =&gt; GameSystems.isCheat;</c>
    /// Không set provider thì mặc định chỉ hiện trong Editor (an toàn: không lỡ ship overlay cho người chơi).
    /// </summary>
    public static class StatsOverlay
    {
        private static readonly StatsOverlayConfig _config = new StatsOverlayConfig();
        private static StatsOverlayView _view;
        private static Func<bool> _visibilityProvider;

        /// <summary>Cấu hình hiển thị. Sửa xong gọi <see cref="ApplyConfig"/>.</summary>
        public static StatsOverlayConfig Config => _config;

        /// <summary>
        /// Cổng hiển thị do phía dùng cấp (vd <c>() =&gt; GameSystems.isCheat</c>).
        /// Null = mặc định chỉ hiện trong Unity Editor.
        /// </summary>
        public static Func<bool> VisibilityProvider
        {
            get => _visibilityProvider;
            set => _visibilityProvider = value;
        }

        /// <summary>Công tắc tổng, cắt trên cả <see cref="VisibilityProvider"/>. Mặc định true.</summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>Đã có GameObject overlay trong scene chưa.</summary>
        public static bool IsInstalled => _view != null;

        /// <summary>Overlay đang thu gọn (chỉ còn dòng header FPS).</summary>
        public static bool IsCollapsed => _view != null ? _view.IsCollapsed : _config.StartCollapsed;

        /// <summary>Overlay có đang được phép hiện ở thời điểm này không.</summary>
        public static bool IsVisible => ShouldBeVisible();

        /// <summary>
        /// Tạo GameObject overlay (DontDestroyOnLoad, sống xuyên mọi scene). Gọi nhiều lần vô hại.
        /// <see cref="StatsOverlayBootstrap"/> đã tự gọi lúc app khởi động nên thường không cần gọi tay.
        /// </summary>
        public static void Install()
        {
            if (_view != null) return;

            var go = new GameObject("[EZG StatsOverlay]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _view = go.AddComponent<StatsOverlayView>();
        }

        /// <summary>Huỷ hẳn overlay (giải phóng luôn ProfilerRecorder).</summary>
        public static void Uninstall()
        {
            if (_view == null) return;

            UnityEngine.Object.Destroy(_view.gameObject);
            _view = null;
        }

        /// <summary>Áp lại <see cref="Config"/> sau khi sửa lúc runtime.</summary>
        public static void ApplyConfig()
        {
            if (_view != null) _view.RebuildFromConfig();
        }

        public static void SetCollapsed(bool collapsed)
        {
            if (_view != null) _view.SetCollapsed(collapsed);
            else _config.StartCollapsed = collapsed;
        }

        public static void ToggleCollapsed() => SetCollapsed(!IsCollapsed);

        internal static bool ShouldBeVisible()
        {
            if (!Enabled) return false;

            Func<bool> provider = _visibilityProvider;
            if (provider == null) return DefaultVisibility();

            try
            {
                return provider();
            }
            catch (Exception e)
            {
                // Provider hỏng (vd singleton game chưa init) → tắt overlay, KHÔNG để nó spam exception mỗi frame.
                Debug.LogWarning($"[StatsOverlay] VisibilityProvider ném exception, tạm tắt overlay: {e.Message}");
                _visibilityProvider = null;
                return false;
            }
        }

        private static bool DefaultVisibility()
        {
#if UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }
    }
}
