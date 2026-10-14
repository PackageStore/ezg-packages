using UnityEngine;
using UnityEngine.UI;

namespace Ezg.BugLogger.UI
{
    /// <summary>Kiểu toast — quyết định màu + icon.</summary>
    internal enum BugToastKind
    {
        Info,
        Success,
        Error
    }

    /// <summary>
    ///     Toast của Bug Logger (trượt xuống từ mép trên safe area). Toast mới thay toast đang hiện. Dùng thay toast
    ///     của game để thông báo báo lỗi trông như nhau ở mọi project.
    /// </summary>
    internal sealed class BugLoggerToast : MonoBehaviour
    {
        #region Fields

        private const float MAX_TEXT_WIDTH = 760f;
        private const float SHOW_DURATION = 0.38f;
        private const float HIDE_DURATION = 0.22f;
        private const float SLIDE_DISTANCE = 56f;
        private const float BASE_HOLD = 2f;
        private const float HOLD_PER_CHAR = 0.035f;
        private const float MAX_HOLD = 4.5f;

        [SerializeField] private CanvasGroup _group;
        [SerializeField] private RectTransform _panel;
        [SerializeField] private Image _iconBackground;
        [SerializeField] private Image _icon;
        [SerializeField] private Text _message;
        [SerializeField] private LayoutElement _messageLayout;
        [SerializeField] private Sprite _iconInfo;
        [SerializeField] private Sprite _iconSuccess;
        [SerializeField] private Sprite _iconError;

        private float _holdTimer;
        private bool _isHolding;

        #endregion

        #region Initialize

        private void OnDisable()
        {
            BugTweener.Kill(this);
            _isHolding = false;
        }

        private void Update()
        {
            if (!_isHolding) return;

            _holdTimer -= Time.unscaledDeltaTime;
            if (_holdTimer <= 0f) Hide();
        }

        #endregion

        #region Public Methods

        public void Show(string message, BugToastKind kind)
        {
            if (string.IsNullOrEmpty(message)) return;

            gameObject.SetActive(true);
            _message.text = message;
            _messageLayout.preferredWidth = Mathf.Min(Mathf.Ceil(_message.preferredWidth), MAX_TEXT_WIDTH);

            switch (kind)
            {
                case BugToastKind.Success:
                    _icon.sprite = _iconSuccess;
                    _iconBackground.color = BugLoggerPalette.Success;
                    break;
                case BugToastKind.Error:
                    _icon.sprite = _iconError;
                    _iconBackground.color = BugLoggerPalette.Danger;
                    break;
                default:
                    _icon.sprite = _iconInfo;
                    _iconBackground.color = BugLoggerPalette.Info;
                    break;
            }

            _holdTimer = Mathf.Min(BASE_HOLD + message.Length * HOLD_PER_CHAR, MAX_HOLD);
            _isHolding = true;

            var fromAlpha = _group.alpha;
            var fromY = fromAlpha > 0f ? _panel.anchoredPosition.y : SLIDE_DISTANCE;
            BugTweener.To(this, BugTweener.CHANNEL_MOVE, SHOW_DURATION, t =>
            {
                _panel.anchoredPosition = new Vector2(0f, Mathf.LerpUnclamped(fromY, 0f, t));
                _group.alpha = Mathf.Lerp(fromAlpha, 1f, t * 2f);
            }, BugEase.OutBack);
        }

        #endregion

        #region Private Methods

        private void Hide()
        {
            _isHolding = false;
            var fromY = _panel.anchoredPosition.y;
            var fromAlpha = _group.alpha;
            BugTweener.To(this, BugTweener.CHANNEL_MOVE, HIDE_DURATION, t =>
            {
                _panel.anchoredPosition = new Vector2(0f, Mathf.Lerp(fromY, SLIDE_DISTANCE * 0.5f, t));
                _group.alpha = Mathf.Lerp(fromAlpha, 0f, t);
            }, BugEase.InCubic, () => gameObject.SetActive(false));
        }

        #endregion
    }
}
