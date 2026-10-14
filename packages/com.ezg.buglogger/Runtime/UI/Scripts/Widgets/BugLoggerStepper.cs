using System;
using UnityEngine;
using UnityEngine.UI;

namespace Ezg.BugLogger.UI
{
    /// <summary>Ô số có nút − / + (tỉ lệ tái hiện "x lần gặp / y lần thử").</summary>
    internal sealed class BugLoggerStepper : MonoBehaviour
    {
        #region Fields

        private const float POP_SCALE = 1.18f;
        private const float POP_DURATION = 0.26f;
        private const float DISABLED_ALPHA = 0.32f;

        [SerializeField] private Button _minusButton;
        [SerializeField] private Button _plusButton;
        [SerializeField] private CanvasGroup _minusGroup;
        [SerializeField] private CanvasGroup _plusGroup;
        [SerializeField] private Text _valueText;

        private int _min;
        private int _max = 1;

        #endregion

        #region Properties

        public int Value { get; private set; }

        public event Action<int> ValueChanged;

        #endregion

        #region Initialize

        private void Awake()
        {
            _minusButton.onClick.AddListener(OnMinus);
            _plusButton.onClick.AddListener(OnPlus);
        }

        private void OnDestroy()
        {
            BugTweener.Kill(_valueText);
        }

        #endregion

        #region Public Methods

        public void SetRange(int min, int max)
        {
            _min = min;
            _max = Mathf.Max(min, max);
            SetValue(Value, false);
        }

        public void SetValue(int value, bool notify)
        {
            var clamped = Mathf.Clamp(value, _min, _max);
            var changed = clamped != Value;
            Value = clamped;
            _valueText.text = clamped.ToString();
            RefreshButtons();

            if (changed && notify) ValueChanged?.Invoke(clamped);
        }

        #endregion

        #region Private Methods

        private void Step(int delta)
        {
            var before = Value;
            SetValue(Value + delta, true);
            if (Value == before) return;

            var rect = _valueText.rectTransform;
            BugTweener.To(_valueText, BugTweener.CHANNEL_SCALE, POP_DURATION,
                t => rect.localScale = Vector3.one * Mathf.LerpUnclamped(POP_SCALE, 1f, t), BugEase.OutBack);
        }

        private void RefreshButtons()
        {
            var canMinus = Value > _min;
            var canPlus = Value < _max;
            _minusButton.interactable = canMinus;
            _plusButton.interactable = canPlus;
            _minusGroup.alpha = canMinus ? 1f : DISABLED_ALPHA;
            _plusGroup.alpha = canPlus ? 1f : DISABLED_ALPHA;
        }

        private void OnMinus()
        {
            Step(-1);
        }

        private void OnPlus()
        {
            Step(1);
        }

        #endregion
    }
}
