#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Collects station apron placements for both road types, parameterized by
    /// <see cref="BlockLayerDesc"/> (R4). Merges the old <c>CollectStationRoadPlacements</c> /
    /// <c>CollectStationRoad2Placements</c> pair.</summary>
    internal sealed class StationRoadCollector
    {
        private readonly RoadCanvasDoc _doc;
        private readonly RoadPartLibrary _library;

        internal StationRoadCollector(RoadCanvasDoc doc, RoadPartLibrary library)
        {
            _doc = doc;
            _library = library;
        }

        /// <summary>Cả dải s+1 điểm dọc line2 (mặt station rộng s ô) đều là đường? Điểm có mask road
        /// HOẶC nằm giữa 2 điểm road nối qua.</summary>
        internal bool StationFaceCovered(int[] roadMasks, int line2, int p02, int spanCells, bool horizontal)
        {
            int lw = _doc.LatticeW;
            int s2 = spanCells * 2;
            int lineMax2 = (horizontal ? _doc.GridHeight - 1 : _doc.GridWidth - 1) * 2;
            if (line2 < 0 || line2 > lineMax2) return false;
            int lateralMax2 = (horizontal ? _doc.GridWidth - 1 : _doc.GridHeight - 1) * 2;
            if (p02 < 0 || p02 + s2 > lateralMax2) return false;
            int dirFwd = horizontal ? DirBits.E : DirBits.N;
            int dirBack = horizontal ? DirBits.W : DirBits.S;
            for (int q2 = p02; q2 <= p02 + s2; q2++)
            {
                int idx = horizontal ? line2 * lw + q2 : q2 * lw + line2;
                if (roadMasks[idx] != 0) continue;

                bool covered = false;
                if (q2 - 1 >= 0 && q2 + 1 <= lateralMax2)
                {
                    int iPrev = horizontal ? line2 * lw + q2 - 1 : (q2 - 1) * lw + line2;
                    int iNext = horizontal ? line2 * lw + q2 + 1 : (q2 + 1) * lw + line2;
                    covered = (roadMasks[iPrev] & dirFwd) != 0 && (roadMasks[iNext] & dirBack) != 0;
                }
                if (!covered) return false;
            }
            return true;
        }

        /// <summary>Library có đủ ô modular mà mảnh đường trước mặt station cần chưa (dùng lại bộ ô của
        /// mảnh giao, TRỪ turn) — thiếu thì ghi tên ô vào <paramref name="missing"/> và trả false.</summary>
        internal bool AddStationRoadMissing(HashSet<string> missing)
        {
            int before = missing.Count;
            if (_library == null || _library.road1x1_side == null) missing.Add("Road Tile Side");
            if (_library == null || _library.road1x1_side_rim == null) missing.Add("Road Tile Side Rim");
            if (_library == null || _library.road1x1_curve == null) missing.Add("Road Tile Curve");
            if (_library == null || _library.road1x1_curve_rim == null) missing.Add("Road Tile Curve Rim");
            if (_library == null || _library.road1x1_center == null) missing.Add("Road Tile Center");
            return missing.Count == before;
        }

        /// <summary>Dò đường TRƯỚC MẶT mỗi station tại hàng CHẤM PIVOT (cách mép mặt 1 ô theo hướng
        /// mặt — khớp <c>StationPivotCell</c> + chấm pivot vẽ trên canvas): đặt station sao cho
        /// chấm pivot nằm trên đường → nối. Cả dải phủ road → bỏ mảnh road nội bộ trong clearance rồi
        /// ghép mảnh đường trước mặt station bằng ô modular.
        /// Merged road/road2 via <paramref name="layer"/> (R4) — replaces the old
        /// <c>CollectStationRoadPlacements</c> / <c>CollectStationRoad2Placements</c> pair.
        /// <paramref name="missing"/> null = chế độ preview (không cần Part Library),
        /// <paramref name="log"/> false = im lặng vì canvas gọi lại mỗi lần repaint.</summary>
        internal void CollectStationRoadPlacements(
            IReadOnlyList<int> stations, int[] roadMasks, BlockSuppression suppressed,
            List<(RoadTilePart part, float x, float y, float yaw)> stationRoads,
            List<BlockStrip> strips, BlockRoadSkin skin, HashSet<string> missing, bool log,
            BlockLayerDesc layer, HashSet<int> connected = null)
        {
            if (stations.Count == 0) return;
            int s = GridConst.StationSize;
            if (s % 2 != 0)
            {
                if (log) Debug.LogWarning("[VisualRoadBuilder] Station size lẻ — bỏ qua đường trước mặt station.");
                return;
            }

            int clearSteps = layer.StationClearSteps;
            float[] apronDepths = layer.StationApronDepths;
            float filletDepth = layer.StationFilletDepth;
            int lw = _doc.LatticeW;

            foreach (int id in stations)
            {
                BlockCodec.DecodeStation(id, out int ax2, out int ay2, out int rot);
                int s2 = s * 2;
                bool horizontal = rot == 0 || rot == 2;

                int pivotLine2 = rot switch
                {
                    0 => ay2 + s2 + clearSteps,
                    2 => ay2 - clearSteps,
                    1 => ax2 + s2 + clearSteps,
                    _ => ax2 - clearSteps,
                };
                int p02 = horizontal ? ax2 : ay2;

                int line2 = pivotLine2;
                if (!StationFaceCovered(roadMasks, line2, p02, s, horizontal))
                    continue;

                connected?.Add(id);
                if (missing != null && !AddStationRoadMissing(missing)) return;

                BlockApronWalker.ForEachStationFrontTile(line2, p02, rot, s,
                    apronDepths, filletDepth, true,
                    (part, tx, ty, tyaw) =>
                {
                    stationRoads.Add((part, tx, ty, tyaw));
                    if (part == RoadTilePart.Center)
                        skin?.AddPlainColumn(horizontal, line2, horizontal ? tx : ty);
                });

                BlockSide.SuppressBlockRoadStrip(roadMasks, suppressed, line2, p02 - 1, p02 + s2 + 1,
                    horizontal, BlockSide.Side(rot), strips, skin,
                    lw, _doc.GridWidth, _doc.GridHeight);

                if (log)
                {
                    float p0 = p02 * 0.5f;
                    Debug.Log($"[VisualRoadBuilder] Station-{layer.Label} hàng {(horizontal ? "y" : "x")}" +
                              $"={line2 * 0.5f}: ghép ô modular, phủ [{p0 - 0.5f}..{p0 + s + 0.5f}]");
                }
            }
        }
    }
}
#endif
