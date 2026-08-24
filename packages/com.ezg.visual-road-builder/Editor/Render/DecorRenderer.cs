#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Vẽ decor trên canvas: chấm màu + mũi tên hướng cho từng item, preview vùng brush đang
    /// khoanh, và ghost hover.</summary>
    internal sealed class DecorRenderer
    {
        private readonly ToolContext _ctx;

        internal DecorRenderer(ToolContext ctx) => _ctx = ctx;

        internal void DrawDecors(Rect canvas, DecorState ds)
        {
            var doc = _ctx.Doc;
            var view = _ctx.View;
            for (int i = 0; i < doc.Decors.Count; i++)
            {
                DecorItem item = doc.Decors[i];
                Color c = DecorColor(ds, item.entry);
                Vector2 p = CoordHelper.PointToPixelF(canvas, item.x2 * 0.5f, item.y2 * 0.5f, doc, view);
                const float s = 9f;
                var r = new Rect(p.x - s * 0.5f, p.y - s * 0.5f, s, s);
                EditorGUI.DrawRect(r, i == ds.DraggingDecor ? Color.white : c);
                DrawPrimitives.DrawRectBorder(r, 1f, Color.black);
                DrawPrimitives.DrawFacingArrow(new Rect(p.x - 14f, p.y - 14f, 28f, 28f), item.rot, c);
            }

            // Preview vùng brush đang khoanh.
            if (ds.AreaDragging)
            {
                Vector2 a = CoordHelper.PointToPixelF(canvas, ds.AreaStart.x, ds.AreaStart.y, doc, view);
                Vector2 b = CoordHelper.PointToPixelF(canvas, ds.AreaEnd.x, ds.AreaEnd.y, doc, view);
                var r = Rect.MinMaxRect(
                    Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                    Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
                Color fill = ds.AreaErasing
                    ? new Color(0.9f, 0.25f, 0.2f, 0.2f)
                    : new Color(0.3f, 0.85f, 0.35f, 0.2f);
                EditorGUI.DrawRect(r, fill);
                DrawPrimitives.DrawRectBorder(r, 1f, ds.AreaErasing ? Color.red : Color.green);
            }

            // Ghost hover khi ở mode Decor và không thao tác gì.
            if (view.Mode == PaintMode.Decor && !ds.AreaMode && ds.Hover
                && ds.DraggingDecor < 0 && !ds.PaintingDecor && !ds.ErasingDecor)
            {
                Color c = DecorColor(ds, ds.EntryIndex);
                c.a = 0.5f;
                Vector2 p = CoordHelper.PointToPixelF(canvas, ds.HoverP2.x * 0.5f, ds.HoverP2.y * 0.5f, doc, view);
                EditorGUI.DrawRect(new Rect(p.x - 4.5f, p.y - 4.5f, 9f, 9f), c);
            }
        }

        internal static Color DecorColor(DecorState ds, int entry)
        {
            if (ds.Library != null && entry >= 0 && entry < ds.Library.entries.Count)
                return ds.Library.entries[entry].canvasColor;
            return Color.magenta;
        }
    }
}
#endif
