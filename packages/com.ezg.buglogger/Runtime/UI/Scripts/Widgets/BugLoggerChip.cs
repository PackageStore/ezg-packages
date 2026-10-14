using System;
using UnityEngine;
using UnityEngine.UI;

namespace Ezg.BugLogger.UI
{
    /// <summary>
    ///     Chip chọn (phân loại / tag / người). Chế độ nhiều lựa chọn hiện icon "+" ↔ "✓" giữ nguyên bề rộng chip
    ///     để các chip phía sau không nhảy chỗ khi bấm.
    /// </summary>
    internal sealed class BugLoggerChip : MonoBehaviour
    {
        #region Fields

        private const float COLOR_DURATION = 0.16f;
        private const float SHAKE_DURATION = 0.4f;
        private const float SHAKE_DEGREES = 6f;
        private const float SHAKE_CYCLES = 3f;

        [SerializeField] private Button _button;
        [SerializeField] private Image _background;
        [SerializeField] private Image _stroke;
        [SerializeField] private Image _icon;
        [SerializeField] private Text _label;
        [SerializeField] private RectTransform _shakeTarget;
        [SerializeField] private Sprite _iconAdd;
        [SerializeField] private Sprite _iconCheck;

        private Action<BugLoggerChip> _onClick;
        private bool _showIcon;

        #endregion

        #region Properties

        public bool IsSelected { get; private set; }

        /// <summary>Id gửi lên server (tag / người) hoặc chính tên (phân loại).</summary>
        public string Value { get; private set; }

        #endregion

        #region Initialize

        private void Awake()
        {
            _button.onClick.AddListener(OnClicked);
        }

        private void OnDestroy()
        {
            BugTweener.Kill(this);
            BugTweener.Kill(_shakeTarget);
        }

        #endregion

        #region Public Methods

        public void Setup(string value, string label, bool showIcon, Action<BugLoggerChip> onClick)
        {
            Value = value;
            _label.text = label;
            _showIcon = showIcon;
            _icon.gameObject.SetActive(showIcon);
            _onClick = onClick;
            SetSelected(false, false);
        }

        public void SetSelected(bool selected, bool animate)
        {
            IsSelected = selected;
            if (_showIcon) _icon.sprite = selected ? _iconCheck : _iconAdd;

            var fromBg = _background.color;
            var fromStroke = _stroke.color;
            var fromLabel = _label.color;
            var fromIcon = _icon.color;
            var toBg = selected ? BugLoggerPalette.Accent : BugLoggerPalette.Surface;
            var toStroke = selected ? BugLoggerPalette.Accent : BugLoggerPalette.Stroke;
            var toLabel = selected ? BugLoggerPalette.OnAccent : BugLoggerPalette.TextSecondary;
            var toIcon = selected ? BugLoggerPalette.OnAccent : BugLoggerPalette.TextMuted;

            if (!animate)
            {
                BugTweener.Kill(this, BugTweener.CHANNEL_COLOR);
                ApplyColors(toBg, toStroke, toLabel, toIcon);
                return;
            }

            BugTweener.To(this, BugTweener.CHANNEL_COLOR, COLOR_DURATION, t => ApplyColors(
                Color.LerpUnclamped(fromBg, toBg, t), Color.LerpUnclamped(fromStroke, toStroke, t),
                Color.LerpUnclamped(fromLabel, toLabel, t), Color.LerpUnclamped(fromIcon, toIcon, t)));
        }

        /// <summary>
        ///     Lắc báo "không chọn thêm được". Lắc bằng góc xoay chứ không dời vị trí: vị trí chip do flow layout
        ///     quản, dời tay sẽ đá nhau với lần dựng layout kế tiếp.
        /// </summary>
        public void Shake()
        {
            BugTweener.To(_shakeTarget, BugTweener.CHANNEL_SHAKE, SHAKE_DURATION, t =>
            {
                var angle = Mathf.Sin(t * Mathf.PI * 2f * SHAKE_CYCLES) * SHAKE_DEGREES * (1f - t);
                _shakeTarget.localEulerAngles = new Vector3(0f, 0f, angle);
            }, BugEase.Linear);
        }

        #endregion

        #region Private Methods

        private void ApplyColors(Color background, Color stroke, Color label, Color icon)
        {
            _background.color = background;
            _stroke.color = stroke;
            _label.color = label;
            _icon.color = icon;
        }

        private void OnClicked()
        {
            _onClick?.Invoke(this);
        }

        #endregion
    }
}
