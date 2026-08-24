#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    using PlaceList = List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)>;
    using TileList = List<(RoadTilePart part, float x, float y, float yaw)>;

    /// <summary>Kết quả gom placement từ MỌI solver — bake, preview và debug boundary
    /// đều đọc từ ĐÂY thay vì tự gom lại.</summary>
    internal sealed class CollectResult
    {
        internal readonly PlaceList Road     = new();
        internal readonly PlaceList Road2    = new();
        internal readonly PlaceList Path     = new();
        internal readonly PlaceList Highway  = new();
        internal readonly PlaceList HwDecor  = new();

        internal readonly TileList StationRoads  = new();
        internal readonly TileList Station2Roads = new();
        internal readonly TileList ParkingRoads  = new();
        internal readonly TileList Parking2Roads = new();

        internal BlockSuppression Suppressed;
        internal BlockSuppression Suppressed2;
        internal BlockRoadSkin Skin;
        internal BlockRoadSkin Skin2;
        internal HashSet<int> RampSuppressed;
        internal HashSet<int> RampSuppressed2;
        internal HashSet<int> Road2Blocks;
        internal readonly HashSet<string> Missing = new();
    }

    /// <summary>Slice-02 methods not yet standalone — injected as delegates until extraction.</summary>
    internal struct CollectAllDeps
    {
        internal System.Action<List<int>, int[], BlockSuppression, HashSet<int>,
            PlaceList, HashSet<string>, BlockRoadSkin> CollectRoadPlacements;
        internal System.Action<List<int>, int[], BlockSuppression, HashSet<int>,
            PlaceList, HashSet<string>, BlockRoadSkin> CollectRoad2Placements;
        internal System.Func<RoadTilePart, GameObject> JunctionTilePrefab;
        internal System.Action<PlaceList, float, float, float, bool, HashSet<string>,
            int, BlockRoadSkin> AddStraightTiles;
        internal System.Action<PlaceList, float, float, float, bool, HashSet<string>,
            int, BlockRoadSkin> AddRoad2StraightTiles;
        internal System.Action<PlaceList> DedupeIsolatedStraightKeys;
        internal System.Action<PlaceList, int> DedupePlacements;
        internal System.Action<PlaceList, List<BlockStrip>, HashSet<string>> AddRoad2ApronFillers;
        internal System.Func<float, float, float, (float x, float y)> PlainCoreCell;
    }

    /// <summary>Single entry point running Apply's exact collect order; returns <see cref="CollectResult"/>.
    /// Replaces the three independent re-implementations (Apply, RoadSprites preview, DebugTab boundary).</summary>
    internal static class CollectAll
    {
        /// <summary>Run the EXACT collect order from Apply.cs:12-101. Order is load-bearing.</summary>
        internal static CollectResult Run(RoadCanvasDoc doc, RoadPartLibrary library, CollectAllDeps deps)
        {
            var r = new CollectResult();
            r.Suppressed = new BlockSuppression();
            r.Suppressed2 = new BlockSuppression();
            r.Skin = new BlockRoadSkin();
            r.Skin2 = new BlockRoadSkin();
            r.RampSuppressed = new HashSet<int>();
            r.Road2Blocks = new HashSet<int>();

            int[] roadMasks  = MaskBuilder.BuildMasks(doc.Edges, doc.LatticeW, doc.LatticeH);
            int[] road2Masks = MaskBuilder.BuildMasks(doc.Road2Edges, doc.LatticeW, doc.LatticeH);
            int[] hwMasks    = MaskBuilder.BuildMasks(doc.HighwayEdges, doc.LatticeW, doc.LatticeH);
            int[] roadMasksLegacy  = MaskBuilder.BuildLegacyMasksFromEdges(doc.Edges, doc.LatticeW, doc.LatticeH);
            int[] road2MasksLegacy = MaskBuilder.BuildLegacyMasksFromEdges(doc.Road2Edges, doc.LatticeW, doc.LatticeH);

            var strips = new List<BlockStrip>();
            var strips2 = new List<BlockStrip>();
            var blockEdgeHalves  = new List<(float x, float y, float yaw, int sides)>();
            var blockEdgeFulls   = new List<(float x, float y, float yaw, int sides)>();
            var blockEdgeHalves2 = new List<(float x, float y, float yaw, int sides)>();
            var blockEdgeFulls2  = new List<(float x, float y, float yaw, int sides)>();

            // 1–4. Block collectors: station + parking, road + road2
            var stationCollector = new StationRoadCollector(doc, library);
            stationCollector.CollectStationRoadPlacements(
                doc.Stations, roadMasks, r.Suppressed, r.StationRoads, strips, r.Skin, r.Missing, true, BlockLayerDesc.Road);
            stationCollector.CollectStationRoadPlacements(
                doc.Stations, road2Masks, r.Suppressed2, r.Station2Roads, strips2, r.Skin2, r.Missing, true, BlockLayerDesc.Road2, r.Road2Blocks);

            var parkingCollector = new ParkingKerbCollector(doc);
            parkingCollector.CollectParkingRoadKerb(
                doc.Parkings, roadMasks, r.Suppressed, r.ParkingRoads, strips, r.Skin, r.Missing, true, BlockLayerDesc.Road, stationCollector);
            parkingCollector.CollectParkingRoadKerb(
                doc.Parkings, road2Masks, r.Suppressed2, r.Parking2Roads, strips2, r.Skin2, r.Missing, true, BlockLayerDesc.Road2, stationCollector, r.Road2Blocks);

            // 5–6. Block edge fills
            var edgeFiller = new BlockEdgeFiller(doc);
            edgeFiller.CollectBlockEdgeFills(roadMasksLegacy, strips, blockEdgeHalves, blockEdgeFulls);
            edgeFiller.CollectBlockEdgeFills(road2MasksLegacy, strips2, blockEdgeHalves2, blockEdgeFulls2);

            // 7. Highway (returns rampSuppressed2)
            var rampDetector = new RampDetector(doc);
            var hwCollector = new HighwayRampCollector(doc, library, rampDetector);
            r.RampSuppressed2 = hwCollector.CollectHighwayPlacements(
                hwMasks, roadMasksLegacy, r.RampSuppressed, r.Highway, r.Road, r.Missing, deps.AddStraightTiles);

            // 8–10. Road2 → Road → Path (order is load-bearing)
            deps.CollectRoad2Placements(doc.Road2Edges, hwMasks, r.Suppressed2, r.RampSuppressed2, r.Road2, r.Missing, r.Skin2);
            deps.CollectRoadPlacements(doc.Edges, hwMasks, r.Suppressed, r.RampSuppressed, r.Road, r.Missing, r.Skin);
            var pathVocab = new PathTileVocabulary(library);
            PathCollector.CollectPathPlacements(doc, doc.PathEdges, pathVocab, r.Path, r.Missing, deps.DedupePlacements);

            // 11–14. Apron plain + push into road/road2 placements
            BlockSide.ApplyApronPlain(r.StationRoads, r.Skin, deps.PlainCoreCell);
            PushTiles(r.StationRoads, r.Road, deps.JunctionTilePrefab);
            BlockSide.ApplyApronPlain(r.Station2Roads, r.Skin2, deps.PlainCoreCell);
            PushTiles(r.Station2Roads, r.Road2, deps.JunctionTilePrefab);
            BlockSide.ApplyApronPlain(r.ParkingRoads, r.Skin, deps.PlainCoreCell);
            PushTiles(r.ParkingRoads, r.Road, deps.JunctionTilePrefab);
            BlockSide.ApplyApronPlain(r.Parking2Roads, r.Skin2, deps.PlainCoreCell);
            PushTiles(r.Parking2Roads, r.Road2, deps.JunctionTilePrefab);

            // 15–16. Edge half/full straight passes (road1)
            foreach ((float hx, float hy, float hyaw, int hSides) in blockEdgeHalves)
                deps.AddStraightTiles(r.Road, hx, hy, hyaw, false, r.Missing, hSides, r.Skin);
            foreach ((float fx, float fy, float fyaw, int fSides) in blockEdgeFulls)
                deps.AddStraightTiles(r.Road, fx, fy, fyaw, true, r.Missing, fSides, r.Skin);

            // 17. DedupeIsolatedStraightKeys (road1 only)
            deps.DedupeIsolatedStraightKeys(r.Road);

            // 18. Edge half/full straight passes (road2)
            foreach ((float hx, float hy, float hyaw, int hSides) in blockEdgeHalves2)
                deps.AddRoad2StraightTiles(r.Road2, hx, hy, hyaw, false, r.Missing, hSides, r.Skin2);
            foreach ((float fx, float fy, float fyaw, int fSides) in blockEdgeFulls2)
                deps.AddRoad2StraightTiles(r.Road2, fx, fy, fyaw, true, r.Missing, fSides, r.Skin2);

            // 19. Road2 apron fillers
            deps.AddRoad2ApronFillers(r.Road2, strips2, r.Missing);

            return r;
        }

        private static void PushTiles(TileList tiles, PlaceList placements, System.Func<RoadTilePart, GameObject> tilePrefab)
        {
            foreach ((RoadTilePart part, float x, float y, float yaw) in tiles)
                placements.Add((x, y, tilePrefab(part), yaw, Vector3.one));
        }
    }
}
#endif
