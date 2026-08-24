#if UNITY_EDITOR
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Tile part of Road 2 junction modular tiles — mirror of <see cref="RoadTilePart"/>.</summary>
    internal enum Road2TilePart
    {
        Side, SideRim, Curve, CurveRim, Center, Filler, Turn3x3, Turn3x3Rim, Turn1x1, Turn1x1Rim,
    }

    /// <summary>Road2TilePart → prefab lookup.</summary>
    internal static class Road2TileParts
    {
        internal static GameObject PrefabFor(Road2TilePart part, RoadPartLibrary lib) =>
            lib == null ? null : part switch
            {
                Road2TilePart.Side => lib.road1x1_side,
                Road2TilePart.SideRim => lib.road1x1_side_rim,
                Road2TilePart.Curve => lib.road2_curve != null ? lib.road2_curve : lib.road1x1_curve,
                Road2TilePart.CurveRim => lib.road2_curve_rim != null ? lib.road2_curve_rim : lib.road1x1_curve_rim,
                Road2TilePart.Filler => lib.road2_center_filler,
                Road2TilePart.Turn3x3 => lib.road3x3_turn,
                Road2TilePart.Turn3x3Rim => lib.road3x3_turn_rim,
                Road2TilePart.Turn1x1 => lib.road1x1_turn,
                Road2TilePart.Turn1x1Rim => lib.road1x1_turn_rim,
                _ => lib.road1x1_center,
            };
    }
}
#endif
