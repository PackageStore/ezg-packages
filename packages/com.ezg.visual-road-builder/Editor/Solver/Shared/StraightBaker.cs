#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Resolves part→prefab and emits Placements for one straight piece (type-1
    /// consumer of <see cref="StraightTileEmitter"/>).</summary>
    internal sealed class StraightBaker
    {
        private readonly ToolContext _ctx;
        internal StraightBaker(ToolContext ctx) => _ctx = ctx;

        /// <summary>Đủ prefab để ghép mảnh thẳng chưa (thiếu 1 trong 2 là không đặt gì).</summary>
        internal bool StraightTilesReady =>
            _ctx.Library != null && _ctx.Library.road1x1_side != null && _ctx.Library.road1x1_side_rim != null;

        /// <summary>Đặt một mảnh THẲNG (0.5×1 ô, hoặc 1×1 ô khi <paramref name="fullCell"/>) bằng ô
        /// modular core + rim. <paramref name="skin"/> = ảnh hưởng của khối lên mặt đường (xem
        /// <see cref="BlockRoadSkin"/>): cột apron trơn đổi core sang ô center, dải thân khối bỏ rim.</summary>
        internal void AddStraightTiles(
            List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)> placements,
            float x, float y, float yaw, bool fullCell, HashSet<string> missing, int sides = DirBits.All,
            BlockRoadSkin skin = null, bool noRim = false)
        {
            var lib = _ctx.Library;
            if (!StraightTilesReady)
            {
                if (lib == null || lib.road1x1_side == null) missing?.Add("Road Tile Side");
                if (lib == null || lib.road1x1_side_rim == null) missing?.Add("Road Tile Side Rim");
                return;
            }

            StraightTileEmitter.ForEachStraightTile(x, y, yaw, fullCell, (tx, ty, tyaw) =>
            {
                if (skin != null && skin.PlainAt(tx, ty, tyaw))
                {
                    if (lib.road1x1_center == null) missing?.Add("Road Tile Center");
                    else
                    {
                        (float cx, float cy) = StraightTileEmitter.PlainCoreCell(tx, ty, tyaw);
                        placements.Add((cx, cy, lib.road1x1_center, 0f, Vector3.one));
                    }
                }
                else placements.Add((tx, ty, lib.road1x1_side, tyaw, Vector3.one));

                if (!noRim && (skin == null || !skin.KerbFreeAt(tx, ty, tyaw)))
                    placements.Add((tx, ty, lib.road1x1_side_rim, tyaw, Vector3.one));
            }, sides);
        }
    }
}
#endif
