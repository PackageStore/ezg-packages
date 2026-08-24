#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Vẽ decor trên canvas: chấm màu + mũi tên hướng cho từng item, preview vùng brush đang
    /// khoanh, và ghost hover.</summary>
    public sealed partial class VisualRoadBuilderTool
    {
        private void DrawDecors(Rect canvas)
        {
            for (int i = 0; i < _decors.Count; i++)
            {
                DecorItem item = _decors[i];
                Color c = DecorColor(item.entry);
                Vector2 p = PointToPixelF(canvas, item.x2 * 0.5f, item.y2 * 0.5f);
                const float s = 9f;
                var r = new Rect(p.x - s * 0.5f, p.y - s * 0.5f, s, s);
                EditorGUI.DrawRect(r, i == _draggingDecor ? Color.white : c);
                DrawRectBorder(r, 1f, Color.black);
                DrawFacingArrow(new Rect(p.x - 14f, p.y - 14f, 28f, 28f), item.rot, c);
            }

            // Preview vùng brush đang khoanh.
            if (_areaDragging)
            {
                Vector2 a = PointToPixelF(canvas, _areaStart.x, _areaStart.y);
                Vector2 b = PointToPixelF(canvas, _areaEnd.x, _areaEnd.y);
                var r = Rect.MinMaxRect(
                    Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                    Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
                Color fill = _areaErasing
                    ? new Color(0.9f, 0.25f, 0.2f, 0.2f)
                    : new Color(0.3f, 0.85f, 0.35f, 0.2f);
                EditorGUI.DrawRect(r, fill);
                DrawRectBorder(r, 1f, _areaErasing ? Color.red : Color.green);
            }

            // Ghost hover khi ở mode Decor và không thao tác gì.
            if (_mode == PaintMode.Decor && !_decorAreaMode && _decorHover
                && _draggingDecor < 0 && !_paintingDecor && !_erasingDecor)
            {
                Color c = DecorColor(_decorEntryIndex);
                c.a = 0.5f;
                Vector2 p = PointToPixelF(canvas, _decorHoverP2.x * 0.5f, _decorHoverP2.y * 0.5f);
                EditorGUI.DrawRect(new Rect(p.x - 4.5f, p.y - 4.5f, 9f, 9f), c);
            }
        }

        private Color DecorColor(int entry)
        {
            if (_decorLibrary != null && entry >= 0 && entry < _decorLibrary.entries.Count)
                return _decorLibrary.entries[entry].canvasColor;
            return Color.magenta;
        }
    }
}
#endif
