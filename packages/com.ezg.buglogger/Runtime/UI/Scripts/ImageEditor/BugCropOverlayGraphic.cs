using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Ezg.BugLogger.UI
{
    /// <summary>
    ///     Khung cắt của màn chỉnh sửa: phần ngoài vùng cắt tối đi, viền + lưới một phần ba + tay cầm ở bốn góc.
    ///     Chỉ hiện khi đang chọn công cụ Cắt (lúc đó vùng ảnh hiện cả ảnh gốc).
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class BugCropOverlayGraphic : MaskableGraphic
    {
        #region Fields

        private const float FRAME_HALF_WIDTH = 2f;
        private const float GUIDE_HALF_WIDTH = 1f;
        private const float HANDLE_HALF_WIDTH = 5f;
        private const float HANDLE_LENGTH = 48f;
        private const float HANDLE_MAX_SHARE = 0.3f;

        private static readonly Color32 Dim = new Color32(0, 0, 0, 150);
        private static readonly Color32 Frame = new Color32(255, 255, 255, 235);
        private static readonly Color32 Guide = new Color32(255, 255, 255, 90);
        private static readonly Color32 Handle = new Color32(255, 255, 255, 255);

        [SerializeField] private Texture _atlas;

        private static readonly BugMeshBuffer Buffer = new BugMeshBuffer();
        private static readonly List<Vector2> Points = new List<Vector2>(4);

        private Rect _view;
        private Rect _crop;

        #endregion

        #region Properties

        public override Texture mainTexture => _atlas != null ? _atlas : s_WhiteTexture;

        #endregion

        #region Public Methods

        /// <summary>Vùng ảnh đang hiện + vùng cắt (cả hai theo px ảnh gốc).</summary>
        public void Set(Rect view, Rect crop)
        {
            _view = view;
            _crop = crop;
            SetVerticesDirty();
        }

        #endregion

        #region Private Methods

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            var rect = rectTransform.rect;
            if (_view.width <= 0f || _view.height <= 0f || rect.width <= 0f || rect.height <= 0f) return;

            Buffer.Clear();
            Buffer.SetTransform(_view, rect);
            Buffer.Feather = GetFeather();
            var crop = Buffer.Map(_crop);

            BugAnnotationMesh.AppendSolidRect(Buffer, Rect.MinMaxRect(rect.xMin, rect.yMin, rect.xMax, crop.yMin), Dim);
            BugAnnotationMesh.AppendSolidRect(Buffer, Rect.MinMaxRect(rect.xMin, crop.yMax, rect.xMax, rect.yMax), Dim);
            BugAnnotationMesh.AppendSolidRect(Buffer, Rect.MinMaxRect(rect.xMin, crop.yMin, crop.xMin, crop.yMax), Dim);
            BugAnnotationMesh.AppendSolidRect(Buffer, Rect.MinMaxRect(crop.xMax, crop.yMin, rect.xMax, crop.yMax), Dim);

            for (var i = 1; i < 3; i++)
            {
                var x = crop.xMin + crop.width * i / 3f;
                var y = crop.yMin + crop.height * i / 3f;
                Line(new Vector2(x, crop.yMin), new Vector2(x, crop.yMax), GUIDE_HALF_WIDTH, Guide);
                Line(new Vector2(crop.xMin, y), new Vector2(crop.xMax, y), GUIDE_HALF_WIDTH, Guide);
            }

            Points.Clear();
            Points.Add(new Vector2(crop.xMin, crop.yMin));
            Points.Add(new Vector2(crop.xMax, crop.yMin));
            Points.Add(new Vector2(crop.xMax, crop.yMax));
            Points.Add(new Vector2(crop.xMin, crop.yMax));
            BugAnnotationMesh.StrokeDestination(Buffer, Points, FRAME_HALF_WIDTH, Frame, true);

            var length = Mathf.Min(HANDLE_LENGTH, crop.width * HANDLE_MAX_SHARE, crop.height * HANDLE_MAX_SHARE);
            Corner(new Vector2(crop.xMin, crop.yMin), 1f, 1f, length);
            Corner(new Vector2(crop.xMax, crop.yMin), -1f, 1f, length);
            Corner(new Vector2(crop.xMax, crop.yMax), -1f, -1f, length);
            Corner(new Vector2(crop.xMin, crop.yMax), 1f, -1f, length);

            Buffer.CopyTo(vertexHelper);
            Buffer.Clear();
        }

        private static void Line(Vector2 from, Vector2 to, float halfWidth, Color32 color)
        {
            Points.Clear();
            Points.Add(from);
            Points.Add(to);
            BugAnnotationMesh.StrokeDestination(Buffer, Points, halfWidth, color, false);
        }

        /// <summary>Tay cầm chữ L ở góc, hai nhánh chạy vào trong khung theo hướng (dx, dy).</summary>
        private static void Corner(Vector2 corner, float dx, float dy, float length)
        {
            Points.Clear();
            Points.Add(corner + new Vector2(0f, dy * length));
            Points.Add(corner);
            Points.Add(corner + new Vector2(dx * length, 0f));
            BugAnnotationMesh.StrokeDestination(Buffer, Points, HANDLE_HALF_WIDTH, Handle, false);
        }

        private float GetFeather()
        {
            var scale = canvas != null ? canvas.scaleFactor : 1f;
            return scale > 0f ? 1f / scale : 1f;
        }

        #endregion
    }
}
