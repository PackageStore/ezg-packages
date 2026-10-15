using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Ezg.BugLogger.UI
{
    /// <summary>
    ///     Vẽ nét của <see cref="BugImageDocument" /> lên vùng ảnh của màn chỉnh sửa. Chia ba lớp để nét đang kéo không
    ///     bắt dựng lại mọi nét cũ mỗi frame: <see cref="Layer.Dim" /> (vùng tối của "làm nổi", dưới cùng),
    ///     <see cref="Layer.Shapes" /> (nét đã thêm) và <see cref="Layer.Pending" /> (nét đang vẽ, trên cùng) — cùng thứ
    ///     tự với ảnh xuất.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class BugAnnotationGraphic : MaskableGraphic
    {
        #region Fields

        public enum Layer
        {
            Dim,
            Shapes,
            Pending
        }

        [SerializeField] private Texture _atlas;
        [SerializeField] private Layer _layer;

        // Dùng chung giữa các Graphic: OnPopulateMesh luôn chạy tuần tự trên main thread.
        private static readonly BugMeshBuffer Buffer = new BugMeshBuffer();

        private readonly List<Vector2> _pendingPoints = new List<Vector2>(256);

        private BugImageDocument _document;
        private Rect _view;
        private bool _hasPending;
        private BugAnnotationKind _pendingKind;
        private Color32 _pendingColor;
        private float _pendingWidth;
        private int _pendingNumber;
        private Rect? _extraHole;

        #endregion

        #region Properties

        public override Texture mainTexture => _atlas != null ? _atlas : s_WhiteTexture;

        #endregion

        #region Public Methods

        /// <summary>Bản đang sửa + vùng ảnh (px ảnh gốc) mà rect của Graphic này đang hiện.</summary>
        public void SetSource(BugImageDocument document, Rect view)
        {
            _document = document;
            _view = view;
            SetVerticesDirty();
        }

        public void SetPending(BugAnnotationKind kind, Color32 color, float width, IReadOnlyList<Vector2> points,
            int number)
        {
            _hasPending = true;
            _pendingKind = kind;
            _pendingColor = color;
            _pendingWidth = width;
            _pendingNumber = number;
            _pendingPoints.Clear();
            for (var i = 0; i < points.Count; i++) _pendingPoints.Add(points[i]);
            SetVerticesDirty();
        }

        /// <summary>Vùng "làm nổi" đang kéo — lớp tối khoét lỗ theo luôn.</summary>
        public void SetExtraHole(Rect? hole)
        {
            if (_extraHole == hole) return;

            _extraHole = hole;
            SetVerticesDirty();
        }

        public void ClearPending()
        {
            if (!_hasPending && _extraHole == null) return;

            _hasPending = false;
            _pendingPoints.Clear();
            _extraHole = null;
            SetVerticesDirty();
        }

        /// <summary>Thả tham chiếu tới bản đang sửa (màn chỉnh sửa đóng).</summary>
        public void Release()
        {
            _document = null;
            _hasPending = false;
            _pendingPoints.Clear();
            _extraHole = null;
            SetVerticesDirty();
        }

        #endregion

        #region Private Methods

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            if (_document == null || _view.width <= 0f || _view.height <= 0f) return;

            var rect = rectTransform.rect;
            if (rect.width <= 0f || rect.height <= 0f) return;

            Buffer.Clear();
            Buffer.SetTransform(_view, rect);
            Buffer.Feather = GetFeather();
            switch (_layer)
            {
                case Layer.Dim:
                    BugAnnotationMesh.AppendSpotlightDim(Buffer, _document.FullRect, _document.Items, _extraHole);
                    break;
                case Layer.Shapes:
                    var items = _document.Items;
                    for (var i = 0; i < items.Count; i++) BugAnnotationMesh.AppendShape(Buffer, items[i]);
                    break;
                case Layer.Pending:
                    if (_hasPending)
                        BugAnnotationMesh.AppendShape(Buffer, _pendingKind, _pendingColor, _pendingWidth, _pendingPoints,
                            _pendingNumber);
                    break;
            }

            Buffer.CopyTo(vertexHelper);
            Buffer.Clear();
        }

        /// <summary>Một pixel màn hình tính theo đơn vị canvas.</summary>
        private float GetFeather()
        {
            var scale = canvas != null ? canvas.scaleFactor : 1f;
            return scale > 0f ? 1f / scale : 1f;
        }

        #endregion
    }
}
