using System;
using UnityEngine;
using UnityEngine.UI;

namespace Ezg.BugLogger.UI
{
    /// <summary>Ô chọn màu nét vẽ: chấm tròn màu, vòng sáng quanh khi đang chọn.</summary>
    internal sealed class BugLoggerSwatch : MonoBehaviour
    {
        #region Fields

        private const float SELECT_DURATION = 0.22f;
        private const float IDLE_SCALE = 0.78f;

        [SerializeField] private Button _button;
        [SerializeField] private Image _fill;
        [SerializeField] private Image _ring;
        [SerializeField] private RectTransform _dot;

        #endregion

        #region Properties

        public Color32 Color { get; private set; }

        #endregion

        #region Events

        public event Action<BugLoggerSwatch> Clicked;

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

        public void Setup(Color32 color)
        {
            Color = color;
            _fill.color = color;
        }

        public void SetSelected(bool selected, bool animate)
        {
            var fromAlpha = _ring.color.a;
            var fromScale = _dot.localScale.x;
            var toAlpha = selected ? 1f : 0f;
            var toScale = selected ? 1f : IDLE_SCALE;

            if (!animate)
            {
                BugTweener.Kill(this, BugTweener.CHANNEL_SCALE);
                Apply(toAlpha, toScale);
                return;
            }

            BugTweener.To(this, BugTweener.CHANNEL_SCALE, SELECT_DURATION,
                t => Apply(Mathf.Lerp(fromAlpha, toAlpha, t), Mathf.LerpUnclamped(fromScale, toScale, t)), BugEase.OutBack);
        }

        #endregion

        #region Private Methods

        private void Apply(float ringAlpha, float dotScale)
        {
            _ring.color = BugLoggerPalette.WithAlpha(_ring.color, Mathf.Clamp01(ringAlpha));
            _dot.localScale = new Vector3(dotScale, dotScale, 1f);
        }

        private void OnClicked()
        {
            Clicked?.Invoke(this);
        }

        #endregion
    }
}
