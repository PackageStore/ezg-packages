#if UNITY_EDITOR
namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>Road-type-specific parameters for station/parking block collectors (R4).
    /// Replaces the old <c>bool road2</c> flag with a descriptor that makes the later
    /// road-profile-unify epic cheaper. Values are pre-computed from BlockClearance constants
    /// (compile-time deterministic — same numbers regardless of call site).</summary>
    internal sealed class BlockLayerDesc
    {
        internal readonly string Label;
        internal readonly int StationClearSteps;
        internal readonly float[] StationApronDepths;
        internal readonly float StationFilletDepth;
        internal readonly int ParkingClearSteps;
        internal readonly float[] ParkingApronDepths;
        internal readonly float ParkingFilletDepth;

        private BlockLayerDesc(string label,
            int stationClearSteps, float[] stationApronDepths, float stationFilletDepth,
            int parkingClearSteps, float[] parkingApronDepths, float parkingFilletDepth)
        {
            Label = label;
            StationClearSteps = stationClearSteps;
            StationApronDepths = stationApronDepths;
            StationFilletDepth = stationFilletDepth;
            ParkingClearSteps = parkingClearSteps;
            ParkingApronDepths = parkingApronDepths;
            ParkingFilletDepth = parkingFilletDepth;
        }

        // station+road1: clearance 2 steps, apron {0.25, 0.75}, fillet 0.5, parking hook 1 step
        internal static readonly BlockLayerDesc Road = new(
            "Road",
            stationClearSteps: 2,
            stationApronDepths: new[] { 0.25f, 0.75f },
            stationFilletDepth: 0.5f,
            parkingClearSteps: 1,
            parkingApronDepths: new[] { 0.25f },
            parkingFilletDepth: 0.5f);

        // station+road2: clearance 3 steps, apron {0.25, 1.0} (skip filler zone), fillet 0.75,
        // parking fillet 0 (mặt khối lọt trong lòng đường Road 2)
        internal static readonly BlockLayerDesc Road2 = new(
            "Road2",
            stationClearSteps: 3,
            stationApronDepths: new[] { 0.25f, 1f },
            stationFilletDepth: 0.75f,
            parkingClearSteps: 1,
            parkingApronDepths: new[] { 0.25f },
            parkingFilletDepth: 0f);
    }
}
#endif
