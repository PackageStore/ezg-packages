#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Crop darkened region + handle rects + size label.</summary>
    internal sealed class CropOverlayRenderer
    {
        private readonly ToolContext _ctx;
        private readonly ToolStyles _styles;

        internal CropOverlayRenderer(ToolContext ctx, ToolStyles styles)
        {
            _ctx = ctx;
            _styles = styles;
        }

        /// <summary>8 handles for Crop Tool: 0=TL, 1=T, 2=TR, 3=R, 4=BR, 5=B, 6=BL, 7=L</summary>
        internal Rect[] GetCropHandleRects(Rect canvas, out Rect proposedCropRect)
        {
            var doc = _ctx.Doc;
            var view = _ctx.View;
            float pL = CoordHelper.PointToPixelF(canvas, -view.CropDeltaLeft, 0, doc, view).x;
            float pR = CoordHelper.PointToPixelF(canvas, doc.GridWidth - 1 + view.CropDeltaRight, 0, doc, view).x;
            float pB = CoordHelper.PointToPixelF(canvas, 0, -view.CropDeltaDown, doc, view).y;
            float pT = CoordHelper.PointToPixelF(canvas, 0, doc.GridHeight - 1 + view.CropDeltaUp, doc, view).y;

            float minX = Mathf.Min(pL, pR);
            float maxX = Mathf.Max(pL, pR);
            float minY = Mathf.Min(pT, pB);
            float maxY = Mathf.Max(pT, pB);

            proposedCropRect = new Rect(minX, minY, maxX - minX, maxY - minY);

            const float hs = 12f;
            const float hhs = hs * 0.5f;

            float midX = (minX + maxX) * 0.5f;
            float midY = (minY + maxY) * 0.5f;

            return new Rect[]
            {
                new(minX - hhs, minY - hhs, hs, hs), // 0: TopLeft
                new(midX - hhs, minY - hhs, hs, hs), // 1: Top
                new(maxX - hhs, minY - hhs, hs, hs), // 2: TopRight
                new(maxX - hhs, midY - hhs, hs, hs), // 3: Right
                new(maxX - hhs, maxY - hhs, hs, hs), // 4: BottomRight
                new(midX - hhs, maxY - hhs, hs, hs), // 5: Bottom
                new(minX - hhs, maxY - hhs, hs, hs), // 6: BottomLeft
                new(minX - hhs, midY - hhs, hs, hs)  // 7: Left
            };
        }

        internal void DrawCropOverlay(Rect canvas)
        {
            var doc = _ctx.Doc;
            var view = _ctx.View;
            Rect[] handles = GetCropHandleRects(canvas, out Rect cropRect);

            var dark = new Color(0f, 0f, 0f, 0.55f);
            EditorGUI.DrawRect(new Rect(canvas.x, canvas.y, canvas.width,
                Mathf.Max(0f, cropRect.y - canvas.y)), dark);
            EditorGUI.DrawRect(new Rect(canvas.x, cropRect.yMax, canvas.width,
                Mathf.Max(0f, canvas.yMax - cropRect.yMax)), dark);
            EditorGUI.DrawRect(new Rect(canvas.x, cropRect.y,
                Mathf.Max(0f, cropRect.x - canvas.x), cropRect.height), dark);
            EditorGUI.DrawRect(new Rect(cropRect.xMax, cropRect.y,
                Mathf.Max(0f, canvas.xMax - cropRect.xMax), cropRect.height), dark);

            var frameColor = new Color(0.2f, 0.85f, 1f, 0.95f);
            DrawPrimitives.DrawRectBorder(cropRect, 2f, frameColor);

            Vector2 mouse = Event.current.mousePosition;
            for (int i = 0; i < handles.Length; i++)
            {
                bool isHover = handles[i].Contains(mouse) || view.CropDragHandle == i;
                Color fill = isHover ? new Color(0.35f, 0.85f, 1f) : Color.white;
                EditorGUI.DrawRect(handles[i], fill);
                DrawPrimitives.DrawRectBorder(handles[i], 1f, Color.black);
            }

            int newW = doc.GridWidth + view.CropDeltaLeft + view.CropDeltaRight;
            int newH = doc.GridHeight + view.CropDeltaDown + view.CropDeltaUp;
            string text = $"Crop: {newW} × {newH}  (L:{view.CropDeltaLeft:+#;-#;0} D:{view.CropDeltaDown:+#;-#;0} R:{view.CropDeltaRight:+#;-#;0} U:{view.CropDeltaUp:+#;-#;0})";

            var tagRect = new Rect(cropRect.x, cropRect.y - 22f, 290f, 20f);
            if (tagRect.y < canvas.y + GridConst.GutterTop) tagRect.y = cropRect.y + 4f;
            EditorGUI.DrawRect(tagRect, new Color(0.1f, 0.1f, 0.1f, 0.85f));
            GUI.Label(tagRect, text, _styles.TagStyle);
        }
    }
}
#endif
