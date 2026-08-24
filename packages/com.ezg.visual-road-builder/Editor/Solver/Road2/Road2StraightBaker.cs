#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Resolves part→prefab and emits Placements for one Road 2 straight piece.</summary>
    internal sealed class Road2StraightBaker
    {
        private readonly ToolContext _ctx;
        private readonly StraightBaker _straightBaker;
        internal Road2StraightBaker(ToolContext ctx)
        {
            _ctx = ctx;
            _straightBaker = new StraightBaker(ctx);
        }

        /// <summary>Bake một mảnh THẲNG Road 2 — consumer placement của
        /// <see cref="Road2StraightEmitter.ForEachRoad2StraightPart"/>.</summary>
        internal void AddRoad2StraightTiles(
            List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)> placements,
            float x, float y, float yaw, bool fullCell, HashSet<string> missing, int sides = DirBits.All,
            BlockRoadSkin skin = null, System.Func<float, float, float, bool> rimCovered = null)
        {
            if (!_straightBaker.StraightTilesReady)
            {
                var lib = _ctx.Library;
                if (lib == null || lib.road1x1_side == null) missing?.Add("Road Tile Side");
                if (lib == null || lib.road1x1_side_rim == null) missing?.Add("Road Tile Side Rim");
                return;
            }

            Road2StraightEmitter.ForEachRoad2StraightPart(x, y, yaw, fullCell, sides, skin,
                (part, px, py, pyaw) =>
                {
                    GameObject prefab = Road2TileParts.PrefabFor(part, _ctx.Library);
                    if (prefab == null)
                    {
                        missing?.Add(part == Road2TilePart.Filler ? "Road2 Center Filler" : "Road Tile Center");
                        return;
                    }
                    placements.Add((px, py, prefab, pyaw, Vector3.one));
                }, rimCovered);
        }
    }
}
#endif
