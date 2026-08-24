#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Draws boundary boxes, hover highlight, tooltip pill.</summary>
    internal sealed class DebugBoundaryRenderer
    {
        private readonly ViewState _view;
        private readonly ToolStyles _styles;

        internal DebugBoundaryRenderer(ViewState view, ToolStyles styles)
        {
            _view = view;
            _styles = styles;
        }

        /// <summary>Vẽ boundary (bounding box) cho các element của những lớp đang bật debug boundary.</summary>
        internal void DrawDebugBoundaries(Rect canvas, List<DebugBoundaryItem> items)
        {
            // Box NHỎ NHẤT chứa con trỏ mới được highlight + tooltip: khối station/parking phủ trọn
            // các piece đường bên trong nên chọn theo thứ tự vẽ sẽ nhặt sai cái to.
            int hovered = -1;
            if (_view.HoverCellValid)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (!items[i].Rect.Contains(_view.HoverPixel)) continue;
                    if (hovered < 0 || RectArea(items[i].Rect) < RectArea(items[hovered].Rect)) hovered = i;
                }
            }

            // Alpha 0% chỉ tắt các box NỀN — box dưới con trỏ vẫn đậm, thành chế độ "chỉ xem cái đang trỏ".
            if (_view.DebugBoundaryAlpha > 0f)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (i != hovered) DrawRectOutline(items[i].Rect, FadeDebug(items[i].Color), 1.2f);
                }
            }

            if (hovered >= 0)
            {
                var item = items[hovered];
                EditorGUI.DrawRect(item.Rect, new Color(1f, 1f, 0f, 0.25f));
                DrawRectOutline(item.Rect, Color.yellow, 2f);

                Vector2 mousePos = _view.HoverPixel;
                string text = item.Name;
                Vector2 size = _styles.PillStyle.CalcSize(new GUIContent(text));
                float labelW = Mathf.Max(60f, size.x + 16f);
                float labelH = Mathf.Max(20f, size.y + 4f);

                var tooltipRect = new Rect(mousePos.x + 12f, mousePos.y - 24f, labelW, labelH);
                if (tooltipRect.xMax > canvas.xMax) tooltipRect.x = mousePos.x - labelW - 8f;
                if (tooltipRect.y < canvas.y) tooltipRect.y = mousePos.y + 16f;

                EditorGUI.DrawRect(tooltipRect, new Color(0.08f, 0.08f, 0.08f, 0.9f));
                DrawRectOutline(tooltipRect, Color.yellow, 1f);
                GUI.Label(tooltipRect, text, _styles.PillStyle);
            }
        }

        internal static float RectArea(Rect r) => r.width * r.height;

        /// <summary>Nhân alpha gốc của box với slider — giữ tương quan đậm/nhạt giữa lòng đường và
        /// vỉa hè (<see cref="DebugBoundaryCollector.DebugRoadRimColor"/> vốn đã nhạt hơn) ở mọi mức slider.</summary>
        internal Color FadeDebug(Color color) =>
            new(color.r, color.g, color.b, color.a * _view.DebugBoundaryAlpha);

        internal static void DrawRectOutline(Rect rect, Color color, float thickness = 1.2f)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }
    }
}
#endif
