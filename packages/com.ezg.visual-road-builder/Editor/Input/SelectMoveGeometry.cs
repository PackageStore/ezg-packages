#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Pure static geometry helpers for selection: edge endpoints, shift, hit-test, bounding rect.</summary>
    internal static class SelectMoveGeometry
    {
        /// <summary>Hai đầu (nửa ô) của một edge: A là đầu nhỏ đã chuẩn hoá, B lệch +1 theo orient.</summary>
        internal static void EdgeEndpoints(int id, out Vector2Int a, out Vector2Int b)
        {
            EdgeCodec.DecodeEdge(id, out int x2, out int y2, out int orient);
            a = new Vector2Int(x2, y2);
            b = orient == 0 ? new Vector2Int(x2 + 1, y2) : new Vector2Int(x2, y2 + 1);
        }

        internal static int ShiftEdgeId(int id, Vector2Int d)
        {
            EdgeCodec.DecodeEdge(id, out int x2, out int y2, out int orient);
            return ((y2 + d.y) << 13) | ((x2 + d.x) << 1) | orient;
        }

        internal static bool InBox(Vector2Int p, int minX, int minY, int maxX, int maxY)
            => p.x >= minX && p.x <= maxX && p.y >= minY && p.y <= maxY;

        /// <summary>Footprint w2 x h2 (nửa ô) neo tại (x2,y2) có nằm TRỌN trong khung không?</summary>
        internal static bool RectInBox(int x2, int y2, int w2, int h2, int minX, int minY, int maxX, int maxY)
            => x2 >= minX && y2 >= minY && x2 + w2 <= maxX && y2 + h2 <= maxY;

        /// <summary>Điểm lattice NỬA Ô gần chuột nhất (đơn vị nửa ô), KHÔNG kiểm tra biên.</summary>
        internal static Vector2Int PixelToHalfPointRaw(Rect canvas, Vector2 mouse, RoadCanvasDoc doc, ViewState view)
        {
            Vector2 f = CoordHelper.MouseToGridF(canvas, mouse, doc, view);
            return new Vector2Int(Mathf.RoundToInt(f.x * 2f), Mathf.RoundToInt(f.y * 2f));
        }

        /// <summary>Chuột có nằm trong khung bao (đã nới nhẹ) của nhóm đã chọn không? (để bắt đầu kéo).</summary>
        internal static bool PointInSelection(Rect canvas, Vector2 mouse, SelectMoveTool sel,
            RoadCanvasDoc doc, ViewState view)
        {
            if (!sel.HasSelection) return false;
            Rect box = SelectionPixelRect(canvas, sel, doc, view);
            box.xMin -= 8f; box.yMin -= 8f; box.xMax += 8f; box.yMax += 8f;
            return box.Contains(mouse);
        }

        /// <summary>Khung bao (pixel) của MỌI vật thể đã chọn, đã dịch theo delta.</summary>
        internal static Rect SelectionPixelRect(Rect canvas, SelectMoveTool sel,
            RoadCanvasDoc doc, ViewState view)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            Vector2Int d = sel.Delta;

            void Acc(int loX, int hiX, int loY, int hiY)
            {
                minX = Mathf.Min(minX, loX + d.x);
                maxX = Mathf.Max(maxX, hiX + d.x);
                minY = Mathf.Min(minY, loY + d.y);
                maxY = Mathf.Max(maxY, hiY + d.y);
            }

            void AccEdges(List<int> edges)
            {
                foreach (int id in edges)
                {
                    EdgeEndpoints(id, out Vector2Int a, out Vector2Int b);
                    Acc(Mathf.Min(a.x, b.x), Mathf.Max(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.y, b.y));
                }
            }

            AccEdges(sel.EdgesOrig);
            AccEdges(sel.HwOrig);
            AccEdges(sel.HwDecOrig);
            AccEdges(sel.Road2Orig);
            AccEdges(sel.PathOrig);

            int st2 = GridConst.StationSize * 2;
            foreach (int id in sel.StationsOrig)
            {
                BlockCodec.DecodeStation(id, out int x2, out int y2, out _);
                Acc(x2, x2 + st2, y2, y2 + st2);
            }
            foreach (int id in sel.ParkingsOrig)
            {
                BlockCodec.DecodeParking(id, out int x2, out int y2, out int orient);
                Vector2Int k = GridConst.ParkingCells(orient);
                Acc(x2, x2 + k.x * 2, y2, y2 + k.y * 2);
            }
            foreach (DecorItem item in sel.DecorsOrig)
                Acc(item.x2, item.x2, item.y2, item.y2);

            if (minX == int.MaxValue) return new Rect(canvas.x, canvas.y, 0f, 0f);
            Vector2 p0 = CoordHelper.PointToPixelF(canvas, minX * 0.5f, minY * 0.5f, doc, view);
            Vector2 p1 = CoordHelper.PointToPixelF(canvas, maxX * 0.5f, maxY * 0.5f, doc, view);
            return Rect.MinMaxRect(Mathf.Min(p0.x, p1.x), Mathf.Min(p0.y, p1.y),
                                   Mathf.Max(p0.x, p1.x), Mathf.Max(p0.y, p1.y));
        }
    }
}
#endif