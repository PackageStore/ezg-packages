using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ezg.Core.UI
{
    /// <summary>
    ///     Handles drag-and-drop interaction for a UI element.
    /// </summary>
    public class DragDropController : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IEndDragHandler,
        IDragHandler
    {
        public Canvas _thisCanvas;
        private Action _onBeginDrag;
        private Action _onEndDrag;
        private Vector3 _originalPos;
        private Image _thisImage;
        private RectTransform _thisRect;

        /// <summary>
        ///     Caches required UI components.
        /// </summary>
        private void Awake()
        {
            _thisRect = GetComponent<RectTransform>();
            _thisImage = GetComponent<Image>();
        }

        /// <summary>
        ///     Stores the starting anchored position when the object becomes enabled.
        /// </summary>
        private void OnEnable()
        {
            _originalPos = _thisRect.anchoredPosition;
        }

        /// <inheritdoc />
        void IBeginDragHandler.OnBeginDrag(PointerEventData eventData)
        {
            _thisImage.raycastTarget = false;
            _onBeginDrag?.Invoke();
        }

        /// <inheritdoc />
        public void OnDrag(PointerEventData eventData)
        {
            _thisRect.anchoredPosition += eventData.delta / _thisCanvas.scaleFactor;
        }

        /// <inheritdoc />
        public void OnEndDrag(PointerEventData eventData)
        {
            _thisImage.raycastTarget = true;
            _onEndDrag?.Invoke();
        }

        /// <inheritdoc />
        public void OnPointerDown(PointerEventData eventData)
        {
        }

        /// <summary>
        ///     Assigns the canvas used to scale drag delta movement.
        /// </summary>
        /// <param name="canvas">The canvas that hosts this draggable object.</param>
        public void SetCanvas(Canvas canvas)
        {
            _thisCanvas = canvas;
        }

        /// <summary>
        ///     Registers a callback invoked when dragging begins.
        /// </summary>
        /// <param name="onReset">The callback to register.</param>
        public void RegisterOnBeginDrag(Action onReset)
        {
            _onBeginDrag += onReset;
        }

        /// <summary>
        ///     Registers a callback invoked when dragging ends.
        /// </summary>
        /// <param name="action">The callback to register.</param>
        public void RegisterOnEndDrag(Action action)
        {
            _onEndDrag -= action;
            _onEndDrag += action;
        }
    }
}