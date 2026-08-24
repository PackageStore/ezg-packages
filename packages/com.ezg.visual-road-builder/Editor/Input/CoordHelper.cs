#if UNITY_EDITOR
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Static coordinate conversions extracted from Coordinates.cs — parameterised
    /// so standalone types can convert without touching the window's private methods.</summary>
    internal static class CoordHelper
    {
        internal static Vector2 MouseToGridF(Rect canvas, Vector2 mouse, RoadCanvasDoc doc, ViewState view)
        {
            float fx = (mouse.x - canvas.x - GridConst.GutterLeft - GridConst.OuterMargin) / view.CellPixelSize;
            float fyScreen = (mouse.y - canvas.y - GridConst.GutterTop - GridConst.OuterMargin) / view.CellPixelSize;
            return new Vector2(fx, (doc.GridHeight - 1) - fyScreen);
        }

        internal static Vector2 PointToPixelF(Rect canvas, float x, float y, RoadCanvasDoc doc, ViewState view)
        {
            return new Vector2(
                canvas.x + GridConst.GutterLeft + GridConst.OuterMargin + x * view.CellPixelSize,
                canvas.y + GridConst.GutterTop + GridConst.OuterMargin + (doc.GridHeight - 1 - y) * view.CellPixelSize);
        }

        internal static bool TryPixelToHalfPoint(Rect canvas, Vector2 mouse, RoadCanvasDoc doc, ViewState view,
            out Vector2Int p2)
        {
            Vector2 f = MouseToGridF(canvas, mouse, doc, view);
            p2 = new Vector2Int(Mathf.RoundToInt(f.x * 2f), Mathf.RoundToInt(f.y * 2f));
            return p2.x >= 0 && p2.x <= (doc.GridWidth - 1) * 2
                   && p2.y >= 0 && p2.y <= (doc.GridHeight - 1) * 2;
        }
    }
}
#endif
