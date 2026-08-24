#if UNITY_EDITOR
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Lattice quantisation helpers: CurveKey, KerbCellKey, QuarterCellCenter, QKey.
    /// Shared by type-1 (<see cref="FilletCollector"/>), Road 2
    /// (<see cref="Road2JunctionEffects"/>), and Overlap.</summary>
    internal static class LatticeKeys
    {
        // "Eighths" per cell — misnomer kept on purpose (D9): value 4 means QUARTER cells.
        internal const int EighthsPerCell = 4;

        internal static long QKey(int ex, int ey) => ((long)(ex + 4096) << 20) | (uint)(ey + 4096);

        /// <summary>Khoá một ô bo góc theo pivot + yaw.</summary>
        internal static long CurveKey(float x, float y, float yaw) =>
            (QKey(Mathf.RoundToInt(x * EighthsPerCell), Mathf.RoundToInt(y * EighthsPerCell)) << 2)
            | (uint)(Mathf.RoundToInt(yaw / 90f) & 3);

        /// <summary>Quarter-cell (đơn vị 0.25 ô) mà mesh lệch <paramref name="lx"/>/<paramref name="ly"/>
        /// so với pivot rơi vào, sau khi xoay theo <paramref name="yaw"/>.</summary>
        internal static long KerbCellKey(float x, float y, float lx, float ly, float yaw)
        {
            (float ox, float oy) = DirBits.RotateCellsCW(lx, ly, Mathf.RoundToInt(yaw / 90f));
            return QKey(Mathf.RoundToInt((x + ox) * EighthsPerCell),
                        Mathf.RoundToInt((y + oy) * EighthsPerCell));
        }

        /// <summary>Tâm quarter-cell mà một ô bo góc phủ (mesh curve chìa về +X/-Z quanh pivot).</summary>
        internal static (float x, float y) QuarterCellCenter((float x, float y, float yaw) curve)
        {
            (float ox, float oy) = DirBits.RotateCellsCW(0.25f, -0.25f, Mathf.RoundToInt(curve.yaw / 90f));
            return (curve.x + ox, curve.y + oy);
        }

        // Toạ độ ô filler là bội 0.125 ô (tim đường bội 0.5 ± 0.625) nên khoá phải lượng tử theo 1/8 ô.
        internal static long Road2FillerKey(float x, float y) =>
            QKey(Mathf.RoundToInt(x * 8f), Mathf.RoundToInt(y * 8f));
    }
}
#endif
