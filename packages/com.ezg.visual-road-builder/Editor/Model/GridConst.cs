#if UNITY_EDITOR
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Compile-time layout/size constants and ParkingCells helper.</summary>
    internal static class GridConst
    {
        internal const int MaxGridSize = 256;
        // Lề canvas: trái chứa số trục Y, đáy chứa số trục X.
        internal const float GutterLeft = 34f;
        internal const float GutterTop = 14f;
        internal const float GutterRight = 14f;
        internal const float GutterBottom = 18f;
        internal const float OuterMargin = 120f;
        internal const float ControlColumnWidth = 296f;

        // Kích thước khối + part nối station, khớp slice art trong _road_plan.psd @128 px/ô — CỐ ĐỊNH,
        // KHÔNG cấu hình trong library (đổi art thì sửa ở đây): station_area 512 px = 4×4 ô,
        // parking_area 512×128 = 4×1 ô slab (khối 4×2 = slab + dải đường mesh parking tự chứa).
        internal const int StationSize = 4;
        internal const int ParkingLong = 4;
        internal const int ParkingShort = 2;

        // Khoảng cách 2 điểm lưới trong scene = 1 đơn vị world, CỐ ĐỊNH (không cấu hình được).
        internal const float CellWorldSize = 1f;

        /// <summary>Kích thước (ô) theo hướng mặt: rot chẵn = dài theo X, rot lẻ = dài theo Z.</summary>
        internal static Vector2Int ParkingCells(int rot) => (rot & 1) == 1
            ? new Vector2Int(ParkingShort, ParkingLong)
            : new Vector2Int(ParkingLong, ParkingShort);
    }
}
#endif
