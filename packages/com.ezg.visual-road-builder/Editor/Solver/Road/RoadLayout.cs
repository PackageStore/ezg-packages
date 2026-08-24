#if UNITY_EDITOR
using System.Collections.Generic;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Kết quả giải layout — dùng thay cho việc tự gọi <see cref="MaskBuilder.BuildMasks"/> +
    /// <see cref="HalfStraightEmitter.CollectHalfStraightSets"/> rải rác ở từng đường.</summary>
    internal sealed class RoadLayout
    {
        public int[] Masks;
        public readonly HashSet<int> ReplacedByHalf = new HashSet<int>();
        public readonly HashSet<int> DroppedBetween = new HashSet<int>();
        /// <summary>Node xuyên thẳng LẺ nấc lattice tính từ node nổi bật gần nhất — thuần tuý điểm nối
        /// do edge nửa ô sinh ra, KHÔNG tự đặt ô riêng (D2 run-coalescing).</summary>
        public readonly HashSet<int> OffStride = new HashSet<int>();
        /// <summary>Node xuyên thẳng là NẤC CUỐI của 1 run có tổng độ dài LẺ — đặt 1 ô NỬA hướng ra
        /// node nổi bật (giá trị = hướng đó) để lấp đúng phần dư 0.5 ô.</summary>
        public readonly Dictionary<int, int> TailHalfDir = new Dictionary<int, int>();
        /// <summary>Node đầu cụt LẺ của một run 1 nấc CÔ LẬP — bao ô ĐẦY tại chính node đó thay vì
        /// 2 nửa cột chìa ra 2 hướng ngược nhau.</summary>
        public readonly HashSet<int> IsolatedFullAnchor = new HashSet<int>();
        /// <summary>Node thẳng bị mesh mảnh GIAO nuốt trọn nên không đặt gì — CHỈ
        /// <see cref="Road2JunctionEffects.SweepArm"/> đổ vào.</summary>
        public readonly HashSet<int> ArcSwallowed = new HashSet<int>();
        /// <summary>Quarter-cell mà ô bo góc (curve + curve_rim) đã trải vỉa hè.</summary>
        public readonly HashSet<long> FilletKerb = new HashSet<long>();
        /// <summary>Ô bo góc bị thay bằng ô cua nhỏ: khoá = pivot + yaw của curve, giá trị =
        /// pivot + yaw của road1x1_turn.</summary>
        public readonly Dictionary<long, (float x, float y, float yaw)> FilletTurn =
            new Dictionary<long, (float x, float y, float yaw)>();

        public bool Skip(int idx) =>
            ReplacedByHalf.Contains(idx) || DroppedBetween.Contains(idx) || OffStride.Contains(idx)
            || ArcSwallowed.Contains(idx);

        /// <summary>Ô bo góc tại (x, y, yaw) có nhường chỗ cho ô cua nhỏ không (và ở đâu)?</summary>
        public (float x, float y, float yaw)? FilletTurnAt(float x, float y, float yaw) =>
            FilletTurn.TryGetValue(LatticeKeys.CurveKey(x, y, yaw), out (float x, float y, float yaw) turn)
                ? turn
                : null;

        /// <summary>Ô side_rim tại (x, y, yaw) có bị ô bo góc lấp chỗ chưa? Vỉa hè của arm nằm
        /// giữa 2 ô bo góc thì ô bo góc đã trải đủ, thêm side_rim là chồng mesh.</summary>
        public bool RimCovered(float x, float y, float yaw) =>
            FilletKerb.Contains(LatticeKeys.KerbCellKey(x, y, 0f, -0.75f, yaw));
    }
}
#endif
