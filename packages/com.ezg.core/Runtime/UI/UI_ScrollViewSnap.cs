using System;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ezg.Core.UI
{
    public class UI_ScrollViewSnap : MonoBehaviour, IPointerDownHandler, IPointerExitHandler
    {
        #region Public Methods

        /// <summary>
        ///     ScrollRect change listener that handles auto-snapping to the nearest item when movement speed falls below a
        ///     threshold.
        /// </summary>
        /// <param name="value">The updated normalized scroll position coordinates.</param>
        public void ListenerMethod(Vector2 value)
        {
            //UpdateItemsScale();

            if (_isHold)
                return;

            if (_isSnaping)
                return;

            var count = _pointsContent.Count();
            var pointResult = 0f;

            if (Math.Abs(_scrollType == ScrollViewTypes.Horizontal ? _scrollRect.velocity.x : _scrollRect.velocity.y) <
                200f)
            {
                if (_scrollType == ScrollViewTypes.Horizontal)
                    if (_contentRectTransform.localPosition.x > 0 ||
                        _contentRectTransform.localPosition.x < _pointsContent[count - 1])
                        return;

                for (var i = 0; i < count - 1; i++)
                    if (_pointsContent[i] > _contentRectTransform.localPosition.x &&
                        _contentRectTransform.localPosition.x > _pointsContent[i + 1])
                    {
                        _scrollRect.velocity = Vector2.zero;
                        pointResult =
                            Math.Abs(_contentRectTransform.localPosition.x) - Math.Abs(_pointsContent[i]) <=
                            Math.Abs(_pointsContent[i + 1]) - Math.Abs(_contentRectTransform.localPosition.x)
                                ? _pointsContent[i]
                                : _pointsContent[i + 1];
                        transform.DOLocalMoveX(pointResult, .2f).SetUpdate(true);
                        _isSnaping = true;
                        return;
                    }
            }
        }

        #endregion

        #region Fields

        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình")] [Title("Loại scroll view")]
#endif
        private ScrollRect _scrollRect;

        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình")] [Title("Loại scroll view")]
#endif
        private ScrollViewTypes _scrollType;

        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình")] [Title("Scale item giữa")]
#endif
        private float _mainScale;

        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình")] [Title("Scale item bên ngoài")]
#endif
        private float _subScale;

        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình")] [Title("Khoảng cách giữ scale của item giữa")]
#endif
        private float _mainSpace;

        [SerializeField]
#if ODIN_INSPECTOR
        [TabGroup("Cấu hình")] [Title("Khoảng cách scale tăng giảm")]
#endif
        private float _subSpace;

        private HorizontalLayoutGroup _horizontalLayout;
        private VerticalLayoutGroup _verticalLayout;

        private enum ScrollViewTypes
        {
            Horizontal,
            Vertical
        }

        private RectTransform _contentRectTransform;
        private RectTransform _itemRectTransform;

        private List<Transform> _listItems;

        /// <summary>
        ///     Vị trí các item
        /// </summary>
        private List<float> _pointsItems;

        private List<float> _pointsContent;

        /// <summary>
        ///     Có nhấn giữ scroll ko
        /// </summary>
        private bool _isHold;

        /// <summary>
        ///     Có đang snap ko
        /// </summary>
        private bool _isSnaping;

        #endregion

        #region Initialize

        /// <summary>
        ///     Registers listeners and caches initial component references on Awake.
        /// </summary>
        private void Awake()
        {
            _scrollRect.onValueChanged.AddListener(ListenerMethod);
            _contentRectTransform = GetComponent<RectTransform>();
            switch (_scrollType)
            {
                case ScrollViewTypes.Horizontal:
                    _horizontalLayout = GetComponent<HorizontalLayoutGroup>();
                    break;
                case ScrollViewTypes.Vertical:
                    _verticalLayout = GetComponent<VerticalLayoutGroup>();
                    break;
            }
        }

        /// <summary>
        ///     Subscribes to enabling lifecycle to initialize positioning points.
        /// </summary>
        private void OnEnable()
        {
            InitPointItems();
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Dynamic scale updater that modifies items sizes depending on their relative distance from the scroll view center.
        /// </summary>
        private void UpdateItemsScale()
        {
            var count = _pointsContent.Count;

            for (var i = 0; i < count; i++)
            {
                var fitPoint = Mathf.Abs(_pointsContent[i]) + (_scrollType == ScrollViewTypes.Horizontal
                    ? _itemRectTransform.sizeDelta.x + _horizontalLayout.spacing
                    : _itemRectTransform.sizeDelta.y + _verticalLayout.spacing);
                if (Mathf.Abs(_pointsContent[i] - _contentRectTransform.localPosition.x) <= _mainSpace / 2)
                {
                    _listItems[i].localScale = new Vector3(_mainScale, _mainScale);
                }
                else
                {
                    var scale = _subScale + Mathf.Abs(_pointsContent[i] -
                                                      (_contentRectTransform.localPosition.x - _mainSpace / 2 -
                                                       _subSpace)) /
                        (_mainScale - _subScale);
                    _listItems[i].localScale = new Vector3(scale, scale);
                }
            }
        }

        /// <summary>
        ///     Measures and establishes reference snapping points for all child objects inside the ScrollRect layout.
        /// </summary>
        private void InitPointItems()
        {
            var childObject = transform.GetChild(0);
            if (childObject == null)
                return;

            _itemRectTransform = childObject.GetComponent<RectTransform>();

            _listItems = new List<Transform>();
            _pointsItems = new List<float>();
            _pointsContent = new List<float>();
            switch (_scrollType)
            {
                case ScrollViewTypes.Horizontal:
                    for (var i = 0; i < transform.childCount; i++)
                    {
                        _pointsItems.Add(_horizontalLayout.padding.left + _itemRectTransform.sizeDelta.x / 2 +
                                         i * (_itemRectTransform.sizeDelta.x + _horizontalLayout.spacing));
                        _pointsContent.Add(i * -(_itemRectTransform.sizeDelta.x + _horizontalLayout.spacing));
                    }

                    break;
                case ScrollViewTypes.Vertical:
                    for (var i = 0; i < transform.childCount; i++)
                    {
                        _pointsItems.Add(_verticalLayout.padding.left + _itemRectTransform.sizeDelta.y / 2 +
                                         i * (_itemRectTransform.sizeDelta.y + _verticalLayout.spacing));
                        _pointsContent.Add(i * -(_itemRectTransform.sizeDelta.y + _verticalLayout.spacing));
                    }

                    break;
            }

            foreach (Transform tran in transform) _listItems.Add(tran);
        }

        #endregion

        #region Event Handlers

        /// <summary>
        ///     Handler for Pointer Down input event to record hold status and pause auto-snapping.
        /// </summary>
        /// <param name="data">The event details of the current pointer press.</param>
        public void OnPointerDown(PointerEventData data)
        {
            _isSnaping = false;
            _isHold = true;
        }

        /// <summary>
        ///     Handler for Pointer Exit input event to release hold status.
        /// </summary>
        /// <param name="eventData">The event details of the current pointer exit.</param>
        public void OnPointerExit(PointerEventData eventData)
        {
            _isHold = false;
        }

        #endregion
    }
}