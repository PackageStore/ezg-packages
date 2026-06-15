using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Ezg.Tracking.UI
{
    /// <summary>
    /// Drop-in component: sends a Firebase event when the attached <see cref="Button"/> is clicked. Reusable in
    /// any project — set the event name in the Inspector; no game-specific types are required. The clicked
    /// GameObject's name is forwarded as <c>source</c> / <c>sourceId</c>.
    /// </summary>
    public class TrackingButtonController : MonoBehaviour
    {
        #region Fields

        [SerializeField]
        private string _eventName = "button_click";

        #endregion

        #region Initialize

        private void Start()
        {
            GetComponent<Button>()?.onClick.AddListener(OnTracking);
        }

        #endregion

        #region Event Handlers

        private void OnTracking()
        {
            TrackingService.SendFirebase(_eventName, new Dictionary<string, object>
            {
                ["source"] = gameObject.name,
                ["sourceId"] = gameObject.name,
            }).Forget();
        }

        #endregion
    }
}
