#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Quantise piece/column footprints to eighth-grid boxes; add/hit/inflate box helpers.
    /// Remains partial during migration — R6: depends on solver methods not yet extracted.</summary>
    public sealed partial class VisualRoadBuilderTool
    {
        private const int EighthsPerCell = 4;

        // D10: biên rộng thêm CHỈ áp cho so sánh có Road2 (layer 3) ở ít nhất 1 bên — khung road/highway
        // gốc (2 lớp cũ) không bao giờ bị phóng. +0.5 ô/cạnh = +2 eighth (đúng bội EighthsPerCell).
        private const int Road2MarginEighths = 2;

        private static long QKey(int ex, int ey) => ((long)(ex + 4096) << 20) | (uint)(ey + 4096);

        /// <summary>Tâm (đơn vị eighth) của điểm lattice nửa ô.</summary>
        private static int LatticeToEighth(int p2) => p2 * (EighthsPerCell / 2);

        /// <summary>Khung (đơn vị eighth) từ khung ô — merged PieceEighthBox cho cả road và road2.</summary>
        private static (int x0, int y0, int x1, int y1) PieceEighthBox(
            (float dx, float dy, float w, float h) rect, int ex, int ey)
        {
            int cx = ex + Mathf.RoundToInt(rect.dx * EighthsPerCell);
            int cy = ey + Mathf.RoundToInt(rect.dy * EighthsPerCell);
            int halfW = Mathf.RoundToInt(rect.w * EighthsPerCell * 0.5f);
            int halfH = Mathf.RoundToInt(rect.h * EighthsPerCell * 0.5f);
            return (cx - halfW, cy - halfH, cx + halfW, cy + halfH);
        }

        /// <summary>Khung (đơn vị eighth) của mảnh road tại điểm lattice.</summary>
        private static (int x0, int y0, int x1, int y1) RoadEighthBox(int mask, int ex, int ey)
            => PieceEighthBox(RoadPieceRectCells(mask), ex, ey);

        private static (int x0, int y0, int x1, int y1) Road2EighthBox(int mask, int ex, int ey)
            => PieceEighthBox(Road2PieceRectCells(mask), ex, ey);

        /// <summary>Khung (đơn vị eighth) của MỘT NỬA Ô highway đã vẽ — 0.5 ô dọc run × 2 ô ngang, nằm
        /// NGANG khớp ghost dưới con trỏ. Dùng cho hover của brush, không phải cho mảnh mesh đã lát.</summary>
        private static (int x0, int y0, int x1, int y1) HighwayEighthBox(int ex, int ey) =>
            (ex - EighthsPerCell / 4, ey - EighthsPerCell, ex + EighthsPerCell / 4, ey + EighthsPerCell);

        /// <summary>Khung (đơn vị eighth) của MỘT CỘT highway đã lát: 0.5 ô dọc run × 2 ô ngang. Tâm cột
        /// luôn là bội 0.25 ô = đúng 1 eighth nên khung không dính sai số.</summary>
        private static (int x0, int y0, int x1, int y1) HighwayColumnEighthBox(float cx, float cy, bool horiz)
        {
            int ex = Mathf.RoundToInt(cx * EighthsPerCell), ey = Mathf.RoundToInt(cy * EighthsPerCell);
            int along = EighthsPerCell / 4, across = EighthsPerCell; // nửa cột 0.25 ô, nửa bề ngang 1 ô
            return horiz
                ? (ex - along, ey - across, ex + along, ey + across)
                : (ex - across, ey - along, ex + across, ey + along);
        }

        private static void AddBox(HashSet<long> set, (int x0, int y0, int x1, int y1) box)
        {
            for (int x = box.x0; x < box.x1; x++)
                for (int y = box.y0; y < box.y1; y++)
                    set.Add(QKey(x, y));
        }

        private static bool BoxHits(HashSet<long> set, (int x0, int y0, int x1, int y1) box)
        {
            for (int x = box.x0; x < box.x1; x++)
                for (int y = box.y0; y < box.y1; y++)
                    if (set.Contains(QKey(x, y))) return true;
            return false;
        }

        /// <summary>Nới khung (đơn vị eighth) đều 4 cạnh theo margin Road2 (D10).</summary>
        private static (int x0, int y0, int x1, int y1) InflateBox(
            (int x0, int y0, int x1, int y1) box, int marginEighths) =>
            (box.x0 - marginEighths, box.y0 - marginEighths, box.x1 + marginEighths, box.y1 + marginEighths);

        /// <summary>Bề rộng mặt cắt Road2 (không tính vỉa hè, đơn vị ô).</summary>
        private const float Road2CrossSectionWidthCells = (Road2Constants.Road2FillerLateralOffset + 0.25f) * 2f;

        /// <summary>Khung (đơn vị ô) mà mảnh road ứng với mask CHIẾM THẬT.</summary>
        private static (float dx, float dy, float w, float h) RoadPieceRectCells(int mask)
        {
            if (mask == 0) return (0f, 0f, 1f, 1f);
            if (IsStraightLikeMask(mask))
            {
                return mask switch
                {
                    DirE => (RoadTileColumnOffsetCells, 0f, 0.5f, 1f),
                    DirW => (-RoadTileColumnOffsetCells, 0f, 0.5f, 1f),
                    DirN => (0f, RoadTileColumnOffsetCells, 1f, 0.5f),
                    DirS => (0f, -RoadTileColumnOffsetCells, 1f, 0.5f),
                    _ => (0f, 0f, 1f, 1f),
                };
            }

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            ForEachJunctionTile(mask, 0, (part, x, y, yaw) =>
            {
                if (!TryTileLocalRect(part, out float lx, out float ly, out float half)) return;
                (float ox, float oy) = RotateCellsCW(lx, ly, Mathf.RoundToInt(yaw / 90f));
                minX = Mathf.Min(minX, x + ox - half);
                maxX = Mathf.Max(maxX, x + ox + half);
                minY = Mathf.Min(minY, y + oy - half);
                maxY = Mathf.Max(maxY, y + oy + half);
            });
            return ((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, maxX - minX, maxY - minY);
        }

        /// <summary>Road2 tương đương RoadPieceRectCells: thẳng giữ chiều dọc trục, đổi bề ngang.</summary>
        private static (float dx, float dy, float w, float h) Road2PieceRectCells(int mask)
        {
            if (mask == 0) return (0f, 0f, 1f, Road2CrossSectionWidthCells);
            if (IsStraightLikeMask(mask))
            {
                return mask switch
                {
                    DirE => (RoadTileColumnOffsetCells, 0f, 0.5f, Road2CrossSectionWidthCells),
                    DirW => (-RoadTileColumnOffsetCells, 0f, 0.5f, Road2CrossSectionWidthCells),
                    DirN => (0f, RoadTileColumnOffsetCells, Road2CrossSectionWidthCells, 0.5f),
                    DirS => (0f, -RoadTileColumnOffsetCells, Road2CrossSectionWidthCells, 0.5f),
                    _ => (mask & (DirE | DirW)) != 0
                        ? (0f, 0f, 1f, Road2CrossSectionWidthCells)
                        : (0f, 0f, Road2CrossSectionWidthCells, 1f),
                };
            }

            (float dx, float dy, float w, float h) = RoadPieceRectCells(mask);
            return (dx, dy, w * Road2CrossSectionWidthCells, h * Road2CrossSectionWidthCells);
        }
    }
}
#endif
