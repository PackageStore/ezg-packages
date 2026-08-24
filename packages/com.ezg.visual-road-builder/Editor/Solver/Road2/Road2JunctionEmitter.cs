#if UNITY_EDITOR
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Enumerates modular tiles for one Road 2 junction piece — THE single source for
    /// bake and preview. D6: separate from type-1 <see cref="JunctionTileEmitter"/>.</summary>
    internal static class Road2JunctionEmitter
    {
        /// <summary>Nhánh d1 của mảnh CUA = nhánh mà nhánh CW kế tiếp cũng mở.</summary>
        internal static int Road2ArcD1(int mask)
        {
            if ((mask & DirBits.E) != 0 && (mask & DirBits.S) != 0) return DirBits.E;
            if ((mask & DirBits.S) != 0 && (mask & DirBits.W) != 0) return DirBits.S;
            if ((mask & DirBits.W) != 0 && (mask & DirBits.N) != 0) return DirBits.W;
            return DirBits.N;
        }

        /// <summary>Số nấc xoay CW của frame corner có d1 = <paramref name="dir"/>.</summary>
        internal static int Road2DirTurns(int dir) =>
            dir == DirBits.E ? 0 : dir == DirBits.S ? 1 : dir == DirBits.W ? 2 : 3;

        /// <summary>Road2 mirror của <see cref="JunctionTileEmitter.ForEachJunctionTile"/>.</summary>
        internal static void ForEachRoad2JunctionTile(
            int mask, int junctionArms, System.Action<Road2TilePart, float, float, float> place,
            System.Func<float, float, float, bool> rimCovered = null,
            System.Func<float, float, float, (float x, float y, float yaw)?> filletTurn = null,
            BlockRoadSkin skin = null, float skinX = 0f, float skinY = 0f, int sides = DirBits.All)
        {
            int lost = DirBits.All & ~sides;

            void OpenArm(int dir)
            {
                if ((mask & dir) == 0 || (junctionArms & dir) != 0) return;
                (int ux, int uy) = DirBits.DirStep(dir);
                float axisYaw = MaskClassifier.StraightYaw(
                    (dir & (DirBits.E | DirBits.W)) != 0 ? DirBits.E | DirBits.W : DirBits.N | DirBits.S);
                Road2StraightEmitter.ForEachRoad2StraightPart(
                    ux * Road2Constants.Road2ArmOffset, uy * Road2Constants.Road2ArmOffset, axisYaw,
                    false, sides, skin, place, rimCovered, skinX, skinY);
                if ((junctionArms & (dir << Road2Constants.Road2OuterArmShift)) != 0) return;
                Road2StraightEmitter.ForEachRoad2StraightPart(
                    ux * Road2Constants.Road2HalfOffset, uy * Road2Constants.Road2HalfOffset, axisYaw,
                    false, sides, skin, place, rimCovered, skinX, skinY);
            }

            void Corner(int d1, int d2, float yaw)
            {
                if ((mask & d1) == 0 || (mask & d2) == 0) return;
                (float px, float py) = DirBits.RotateCellsCW(
                    Road2Constants.Road2CornerPivotX, Road2Constants.Road2CornerPivotY,
                    Mathf.RoundToInt(yaw / 90f));
                if (filletTurn != null && filletTurn(px, py, yaw) is { } small)
                {
                    place(Road2TilePart.Turn1x1, small.x, small.y, small.yaw);
                    place(Road2TilePart.Turn1x1Rim, small.x, small.y, small.yaw);
                    return;
                }
                place(Road2TilePart.Curve, px, py, yaw);
                place(Road2TilePart.CurveRim, px, py, yaw);
            }

            if (DirBits.IsArcCoreMask(mask))
            {
                int turns = Road2DirTurns(Road2ArcD1(mask));
                float cornerYaw = turns * 90f;

                (float px, float py) = DirBits.RotateCellsCW(
                    Road2Constants.Road2CornerPivotX, Road2Constants.Road2CornerPivotY, turns);
                float turnYaw = (cornerYaw + 90f) % 360f;
                place(Road2TilePart.Turn3x3, px, py, turnYaw);
                place(Road2TilePart.Turn3x3Rim, px, py, turnYaw);
                if (filletTurn != null && filletTurn(px, py, cornerYaw) is { } arcSmall)
                {
                    place(Road2TilePart.Turn1x1, arcSmall.x, arcSmall.y, arcSmall.yaw);
                    place(Road2TilePart.Turn1x1Rim, arcSmall.x, arcSmall.y, arcSmall.yaw);
                }
                else
                {
                    place(Road2TilePart.Curve, px, py, cornerYaw);
                    place(Road2TilePart.CurveRim, px, py, cornerYaw);
                }

                OpenArm(DirBits.E);
                OpenArm(DirBits.W);
                OpenArm(DirBits.N);
                OpenArm(DirBits.S);
                return;
            }

            int stub = 0;
            foreach (int dir in new[] { DirBits.E, DirBits.N, DirBits.W, DirBits.S })
                if ((mask & dir) != 0 && (mask & DirBits.OppositeDir(dir)) == 0) stub = dir;
            (int sx, int sy) = DirBits.DirStep(stub);

            bool SlotLost(int i, int j) =>
                (i == 1 && (lost & DirBits.E) != 0) || (i == -1 && (lost & DirBits.W) != 0)
                || (j == 1 && (lost & DirBits.N) != 0) || (j == -1 && (lost & DirBits.S) != 0);

            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                    if (i * sx + j * sy != 1 && !SlotLost(i, j))
                        place(Road2TilePart.Center,
                            i * Road2Constants.Road2BlockSlotPitch,
                            j * Road2Constants.Road2BlockSlotPitch, 0f);

            if (stub != 0)
                Road2StraightEmitter.ForEachRoad2StraightPart(
                    sx * Road2Constants.Road2BlockSlotPitch,
                    sy * Road2Constants.Road2BlockSlotPitch,
                    MaskClassifier.StraightYaw(
                        (stub & (DirBits.E | DirBits.W)) != 0 ? DirBits.E | DirBits.W : DirBits.N | DirBits.S),
                    false, DirBits.All, null, place, (px, py, pyaw) => true);

            OpenArm(DirBits.E);
            OpenArm(DirBits.W);
            OpenArm(DirBits.N);
            OpenArm(DirBits.S);

            Corner(DirBits.E, DirBits.S, 0f);
            Corner(DirBits.S, DirBits.W, 90f);
            Corner(DirBits.W, DirBits.N, 180f);
            Corner(DirBits.N, DirBits.E, 270f);

            void EdgeRim(int dir, float yaw)
            {
                if ((mask & dir) != 0) return;
                int turns = Mathf.RoundToInt(yaw / 90f) & 3;
                int plus = DirBits.RimRunPlusDir(dir), minus = DirBits.OppositeDir(plus);
                for (int k = -1; k <= 1; k++)
                {
                    float t = k * Road2Constants.Road2BlockSlotPitch;
                    if (k != 0 && (junctionArms & (t > 0f ? plus : minus)) != 0) continue;
                    (float rx, float ry) = DirBits.RotateCellsCW(
                        t, -Road2Constants.Road2RimLateralOffset, turns);
                    if (rimCovered != null && rimCovered(rx, ry, yaw)) continue;
                    if (skin != null && skin.KerbFreeAt(rx + skinX, ry + skinY, yaw)) continue;
                    place(Road2TilePart.SideRim, rx, ry, yaw);
                }
            }

            EdgeRim(DirBits.S, 0f);
            EdgeRim(DirBits.W, 90f);
            EdgeRim(DirBits.N, 180f);
            EdgeRim(DirBits.E, 270f);
        }
    }
}
#endif
