#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Merges block strips then emits half/full edge-fill slots at strip boundaries.</summary>
    internal sealed class BlockEdgeFiller
    {
        private readonly RoadCanvasDoc _doc;

        internal BlockEdgeFiller(RoadCanvasDoc doc) { _doc = doc; }

        /// <summary>Lấp khe giữa mesh khối và đường thường — chạy SAU KHI đã gom mọi dải khối, vì mép
        /// một dải chỉ hở thật khi bên kia không phải khối khác. Các dải trên cùng một hàng VÀ cùng một
        /// NỬA đường được GỘP trước nên 2 khối CHẠM nhau không còn mép trong; 2 khối đối diện nhau qua
        /// con đường nằm ở 2 nửa khác nhau nên lấp mép độc lập. Mỗi mép hở đúng nửa ô; 2 khe nửa ô KỀ
        /// nhau (2 khối cách nhau đúng 1 ô) gộp thành 1 mảnh đủ ô vào <paramref name="fulls"/> — 2 part
        /// nửa ô úp lưng sẽ sai nét và hở 0.25 ô mỗi đầu. Sắp <paramref name="strips"/> tại chỗ để
        /// thứ tự kết quả ổn định giữa các lần bake.</summary>
        internal void CollectBlockEdgeFills(
            int[] roadMasks, List<BlockStrip> strips,
            List<(float x, float y, float yaw, int sides)> halves,
            List<(float x, float y, float yaw, int sides)> fulls)
        {
            if (strips == null || strips.Count == 0) return;

            strips.Sort((a, b) =>
            {
                if (a.Horizontal != b.Horizontal) return a.Horizontal ? -1 : 1;
                if (a.Line2 != b.Line2) return a.Line2.CompareTo(b.Line2);
                if (a.Side != b.Side) return a.Side.CompareTo(b.Side);
                if (a.Lo2 != b.Lo2) return a.Lo2.CompareTo(b.Lo2);
                return a.Hi2.CompareTo(b.Hi2);
            });

            var merged = new List<(int lo2, int hi2)>();
            int i = 0;
            while (i < strips.Count)
            {
                bool horizontal = strips[i].Horizontal;
                int line2 = strips[i].Line2;
                int side = strips[i].Side;
                merged.Clear();
                // Dải chồng nhau HOẶC chạm nhau (lo2 kế tiếp == hi2) là MỘT dải liền.
                for (; i < strips.Count && strips[i].Horizontal == horizontal
                       && strips[i].Line2 == line2 && strips[i].Side == side; i++)
                {
                    int last = merged.Count - 1;
                    if (last >= 0 && strips[i].Lo2 <= merged[last].hi2)
                    {
                        if (strips[i].Hi2 > merged[last].hi2) merged[last] = (merged[last].lo2, strips[i].Hi2);
                        continue;
                    }
                    merged.Add((strips[i].Lo2, strips[i].Hi2));
                }
                CollectLineEdgeFills(roadMasks, merged, line2, horizontal, side, halves, fulls);
            }
        }

        /// <summary>Lấp mép cho các dải ĐÃ GỘP trên một hàng đường, ở đúng nửa <paramref name="side"/>
        /// mà dải chiếm. Khe = đoạn nửa ô [start2, start2+1] ngay ngoài mép dải; 2 khe kề nhau → 1 mảnh
        /// đủ ô canh giữa, khe lẻ → 1 part nửa ô (+X hướng TỪ khối RA ngoài: E=0° S=90° W=180° N=270°).</summary>
        private void CollectLineEdgeFills(
            int[] roadMasks, List<(int lo2, int hi2)> merged, int line2, bool horizontal, int side,
            List<(float x, float y, float yaw, int sides)> halves,
            List<(float x, float y, float yaw, int sides)> fulls)
        {
            var holes = new List<(int start2, int outward)>();
            foreach ((int lo2, int hi2) in merged)
            {
                if (StripEdgeHoleOpen(roadMasks, merged, line2, lo2, -1, horizontal))
                    holes.Add((lo2 - 1, -1));
                if (StripEdgeHoleOpen(roadMasks, merged, line2, hi2, +1, horizontal))
                    holes.Add((hi2, +1));
            }
            holes.Sort((a, b) => a.start2.CompareTo(b.start2));
            // 2 dải lệch nhau đúng nửa ô đòi CÙNG một khe → chỉ lấp 1 lần.
            for (int k = holes.Count - 1; k > 0; k--)
                if (holes[k].start2 == holes[k - 1].start2) holes.RemoveAt(k);

            float line = line2 * 0.5f;
            for (int k = 0; k < holes.Count; k++)
            {
                if (k + 1 < holes.Count && holes[k + 1].start2 == holes[k].start2 + 1)
                {
                    float c = (holes[k].start2 + 1) * 0.5f;
                    fulls.Add(horizontal ? (c, line, 0f, side) : (line, c, 90f, side));
                    k++;
                    continue;
                }

                float pos = (holes[k].start2 + 0.5f) * 0.5f;
                float yaw = horizontal
                    ? (holes[k].outward < 0 ? 180f : 0f)
                    : (holes[k].outward < 0 ? 90f : 270f);
                halves.Add(horizontal ? (pos, line, yaw, side) : (line, pos, yaw, side));
            }
        }

        /// <summary>Mép dải khối tại <paramref name="edge2"/> có hở nửa ô về phía
        /// <paramref name="outward"/> không.</summary>
        private bool StripEdgeHoleOpen(
            int[] roadMasks, List<(int lo2, int hi2)> merged, int line2, int edge2, int outward,
            bool horizontal)
        {
            int lw = _doc.LatticeW;
            int lateralMax2 = (horizontal ? _doc.GridWidth - 1 : _doc.GridHeight - 1) * 2;
            if (edge2 < 0 || edge2 > lateralMax2) return false;
            int edgeMask = roadMasks[horizontal ? line2 * lw + edge2 : edge2 * lw + line2];
            if (edgeMask == 0) return false;

            int outDir = horizontal
                ? (outward < 0 ? DirBits.W : DirBits.E)
                : (outward < 0 ? DirBits.S : DirBits.N);
            if (DirBits.CountBits(edgeMask) == 1)
            {
                int ex2 = horizontal ? edge2 : line2, ey2 = horizontal ? line2 : edge2;
                int halfDir = NeighborIsSignificant(roadMasks, edgeMask, ex2, ey2)
                    ? DirBits.OppositeDir(edgeMask)
                    : edgeMask;
                if (halfDir != outDir) return false;
            }

            int n2 = edge2 + outward * 2;
            if (n2 < 0 || n2 > lateralMax2) return true;
            foreach ((int lo2, int hi2) in merged)
                if (n2 >= lo2 && n2 <= hi2) return true;

            int nMask = roadMasks[horizontal ? line2 * lw + n2 : n2 * lw + line2];
            return nMask == 0 || DirBits.IsStraightLikeMask(nMask);
        }

        /// <summary>Node láng giềng theo hướng <paramref name="dir"/> có phải node "nổi bật" (junction hoặc
        /// dead-end có kẻ) không — dùng quyết hướng nửa mảnh dead-end.</summary>
        private bool NeighborIsSignificant(int[] masks, int dir, int x2, int y2)
        {
            int lw = _doc.LatticeW, lh = _doc.LatticeH;
            (int dx, int dy) = DirBits.DirStep(dir);
            int nx2 = x2 + dx, ny2 = y2 + dy;
            if (nx2 < 0 || nx2 >= lw || ny2 < 0 || ny2 >= lh) return false;
            int m = masks[ny2 * lw + nx2];
            return m != 0 && (DirBits.IsJunctionMask(m) || DirBits.CountBits(m) == 1);
        }
    }
}
#endif
