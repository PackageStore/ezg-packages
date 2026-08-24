#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Collects parking apron + kerb-free placements for both road types, parameterized
    /// by <see cref="BlockLayerDesc"/> (R4). Merges the old <c>CollectParkingRoadKerb</c> /
    /// <c>CollectParkingRoad2Kerb</c> pair.</summary>
    internal sealed class ParkingKerbCollector
    {
        private readonly RoadCanvasDoc _doc;

        internal ParkingKerbCollector(RoadCanvasDoc doc) { _doc = doc; }

        /// <summary>Parking option-a: prefab chỉ chứa slab, tool trải apron modular từ tim đường tới
        /// mặt khối — y hệt station (dùng chung <see cref="BlockApronWalker.ForEachStationFrontTile"/>).
        /// Merged road/road2 via <paramref name="layer"/> (R4).
        /// <paramref name="log"/> false = im lặng cho preview canvas.</summary>
        internal void CollectParkingRoadKerb(
            IReadOnlyList<int> parkings, int[] roadMasks, BlockSuppression suppressed,
            List<(RoadTilePart part, float x, float y, float yaw)> parkingRoads,
            List<BlockStrip> strips, BlockRoadSkin skin, HashSet<string> missing, bool log,
            BlockLayerDesc layer, StationRoadCollector stationCollector,
            HashSet<int> connected = null)
        {
            if (parkings.Count == 0) return;
            int clearSteps = layer.ParkingClearSteps;
            float[] apronDepths = layer.ParkingApronDepths;
            float outerFillet = layer.ParkingFilletDepth;
            int lw = _doc.LatticeW;

            foreach (int id in parkings)
            {
                BlockCodec.DecodeParking(id, out int ax2, out int ay2, out int rot);
                Vector2Int k = GridConst.ParkingCells(rot);
                bool horizontal = rot == 0 || rot == 2;
                int spanCells = horizontal ? k.x : k.y;

                int faceEdge2 = rot switch
                {
                    0 => ay2 + k.y * 2,
                    2 => ay2,
                    1 => ax2 + k.x * 2,
                    _ => ax2,
                };
                int outward = rot == 0 || rot == 1 ? 1 : -1;
                int pivotLine2 = faceEdge2 + outward * clearSteps;
                int p02 = horizontal ? ax2 : ay2;

                int line2 = pivotLine2;
                if (stationCollector.StationFaceCovered(roadMasks, line2, p02, spanCells, horizontal))
                {
                    connected?.Add(id);
                    if (missing != null && !stationCollector.AddStationRoadMissing(missing)) return;

                    BlockApronWalker.ForEachStationFrontTile(line2, p02, rot, spanCells, apronDepths, outerFillet, false,
                        (part, tx, ty, tyaw) =>
                    {
                        parkingRoads.Add((part, tx, ty, tyaw));
                        if (part == RoadTilePart.Center)
                            skin?.AddPlainColumn(horizontal, line2, horizontal ? tx : ty);
                    });

                    int span2 = spanCells * 2;
                    BlockSide.SuppressBlockRoadStrip(roadMasks, suppressed, line2, p02 - 1, p02 + span2 + 1,
                        horizontal, BlockSide.Side(rot), strips, skin,
                        lw, _doc.GridWidth, _doc.GridHeight);
                }

                // Vỉa hè trên dải mặt khối: bỏ từ mép mặt ra tới pivot (bất kể có road hay không)
                if (skin != null)
                {
                    int lo2 = horizontal ? ax2 : ay2;
                    int hi2 = lo2 + (horizontal ? k.x : k.y) * 2;
                    int side = BlockSide.Side(rot);
                    int lineMax2 = (horizontal ? _doc.GridHeight - 1 : _doc.GridWidth - 1) * 2;
                    for (int d = 0; d <= clearSteps; d++)
                    {
                        int kerbLine2 = faceEdge2 + outward * d;
                        if (kerbLine2 < 0 || kerbLine2 > lineMax2) continue;
                        skin.AddKerbFree(new BlockStrip(horizontal, kerbLine2, lo2 - 1, hi2 + 1, side));
                    }
                }
            }

            if (log)
                Debug.Log($"[VisualRoadBuilder] Parking-{layer.Label}: {parkings.Count} khối ghép apron + bỏ vỉa hè.");
        }
    }
}
#endif
