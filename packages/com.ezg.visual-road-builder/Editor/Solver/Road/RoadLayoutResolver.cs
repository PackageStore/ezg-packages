#if UNITY_EDITOR
using System.Collections.Generic;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Orchestrates ResolveRoadLayout: BuildMasks → AddSideBranchJunctions →
    /// CollectHalfStraightSets → MarkStraightRuns → MarkIsolatedSingleStepRuns →
    /// CollectFilletKerb → CollectFilletTurns.</summary>
    internal sealed class RoadLayoutResolver
    {
        private readonly ToolContext _ctx;
        private readonly HalfStraightEmitter _halfEmitter;
        private readonly SideBranchJunctions _sideBranch;
        private readonly StraightRunMarker _runMarker;
        private readonly FilletCollector _filletCollector;

        internal RoadLayoutResolver(ToolContext ctx)
        {
            _ctx = ctx;
            _halfEmitter = new HalfStraightEmitter(ctx);
            _sideBranch = new SideBranchJunctions(ctx);
            _runMarker = new StraightRunMarker(ctx);
            _filletCollector = new FilletCollector(ctx);
        }

        /// <summary>Tầm quét nhánh chạm sườn của lớp type-1 (nấc lattice) = nửa bề rộng mảnh type-1
        /// CỘNG nửa bề rộng nhánh (0.5 + 0.5 ô = 1 ô = 2 nấc).</summary>
        internal const int SideBranchReachSteps = 2;

        /// <summary>Giải layout cho một tập edge. <paramref name="blocked"/> = node bị highway / khối
        /// station-parking / arm ramp chiếm (null = không chặn gì). <paramref name="sideBranchReach"/> =
        /// tầm quét nhánh chạm sườn. <paramref name="bridgeSingleAxisBranch"/> = bật nới trục cho
        /// nhánh chạm sườn (CHỈ Road 2).</summary>
        internal RoadLayout ResolveRoadLayout(
            List<int> edges, System.Func<int, bool> blocked,
            int sideBranchReach = SideBranchReachSteps, bool bridgeSingleAxisBranch = false)
        {
            int lw = _ctx.Doc.LatticeW, lh = _ctx.Doc.LatticeH;
            var layout = new RoadLayout { Masks = MaskBuilder.BuildMasks(edges, lw, lh) };
            _sideBranch.AddSideBranchJunctions(layout, blocked, sideBranchReach, bridgeSingleAxisBranch);
            _halfEmitter.CollectHalfStraightSets(layout.Masks, blocked ?? (_ => false),
                layout.ReplacedByHalf, layout.DroppedBetween);
            _runMarker.MarkStraightRuns(layout, blocked);
            _runMarker.MarkIsolatedSingleStepRuns(layout, blocked);
            _filletCollector.CollectFilletKerb(layout, blocked);
            _filletCollector.CollectFilletTurns(layout, blocked);
            return layout;
        }
    }
}
#endif
