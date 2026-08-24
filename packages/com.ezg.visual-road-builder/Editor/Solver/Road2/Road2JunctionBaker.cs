#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Road2JunctionArms + AddRoad2JunctionTiles — D6: separate from type-1
    /// <see cref="JunctionBaker"/>.</summary>
    internal sealed class Road2JunctionBaker
    {
        private readonly ToolContext _ctx;
        internal Road2JunctionBaker(ToolContext ctx) => _ctx = ctx;

        /// <summary>Road2 mirror của <see cref="JunctionBaker.JunctionArms"/>: nibble THẤP = nhường HẲN
        /// nhánh, nibble CAO = nhường NỬA (chỉ bỏ cột ngoài).</summary>
        internal int Road2JunctionArms(int[] road2Masks, int x2, int y2, int mask)
        {
            int lw = _ctx.Doc.LatticeW, lh = _ctx.Doc.LatticeH;
            int arms = 0;

            void Probe(int dir, int jx2, int jy2, int shift)
            {
                if ((mask & dir) == 0) return;
                if (jx2 < 0 || jx2 >= lw || jy2 < 0 || jy2 >= lh) return;
                if (DirBits.IsJunctionMask(road2Masks[jy2 * lw + jx2])) arms |= dir << shift;
            }

            void ProbeRing(int d, int shift)
            {
                Probe(DirBits.E, x2 + d, y2, shift);
                Probe(DirBits.W, x2 - d, y2, shift);
                Probe(DirBits.N, x2, y2 + d, shift);
                Probe(DirBits.S, x2, y2 - d, shift);
            }

            for (int d = 1; d <= Road2Constants.Road2ArmShareFullSteps; d++) ProbeRing(d, 0);
            ProbeRing(Road2Constants.Road2ArmShareOuterSteps, Road2Constants.Road2OuterArmShift);
            return arms;
        }

        /// <summary>Đặt một mảnh GIAO Road2 bằng ô modular — warn-and-continue THEO TỪNG PART.</summary>
        internal void AddRoad2JunctionTiles(
            List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)> placements,
            float x, float y, int mask, int junctionArms, HashSet<string> missing,
            System.Func<float, float, float, bool> rimCovered = null,
            System.Func<float, float, float, (float x, float y, float yaw)?> filletTurn = null,
            BlockRoadSkin skin = null, int sides = DirBits.All)
        {
            var lib = _ctx.Library;
            if (lib == null || (lib.road2_curve == null && lib.road1x1_curve == null))
                missing?.Add("Road2 Curve");
            if (lib == null || (lib.road2_curve_rim == null && lib.road1x1_curve_rim == null))
                missing?.Add("Road2 Curve Rim");
            if (DirBits.IsArcCoreMask(mask))
            {
                if (lib == null || lib.road3x3_turn == null) missing?.Add("Road2 Turn");
                if (lib == null || lib.road3x3_turn_rim == null) missing?.Add("Road2 Turn Rim");
            }
            if (lib == null || lib.road1x1_turn == null || lib.road1x1_turn_rim == null)
            {
                if (lib == null || lib.road1x1_turn == null) missing?.Add("Road2 Turn 1x1");
                if (lib == null || lib.road1x1_turn_rim == null) missing?.Add("Road2 Turn 1x1 Rim");
            }

            System.Func<float, float, float, bool> rimCoveredLocal = rimCovered == null ? null
                : (rx, ry, ryaw) => rimCovered(x + rx, y + ry, ryaw);
            System.Func<float, float, float, (float x, float y, float yaw)?> filletTurnLocal =
                filletTurn == null ? null
                : (rx, ry, ryaw) =>
                {
                    (float x, float y, float yaw)? turn = filletTurn(x + rx, y + ry, ryaw);
                    return turn == null ? null : (turn.Value.x - x, turn.Value.y - y, turn.Value.yaw);
                };
            Road2JunctionEmitter.ForEachRoad2JunctionTile(mask, junctionArms, (part, dx, dy, yaw) =>
            {
                GameObject prefab = Road2TileParts.PrefabFor(part, _ctx.Library);
                if (prefab == null) return;
                placements.Add((x + dx, y + dy, prefab, yaw, Vector3.one));
            }, rimCoveredLocal, filletTurnLocal, skin, x, y, sides);
        }
    }
}
#endif
