#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Ezg.Core.UI
{
    public class UI_FadeImageOnScroll : MonoBehaviour
    {
        #region Private Methods

        /// <summary>
        ///     Updates the alpha of the target object dynamically based on the scroll view's vertical position.
        /// </summary>
        private void Update()
        {
            var scrollPos = 0 - _scrollView.verticalNormalizedPosition;
            if (scrollPos > _percentStartFade)
            {
                var alpha = Mathf.Lerp(1, 0, (scrollPos - _percentStartFade) / 0.1f);
                _objectToFade.alpha = alpha;
                if (alpha < 0.01)
                    _objectToFade.gameObject.SetActive(false);
                else
                    _objectToFade.gameObject.SetActive(true);
            }
            else
            {
                _objectToFade.gameObject.SetActive(true);
                _objectToFade.alpha = 1;
            }
        }

        #endregion

        #region Fields

        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình")]
#endif
        private ScrollRect _scrollView;

        [FormerlySerializedAs("_imageToFade")] [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình")]
#endif
        private CanvasGroup _objectToFade;

        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình")]
#endif
        [Min(-1)]
#if ODIN_INSPECTOR
        [MaxValue(1)]
#endif
        private float _percentStartFade;

        #endregion
    }
}