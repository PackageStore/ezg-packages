using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ezg.BugLogger.UI
{
    /// <summary>
    ///     Nút nổi mở form báo lỗi: kéo đi đâu cũng được, thả ra tự dạt về mép trái/phải gần nhất, nhớ vị trí qua
    ///     các lần mở app. Đứng yên một lúc thì mờ bớt để không che gameplay; huy hiệu đỏ đếm report chờ gửi.
    /// </summary>
    internal sealed class BugLoggerLauncher : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        #region Fields

        private const string PREF_SIDE = "Ezg.BugLogger.Launcher.Side";
        private const string PREF_Y = "Ezg.BugLogger.Launcher.Y";

        private const float EDGE_PADDING = 20f;
        private const float DEFAULT_Y_RATIO = 0.3f;
        private const float SNAP_DURATION = 0.42f;
        private const float PRESS_SCALE = 0.9f;
        private const float PRESS_DURATION = 0.1f;
        private const float RELEASE_DURATION = 0.3f;
        private const float IDLE_DELAY = 2.5f;
        private const float IDLE_ALPHA = 0.62f;
        private const float FADE_DURATION = 0.3f;
        private const float SHOW_DURATION = 0.36f;
        private const float HIDE_DURATION = 0.18f;
        private const float BADGE_POP_DURATION = 0.32f;
        private const int BADGE_MAX = 99;

        [SerializeField] private CanvasGroup _group;
        [SerializeField] private RectTransform _visual;
        [SerializeField] private GameObject _badge;
        [SerializeField] private RectTransform _badgeRect;
        [SerializeField] private Text _badgeText;

        private RectTransform _rect;
        private RectTransform _parent;
        private Vector2 _lastParentSize;
        private bool _isDragging;
        private bool _isPressed;
        private bool _isShown;
        private bool _isIdle;
        private float _idleTimer;
        private int _badgeCount = -1;

        #endregion

        #region Properties

        /// <summary>Bấm (không kéo) vào nút.</summary>
        public event Action Clicked;

        #endregion

        #region Initialize

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _parent = (RectTransform)_rect.parent;
        }

        private void OnDisable()
        {
            BugTweener.Kill(this);
            BugTweener.Kill(_visual);
            BugTweener.Kill(_badgeRect);
            _isDragging = false;
            _isPressed = false;
        }

        private void Update()
        {
            // Đổi hướng màn hình / safe area ⇒ đặt lại theo vị trí đã nhớ (tỉ lệ) để không văng ra ngoài.
            var size = _parent.rect.size;
            if (size != _lastParentSize)
            {
                _lastParentSize = size;
                RestorePosition();
            }

            if (_isPressed || _isDragging || _isIdle || !_isShown) return;

            _idleTimer += Time.unscaledDeltaTime;
            if (_idleTimer < IDLE_DELAY) return;

            _isIdle = true;
            FadeTo(IDLE_ALPHA, FADE_DURATION);
        }

        #endregion

        #region Public Methods

        public void Show()
        {
            if (_isShown && gameObject.activeSelf) return;

            _isShown = true;
            gameObject.SetActive(true);
            RestorePosition();
            WakeUp();
            BugTweener.To(_visual, BugTweener.CHANNEL_SCALE, SHOW_DURATION,
                t => _visual.localScale = Vector3.one * t, BugEase.OutBack);
        }

        public void Hide(bool animate)
        {
            if (!_isShown) return;

            _isShown = false;
            if (!animate || !gameObject.activeInHierarchy)
            {
                gameObject.SetActive(false);
                return;
            }

            var from = _visual.localScale.x;
            BugTweener.To(_visual, BugTweener.CHANNEL_SCALE, HIDE_DURATION,
                t => _visual.localScale = Vector3.one * Mathf.Lerp(from, 0f, t), BugEase.InCubic,
                () => gameObject.SetActive(false));
        }

        public void SetPendingCount(int count)
        {
            if (count == _badgeCount) return;

            var grew = count > _badgeCount && _badgeCount >= 0;
            _badgeCount = count;
            _badge.SetActive(count > 0);
            if (count <= 0) return;

            _badgeText.text = count > BADGE_MAX ? BADGE_MAX + "+" : count.ToString();
            if (!grew || !gameObject.activeInHierarchy) return;

            BugTweener.To(_badgeRect, BugTweener.CHANNEL_SCALE, BADGE_POP_DURATION,
                t => _badgeRect.localScale = Vector3.one * Mathf.LerpUnclamped(0.4f, 1f, t), BugEase.OutBack);
        }

        #endregion

        #region Drag Handlers

        public void OnPointerDown(PointerEventData eventData)
        {
            _isPressed = true;
            _isDragging = false;
            WakeUp();
            ScaleVisual(PRESS_SCALE, PRESS_DURATION, BugEase.OutCubic);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _isPressed = false;
            ScaleVisual(1f, RELEASE_DURATION, BugEase.OutBack);
            // EventSystem gửi PointerUp trước EndDrag ⇒ cờ của mình còn bật; eventData.dragging chặn thêm lớp nữa.
            if (!_isDragging && !eventData.dragging) Clicked?.Invoke();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            _isDragging = true;
            BugTweener.Kill(this, BugTweener.CHANNEL_MOVE);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!ToLocal(eventData.position, eventData.pressEventCamera, out var current) ||
                !ToLocal(eventData.position - eventData.delta, eventData.pressEventCamera, out var previous))
                return;

            _rect.anchoredPosition = Clamp(_rect.anchoredPosition + current - previous);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _isDragging = false;
            SnapToEdge();
        }

        #endregion

        #region Private Methods

        private bool ToLocal(Vector2 screen, Camera eventCamera, out Vector2 local)
        {
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent, screen, eventCamera, out local);
        }

        private void GetBounds(out Vector2 min, out Vector2 max)
        {
            // Nút neo giữa parent (anchor 0.5) ⇒ toạ độ anchoredPosition tính từ tâm.
            var half = _parent.rect.size * 0.5f;
            var self = _rect.rect.size * 0.5f;
            min = -half + self + Vector2.one * EDGE_PADDING;
            max = half - self - Vector2.one * EDGE_PADDING;
        }

        private Vector2 Clamp(Vector2 position)
        {
            GetBounds(out var min, out var max);
            return new Vector2(Mathf.Clamp(position.x, min.x, max.x), Mathf.Clamp(position.y, min.y, max.y));
        }

        private void SnapToEdge()
        {
            GetBounds(out var min, out var max);
            var from = _rect.anchoredPosition;
            var toRight = from.x >= 0f;
            var to = new Vector2(toRight ? max.x : min.x, Mathf.Clamp(from.y, min.y, max.y));

            SavePosition(toRight, Mathf.InverseLerp(min.y, max.y, to.y));
            BugTweener.To(this, BugTweener.CHANNEL_MOVE, SNAP_DURATION,
                t => _rect.anchoredPosition = Vector2.LerpUnclamped(from, to, t), BugEase.OutBack);
            WakeUp();
        }

        private void RestorePosition()
        {
            if (_parent == null || _parent.rect.width <= 0f) return;

            BugTweener.Kill(this, BugTweener.CHANNEL_MOVE);
            GetBounds(out var min, out var max);
            var toRight = PlayerPrefs.GetInt(PREF_SIDE, 1) == 1;
            var yRatio = PlayerPrefs.GetFloat(PREF_Y, DEFAULT_Y_RATIO);
            _rect.anchoredPosition = new Vector2(toRight ? max.x : min.x, Mathf.Lerp(min.y, max.y, yRatio));
        }

        private static void SavePosition(bool toRight, float yRatio)
        {
            PlayerPrefs.SetInt(PREF_SIDE, toRight ? 1 : 0);
            PlayerPrefs.SetFloat(PREF_Y, yRatio);
        }

        private void WakeUp()
        {
            _idleTimer = 0f;
            if (!_isIdle && _group.alpha >= 1f) return;

            _isIdle = false;
            FadeTo(1f, FADE_DURATION * 0.5f);
        }

        private void FadeTo(float alpha, float duration)
        {
            var from = _group.alpha;
            BugTweener.To(this, BugTweener.CHANNEL_ALPHA, duration,
                t => _group.alpha = Mathf.Lerp(from, alpha, t), BugEase.InOutSine);
        }

        private void ScaleVisual(float scale, float duration, BugEase ease)
        {
            var from = _visual.localScale.x;
            BugTweener.To(_visual, BugTweener.CHANNEL_SCALE, duration,
                t => _visual.localScale = Vector3.one * Mathf.LerpUnclamped(from, scale, t), ease);
        }

        #endregion
    }
}
