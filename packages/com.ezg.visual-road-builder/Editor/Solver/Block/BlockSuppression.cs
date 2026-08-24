#if UNITY_EDITOR
using System.Collections.Generic;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Nửa mặt cắt đường mà khối đã chiếm, theo từng điểm lattice. Nửa còn lại vẫn đặt
    /// đường thường, nên station bờ Bắc + parking bờ Nam trên cùng một đoạn ghép khít nhau.
    /// Mảnh thẳng chạy VUÔNG GÓC dải khối, hoặc mảnh GIAO mà đường KHÔNG xuyên qua theo trục dải
    /// (cua, hoặc nhánh cắt ngang) ⇒ chặn cả điểm như trước. Mảnh giao mà đường xuyên qua thì
    /// GIỮ: mảnh giao tự lát trọn mặt cắt và tự đọc <see cref="BlockRoadSkin"/> để bỏ vỉa hè trên
    /// dải khối, nên chặn cả điểm chỉ để lại lỗ đúng chỗ khối nối vào.</summary>
    internal sealed class BlockSuppression
    {
        private readonly Dictionary<int, int> _sides = new();
        private readonly HashSet<int> _full = new();

        /// <summary>Các nửa đã bị khối chiếm tại điểm này (0 = còn nguyên).</summary>
        internal int Sides(int idx) => _sides.TryGetValue(idx, out int s) ? s : 0;

        /// <summary>Điểm mất TRỌN mảnh đường — solver bỏ hẳn, không đặt ô nào.</summary>
        internal bool Blocked(int idx) => _full.Contains(idx);

        internal void Take(int idx, int roadMask, int side)
        {
            int axis = MaskClassifier.StraightSides(roadMask);
            bool keeps = DirBits.IsStraightLikeMask(roadMask)
                ? (axis & side) != 0
                : (roadMask & ThroughAxis(side)) == ThroughAxis(side);
            if (!keeps)
            {
                _full.Add(idx);
                return;
            }

            int taken = Sides(idx) | side;
            _sides[idx] = taken;
            if ((taken & axis) == axis) _full.Add(idx); // 2 khối kẹp 2 bên → hết đường thường
        }

        /// <summary>Trục SONG SONG dải khối (dải nằm ở nửa <paramref name="side"/> nên chạy vuông
        /// góc với nó). Mảnh giao chỉ chia nửa được khi đường xuyên qua trọn trục này — cua hay
        /// nhánh cắt ngang thì mảnh của nó chiếm luôn phần dải khối, không tách được.</summary>
        private static int ThroughAxis(int side) =>
            (side & (DirBits.N | DirBits.S)) != 0 ? DirBits.E | DirBits.W : DirBits.N | DirBits.S;

        internal int Count => _sides.Count;
    }
}
#endif
