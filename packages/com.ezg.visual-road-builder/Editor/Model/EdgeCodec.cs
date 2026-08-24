#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Encode, decode, double-coords, split-span, pair-half-edges for edge ints.
    /// Also DecodeRampAnchor (R7: pure bit-unpacking, no highway-specific logic).</summary>
    internal static class EdgeCodec
    {
        /// <summary>Nhân đôi toạ độ edge (ô nguyên → nửa ô), giữ nguyên orient.</summary>
        internal static void DoubleEdgeCoords(List<int> edges)
        {
            for (int i = 0; i < edges.Count; i++)
            {
                DecodeEdge(edges[i], out int x, out int y, out int orient);
                edges[i] = ((y * 2) << 13) | ((x * 2) << 1) | orient;
            }
        }

        /// <summary>Edge dài 1 ô (span 2 nấc) → 2 edge nửa ô liền kề (span 1 nấc), cùng anchor gốc.</summary>
        internal static void SplitEdgeSpan(List<int> edges)
        {
            var result = new List<int>(edges.Count * 2);
            foreach (int id in edges)
            {
                DecodeEdge(id, out int x2, out int y2, out int orient);
                result.Add((y2 << 13) | (x2 << 1) | orient);
                int nx2 = orient == 0 ? x2 + 1 : x2;
                int ny2 = orient == 1 ? y2 + 1 : y2;
                result.Add((ny2 << 13) | (nx2 << 1) | orient);
            }
            edges.Clear();
            edges.AddRange(result);
        }

        internal static int EncodeEdge(Vector2Int a, Vector2Int b)
        {
            // Chuẩn hoá: a là đầu nhỏ hơn (trái hoặc dưới).
            if (b.x < a.x || b.y < a.y) (a, b) = (b, a);
            int orient = b.y > a.y ? 1 : 0;
            return (a.y << 13) | (a.x << 1) | orient;
        }

        internal static void DecodeEdge(int id, out int x, out int y, out int orient)
        {
            orient = id & 1;
            x = (id >> 1) & 0xFFF;
            y = id >> 13;
        }

        internal static int EncodeEdgeRaw(int x2, int y2, int orient) => (y2 << 13) | (x2 << 1) | orient;

        /// <summary>Ghép 2 nửa-edge liền kề trên MỘT line thành 1 edge "view cũ" (span 2 nấc, node giữa
        /// KHÔNG có mesh) — nghịch đảo <see cref="SplitEdgeSpan"/>. Tái tạo anchor từ count nửa-edge là
        /// deconvolution theo kernel [1,1]: trên mỗi chain (dải liên tục), số anchor bắt đầu tại p là
        /// s(p) = n(p) − s(p−1) (s=0 ở biên chain) — nghiệm không-âm DUY NHẤT, giải bằng đúng 1 lượt quét
        /// trái→phải. s(p) âm (dữ liệu hỏng) bị clamp 0 (leftover, bỏ). s(p) > 0 tại nấc CUỐI chain nghĩa
        /// là edge sẽ thò ra ngoài chain → cũng là leftover (brush nửa ô vẽ mới, không đến từ migrate) →
        /// OMIT khỏi view legacy, đúng theo thiết kế — dùng cho các consumer legacy quét mask thô
        /// (BlockSolver edge-fill, HighwaySolver ramp scan).</summary>
        internal static List<int> PairHalfEdges(List<int> edges)
        {
            var counts = new Dictionary<(int orient, int perp, int along), int>();
            var lines = new Dictionary<(int orient, int perp), List<int>>();
            foreach (int id in edges)
            {
                DecodeEdge(id, out int x2, out int y2, out int orient);
                int perp = orient == 0 ? y2 : x2;
                int along = orient == 0 ? x2 : y2;
                var ak = (orient, perp, along);
                counts.TryGetValue(ak, out int c);
                counts[ak] = c + 1;
                if (c == 0)
                {
                    var lk = (orient, perp);
                    if (!lines.TryGetValue(lk, out List<int> list)) lines[lk] = list = new List<int>();
                    list.Add(along);
                }
            }

            var result = new List<int>();
            foreach (KeyValuePair<(int orient, int perp), List<int>> kv in lines)
            {
                List<int> alongs = kv.Value;
                alongs.Sort();
                int orient = kv.Key.orient, perp = kv.Key.perp;
                int sPrev = 0;
                for (int i = 0; i < alongs.Count; i++)
                {
                    int along = alongs[i];
                    if (i > 0 && along != alongs[i - 1] + 1) sPrev = 0; // biên chain mới

                    int s = counts[(orient, perp, along)] - sPrev;
                    if (s < 0) s = 0;

                    bool chainEnd = i == alongs.Count - 1 || alongs[i + 1] != along + 1;
                    if (!chainEnd)
                    {
                        for (int n = 0; n < s; n++)
                            result.Add(orient == 0 ? EncodeEdgeRaw(along, perp, 0) : EncodeEdgeRaw(perp, along, 1));
                    }

                    sPrev = s;
                }
            }
            return result;
        }

        // R7: DecodeRampAnchor — pure bit-unpacking, moved from HighwaySolver.
        internal static int RampAnchorKey(int x2, int y2) => (y2 << 13) | x2;

        internal static void DecodeRampAnchor(int key, out int x2, out int y2)
        {
            x2 = key & 0x1FFF;
            y2 = key >> 13;
        }
    }
}
#endif
