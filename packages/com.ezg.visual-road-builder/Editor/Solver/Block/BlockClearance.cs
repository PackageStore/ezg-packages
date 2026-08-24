#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Clearance centreline→block-edge và apron-strip profile cho mọi tổ hợp (road-type,
    /// block-type). Một chỗ duy nhất chứa hằng bề rộng đường — đo lại art chỉ sửa ở đây.</summary>
    public sealed partial class VisualRoadBuilderTool
    {
        private const float RoadHalfWidthCells = 0.5f;

        // Nửa bề rộng mặt cắt Road 2 — ĐO LẠI ART THÌ SỬA Ở ĐÂY (suy từ Road2CrossSectionWidthCells,
        // Overlap.cs). Đổi Road2FillerLateralOffset (Road2Solver.cs) sẽ tự cập nhật qua hằng đó.
        private const float Road2HalfWidthCells = Road2CrossSectionWidthCells / 2f;

        // Bề dày ô road2_center_filler, đo trên Road_0.5x1_center (local z = ±0.125).
        private const float Road2FillerDepthCells = 0.25f;

        // Nửa bề rộng LÒNG đường mà mặt cắt TỰ LÁT, không kể vỉa hè: type-1 chỉ có ô side dày
        // RoadHalfWidthCells; Road 2 có thêm ô filler ở Road2FillerLateralOffset. Ô filler phát theo
        // CỘT nên sống sót BlockSuppression ⇒ khối KHÔNG lát lại được khúc đó (xem BlockApronDepths).
        private const float Road2SurfaceHalfWidthCells =
            Road2Constants.Road2FillerLateralOffset + Road2FillerDepthCells / 2f;

        private const float StationApronPlazaCells = 0.5f;

        private static float RoadHalfWidthFor(bool road2) =>
            road2 ? Road2HalfWidthCells : RoadHalfWidthCells;

        private static float RoadSurfaceHalfWidthFor(bool road2) =>
            road2 ? Road2SurfaceHalfWidthCells : RoadHalfWidthCells;

        /// <summary>Clearance centreline → block road-facing edge, đơn vị HALF-CELL steps.</summary>
        private static int BlockClearanceSteps(bool road2, float plazaCells) =>
            Mathf.CeilToInt((RoadHalfWidthFor(road2) + plazaCells) * 2f);

        private static float BlockClearanceCells(bool road2, float plazaCells) =>
            BlockClearanceSteps(road2, plazaCells) * 0.5f;

        // Tránh alloc mỗi repaint — cache sẵn dải apron cho mọi step count gặp được.
        // station+road1 = 2 steps: { 0.25, 0.75 } — khớp chính xác hành vi shipped hiện tại.
        private static readonly float[] ApronDepths1 = { 0.25f };
        private static readonly float[] ApronDepths2 = { 0.25f, 0.75f };
        private static readonly float[] ApronDepths3 = { 0.25f, 0.75f, 1.25f };
        private static readonly float[] ApronDepths4 = { 0.25f, 0.75f, 1.25f, 1.75f };

        // Road 2: hàng apron BỎ khúc [RoadHalfWidthCells .. Road2SurfaceHalfWidthCells] mà ô filler đã
        // lát sẵn (lát đè lên là 2 lớp mesh), rồi khởi động LẠI lưới hàng 0.5 ô từ mép ngoài khúc đó ⇒
        // station Road 2 (3 steps) ra { 0.25, 1.0 } chứ không phải { 0.25, 0.75, 1.25 }.
        private static readonly float[] Road2ApronDepths1 = { 0.25f };
        private static readonly float[] Road2ApronDepths2 = { 0.25f };
        private static readonly float[] Road2ApronDepths3 = { 0.25f, 1f };
        private static readonly float[] Road2ApronDepths4 = { 0.25f, 1f, 1.5f };

        /// <summary>Dải apron 0.5 ô lát [0 .. clearance] trừ khúc mặt cắt đường tự lát — trả mảng
        /// cached, caller CHỈ đọc. Hàng cuối dừng khi mép ngoài của nó vượt clearance, và mesh khối lùi
        /// về đúng mép đó (xem <see cref="BlockPivotInsetFor"/>).</summary>
        private static float[] BlockApronDepths(bool road2, float plazaCells) =>
            ApronDepthsForSteps(road2, BlockClearanceSteps(road2, plazaCells));

        private static float[] ApronDepthsForSteps(bool road2, int steps) => road2
            ? steps switch
            {
                1 => Road2ApronDepths1,
                2 => Road2ApronDepths2,
                3 => Road2ApronDepths3,
                4 => Road2ApronDepths4,
                _ => BuildApronDepths(true, steps),
            }
            : steps switch
            {
                1 => ApronDepths1,
                2 => ApronDepths2,
                3 => ApronDepths3,
                4 => ApronDepths4,
                _ => BuildApronDepths(false, steps),
            };

        // Parking: slab ăn SÁT kerb nên tim đường LUÔN cách mặt khối 0.5 ô ở MỌI road type — chỗ hook
        // KHÔNG giãn theo bề rộng đường (chốt theo map demo: parking Road 2 vẫn neo ở 0.5 ô).
        private const int ParkingHookSteps = 1;

        /// <summary>Dải apron parking = đúng khoảng hook (0.5 ô) ở MỌI road type: phần lòng đường ngoài
        /// 0.5 ô đã là ô filler của chính mặt cắt Road 2, lát thêm chỉ chồng mesh.</summary>
        private static float[] ParkingApronDepths(bool road2) =>
            ApronDepthsForSteps(road2, ParkingHookSteps);

        /// <summary>Độ sâu v của ô bo góc apron. Station lấy GIỮA clearance ⇒ road1 0.5, Road 2 0.75
        /// (dùng cho cả bo góc NGOÀI lẫn 2 ô đảo phân cách giữa 2 lối vào).</summary>
        private static float StationOuterFilletDepth(bool road2) =>
            BlockClearanceCells(road2, StationApronPlazaCells) * 0.5f;

        /// <summary>Bo góc parking neo ở mép mặt khối — nhưng chỉ khi mặt đó đứng NGOÀI lòng đường.
        /// Trên Road 2 mặt khối (0.5 ô) nằm LỌT trong nửa lòng đường (0.75 ô) nên không có bậc vỉa hè
        /// nào để bo: trả 0 = không đặt bo góc.</summary>
        private static float ParkingOuterFilletDepth(bool road2)
        {
            float faceDepth = ParkingHookSteps * 0.5f;
            return faceDepth >= RoadSurfaceHalfWidthFor(road2) ? faceDepth : 0f;
        }

        /// <summary>Khối nối Road 2 đặt LÙI về phía đường đúng bề dày dải filler: mặt cắt Road 2 tự lát
        /// thêm bấy nhiêu mà apron không lát lại được, nên mép mesh khối phải dịch vào đúng đó mới khít
        /// mép apron. 0 = type-1, pivot giữ nguyên.</summary>
        private static float BlockPivotInsetFor(bool road2) =>
            RoadSurfaceHalfWidthFor(road2) - RoadHalfWidthCells;

        // Fallback cho step count ngoài dự kiến — không cache vì không nên xảy ra trong thực tế.
        private static float[] BuildApronDepths(bool road2, int steps)
        {
            float clearance = steps * 0.5f;
            var depths = new List<float>(steps);

            void Run(float from, float to)
            {
                for (float v = from + 0.25f; v + 0.25f <= to + 0.001f; v += 0.5f) depths.Add(v);
            }

            if (road2)
            {
                Run(0f, Mathf.Min(RoadHalfWidthCells, clearance));
                Run(Road2SurfaceHalfWidthCells, clearance);
            }
            else Run(0f, clearance);

            return depths.ToArray();
        }
    }
}
#endif
