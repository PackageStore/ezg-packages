using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ezg.BugLogger.UI
{
    /// <summary>Công cụ của màn chỉnh sửa ảnh. Giá trị = chỉ số trong mảng nút công cụ của prefab.</summary>
    internal enum BugEditorTool
    {
        Crop,
        Rect,
        Ellipse,
        Arrow,
        Pen,
        Marker,
        Spotlight
    }

    /// <summary>
    ///     Màn chỉnh sửa ảnh chụp trước khi gửi (kiểu công cụ chụp màn hình): cắt, khoanh khung / elip, mũi tên, bút vẽ
    ///     tay, huy hiệu đánh số, làm nổi một vùng (phần còn lại tối đi); hoàn tác / làm lại / xoá hết; 5 màu nét.
    ///     Sửa trên bản sao của <see cref="BugImageDocument" />: Xong → dựng ảnh mới (<see cref="BugImageFlattener" />)
    ///     và trả bản sửa cho form; Huỷ / Back → bỏ (có hỏi lại khi đã sửa). Toàn bộ chạy trong game, không đụng thư
    ///     viện ảnh của máy ⇒ không cần quyền gì.
    /// </summary>
    internal sealed class BugLoggerImageEditor : MonoBehaviour
    {
        #region Fields

        private const float SHOW_DURATION = 0.28f;
        private const float HIDE_DURATION = 0.18f;
        private const float FROM_SCALE = 0.96f;
        private const float DISABLED_ALPHA = 0.3f;
        private const float DISCARD_CONFIRM_SECONDS = 2.5f;

        // Kích thước theo cạnh ngắn của vùng đang xem (px ảnh gốc): cắt nhỏ rồi vẽ thì nét mảnh theo, nhìn vẫn vừa.
        private const float STROKE_RATIO = 0.009f;
        private const float MIN_STROKE = 3f;
        private const float MARKER_RATIO = 0.032f;
        private const float QUICK_SHAPE_RATIO = 0.22f;
        private const float QUICK_RECT_ASPECT = 1.4f;
        private const float QUICK_ARROW_RATIO = 0.2f;

        // Theo bội số độ dày nét.
        private const float MIN_SHAPE_STROKES = 4f;
        private const float MIN_ARROW_STROKES = 5f;
        private const float PEN_STEP_STROKES = 0.3f;
        private const float PEN_SIMPLIFY_STROKES = 0.12f;
        private const int MAX_PEN_POINTS = 1500;

        // Đơn vị canvas.
        private const float TAP_DISTANCE = 16f;
        private const float CROP_HANDLE_REACH = 64f;
        private const float CROP_INNER_REACH_SHARE = 0.3f;

        [Header("Khung")]
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private RectTransform _content;

        [Header("Thanh trên")]
        [SerializeField] private Button _closeButton;
        [SerializeField] private Button _undoButton;
        [SerializeField] private CanvasGroup _undoGroup;
        [SerializeField] private Button _redoButton;
        [SerializeField] private CanvasGroup _redoGroup;
        [SerializeField] private Button _doneButton;

        [Header("Ảnh")]
        [SerializeField] private BugImageEditorSurface _surface;
        [SerializeField] private RectTransform _fit;
        [SerializeField] private AspectRatioFitter _fitter;
        [SerializeField] private RawImage _image;
        [SerializeField] private BugAnnotationGraphic _dimLayer;
        [SerializeField] private BugAnnotationGraphic _shapeLayer;
        [SerializeField] private BugAnnotationGraphic _pendingLayer;
        [SerializeField] private BugCropOverlayGraphic _cropOverlay;
        [SerializeField] private Texture2D _atlas;

        [Header("Công cụ")]
        [SerializeField] private Text _hint;
        [SerializeField] private CanvasGroup _colorGroup;
        [SerializeField] private BugLoggerSwatch[] _swatches;
        [SerializeField] private BugLoggerToolButton[] _toolButtons;
        [SerializeField] private Button _clearButton;
        [SerializeField] private CanvasGroup _clearGroup;

        // Công cụ / màu QA chọn lần trước — giữ trong phiên.
        private static BugEditorTool _lastTool = BugEditorTool.Rect;
        private static int _lastColor;

        private readonly List<Vector2> _penPoints = new List<Vector2>(256);
        private readonly List<Vector2> _shapePoints = new List<Vector2>(2);

        private BugLoggerOverlay _overlay;
        private Func<Texture> _sourceProvider;
        private BugImageDocument _document;
        private Action<BugImageDocument, byte[]> _onDone;
        private BugEditorTool _tool;
        private int _colorIndex;
        private bool _isClosing;
        private bool _isDirty;
        private float _discardArmedUntil;
        private float _contentScale = 1f;

        private bool _isGesture;
        private bool _hasMoved;
        private Vector2 _startLocal;
        private Vector2 _startImage;
        private Vector2 _currentImage;

        private bool _isCropGesture;
        private bool _cropMove;
        private bool _cropNew;
        private bool _grabLeft;
        private bool _grabRight;
        private bool _grabBottom;
        private bool _grabTop;
        private Rect _cropStart;
        private Rect _cropDraft;

        #endregion

        #region Properties

        /// <summary>Đang hiện (tính cả lúc chạy animation đóng).</summary>
        public bool IsOpen => gameObject.activeSelf;

        private Color32 CurrentColor => BugLoggerPalette.AnnotationColors[_colorIndex];

        #endregion

        #region Initialize

        public void Bind(BugLoggerOverlay overlay)
        {
            _overlay = overlay;
        }

        private void Awake()
        {
            _closeButton.onClick.AddListener(OnCloseClicked);
            _doneButton.onClick.AddListener(OnDoneClicked);
            _undoButton.onClick.AddListener(OnUndoClicked);
            _redoButton.onClick.AddListener(OnRedoClicked);
            _clearButton.onClick.AddListener(OnClearClicked);

            _surface.Pressed += OnPressed;
            _surface.Dragged += OnDragged;
            _surface.Released += OnReleased;
            _surface.Cancelled += OnGestureCancelled;

            for (var i = 0; i < _toolButtons.Length; i++) _toolButtons[i].Clicked += OnToolClicked;

            var colors = BugLoggerPalette.AnnotationColors;
            for (var i = 0; i < _swatches.Length; i++)
            {
                _swatches[i].Setup(colors[Mathf.Min(i, colors.Length - 1)]);
                _swatches[i].Clicked += OnSwatchClicked;
            }
        }

        // Editor tắt Reload Domain thì static sống qua các lần Play.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _lastTool = BugEditorTool.Rect;
            _lastColor = 0;
        }

        private void OnDestroy()
        {
            BugTweener.Kill(this);
            BugTweener.Kill(_group);
        }

        private void Update()
        {
            // Form có thể thay ảnh gốc (dựng lại sau khi GPU mất context) — luôn hiện đúng ảnh nó đang giữ.
            var source = _sourceProvider?.Invoke();
            if (source != null && _image.texture != source) _image.texture = source;
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Mở màn chỉnh sửa trên ảnh chụp gốc (<paramref name="sourceProvider" /> — caller giữ quyền sở hữu, có thể
        ///     thay ảnh khi bị mất nội dung) với bản sửa hiện có. Xong khi đã sửa → <paramref name="onDone" />(bản sửa mới,
        ///     JPG mới — null khi đã hoàn tác về ảnh gốc).
        /// </summary>
        public void Open(Func<Texture> sourceProvider, BugImageDocument document,
            Action<BugImageDocument, byte[]> onDone)
        {
            var source = sourceProvider?.Invoke();
            if (source == null || document == null) return;

            BugTweener.Kill(this);
            BugTweener.Kill(_group);
            _sourceProvider = sourceProvider;
            _document = document.Clone();
            _onDone = onDone;
            _isClosing = false;
            _isDirty = false;
            _discardArmedUntil = 0f;
            _isGesture = false;
            _isCropGesture = false;
            _tool = _lastTool;
            _colorIndex = Mathf.Clamp(_lastColor, 0, BugLoggerPalette.AnnotationColors.Length - 1);

            gameObject.SetActive(true);
            _group.blocksRaycasts = true;
            _image.texture = source;
            ApplyView();
            RefreshTools(false);
            RefreshSwatches(false);
            RefreshButtons();
            PlayShow();
        }

        /// <summary>Phím Back: bỏ nét đang vẽ dở, hoặc đóng (đã sửa thì phải bấm lần nữa).</summary>
        public void HandleBack()
        {
            if (_isGesture)
            {
                CancelGesture();
                return;
            }

            RequestCancel();
        }

        /// <summary>Đóng ngay, không animation, bỏ phần đang sửa (form đóng / ảnh sắp bị huỷ).</summary>
        public void Close()
        {
            CancelGesture();
            BugTweener.Kill(this);
            BugTweener.Kill(_group);
            _isClosing = false;
            gameObject.SetActive(false);
            _image.texture = null;
            _dimLayer.Release();
            _shapeLayer.Release();
            _pendingLayer.Release();
            _sourceProvider = null;
            _document = null;
            _onDone = null;
        }

        #endregion

        #region Private Methods — open / close

        private void PlayShow()
        {
            _group.alpha = 0f;
            SetContentScale(FROM_SCALE);
            BugTweener.To(_group, BugTweener.CHANNEL_ALPHA, SHOW_DURATION, t => _group.alpha = t, BugEase.OutCubic);
            BugTweener.To(this, BugTweener.CHANNEL_SCALE, SHOW_DURATION,
                t => SetContentScale(Mathf.LerpUnclamped(FROM_SCALE, 1f, t)), BugEase.OutQuint);
        }

        private void Hide()
        {
            if (_isClosing) return;

            _isClosing = true;
            CancelGesture();
            _group.blocksRaycasts = false;
            var fromAlpha = _group.alpha;
            var fromScale = _contentScale;
            BugTweener.To(this, BugTweener.CHANNEL_SCALE, HIDE_DURATION,
                t => SetContentScale(Mathf.Lerp(fromScale, FROM_SCALE, t)), BugEase.InCubic);
            BugTweener.To(_group, BugTweener.CHANNEL_ALPHA, HIDE_DURATION, t => _group.alpha = Mathf.Lerp(fromAlpha, 0f, t),
                BugEase.InCubic, Close);
        }

        private void SetContentScale(float scale)
        {
            _contentScale = scale;
            _content.localScale = new Vector3(scale, scale, 1f);
        }

        private void RequestCancel()
        {
            if (_isClosing) return;

            if (!_isDirty || Time.realtimeSinceStartup < _discardArmedUntil)
            {
                Hide();
                return;
            }

            _discardArmedUntil = Time.realtimeSinceStartup + DISCARD_CONFIRM_SECONDS;
            ShowToast(BugLoggerStrings.EDITOR_DISCARD_CONFIRM, BugToastKind.Info);
        }

        private void ShowToast(string message, BugToastKind kind)
        {
            if (_overlay != null) _overlay.ShowToast(message, kind);
        }

        #endregion

        #region Private Methods — view

        /// <summary>Vùng ảnh đang hiện (px ảnh gốc): cả ảnh khi đang cắt, còn lại là vùng đã cắt.</summary>
        private Rect GetView()
        {
            return _tool == BugEditorTool.Crop ? _document.FullRect : _document.Crop;
        }

        private void ApplyView()
        {
            var view = GetView();
            var full = _document.FullRect;
            _fitter.aspectRatio = view.width / Mathf.Max(1f, view.height);
            _image.uvRect = new Rect(view.x / full.width, view.y / full.height, view.width / full.width,
                view.height / full.height);
            _dimLayer.SetSource(_document, view);
            _shapeLayer.SetSource(_document, view);
            _pendingLayer.SetSource(_document, view);

            var isCropping = _tool == BugEditorTool.Crop;
            _cropOverlay.gameObject.SetActive(isCropping);
            if (isCropping) _cropOverlay.Set(view, _isCropGesture ? _cropDraft : _document.Crop);
        }

        private void RefreshTools(bool animate)
        {
            for (var i = 0; i < _toolButtons.Length; i++) _toolButtons[i].SetSelected(i == (int)_tool, animate);

            var usesColor = _tool != BugEditorTool.Crop;
            _colorGroup.interactable = usesColor;
            _colorGroup.blocksRaycasts = usesColor;
            _colorGroup.alpha = usesColor ? 1f : DISABLED_ALPHA;
            _hint.text = GetHint(_tool);
        }

        private void RefreshSwatches(bool animate)
        {
            for (var i = 0; i < _swatches.Length; i++) _swatches[i].SetSelected(i == _colorIndex, animate);
        }

        private void RefreshButtons()
        {
            SetEnabled(_undoButton, _undoGroup, _document.CanUndo);
            SetEnabled(_redoButton, _redoGroup, _document.CanRedo);
            SetEnabled(_clearButton, _clearGroup, !_document.IsPristine);
        }

        private static void SetEnabled(Button button, CanvasGroup group, bool isEnabled)
        {
            button.interactable = isEnabled;
            group.alpha = isEnabled ? 1f : DISABLED_ALPHA;
        }

        private static string GetHint(BugEditorTool tool)
        {
            switch (tool)
            {
                case BugEditorTool.Crop: return BugLoggerStrings.HINT_CROP;
                case BugEditorTool.Rect: return BugLoggerStrings.HINT_RECT;
                case BugEditorTool.Ellipse: return BugLoggerStrings.HINT_ELLIPSE;
                case BugEditorTool.Arrow: return BugLoggerStrings.HINT_ARROW;
                case BugEditorTool.Pen: return BugLoggerStrings.HINT_PEN;
                case BugEditorTool.Marker: return BugLoggerStrings.HINT_MARKER;
                default: return BugLoggerStrings.HINT_SPOTLIGHT;
            }
        }

        /// <summary>Đã đổi bản sửa (thêm nét, cắt, hoàn tác…): vẽ lại, cập nhật nút.</summary>
        private void MarkChanged()
        {
            _isDirty = true;
            _discardArmedUntil = 0f;
            ApplyView();
            RefreshButtons();
        }

        #endregion

        #region Private Methods — gesture

        private void CancelGesture()
        {
            _surface.Cancel();
            FinishGesture(false);
        }

        private void Track(Vector2 local)
        {
            if (!_hasMoved && (local - _startLocal).sqrMagnitude >= TAP_DISTANCE * TAP_DISTANCE) _hasMoved = true;

            _currentImage = LocalToImage(local, true);
            switch (_tool)
            {
                case BugEditorTool.Crop:
                    UpdateCrop(local);
                    break;
                case BugEditorTool.Pen:
                    if (AddPenPoint(_currentImage)) UpdatePending();
                    break;
                case BugEditorTool.Marker:
                    UpdatePending();
                    break;
                default:
                    if (_hasMoved) UpdatePending();
                    break;
            }
        }

        private void FinishGesture(bool commit)
        {
            if (!_isGesture) return;

            _isGesture = false;
            if (_tool == BugEditorTool.Crop)
            {
                EndCrop(commit);
            }
            else if (commit)
            {
                var item = BuildItem();
                if (item != null) AddItem(item);
            }

            _pendingLayer.ClearPending();
            _dimLayer.ClearPending();
            _penPoints.Clear();
        }

        /// <summary>Thêm điểm vào nét bút nếu đủ xa điểm trước (bỏ rung tay, giới hạn số điểm). False khi bỏ qua.</summary>
        private bool AddPenPoint(Vector2 point)
        {
            if (_penPoints.Count >= MAX_PEN_POINTS) return false;

            var step = GetStrokeWidth() * PEN_STEP_STROKES;
            if (_penPoints.Count > 0 && (point - _penPoints[_penPoints.Count - 1]).sqrMagnitude < step * step)
                return false;

            _penPoints.Add(point);
            return true;
        }

        private void UpdatePending()
        {
            var color = CurrentColor;
            switch (_tool)
            {
                case BugEditorTool.Pen:
                    _pendingLayer.SetPending(BugAnnotationKind.Pen, color, GetStrokeWidth(), _penPoints, 0);
                    break;
                case BugEditorTool.Marker:
                    _shapePoints.Clear();
                    _shapePoints.Add(GetMarkerCenter());
                    _pendingLayer.SetPending(BugAnnotationKind.Marker, color, GetMarkerRadius(), _shapePoints,
                        _document.NextMarkerNumber);
                    break;
                case BugEditorTool.Arrow:
                    GetArrow(out var tail, out var head);
                    _shapePoints.Clear();
                    _shapePoints.Add(tail);
                    _shapePoints.Add(head);
                    _pendingLayer.SetPending(BugAnnotationKind.Arrow, color, GetStrokeWidth(), _shapePoints, 0);
                    break;
                case BugEditorTool.Rect:
                case BugEditorTool.Ellipse:
                case BugEditorTool.Spotlight:
                    var box = GetShapeBox();
                    _shapePoints.Clear();
                    _shapePoints.Add(box.min);
                    _shapePoints.Add(box.max);
                    _pendingLayer.SetPending(ToKind(_tool), color, GetStrokeWidth(), _shapePoints, 0);
                    if (_tool == BugEditorTool.Spotlight) _dimLayer.SetExtraHole(box);
                    break;
            }
        }

        private BugAnnotation BuildItem()
        {
            var color = CurrentColor;
            var width = GetStrokeWidth();
            switch (_tool)
            {
                case BugEditorTool.Pen:
                    if (_penPoints.Count == 0) return null;

                    return new BugAnnotation(BugAnnotationKind.Pen, color, width,
                        BugPolyline.Simplify(_penPoints, width * PEN_SIMPLIFY_STROKES));
                case BugEditorTool.Marker:
                    return new BugAnnotation(BugAnnotationKind.Marker, color, GetMarkerRadius(),
                        new[] { GetMarkerCenter() }, _document.NextMarkerNumber);
                case BugEditorTool.Arrow:
                    GetArrow(out var tail, out var head);
                    return new BugAnnotation(BugAnnotationKind.Arrow, color, width, new[] { tail, head });
                case BugEditorTool.Rect:
                case BugEditorTool.Ellipse:
                case BugEditorTool.Spotlight:
                    var box = GetShapeBox();
                    return new BugAnnotation(ToKind(_tool), color, width, new[] { box.min, box.max });
                default:
                    return null;
            }
        }

        private void AddItem(BugAnnotation item)
        {
            switch (_document.TryAdd(item))
            {
                case BugAddResult.Added:
                    MarkChanged();
                    break;
                case BugAddResult.SpotlightLimit:
                    ShowToast(string.Format(BugLoggerStrings.EDITOR_SPOTLIGHT_LIMIT_FORMAT,
                        BugImageDocument.MAX_SPOTLIGHTS), BugToastKind.Info);
                    break;
                case BugAddResult.MarkerLimit:
                    ShowToast(string.Format(BugLoggerStrings.EDITOR_MARKER_LIMIT_FORMAT, BugImageDocument.MAX_MARKER),
                        BugToastKind.Info);
                    break;
                default:
                    ShowToast(BugLoggerStrings.EDITOR_LIMIT, BugToastKind.Info);
                    break;
            }
        }

        /// <summary>
        ///     Khung của Khung / Elip / Làm nổi: kéo = từ điểm chạm tới ngón tay; chạm (không kéo) = khung mặc định quanh
        ///     điểm chạm. Không nhỏ hơn vài lần độ dày nét, luôn nằm trong vùng đang xem.
        /// </summary>
        private Rect GetShapeBox()
        {
            Rect box;
            if (_hasMoved)
            {
                box = Rect.MinMaxRect(Mathf.Min(_startImage.x, _currentImage.x), Mathf.Min(_startImage.y, _currentImage.y),
                    Mathf.Max(_startImage.x, _currentImage.x), Mathf.Max(_startImage.y, _currentImage.y));
            }
            else
            {
                var size = GetShortEdge() * QUICK_SHAPE_RATIO;
                var width = _tool == BugEditorTool.Rect ? size * QUICK_RECT_ASPECT : size;
                box = new Rect(_startImage.x - width * 0.5f, _startImage.y - size * 0.5f, width, size);
            }

            var minSide = GetStrokeWidth() * MIN_SHAPE_STROKES;
            if (box.width < minSide) box = new Rect(box.center.x - minSide * 0.5f, box.y, minSide, box.height);
            if (box.height < minSide) box = new Rect(box.x, box.center.y - minSide * 0.5f, box.width, minSide);
            return FitIntoView(box);
        }

        /// <summary>
        ///     Mũi tên: kéo = từ chỗ bắt đầu (đuôi) tới chỗ thả (đầu). Chạm, hoặc kéo quá ngắn = mũi tên dựng sẵn chỉ vào
        ///     điểm đó, đuôi hướng về phía giữa ảnh.
        /// </summary>
        private void GetArrow(out Vector2 tail, out Vector2 head)
        {
            var minLength = GetStrokeWidth() * MIN_ARROW_STROKES;
            if (_hasMoved && (_currentImage - _startImage).sqrMagnitude >= minLength * minLength)
            {
                tail = _startImage;
                head = _currentImage;
                return;
            }

            head = _hasMoved ? _currentImage : _startImage;
            var direction = GetView().center - head;
            if (direction.sqrMagnitude < 1f) direction = new Vector2(1f, -1f);
            tail = ClampToView(head + direction.normalized * (GetShortEdge() * QUICK_ARROW_RATIO));
        }

        private Vector2 GetMarkerCenter()
        {
            var view = GetView();
            var margin = Mathf.Min(GetMarkerRadius() * 1.2f, Mathf.Min(view.width, view.height) * 0.5f);
            return new Vector2(Mathf.Clamp(_currentImage.x, view.xMin + margin, view.xMax - margin),
                Mathf.Clamp(_currentImage.y, view.yMin + margin, view.yMax - margin));
        }

        private static BugAnnotationKind ToKind(BugEditorTool tool)
        {
            switch (tool)
            {
                case BugEditorTool.Ellipse: return BugAnnotationKind.Ellipse;
                case BugEditorTool.Spotlight: return BugAnnotationKind.Spotlight;
                case BugEditorTool.Arrow: return BugAnnotationKind.Arrow;
                case BugEditorTool.Pen: return BugAnnotationKind.Pen;
                case BugEditorTool.Marker: return BugAnnotationKind.Marker;
                default: return BugAnnotationKind.Rect;
            }
        }

        #endregion

        #region Private Methods — crop

        /// <summary>Chạm gần cạnh / góc = kéo cạnh đó; trong khung = dời khung; ngoài khung = kéo khung mới.</summary>
        private void BeginCrop(Vector2 local)
        {
            var frame = ImageToLocal(_document.Crop);
            var innerX = Mathf.Min(CROP_HANDLE_REACH, frame.width * CROP_INNER_REACH_SHARE);
            var innerY = Mathf.Min(CROP_HANDLE_REACH, frame.height * CROP_INNER_REACH_SHARE);
            var withinY = local.y >= frame.yMin - CROP_HANDLE_REACH && local.y <= frame.yMax + CROP_HANDLE_REACH;
            var withinX = local.x >= frame.xMin - CROP_HANDLE_REACH && local.x <= frame.xMax + CROP_HANDLE_REACH;

            var left = EdgeDistance(local.x, frame.xMin, 1f, innerX);
            var right = EdgeDistance(local.x, frame.xMax, -1f, innerX);
            var bottom = EdgeDistance(local.y, frame.yMin, 1f, innerY);
            var top = EdgeDistance(local.y, frame.yMax, -1f, innerY);
            _grabLeft = withinY && left >= 0f && (right < 0f || left <= right);
            _grabRight = withinY && right >= 0f && !_grabLeft;
            _grabBottom = withinX && bottom >= 0f && (top < 0f || bottom <= top);
            _grabTop = withinX && top >= 0f && !_grabBottom;

            var onEdge = _grabLeft || _grabRight || _grabBottom || _grabTop;
            _cropMove = !onEdge && frame.Contains(local);
            _cropNew = !onEdge && !_cropMove;
            _cropStart = _document.Crop;
            _cropDraft = _cropStart;
            _isCropGesture = true;
        }

        /// <summary>
        ///     Khoảng cách tới cạnh nếu điểm nằm trong tầm với (ngoài khung: tới <see cref="CROP_HANDLE_REACH" />; trong
        ///     khung: tới <paramref name="innerReach" /> để khung nhỏ vẫn dời được), −1 nếu ngoài tầm.
        /// </summary>
        private static float EdgeDistance(float value, float edge, float inwardSign, float innerReach)
        {
            var inward = (value - edge) * inwardSign;
            var reach = inward >= 0f ? innerReach : CROP_HANDLE_REACH;
            var distance = Mathf.Abs(inward);
            return distance <= reach ? distance : -1f;
        }

        private void UpdateCrop(Vector2 local)
        {
            var full = _document.FullRect;
            var min = _document.MinCropSize;
            var delta = LocalToImage(local, false) - LocalToImage(_startLocal, false);
            var crop = _cropStart;

            if (_cropMove)
            {
                crop.x = Mathf.Clamp(crop.x + delta.x, 0f, full.width - crop.width);
                crop.y = Mathf.Clamp(crop.y + delta.y, 0f, full.height - crop.height);
            }
            else if (_cropNew)
            {
                if (!_hasMoved) return;

                crop = Rect.MinMaxRect(Mathf.Min(_startImage.x, _currentImage.x), Mathf.Min(_startImage.y, _currentImage.y),
                    Mathf.Max(_startImage.x, _currentImage.x), Mathf.Max(_startImage.y, _currentImage.y));
            }
            else
            {
                var xMin = crop.xMin;
                var xMax = crop.xMax;
                var yMin = crop.yMin;
                var yMax = crop.yMax;
                if (_grabLeft) xMin = Mathf.Clamp(xMin + delta.x, 0f, xMax - min);
                if (_grabRight) xMax = Mathf.Clamp(xMax + delta.x, xMin + min, full.width);
                if (_grabBottom) yMin = Mathf.Clamp(yMin + delta.y, 0f, yMax - min);
                if (_grabTop) yMax = Mathf.Clamp(yMax + delta.y, yMin + min, full.height);
                crop = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            }

            _cropDraft = crop;
            _cropOverlay.Set(GetView(), _cropDraft);
        }

        private void EndCrop(bool commit)
        {
            _isCropGesture = false;
            var isTapOutside = _cropNew && !_hasMoved;
            if (commit && !isTapOutside && _document.SetCrop(_cropDraft))
            {
                MarkChanged();
                return;
            }

            if (_document != null) _cropOverlay.Set(GetView(), _document.Crop);
        }

        #endregion

        #region Private Methods — coordinates

        private bool TryGetLocal(PointerEventData eventData, out Vector2 local)
        {
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(_fit, eventData.position,
                eventData.pressEventCamera, out local);
        }

        /// <summary>Điểm trong rect của vùng ảnh → px ảnh gốc (kẹp vào vùng đang xem nếu <paramref name="clamp" />).</summary>
        private Vector2 LocalToImage(Vector2 local, bool clamp)
        {
            var rect = _fit.rect;
            var view = GetView();
            var normalized = new Vector2((local.x - rect.xMin) / Mathf.Max(rect.width, 0.001f),
                (local.y - rect.yMin) / Mathf.Max(rect.height, 0.001f));
            var point = view.min + Vector2.Scale(normalized, view.size);
            return clamp ? ClampToView(point) : point;
        }

        private Rect ImageToLocal(Rect image)
        {
            var rect = _fit.rect;
            var view = GetView();
            var scale = new Vector2(rect.width / Mathf.Max(view.width, 0.001f), rect.height / Mathf.Max(view.height, 0.001f));
            var min = rect.min + Vector2.Scale(image.min - view.min, scale);
            return new Rect(min, Vector2.Scale(image.size, scale));
        }

        private Vector2 ClampToView(Vector2 point)
        {
            var view = GetView();
            return new Vector2(Mathf.Clamp(point.x, view.xMin, view.xMax), Mathf.Clamp(point.y, view.yMin, view.yMax));
        }

        /// <summary>Dời khung vào trong vùng đang xem (khung lớn hơn vùng xem thì kẹp lại).</summary>
        private Rect FitIntoView(Rect box)
        {
            var view = GetView();
            var width = Mathf.Min(box.width, view.width);
            var height = Mathf.Min(box.height, view.height);
            var x = Mathf.Clamp(box.center.x - width * 0.5f, view.xMin, view.xMax - width);
            var y = Mathf.Clamp(box.center.y - height * 0.5f, view.yMin, view.yMax - height);
            return new Rect(x, y, width, height);
        }

        private float GetShortEdge()
        {
            var view = GetView();
            return Mathf.Min(view.width, view.height);
        }

        private float GetStrokeWidth()
        {
            return Mathf.Max(MIN_STROKE, GetShortEdge() * STROKE_RATIO);
        }

        private float GetMarkerRadius()
        {
            return Mathf.Max(MIN_STROKE * 3f, GetShortEdge() * MARKER_RATIO);
        }

        #endregion

        #region Events

        private void OnPressed(PointerEventData eventData)
        {
            if (_isClosing || _document == null || !TryGetLocal(eventData, out var local))
            {
                _surface.Cancel();
                return;
            }

            _isGesture = true;
            _hasMoved = false;
            _startLocal = local;
            _startImage = LocalToImage(local, true);
            _currentImage = _startImage;
            switch (_tool)
            {
                case BugEditorTool.Crop:
                    BeginCrop(local);
                    break;
                case BugEditorTool.Pen:
                    _penPoints.Clear();
                    _penPoints.Add(_startImage);
                    UpdatePending();
                    break;
                case BugEditorTool.Marker:
                    UpdatePending();
                    break;
            }
        }

        private void OnDragged(PointerEventData eventData)
        {
            if (_isGesture && TryGetLocal(eventData, out var local)) Track(local);
        }

        private void OnReleased(PointerEventData eventData)
        {
            if (!_isGesture) return;

            if (TryGetLocal(eventData, out var local)) Track(local);
            FinishGesture(true);
        }

        private void OnGestureCancelled()
        {
            FinishGesture(false);
        }

        private void OnToolClicked(BugLoggerToolButton button)
        {
            var index = Array.IndexOf(_toolButtons, button);
            if (index < 0 || _isClosing) return;

            var tool = (BugEditorTool)index;
            if (tool == _tool) return;

            CancelGesture();
            _tool = tool;
            _lastTool = tool;
            ApplyView();
            RefreshTools(true);
        }

        private void OnSwatchClicked(BugLoggerSwatch swatch)
        {
            var index = Array.IndexOf(_swatches, swatch);
            if (index < 0 || index == _colorIndex) return;

            _colorIndex = index;
            _lastColor = index;
            RefreshSwatches(true);
        }

        private void OnUndoClicked()
        {
            CancelGesture();
            if (_document != null && _document.Undo()) MarkChanged();
        }

        private void OnRedoClicked()
        {
            CancelGesture();
            if (_document != null && _document.Redo()) MarkChanged();
        }

        private void OnClearClicked()
        {
            CancelGesture();
            if (_document != null && _document.ClearAll()) MarkChanged();
        }

        private void OnCloseClicked()
        {
            CancelGesture();
            RequestCancel();
        }

        private void OnDoneClicked()
        {
            if (_isClosing || _document == null) return;

            CancelGesture();
            if (!_isDirty)
            {
                Hide();
                return;
            }

            byte[] jpg = null;
            if (!_document.IsPristine)
            {
                jpg = BugImageFlattener.Render(_sourceProvider?.Invoke(), _document, _atlas);
                if (jpg == null)
                {
                    ShowToast(BugLoggerStrings.EDITOR_RENDER_FAILED, BugToastKind.Error);
                    return;
                }
            }

            var onDone = _onDone;
            var document = _document;
            Hide();
            onDone?.Invoke(document, jpg);
        }

        #endregion
    }
}
