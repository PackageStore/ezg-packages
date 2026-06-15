using Cysharp.Threading.Tasks;
using Ezg.Tracking;

namespace Ezg.Tracking.Samples
{
    /// <summary>
    /// Sample event names. In your project, rename these to your real Firebase events and put the enum wherever
    /// your call sites can reach it (the live game keeps it in the global namespace so no <c>using</c> is needed).
    /// </summary>
    public enum DemoFirebaseEvent
    {
        level_start,
        level_complete,
        button_click,
    }

    /// <summary>
    /// Sample typed payload: every public instance field becomes one Firebase parameter. Numeric fields default
    /// to <see cref="TrackingService.NULL_NUMBER"/> so "unset" values are skipped automatically — set only the
    /// fields a given event actually uses.
    /// </summary>
    public class DemoEventConfig
    {
        public int level_id = TrackingService.NULL_NUMBER;
        public string level_name;
        public int score = TrackingService.NULL_NUMBER;
        public string source;
    }

    /// <summary>
    /// Ergonomic <c>.Send()</c> sugar binding the project's own enum to the agnostic engine. This is the one
    /// place that mentions <see cref="DemoFirebaseEvent"/>; the engine never sees it.
    /// </summary>
    public static class DemoTrackingExtensions
    {
        /// <summary>Sends a Firebase event from the sample enum + typed config.</summary>
        public static UniTask Send(this DemoFirebaseEvent eventName, DemoEventConfig config = null)
            => TrackingService.SendFirebase(eventName.ToString(), config);
    }
}
