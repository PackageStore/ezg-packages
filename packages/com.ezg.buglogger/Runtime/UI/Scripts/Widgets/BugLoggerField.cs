using UnityEngine;
using UnityEngine.UI;

namespace Ezg.BugLogger.UI
{
    /// <summary>
    ///     Ô nhập có viền sáng khi focus, bộ đếm ký tự và dòng báo lỗi. Giới hạn độ dài lấy từ form của server
    ///     (<c>characterLimit</c>) nên QA thấy ngay chạm trần thay vì bị cắt lặng lẽ lúc gửi.
    /// </summary>
    internal sealed class BugLoggerField : MonoBehaviour
    {
        #region Fields

        private const float FOCUS_DURATION = 0.18f;
        private const float SHAKE_DURATION = 0.42f;
        private const float SHAKE_DISTANCE = 14f;
        private const float SHAKE_CYCLES = 3.5f;

        // Bộ đếm chỉ hiện khi đã dùng quá phần này của giới hạn — đỡ rối mắt lúc ô còn trống.
        private const float COUNTER_VISIBLE_RATIO = 0.6f;

        [SerializeField] private InputField _input;
        [SerializeField] private Image _stroke;
        [SerializeField] private RectTransform _shakeTarget;
        [SerializeField] private Text _counter;
        [SerializeField] private Text _error;
        [SerializeField] private bool _alwaysShowCounter;

        private bool _wasFocused;
        private bool _hasError;

        #endregion

        #region Properties

        public InputField Input => _input;

        public string Text
        {
            get => _input.text;
            set
            {
                _input.SetTextWithoutNotify(value ?? string.Empty);
                ClearError();
                RefreshCounter();
            }
        }

        /// <summary>Đang gõ vào ô — tính cả lúc gõ qua ô sửa native của Android.</summary>
        public bool IsFocused => _input is BugLoggerInputField field ? field.IsEditing : _input.isFocused;

        /// <summary>Ngón tay đang chạm ô chưa focus / ô sắp mở (xem <see cref="BugLoggerInputField.IsAwaitingFocus" />).</summary>
        public bool IsAwaitingFocus => _input is BugLoggerInputField field && field.IsAwaitingFocus;

        #endregion

        #region Initialize

        private void Awake()
        {
            _input.onValueChanged.AddListener(OnValueChanged);
            _error.gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            BugTweener.Kill(this);
            BugTweener.Kill(_shakeTarget);
            _shakeTarget.anchoredPosition = Vector2.zero;
            _wasFocused = false;
            ApplyStroke(false, false);
        }

        private void LateUpdate()
        {
            // InputField legacy không có sự kiện focus/blur ⇒ dò cờ mỗi frame (một bool, rẻ).
            var focused = IsFocused;
            if (focused == _wasFocused) return;

            _wasFocused = focused;
            // Ô sửa native vừa mở thì Unity sắp đứng hình (app mất focus) ⇒ viền sáng ngay, khỏi kẹt giữa animation.
            var isNative = focused && _input is BugLoggerInputField field && BugLoggerNativeEditor.IsEditing(field);
            ApplyStroke(focused, !isNative);
        }

        #endregion

        #region Public Methods

        /// <summary>Tên ô hiện trên ô sửa native của Android.</summary>
        public void SetEditorLabel(string label)
        {
            if (_input is BugLoggerInputField field) field.EditorLabel = label;
        }

        public void SetLimit(int limit)
        {
            _input.characterLimit = Mathf.Max(0, limit);
            RefreshCounter();
        }

        /// <summary>Báo lỗi dưới ô + viền đỏ + lắc. Gõ tiếp là tự xoá.</summary>
        public void ShowError(string message)
        {
            _hasError = true;
            _error.text = message;
            _error.gameObject.SetActive(true);
            ApplyStroke(IsFocused, true);

            BugTweener.To(_shakeTarget, BugTweener.CHANNEL_SHAKE, SHAKE_DURATION, t =>
            {
                var offset = Mathf.Sin(t * Mathf.PI * 2f * SHAKE_CYCLES) * SHAKE_DISTANCE * (1f - t);
                _shakeTarget.anchoredPosition = new Vector2(offset, 0f);
            }, BugEase.Linear);
        }

        public void ClearError()
        {
            if (!_hasError) return;

            _hasError = false;
            _error.gameObject.SetActive(false);
            ApplyStroke(IsFocused, true);
        }

        #endregion

        #region Private Methods

        private void ApplyStroke(bool focused, bool animate)
        {
            var target = _hasError ? BugLoggerPalette.Danger :
                focused ? BugLoggerPalette.Accent : BugLoggerPalette.Stroke;
            if (!animate)
            {
                BugTweener.Kill(this, BugTweener.CHANNEL_COLOR);
                _stroke.color = target;
                return;
            }

            var from = _stroke.color;
            BugTweener.To(this, BugTweener.CHANNEL_COLOR, FOCUS_DURATION,
                t => _stroke.color = Color.Lerp(from, target, t));
        }

        private void RefreshCounter()
        {
            if (_counter == null) return;

            var limit = _input.characterLimit;
            var length = _input.text.Length;
            var visible = limit > 0 && (_alwaysShowCounter || length >= limit * COUNTER_VISIBLE_RATIO);
            _counter.enabled = visible;
            if (!visible) return;

            _counter.text = string.Format(BugLoggerStrings.COUNTER_FORMAT, length, limit);
            _counter.color = length >= limit ? BugLoggerPalette.Warning : BugLoggerPalette.TextMuted;
        }

        private void OnValueChanged(string value)
        {
            ClearError();
            RefreshCounter();
        }

        #endregion
    }
}
