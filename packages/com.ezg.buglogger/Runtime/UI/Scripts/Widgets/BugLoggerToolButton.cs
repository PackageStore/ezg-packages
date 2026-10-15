using System;
using UnityEngine;
using UnityEngine.UI;

namespace Ezg.BugLogger.UI
{
    /// <summary>Nút công cụ của màn chỉnh sửa ảnh: icon + nhãn, nền nổi màu nhấn khi đang chọn.</summary>
    internal sealed class BugLoggerToolButton : MonoBehaviour
    {
        #region Fields

        private const float COLOR_DURATION = 0.16f;

        [SerializeField] private Button _button;
        [SerializeField] private Image _background;
        [SerializeField] private Image _icon;
        [SerializeField] private Text _label;

        #endregion

        #region Properties

        public bool IsSelected { get; private set; }

        #endregion

        #region Events

        public event Action<BugLoggerToolButton> Clicked;

        #endregion

        #region Initialize

        private void Awake()
        {
            _button.onClick.AddListener(OnClicked);
        }

        private void OnDestroy()
        {
            BugTweener.Kill(this);
        }

        #endregion

        #region Public Methods

        public void SetSelected(bool selected, bool animate)
        {
            IsSelected = selected;
            var fromBackground = _background.color;
            var fromIcon = _icon.color;
            var fromLabel = _label.color;
            var toBackground = selected ? BugLoggerPalette.AccentSoft : BugLoggerPalette.WithAlpha(BugLoggerPalette.Surface, 0f);
            var toIcon = selected ? BugLoggerPalette.Accent : BugLoggerPalette.TextSecondary;
            var toLabel = selected ? BugLoggerPalette.Accent : BugLoggerPalette.TextSecondary;

            if (!animate)
            {
                BugTweener.Kill(this, BugTweener.CHANNEL_COLOR);
                ApplyColors(toBackground, toIcon, toLabel);
                return;
            }

            BugTweener.To(this, BugTweener.CHANNEL_COLOR, COLOR_DURATION, t => ApplyColors(
                Color.LerpUnclamped(fromBackground, toBackground, t), Color.LerpUnclamped(fromIcon, toIcon, t),
                Color.LerpUnclamped(fromLabel, toLabel, t)));
        }

        #endregion

        #region Private Methods

        private void ApplyColors(Color background, Color icon, Color label)
        {
            _background.color = background;
            _icon.color = icon;
            _label.color = label;
        }

        private void OnClicked()
        {
            Clicked?.Invoke(this);
        }

        #endregion
    }
}
