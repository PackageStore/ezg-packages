#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Universal eraser: drag to erase all layers under cursor.</summary>
    internal sealed class EraserTool : IPaintTool
    {
        private readonly ToolContext _ctx;

        // Bán kính (ô) tính từ con trỏ để bắt edge/decor cần xoá — khớp cỡ con trỏ 1 ô (nửa cạnh 0.5).
        private const float EraseRadiusCells = 0.45f;

        private bool _eraserPainting;

        internal EraserTool(ToolContext ctx) => _ctx = ctx;

        public bool HandleInput(Rect canvas, Event e)
        {
            var doc = _ctx.Doc;
            switch (e.type)
            {
                case EventType.MouseDown when (e.button == 0 || e.button == 1)
                                              && canvas.Contains(e.mousePosition):
                    _eraserPainting = true;
                    EraseAt(canvas, e.mousePosition, doc);
                    e.Use();
                    _ctx.Host.Repaint();
                    break;

                case EventType.MouseDrag when _eraserPainting:
                    EraseAt(canvas, e.mousePosition, doc);
                    e.Use();
                    _ctx.Host.Repaint();
                    break;

                case EventType.MouseUp when _eraserPainting:
                    _eraserPainting = false;
                    e.Use();
                    _ctx.Host.Repaint();
                    break;

                default:
                    return false;
            }
            return true;
        }

        public void DrawOverlay(Rect canvas)
        {
            var view = _ctx.View;
            if (!view.HoverCellValid) return;
            float r = view.CellPixelSize * EraseRadiusCells;
            var rect = new Rect(view.HoverPixel.x - r, view.HoverPixel.y - r, r * 2f, r * 2f);
            EditorGUI.DrawRect(rect, new Color(1f, 0.3f, 0.25f, 0.22f));
            DrawPrimitives.DrawRectBorder(rect, 1.5f, new Color(1f, 0.35f, 0.3f, 0.9f));
        }

        public void Cancel() => _eraserPainting = false;

        /// <summary>Xoá mọi phần tử của MỌI lớp nằm dưới con trỏ tại một vị trí chuột.</summary>
        private void EraseAt(Rect canvas, Vector2 mouse, RoadCanvasDoc doc)
        {
            Vector2 f = CoordHelper.MouseToGridF(canvas, mouse, doc, _ctx.View);
            EraseEdgesAt(doc.Edges, f);
            EraseEdgesAt(doc.HighwayEdges, f);
            EraseEdgesAt(doc.HwDecorEdges, f);
            EraseEdgesAt(doc.Road2Edges, f);
            EraseEdgesAt(doc.PathEdges, f);
            EraseBlocksAt(f, doc);
            EraseDecorsAt(f, doc);
        }

        private static void EraseEdgesAt(List<int> edges, Vector2 f)
            => edges.RemoveAll(id => DistPointToEdge(id, f) <= EraseRadiusCells);

        /// <summary>Khoảng cách (ô) từ điểm f tới đoạn edge (dài nửa ô, ngang hoặc dọc).</summary>
        private static float DistPointToEdge(int id, Vector2 f)
        {
            EdgeCodec.DecodeEdge(id, out int x2, out int y2, out int orient);
            var a = new Vector2(x2 * 0.5f, y2 * 0.5f);
            Vector2 ab = (orient == 0 ? Vector2.right : Vector2.up) * 0.5f;
            float t = Mathf.Clamp01(Vector2.Dot(f - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(f, a + ab * t);
        }

        private static void EraseBlocksAt(Vector2 f, RoadCanvasDoc doc)
        {
            float px2 = f.x * 2f, py2 = f.y * 2f;
            int s2 = GridConst.StationSize * 2;
            doc.Stations.RemoveAll(id =>
            {
                BlockCodec.DecodeStation(id, out int x2, out int y2, out _);
                return px2 >= x2 && px2 <= x2 + s2 && py2 >= y2 && py2 <= y2 + s2;
            });
            doc.Parkings.RemoveAll(id =>
            {
                BlockCodec.DecodeParking(id, out int x2, out int y2, out int orient);
                Vector2Int k = GridConst.ParkingCells(orient);
                return px2 >= x2 && px2 <= x2 + k.x * 2 && py2 >= y2 && py2 <= y2 + k.y * 2;
            });
        }

        private static void EraseDecorsAt(Vector2 f, RoadCanvasDoc doc)
            => doc.Decors.RemoveAll(d =>
                Vector2.Distance(f, new Vector2(d.x2 * 0.5f, d.y2 * 0.5f)) <= EraseRadiusCells);
    }
}
#endif
