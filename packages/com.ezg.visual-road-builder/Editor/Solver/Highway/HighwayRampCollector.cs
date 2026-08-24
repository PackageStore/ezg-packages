#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Collects highway junction placements (road and road2 ramps) and orchestrates the
    /// full highway placement pipeline.</summary>
    internal sealed class HighwayRampCollector
    {
        private readonly RoadCanvasDoc _doc;
        private readonly RoadPartLibrary _library;
        private readonly RampDetector _rampDetector;

        internal HighwayRampCollector(RoadCanvasDoc doc, RoadPartLibrary library, RampDetector rampDetector)
        {
            _doc = doc;
            _library = library;
            _rampDetector = rampDetector;
        }

        /// <summary>Trả <c>rampSuppressed2</c> (ô Road 2 dưới arm ramp hway_to_road2).
        /// <paramref name="addStraightTiles"/>: delegate to road solver's AddStraightTiles (still on
        /// partial class, cross-slice dependency).</summary>
        internal HashSet<int> CollectHighwayPlacements(
            int[] hwMasks, int[] roadMasks,
            HashSet<int> rampSuppressed,
            List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)> placements,
            List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)> roadPlacements,
            HashSet<string> missing,
            System.Action<List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)>,
                float, float, float, bool, HashSet<string>, int, BlockRoadSkin> addStraightTiles)
        {
            List<(int x2, int y2, int stem, int hwMask)> ramps = CollectHighwayJunctions(
                hwMasks, roadMasks, rampSuppressed, placements, roadPlacements, missing, addStraightTiles);

            int[] road2Masks = MaskBuilder.BuildLegacyMasksFromEdges(_doc.Road2Edges, _doc.LatticeW, _doc.LatticeH);
            var rampSuppressed2 = new HashSet<int>();
            List<(int x2, int y2, int stem, int hwMask)> ramps2 = CollectHighwayJunctionsRoad2(
                hwMasks, road2Masks, rampSuppressed2, placements, missing);

            var allRamps = new List<(int x2, int y2, int stem, int hwMask)>(ramps);
            allRamps.AddRange(ramps2);
            var columnSolver = new HighwayColumnSolver(_doc, _library, _rampDetector);
            columnSolver.CollectHighwayRuns(allRamps, placements, missing);

            return rampSuppressed2;
        }

        /// <summary>Pass 1: đặt ramp hway_to_road tại mỗi nút. Highway road vs road2 bridge geometry
        /// kept separate per D6/keep-separate list.</summary>
        private List<(int x2, int y2, int stem, int hwMask)> CollectHighwayJunctions(
            int[] hwMasks, int[] roadMasks, HashSet<int> rampSuppressed,
            List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)> placements,
            List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)> roadPlacements,
            HashSet<string> missing,
            System.Action<List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)>,
                float, float, float, bool, HashSet<string>, int, BlockRoadSkin> addStraightTiles)
        {
            List<(int x2, int y2, int stem, int hwMask)> ramps = _rampDetector.CollectRampJunctions(hwMasks, roadMasks);
            foreach ((int x2, int y2, int stem, int hwMask) in ramps)
            {
                if (_library.hway_to_road == null)
                {
                    missing.Add("Hway To Road");
                    continue;
                }
                // Lật gương (F) = scaleMul.x = -1 (mirror trục cao tốc trong local space TRƯỚC yaw)
                Vector3 scaleMul = _rampDetector.RampFlipped(x2, y2) ? new Vector3(-1f, 1f, 1f) : Vector3.one;
                placements.Add((x2 * 0.5f, y2 * 0.5f, _library.hway_to_road,
                    DirBits.SolveYaw(DirBits.E | DirBits.N | DirBits.W, hwMask | stem), scaleMul));

                if (_rampDetector.TryRampRoadBridge(x2, y2, stem, roadMasks, rampSuppressed,
                        out float bx, out float by, out float bYaw))
                    addStraightTiles(roadPlacements, bx, by, bYaw, false, missing, 0, null);
            }
            return ramps;
        }

        /// <summary>Bản mirror Road 2 của <see cref="CollectHighwayJunctions"/>: KHÔNG bắc cầu bridge tile
        /// — Road 2 rộng 3 ô, 1 mảnh cầu không đủ phủ (P4/00-overview.md).</summary>
        private List<(int x2, int y2, int stem, int hwMask)> CollectHighwayJunctionsRoad2(
            int[] hwMasks, int[] road2Masks, HashSet<int> rampSuppressed2,
            List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)> placements,
            HashSet<string> missing)
        {
            List<(int x2, int y2, int stem, int hwMask)> ramps2 = _rampDetector.CollectRampJunctions(hwMasks, road2Masks);
            foreach ((int x2, int y2, int stem, int hwMask) in ramps2)
            {
                if (_library.hway_to_road2 == null)
                {
                    missing.Add("Hway To Road2");
                    continue;
                }
                Vector3 scaleMul = _rampDetector.RampFlipped(x2, y2) ? new Vector3(-1f, 1f, 1f) : Vector3.one;
                placements.Add((x2 * 0.5f, y2 * 0.5f, _library.hway_to_road2,
                    DirBits.SolveYaw(DirBits.E | DirBits.N | DirBits.W, hwMask | stem), scaleMul));

                _rampDetector.TryRampRoadBridge(x2, y2, stem, road2Masks, rampSuppressed2, out _, out _, out _);
            }
            return ramps2;
        }
    }
}
#endif
