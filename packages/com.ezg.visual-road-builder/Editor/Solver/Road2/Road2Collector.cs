#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Orchestrates CollectRoad2Placements: resolve layout → junction effects →
    /// iterate mask → straight/junction placements → dedupe.</summary>
    internal sealed class Road2Collector
    {
        private readonly ToolContext _ctx;
        private readonly RoadLayoutResolver _layoutResolver;
        private readonly RoadStraightAnchor _anchor;
        private readonly Road2StraightBaker _straightBaker;
        private readonly Road2JunctionBaker _junctionBaker;
        private readonly Road2JunctionEffects _junctionEffects;
        private readonly Road2ApronFiller _apronFiller;

        internal Road2Collector(ToolContext ctx)
        {
            _ctx = ctx;
            _layoutResolver = new RoadLayoutResolver(ctx);
            _anchor = new RoadStraightAnchor(ctx);
            _straightBaker = new Road2StraightBaker(ctx);
            _junctionBaker = new Road2JunctionBaker(ctx);
            _junctionEffects = new Road2JunctionEffects(ctx);
            _apronFiller = new Road2ApronFiller(ctx);
        }

        internal void Collect(
            List<int> road2Edges, int[] hwMasks, BlockSuppression suppressed2,
            HashSet<int> rampSuppressed2,
            List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)> placements,
            HashSet<string> missing, BlockRoadSkin skin2 = null)
        {
            int lw = _ctx.Doc.LatticeW, lh = _ctx.Doc.LatticeH;
            int start = placements.Count;

            bool Blocked(int idx) =>
                hwMasks[idx] != 0 || suppressed2.Blocked(idx) || rampSuppressed2.Contains(idx);
            RoadLayout layout = _layoutResolver.ResolveRoadLayout(
                road2Edges, Blocked, Road2Constants.Road2SideBranchReachSteps, true);
            int[] masks = layout.Masks;

            var filletKerb = new HashSet<long>();
            var filletTurns = new Dictionary<long, (float x, float y, float yaw)>();
            _junctionEffects.CollectRoad2JunctionEffects(layout, Blocked, filletKerb, filletTurns);
            System.Func<float, float, float, bool> rimCovered = filletKerb.Count == 0 ? null
                : (rx, ry, ryaw) => filletKerb.Contains(
                    LatticeKeys.KerbCellKey(rx, ry, 0f, -0.75f, ryaw));
            System.Func<float, float, float, (float x, float y, float yaw)?> filletTurn =
                filletTurns.Count == 0 ? null
                : (rx, ry, ryaw) => filletTurns.TryGetValue(LatticeKeys.CurveKey(rx, ry, ryaw),
                    out (float x, float y, float yaw) turn) ? turn : ((float, float, float)?)null;

            for (int y2 = 0; y2 < lh; y2++)
            {
                for (int x2 = 0; x2 < lw; x2++)
                {
                    int i = y2 * lw + x2;
                    int mask = masks[i];
                    if (mask == 0 || Blocked(i) || layout.Skip(i)) continue;

                    if (DirBits.IsStraightLikeMask(mask))
                    {
                        (float ax, float ay, bool full) =
                            _anchor.StraightAnchorFor(layout, i, mask, x2, y2);
                        _straightBaker.AddRoad2StraightTiles(placements, ax, ay,
                            MaskClassifier.StraightYaw(mask), full, missing,
                            MaskClassifier.StraightSides(mask) & ~suppressed2.Sides(i),
                            skin2, rimCovered);
                        continue;
                    }

                    int arms = _junctionBaker.Road2JunctionArms(masks, x2, y2, mask);
                    _junctionBaker.AddRoad2JunctionTiles(placements, x2 * 0.5f, y2 * 0.5f, mask,
                        arms, missing, rimCovered, filletTurn, skin2,
                        DirBits.All & ~suppressed2.Sides(i));
                }
            }

            DedupePlacement.Dedupe(placements, start);
        }
    }
}
#endif
