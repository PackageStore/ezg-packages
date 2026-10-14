using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ezg.BugLogger.UI
{
    /// <summary>Phản hồi chạm: thu nhỏ khi nhấn, bật lại khi nhả. Không đụng logic click của Button.</summary>
    [DisallowMultipleComponent]
    internal sealed class BugLoggerPressable : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IPointerExitHandler
    {
        #region Fields

        private const float PRESS_DURATION = 0.08f;
        private const float RELEASE_DURATION = 0.28f;

        [SerializeField] private RectTransform _target;
        [SerializeField] private float _pressedScale = 0.94f;

        private Selectable _selectable;
        private bool _isPressed;

        #endregion

        #region Initialize

        private void Awake()
        {
            if (_target == null) _target = (RectTransform)transform;
            _selectable = GetComponent<Selectable>();
        }

        private void OnDisable()
        {
            BugTweener.Kill(_target, BugTweener.CHANNEL_SCALE);
            _target.localScale = Vector3.one;
            _isPressed = false;
        }

        #endregion

        #region Events

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_selectable != null && !_selectable.IsInteractable()) return;

            _isPressed = true;
            AnimateTo(_pressedScale, PRESS_DURATION, BugEase.OutCubic);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            Release();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            Release();
        }

        #endregion

        #region Private Methods

        private void Release()
        {
            if (!_isPressed) return;

            _isPressed = false;
            AnimateTo(1f, RELEASE_DURATION, BugEase.OutBack);
        }

        private void AnimateTo(float scale, float duration, BugEase ease)
        {
            var from = _target.localScale.x;
            BugTweener.To(_target, BugTweener.CHANNEL_SCALE, duration,
                t => _target.localScale = Vector3.one * Mathf.LerpUnclamped(from, scale, t), ease);
        }

        #endregion
    }
}
