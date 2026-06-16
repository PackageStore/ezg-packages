using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Ezg.Core.UI
{
    public class UI_CanvasGroupFade : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        #region Initialize

        /// <summary>
        ///     Cache the CanvasGroup component at start if not manually configured.
        /// </summary>
        private void Start()
        {
            if (canvasGroup == null)
                canvasGroup = GetComponent<CanvasGroup>();

            //initialAlpha = canvasGroup.alpha;
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Configures the fade duration dynamically from external controllers.
        /// </summary>
        /// <param name="duration">The new duration of the fade transition.</param>
        public void SetFadeDuration(float duration)
        {
            fadeDuration = Mathf.Max(0.1f, duration); // Đảm bảo duration không nhỏ quá
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Coroutine that fades the canvas group alpha over time using unscaled delta time.
        /// </summary>
        /// <returns>IEnumerator for coroutine execution.</returns>
        private IEnumerator FadeAlpha()
        {
            var elapsedTime = 0f;
            var startAlpha = canvasGroup.alpha;

            while (elapsedTime < fadeDuration && isHolding)
            {
                elapsedTime += Time.unscaledDeltaTime; // Sử dụng unscaledDeltaTime để bỏ qua timeScale
                var t = elapsedTime / fadeDuration;
                canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
                yield return null;
            }

            if (isHolding)
                canvasGroup.alpha = targetAlpha;
        }

        #endregion

        #region Fields

        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private float fadeDuration = 1f;
        [SerializeField] private float targetAlpha;
        [SerializeField] private float initialAlpha = 1;
        private Coroutine fadeCoroutine;
        private bool isHolding;

        #endregion

        #region Event Handlers

        /// <summary>
        ///     Handles the pointer down event to begin fading the CanvasGroup.
        /// </summary>
        /// <param name="eventData">Current pointer event data.</param>
        public void OnPointerDown(PointerEventData eventData)
        {
            isHolding = true;
            if (fadeCoroutine != null)
                StopCoroutine(fadeCoroutine);
            fadeCoroutine = StartCoroutine(FadeAlpha());
        }

        /// <summary>
        ///     Handles the pointer up event to cancel the fade and restore initial alpha.
        /// </summary>
        /// <param name="eventData">Current pointer event data.</param>
        public void OnPointerUp(PointerEventData eventData)
        {
            isHolding = false;
            if (fadeCoroutine != null)
                StopCoroutine(fadeCoroutine);
            canvasGroup.alpha = initialAlpha; // Reset về alpha ban đầu khi thả
        }

        #endregion
    }
}