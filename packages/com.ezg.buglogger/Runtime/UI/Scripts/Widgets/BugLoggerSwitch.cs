using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ezg.BugLogger.UI
{
    /// <summary>Công tắc bật/tắt (đính kèm ảnh chụp). Bấm cả hàng (track + nhãn) đều đổi trạng thái.</summary>
    internal sealed class BugLoggerSwitch : MonoBehaviour, IPointerClickHandler
    {
        #region Fields

        private const float DURATION = 0.22f;

        [SerializeField] private Image _track;
        [SerializeField] private RectTransform _knob;
        [SerializeField] private float _knobTravel = 19f;

        #endregion

        #region Properties

        public bool IsOn { get; private set; } = true;

        public event Action<bool> Changed;

        #endregion

        #region Initialize

        private void OnDestroy()
        {
            BugTweener.Kill(this);
        }

        #endregion

        #region Public Methods

        public void SetOn(bool isOn, bool animate)
        {
            IsOn = isOn;
            var fromX = _knob.anchoredPosition.x;
            var toX = isOn ? _knobTravel : -_knobTravel;
            var fromColor = _track.color;
            var toColor = isOn ? BugLoggerPalette.Accent : BugLoggerPalette.StrokeStrong;

            if (!animate)
            {
                BugTweener.Kill(this, BugTweener.CHANNEL_MOVE);
                _knob.anchoredPosition = new Vector2(toX, 0f);
                _track.color = toColor;
                return;
            }

            BugTweener.To(this, BugTweener.CHANNEL_MOVE, DURATION, t =>
            {
                _knob.anchoredPosition = new Vector2(Mathf.LerpUnclamped(fromX, toX, t), 0f);
                _track.color = Color.Lerp(fromColor, toColor, t);
            }, BugEase.OutBack);
        }

        #endregion

        #region Events

        public void OnPointerClick(PointerEventData eventData)
        {
            SetOn(!IsOn, true);
            Changed?.Invoke(IsOn);
        }

        #endregion
    }
}
