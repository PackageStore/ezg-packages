#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Pre-collects junction fillet/sweep effects for Road 2 before the placement loop.</summary>
    internal sealed class Road2JunctionEffects
    {
        private readonly ToolContext _ctx;
        internal Road2JunctionEffects(ToolContext ctx) => _ctx = ctx;

        /// <summary>Hiệu ứng lan của các mảnh GIAO Road2 lên phần còn lại của lưới.</summary>
        internal void CollectRoad2JunctionEffects(
            RoadLayout layout, System.Func<int, bool> blocked, HashSet<long> filletKerb,
            Dictionary<long, (float x, float y, float yaw)> filletTurn = null)
        {
            var byCell = new Dictionary<long, List<(float x, float y, float yaw)>>();
            int lw = _ctx.Doc.LatticeW, lh = _ctx.Doc.LatticeH;
            int[] masks = layout.Masks;
            for (int y2 = 0; y2 < lh; y2++)
            {
                for (int x2 = 0; x2 < lw; x2++)
                {
                    int i = y2 * lw + x2;
                    int mask = masks[i];
                    if (mask == 0 || DirBits.IsStraightLikeMask(mask)) continue;
                    if (blocked != null && blocked(i)) continue;
                    if (layout.Skip(i)) continue;

                    float jx = x2 * 0.5f, jy = y2 * 0.5f;
                    void Fillet(int turns)
                    {
                        (float fx, float fy) = DirBits.RotateCellsCW(
                            Road2Constants.Road2CornerPivotX, Road2Constants.Road2CornerPivotY, turns);
                        float px = jx + fx, py = jy + fy, pyaw = turns * 90f;
                        filletKerb.Add(LatticeKeys.KerbCellKey(px, py, 0.25f, -0.25f, pyaw));
                        if (filletTurn == null) return;
                        long cell = LatticeKeys.KerbCellKey(px, py, 0.25f, -0.25f, pyaw);
                        if (!byCell.TryGetValue(cell, out List<(float x, float y, float yaw)> list))
                            byCell[cell] = list = new List<(float x, float y, float yaw)>();
                        list.Add((px, py, pyaw));
                    }

                    if (DirBits.IsArcCoreMask(mask))
                        Fillet(Road2JunctionEmitter.Road2DirTurns(Road2JunctionEmitter.Road2ArcD1(mask)));
                    else
                    {
                        if ((mask & DirBits.E) != 0 && (mask & DirBits.S) != 0) Fillet(0);
                        if ((mask & DirBits.S) != 0 && (mask & DirBits.W) != 0) Fillet(1);
                        if ((mask & DirBits.W) != 0 && (mask & DirBits.N) != 0) Fillet(2);
                        if ((mask & DirBits.N) != 0 && (mask & DirBits.E) != 0) Fillet(3);
                    }

                    SweepArm(layout, mask, DirBits.E, x2, y2);
                    SweepArm(layout, mask, DirBits.W, x2, y2);
                    SweepArm(layout, mask, DirBits.N, x2, y2);
                    SweepArm(layout, mask, DirBits.S, x2, y2);
                }
            }

            if (filletTurn == null) return;
            foreach (KeyValuePair<long, List<(float x, float y, float yaw)>> kv in byCell)
            {
                List<(float x, float y, float yaw)> curves = kv.Value;
                if (curves.Count != 2) continue;

                (float x, float y, float yaw) a = curves[0], b = curves[1];
                bool sameX = Mathf.Approximately(a.x, b.x), sameY = Mathf.Approximately(a.y, b.y);
                if (sameX == sameY) continue;

                float tx = (a.x + b.x) * 0.5f, ty = (a.y + b.y) * 0.5f;
                (float cx, float cy) = LatticeKeys.QuarterCellCenter(a);
                float tyaw = DirBits.RimYawFacing(
                    sameY ? (cy > ty ? DirBits.N : DirBits.S) : (cx > tx ? DirBits.E : DirBits.W));
                filletTurn[LatticeKeys.CurveKey(a.x, a.y, a.yaw)] = (tx, ty, tyaw);
                filletTurn[LatticeKeys.CurveKey(b.x, b.y, b.yaw)] = (tx, ty, tyaw);
            }
        }

        /// <summary>Quét một nhánh MỞ của mảnh giao ra tối đa 3 nấc lattice.</summary>
        internal void SweepArm(RoadLayout layout, int mask, int dir, int x2, int y2)
        {
            if ((mask & dir) == 0) return;
            int lw = _ctx.Doc.LatticeW, lh = _ctx.Doc.LatticeH;
            (int dx, int dy) = DirBits.DirStep(dir);
            for (int k = 1; k <= 3; k++)
            {
                int nx2 = x2 + dx * k, ny2 = y2 + dy * k;
                if (nx2 < 0 || nx2 >= lw || ny2 < 0 || ny2 >= lh) return;
                int ni = ny2 * lw + nx2;
                int nm = layout.Masks[ni];
                if (nm == 0 || !DirBits.IsStraightLikeMask(nm)) return;
                if (k < 3 || DirBits.CountBits(nm) == 1) { layout.ArcSwallowed.Add(ni); continue; }

                layout.IsolatedFullAnchor.Remove(ni);
                layout.TailHalfDir[ni] = dir;
            }
        }
    }
}
#endif
