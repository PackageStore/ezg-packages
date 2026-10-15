using System.Collections.Generic;
using UnityEngine;

namespace Ezg.BugLogger.UI
{
    /// <summary>Rút gọn nét bút vẽ tay (Ramer–Douglas–Peucker) — ít đỉnh hơn, hình giữ nguyên trong sai số cho trước.</summary>
    internal static class BugPolyline
    {
        #region Fields

        private static readonly Stack<Vector2Int> Ranges = new Stack<Vector2Int>(64);
        private static readonly List<bool> Keep = new List<bool>(256);

        #endregion

        #region Public Methods

        /// <summary>
        ///     Bỏ các điểm lệch khỏi đường nối hai điểm giữ lại ít hơn <paramref name="tolerance" />. Luôn giữ điểm đầu và
        ///     cuối. Không đệ quy (nét dài không tràn stack).
        /// </summary>
        public static Vector2[] Simplify(IReadOnlyList<Vector2> points, float tolerance)
        {
            var count = points.Count;
            if (count <= 2 || tolerance <= 0f)
            {
                var copy = new Vector2[count];
                for (var i = 0; i < count; i++) copy[i] = points[i];
                return copy;
            }

            Keep.Clear();
            for (var i = 0; i < count; i++) Keep.Add(false);
            Keep[0] = true;
            Keep[count - 1] = true;

            var toleranceSquared = tolerance * tolerance;
            Ranges.Clear();
            Ranges.Push(new Vector2Int(0, count - 1));
            while (Ranges.Count > 0)
            {
                var range = Ranges.Pop();
                var start = points[range.x];
                var end = points[range.y];
                var farthest = -1;
                var farthestDistance = toleranceSquared;
                for (var i = range.x + 1; i < range.y; i++)
                {
                    var distance = DistanceToSegmentSquared(points[i], start, end);
                    if (distance <= farthestDistance) continue;

                    farthestDistance = distance;
                    farthest = i;
                }

                if (farthest < 0) continue;

                Keep[farthest] = true;
                Ranges.Push(new Vector2Int(range.x, farthest));
                Ranges.Push(new Vector2Int(farthest, range.y));
            }

            var kept = 0;
            for (var i = 0; i < count; i++)
                if (Keep[i])
                    kept++;

            var result = new Vector2[kept];
            var index = 0;
            for (var i = 0; i < count; i++)
                if (Keep[i])
                    result[index++] = points[i];
            return result;
        }

        #endregion

        #region Private Methods

        private static float DistanceToSegmentSquared(Vector2 point, Vector2 start, Vector2 end)
        {
            var segment = end - start;
            var lengthSquared = segment.sqrMagnitude;
            if (lengthSquared < 1e-8f) return (point - start).sqrMagnitude;

            var t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
            return (point - (start + segment * t)).sqrMagnitude;
        }

        #endregion
    }
}
