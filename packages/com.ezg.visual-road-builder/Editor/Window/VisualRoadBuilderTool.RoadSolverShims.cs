#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    // ── SHIM LAYER — Road Solver / Layout / Road 2 Solver ───────────────────────
    // Delegating forwarders so the not-yet-migrated partial files (Apply, RoadSprites,
    // DebugTab, HighwaySolver, BlockSolver, PathSolver) keep compiling after the three
    // source files (RoadSolver, RoadLayout, Road2Solver) are deleted.
    // Integration (slice 03+) deletes this file as each caller migrates.
    // ─────────────────────────────────────────────────────────────────────────────
    public sealed partial class VisualRoadBuilderTool
    {
        // ── Lazy ToolContext + extracted-class instances ─────────────────────────
        private ToolContext _toolCtx;
        private ToolContext EnsureToolCtx() => _toolCtx ??= new ToolContext(_doc, _library, _view, this);

        private RoadLayoutResolver _roadLayoutResolver;
        private RoadLayoutResolver EnsureRoadLayoutResolver() =>
            _roadLayoutResolver ??= new RoadLayoutResolver(EnsureToolCtx());

        private RoadStraightAnchor _roadStraightAnchor;
        private RoadStraightAnchor EnsureRoadStraightAnchor() =>
            _roadStraightAnchor ??= new RoadStraightAnchor(EnsureToolCtx());

        private StraightBaker _straightBaker;
        private StraightBaker EnsureStraightBaker() =>
            _straightBaker ??= new StraightBaker(EnsureToolCtx());

        private JunctionBaker _junctionBaker;
        private JunctionBaker EnsureJunctionBaker() =>
            _junctionBaker ??= new JunctionBaker(EnsureToolCtx());

        private HalfStraightEmitter _halfEmitter;
        private HalfStraightEmitter EnsureHalfEmitter() =>
            _halfEmitter ??= new HalfStraightEmitter(EnsureToolCtx());

        private RoadCollector _roadCollector;
        private RoadCollector EnsureRoadCollector() =>
            _roadCollector ??= new RoadCollector(EnsureToolCtx());

        private Road2Collector _road2Collector;
        private Road2Collector EnsureRoad2Collector() =>
            _road2Collector ??= new Road2Collector(EnsureToolCtx());

        private Road2StraightBaker _road2StraightBaker;
        private Road2StraightBaker EnsureRoad2StraightBaker() =>
            _road2StraightBaker ??= new Road2StraightBaker(EnsureToolCtx());

        private Road2JunctionBaker _road2JunctionBaker;
        private Road2JunctionBaker EnsureRoad2JunctionBaker() =>
            _road2JunctionBaker ??= new Road2JunctionBaker(EnsureToolCtx());

        private Road2JunctionEffects _road2JunctionEffects;
        private Road2JunctionEffects EnsureRoad2JunctionEffects() =>
            _road2JunctionEffects ??= new Road2JunctionEffects(EnsureToolCtx());

        private Road2ApronFiller _road2ApronFiller;
        private Road2ApronFiller EnsureRoad2ApronFiller() =>
            _road2ApronFiller ??= new Road2ApronFiller(EnsureToolCtx());

        // ── Const shims ─────────────────────────────────────────────────────────
        private const float RoadTileColumnOffsetCells = StraightTileEmitter.RoadTileColumnOffsetCells;
        private const int SideBranchReachSteps = RoadLayoutResolver.SideBranchReachSteps;
        private const int Road2SideBranchReachSteps = Road2Constants.Road2SideBranchReachSteps;

        // ── DirBits static shims ────────────────────────────────────────────────
        private static int OppositeDir(int dir) => DirBits.OppositeDir(dir);
        private static float RimYawFacing(int dir) => DirBits.RimYawFacing(dir);
        private static int RimRunPlusDir(int closed) => DirBits.RimRunPlusDir(closed);
        private static (int x, int y) DirStep(int dir) => DirBits.DirStep(dir);
        private static (float x, float y) RotateCellsCW(float x, float y, int turns) =>
            DirBits.RotateCellsCW(x, y, turns);
        private static bool IsJunctionMask(int m) => DirBits.IsJunctionMask(m);
        private static bool IsArcCoreMask(int m) => DirBits.IsArcCoreMask(m);
        private static bool IsStraightLikeMask(int m) => DirBits.IsStraightLikeMask(m);

        // ── LatticeKeys static shims ────────────────────────────────────────────
        private static long CurveKey(float x, float y, float yaw) =>
            LatticeKeys.CurveKey(x, y, yaw);
        private static long KerbCellKey(float x, float y, float lx, float ly, float yaw) =>
            LatticeKeys.KerbCellKey(x, y, lx, ly, yaw);
        private static (float x, float y) QuarterCellCenter(
            (float x, float y, float yaw) curve) =>
            LatticeKeys.QuarterCellCenter(curve);
        private static long Road2FillerKey(float x, float y) =>
            LatticeKeys.Road2FillerKey(x, y);

        // ── StraightTileEmitter static shims ────────────────────────────────────
        private static (float x, float y, bool fullCell) StraightAnchor(int mask, int x2, int y2) =>
            StraightTileEmitter.StraightAnchor(mask, x2, y2);
        private static (float x, float y) PlainCoreCell(float x, float y, float yaw) =>
            StraightTileEmitter.PlainCoreCell(x, y, yaw);
        private void ForEachStraightTile(
            float x, float y, float yaw, bool fullCell, System.Action<float, float, float> place,
            int sides = DirBits.All, float columnOffset = StraightTileEmitter.RoadTileColumnOffsetCells) =>
            StraightTileEmitter.ForEachStraightTile(x, y, yaw, fullCell, place, sides, columnOffset);

        // ── JunctionTileEmitter static shim ─────────────────────────────────────
        private static void ForEachJunctionTile(
            int mask, int junctionArms, System.Action<RoadTilePart, float, float, float> place,
            System.Func<float, float, float, bool> rimBlocked = null,
            System.Func<float, float, float, (float x, float y, float yaw)?> filletTurn = null) =>
            JunctionTileEmitter.ForEachJunctionTile(mask, junctionArms, place, rimBlocked, filletTurn);

        // ── DedupePlacement static shims ────────────────────────────────────────
        private static (GameObject, int, int, int) PlacementKey(
            (float x, float y, GameObject prefab, float yaw, Vector3 scaleMul) p) =>
            DedupePlacement.Key(p);
        private static void DedupePlacements(
            List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)> placements,
            int start) =>
            DedupePlacement.Dedupe(placements, start);

        // ── JunctionBaker static shims ──────────────────────────────────────────
        private static System.Func<float, float, float, bool> RimBlockedProbe(
            RoadLayout layout, BlockRoadSkin skin, float nx, float ny) =>
            JunctionBaker.RimBlockedProbe(layout, skin, nx, ny);
        private static System.Func<float, float, float, (float x, float y, float yaw)?> FilletTurnProbe(
            RoadLayout layout, float nx, float ny) =>
            JunctionBaker.FilletTurnProbe(layout, nx, ny);

        // ── Road2 emitter static shims ──────────────────────────────────────────
        private void ForEachRoad2StraightPart(
            float x, float y, float yaw, bool fullCell, int sides, BlockRoadSkin skin,
            System.Action<Road2TilePart, float, float, float> place,
            System.Func<float, float, float, bool> rimCovered = null,
            float skinX = 0f, float skinY = 0f) =>
            Road2StraightEmitter.ForEachRoad2StraightPart(
                x, y, yaw, fullCell, sides, skin, place, rimCovered, skinX, skinY);
        private void ForEachRoad2JunctionTile(
            int mask, int junctionArms, System.Action<Road2TilePart, float, float, float> place,
            System.Func<float, float, float, bool> rimCovered = null,
            System.Func<float, float, float, (float x, float y, float yaw)?> filletTurn = null,
            BlockRoadSkin skin = null, float skinX = 0f, float skinY = 0f, int sides = DirBits.All) =>
            Road2JunctionEmitter.ForEachRoad2JunctionTile(
                mask, junctionArms, place, rimCovered, filletTurn, skin, skinX, skinY, sides);

        // ── Instance forwarders — road layout + straight anchor ─────────────────
        private RoadLayout ResolveRoadLayout(
            List<int> edges, System.Func<int, bool> blocked,
            int sideBranchReach = RoadLayoutResolver.SideBranchReachSteps,
            bool bridgeSingleAxisBranch = false) =>
            EnsureRoadLayoutResolver().ResolveRoadLayout(
                edges, blocked, sideBranchReach, bridgeSingleAxisBranch);

        private (float x, float y, bool fullCell) StraightAnchorFor(
            RoadLayout layout, int idx, int mask, int x2, int y2) =>
            EnsureRoadStraightAnchor().StraightAnchorFor(layout, idx, mask, x2, y2);

        // ── Instance forwarders — type-1 bakers ─────────────────────────────────
        private void AddStraightTiles(
            List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)> placements,
            float x, float y, float yaw, bool fullCell, HashSet<string> missing,
            int sides = DirBits.All, BlockRoadSkin skin = null, bool noRim = false) =>
            EnsureStraightBaker().AddStraightTiles(
                placements, x, y, yaw, fullCell, missing, sides, skin, noRim);

        private int JunctionArms(int[] masks, int x2, int y2, int mask) =>
            EnsureJunctionBaker().JunctionArms(masks, x2, y2, mask);

        private bool AddJunctionTiles(
            List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)> placements,
            float x, float y, int mask, int junctionArms, HashSet<string> missing,
            RoadLayout layout = null, BlockRoadSkin skin = null) =>
            EnsureJunctionBaker().AddJunctionTiles(
                placements, x, y, mask, junctionArms, missing, layout, skin);

        private GameObject JunctionTilePrefab(RoadTilePart part) =>
            EnsureJunctionBaker().JunctionTilePrefab(part);

        private void ForEachHalfStraight(
            int mask, int x2, int y2, int[] masks, HashSet<int> replacedByHalf,
            BlockSuppression suppressed, System.Action<float, float, float, int> place) =>
            EnsureHalfEmitter().ForEachHalfStraight(
                mask, x2, y2, masks, replacedByHalf, suppressed, place);

        // ── Instance forwarders — road collector + isolated-key bridge ───────────
        private HashSet<(GameObject, int, int, int)> _isolatedStraightKeys;

        private void CollectRoadPlacements(
            List<int> edges, int[] hwMasks, BlockSuppression suppressed, HashSet<int> rampSuppressed,
            List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)> placements,
            HashSet<string> missing, BlockRoadSkin skin = null)
        {
            var result = EnsureRoadCollector().Collect(
                edges, hwMasks, suppressed, rampSuppressed, placements, missing, skin);
            _isolatedStraightKeys = result.isolatedKeys;
        }

        private void DedupeIsolatedStraightKeys(
            List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)> placements) =>
            DedupePlacement.DedupeIsolatedKeys(placements, _isolatedStraightKeys);

        // ── Instance forwarders — Road 2 bakers / collector ─────────────────────
        private void AddRoad2StraightTiles(
            List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)> placements,
            float x, float y, float yaw, bool fullCell, HashSet<string> missing,
            int sides = DirBits.All, BlockRoadSkin skin = null,
            System.Func<float, float, float, bool> rimCovered = null) =>
            EnsureRoad2StraightBaker().AddRoad2StraightTiles(
                placements, x, y, yaw, fullCell, missing, sides, skin, rimCovered);

        private int Road2JunctionArms(int[] road2Masks, int x2, int y2, int mask) =>
            EnsureRoad2JunctionBaker().Road2JunctionArms(road2Masks, x2, y2, mask);

        private void AddRoad2JunctionTiles(
            List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)> placements,
            float x, float y, int mask, int junctionArms, HashSet<string> missing,
            System.Func<float, float, float, bool> rimCovered = null,
            System.Func<float, float, float, (float x, float y, float yaw)?> filletTurn = null,
            BlockRoadSkin skin = null, int sides = DirBits.All) =>
            EnsureRoad2JunctionBaker().AddRoad2JunctionTiles(
                placements, x, y, mask, junctionArms, missing, rimCovered, filletTurn, skin, sides);

        private void CollectRoad2JunctionEffects(
            RoadLayout layout, System.Func<int, bool> blocked, HashSet<long> filletKerb,
            Dictionary<long, (float x, float y, float yaw)> filletTurn = null) =>
            EnsureRoad2JunctionEffects().CollectRoad2JunctionEffects(
                layout, blocked, filletKerb, filletTurn);

        private GameObject Road2JunctionTilePrefab(Road2TilePart part) =>
            Road2TileParts.PrefabFor(part, _library);

        private void ForEachRoad2ApronFiller(
            List<BlockStrip> strips, System.Action<float, float, float> place) =>
            EnsureRoad2ApronFiller().ForEachRoad2ApronFiller(strips, place);

        private void AddRoad2ApronFillers(
            List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)> placements,
            List<BlockStrip> strips, HashSet<string> missing) =>
            EnsureRoad2ApronFiller().AddRoad2ApronFillers(placements, strips, missing);

        private void CollectRoad2Placements(
            List<int> road2Edges, int[] hwMasks, BlockSuppression suppressed2,
            HashSet<int> rampSuppressed2,
            List<(float x, float y, GameObject prefab, float yaw, Vector3 scaleMul)> placements,
            HashSet<string> missing, BlockRoadSkin skin2 = null) =>
            EnsureRoad2Collector().Collect(
                road2Edges, hwMasks, suppressed2, rampSuppressed2, placements, missing, skin2);
    }
}
#endif
