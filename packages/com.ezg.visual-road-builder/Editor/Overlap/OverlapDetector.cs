#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Detect HW/Road/Road2 overlap for paint guard and status label.
    /// Remains partial during migration — R6: depends on solver methods not yet extracted.</summary>
    public sealed partial class VisualRoadBuilderTool
    {
        private bool _overlapHint; // hover / lượt vẽ vừa rồi bị chồng → brush đỏ + nhãn "Overlapping"

        // Cache cờ "layout đang mở đã chồng sẵn" theo signature canvas.
        private int _layoutOverlapSig;
        private bool _layoutOverlapSigValid;
        private bool _layoutOverlapCached;

        /// <summary>Có chồng HW↔HW (2 mảnh phủ chung eighth) hoặc HW↔Road (mảnh road nằm trong mảnh HW)?
        /// Road2 (layer 3, D10) cộng thêm 3 vế: Road2↔Highway, Road2↔Road(type-1) — khung Road2 PHÓNG
        /// margin, so với khung HW/Road GỐC (không phóng, margin chỉ tính 1 lần/cặp về phía Road2) — và
        /// Road2↔Road2 tự chồng (khung THẬT, không phóng, y hệt cách HasRoadSelfOverlap đo type-1).</summary>
        private bool HasAnyOverlap()
        {
            var hwQ = new HashSet<long>();
            foreach ((float cx, float cy, bool horiz) in CollectHighwayColumns(null))
            {
                (int x0, int y0, int x1, int y1) = HighwayColumnEighthBox(cx, cy, horiz);
                for (int ex = x0; ex < x1; ex++)
                    for (int ey = y0; ey < y1; ey++)
                        if (!hwQ.Add(QKey(ex, ey))) return true; // HW↔HW
            }

            foreach ((int rx2, int ry2, int mask) in RoadLatticeNodes(_edges))
                if (BoxHits(hwQ, RoadEighthBox(mask, LatticeToEighth(rx2), LatticeToEighth(ry2))))
                    return true; // HW↔Road

            if (HasRoadSelfOverlap(_edges)) return true;

            var roadQ = BuildOccupancy(OccupancyMode.Road);
            foreach ((int rx2, int ry2, int mask) in RoadLatticeNodes(_road2Edges))
            {
                var box = InflateBox(Road2EighthBox(mask, LatticeToEighth(rx2), LatticeToEighth(ry2)),
                    Road2MarginEighths);
                if (BoxHits(hwQ, box)) return true;
                if (BoxHits(roadQ, box)) return true;
            }

            return HasRoadSelfOverlap(_road2Edges);
        }

        /// <summary>Layout ĐANG mở đã chồng sẵn chưa (chưa tính nét sắp vẽ)?</summary>
        private bool LayoutAlreadyOverlaps()
        {
            int sig = ComputeCanvasSignature();
            if (_layoutOverlapSigValid && sig == _layoutOverlapSig) return _layoutOverlapCached;
            _layoutOverlapSig = sig;
            _layoutOverlapSigValid = true;
            _layoutOverlapCached = HasAnyOverlap();
            return _layoutOverlapCached;
        }

        /// <summary>2 mảnh road chiếm CHUNG một ô modular 0.5×0.5.</summary>
        private bool HasRoadSelfOverlap(List<int> edges)
        {
            var owner = new Dictionary<long, long>();
            bool overlap = false;
            ForEachRoadTileCell(edges, (tile, ex, ey) =>
            {
                if (overlap) return;
                long k = QKey(ex, ey);
                if (owner.TryGetValue(k, out long other)) overlap = other != tile;
                else owner[k] = tile;
            });
            return overlap;
        }

        /// <summary>Định danh một ô modular: cùng loại + cùng vị trí + cùng yaw ⇒ cùng khoá.</summary>
        private static long TileIdentity(RoadTilePart part, float x, float y, float yaw)
        {
            long qx = Mathf.RoundToInt(x * EighthsPerCell) + 4096;
            long qy = Mathf.RoundToInt(y * EighthsPerCell) + 4096;
            return (((qx << 16) | qy) << 8) | ((long)(Mathf.RoundToInt(yaw / 90f) & 3) << 4) | (long)part;
        }

        /// <summary>Duyệt ĐÚNG các ô modular 0.5×0.5 (toạ độ eighth) mà lớp Đường chiếm thật.</summary>
        private void ForEachRoadTileCell(List<int> edges, System.Action<long, int, int> cell)
        {
            int lw = LatticeW, lh = LatticeH;
            RoadLayout layout = ResolveRoadLayout(edges, null);
            int[] masks = layout.Masks;
            HashSet<int> replacedByHalf = layout.ReplacedByHalf;

            void Tile(RoadTilePart part, float x, float y, float yaw)
            {
                if (!TryTileLocalRect(part, out float lx, out float ly, out float half)) return;
                (float ox, float oy) = RotateCellsCW(lx, ly, Mathf.RoundToInt(yaw / 90f));
                int ex0 = Mathf.RoundToInt((x + ox - half) * EighthsPerCell);
                int ey0 = Mathf.RoundToInt((y + oy - half) * EighthsPerCell);
                int span = Mathf.RoundToInt(half * 2f * EighthsPerCell);
                long tile = TileIdentity(part, x, y, yaw);
                for (int ex = ex0; ex < ex0 + span; ex++)
                    for (int ey = ey0; ey < ey0 + span; ey++)
                        cell(tile, ex, ey);
            }

            void Straight(float x, float y, float yaw, bool fullCell) =>
                ForEachStraightTile(x, y, yaw, fullCell,
                    (tx, ty, tyaw) => Tile(RoadTilePart.Side, tx, ty, tyaw));

            for (int y2 = 0; y2 < lh; y2++)
                for (int x2 = 0; x2 < lw; x2++)
                {
                    int i = y2 * lw + x2;
                    int mask = masks[i];
                    if (mask == 0 || layout.Skip(i)) continue;

                    if (IsStraightLikeMask(mask))
                    {
                        (float ax, float ay, bool full) = StraightAnchorFor(layout, i, mask, x2, y2);
                        Straight(ax, ay, StraightYaw(mask), full);
                        continue;
                    }

                    ForEachJunctionTile(mask, JunctionArms(masks, x2, y2, mask),
                        (part, dx, dy, yaw) => Tile(part, x2 * 0.5f + dx, y2 * 0.5f + dy, yaw),
                        null, FilletTurnProbe(layout, x2 * 0.5f, y2 * 0.5f));
                    ForEachHalfStraight(mask, x2, y2, masks, replacedByHalf, null,
                        (hx, hy, hyaw, _) => Straight(hx, hy, hyaw, false));
                }
        }

        /// <summary>Hover hiện tại (theo lớp đang vẽ) đè lên lớp KIA?</summary>
        private bool HoverWouldOverlap()
        {
            if (!_hoverCellValid || _mode != PaintMode.Road || _eraserMode || _selectMode) return false;

            if (_edgeLayer == 0)
            {
                float hx = Mathf.Clamp(Mathf.RoundToInt(_hoverCell.x * 2f) * 0.5f, 0f, _gridWidth - 1);
                float hy = Mathf.Clamp(Mathf.RoundToInt(_hoverCell.y * 2f) * 0.5f, 0f, _gridHeight - 1);
                int cx2 = Mathf.RoundToInt(hx * 2f);
                int cy2 = Mathf.RoundToInt(hy * 2f);

                var ghost = RoadEighthBox(DirE, LatticeToEighth(cx2), LatticeToEighth(cy2));
                if (BoxHits(BuildOccupancy(OccupancyMode.Highway), ghost)) return true;
                return BoxHits(BuildOccupancy(OccupancyMode.Road2), InflateBox(ghost, Road2MarginEighths));
            }
            if (_edgeLayer == 1)
            {
                int cx2 = Mathf.Clamp(Mathf.RoundToInt(_hoverCell.x * 2f), 0, (_gridWidth - 1) * 2);
                int cy2 = Mathf.Clamp(Mathf.RoundToInt(_hoverCell.y * 2f), 0, (_gridHeight - 1) * 2);

                var ghost = HighwayEighthBox(LatticeToEighth(cx2), LatticeToEighth(cy2));
                if (BoxHits(BuildOccupancy(OccupancyMode.Road), ghost)) return true;
                return BoxHits(BuildOccupancy(OccupancyMode.Road2), InflateBox(ghost, Road2MarginEighths));
            }
            if (_edgeLayer == 3)
            {
                float hx = Mathf.Clamp(Mathf.RoundToInt(_hoverCell.x * 2f) * 0.5f, 0f, _gridWidth - 1);
                float hy = Mathf.Clamp(Mathf.RoundToInt(_hoverCell.y * 2f) * 0.5f, 0f, _gridHeight - 1);
                int cx2 = Mathf.RoundToInt(hx * 2f);
                int cy2 = Mathf.RoundToInt(hy * 2f);

                var ghost = Road2EighthBox(DirE, LatticeToEighth(cx2), LatticeToEighth(cy2));
                var inflatedGhost = InflateBox(ghost, Road2MarginEighths);
                if (BoxHits(BuildOccupancy(OccupancyMode.Highway), inflatedGhost)) return true;
                if (BoxHits(BuildOccupancy(OccupancyMode.Road), inflatedGhost)) return true;
                // Tự chồng Road2↔Road2: khung THẬT, không phóng (giống Road↔Road ở HasAnyOverlap).
                return BoxHits(BuildOccupancy(OccupancyMode.Road2), ghost);
            }
            return false; // hw-decor: ngoài scope
        }

        private enum OccupancyMode { Highway, Road, Road2 }

        private HashSet<long> BuildOccupancy(OccupancyMode mode)
        {
            var q = new HashSet<long>();
            switch (mode)
            {
                case OccupancyMode.Highway:
                    foreach ((float cx, float cy, bool horiz) in CollectHighwayColumns(null))
                        AddBox(q, HighwayColumnEighthBox(cx, cy, horiz));
                    break;
                case OccupancyMode.Road:
                    foreach ((int rx2, int ry2, int mask) in RoadLatticeNodes(_edges))
                        AddBox(q, RoadEighthBox(mask, LatticeToEighth(rx2), LatticeToEighth(ry2)));
                    break;
                case OccupancyMode.Road2:
                    foreach ((int rx2, int ry2, int mask) in RoadLatticeNodes(_road2Edges))
                        AddBox(q, Road2EighthBox(mask, LatticeToEighth(rx2), LatticeToEighth(ry2)));
                    break;
            }
            return q;
        }

        /// <summary>Điểm lattice (nửa ô) có mảnh road trên edges, kèm mask.</summary>
        private IEnumerable<(int x2, int y2, int mask)> RoadLatticeNodes(List<int> edges)
        {
            int[] masks = BuildMasks(edges);
            int lw = LatticeW, lh = LatticeH;
            for (int y2 = 0; y2 < lh; y2++)
                for (int x2 = 0; x2 < lw; x2++)
                {
                    int mask = masks[y2 * lw + x2];
                    if (mask != 0) yield return (x2, y2, mask);
                }
        }
    }
}
#endif
