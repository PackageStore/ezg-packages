using UnityEngine;
using UnityEngine.UI;

namespace Ezg.Core.UI
{
    public class UILayoutAdjuster : MonoBehaviour
    {
        #region Private Methods

        /// <summary>
        ///     Calculates and sets the preferred height of the LayoutElement based on screen aspect ratio scaling.
        /// </summary>
        private void AdjustMinHeight()
        {
            if (layoutElement == null) return;

            float currentHeight = Screen.height;
            var referenceHeight = referenceResolution.y;

            var scaleFactor = currentHeight / referenceHeight;

            var adjustedMinHeight = referenceMinHeight * scaleFactor;
            layoutElement.preferredHeight = adjustedMinHeight;
        }

        #endregion

        #region Fields

        [SerializeField] private LayoutElement layoutElement;
        [SerializeField] private float referenceMinHeight = 130f; // minHeight tại độ phân giải tham chiếu
        [SerializeField] private Vector2 referenceResolution = new(1080, 2400); // Độ phân giải tham chiếu

        #endregion

        #region Initialize

        /// <summary>
        ///     Adjusts the layout element's height at start.
        /// </summary>
        private void Start()
        {
            AdjustMinHeight();
        }

#if UNITY_EDITOR
        /// <summary>
        ///     Continually updates and adjusts the layout height in editor update loops.
        /// </summary>
        private void Update()
        {
            AdjustMinHeight();
        }
#endif

        #endregion
    }
}