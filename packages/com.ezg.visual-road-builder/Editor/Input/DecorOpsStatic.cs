#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Static decor operations for standalone tool types — parameterised
    /// versions of the partial-class methods in Model/DecorOps.cs.</summary>
    internal static class DecorOpsStatic
    {
        /// <summary>Index decor gần toạ độ lưới f nhất trong bán kính 0.35 ô, -1 nếu không có.</summary>
        internal static int FindDecorAt(Vector2 f, List<DecorItem> decors)
        {
            const float radius = 0.35f;
            int best = -1;
            float bestD = radius;
            for (int i = decors.Count - 1; i >= 0; i--)
            {
                float d = Vector2.Distance(f, new Vector2(decors[i].x2 * 0.5f, decors[i].y2 * 0.5f));
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        internal static void PlaceDecorAt(Vector2Int p2, DecorState state, RoadCanvasDoc doc)
        {
            if (state.Library == null || state.Library.entries.Count == 0) return;
            int entry = Mathf.Clamp(state.EntryIndex, 0, state.Library.entries.Count - 1);

            // Không đặt trùng item cùng loại tại cùng điểm (kéo rải đi qua nhiều lần).
            for (int i = 0; i < doc.Decors.Count; i++)
            {
                if (doc.Decors[i].x2 == p2.x && doc.Decors[i].y2 == p2.y && doc.Decors[i].entry == entry)
                    return;
            }

            doc.Decors.Add(new DecorItem
            {
                entry = entry, x2 = p2.x, y2 = p2.y, rot = 0, scale = RollScale(entry, state),
            });
        }

        /// <summary>Rải random trong vùng (ô): số item = diện tích × mật độ, vị trí snap nửa ô,
        /// không trùng điểm cùng loại; xoay random 4 hướng nếu bật.</summary>
        internal static void ScatterDecorsInRect(float minX, float minY, float maxX, float maxY,
            DecorState state, RoadCanvasDoc doc)
        {
            if (state.Library == null || state.Library.entries.Count == 0) return;
            float w = maxX - minX, h = maxY - minY;
            if (w <= 0.01f || h <= 0.01f) return;

            int entry = Mathf.Clamp(state.EntryIndex, 0, state.Library.entries.Count - 1);
            int count = Mathf.Max(1, Mathf.RoundToInt(w * h * state.Density));
            int placed = 0;

            // Thử tối đa count*4 lần để bù các lần trúng điểm đã có.
            for (int attempt = 0; attempt < count * 4 && placed < count; attempt++)
            {
                int x2 = Mathf.Clamp(Mathf.RoundToInt(Random.Range(minX, maxX) * 2f), 0, (doc.GridWidth - 1) * 2);
                int y2 = Mathf.Clamp(Mathf.RoundToInt(Random.Range(minY, maxY) * 2f), 0, (doc.GridHeight - 1) * 2);

                bool exists = false;
                for (int i = 0; i < doc.Decors.Count && !exists; i++)
                    exists = doc.Decors[i].x2 == x2 && doc.Decors[i].y2 == y2 && doc.Decors[i].entry == entry;
                if (exists) continue;

                doc.Decors.Add(new DecorItem
                {
                    entry = entry, x2 = x2, y2 = y2,
                    rot = state.RandomRot ? Random.Range(0, 4) : 0,
                    scale = RollScale(entry, state),
                });
                placed++;
            }
        }

        internal static void EraseDecorsInRect(float minX, float minY, float maxX, float maxY, RoadCanvasDoc doc)
        {
            doc.Decors.RemoveAll(d =>
            {
                float x = d.x2 * 0.5f, y = d.y2 * 0.5f;
                return x >= minX - 0.001f && x <= maxX + 0.001f
                       && y >= minY - 0.001f && y <= maxY + 0.001f;
            });
        }

        /// <summary>Roll scale ngẫu nhiên theo config [scaleMin, scaleMax] của entry.</summary>
        private static float RollScale(int entry, DecorState state)
        {
            if (state.Library == null || entry < 0 || entry >= state.Library.entries.Count) return 1f;
            DecorLibrary.DecorEntry e = state.Library.entries[entry];
            float min = Mathf.Max(0.01f, Mathf.Min(e.scaleMin, e.scaleMax));
            float max = Mathf.Max(0.01f, Mathf.Max(e.scaleMin, e.scaleMax));
            return Random.Range(min, max);
        }
    }
}
#endif
