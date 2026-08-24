#if UNITY_EDITOR
namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Marks off-stride / tail-half / isolated-full-anchor nodes along straight runs
    /// for D2 run-coalescing.</summary>
    internal sealed class StraightRunMarker
    {
        private readonly ToolContext _ctx;
        internal StraightRunMarker(ToolContext ctx) => _ctx = ctx;

        /// <summary>Đánh dấu các node xuyên thẳng LẺ nấc lattice tính từ node nổi bật gần nhất vào
        /// <see cref="RoadLayout.OffStride"/> — dưới edge nửa ô (D1), BuildMasks giờ cấp mask xuyên thẳng
        /// ở MỌI nấc lattice, nên chỉ nấc CHẴN (đúng nhịp full-cell của bake cũ) mới được giữ.
        /// Run độ dài LẺ dư đúng 1 nấc ở cuối — nấc đó ghi vào <see cref="RoadLayout.TailHalfDir"/>
        /// để đặt 1 ô NỬA thay vì bỏ trống.</summary>
        internal void MarkStraightRuns(RoadLayout layout, System.Func<int, bool> blocked)
        {
            int lw = _ctx.Doc.LatticeW, lh = _ctx.Doc.LatticeH;
            int[] masks = layout.Masks;
            var walked = new bool[masks.Length];

            bool Significant(int idx) =>
                masks[idx] != 0 && (DirBits.IsJunctionMask(masks[idx]) || DirBits.CountBits(masks[idx]) == 1);
            bool Interior(int idx) =>
                masks[idx] != 0 && (blocked == null || !blocked(idx))
                && DirBits.IsStraightLikeMask(masks[idx]) && DirBits.CountBits(masks[idx]) == 2;

            void Walk(int startIdx, int dir, int dx, int dy)
            {
                int cx2 = startIdx % lw + dx, cy2 = startIdx / lw + dy;
                int steps = 0, lastIdx = -1;
                while (cx2 >= 0 && cx2 < lw && cy2 >= 0 && cy2 < lh)
                {
                    int ci = cy2 * lw + cx2;
                    if (Significant(ci))
                    {
                        if (steps == 1 && lastIdx >= 0)
                        {
                            layout.OffStride.Remove(lastIdx);
                            layout.IsolatedFullAnchor.Add(lastIdx);
                        }
                        else if (steps % 2 == 0 && lastIdx >= 0)
                            layout.TailHalfDir[lastIdx] = DirBits.OppositeDir(dir);
                        return;
                    }
                    if (!Interior(ci) || walked[ci]) return;
                    walked[ci] = true;
                    steps++;
                    if (steps % 2 == 1) layout.OffStride.Add(ci);
                    lastIdx = ci;
                    cx2 += dx; cy2 += dy;
                }
            }

            for (int y2 = 0; y2 < lh; y2++)
                for (int x2 = 0; x2 < lw; x2++)
                {
                    int i = y2 * lw + x2;
                    if (blocked != null && blocked(i)) continue;
                    if (!Significant(i)) continue;
                    int mask = masks[i];
                    if ((mask & DirBits.E) != 0) Walk(i, DirBits.E, 1, 0);
                    if ((mask & DirBits.W) != 0) Walk(i, DirBits.W, -1, 0);
                    if ((mask & DirBits.N) != 0) Walk(i, DirBits.N, 0, 1);
                    if ((mask & DirBits.S) != 0) Walk(i, DirBits.S, 0, -1);
                }
        }

        /// <summary>Run 1 nấc CÔ LẬP: 2 đầu cụt kề trực tiếp. Node LẺ (x2/y2 lẻ = tâm ô) bao ô đầy;
        /// node CHẴN (biên ô) bỏ vào OffStride.</summary>
        internal void MarkIsolatedSingleStepRuns(RoadLayout layout, System.Func<int, bool> blocked)
        {
            int lw = _ctx.Doc.LatticeW, lh = _ctx.Doc.LatticeH;
            int[] masks = layout.Masks;

            for (int y2 = 0; y2 < lh; y2++)
                for (int x2 = 0; x2 < lw; x2++)
                {
                    int i = y2 * lw + x2;
                    int mask = masks[i];
                    if (mask == 0 || DirBits.CountBits(mask) != 1) continue;
                    if (blocked != null && blocked(i)) continue;

                    int dx = mask == DirBits.E ? 1 : mask == DirBits.W ? -1 : 0;
                    int dy = mask == DirBits.N ? 1 : mask == DirBits.S ? -1 : 0;
                    int nx2 = x2 + dx, ny2 = y2 + dy;
                    if (nx2 < 0 || nx2 >= lw || ny2 < 0 || ny2 >= lh) continue;
                    int ni = ny2 * lw + nx2;
                    if (blocked != null && blocked(ni)) continue;
                    if (DirBits.CountBits(masks[ni]) != 1) continue;

                    bool alongX = mask == DirBits.E || mask == DirBits.W;
                    if (((alongX ? x2 : y2) & 1) != 0) layout.IsolatedFullAnchor.Add(i);
                    else layout.OffStride.Add(i);
                }
        }
    }
}
#endif
