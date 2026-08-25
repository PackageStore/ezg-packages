#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    // ── SHIM LAYER 03 ─────────────────────────────────────────────────────────
    // Forwarders for methods extracted by slice 03 (block/highway/path/apply).
    // Integration deletes this file once every slice has migrated.
    // ───────────────────────────────────────────────────────────────────────────
    public sealed partial class VisualRoadBuilderTool
    {
        // ── Block solver shims ────────────────────────────────────────────────

        // Phương thức cũ cùng tên với class BlockSide → inline logic tránh conflict tên.
        private static int BlockSide(int rot) =>
            rot switch { 0 => DirS, 1 => DirW, 2 => DirN, _ => DirE };

        private void CollectStationRoadPlacements(
            IReadOnlyList<int> stations, int[] roadMasks, BlockSuppression suppressed,
            List<(RoadTilePart part, float x, float y, float yaw)> stationRoads,
            List<BlockStrip> strips, BlockRoadSkin skin, HashSet<string> missing, bool log) =>
            new StationRoadCollector(_doc, _library).CollectStationRoadPlacements(
                stations, roadMasks, suppressed, stationRoads, strips, skin, missing, log, BlockLayerDesc.Road);

        private void CollectStationRoad2Placements(
            IReadOnlyList<int> stations, int[] road2Masks, BlockSuppression suppressed2,
            List<(RoadTilePart part, float x, float y, float yaw)> station2Roads,
            List<BlockStrip> strips2, BlockRoadSkin skin2, HashSet<string> missing, bool log,
            HashSet<int> connected = null) =>
            new StationRoadCollector(_doc, _library).CollectStationRoadPlacements(
                stations, road2Masks, suppressed2, station2Roads, strips2, skin2, missing, log, BlockLayerDesc.Road2, connected);

        private void CollectParkingRoadKerb(
            IReadOnlyList<int> parkings, int[] roadMasks, BlockSuppression suppressed,
            List<(RoadTilePart part, float x, float y, float yaw)> parkingRoads,
            List<BlockStrip> strips, BlockRoadSkin skin, HashSet<string> missing, bool log) =>
            new ParkingKerbCollector(_doc).CollectParkingRoadKerb(
                parkings, roadMasks, suppressed, parkingRoads, strips, skin, missing, log,
                BlockLayerDesc.Road, new StationRoadCollector(_doc, _library));

        private void CollectParkingRoad2Kerb(
            IReadOnlyList<int> parkings, int[] road2Masks, BlockSuppression suppressed2,
            List<(RoadTilePart part, float x, float y, float yaw)> parking2Roads,
            List<BlockStrip> strips2, BlockRoadSkin skin2, HashSet<string> missing, bool log,
            HashSet<int> connected = null) =>
            new ParkingKerbCollector(_doc).CollectParkingRoadKerb(
                parkings, road2Masks, suppressed2, parking2Roads, strips2, skin2, missing, log,
                BlockLayerDesc.Road2, new StationRoadCollector(_doc, _library), connected);

        private static void ApplyApronPlain(
            List<(RoadTilePart part, float x, float y, float yaw)> stationRoads, BlockRoadSkin skin) =>
            global::EZG.TechnicalArt.VisualRoadBuilder.BlockSide.ApplyApronPlain(
                stationRoads, skin, StraightTileEmitter.PlainCoreCell);

        private void CollectBlockEdgeFills(
            int[] roadMasks, List<BlockStrip> strips,
            List<(float x, float y, float yaw, int sides)> halves,
            List<(float x, float y, float yaw, int sides)> fulls) =>
            new BlockEdgeFiller(_doc).CollectBlockEdgeFills(roadMasks, strips, halves, fulls);

        private bool StationFaceCovered(int[] roadMasks, int line2, int p02, int spanCells, bool horizontal) =>
            new StationRoadCollector(_doc, _library).StationFaceCovered(roadMasks, line2, p02, spanCells, horizontal);

        // ── Highway solver shims ──────────────────────────────────────────────

        private List<(int x2, int y2, int stem, int hwMask)> CollectRampJunctions(int[] hwMasks, int[] roadMasks) =>
            new RampDetector(_doc).CollectRampJunctions(hwMasks, roadMasks);

        private bool TryRampRoadBridge(int x2, int y2, int stem, int[] roadMasks, HashSet<int> rampSuppress,
            out float bx, out float by, out float bYaw) =>
            new RampDetector(_doc).TryRampRoadBridge(x2, y2, stem, roadMasks, rampSuppress, out bx, out by, out bYaw);

        private bool RampFlipped(int x2, int y2) =>
            new RampDetector(_doc).RampFlipped(x2, y2);

        private static int RampAnchorKey(int x2, int y2) => EdgeCodec.RampAnchorKey(x2, y2);

        private static void DecodeRampAnchor(int key, out int x2, out int y2) =>
            EdgeCodec.DecodeRampAnchor(key, out x2, out y2);

        private static (bool horiz, int line2, int lo2, int hi2) RampHighwaySpan(int x2, int y2, int stem, bool flipped) =>
            RampDetector.RampHighwaySpan(x2, y2, stem, flipped);

        // ── Highway runs shims ────────────────────────────────────────────────

        private static void ForEachHighwayColumnTile(float cx, float cy, float yaw,
            System.Action<float, float, float> place) =>
            HighwayColumnSolver.ForEachHighwayColumnTile(cx, cy, yaw, place);

        private List<(float cx, float cy, bool horiz)> CollectHighwayColumns(
            List<(int x2, int y2, int stem, int hwMask)> ramps) =>
            new HighwayColumnSolver(_doc, _library, new RampDetector(_doc)).CollectHighwayColumns(ramps);

        private bool HighwayTilesReady =>
            _library != null && _library.hway1x2_side != null && _library.hway1x2_side_rim != null;

        // ── Path solver shims ─────────────────────────────────────────────────

        private void ForEachPathNode(
            List<int> edges, System.Action<PathTilePart, float, float, float> place) =>
            PathCollector.ForEachPathNode(_doc, edges, place);

        private void CollectPathPlacements(
            List<int> edges,
            List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)> placements,
            HashSet<string> missing) =>
            PathCollector.CollectPathPlacements(_doc, edges, new PathTileVocabulary(_library),
                placements, missing, DedupePlacements);

        private bool PathTilesReady => new PathTileVocabulary(_library).PathTilesReady;

        // ── Apply shim ───────────────────────────────────────────────────────

        private void Apply()
        {
            if (_library == null)
            {
                EditorUtility.DisplayDialog("Road Grid", "Chưa gán Part Library.", "OK");
                return;
            }
            var deps = new CollectAllDeps
            {
                CollectRoadPlacements = (edges, hwMasks, suppress, rampSup, placements, missing, skin) =>
                    CollectRoadPlacements(edges, hwMasks, suppress, rampSup, placements, missing, skin),
                CollectRoad2Placements = (edges, hwMasks, suppress, rampSup, placements, missing, skin) =>
                    CollectRoad2Placements(edges, hwMasks, suppress, rampSup, placements, missing, skin),
                JunctionTilePrefab = part => JunctionTilePrefab(part),
                AddStraightTiles = (list, x, y, yaw, full, missing, sides, skin) =>
                    AddStraightTiles(list, x, y, yaw, full, missing, sides, skin),
                AddRoad2StraightTiles = (list, x, y, yaw, full, missing, sides, skin) =>
                    AddRoad2StraightTiles(list, x, y, yaw, full, missing, sides, skin),
                DedupeIsolatedStraightKeys = list => DedupeIsolatedStraightKeys(list),
                DedupePlacements = (list, start) => DedupePlacements(list, start),
                AddRoad2ApronFillers = (list, strips, missing) => AddRoad2ApronFillers(list, strips, missing),
                PlainCoreCell = StraightTileEmitter.PlainCoreCell,
            };
            CollectResult result = CollectAll.Run(_doc, _library, deps);
            string path = LevelPrefabPath();
            string rpn = string.IsNullOrEmpty(_roadParentName) ? DefaultRoadParentName : _roadParentName;
            RoadBaker.Bake(result, _doc, _library, path, rpn,
                root => ApplyDecors(root),
                () => { SaveToSo(false); EditorGUIUtility.PingObject(_levelPrefab);
                    Debug.Log($"[VisualRoadBuilder] Đã ghi {result.Road.Count} road" +
                        $" + {result.Road2.Count} road2 + {result.Path.Count} path" +
                        $" + {result.Highway.Count} highway + {result.HwDecor.Count} hw-decor" +
                        $" + {_stations.Count} station + {_stations2.Count} station 2 + {_parkings.Count} parking vào '{_levelPrefab.name}/{rpn}'."); },
                rot => BlockFacingStep(rot),
                road2 => BlockPivotInsetFor(road2));
        }
    }
}
#endif
