#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Enumerates modular tiles for one Road 2 straight piece (side + rim + filler columns) —
    /// THE single source for bake and preview.</summary>
    internal static class Road2StraightEmitter
    {
        /// <summary>Duyệt các ô modular dựng nên một mảnh THẲNG Road 2 — nguồn DUY NHẤT của
        /// (part, vị trí, yaw) cho cả bake lẫn preview.</summary>
        internal static void ForEachRoad2StraightPart(
            float x, float y, float yaw, bool fullCell, int sides, BlockRoadSkin skin,
            System.Action<Road2TilePart, float, float, float> place,
            System.Func<float, float, float, bool> rimCovered = null,
            float skinX = 0f, float skinY = 0f)
        {
            var fillerColumns = new List<(float x, float y)>();
            StraightTileEmitter.ForEachStraightTile(x, y, yaw, fullCell, (tx, ty, tyaw) =>
            {
                if (skin != null && skin.PlainAt(tx + skinX, ty + skinY, tyaw))
                {
                    (float cx, float cy) = StraightTileEmitter.PlainCoreCell(tx, ty, tyaw);
                    place(Road2TilePart.Center, cx, cy, 0f);
                }
                else place(Road2TilePart.Side, tx, ty, tyaw);

                float kerbShift = skin?.KerbEdgeShift(tx + skinX, ty + skinY, tyaw) ?? 0f;
                bool kerbAlongX = (Mathf.RoundToInt(tyaw / 90f) & 1) == 0;
                float kx = kerbAlongX ? kerbShift : 0f, ky = kerbAlongX ? 0f : kerbShift;
                if (skin == null || !skin.KerbFreeAt(tx + kx + skinX, ty + ky + skinY, tyaw))
                {
                    (float rx, float ry) = DirBits.RotateCellsCW(0f,
                        -Road2Constants.Road2RimLateralOffset, Mathf.RoundToInt(tyaw / 90f));
                    if (rimCovered == null || !rimCovered(tx + rx + kx, ty + ry + ky, tyaw))
                        place(Road2TilePart.SideRim, tx + rx + kx, ty + ry + ky, tyaw);
                }

                if (fillerColumns.Count == 0 || fillerColumns[fillerColumns.Count - 1] != (tx, ty))
                    fillerColumns.Add((tx, ty));
            }, sides);

            int turns = Mathf.RoundToInt(yaw / 90f);
            (float fx1, float fy1) = DirBits.RotateCellsCW(0f, Road2Constants.Road2FillerLateralOffset, turns);
            (float fx2, float fy2) = DirBits.RotateCellsCW(0f, -Road2Constants.Road2FillerLateralOffset, turns);
            foreach ((float cx, float cy) in fillerColumns)
            {
                place(Road2TilePart.Filler, cx + fx1, cy + fy1, yaw);
                place(Road2TilePart.Filler, cx + fx2, cy + fy2, yaw);
            }
        }
    }
}
#endif
