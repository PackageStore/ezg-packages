#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Orchestrates CollectRoadPlacements: resolve layout → iterate mask → emit
    /// straight/junction/half-straight placements → dedupe. Returns both the placement list
    /// AND the isolated-key set (R1: no longer window state).</summary>
    internal sealed class RoadCollector
    {
        private readonly ToolContext _ctx;
        private readonly RoadLayoutResolver _layoutResolver;
        private readonly RoadStraightAnchor _anchor;
        private readonly StraightBaker _straightBaker;
        private readonly JunctionBaker _junctionBaker;
        private readonly HalfStraightEmitter _halfEmitter;

        internal RoadCollector(ToolContext ctx)
        {
            _ctx = ctx;
            _layoutResolver = new RoadLayoutResolver(ctx);
            _anchor = new RoadStraightAnchor(ctx);
            _straightBaker = new StraightBaker(ctx);
            _junctionBaker = new JunctionBaker(ctx);
            _halfEmitter = new HalfStraightEmitter(ctx);
        }

        /// <summary>Returns road placements + the isolated-straight-key set for deferred dedupe.</summary>
        internal (List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)> placements,
                  HashSet<(GameObject, int, int, int)> isolatedKeys)
            Collect(List<int> edges, int[] hwMasks, BlockSuppression suppressed,
                    HashSet<int> rampSuppressed,
                    List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)> placements,
                    HashSet<string> missing, BlockRoadSkin skin = null)
        {
            int lw = _ctx.Doc.LatticeW, lh = _ctx.Doc.LatticeH;
            int start = placements.Count;

            bool Blocked(int idx) => hwMasks[idx] != 0 || suppressed.Blocked(idx) || rampSuppressed.Contains(idx);

            RoadLayout layout = _layoutResolver.ResolveRoadLayout(edges, Blocked);
            int[] masks = layout.Masks;
            HashSet<int> replacedByHalf = layout.ReplacedByHalf;

            if (layout.DroppedBetween.Count > 0)
            {
                string At(HashSet<int> set)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (int idx in set) sb.Append($"({idx % lw * 0.5f}, {idx / lw * 0.5f}) ");
                    return sb.ToString();
                }

                Debug.Log($"[VisualRoadBuilder] {layout.DroppedBetween.Count} straight kẹp giữa 2" +
                          $" junction bị BỎ HẲN tại: {At(layout.DroppedBetween)}");
            }

            bool NeighborPlainKept(int nx2, int ny2)
            {
                if (nx2 < 0 || nx2 >= lw || ny2 < 0 || ny2 >= lh) return false;
                int ni = ny2 * lw + nx2;
                return masks[ni] != 0 && !Blocked(ni) && !layout.Skip(ni);
            }

            var isolatedKeys = new HashSet<(GameObject, int, int, int)>();

            for (int y2 = 0; y2 < lh; y2++)
            {
                for (int x2 = 0; x2 < lw; x2++)
                {
                    int i = y2 * lw + x2;
                    int mask = masks[i];
                    if (mask == 0 || Blocked(i) || layout.Skip(i)) continue;

                    if (DirBits.IsStraightLikeMask(mask))
                    {
                        (float ax, float ay, bool full) = _anchor.StraightAnchorFor(layout, i, mask, x2, y2);

                        bool isolated = full && DirBits.CountBits(mask) == 2 && (mask == (DirBits.E | DirBits.W)
                            ? !NeighborPlainKept(x2 - 2, y2) && !NeighborPlainKept(x2 + 2, y2)
                            : !NeighborPlainKept(x2, y2 - 2) && !NeighborPlainKept(x2, y2 + 2));

                        int before = placements.Count;
                        _straightBaker.AddStraightTiles(placements, ax, ay,
                            MaskClassifier.StraightYaw(mask), full, missing,
                            MaskClassifier.StraightSides(mask) & ~suppressed.Sides(i), skin,
                            _anchor.StraightTailNoRim(layout, i, mask));
                        if (isolated)
                            for (int k = before; k < placements.Count; k++)
                                isolatedKeys.Add(DedupePlacement.Key(placements[k]));
                        continue;
                    }

                    if (_junctionBaker.AddJunctionTiles(placements, x2 * 0.5f, y2 * 0.5f, mask,
                            _junctionBaker.JunctionArms(masks, x2, y2, mask), missing, layout, skin))
                        _halfEmitter.AddHalfStraights(mask, x2, y2, masks, replacedByHalf, suppressed,
                            placements, missing, _straightBaker, skin);
                }
            }

            DedupePlacement.Dedupe(placements, start);
            return (placements, isolatedKeys);
        }
    }
}
#endif
