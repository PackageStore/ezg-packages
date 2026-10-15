using System.Collections.Generic;
using UnityEngine;

namespace Ezg.BugLogger.UI
{
    /// <summary>Kết quả thêm nét vào <see cref="BugImageDocument" />.</summary>
    internal enum BugAddResult
    {
        Added,

        /// <summary>Quá số nét / số đỉnh mesh cho phép (canvas uGUI tối đa 65 000 đỉnh một Graphic).</summary>
        LimitReached,

        /// <summary>Quá số vùng làm nổi.</summary>
        SpotlightLimit,

        /// <summary>Hết số thứ tự (tối đa <see cref="BugImageDocument.MAX_MARKER" />).</summary>
        MarkerLimit
    }

    /// <summary>
    ///     Bản chỉnh sửa KHÔNG phá huỷ của một ảnh chụp: vùng cắt + danh sách nét vẽ, kèm lịch sử undo/redo. Ảnh gốc
    ///     không bao giờ bị sửa — ảnh gửi đi được dựng lại từ ảnh gốc + bản này (<see cref="BugImageFlattener" />), nên
    ///     mở lại màn chỉnh sửa là sửa tiếp được, hoàn tác hết là về đúng ảnh gốc.
    /// </summary>
    internal sealed class BugImageDocument
    {
        #region Fields

        public const int MAX_ITEMS = 80;
        public const int MAX_SPOTLIGHTS = 8;
        public const int MAX_MARKER = 99;

        /// <summary>Tổng đỉnh mesh của các nét (dưới trần 65 000 của một Graphic uGUI, chừa chỗ cho nét đang vẽ).</summary>
        public const int VERTEX_BUDGET = 56000;

        private const int MAX_HISTORY = 100;
        private const float MIN_CROP_PX = 32f;
        private const float MIN_CROP_RATIO = 0.06f;

        private struct State
        {
            public BugAnnotation[] Items;
            public Rect Crop;
        }

        private readonly List<BugAnnotation> _items = new List<BugAnnotation>();
        private readonly Dictionary<BugAnnotation, int> _vertexCosts = new Dictionary<BugAnnotation, int>();
        private readonly List<State> _undo = new List<State>();
        private readonly List<State> _redo = new List<State>();

        #endregion

        #region Properties

        /// <summary>Kích thước ảnh gốc (px).</summary>
        public Vector2Int SourceSize { get; }

        public Rect FullRect => new Rect(0f, 0f, SourceSize.x, SourceSize.y);

        /// <summary>Vùng cắt theo pixel ảnh gốc (cả ảnh khi chưa cắt).</summary>
        public Rect Crop { get; private set; }

        public IReadOnlyList<BugAnnotation> Items => _items;

        public bool IsCropped => Crop != FullRect;

        /// <summary>Không cắt, không nét nào — ảnh gửi đi đúng là ảnh chụp.</summary>
        public bool IsPristine => _items.Count == 0 && !IsCropped;

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        /// <summary>Tổng đỉnh mesh của các nét hiện có.</summary>
        public int VertexCount { get; private set; }

        /// <summary>Cạnh nhỏ nhất của vùng cắt (px ảnh gốc).</summary>
        public float MinCropSize =>
            Mathf.Min(Mathf.Max(MIN_CROP_PX, MIN_CROP_RATIO * Mathf.Min(SourceSize.x, SourceSize.y)),
                Mathf.Min(SourceSize.x, SourceSize.y));

        /// <summary>Số thứ tự cho huy hiệu đánh số kế tiếp.</summary>
        public int NextMarkerNumber
        {
            get
            {
                var max = 0;
                for (var i = 0; i < _items.Count; i++)
                    if (_items[i].Kind == BugAnnotationKind.Marker && _items[i].Number > max)
                        max = _items[i].Number;
                return max + 1;
            }
        }

        public int SpotlightCount
        {
            get
            {
                var count = 0;
                for (var i = 0; i < _items.Count; i++)
                    if (_items[i].Kind == BugAnnotationKind.Spotlight)
                        count++;
                return count;
            }
        }

        #endregion

        #region Public Methods

        public BugImageDocument(int width, int height)
        {
            SourceSize = new Vector2Int(Mathf.Max(1, width), Mathf.Max(1, height));
            Crop = FullRect;
        }

        /// <summary>Bản sao độc lập (màn chỉnh sửa sửa trên bản sao; Huỷ thì bỏ, Xong thì thay bản cũ).</summary>
        public BugImageDocument Clone()
        {
            var copy = new BugImageDocument(SourceSize.x, SourceSize.y) { Crop = Crop };
            copy._items.AddRange(_items);
            foreach (var pair in _vertexCosts) copy._vertexCosts[pair.Key] = pair.Value;
            copy.VertexCount = VertexCount;
            copy._undo.AddRange(_undo);
            copy._redo.AddRange(_redo);
            return copy;
        }

        public BugAddResult TryAdd(BugAnnotation item)
        {
            if (item.Kind == BugAnnotationKind.Spotlight && SpotlightCount >= MAX_SPOTLIGHTS)
                return BugAddResult.SpotlightLimit;
            if (item.Kind == BugAnnotationKind.Marker && item.Number > MAX_MARKER) return BugAddResult.MarkerLimit;

            var cost = GetVertexCost(item);
            if (_items.Count >= MAX_ITEMS || VertexCount + cost > VERTEX_BUDGET) return BugAddResult.LimitReached;

            Push();
            _items.Add(item);
            VertexCount += cost;
            return BugAddResult.Added;
        }

        /// <summary>Đặt vùng cắt (tự kẹp vào ảnh, không nhỏ hơn <see cref="MinCropSize" />). False khi không đổi.</summary>
        public bool SetCrop(Rect crop)
        {
            crop = ClampCrop(crop);
            if (IsSameRect(crop, Crop)) return false;

            Push();
            Crop = crop;
            return true;
        }

        /// <summary>Bỏ mọi nét + vùng cắt (hoàn tác được). False khi vốn đã trống.</summary>
        public bool ClearAll()
        {
            if (IsPristine) return false;

            Push();
            _items.Clear();
            Crop = FullRect;
            VertexCount = 0;
            return true;
        }

        public bool Undo()
        {
            if (_undo.Count == 0) return false;

            _redo.Add(Capture());
            Restore(PopLast(_undo));
            return true;
        }

        public bool Redo()
        {
            if (_redo.Count == 0) return false;

            _undo.Add(Capture());
            Restore(PopLast(_redo));
            return true;
        }

        /// <summary>
        ///     Chuẩn hoá vùng cắt: số nguyên pixel, nằm trong ảnh, mỗi cạnh ≥ <see cref="MinCropSize" /> (nới quanh tâm
        ///     khi nhỏ hơn).
        /// </summary>
        public Rect ClampCrop(Rect crop)
        {
            var min = MinCropSize;
            var width = Mathf.Clamp(Mathf.Abs(crop.width), min, SourceSize.x);
            var height = Mathf.Clamp(Mathf.Abs(crop.height), min, SourceSize.y);
            var center = crop.center;
            var x = Mathf.Clamp(center.x - width * 0.5f, 0f, SourceSize.x - width);
            var y = Mathf.Clamp(center.y - height * 0.5f, 0f, SourceSize.y - height);

            var xMin = Mathf.Round(x);
            var yMin = Mathf.Round(y);
            var xMax = Mathf.Min(SourceSize.x, Mathf.Round(x + width));
            var yMax = Mathf.Min(SourceSize.y, Mathf.Round(y + height));
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        #endregion

        #region Private Methods

        private int GetVertexCost(BugAnnotation item)
        {
            if (_vertexCosts.TryGetValue(item, out var cost)) return cost;

            cost = BugAnnotationMesh.CountVertices(item);
            _vertexCosts[item] = cost;
            return cost;
        }

        private void Push()
        {
            _undo.Add(Capture());
            if (_undo.Count > MAX_HISTORY) _undo.RemoveAt(0);
            _redo.Clear();
        }

        private State Capture()
        {
            return new State { Items = _items.ToArray(), Crop = Crop };
        }

        private void Restore(State state)
        {
            _items.Clear();
            _items.AddRange(state.Items);
            Crop = state.Crop;

            VertexCount = 0;
            for (var i = 0; i < _items.Count; i++) VertexCount += GetVertexCost(_items[i]);
        }

        private static State PopLast(List<State> stack)
        {
            var state = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            return state;
        }

        private static bool IsSameRect(Rect a, Rect b)
        {
            return Mathf.Approximately(a.xMin, b.xMin) && Mathf.Approximately(a.yMin, b.yMin) &&
                   Mathf.Approximately(a.xMax, b.xMax) && Mathf.Approximately(a.yMax, b.yMax);
        }

        #endregion
    }
}
