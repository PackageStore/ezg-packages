#if UNITY_EDITOR
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Enumerates modular tile (x, y, yaw) for one straight piece — THE single source of
    /// column positions for all layers (bake, preview, overlap, debug boundary).</summary>
    internal static class StraightTileEmitter
    {
        // Mảnh 1×1 = 2 cột ô ghép cạnh nhau ⇒ mỗi cột lệch nửa bề rộng cột (0.25 ô) khỏi tâm mảnh.
        internal const float RoadTileColumnOffsetCells = 0.25f;

        /// <summary>Điểm neo + có phải ô ĐẦY của một mảnh THẲNG theo mask — nguồn DUY NHẤT cho mọi nơi
        /// gọi <see cref="ForEachStraightTile"/> bằng mask thô (bake, preview, overlap, debug boundary).
        /// Trục (2 hướng, DirE|DirW hay DirN|DirS) giữ nguyên tâm nút, ô đầy. Đầu cụt (1 hướng) neo lệch
        /// <see cref="RoadTileColumnOffsetCells"/> ô VỀ PHÍA hướng mở, chỉ 1 cột — mảnh dừng KHÍT mép nút
        /// thay vì tràn 0.5 ô ra ngoài nét vẽ (giống hành vi highway brush).</summary>
        internal static (float x, float y, bool fullCell) StraightAnchor(int mask, int x2, int y2)
        {
            float x = x2 * 0.5f, y = y2 * 0.5f;
            return mask switch
            {
                DirBits.E => (x + RoadTileColumnOffsetCells, y, false),
                DirBits.W => (x - RoadTileColumnOffsetCells, y, false),
                DirBits.N => (x, y + RoadTileColumnOffsetCells, false),
                DirBits.S => (x, y - RoadTileColumnOffsetCells, false),
                _ => (x, y, true),
            };
        }

        /// <summary>Duyệt các ô modular dựng nên một mảnh THẲNG, gọi <paramref name="place"/> với
        /// (x, y, yaw) của từng ô — nguồn DUY NHẤT của vị trí + yaw ô cho bake, preview sprite và
        /// debug boundary. Ô core chiếm đúng ô logic phía -Z của pivot, rim là vỉa hè chìa thêm ra
        /// ngoài, cả hai dùng CHUNG một transform. 1 CỘT = ô ở <paramref name="yaw"/> + ô ở yaw+180
        /// quanh CÙNG pivot → phủ 0.5 ô ngang trục × 1 ô dọc trục; mảnh 1×1
        /// (<paramref name="fullCell"/>) = 2 cột lệch ±<paramref name="columnOffset"/> ô theo trục X
        /// đã xoay — mặc định 0.25 (ô side type-1 dài 0.5 dọc trục), lớp PATH truyền 0.125 vì ô
        /// path_side chỉ dài 0.25.
        /// CHỈ xoay quanh Y (bội 90°) — không bao giờ scale âm, mesh giữ nguyên hướng normal.
        /// <paramref name="sides"/> lọc theo NỬA mặt cắt (2 ô úp lưng của mỗi cột nằm ở 2 nửa khác
        /// nhau): khối station/parking chỉ ăn nửa phía nó nên nửa kia vẫn đặt đường thường.</summary>
        internal static void ForEachStraightTile(
            float x, float y, float yaw, bool fullCell, System.Action<float, float, float> place,
            int sides = DirBits.All, float columnOffset = RoadTileColumnOffsetCells)
        {
            void Column(float cx, float cy)
            {
                for (int half = 0; half < 2; half++)
                {
                    float tileYaw = (yaw + half * 180f) % 360f; // giữ euler hint trong [0, 360)
                    if ((sides & MaskClassifier.SideAtRimYaw(tileYaw)) != 0) place(cx, cy, tileYaw);
                }
            }

            if (!fullCell)
            {
                Column(x, y);
                return;
            }

            // yaw bội 90 ⇒ lấy trục thẳng thay vì cos/sin (tránh toạ độ bake dính sai số 1e-8).
            bool alongX = (Mathf.RoundToInt(yaw / 90f) & 1) == 0;
            float dx = alongX ? columnOffset : 0f;
            float dy = alongX ? 0f : columnOffset;
            Column(x - dx, y - dy);
            Column(x + dx, y + dy);
        }

        /// <summary>Tâm ô center đặt THAY cho ô side ở (x, y, yaw): ô side lấy pivot ở tim mảnh rồi phủ
        /// nửa ô về phía vỉa hè (xem <c>TryTileLocalRect</c>), còn ô center canh TÂM ô nó phủ.</summary>
        internal static (float x, float y) PlainCoreCell(float x, float y, float yaw)
        {
            (float ox, float oy) = DirBits.RotateCellsCW(0f, -RoadTileColumnOffsetCells,
                Mathf.RoundToInt(yaw / 90f));
            return (x + ox, y + oy);
        }
    }
}
#endif
