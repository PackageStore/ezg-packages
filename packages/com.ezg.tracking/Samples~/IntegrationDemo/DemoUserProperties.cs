using Ezg.Tracking;

namespace Ezg.Tracking.Samples
{
    /// <summary>
    /// Sample user-property snapshot. Public fields become Firebase user properties. In your project, replace
    /// these fields with whatever you segment on (level, spend tier, A/B group…) and fill them from your own
    /// player data inside the provider (see <see cref="DemoBootstrap"/>).
    /// </summary>
    public class DemoUserProperties
    {
        public string player_id;
        public int current_level = TrackingService.NULL_NUMBER;
        public int iap_count = TrackingService.NULL_NUMBER;
        public string ab_group;
    }
}
