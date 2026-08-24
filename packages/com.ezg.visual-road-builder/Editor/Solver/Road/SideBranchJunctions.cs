#if UNITY_EDITOR
namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Detects side-branch junctions (road perpendicular to existing road within reach)
    /// and patches masks.</summary>
    internal sealed class SideBranchJunctions
    {
        private readonly ToolContext _ctx;
        internal SideBranchJunctions(ToolContext ctx) => _ctx = ctx;

        /// <summary>Nhánh chạm sườn tại NODE THẬT: nhánh vuông góc kết thúc cách một node đường đúng 1 ô
        /// mà KHÔNG có edge nối ⇒ thêm hướng nhánh vào mask node đó (node thành mảnh giao).
        /// <paramref name="bridgeSingleAxis"/> (CHỈ Road 2) nới đòi hỏi đó: mảnh giao Road 2 rộng 1.5 ô
        /// trùm luôn nửa ô kề nên người vẽ chừa trống đúng half-cell đó.</summary>
        internal void AddSideBranchJunctions(
            RoadLayout layout, System.Func<int, bool> blocked, int reach, bool bridgeSingleAxis = false)
        {
            int lw = _ctx.Doc.LatticeW, lh = _ctx.Doc.LatticeH;
            int[] masks = layout.Masks;
            var added = new int[masks.Length];

            for (int y2 = 0; y2 < lh; y2++)
            {
                for (int x2 = 0; x2 < lw; x2++)
                {
                    int i = y2 * lw + x2;
                    int mask = masks[i];
                    if (mask == 0) continue;
                    if (blocked != null && blocked(i)) continue;

                    void Probe(int dir, int axis, int bx2, int by2)
                    {
                        if ((mask & dir) != 0) return;
                        int onAxis = mask & axis;
                        bool bridged = bridgeSingleAxis && onAxis != 0
                                       && (mask & DirBits.OppositeDir(dir)) == 0;
                        if (onAxis != axis && !bridged) return;
                        if (bx2 < 0 || bx2 >= lw || by2 < 0 || by2 >= lh) return;
                        int bi = by2 * lw + bx2;
                        if (blocked != null && blocked(bi)) return;
                        // Nhánh phải CHẠY theo hướng rời node, không phải con đường song song chạy ngang.
                        if ((masks[bi] & dir) != 0) added[i] |= dir | (bridged ? axis : 0);
                    }

                    for (int d = RoadLayoutResolver.SideBranchReachSteps; d <= reach; d++)
                    {
                        Probe(DirBits.N, DirBits.E | DirBits.W, x2, y2 + d);
                        Probe(DirBits.S, DirBits.E | DirBits.W, x2, y2 - d);
                        Probe(DirBits.E, DirBits.N | DirBits.S, x2 + d, y2);
                        Probe(DirBits.W, DirBits.N | DirBits.S, x2 - d, y2);
                    }

                    // GÓC CUA vẽ hụt nửa ô
                    void CornerGap(int dir, int axis, int nx2, int ny2)
                    {
                        if ((mask & axis) != 0) return;
                        if (nx2 < 0 || nx2 >= lw || ny2 < 0 || ny2 >= lh) return;
                        int ni = ny2 * lw + nx2;
                        if (blocked != null && blocked(ni)) return;
                        if ((masks[ni] & dir) != 0) added[i] |= dir;
                    }

                    CornerGap(DirBits.E, DirBits.E | DirBits.W, x2 + 1, y2);
                    CornerGap(DirBits.W, DirBits.E | DirBits.W, x2 - 1, y2);
                    CornerGap(DirBits.N, DirBits.N | DirBits.S, x2, y2 + 1);
                    CornerGap(DirBits.S, DirBits.N | DirBits.S, x2, y2 - 1);
                }
            }

            for (int i = 0; i < masks.Length; i++) masks[i] |= added[i];
        }
    }
}
#endif
