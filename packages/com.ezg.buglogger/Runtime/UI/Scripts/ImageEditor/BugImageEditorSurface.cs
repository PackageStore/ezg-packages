using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Ezg.BugLogger.UI
{
    /// <summary>
    ///     Vùng nhận chạm của màn chỉnh sửa ảnh: chỉ theo một ngón (ngón đầu tiên), di chuyển báo ngay từ pixel đầu
    ///     (tắt ngưỡng kéo của EventSystem để nét bút không hụt đoạn đầu).
    /// </summary>
    internal sealed class BugImageEditorSurface : MonoBehaviour, IInitializePotentialDragHandler, IPointerDownHandler,
        IDragHandler, IPointerUpHandler
    {
        #region Fields

        private bool _isTracking;
        private int _pointerId;

        #endregion

        #region Callbacks

        public event Action<PointerEventData> Pressed;
        public event Action<PointerEventData> Dragged;
        public event Action<PointerEventData> Released;

        /// <summary>Thao tác bị cắt ngang (màn tắt giữa chừng) — không có Released.</summary>
        public event Action Cancelled;

        #endregion

        #region Initialize

        private void OnDisable()
        {
            if (!_isTracking) return;

            _isTracking = false;
            Cancelled?.Invoke();
        }

        #endregion

        #region Public Methods

        /// <summary>Bỏ theo dõi ngón đang chạm (không báo Released).</summary>
        public void Cancel()
        {
            _isTracking = false;
        }

        #endregion

        #region Events

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            eventData.useDragThreshold = false;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_isTracking || eventData.button != PointerEventData.InputButton.Left) return;

            _isTracking = true;
            _pointerId = eventData.pointerId;
            Pressed?.Invoke(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_isTracking || eventData.pointerId != _pointerId) return;

            Dragged?.Invoke(eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!_isTracking || eventData.pointerId != _pointerId) return;

            _isTracking = false;
            Released?.Invoke(eventData);
        }

        #endregion
    }
}
