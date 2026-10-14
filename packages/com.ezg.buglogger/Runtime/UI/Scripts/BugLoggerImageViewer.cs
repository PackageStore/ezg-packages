using UnityEngine;
using UnityEngine.UI;

namespace Ezg.BugLogger.UI
{
    /// <summary>Xem lớn ảnh chụp màn hình sẽ gửi kèm. Chạm bất kỳ đâu để đóng.</summary>
    internal sealed class BugLoggerImageViewer : MonoBehaviour
    {
        #region Fields

        private const float SHOW_DURATION = 0.3f;
        private const float HIDE_DURATION = 0.2f;
        private const float FROM_SCALE = 0.9f;

        [SerializeField] private CanvasGroup _group;
        [SerializeField] private RectTransform _frame;
        [SerializeField] private RawImage _image;
        [SerializeField] private AspectRatioFitter _fitter;
        [SerializeField] private Button _closeArea;
        [SerializeField] private Button _closeButton;

        #endregion

        #region Properties

        public bool IsOpen { get; private set; }

        #endregion

        #region Initialize

        private void Awake()
        {
            _closeArea.onClick.AddListener(Hide);
            _closeButton.onClick.AddListener(Hide);
        }

        private void OnDisable()
        {
            BugTweener.Kill(this);
        }

        #endregion

        #region Public Methods

        public void Show(Texture texture)
        {
            if (texture == null) return;

            IsOpen = true;
            _image.texture = texture;
            _fitter.aspectRatio = (float)texture.width / Mathf.Max(1, texture.height);
            gameObject.SetActive(true);
            _group.blocksRaycasts = true;

            BugTweener.To(this, BugTweener.CHANNEL_ALPHA, SHOW_DURATION, t =>
            {
                _group.alpha = Mathf.Clamp01(t * 1.6f);
                _frame.localScale = Vector3.one * Mathf.LerpUnclamped(FROM_SCALE, 1f, t);
            }, BugEase.OutQuint);
        }

        public void Hide()
        {
            if (!IsOpen) return;

            IsOpen = false;
            _group.blocksRaycasts = false;
            var fromAlpha = _group.alpha;
            BugTweener.To(this, BugTweener.CHANNEL_ALPHA, HIDE_DURATION, t =>
            {
                _group.alpha = Mathf.Lerp(fromAlpha, 0f, t);
                _frame.localScale = Vector3.one * Mathf.Lerp(1f, FROM_SCALE + 0.04f, t);
            }, BugEase.InCubic, Close);
        }

        /// <summary>Đóng ngay không animation (form đóng / texture sắp bị huỷ).</summary>
        public void Close()
        {
            IsOpen = false;
            _image.texture = null;
            gameObject.SetActive(false);
        }

        #endregion
    }
}
