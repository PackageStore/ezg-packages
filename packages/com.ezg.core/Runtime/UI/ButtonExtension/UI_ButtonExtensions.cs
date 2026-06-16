using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ezg.Core.UI
{
    /// <summary>
    ///     Control UI mở rộng cho button
    /// </summary>
    public class UI_ButtonExtensions : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler
    {
        #region Fields

        private RectTransform _rectTransform;

        private RectTransform RectTransform
        {
            get
            {
                if (_rectTransform == null) _rectTransform = GetComponent<RectTransform>();
                return _rectTransform;
            }
            set => _rectTransform = value;
        }

        #region Cấu hình hiển thị

        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình hiển thị")]
        [OnValueChanged("ApplyButtonSize")]
        [ValueDropdown("_buttonWidthSizeList")]
        [Title("Kích thước rộng")]
        [InfoBox("Kích thước Không hoạt động với anchor này.", InfoMessageType.Error,
            VisibleIf =
                "@RectTransform.anchorMax.x != RectTransform.anchorMin.x || RectTransform.anchorMax.y != RectTransform.anchorMin.y")]
#endif
        private string _btnWidthSize;

        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình hiển thị")]
        [OnValueChanged("ApplyButtonSize")]
        [ValueDropdown("_buttonHightSizeList")]
        [Title("Kích thước cao")]
        [InfoBox("Kích thước Không hoạt động với anchor này.", InfoMessageType.Error,
            VisibleIf =
                "@RectTransform.anchorMax.x != RectTransform.anchorMin.x || RectTransform.anchorMax.y != RectTransform.anchorMin.y")]
#endif
        private string _btnHightSize;

        private readonly string[] _buttonWidthSizeList = _buttonWidthSize.Select(x => x.Value).ToArray();
        private readonly string[] _buttonHightSizeList = _buttonHightSize.Select(x => x.Value).ToArray();

        private static readonly Dictionary<float, string> _buttonWidthSize = new()
        {
            { 50, "Width = 50" },
            { 100, "Width = 100" },
            { 150, "Width = 150" },
            { 200, "Width = 200" },
            { 250, "Width = 250" },
            { 300, "Width = 300" },
            { 350, "Width = 350" },
            { 400, "Width = 400" },
            { 450, "Width = 450" },
            { 500, "Width = 500" },
            { 550, "Width = 550" },
            { 600, "Width = 600" }
        };

        private static readonly Dictionary<float, string> _buttonHightSize = new()
        {
            { 50, "High = 50" },
            { 60, "High = 60" },
            { 70, "High = 70" },
            { 80, "High = 80" },
            { 90, "High = 90" },
            { 100, "High = 100" },
            { 110, "High = 110" },
            { 120, "High = 120" },
            { 130, "High = 130" },
            { 140, "High = 140" },
            { 150, "High = 150" },
            { 160, "High = 160" },
            { 170, "High = 170" },
            { 180, "High = 180" },
            { 190, "High = 190" },
            { 200, "High = 200" }
        };

        private readonly float[] _textSizeList =
        {
            20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 75, 80, 85, 90, 95, 100, 105, 110
        };

        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình hiển thị")] [Title("Chữ trong button")]
#endif
        private Text _textButton;

        [SerializeField]
#if ODIN_INSPECTOR
        [ShowIf("_textButton")]
        [TabGroup("Cấu hình hiển thị")]
        [OnValueChanged("ApplyTextSize")]
        [ValueDropdown("_textSizeList")]
        [Title("Kích thước chữ")]
#endif
        private float _textSize;

        private Vector2 _currentPivot;

        #endregion

        #region Cấu hình nhấn

        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình nhấn")] [Title("Thu nhỏ size khi nhấn hay không")]
#endif
        private bool _isChangeSizeOnClick = true;

        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình nhấn")]
        [ShowIf("_isChangeSizeOnClick")]
        [Title("Kích thước khi nhấn giữ (1 = 100%)")]
#endif
        private float _onDownScalePercent = 0.9f;

        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình nhấn")] [ShowIf("_isChangeSizeOnClick")] [Title("Thời gian thay đổi size")]
#endif
        private float _holdDuration = 0.2f;

        private Vector2 _originScale;
        private bool _setOriginal;

        #endregion

        private UnityAction _onDown, _onUp, _onHold;

        #endregion

        #region Public Methods

        /// <summary>
        ///     Registers a callback for pointer down events.
        /// </summary>
        /// <param name="action">The UnityAction callback to register.</param>
        public void RegEventOnDown(UnityAction action)
        {
            _onDown = action;
        }

        /// <summary>
        ///     Registers a callback for pointer up events.
        /// </summary>
        /// <param name="action">The UnityAction callback to register.</param>
        public void RegEventOnUp(UnityAction action)
        {
            _onUp = action;
        }

        /// <summary>
        ///     Registers a callback for pointer enter/hover events.
        /// </summary>
        /// <param name="action">The UnityAction callback to register.</param>
        public void RegEventOnHold(UnityAction action)
        {
            _onHold = action;
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Applies the selected button size based on width and height settings if anchors permit.
        /// </summary>
        private void ApplyButtonSize()
        {
            if (string.IsNullOrEmpty(_btnWidthSize) || string.IsNullOrEmpty(_btnHightSize)) return;

            var thisRect = GetComponent<RectTransform>();
            _currentPivot = thisRect.pivot;

            if (thisRect.anchorMax.x == thisRect.anchorMin.x && thisRect.anchorMax.y == thisRect.anchorMin.y)
            {
                SetPivot(GetComponent<RectTransform>(), new Vector2(thisRect.anchorMin.x, thisRect.anchorMin.y));
                var width = _buttonWidthSize.FirstOrDefault(x => x.Value == _btnWidthSize).Key;
                var high = _buttonHightSize.FirstOrDefault(x => x.Value == _btnHightSize).Key;
                GetComponent<RectTransform>().sizeDelta = new Vector2(width, high);
                SetPivot(GetComponent<RectTransform>(), _currentPivot);
            }
        }

        /// <summary>
        ///     Sets the pivot of a RectTransform without changing its relative position on the screen.
        /// </summary>
        /// <param name="rectTransform">The target RectTransform.</param>
        /// <param name="pivot">The new pivot coordinate vector.</param>
        private void SetPivot(RectTransform rectTransform, Vector2 pivot)
        {
            if (rectTransform == null) return;

            var size = rectTransform.rect.size;
            var deltaPivot = rectTransform.pivot - pivot;
            var deltaPosition = new Vector3(deltaPivot.x * size.x, deltaPivot.y * size.y);
            rectTransform.pivot = pivot;
            rectTransform.localPosition -= deltaPosition;
        }

        /// <summary>
        ///     Updates the text element font size to match configuration.
        /// </summary>
        private void ApplyTextSize()
        {
            if (_textButton == null)
                return;

            _textButton.fontSize = (int)_textSize;
        }

        #endregion

        #region Event Handlers

        /// <summary>
        ///     Event handler triggered when pointer down occurs; initiates click size scaling.
        /// </summary>
        /// <param name="data">The event details of the current pointer press.</param>
        public void OnPointerDown(PointerEventData data)
        {
            _onDown?.Invoke();

            if (!_setOriginal)
            {
                _originScale = transform.localScale;
                _setOriginal = true;
            }

            if (_isChangeSizeOnClick)
                transform.DOScale(_originScale * _onDownScalePercent, _holdDuration).SetUpdate(true);
        }

        /// <summary>
        ///     Event handler triggered when pointer up occurs; resets click size scaling.
        /// </summary>
        /// <param name="data">The event details of the current pointer release.</param>
        public void OnPointerUp(PointerEventData data)
        {
            _onUp?.Invoke();

            if (_isChangeSizeOnClick)
                transform.DOScale(_originScale, _holdDuration).SetUpdate(true);
        }

        /// <summary>
        ///     Event handler triggered when pointer enters hover bounds.
        /// </summary>
        /// <param name="eventData">The event details of the pointer entry.</param>
        public void OnPointerEnter(PointerEventData eventData)
        {
            _onHold?.Invoke();
        }

        #endregion
    }
}