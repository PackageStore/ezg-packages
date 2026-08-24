#if UNITY_EDITOR
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Chuyển đổi toạ độ giữa lưới (ô / nửa ô) và pixel canvas. Trục y lưới hướng LÊN trên
    /// màn hình (khớp +Z world).</summary>
    public sealed partial class VisualRoadBuilderTool
    {
        /// <summary>Toạ độ lưới dạng float (chưa snap) từ vị trí chuột.</summary>
        private Vector2 MouseToGridF(Rect canvas, Vector2 mouse)
        {
            float fx = (mouse.x - canvas.x - GutterLeft - OuterMargin) / _cellPixelSize;
            float fyScreen = (mouse.y - canvas.y - GutterTop - OuterMargin) / _cellPixelSize;
            return new Vector2(fx, (_gridHeight - 1) - fyScreen);
        }

        /// <summary>Như <see cref="PointToPixel"/> nhưng nhận toạ độ lưới float (cho station nửa ô).</summary>
        private Vector2 PointToPixelF(Rect canvas, float x, float y)
        {
            return new Vector2(
                canvas.x + GutterLeft + OuterMargin + x * _cellPixelSize,
                canvas.y + GutterTop + OuterMargin + (_gridHeight - 1 - y) * _cellPixelSize);
        }

        /// <summary>Điểm lưới → pixel trong canvas. Trục y lưới hướng LÊN trên màn hình (khớp +Z world).</summary>
        private Vector2 PointToPixel(Rect canvas, int x, int y)
        {
            return new Vector2(
                canvas.x + GutterLeft + OuterMargin + x * _cellPixelSize,
                canvas.y + GutterTop + OuterMargin + (_gridHeight - 1 - y) * _cellPixelSize);
        }

        /// <summary>Điểm lattice NỬA Ô gần chuột nhất (đơn vị nửa ô), KHÔNG kiểm tra biên — Move All
        /// snap 1/2 ô.</summary>
        private Vector2Int PixelToHalfPointRaw(Rect canvas, Vector2 mouse)
        {
            Vector2 f = MouseToGridF(canvas, mouse);
            return new Vector2Int(Mathf.RoundToInt(f.x * 2f), Mathf.RoundToInt(f.y * 2f));
        }

        /// <summary>Điểm lattice NỬA Ô gần chuột nhất — vẽ đường/highway + đặt decor (snap 1/2 ô).</summary>
        private bool TryPixelToHalfPoint(Rect canvas, Vector2 mouse, out Vector2Int p2)
        {
            Vector2 f = MouseToGridF(canvas, mouse);
            p2 = new Vector2Int(Mathf.RoundToInt(f.x * 2f), Mathf.RoundToInt(f.y * 2f));
            return p2.x >= 0 && p2.x <= (_gridWidth - 1) * 2
                   && p2.y >= 0 && p2.y <= (_gridHeight - 1) * 2;
        }
    }
}
#endif
