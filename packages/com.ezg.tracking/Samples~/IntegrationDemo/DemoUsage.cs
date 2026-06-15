using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Ezg.Tracking;

namespace Ezg.Tracking.Samples
{
    /// <summary>
    /// Copy-paste call-site examples. Nothing here runs automatically — it shows how feature code should call the
    /// engine so you can model your own gameplay tracking after it. Delete this file after copying what you need.
    /// </summary>
    public static class DemoTrackingCalls
    {
        /// <summary>Typed config + enum extension — clearest at the call site for events you send often.</summary>
        public static void OnLevelComplete(int levelId, int score)
        {
            DemoFirebaseEvent.level_complete.Send(new DemoEventConfig
            {
                level_id = levelId,
                level_name = "forest",
                score = score,
            }).Forget();
        }

        /// <summary>Plain dictionary — best for one-off events where a custom class would be overkill.</summary>
        public static void OnButtonClick(string buttonId)
        {
            TrackingService.SendFirebase("button_click", new Dictionary<string, object>
            {
                ["id"] = buttonId,
            }).Forget();
        }

        /// <summary>Push a one-off user-property update outside the registered provider.</summary>
        public static void OnPurchase(int newIapCount)
        {
            TrackingService.SetUserProperty(new Dictionary<string, object>
            {
                ["iap_count"] = newIapCount,
            });
        }
    }
}
