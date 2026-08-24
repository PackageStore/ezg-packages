#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Draws marquee, highlighted edges/blocks, gizmo, and delta tag for the active selection.</summary>
    internal sealed class SelectOverlayRenderer
    {
        private readonly ToolContext _ctx;
        private readonly SelectMoveTool _sel;
        private readonly ToolStyles _styles;

        internal SelectOverlayRenderer(ToolContext ctx, SelectMoveTool sel, ToolStyles styles)
        {
            _ctx = ctx;
            _sel = sel;
            _styles = styles;
        }

        internal void Draw(Rect canvas)
        {
            if (_sel.Selecting)
            {
                DrawMarquee(canvas);
                return;
            }
            if (!_sel.HasSelection) return;
            DrawHighlights(canvas);
            DrawBoundingBox(canvas);
            DrawGizmo(canvas);
            DrawDeltaTag(canvas);
        }

        private void DrawMarquee(Rect canvas)
        {
            var doc = _ctx.Doc;
            var view = _ctx.View;
            Vector2 p0 = CoordHelper.PointToPixelF(canvas, _sel.SelStart2.x * 0.5f, _sel.SelStart2.y * 0.5f, doc, view);
            Vector2 p1 = CoordHelper.PointToPixelF(canvas, _sel.SelEnd2.x * 0.5f, _sel.SelEnd2.y * 0.5f, doc, view);
            Rect r = Rect.MinMaxRect(Mathf.Min(p0.x, p1.x), Mathf.Min(p0.y, p1.y),
                                     Mathf.Max(p0.x, p1.x), Mathf.Max(p0.y, p1.y));
            EditorGUI.DrawRect(r, new Color(0.3f, 0.7f, 1f, 0.15f));
            DrawPrimitives.DrawRectBorder(r, 1.5f, new Color(0.4f, 0.8f, 1f, 0.95f));
        }

        private void DrawHighlights(Rect canvas)
        {
            var hiRoad = new Color(0.3f, 0.9f, 1f, 0.5f);
            var hiHw = new Color(1f, 0.5f, 0.35f, 0.5f);
            var hiHwDec = new Color(0.95f, 0.95f, 0.95f, 0.5f);
            var hiRoad2 = new Color(0.78f, 0.60f, 1f, 0.5f);
            var hiPath = new Color(0.40f, 0.95f, 0.85f, 0.5f);
            var hiBlock = new Color(0.5f, 0.9f, 1f, 0.28f);
            Vector2Int d = _sel.Delta;

            foreach (int id in _sel.EdgesOrig) DrawEdgeBar(canvas, id, d, hiRoad);
            foreach (int id in _sel.HwOrig) DrawEdgeBar(canvas, id, d, hiHw);
            foreach (int id in _sel.HwDecOrig) DrawEdgeBar(canvas, id, d, hiHwDec);
            foreach (int id in _sel.Road2Orig) DrawEdgeBar(canvas, id, d, hiRoad2);
            foreach (int id in _sel.PathOrig) DrawEdgeBar(canvas, id, d, hiPath);

            int st2 = GridConst.StationSize * 2;
            foreach (int id in _sel.StationsOrig)
            {
                BlockCodec.DecodeStation(id, out int x2, out int y2, out _);
                DrawCellRect(canvas, x2, y2, st2, st2, d, hiBlock);
            }
            foreach (int id in _sel.ParkingsOrig)
            {
                BlockCodec.DecodeParking(id, out int x2, out int y2, out int orient);
                Vector2Int k = GridConst.ParkingCells(orient);
                DrawCellRect(canvas, x2, y2, k.x * 2, k.y * 2, d, hiBlock);
            }
            foreach (DecorItem item in _sel.DecorsOrig)
                DrawCellRect(canvas, item.x2 - 1, item.y2 - 1, 2, 2, d, hiBlock);
        }

        private void DrawBoundingBox(Rect canvas)
        {
            Rect box = SelectMoveGeometry.SelectionPixelRect(canvas, _sel, _ctx.Doc, _ctx.View);
            box.xMin -= 5f; box.yMin -= 5f; box.xMax += 5f; box.yMax += 5f;
            DrawPrimitives.DrawRectBorder(box, 1.5f, new Color(0.4f, 0.85f, 1f, 0.95f));
        }

        private void DrawGizmo(Rect canvas)
        {
            Rect box = SelectMoveGeometry.SelectionPixelRect(canvas, _sel, _ctx.Doc, _ctx.View);
            box.xMin -= 5f; box.yMin -= 5f; box.xMax += 5f; box.yMax += 5f;
            Vector2 c = box.center;
            var red = new Color(1f, 0.35f, 0.3f, 0.95f);
            var green = new Color(0.4f, 0.9f, 0.4f, 0.95f);
            EditorGUI.DrawRect(new Rect(c.x, c.y - 1f, 26f, 2f), red);
            EditorGUI.DrawRect(new Rect(c.x + 24f, c.y - 4f, 7f, 7f), red);
            EditorGUI.DrawRect(new Rect(c.x - 1f, c.y - 26f, 2f, 26f), green);
            EditorGUI.DrawRect(new Rect(c.x - 4f, c.y - 31f, 7f, 7f), green);
            var handle = new Rect(c.x - 6f, c.y - 6f, 12f, 12f);
            EditorGUI.DrawRect(handle, _sel.MovingSel ? new Color(1f, 0.9f, 0.3f) : new Color(0.5f, 0.85f, 1f));
            DrawPrimitives.DrawRectBorder(handle, 1f, Color.black);
        }

        private void DrawDeltaTag(Rect canvas)
        {
            if (_sel.Delta == Vector2Int.zero) return;
            Rect box = SelectMoveGeometry.SelectionPixelRect(canvas, _sel, _ctx.Doc, _ctx.View);
            box.xMin -= 5f; box.yMin -= 5f; box.xMax += 5f; box.yMax += 5f;
            int items = _sel.EdgesOrig.Count + _sel.HwOrig.Count + _sel.HwDecOrig.Count
                + _sel.Road2Orig.Count + _sel.PathOrig.Count
                + _sel.StationsOrig.Count + _sel.ParkingsOrig.Count + _sel.DecorsOrig.Count;
            string t = $"Move  Δx {_sel.Delta.x * 0.5f:0.#}  Δy {_sel.Delta.y * 0.5f:0.#}   ({items} vật thể)";
            var tag = new Rect(box.x, box.y - 20f, 280f, 18f);
            if (tag.y < canvas.y + GridConst.GutterTop) tag.y = box.yMax + 4f;
            EditorGUI.DrawRect(tag, new Color(0.1f, 0.1f, 0.1f, 0.85f));
            GUI.Label(tag, t, _styles.MiniTagStyle);
        }

        private void DrawEdgeBar(Rect canvas, int id, Vector2Int d, Color color)
        {
            SelectMoveGeometry.EdgeEndpoints(id, out Vector2Int a, out Vector2Int b);
            Vector2 pa = CoordHelper.PointToPixelF(canvas, (a.x + d.x) * 0.5f, (a.y + d.y) * 0.5f, _ctx.Doc, _ctx.View);
            Vector2 pb = CoordHelper.PointToPixelF(canvas, (b.x + d.x) * 0.5f, (b.y + d.y) * 0.5f, _ctx.Doc, _ctx.View);
            const float t = 5f;
            Rect r = Rect.MinMaxRect(
                Mathf.Min(pa.x, pb.x) - t * 0.5f, Mathf.Min(pa.y, pb.y) - t * 0.5f,
                Mathf.Max(pa.x, pb.x) + t * 0.5f, Mathf.Max(pa.y, pb.y) + t * 0.5f);
            EditorGUI.DrawRect(r, color);
        }

        private void DrawCellRect(Rect canvas, int x2, int y2, int w2, int h2, Vector2Int d, Color color)
        {
            Vector2 p0 = CoordHelper.PointToPixelF(canvas, (x2 + d.x) * 0.5f, (y2 + d.y) * 0.5f, _ctx.Doc, _ctx.View);
            Vector2 p1 = CoordHelper.PointToPixelF(canvas, (x2 + w2 + d.x) * 0.5f, (y2 + h2 + d.y) * 0.5f, _ctx.Doc, _ctx.View);
            EditorGUI.DrawRect(Rect.MinMaxRect(Mathf.Min(p0.x, p1.x), Mathf.Min(p0.y, p1.y),
                                               Mathf.Max(p0.x, p1.x), Mathf.Max(p0.y, p1.y)), color);
        }
    }
}
#endif
