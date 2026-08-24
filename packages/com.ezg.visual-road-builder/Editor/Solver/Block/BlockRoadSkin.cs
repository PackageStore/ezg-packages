#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Khối đổi MẶT đường thường mà không bỏ hẳn nửa đường như <see cref="BlockSuppression"/>:
    /// <list type="bullet">
    /// <item>cột apron trơn — apron station trải ô center (lối vào) nên nửa ĐỐI DIỆN trên cùng cột
    /// cũng phải trơn: xe cắt ngang cả mặt cắt đường, để lại ô side là còn vạch mép chắn ngang
    /// giữa 2 lối vào đối nhau;</item>
    /// <item>dải không vỉa hè — thân parking áp thẳng vào lòng đường nên vỉa hè trên dải đó bị
    /// khối che, đặt vào là chồng mesh.</item>
    /// </list></summary>
    internal sealed class BlockRoadSkin
    {
        // Nửa chiều dài ô vỉa hè DỌC dải (ô side/side_rim rộng 0.5 ô theo trục đường).
        private const float KerbHalfRunCells = 0.25f;

        private readonly HashSet<long> _plain = new HashSet<long>();
        private readonly List<BlockStrip> _kerbFree = new List<BlockStrip>();
        private readonly List<BlockStrip> _blockEdges = new List<BlockStrip>();

        internal void AddPlainColumn(bool horizontal, int line2, float along) =>
            _plain.Add(PlainKey(horizontal, line2, Mathf.RoundToInt(along * 4f)));

        internal void AddKerbFree(BlockStrip strip) => _kerbFree.Add(strip);

        /// <summary>Dải mesh khối trên hàng đường này — chỉ dùng để nắn vỉa hè cưỡi mép dải
        /// (<see cref="KerbEdgeShift"/>).</summary>
        internal void AddBlockEdge(BlockStrip strip) => _blockEdges.Add(strip);

        /// <summary>Ô side tại (x, y, yaw) rơi vào cột apron trơn ⇒ thay bằng ô center.</summary>
        internal bool PlainAt(float x, float y, float yaw)
        {
            if (_plain.Count == 0) return false;
            Split(x, y, yaw, out bool horizontal, out int line2, out float along, out _);
            return _plain.Contains(PlainKey(horizontal, line2, Mathf.RoundToInt(along * 4f)));
        }

        /// <summary>Ô vỉa hè tại (x, y, yaw) nằm trong dải thân khối ⇒ bỏ.</summary>
        internal bool KerbFreeAt(float x, float y, float yaw)
        {
            if (_kerbFree.Count == 0) return false;
            Split(x, y, yaw, out bool horizontal, out int line2, out float along, out int side);
            float along2 = along * 2f;
            foreach (BlockStrip s in _kerbFree)
                if (s.Horizontal == horizontal && s.Line2 == line2 && s.Side == side
                    && along2 >= s.Lo2 && along2 <= s.Hi2)
                    return true;
            return false;
        }

        /// <summary>Ô vỉa hè ở (x, y, yaw) CƯỠI mép dải khối — nửa trong của nó nằm trên lối vào
        /// apron. Trả về độ dịch DỌC dải để mép trong khít đúng biên (0 = không cưỡi). Lưới cột
        /// type-1 luôn rơi giữa 2 nấc lattice nên chỉ cột nhánh Road 2 (Road2ArmOffset /
        /// Road2HalfOffset, lệch 0.25 ô so với lưới đó) mới cưỡi được.</summary>
        internal float KerbEdgeShift(float x, float y, float yaw)
        {
            if (_blockEdges.Count == 0) return 0f;
            Split(x, y, yaw, out bool horizontal, out int line2, out float along, out int side);
            foreach (BlockStrip s in _blockEdges)
            {
                if (s.Horizontal != horizontal || s.Line2 != line2 || s.Side != side) continue;
                float lo = s.Lo2 * 0.5f, hi = s.Hi2 * 0.5f;
                if (along - KerbHalfRunCells < lo && along + KerbHalfRunCells > lo)
                    return lo - KerbHalfRunCells - along;
                if (along - KerbHalfRunCells < hi && along + KerbHalfRunCells > hi)
                    return hi + KerbHalfRunCells - along;
            }
            return 0f;
        }

        private static long PlainKey(bool horizontal, int line2, int col4) =>
            ((long)(horizontal ? 1 : 0) << 48) | ((long)(line2 & 0xFFFFFF) << 24)
            | (uint)(col4 & 0xFFFFFF);

        /// <summary>Ô side/rim ở (x, y, yaw) thuộc dải nào: nửa mặt cắt nó phủ cho biết dải chạy
        /// ngang hay dọc, hàng điểm (nửa ô) của dải, và vị trí ô dọc dải (đơn vị ô).</summary>
        private static void Split(
            float x, float y, float yaw,
            out bool horizontal, out int line2, out float along, out int side)
        {
            side = MaskClassifier.SideAtRimYaw(yaw);
            horizontal = (side & (DirBits.N | DirBits.S)) != 0;
            line2 = Mathf.RoundToInt((horizontal ? y : x) * 2f);
            along = horizontal ? x : y;
        }
    }
}
#endif
