using UnityEngine;

namespace Ezg.BugLogger.UI
{
    /// <summary>
    ///     Bố cục của <c>Sprites/bl_annotation_atlas.png</c>: một ô trắng (tô hình đặc — viền mờ khử răng cưa nằm ở alpha
    ///     của đỉnh) + chữ số 0–9 Be Vietnam Pro SemiBold cho huy hiệu đánh số. Số liệu do
    ///     <c>Tools~/generate_image_editor_sprites.py</c> in ra — sinh lại atlas thì cập nhật ở đây.
    /// </summary>
    internal static class BugAnnotationAtlas
    {
        #region Fields

        private const float WIDTH = 512f;
        private const float HEIGHT = 256f;

        // Ô trắng 16×16 ở góc trên-trái; lấy mẫu ở tâm để bilinear không chạm mép trong suốt.
        private const float WHITE_CENTER_X = 8f;
        private const float WHITE_CENTER_Y = 8f;

        /// <summary>Kích thước một ô chữ số (px atlas).</summary>
        public const float CELL_WIDTH = 96f;

        public const float CELL_HEIGHT = 112f;

        /// <summary>Mép trên / dưới phần mực của chữ số trong ô (px atlas, tính từ mép trên ô) — chung mọi chữ số.</summary>
        public const float INK_TOP = 15.44f;

        public const float INK_BOTTOM = 94.44f;

        private const float CELL_X0 = 16f;
        private static readonly float[] RowY = { 16f, 136f };
        private static readonly float[] Advances = { 72f, 43f, 69f, 70f, 74f, 70f, 72f, 62f, 68f, 72f };

        /// <summary>UV lấy màu trắng đặc.</summary>
        public static readonly Vector2 SolidUv = new Vector2(WHITE_CENTER_X / WIDTH, 1f - WHITE_CENTER_Y / HEIGHT);

        #endregion

        #region Public Methods

        /// <summary>Bề rộng chữ số <paramref name="digit" /> theo font (px atlas) — dùng để ghép số nhiều chữ.</summary>
        public static float GetAdvance(int digit)
        {
            return Advances[Mathf.Clamp(digit, 0, 9)];
        }

        /// <summary>UV của cả ô chữ số (chữ nằm giữa ô theo chiều ngang).</summary>
        public static Rect GetDigitUv(int digit)
        {
            digit = Mathf.Clamp(digit, 0, 9);
            var x = CELL_X0 + digit % 5 * CELL_WIDTH;
            var y = RowY[digit / 5];
            return Rect.MinMaxRect(x / WIDTH, 1f - (y + CELL_HEIGHT) / HEIGHT, (x + CELL_WIDTH) / WIDTH, 1f - y / HEIGHT);
        }

        #endregion
    }
}
