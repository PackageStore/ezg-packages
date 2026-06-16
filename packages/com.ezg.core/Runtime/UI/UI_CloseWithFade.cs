using DG.Tweening;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif
using UnityEngine;
using UnityEngine.UI;

namespace Ezg.Core.UI
{
    internal class UI_CloseWithFade : MonoBehaviour
    {
        #region Initialize

        /// <summary>
        ///     Cache components and hook up listeners at start.
        /// </summary>
        private void Start()
        {
            _closeButton = _closeButton ??= GetComponent<Button>();
            _canvasGrp = _canvasGrp ??= GetComponent<CanvasGroup>();
            if (_closeButton != null) _closeButton.onClick.AddListener(OnClose);
        }

        #endregion

        #region Event Handlers

        /// <summary>
        ///     Fades the canvas group out when the close button is clicked, then disables the game object.
        /// </summary>
        private void OnClose()
        {
            if (_canvasGrp != null)
                _canvasGrp.DOFade(0, .3f).OnComplete(() =>
                {
                    _canvasGrp.gameObject.SetActive(false);
                    _canvasGrp.alpha = 1;
                });
        }

        #endregion

        #region Fields

        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình")]
#endif
        [Tooltip("Button close, not necessarily ref")]
        private Button _closeButton;

        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình")]
#endif
        [Tooltip("Button close, not necessarily ref")]
        private CanvasGroup _canvasGrp;

        #endregion
    }
}