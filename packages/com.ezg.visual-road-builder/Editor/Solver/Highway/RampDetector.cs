#if UNITY_EDITOR
using System.Collections.Generic;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Detects ramp junctions between highway and road, computes ramp span and bridge
    /// positions. Pure geometry — no prefab instantiation.</summary>
    internal sealed class RampDetector
    {
        internal const int RampHalfWidthCells = 2;
        private const int RampRoadArmCells = 3;
        private const int RampHwLeftCells = 3;
        private const int RampHwRightCells = 1;

        private readonly RoadCanvasDoc _doc;

        internal RampDetector(RoadCanvasDoc doc) { _doc = doc; }

        /// <summary>Đoạn cao tốc mà mesh ramp đã phủ TRỌN (cả lòng đường lẫn vỉa hè) — cột ô modular
        /// nằm trong đoạn này phải nhường ramp. Mesh lệch <see cref="RampHwLeftCells"/> ô bên TRÁI +
        /// <see cref="RampHwRightCells"/> ô bên PHẢI nhìn theo hướng stem. Trả trục run (highway dọc
        /// ⇒ stem Đông/Tây), toạ độ line và đoạn [lo2, hi2] dọc trục run — đơn vị NỬA ô.
        /// Nguồn DUY NHẤT của mép nối ramp ↔ cột, dùng chung cho bake, preview, overlap và debug.</summary>
        internal static (bool horiz, int line2, int lo2, int hi2) RampHighwaySpan(int x2, int y2, int stem, bool flipped)
        {
            bool horiz = stem == DirBits.N || stem == DirBits.S;
            int along = horiz ? x2 : y2;
            int left = RampHwLeftCells * 2, right = RampHwRightCells * 2;
            // flipped = lật gương ngang → đổi bên loe (3 ô ↔ 1 ô) dọc cao tốc, KHÍT với mesh mirror scaleMul.x=-1.
            bool wideLow = (stem == DirBits.N || stem == DirBits.W) ^ flipped;
            return wideLow
                ? (horiz, horiz ? y2 : x2, along - left, along + right)
                : (horiz, horiz ? y2 : x2, along - right, along + left);
        }

        /// <summary>Ramp tại anchor này có bị LẬT GƯƠNG (phím F) không.</summary>
        internal bool RampFlipped(int x2, int y2) =>
            _doc.RampFlips != null && _doc.RampFlips.Contains(EdgeCodec.RampAnchorKey(x2, y2));

        /// <summary>Phát hiện nút ramp Highway→Road: tại mỗi điểm cao tốc ĐI THẲNG, road THƯỜNG đấu vào
        /// từ 1 hướng VUÔNG GÓC. Dùng CHUNG cho bake và preview (KHÔNG log/không đụng prefab).</summary>
        internal List<(int x2, int y2, int stem, int hwMask)> CollectRampJunctions(
            int[] hwMasks, int[] roadMasks)
        {
            int lw = _doc.LatticeW, lh = _doc.LatticeH;
            var ramps = new List<(int, int, int, int)>();
            var anchored = new HashSet<int>();
            for (int y2 = 0; y2 < lh; y2++)
            {
                for (int x2 = 0; x2 < lw; x2++)
                {
                    int mask = hwMasks[y2 * lw + x2];
                    if (mask != (DirBits.E | DirBits.W) && mask != (DirBits.N | DirBits.S)) continue;
                    TryAddRamp(x2, y2, mask, roadMasks, anchored, ramps);
                }
            }
            return ramps;
        }

        /// <summary>Thử nhận ramp tại 1 anchor cao tốc.</summary>
        private void TryAddRamp(int x2, int y2, int axis, int[] roadMasks, HashSet<int> anchored,
            List<(int, int, int, int)> ramps)
        {
            int lw = _doc.LatticeW, lh = _doc.LatticeH;
            int i = y2 * lw + x2;
            if (anchored.Contains(i)) return;
            int reach = RampHalfWidthCells * 2;

            int stem = 0;
            if (axis == (DirBits.N | DirBits.S))
            {
                if (x2 + reach < lw && (roadMasks[i + reach] & DirBits.E) != 0) stem = DirBits.E;
                else if (x2 - reach >= 0 && (roadMasks[i - reach] & DirBits.W) != 0) stem = DirBits.W;
            }
            else
            {
                if (y2 + reach < lh && (roadMasks[i + reach * lw] & DirBits.N) != 0) stem = DirBits.N;
                else if (y2 - reach >= 0 && (roadMasks[i - reach * lw] & DirBits.S) != 0) stem = DirBits.S;
            }
            if (stem == 0) return;

            anchored.Add(i);
            ramps.Add((x2, y2, stem, axis));
        }

        /// <summary>Road (bị overlap-rule chặn) dừng cách tâm ramp 2 ô nhưng arm-road của mesh vươn tới
        /// <see cref="RampRoadArmCells"/> ô → BỎ mảnh road full nằm dưới arm (thêm vào
        /// <paramref name="rampSuppress"/>) và tính vị trí+yaw part 0.5×1 ô ngay ngoài đầu arm để bắc cầu.
        /// Trả true nếu CÓ road THƯỜNG ngay ngoài arm.
        /// +X mảnh cầu quay RA road (0=E,90=S,180=W,270=N).</summary>
        internal bool TryRampRoadBridge(int x2, int y2, int stem, int[] roadMasks, HashSet<int> rampSuppress,
            out float bx, out float by, out float bYaw)
        {
            int lw = _doc.LatticeW, lh = _doc.LatticeH;
            int ux2 = stem == DirBits.E ? 2 : stem == DirBits.W ? -2 : 0;
            int uy2 = stem == DirBits.N ? 2 : stem == DirBits.S ? -2 : 0;

            for (int h = 1; h <= RampRoadArmCells * 2; h++)
            {
                int sx2 = x2 + ux2 / 2 * h, sy2 = y2 + uy2 / 2 * h;
                if (sx2 >= 0 && sx2 < lw && sy2 >= 0 && sy2 < lh) rampSuppress.Add(sy2 * lw + sx2);
            }

            float bo = RampRoadArmCells + 0.25f;
            bx = x2 * 0.5f + (stem == DirBits.E ? bo : stem == DirBits.W ? -bo : 0f);
            by = y2 * 0.5f + (stem == DirBits.N ? bo : stem == DirBits.S ? -bo : 0f);
            bYaw = stem == DirBits.E ? 0f : stem == DirBits.S ? 90f : stem == DirBits.W ? 180f : 270f;

            int nx2 = x2 + ux2 * (RampRoadArmCells + 1), ny2 = y2 + uy2 * (RampRoadArmCells + 1);
            if (nx2 < 0 || nx2 >= lw || ny2 < 0 || ny2 >= lh) return false;
            int target = roadMasks[ny2 * lw + nx2];
            return target != 0 && !DirBits.IsJunctionMask(target);
        }
    }
}
#endif
