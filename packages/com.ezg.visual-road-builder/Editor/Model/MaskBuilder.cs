#if UNITY_EDITOR
using System.Collections.Generic;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Build 4-direction mask arrays from edge lists (half-cell and legacy span).</summary>
    internal static class MaskBuilder
    {
        /// <summary>Mask 4 hướng nối tại từng điểm lattice NỬA Ô, index = y2 * latticeW + x2.
        /// Edge dài nửa ô nên hàng xóm cách nhau 1 nấc lattice.</summary>
        internal static int[] BuildMasks(List<int> edges, int latticeW, int latticeH)
        {
            var masks = new int[latticeW * latticeH];
            foreach (int id in edges)
            {
                EdgeCodec.DecodeEdge(id, out int x2, out int y2, out int orient);
                if (orient == 0)
                {
                    if (x2 + 1 >= latticeW || y2 >= latticeH) continue;
                    masks[y2 * latticeW + x2] |= DirBits.E;
                    masks[y2 * latticeW + x2 + 1] |= DirBits.W;
                }
                else
                {
                    if (x2 >= latticeW || y2 + 1 >= latticeH) continue;
                    masks[y2 * latticeW + x2] |= DirBits.N;
                    masks[(y2 + 1) * latticeW + x2] |= DirBits.S;
                }
            }
            return masks;
        }

        /// <summary>Mask từ edge "view cũ" (span 2 nấc, <see cref="EdgeCodec.PairHalfEdges"/>) — chỉ set 2 ĐẦU, node
        /// giữa (do split sinh ra) không nhận mesh, tái tạo đúng topology TRƯỚC migrate cho các consumer
        /// legacy quét mask thô.</summary>
        internal static int[] BuildLegacyMasks(List<int> anchors, int latticeW, int latticeH)
        {
            var masks = new int[latticeW * latticeH];
            foreach (int id in anchors)
            {
                EdgeCodec.DecodeEdge(id, out int x2, out int y2, out int orient);
                if (orient == 0)
                {
                    if (x2 + 2 >= latticeW || y2 >= latticeH) continue;
                    masks[y2 * latticeW + x2] |= DirBits.E;
                    masks[y2 * latticeW + x2 + 2] |= DirBits.W;
                }
                else
                {
                    if (x2 >= latticeW || y2 + 2 >= latticeH) continue;
                    masks[y2 * latticeW + x2] |= DirBits.N;
                    masks[(y2 + 2) * latticeW + x2] |= DirBits.S;
                }
            }
            return masks;
        }

        internal static int[] BuildLegacyMasksFromEdges(List<int> edges, int latticeW, int latticeH)
            => BuildLegacyMasks(EdgeCodec.PairHalfEdges(edges), latticeW, latticeH);
    }
}
#endif
