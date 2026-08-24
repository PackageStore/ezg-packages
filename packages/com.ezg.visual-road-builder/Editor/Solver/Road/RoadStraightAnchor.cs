#if UNITY_EDITOR
namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Computes anchor position + fullCell for a straight node, respecting tail-half,
    /// isolated-full, and neighbor-significant rules.</summary>
    internal sealed class RoadStraightAnchor
    {
        private readonly ToolContext _ctx;
        internal RoadStraightAnchor(ToolContext ctx) => _ctx = ctx;

        /// <summary>Anchor + fullCell cho mảnh thẳng tại node (x2,y2) — tôn trọng phần đuôi lẻ của D2
        /// run-coalescing: node <see cref="RoadLayout.TailHalfDir"/> LUÔN là ô NỬA hướng ra node nổi bật
        /// gần nhất (run độ dài lẻ, chỉ nội dung mới vẽ), bất kể mask thật (xuyên thẳng) nói gì.
        /// Đầu cụt (1 hướng) cách MỘT node nổi bật khác đúng 1 nấc (nửa ô) — mảnh giao ở đó đã chiếm
        /// trọn 1 ô, nên đầu cụt phải lấp về phía NGƯỢC hướng mở. Đầu cụt trong
        /// <see cref="RoadLayout.IsolatedFullAnchor"/> là ngoại lệ: hàng xóm KHÔNG phải junction nên
        /// tự nó bao ô đầy tại chính node.</summary>
        internal (float x, float y, bool fullCell) StraightAnchorFor(
            RoadLayout layout, int idx, int mask, int x2, int y2)
        {
            if (layout.IsolatedFullAnchor.Contains(idx)) return (x2 * 0.5f, y2 * 0.5f, true);
            if (layout.TailHalfDir.TryGetValue(idx, out int dir))
                return StraightTileEmitter.StraightAnchor(dir, x2, y2);
            if (DirBits.CountBits(mask) == 1 && NeighborIsSignificant(layout.Masks, mask, x2, y2))
                return StraightTileEmitter.StraightAnchor(DirBits.OppositeDir(mask), x2, y2);
            return StraightTileEmitter.StraightAnchor(mask, x2, y2);
        }

        /// <summary>Node NGAY KỀ (đúng 1 nấc, theo hướng <paramref name="dir"/>) có phải node nổi bật
        /// (mảnh giao hoặc đầu cụt khác) không.</summary>
        internal bool NeighborIsSignificant(int[] masks, int dir, int x2, int y2)
        {
            int lw = _ctx.Doc.LatticeW, lh = _ctx.Doc.LatticeH;
            int nx2 = x2 + (dir == DirBits.E ? 1 : dir == DirBits.W ? -1 : 0);
            int ny2 = y2 + (dir == DirBits.N ? 1 : dir == DirBits.S ? -1 : 0);
            if (nx2 < 0 || nx2 >= lw || ny2 < 0 || ny2 >= lh) return false;
            int m = masks[ny2 * lw + nx2];
            return m != 0 && (DirBits.IsJunctionMask(m) || DirBits.CountBits(m) == 1);
        }

        /// <summary>Mảnh thẳng tại node <paramref name="idx"/> có phải ô NỬA giáp SÁT một node nổi bật
        /// khác — mesh mảnh giao đó đã tự trải vỉa hè phần khe hở này, thêm rim ở đây SẼ chồng.
        /// Node <see cref="RoadLayout.IsolatedFullAnchor"/> loại trừ TRƯỚC: nó là ô ĐẦY tự đứng một
        /// mình, rim vẫn phải vẽ như một mảnh thẳng bình thường.</summary>
        internal bool StraightTailNoRim(RoadLayout layout, int idx, int mask) =>
            !layout.IsolatedFullAnchor.Contains(idx) &&
            (TailNeighborIsJunction(layout, idx)
            || (DirBits.CountBits(mask) == 1 && NeighborIsSignificant(layout.Masks, mask,
                    idx % _ctx.Doc.LatticeW, idx / _ctx.Doc.LatticeW)));

        private bool TailNeighborIsJunction(RoadLayout layout, int idx)
        {
            if (!layout.TailHalfDir.TryGetValue(idx, out int facingDir)) return false;
            int toward = DirBits.OppositeDir(facingDir);
            int lw = _ctx.Doc.LatticeW, lh = _ctx.Doc.LatticeH;
            int x2 = idx % lw + (toward == DirBits.E ? 1 : toward == DirBits.W ? -1 : 0);
            int y2 = idx / lw + (toward == DirBits.N ? 1 : toward == DirBits.S ? -1 : 0);
            if (x2 < 0 || x2 >= lw || y2 < 0 || y2 >= lh) return false;
            return DirBits.IsJunctionMask(layout.Masks[y2 * lw + x2]);
        }
    }
}
#endif
