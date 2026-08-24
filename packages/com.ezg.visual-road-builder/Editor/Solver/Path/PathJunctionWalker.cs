#if UNITY_EDITOR
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Junction tile emitter for PATH layer — enumerates turn/center/curve/arm-stub tiles
    /// at each junction node (including dead-ends).</summary>
    internal static class PathJunctionWalker
    {
        /// <summary>Duyệt các ô modular tại node giao PATH (kể cả ĐẦU CỤT 1 nhánh) — place nhận offset
        /// so với tâm node.
        /// <paramref name="cornerBlocked"/> (P14): fillet chiếm quarter ô chéo bên trong slot hàng xóm
        /// chéo — nếu hàng xóm có mask thì skip fillet tránh z-fight.</summary>
        internal static void ForEachPathJunctionTile(
            int mask, System.Action<PathTilePart, float, float, float> place,
            System.Func<int, bool> cornerBlocked = null)
        {
            // Lõi: nắp 2 Turn (đầu cụt) / Turn (arc-core) / Center (T, cross).
            if (DirBits.CountBits(mask) == 1)
            {
                // Đầu cụt: bịt bằng 2 ô cung úp lưng
                bool armAlongX = (mask & (DirBits.E | DirBits.W)) != 0;
                place(PathTilePart.Turn, 0f, 0f, ArcCoreTurnYaw(mask | (armAlongX ? DirBits.N : DirBits.E)));
                place(PathTilePart.Turn, 0f, 0f, ArcCoreTurnYaw(mask | (armAlongX ? DirBits.S : DirBits.W)));
            }
            else if (DirBits.IsArcCoreMask(mask))
            {
                place(PathTilePart.Turn, 0f, 0f, ArcCoreTurnYaw(mask));
            }
            else
            {
                place(PathTilePart.Center, 0f, 0f, 0f);
            }

            // Curve ở mỗi góc có CẢ HAI nhánh kề mở.
            void Corner(int d1, int d2, float yaw)
            {
                if ((mask & d1) == 0 || (mask & d2) == 0) return;
                if (cornerBlocked != null && cornerBlocked(d1 | d2)) return;
                (float px, float py) = DirBits.RotateCellsCW(
                    PathTileVocabulary.PathCornerPivotX,
                    PathTileVocabulary.PathCornerPivotY,
                    Mathf.RoundToInt(yaw / 90f));
                place(PathTilePart.Curve, px, py, yaw);
            }

            Corner(DirBits.E, DirBits.S, 0f);
            Corner(DirBits.S, DirBits.W, 90f);
            Corner(DirBits.W, DirBits.N, 180f);
            Corner(DirBits.N, DirBits.E, 270f);

            // Cuống nhánh: lõi (Turn/Center) chỉ phủ 0.5×0.5 quanh tâm, còn cột thẳng đầu tiên của
            // nhánh nằm ở 0.375 — hở đúng 1 ô 0.25 tại 0.125.
            void ArmStub(int arm, float ax, float ay)
            {
                if ((mask & arm) == 0) return;
                bool armAlongX = (arm & (DirBits.E | DirBits.W)) != 0;
                int p1 = armAlongX ? DirBits.N : DirBits.E, p2 = armAlongX ? DirBits.S : DirBits.W;
                if ((mask & p1) == 0) place(PathTilePart.Side, ax, ay, DirBits.RimYawFacing(p1));
                if ((mask & p2) == 0) place(PathTilePart.Side, ax, ay, DirBits.RimYawFacing(p2));
            }

            ArmStub(DirBits.E,  PathTileVocabulary.PathTileColumnOffsetCells, 0f);
            ArmStub(DirBits.W, -PathTileVocabulary.PathTileColumnOffsetCells, 0f);
            ArmStub(DirBits.N, 0f,  PathTileVocabulary.PathTileColumnOffsetCells);
            ArmStub(DirBits.S, 0f, -PathTileVocabulary.PathTileColumnOffsetCells);
        }

        /// <summary>Yaw của Turn cho arc-core mask (2 nhánh vuông góc → 1 góc mở).</summary>
        internal static float ArcCoreTurnYaw(int mask)
        {
            if ((mask & DirBits.E) != 0 && (mask & DirBits.S) != 0) return 0f;
            if ((mask & DirBits.S) != 0 && (mask & DirBits.W) != 0) return 90f;
            if ((mask & DirBits.W) != 0 && (mask & DirBits.N) != 0) return 180f;
            return 270f;
        }
    }
}
#endif
