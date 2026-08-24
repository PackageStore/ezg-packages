#if UNITY_EDITOR
namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>All Road 2 geometry constants (offsets, arm-share steps, block-slot pitch).</summary>
    internal static class Road2Constants
    {
        // KHÔNG alias RoadTileColumnOffsetCells (StraightTileEmitter) dù trùng giá trị 0.25 — hằng đó chia
        // cột DỌC TRỤC cho mảnh 1x1 type-1, còn hằng này dịch vỉa hè NGANG TRỤC ra khỏi pivot chung
        // với side. Gộp chung thì đổi 1 trong 2 sẽ âm thầm sai cái còn lại.
        internal const float Road2RimLateralOffset = 0.25f;

        // Khoảng cách pivot road2_center_filler tới tim đường.
        internal const float Road2FillerLateralOffset = 0.625f;

        /// <summary>Tầm quét nhánh chạm sườn của lớp Road 2.</summary>
        internal const int Road2SideBranchReachSteps = 3;

        internal const float Road2CornerPivotX = 0.75f, Road2CornerPivotY = -0.75f;
        internal const float Road2ArmOffset = 1.0f;
        internal const float Road2HalfOffset = 1.5f;

        internal const int Road2ArmShareFullSteps = 3;
        internal const int Road2ArmShareOuterSteps = 4;

        // Nhường NỬA nhánh mã hoá ở nibble CAO của junctionArms.
        internal const int Road2OuterArmShift = 4;

        // Khối 1.5×1.5 của mảnh giao KHÔNG-arc lát bằng ô 0.5 ô.
        internal const float Road2BlockSlotPitch = 0.5f;
    }
}
#endif
