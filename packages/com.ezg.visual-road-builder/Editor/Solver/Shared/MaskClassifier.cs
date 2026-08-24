#if UNITY_EDITOR
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Classify a mask as straight/junction/arc, compute bounding rect, tile footprint.
    /// Pure helpers that don't depend on solver state.</summary>
    internal static class MaskClassifier
    {
        /// <summary>Yaw của mảnh thẳng theo trục nhánh — base mảnh thẳng nằm dọc trục X (E/W).</summary>
        internal static float StraightYaw(int mask) => (mask & (DirBits.E | DirBits.W)) != 0 ? 0f : 90f;

        /// <summary>Hai NỬA mặt cắt của mảnh thẳng — vuông góc trục đường. Mảnh 1×1 ghép từ 2 ô side
        /// úp lưng nên mỗi nửa là một ô riêng, tách rời được: khối station/parking chỉ ăn đúng nửa
        /// phía nó (BlockSuppression).</summary>
        internal static int StraightSides(int mask) =>
            (mask & (DirBits.E | DirBits.W)) != 0 ? DirBits.N | DirBits.S : DirBits.E | DirBits.W;

        /// <summary>Nửa mặt cắt mà ô side/side_rim đặt ở <paramref name="yaw"/> phủ — nghịch đảo của
        /// RimYawFacing (ô side chìa về -Z ở yaw 0).</summary>
        internal static int SideAtRimYaw(float yaw) =>
            (Mathf.RoundToInt(yaw / 90f) & 3) switch { 0 => DirBits.S, 1 => DirBits.W, 2 => DirBits.N, _ => DirBits.E };

        /// <summary>Số điểm lattice có nét vẽ — mỗi điểm ứng đúng 1 mảnh road.</summary>
        internal static int CountPieces(int[] masks)
        {
            int count = 0;
            foreach (int mask in masks)
                if (mask != 0) count++;
            return count;
        }

        /// <summary>Tâm + nửa cạnh (đơn vị ô) của MỘT ô modular so với pivot của nó, TRƯỚC khi xoay.
        /// Trả false cho ô vỉa hè (rim): nó chìa ra NGOÀI ô logic nên không tính vào khung/footprint.
        /// Nguồn DUY NHẤT của hình học ô cho cả khung bbox mảnh (RoadPieceRectCells) và
        /// footprint ô thật (ForEachRoadTileCell) nên 2 chỗ luôn khớp nhau.</summary>
        internal static bool TryTileLocalRect(RoadTilePart part, out float lx, out float ly, out float half)
        {
            switch (part)
            {
                case RoadTilePart.Center: (lx, ly, half) = (0f, 0f, 0.25f); return true;
                case RoadTilePart.Side: (lx, ly, half) = (0f, -0.25f, 0.25f); return true;
                case RoadTilePart.Turn2x2: (lx, ly, half) = (-0.5f, -0.5f, 0.5f); return true;
                // Cua nhỏ phủ ĐÚNG quarter-cell mà ô side ở cùng pivot sẽ phủ
                case RoadTilePart.Turn1x1: (lx, ly, half) = (0f, -0.25f, 0.25f); return true;
                case RoadTilePart.Curve: (lx, ly, half) = (0.25f, -0.25f, 0.25f); return true;
                default: lx = ly = half = 0f; return false;
            }
        }
    }
}
#endif
