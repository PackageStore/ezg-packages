#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Shared drawing helpers used by multiple renderers and overlays.</summary>
    internal static class DrawPrimitives
    {
        internal static void DrawRectBorder(Rect r, float t, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, t), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - t, r.width, t), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, t, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        /// <summary>Mũi tên chỉ hướng MẶT: rot 0 = +Z (lên trên canvas), quay theo chiều
        /// kim đồng hồ mỗi nấc 90° (khớp yaw = rot * 90 khi Apply).</summary>
        internal static void DrawFacingArrow(Rect r, int rot, Color color)
        {
            Vector2 dir = rot switch
            {
                1 => new Vector2(1f, 0f),   // E
                2 => new Vector2(0f, 1f),   // S (pixel y xuống)
                3 => new Vector2(-1f, 0f),  // W
                _ => new Vector2(0f, -1f),  // N
            };
            Vector2 c = r.center;
            float len = Mathf.Min(r.width, r.height) * 0.32f;
            const float thick = 3f;

            // Thân mũi tên: rect mảnh từ tâm về hướng mặt.
            Vector2 tip = c + dir * len;
            if (dir.x == 0f)
            {
                EditorGUI.DrawRect(new Rect(c.x - thick * 0.5f, Mathf.Min(c.y, tip.y), thick, len), color);
            }
            else
            {
                EditorGUI.DrawRect(new Rect(Mathf.Min(c.x, tip.x), c.y - thick * 0.5f, len, thick), color);
            }

            // Đầu mũi tên: ô vuông nhỏ ở tip.
            const float head = 7f;
            EditorGUI.DrawRect(new Rect(tip.x - head * 0.5f, tip.y - head * 0.5f, head, head), color);
        }
    }
}
#endif
