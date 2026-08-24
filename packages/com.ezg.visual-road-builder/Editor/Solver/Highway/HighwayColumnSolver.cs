#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Pass 2 của solver Highway: lát lòng cao tốc bằng CỘT ô modular, quy ước y hệt road
    /// side/side_rim nhưng ô sâu gấp đôi. Mỗi cột rộng 0.5 ô dọc run = cặp <c>hway1x2_side</c> yaw /
    /// yaw+180 quanh CÙNG pivot ở TIM đường (mỗi ô sâu 1 ô ⇒ cặp phủ trọn 2 ô bề ngang), kèm
    /// <c>hway1x2_side_rim</c> cùng pivot/yaw làm vỉa hè 2 mép NGOÀI. Cột lát KHÍT run đã vẽ (không lố
    /// ra ngoài) và dừng đúng mép mesh ramp. Yaw thuần theo trục (ngang 0°, dọc 90°). Toạ độ nội bộ = NỬA ô.</summary>
    internal sealed class HighwayColumnSolver
    {
        private readonly RoadCanvasDoc _doc;
        private readonly RoadPartLibrary _library;
        private readonly RampDetector _rampDetector;

        internal HighwayColumnSolver(RoadCanvasDoc doc, RoadPartLibrary library, RampDetector rampDetector)
        {
            _doc = doc;
            _library = library;
            _rampDetector = rampDetector;
        }

        private bool HighwayTilesReady =>
            _library != null && _library.hway1x2_side != null && _library.hway1x2_side_rim != null;

        internal void CollectHighwayRuns(
            List<(int x2, int y2, int stem, int hwMask)> ramps,
            List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)> placements,
            HashSet<string> missing)
        {
            List<(float cx, float cy, bool horiz)> columns = CollectHighwayColumns(ramps);
            if (columns.Count == 0) return;
            if (!HighwayTilesReady)
            {
                if (_library == null || _library.hway1x2_side == null) missing.Add("Highway Tile Side");
                if (_library == null || _library.hway1x2_side_rim == null) missing.Add("Highway Tile Side Rim");
                return;
            }

            foreach ((float cx, float cy, bool horiz) in columns)
                ForEachHighwayColumnTile(cx, cy, horiz ? 0f : 90f, (tx, ty, tyaw) =>
                {
                    placements.Add((tx, ty, _library.hway1x2_side, tyaw, Vector3.one));
                    placements.Add((tx, ty, _library.hway1x2_side_rim, tyaw, Vector3.one));
                });
        }

        /// <summary>Duyệt 2 ô của MỘT cột cao tốc: cặp yaw / yaw+180 quanh CÙNG pivot ở tim đường.</summary>
        internal static void ForEachHighwayColumnTile(float cx, float cy, float yaw,
            System.Action<float, float, float> place)
        {
            for (int half = 0; half < 2; half++)
                place(cx, cy, (yaw + half * 180f) % 360f);
        }

        /// <summary>Tâm (đơn vị Ô) các cột cao tốc, gom theo run thẳng.</summary>
        internal List<(float cx, float cy, bool horiz)> CollectHighwayColumns(
            List<(int x2, int y2, int stem, int hwMask)> ramps)
        {
            var columns = new List<(float, float, bool)>();
            var hRows = new Dictionary<int, List<int>>();
            var vCols = new Dictionary<int, List<int>>();

            foreach (int id in _doc.HighwayEdges)
            {
                EdgeCodec.DecodeEdge(id, out int x2, out int y2, out int orient);
                Dictionary<int, List<int>> map = orient == 0 ? hRows : vCols;
                int key = orient == 0 ? y2 : x2;
                if (!map.TryGetValue(key, out List<int> l)) { l = new List<int>(); map[key] = l; }
                l.Add(orient == 0 ? x2 : y2);
            }

            var hSpans = new Dictionary<int, List<(int lo2, int hi2)>>();
            var vSpans = new Dictionary<int, List<(int lo2, int hi2)>>();
            if (ramps != null)
                foreach ((int x2, int y2, int stem, int _) in ramps)
                {
                    (bool horiz, int line2, int lo2, int hi2) = RampDetector.RampHighwaySpan(x2, y2, stem, _rampDetector.RampFlipped(x2, y2));
                    Dictionary<int, List<(int lo2, int hi2)>> map = horiz ? hSpans : vSpans;
                    if (!map.TryGetValue(line2, out List<(int lo2, int hi2)> l))
                    {
                        l = new List<(int lo2, int hi2)>();
                        map[line2] = l;
                    }
                    l.Add((lo2, hi2));
                }

            foreach (KeyValuePair<int, List<int>> kv in hRows)
                TileLine(kv.Value, kv.Key, true, hSpans.GetValueOrDefault(kv.Key), columns);
            foreach (KeyValuePair<int, List<int>> kv in vCols)
                TileLine(kv.Value, kv.Key, false, vSpans.GetValueOrDefault(kv.Key), columns);
            return columns;
        }

        /// <summary>Chia danh sách edge-start NỬA ô thành các run liền mạch, lát mỗi run bằng cột rộng
        /// NỬA ô căn khít 2 đầu run.</summary>
        private static void TileLine(List<int> starts, int line2, bool horiz,
            List<(int lo2, int hi2)> rampSpans, List<(float, float, bool)> columns)
        {
            starts.Sort();
            int n = starts.Count;
            int i = 0;
            while (i < n)
            {
                int runLo2 = starts[i];
                int last2 = starts[i];
                i++;
                while (i < n && starts[i] == last2 + 1) { last2 = starts[i]; i++; }
                int runHi2 = last2 + 1;
                for (float c2 = runLo2 + 0.5f; c2 < runHi2; c2 += 1f)
                {
                    if (CoveredByRamp(rampSpans, c2)) continue;
                    columns.Add(horiz ? (c2 * 0.5f, line2 * 0.5f, true) : (line2 * 0.5f, c2 * 0.5f, false));
                }
            }
        }

        /// <summary>Cột tâm <paramref name="c2"/> (nửa ô) có nằm TRỌN trong một đoạn ramp?</summary>
        private static bool CoveredByRamp(List<(int lo2, int hi2)> rampSpans, float c2)
        {
            if (rampSpans == null) return false;
            foreach ((int lo2, int hi2) in rampSpans)
                if (c2 - 0.5f >= lo2 && c2 + 0.5f <= hi2) return true;
            return false;
        }
    }
}
#endif
