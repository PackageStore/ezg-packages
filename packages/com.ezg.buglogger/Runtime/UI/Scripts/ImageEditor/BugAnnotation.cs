using UnityEngine;

namespace Ezg.BugLogger.UI
{
    /// <summary>Loại nét vẽ QA đặt lên ảnh chụp.</summary>
    internal enum BugAnnotationKind
    {
        Pen,
        Rect,
        Ellipse,
        Arrow,
        Marker,
        Spotlight
    }

    /// <summary>
    ///     Một nét / hình QA vẽ lên ảnh. Toạ độ tính theo pixel ảnh gốc, gốc ở góc trái-dưới (như texture Unity) — ảnh
    ///     cắt hay thu nhỏ thì nét đi theo ảnh. Không đổi sau khi đã thêm vào <see cref="BugImageDocument" /> (lịch sử
    ///     undo dùng chung tham chiếu).
    /// </summary>
    internal sealed class BugAnnotation
    {
        #region Fields

        public readonly BugAnnotationKind Kind;
        public readonly Color32 Color;

        /// <summary>Độ dày nét (px ảnh gốc). Với <see cref="BugAnnotationKind.Marker" /> là bán kính huy hiệu.</summary>
        public readonly float Width;

        /// <summary>
        ///     Pen: các điểm của nét · Rect / Ellipse / Spotlight: hai góc đối · Arrow: [đuôi, đầu] · Marker: [tâm].
        /// </summary>
        public readonly Vector2[] Points;

        /// <summary>Số thứ tự của <see cref="BugAnnotationKind.Marker" /> (1–99).</summary>
        public readonly int Number;

        #endregion

        #region Public Methods

        public BugAnnotation(BugAnnotationKind kind, Color32 color, float width, Vector2[] points, int number = 0)
        {
            Kind = kind;
            Color = color;
            Width = width;
            Points = points;
            Number = number;
        }

        /// <summary>Khung bao hai điểm đầu (hình chữ nhật / elip / làm nổi).</summary>
        public Rect GetBox()
        {
            var a = Points[0];
            var b = Points.Length > 1 ? Points[1] : a;
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        #endregion
    }
}
