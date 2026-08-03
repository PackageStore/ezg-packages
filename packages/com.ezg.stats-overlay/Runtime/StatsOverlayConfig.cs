using UnityEngine;

namespace EZG.StatsOverlay
{
    /// <summary>Góc neo mặc định của panel overlay (trước khi người dùng kéo thả).</summary>
    public enum StatsOverlayCorner
    {
        TopLeft = 0,
        TopRight = 1,
        BottomLeft = 2,
        BottomRight = 3,
    }

    /// <summary>
    /// Cấu hình hiển thị của <see cref="StatsOverlay"/>. Sửa runtime được: đổi field rồi gọi
    /// <see cref="StatsOverlay.ApplyConfig"/> để view dựng lại theo giá trị mới.
    /// </summary>
    public sealed class StatsOverlayConfig
    {
        /// <summary>Chu kỳ vẽ lại text (giây). FPS vẫn được lấy mẫu MỖI frame, chỉ text là gom theo chu kỳ.</summary>
        public float RefreshInterval = 0.5f;

        /// <summary>Cỡ chữ, tính theo <see cref="ReferenceResolution"/>.</summary>
        public int FontSize = 26;

        /// <summary>Góc neo mặc định.</summary>
        public StatsOverlayCorner Corner = StatsOverlayCorner.TopLeft;

        /// <summary>Lề so với mép màn hình (đơn vị canvas), cộng thêm safe area nếu <see cref="RespectSafeArea"/>.</summary>
        public Vector2 Margin = new Vector2(24f, 24f);

        public Color BackgroundColor = new Color(0f, 0f, 0f, 0.72f);
        public Color TextColor = Color.white;
        public Color HeaderColor = new Color(0.55f, 1f, 0.55f, 1f);

        /// <summary>Hiện khối Graphics (FPS/CPU/GPU/batches/tris/verts…).</summary>
        public bool ShowGraphics = true;

        /// <summary>Hiện khối Memory (system/total/GC/texture/mesh).</summary>
        public bool ShowMemory = true;

        /// <summary>Cho phép kéo thả panel bằng chạm/chuột. Tap (không kéo) = thu gọn/mở rộng.</summary>
        public bool Draggable = true;

        /// <summary>Mở app ở trạng thái thu gọn (chỉ còn dòng header FPS).</summary>
        public bool StartCollapsed = false;

        /// <summary>Sorting order của canvas overlay — để rất cao cho nằm trên mọi UI game.</summary>
        public int SortingOrder = 32760;

        /// <summary>Reference resolution cho CanvasScaler (khớp khổ dọc của game).</summary>
        public Vector2 ReferenceResolution = new Vector2(1080f, 1920f);

        /// <summary>Tránh tai thỏ / thanh trạng thái khi đặt vị trí mặc định.</summary>
        public bool RespectSafeArea = true;
    }
}
