#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Deduplicates placement lists by quantised key — shared by Road, Road 2, PATH.</summary>
    internal static class DedupePlacement
    {
        // Lượng tử hoá vị trí phải MỊN hơn lưới đặt ô nhỏ nhất, nếu không 2 ô khác chỗ sẽ chung khoá
        // và bị dedupe oan: lớp PATH đặt cột lệch ±0.125 (PathTileColumnOffsetCells) nên phải nhân 8.
        internal static (GameObject, int, int, int) Key(
            (float x, float y, GameObject prefab, float yaw, Vector3 scaleMul) p) =>
            (p.prefab, Mathf.RoundToInt(p.x * 8f), Mathf.RoundToInt(p.y * 8f), Mathf.RoundToInt(p.yaw) % 360);

        /// <summary>Lọc TOÀN BỘ <paramref name="placements"/>, giữ lại đúng 1 bản cho mỗi khoá có trong
        /// <paramref name="isolatedKeys"/> (bản đầu tiên theo thứ tự list) — khoá KHÔNG cô lập giữ
        /// nguyên số lượng dù trùng (dữ liệu migrate hợp lệ có sẵn trùng lặp).</summary>
        internal static void DedupeIsolatedKeys(
            List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)> placements,
            HashSet<(GameObject, int, int, int)> isolatedKeys)
        {
            if (isolatedKeys == null || isolatedKeys.Count == 0) return;
            var seen = new HashSet<(GameObject, int, int, int)>();
            int write = 0;
            for (int i = 0; i < placements.Count; i++)
            {
                var p = placements[i];
                var key = Key(p);
                if (isolatedKeys.Contains(key) && !seen.Add(key)) continue;
                placements[write++] = p;
            }
            placements.RemoveRange(write, placements.Count - write);
        }

        /// <summary>Bỏ ô TRÙNG HOÀN TOÀN (cùng prefab + vị trí + yaw) trong [start, count) — 2 mảnh giao
        /// lệch nhau 1.5 ô cùng chìa arm vào đúng một chỗ, mesh y hệt nhau nên giữ 1 ô.</summary>
        internal static void Dedupe(
            List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)> placements, int start)
        {
            var seen = new HashSet<(GameObject, int, int, int)>();
            int write = start;
            for (int i = start; i < placements.Count; i++)
            {
                var p = placements[i];
                if (!seen.Add(Key(p))) continue;
                placements[write++] = p;
            }
            placements.RemoveRange(write, placements.Count - write);
        }
    }
}
#endif
