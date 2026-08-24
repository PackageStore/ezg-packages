#if UNITY_EDITOR
namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Straight tile emitter for PATH layer.</summary>
    internal static class PathStraightWalker
    {
        /// <summary>PATH chỉ coi mảnh XUYÊN THẲNG (2 nhánh đối nhau) là mảnh thẳng. Đầu cụt (1 nhánh)
        /// đi nhánh mảnh GIAO để được bịt bằng nắp 2 ô turn — khác type-1.</summary>
        internal static bool IsPathStraightMask(int mask) =>
            mask == (DirBits.E | DirBits.W) || mask == (DirBits.N | DirBits.S);

        /// <summary>Duyệt 4 ô side của node thẳng — 2 cột lệch ±0.125 dọc trục, mỗi cột 2 ô úp lưng.</summary>
        internal static void ForEachPathStraightTile(
            float x, float y, float yaw, System.Action<PathTilePart, float, float, float> place)
        {
            StraightTileEmitter.ForEachStraightTile(x, y, yaw, true, (tx, ty, tyaw) =>
                place(PathTilePart.Side, tx, ty, tyaw),
                columnOffset: PathTileVocabulary.PathTileColumnOffsetCells);
        }
    }
}
#endif
