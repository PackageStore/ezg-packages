#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Left/right-drag paint/erase edges on the active layer; shift line-lock.</summary>
    internal sealed class RoadPaintTool : IPaintTool
    {
        private readonly ToolContext _ctx;

        // Nét này bắt đầu trên layout ĐÃ chồng sẵn → miễn guard chống chồng cho cả nét (xem SetEdge).
        private bool _strokeOverlapWaived;

        // Giữ SHIFT khi kéo = khoá nét vào 1 trục (vẽ THẲNG ngang/dọc).
        private bool _lineLockActive;
        private int _lineLockAxis; // 0 = chưa chốt trục, 1 = ngang, 2 = dọc
        private Vector2Int _lineLockOrigin;

        internal bool LineLockActive => _lineLockActive;
        internal int LineLockAxis => _lineLockAxis;
        internal Vector2Int LineLockOrigin => _lineLockOrigin;

        // Callback to overlap detector (still on the partial class during migration).
        private readonly Func<bool> _hasAnyOverlap;
        private readonly Func<bool> _layoutAlreadyOverlaps;

        /// <summary>True if the last WalkAndPaint stroke was blocked by overlap guard.</summary>
        internal bool LastStrokeBlocked { get; private set; }

        internal RoadPaintTool(ToolContext ctx, Func<bool> hasAnyOverlap, Func<bool> layoutAlreadyOverlaps)
        {
            _ctx = ctx;
            _hasAnyOverlap = hasAnyOverlap;
            _layoutAlreadyOverlaps = layoutAlreadyOverlaps;
        }

        public bool HandleInput(Rect canvas, Event e)
        {
            var view = _ctx.View;
            var doc = _ctx.Doc;
            switch (e.type)
            {
                case EventType.MouseDown when (e.button == 0 || e.button == 1)
                                              && canvas.Contains(e.mousePosition):
                    bool hit = CoordHelper.TryPixelToHalfPoint(canvas, e.mousePosition, doc, view, out Vector2Int down2);
                    if (hit)
                    {
                        view.Dragging = true;
                        view.Erasing = e.button == 1;
                        view.DragPoint = down2;
                        _lineLockActive = false;
                        _lineLockAxis = 0;
                        // Chốt 1 lần đầu nét: layout đã chồng sẵn thì guard tắt suốt nét (không thì
                        // không vẽ được gì trên map cũ đang chồng).
                        _strokeOverlapWaived = !view.Erasing && OverlapGuardedLayer(view.EdgeLayer)
                                               && _layoutAlreadyOverlaps();
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag when view.Dragging:
                    bool hitDrag = CoordHelper.TryPixelToHalfPoint(canvas, e.mousePosition, doc, view, out Vector2Int drag2);
                    if (hitDrag)
                    {
                        Vector2Int step2 = LineLockTarget(drag2, e.shift, view);
                        if (step2 != view.DragPoint) WalkAndPaint(view.DragPoint, step2, view, doc);
                    }
                    e.Use();
                    _ctx.Host.Repaint();
                    break;

                case EventType.MouseUp when view.Dragging:
                    view.Dragging = false;
                    _lineLockActive = false;
                    _lineLockAxis = 0;
                    e.Use();
                    _ctx.Host.Repaint();
                    break;

                default:
                    return false;
            }
            return true;
        }

        public void DrawOverlay(Rect canvas) { }

        public void Cancel()
        {
            _ctx.View.Dragging = false;
            _ctx.View.Erasing = false;
            _lineLockActive = false;
            _lineLockAxis = 0;
        }

        /// <summary>Ép đích của nét về ĐƯỜNG THẲNG khi giữ SHIFT (mọi lớp nét: đường, highway, HW decor,
        /// Road 2 — vẽ trái và xoá phải như nhau). Gốc chốt lúc BẤM shift chứ không phải lúc bấm chuột, nên
        /// bật shift giữa nét vẫn thẳng tiếp từ đầu nét đang vẽ; bước di chuyển đầu tiên chốt trục theo
        /// chiều lệch NHIỀU hơn và trục đó GIỮ tới khi nhả shift (rung tay không làm gãy nét). Nhả shift →
        /// trả nguyên điểm, vẽ tự do như cũ.</summary>
        private Vector2Int LineLockTarget(Vector2Int to2, bool shift, ViewState view)
        {
            if (!shift)
            {
                _lineLockActive = false;
                _lineLockAxis = 0;
                return to2;
            }

            if (!_lineLockActive)
            {
                _lineLockActive = true;
                _lineLockAxis = 0;
                _lineLockOrigin = view.DragPoint;
            }

            int dx = to2.x - _lineLockOrigin.x, dy = to2.y - _lineLockOrigin.y;
            if (_lineLockAxis == 0)
            {
                if (dx == 0 && dy == 0) return _lineLockOrigin;
                _lineLockAxis = Math.Abs(dx) >= Math.Abs(dy) ? 1 : 2;
            }

            return _lineLockAxis == 1
                ? new Vector2Int(to2.x, _lineLockOrigin.y)
                : new Vector2Int(_lineLockOrigin.x, to2.y);
        }

        /// <summary>Đi từng bước NỬA Ô từ from → to (toạ độ nửa ô, ưu tiên trục X trước): PAINT/ERASE
        /// đúng 1 edge nửa ô mỗi bước 1 nấc lattice (edge nay dài đúng 1 nấc — không còn gộp/dư). Không
        /// đặt/xoá edge vượt biên lattice; xoá edge chưa tồn tại là no-op (không set blocked).</summary>
        private void WalkAndPaint(Vector2Int from2, Vector2Int rawTo2, ViewState view, RoadCanvasDoc doc)
        {
            int maxX2 = (doc.GridWidth - 1) * 2, maxY2 = (doc.GridHeight - 1) * 2;
            bool InBounds(Vector2Int p) => p.x >= 0 && p.x <= maxX2 && p.y >= 0 && p.y <= maxY2;

            Vector2Int cur = from2;
            bool blocked = false;

            void PaintAxis(bool isX, int targetCoord)
            {
                int cur0 = isX ? cur.x : cur.y;
                int dir = Math.Sign(targetCoord - cur0);
                int remaining = Math.Abs(targetCoord - cur0);
                while (remaining >= 1)
                {
                    Vector2Int next = cur;
                    if (isX) next.x += dir; else next.y += dir;
                    if (!InBounds(next)) break;
                    if (!SetEdge(cur, next, true, view, doc)) blocked = true;
                    cur = next;
                    remaining -= 1;
                }
            }

            void EraseAxis(bool isX, int targetCoord)
            {
                int dir = Math.Sign(targetCoord - (isX ? cur.x : cur.y));
                if (dir == 0) return;
                while ((isX ? cur.x : cur.y) != targetCoord)
                {
                    Vector2Int next = cur;
                    if (isX) next.x += dir; else next.y += dir;
                    if (InBounds(cur) && InBounds(next)) SetEdge(cur, next, false, view, doc);
                    cur = next;
                }
            }

            if (view.Erasing)
            {
                EraseAxis(true, rawTo2.x);
                EraseAxis(false, rawTo2.y);
            }
            else
            {
                PaintAxis(true, rawTo2.x);
                PaintAxis(false, rawTo2.y);
            }

            view.DragPoint = cur;
            LastStrokeBlocked = blocked;
        }

        /// <summary>Lớp nét có guard chống chồng (0 = đường, 1 = highway, 3 = Road2; 2 = hw-decor thì
        /// không).</summary>
        // Layer 4 (PATH) cố ý KHÔNG nằm ở đây — D6/D7: PATH không bao giờ nằm chung chỗ với
        // lớp khác, và khung type-1 rộng 1 ô sẽ báo chồng oan cho mặt cắt 0.5 ô.
        private static bool OverlapGuardedLayer(int edgeLayer) =>
            edgeLayer == 0 || edgeLayer == 1 || edgeLayer == 3;

        /// <summary>Đặt/xoá 1 edge trên lớp đang vẽ. Với Road/Highway/Road2, nếu THÊM edge gây chồng
        /// (HW↔HW, HW↔Road, Road↔Road hoặc — D10 — Road2↔Highway/Road/Road2) thì HOÀN TÁC và trả false
        /// (chặn) — bất biến: layout luôn không chồng. Guard đo trên TOÀN layout nên chỉ giữ được bất
        /// biến khi layout vào nét đã sạch;
        /// map mở lên mà đã chồng sẵn thì <see cref="_strokeOverlapWaived"/> tắt guard, nếu không mọi
        /// nét mới đều bị hoàn tác và không vẽ thêm được gì (thanh trạng thái báo "Layout overlap").</summary>
        private bool SetEdge(Vector2Int a, Vector2Int b, bool on, ViewState view, RoadCanvasDoc doc)
        {
            List<int> edges = doc.EdgesFor(view.EdgeLayer);
            int id = EdgeCodec.EncodeEdge(a, b);
            if (on)
            {
                if (edges.Contains(id)) return true;
                edges.Add(id);
                if (!_strokeOverlapWaived && OverlapGuardedLayer(view.EdgeLayer) && _hasAnyOverlap())
                {
                    edges.Remove(id);
                    return false;
                }
            }
            else
            {
                edges.Remove(id);
            }
            return true;
        }
    }
}
#endif
