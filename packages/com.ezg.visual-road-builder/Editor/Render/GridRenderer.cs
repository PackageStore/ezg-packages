#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Background grid lines + axis ruler overlays.</summary>
    internal sealed class GridRenderer
    {
        private readonly ToolContext _ctx;
        private readonly ToolStyles _styles;

        internal GridRenderer(ToolContext ctx, ToolStyles styles)
        {
            _ctx = ctx;
            _styles = styles;
        }

        /// <summary>Lưới ô: nét mờ mỗi ô, nét đậm hơn mỗi AxisStep ô; trục toạ độ 0 (X=0 và Y=0)
        /// tô xanh và DÀY hơn để dễ định vị gốc.</summary>
        internal void DrawGridLines(Rect canvas)
        {
            var doc = _ctx.Doc;
            var view = _ctx.View;
            var line = new Color(1f, 1f, 1f, 0.06f);
            var major = new Color(1f, 1f, 1f, 0.13f);
            var axis = new Color(0.45f, 0.72f, 1f, 0.75f);
            int step = AxisStep();
            int zeroX = -doc.OriginCell.x, zeroY = -doc.OriginCell.y;

            float top = CoordHelper.PointToPixelF(canvas, 0f, doc.GridHeight - 1, doc, view).y;
            float h = (doc.GridHeight - 1) * view.CellPixelSize;
            for (int x = 0; x < doc.GridWidth; x++)
            {
                float px = CoordHelper.PointToPixelF(canvas, x, 0f, doc, view).x;
                if (x == zeroX) EditorGUI.DrawRect(new Rect(px - 1f, top, 2f, h), axis);
                else EditorGUI.DrawRect(new Rect(px, top, 1f, h), x % step == 0 ? major : line);
            }
            float left = CoordHelper.PointToPixelF(canvas, 0f, 0f, doc, view).x;
            float w = (doc.GridWidth - 1) * view.CellPixelSize;
            for (int y = 0; y < doc.GridHeight; y++)
            {
                float py = CoordHelper.PointToPixelF(canvas, 0f, y, doc, view).y;
                if (y == zeroY) EditorGUI.DrawRect(new Rect(left, py - 1f, w, 2f), axis);
                else EditorGUI.DrawRect(new Rect(left, py, w, 1f), y % step == 0 ? major : line);
            }
        }

        /// <summary>Bước đánh số trục (ô) sao cho nhãn cách nhau tối thiểu ~34px theo zoom.</summary>
        internal int AxisStep()
        {
            int raw = Mathf.Max(1, Mathf.CeilToInt(34f / _ctx.View.CellPixelSize));
            int[] nice = { 1, 2, 5, 10, 20, 25, 50, 100 };
            foreach (int n in nice)
                if (n >= raw) return n;
            return raw;
        }

        /// <summary>Vẽ thước tỉ lệ trục X (mép trên window pane) & Y (mép trái window pane) cố định kiểu Photoshop.</summary>
        internal void DrawAxisRulersOverlay(Rect canvas, Rect scrollRect)
        {
            var doc = _ctx.Doc;
            var view = _ctx.View;
            const float rulerW = 32f;
            const float rulerH = 20f;

            GUI.BeginGroup(scrollRect);

            var bgRuler = new Color(0.17f, 0.17f, 0.17f, 0.96f);
            var bgCorner = new Color(0.14f, 0.14f, 0.14f, 1f);
            var borderColor = new Color(1f, 1f, 1f, 0.15f);
            var tickColor = new Color(1f, 1f, 1f, 0.45f);

            EditorGUI.DrawRect(new Rect(rulerW, 0f, scrollRect.width - rulerW, rulerH), bgRuler);
            EditorGUI.DrawRect(new Rect(0f, rulerH, rulerW, scrollRect.height - rulerH), bgRuler);
            EditorGUI.DrawRect(new Rect(0f, 0f, rulerW, rulerH), bgCorner);

            EditorGUI.DrawRect(new Rect(rulerW, rulerH - 1f, scrollRect.width - rulerW, 1f), borderColor);
            EditorGUI.DrawRect(new Rect(rulerW - 1f, rulerH, 1f, scrollRect.height - rulerH), borderColor);

            int step = AxisStep();
            for (int x = 0; x < doc.GridWidth; x += step)
            {
                float pxCanvas = CoordHelper.PointToPixelF(canvas, x, 0f, doc, view).x;
                float screenX = pxCanvas - view.Scroll.x;

                if (screenX >= rulerW && screenX <= scrollRect.width)
                {
                    EditorGUI.DrawRect(new Rect(screenX, rulerH - 6f, 1f, 6f), tickColor);
                    GUI.Label(new Rect(screenX - 20f, 1f, 40f, rulerH - 4f),
                        (x + doc.OriginCell.x).ToString(), _styles.RulerXStyle);
                }
            }

            for (int y = 0; y < doc.GridHeight; y += step)
            {
                float pyCanvas = CoordHelper.PointToPixelF(canvas, 0f, y, doc, view).y;
                float screenY = pyCanvas - view.Scroll.y;

                if (screenY >= rulerH && screenY <= scrollRect.height)
                {
                    EditorGUI.DrawRect(new Rect(rulerW - 6f, screenY, 6f, 1f), tickColor);
                    GUI.Label(new Rect(1f, screenY - 8f, rulerW - 7f, 16f),
                        (y + doc.OriginCell.y).ToString(), _styles.RulerYStyle);
                }
            }

            Vector2 mouseWin = Event.current.mousePosition;
            float mouseLocalX = mouseWin.x - scrollRect.x;
            float mouseLocalY = mouseWin.y - scrollRect.y;
            var cursorTickColor = new Color(0.4f, 0.8f, 1f, 0.85f);

            if (mouseLocalX >= rulerW && mouseLocalX <= scrollRect.width &&
                mouseLocalY >= 0 && mouseLocalY <= scrollRect.height)
            {
                EditorGUI.DrawRect(new Rect(mouseLocalX, 0f, 1f, rulerH), cursorTickColor);
            }
            if (mouseLocalY >= rulerH && mouseLocalY <= scrollRect.height &&
                mouseLocalX >= 0 && mouseLocalX <= scrollRect.width)
            {
                EditorGUI.DrawRect(new Rect(0f, mouseLocalY, rulerW, 1f), cursorTickColor);
            }

            GUI.Label(new Rect(0f, 2f, rulerW, rulerH - 2f), "XY", _styles.RulerXStyle);

            GUI.EndGroup();
        }
    }
}
#endif
