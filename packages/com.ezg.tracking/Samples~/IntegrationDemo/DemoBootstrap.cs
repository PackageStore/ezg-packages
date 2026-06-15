using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Ezg.Tracking;
using Ezg.Tracking.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ezg.Tracking.Samples
{
    /// <summary>
    /// One-stop demo wiring, attached to the single GameObject in <c>DemoTracking.unity</c>. On <c>Start</c> it:
    /// <list type="number">
    /// <item>turns the engine on (mimicking "Firebase finished initializing");</item>
    /// <item>registers a user-property provider;</item>
    /// <item>fires a few events in all three styles so the Console shows tracking output immediately;</item>
    /// <item>builds a uGUI button carrying <see cref="TrackingButtonController"/> so you can click and watch it fire.</item>
    /// </list>
    /// This is the file you delete after copying the pattern into your own bootstrap.
    /// </summary>
    public class DemoBootstrap : MonoBehaviour
    {
        #region Initialize

        private void Start()
        {
            EnableTrackingForDemo();
            RegisterUserProperties();
            BuildDemoUi();
            SendStartupDemoEvents().Forget();
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Flips the engine flags the demo needs. In a real project, set <c>IsInitFirebase = true</c> only after
        /// <c>Firebase.InitializeAsync()</c> reports the dependencies are available.
        /// </summary>
        private void EnableTrackingForDemo()
        {
            TrackingService.IsTracking = true;
            TrackingService.IsInitFirebase = true;
        }

        /// <summary>
        /// Registers the user-property provider. The engine invokes it before every Firebase event. Here it
        /// returns a hard-coded snapshot; in your game, read it from your player-data layer instead.
        /// </summary>
        private void RegisterUserProperties()
        {
            TrackingService.UserPropertyProvider = () => new DemoUserProperties
            {
                player_id = "demo-player-0001",
                current_level = 7,
                iap_count = 0,
                ab_group = "A",
            };
        }

        /// <summary>
        /// Demonstrates the three supported call styles. Watch the Console for the "FIREBASE TRACKING" blocks.
        /// </summary>
        private async UniTask SendStartupDemoEvents()
        {
            // Style 1 — typed config + enum .Send() extension (most ergonomic, type-safe call site).
            await DemoFirebaseEvent.level_start.Send(new DemoEventConfig
            {
                level_id = 7,
                level_name = "forest",
            });

            // Style 2 — plain dictionary, no custom classes at all.
            await TrackingService.SendFirebase("score_update", new Dictionary<string, object>
            {
                ["score"] = 1500,
                ["combo"] = 4,
            });

            // Style 3 — AppsFlyer event (only string values are forwarded).
            TrackingService.SendAppsFlyer("af_level_start", new Dictionary<string, object>
            {
                ["level"] = "7",
            });
        }

        /// <summary>
        /// Builds a minimal canvas + button at runtime so the scene file stays trivial. The button carries
        /// <see cref="TrackingButtonController"/>, which sends a Firebase event on click using the GameObject name
        /// as <c>source</c>.
        /// </summary>
        private void BuildDemoUi()
        {
#if UNITY_2023_1_OR_NEWER
            var hasEventSystem = FindFirstObjectByType<EventSystem>() != null;
#else
            var hasEventSystem = FindObjectOfType<EventSystem>() != null;
#endif
            if (!hasEventSystem)
            {
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            }

            var canvasGo = new GameObject("DemoCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            var buttonGo = new GameObject("demo_track_button", typeof(Image), typeof(Button), typeof(TrackingButtonController));
            buttonGo.transform.SetParent(canvasGo.transform, false);

            var image = buttonGo.GetComponent<Image>();
            image.color = new Color(0.20f, 0.55f, 0.95f);
            buttonGo.GetComponent<Button>().targetGraphic = image;

            var rt = (RectTransform)buttonGo.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(300f, 96f);
            rt.anchoredPosition = Vector2.zero;

            AddLabel(buttonGo.transform, "Click me → track event");
        }

        /// <summary>Adds a stretched, centered legacy <see cref="Text"/> label to a parent transform.</summary>
        private static void AddLabel(Transform parent, string message)
        {
            var textGo = new GameObject("Label", typeof(Text));
            textGo.transform.SetParent(parent, false);

            var text = textGo.GetComponent<Text>();
            text.text = message;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.fontSize = 26;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var rt = (RectTransform)textGo.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        #endregion
    }
}
