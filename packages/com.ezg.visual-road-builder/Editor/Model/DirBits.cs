#if UNITY_EDITOR
namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Direction bitmask constants (E/N/W/S/All) and pure mask primitives.</summary>
    internal static class DirBits
    {
        // Bitmask 4 hướng trên mặt phẳng XZ: E = +X, N = +Z, W = -X, S = -Z.
        internal const int E = 1, N = 2, W = 4, S = 8;
        internal const int All = E | N | W | S;

        /// <summary>Tìm góc yaw (bội 90°) sao cho baseMask xoay tới trùng targetMask.</summary>
        internal static float SolveYaw(int baseMask, int targetMask)
        {
            int mask = baseMask;
            for (int k = 0; k < 4; k++)
            {
                if (mask == targetMask) return k * 90f;
                mask = RotateMask90(mask);
            }
            return 0f; // không xảy ra khi degree đã khớp loại mảnh
        }

        /// <summary>Xoay +90° quanh trục Y (nhìn từ trên xuống là theo chiều kim đồng hồ): E→S, S→W, W→N, N→E.</summary>
        internal static int RotateMask90(int mask)
        {
            int next = 0;
            if ((mask & E) != 0) next |= S;
            if ((mask & S) != 0) next |= W;
            if ((mask & W) != 0) next |= N;
            if ((mask & N) != 0) next |= E;
            return next;
        }

        internal static int CountBits(int mask)
        {
            int count = 0;
            while (mask != 0)
            {
                count += mask & 1;
                mask >>= 1;
            }
            return count;
        }

        internal static int OppositeDir(int dir) =>
            dir == E ? W : dir == W ? E : dir == N ? S : N;

        /// <summary>Yaw của ô side/side_rim để vỉa hè quay về <paramref name="dir"/> — vỉa hè chìa về
        /// -Z ở yaw 0, xoay CW theo yaw.</summary>
        internal static float RimYawFacing(int dir) =>
            dir == S ? 0f : dir == W ? 90f : dir == N ? 180f : 270f;

        /// <summary>Hướng mà dãy vỉa hè mép đóng <paramref name="closed"/> chạy tới khi t DƯƠNG (t âm là
        /// hướng ngược lại) — dãy nằm vuông góc mép, xem <c>EdgeRim</c> trong
        /// <see cref="JunctionTileEmitter.ForEachJunctionTile"/>.</summary>
        internal static int RimRunPlusDir(int closed) =>
            closed == S ? E : closed == W ? S : closed == N ? W : N;

        /// <summary>Bước 1 nấc lattice theo <paramref name="dir"/> (0 = không hướng nào).</summary>
        internal static (int x, int y) DirStep(int dir) =>
            dir == E ? (1, 0) : dir == W ? (-1, 0) : dir == N ? (0, 1) : dir == S ? (0, -1) : (0, 0);

        /// <summary>Xoay offset ô quanh tâm mảnh, mỗi nấc 90° THEO CHIỀU KIM ĐỒNG HỒ (khớp yaw +Y của
        /// prefab và chiều xoay sprite trên canvas): (x, y) → (y, -x).</summary>
        internal static (float x, float y) RotateCellsCW(float x, float y, int turns)
        {
            for (int k = 0; k < (turns & 3); k++) (x, y) = (y, -x);
            return (x, y);
        }

        /// <summary>Mảnh giao = mọi mảnh KHÔNG phải straight (turn / T / cross) — đúng tập mảnh ghép từ
        /// ô modular, nên khớp 1:1 với <c>RoadPieceRectCells</c>.</summary>
        internal static bool IsJunctionMask(int m) => m != 0 && !IsStraightLikeMask(m);

        /// <summary>Mảnh CUA: đúng 2 nhánh mở VUÔNG GÓC ⇒ lõi là cung turn 2x2 thay 4 ô center.</summary>
        internal static bool IsArcCoreMask(int m) => CountBits(m) == 2 && !IsStraightLikeMask(m);

        internal static bool IsStraightLikeMask(int m)
        {
            int d = CountBits(m);
            return d == 1 || m == (E | W) || m == (N | S);
        }
    }
}
#endif
