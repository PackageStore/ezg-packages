using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using BugLoggerApi = Ezg.BugLogger.BugLogger;

namespace Ezg.BugLogger.UI
{
    /// <summary>
    ///     Form báo lỗi của QA: ảnh chụp màn hình (chạm để khoanh vùng / vẽ / cắt), tiêu đề, mô tả, phân loại, tỉ lệ tái
    ///     hiện, tag, người được tag.
    ///     Lựa chọn (phân loại / tag / người / giới hạn) tải từ BugHub qua <see cref="BugLoggerApi" />; gửi = ghi vào
    ///     outbox của package rồi uploader tự đẩy lên server. Đóng mà chưa gửi thì nội dung đang gõ được giữ cho lần mở
    ///     sau trong phiên.
    /// </summary>
    internal sealed class BugLoggerForm : MonoBehaviour
    {
        #region Fields

        private const float SHOW_DURATION = 0.42f;
        private const float HIDE_DURATION = 0.2f;
        private const float SCRIM_DURATION = 0.24f;
        private const float CARD_FROM_SCALE = 0.94f;
        private const float CARD_SLIDE = 64f;
        private const float FIRST_FRAME_DELAY = 0.001f;
        private const float STAGGER_START = 0.08f;
        private const float STAGGER_STEP = 0.035f;
        private const float STAGGER_DURATION = 0.3f;
        private const float CHIPS_FADE_DURATION = 0.28f;
        private const float SKELETON_HALF_PERIOD = 0.7f;
        private const float SKELETON_MIN_ALPHA = 0.35f;
        private const float THUMB_DIM_ALPHA = 0.35f;
        private const float THUMB_FADE_DURATION = 0.2f;
        private const float SUCCESS_COLOR_DURATION = 0.22f;
        private const float SUCCESS_HOLD = 0.55f;
        private const float SCROLL_DURATION = 0.32f;
        private const float SCROLL_PADDING = 40f;
        private const float STATUS_REFRESH_INTERVAL = 1f;
        private const float KEYBOARD_SMOOTHING = 14f;

        // Chuyển từ ô này sang ô khác: bàn phím tắt rồi bật lại trong khoảng này ⇒ giữ cỡ card, khỏi nhún cả form.
        private const float KEYBOARD_SWITCH_GRACE = 0.35f;

        private const float CARD_MARGIN = 28f;
        private const float CARD_MAX_WIDTH = 1040f;
        private const float CARD_MAX_HEIGHT = 1900f;
        private const float THUMB_HEIGHT = 300f;
        private const float THUMB_MAX_WIDTH = 420f;

        // Ảnh đã cắt thành dải rất ngang / rất dọc: khung thumbnail không nhỏ hơn mức này (vẫn chạm được để sửa tiếp),
        // phần ảnh thừa cắt giữa thay vì kéo méo.
        private const float THUMB_MIN_SIDE = 120f;

        private const int FEATURE_MAX = 64;
        private const int REPRODUCE_MAX = 20;
        private const int SKELETON_COUNT = 3;
        private static readonly float[] SkeletonWidths = { 168f, 128f, 196f };

        [Header("Khung")]
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private Image _scrim;
        [SerializeField] private RectTransform _cardFrame;
        [SerializeField] private RectTransform _card;
        [SerializeField] private CanvasGroup _cardGroup;
        [SerializeField] private CanvasGroup[] _staggerGroups;

        [Header("Đầu form")]
        [SerializeField] private Text _subtitle;
        [SerializeField] private Button _closeButton;
        [SerializeField] private ScrollRect _scroll;

        [Header("Ảnh chụp")]
        [SerializeField] private GameObject _screenshotPreview;
        [SerializeField] private RawImage _thumbnail;
        [SerializeField] private LayoutElement _thumbnailLayout;
        [SerializeField] private Button _thumbnailButton;
        [SerializeField] private CanvasGroup _thumbnailGroup;
        [SerializeField] private GameObject _screenshotMissing;
        [SerializeField] private Text _screenshotHint;
        [SerializeField] private GameObject _attachRow;
        [SerializeField] private BugLoggerSwitch _attachSwitch;

        [Header("Ô nhập")]
        [SerializeField] private BugLoggerField _titleField;
        [SerializeField] private BugLoggerField _contentField;
        [SerializeField] private BugLoggerField _featureField;
        [SerializeField] private BugLoggerStepper _hitsStepper;
        [SerializeField] private BugLoggerStepper _triesStepper;

        [Header("Lựa chọn")]
        [SerializeField] private RectTransform _categoryContainer;
        [SerializeField] private CanvasGroup _categoryGroup;
        [SerializeField] private GameObject _tagSection;
        [SerializeField] private RectTransform _tagContainer;
        [SerializeField] private CanvasGroup _tagGroup;
        [SerializeField] private Text _tagHint;
        [SerializeField] private GameObject _peopleSection;
        [SerializeField] private RectTransform _peopleContainer;
        [SerializeField] private CanvasGroup _peopleGroup;
        [SerializeField] private Text _peopleHint;
        [SerializeField] private Text _optionsNote;
        [SerializeField] private BugLoggerChip _chipTemplate;
        [SerializeField] private Graphic _skeletonTemplate;

        [Header("Chân form")]
        [SerializeField] private Image _statusDot;
        [SerializeField] private Text _statusText;
        [SerializeField] private Button _submitButton;
        [SerializeField] private Image _submitBackground;
        [SerializeField] private Image _submitIcon;
        [SerializeField] private Text _submitLabel;
        [SerializeField] private Sprite _iconSend;
        [SerializeField] private Sprite _iconCheck;

        [Header("Khác")]
        [SerializeField] private BugLoggerImageEditor _editor;

        // Lựa chọn tải từ server được giữ cả phiên; bản cache/mặc định (mất mạng) thì lần mở sau tải lại.
        private static BugFormOptions _serverOptions;

        // Lượt tải dùng chung — prefetch lúc hiện nút nổi và form mở giữa chừng chờ cùng một request.
        private static readonly BugSharedFetch<BugFormOptions> OptionsFetch =
            new BugSharedFetch<BugFormOptions>(FetchOptionsAsync);

        // Nội dung đang gõ dở khi đóng form mà chưa gửi.
        private static string _draftTitle = string.Empty;
        private static string _draftContent = string.Empty;

        private readonly List<BugLoggerChip> _categoryChips = new List<BugLoggerChip>();
        private readonly List<BugLoggerChip> _tagChips = new List<BugLoggerChip>();
        private readonly List<BugLoggerChip> _peopleChips = new List<BugLoggerChip>();
        private readonly List<Graphic> _skeletons = new List<Graphic>();

        private BugLoggerOverlay _overlay;
        private Canvas _rootCanvas;
        private BugFormOptions _options;
        private BugFormOptions _chipsBuiltFor;
        private CancellationTokenSource _openCts;
        private Action _onClosed;
        private byte[] _screenshotJpg;
        private Texture2D _preview;

        // Ảnh chụp gốc (GPU, full độ phân giải) + JPG gốc + bản chỉnh sửa — màn chỉnh sửa luôn dựng lại từ ảnh gốc.
        private Texture _source;
        private byte[] _originalJpg;
        private BugImageDocument _document;
        private bool _isClosing;
        private bool _isSubmitted;
        private bool _isSelectionTouched;
        private float _cardOffsetY;
        private float _cardScale = 1f;
        private float _keyboardInset;
        private float _keyboardTarget;
        private float _keyboardHoldUntil;
        private BugLoggerField _keyboardField;
        private float _statusTimer;
        private BugLoggerField _revealedField;
        private float _revealedViewHeight = -1f;
        private float _lastViewHeight = -1f;

        #endregion

        #region Properties

        /// <summary>Form đang hiện — tính cả lúc đang chạy animation đóng (để phím Back của game chưa xử lý).</summary>
        public bool IsOpen => gameObject.activeSelf;

        #endregion

        #region Initialize

        public void Bind(BugLoggerOverlay overlay, Canvas rootCanvas)
        {
            _overlay = overlay;
            _rootCanvas = rootCanvas;
        }

        private void Awake()
        {
            _closeButton.onClick.AddListener(OnCloseClicked);
            _submitButton.onClick.AddListener(OnSubmitClicked);
            _thumbnailButton.onClick.AddListener(OnThumbnailClicked);
            _attachSwitch.Changed += OnAttachChanged;
            _triesStepper.ValueChanged += OnTriesChanged;

            _chipTemplate.gameObject.SetActive(false);
            _skeletonTemplate.gameObject.SetActive(false);
            _featureField.SetLimit(FEATURE_MAX);
            _titleField.SetEditorLabel(BugLoggerStrings.LABEL_TITLE);
            _contentField.SetEditorLabel(BugLoggerStrings.LABEL_CONTENT);
            _featureField.SetEditorLabel(BugLoggerStrings.LABEL_FEATURE);
            _hitsStepper.SetRange(0, 1);
            _triesStepper.SetRange(1, REPRODUCE_MAX);
        }

        // Editor tắt Reload Domain thì static sống qua các lần Play.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _serverOptions = null;
            OptionsFetch.Reset();
            _draftTitle = string.Empty;
            _draftContent = string.Empty;
        }

        private void OnDestroy()
        {
            CancelOpen();
            BugTweener.Kill(this);
            DestroyPreview();
            DestroySource();
        }

        private void Update()
        {
            if (_isClosing) return;

            UpdateKeyboardInset();
            ApplyCardLayout();
            RevealFocusedField();

            _statusTimer -= Time.unscaledDeltaTime;
            if (_statusTimer <= 0f) RefreshStatus();
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Mở form với ảnh vừa chụp (có thể null) và màn hình/tính năng gợi ý. Form nhận quyền sở hữu
        ///     <paramref name="preview" /> và <paramref name="source" /> (ảnh chụp gốc để chỉnh sửa), huỷ khi đóng.
        /// </summary>
        public void Open(byte[] screenshotJpg, Texture2D preview, Texture source, string featureKey, Action onClosed)
        {
            CancelOpen();
            _openCts = new CancellationTokenSource();
            _onClosed = onClosed;
            _isClosing = false;
            _isSubmitted = false;

            if (_editor.IsOpen) _editor.Close();
            DestroyPreview();
            DestroySource();
            _screenshotJpg = screenshotJpg;
            _originalJpg = screenshotJpg;
            _preview = preview;
            _source = source;
            _document = source != null && screenshotJpg != null ? new BugImageDocument(source.width, source.height) : null;

            gameObject.SetActive(true);
            ResetForm(featureKey);
            SetupScreenshot();
            RefreshStatus();
            LoadOptionsAsync(_openCts.Token).Forget();
            PlayShow();
        }

        /// <summary>Đóng form (có animation). Nội dung chưa gửi được giữ cho lần mở sau.</summary>
        public void Close()
        {
            if (!IsOpen || _isClosing) return;

            _isClosing = true;
            CancelOpen();
            // Ô sửa native (Android) đang mở thì đóng theo — chữ trong đó được ghi vào ô trước khi lưu nháp.
            BugLoggerNativeEditor.CloseActive();
            if (!_isSubmitted)
            {
                _draftTitle = _titleField.Text;
                _draftContent = _contentField.Text;
            }

            if (_editor.IsOpen) _editor.Close();
            _group.blocksRaycasts = false;
            PlayHide();
        }

        /// <summary>Cập nhật dòng trạng thái gửi (mạng + số report chờ).</summary>
        public void RefreshStatus()
        {
            _statusTimer = STATUS_REFRESH_INTERVAL;

            var pending = BugLoggerApi.PendingCount;
            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                _statusDot.color = BugLoggerPalette.Warning;
                _statusText.text = BugLoggerStrings.STATUS_OFFLINE;
                return;
            }

            if (pending > 0)
            {
                _statusDot.color = BugLoggerPalette.Warning;
                _statusText.text = string.Format(BugLoggerStrings.STATUS_PENDING_FORMAT, pending);
                return;
            }

            _statusDot.color = BugLoggerPalette.Success;
            _statusText.text = BugLoggerStrings.STATUS_ONLINE;
        }

        #endregion

        #region Private Methods — open / close

        private void ResetForm(string featureKey)
        {
            _subtitle.text = BuildSubtitle();
            _titleField.Text = _draftTitle;
            _contentField.Text = _draftContent;
            _featureField.Text = featureKey;
            _triesStepper.SetValue(1, false);
            _hitsStepper.SetRange(0, _triesStepper.Value);
            _hitsStepper.SetValue(1, false);
            _attachSwitch.SetOn(true, false);
            _thumbnailGroup.alpha = 1f;
            _optionsNote.gameObject.SetActive(false);

            _isSelectionTouched = false;
            BugTweener.Kill(this, BugTweener.CHANNEL_SCALE);
            BugTweener.Kill(this, BugTweener.CHANNEL_COLOR);
            _submitButton.interactable = true;
            _submitBackground.color = BugLoggerPalette.Accent;
            _submitIcon.sprite = _iconSend;
            _submitIcon.rectTransform.localScale = Vector3.one;
            _submitLabel.text = BugLoggerStrings.SUBMIT;

            _scroll.StopMovement();
            _scroll.verticalNormalizedPosition = 1f;
            _keyboardInset = 0f;
            _keyboardTarget = 0f;
            _keyboardHoldUntil = 0f;
            _keyboardField = null;
            _revealedField = null;
            _revealedViewHeight = -1f;
            _lastViewHeight = -1f;
            _group.blocksRaycasts = true;
        }

        private static string BuildSubtitle()
        {
            return Application.productName + BugLoggerStrings.SUBTITLE_SEPARATOR + "v" + Application.version +
                   BugLoggerStrings.SUBTITLE_SEPARATOR + SceneManager.GetActiveScene().name;
        }

        private void SetupScreenshot()
        {
            var hasPreview = _preview != null;
            _screenshotPreview.SetActive(hasPreview);
            _screenshotMissing.SetActive(!hasPreview);
            _attachRow.SetActive(hasPreview);
            if (!hasPreview)
            {
                _thumbnail.texture = null;
                _screenshotHint.text = BugLoggerStrings.SCREENSHOT_NONE;
                return;
            }

            _thumbnail.texture = _preview;
            FitThumbnail((float)_preview.width / Mathf.Max(1, _preview.height));
            var isEdited = _document != null && !_document.IsPristine;
            _screenshotHint.text = string.Format(
                isEdited ? BugLoggerStrings.SCREENSHOT_EDITED_FORMAT : BugLoggerStrings.SCREENSHOT_HINT_FORMAT,
                _preview.width, _preview.height);
        }

        /// <summary>
        ///     Khung thumbnail giữ tỉ lệ ảnh trong giới hạn cao <see cref="THUMB_HEIGHT" /> × rộng
        ///     <see cref="THUMB_MAX_WIDTH" />, không cạnh nào dưới <see cref="THUMB_MIN_SIDE" />; khi phải nới khung thì
        ///     cắt giữa ảnh (uvRect) cho khỏi méo.
        /// </summary>
        private void FitThumbnail(float aspect)
        {
            aspect = Mathf.Max(aspect, 0.0001f);
            var width = THUMB_HEIGHT * aspect;
            var height = THUMB_HEIGHT;
            if (width > THUMB_MAX_WIDTH)
            {
                width = THUMB_MAX_WIDTH;
                height = width / aspect;
            }

            width = Mathf.Max(width, THUMB_MIN_SIDE);
            height = Mathf.Max(height, THUMB_MIN_SIDE);
            _thumbnailLayout.preferredWidth = width;
            _thumbnailLayout.preferredHeight = height;

            var frameAspect = width / height;
            _thumbnail.uvRect = aspect > frameAspect
                ? new Rect((1f - frameAspect / aspect) * 0.5f, 0f, frameAspect / aspect, 1f)
                : new Rect(0f, (1f - aspect / frameAspect) * 0.5f, 1f, aspect / frameAspect);
        }

        private void PlayShow()
        {
            _cardOffsetY = -CARD_SLIDE;
            _cardScale = CARD_FROM_SCALE;
            _group.alpha = 0f;
            _cardGroup.alpha = 0f;
            ApplyCardLayout();

            // Frame mở form gánh việc nặng (chụp + nén + giải mã ảnh) ⇒ delta frame kế rất lớn. Trễ một chút để
            // frame đó chỉ "ăn" phần trễ, animation bắt đầu từ 0 ở frame sau — không giật ở nhịp đầu.
            BugTweener.To(_group, BugTweener.CHANNEL_ALPHA, SCRIM_DURATION, t => _group.alpha = t, BugEase.OutCubic,
                null, FIRST_FRAME_DELAY);
            BugTweener.To(this, BugTweener.CHANNEL_MOVE, SHOW_DURATION, t =>
            {
                _cardOffsetY = Mathf.LerpUnclamped(-CARD_SLIDE, 0f, t);
                _cardScale = Mathf.LerpUnclamped(CARD_FROM_SCALE, 1f, t);
                _cardGroup.alpha = Mathf.Clamp01(t * 2.2f);
                ApplyCardLayout();
            }, BugEase.OutQuint, null, FIRST_FRAME_DELAY);

            for (var i = 0; i < _staggerGroups.Length; i++)
            {
                var group = _staggerGroups[i];
                group.alpha = 0f;
                BugTweener.To(group, BugTweener.CHANNEL_ALPHA, STAGGER_DURATION, t => group.alpha = t,
                    BugEase.OutCubic, null, STAGGER_START + i * STAGGER_STEP);
            }
        }

        private void PlayHide()
        {
            var fromAlpha = _cardGroup.alpha;
            var fromOffset = _cardOffsetY;
            var fromScale = _cardScale;
            BugTweener.To(this, BugTweener.CHANNEL_MOVE, HIDE_DURATION, t =>
            {
                _cardOffsetY = Mathf.Lerp(fromOffset, CARD_SLIDE * 0.4f, t);
                _cardScale = Mathf.Lerp(fromScale, 0.97f, t);
                _cardGroup.alpha = Mathf.Lerp(fromAlpha, 0f, t);
                ApplyCardLayout();
            }, BugEase.InCubic);

            var fromScrim = _group.alpha;
            BugTweener.To(_group, BugTweener.CHANNEL_ALPHA, SCRIM_DURATION, t => _group.alpha = Mathf.Lerp(fromScrim, 0f, t),
                BugEase.InCubic, FinishClose);
        }

        private void FinishClose()
        {
            _editor.Close();
            BugTweener.Kill(this);
            HideSkeletons();
            gameObject.SetActive(false);
            _isClosing = false;
            _thumbnail.texture = null;
            DestroyPreview();
            DestroySource();
            _screenshotJpg = null;

            var onClosed = _onClosed;
            _onClosed = null;
            onClosed?.Invoke();
        }

        private void CancelOpen()
        {
            if (_openCts == null) return;

            _openCts.Cancel();
            _openCts.Dispose();
            _openCts = null;
        }

        private void DestroyPreview()
        {
            if (_preview == null) return;

            Destroy(_preview);
            _preview = null;
        }

        /// <summary>
        ///     Ảnh gốc cho màn chỉnh sửa. RenderTexture mất nội dung khi GPU mất context (Android GLES lúc app vào nền) ⇒
        ///     dựng lại từ JPG gốc: nét thấp hơn (cạnh dài ≤ 1280) nhưng bản sửa vẫn khớp vì toạ độ cắt / nét tính theo tỉ lệ
        ///     ảnh.
        /// </summary>
        private Texture GetSource()
        {
            if (!(_source is RenderTexture renderTexture) || renderTexture.IsCreated()) return _source;

            Destroy(renderTexture);
            _source = BugLoggerScreenCapture.DecodePreview(_originalJpg);
            return _source;
        }

        private void DestroySource()
        {
            _document = null;
            _originalJpg = null;
            if (_source == null) return;

            Destroy(_source);
            _source = null;
        }

        #endregion

        #region Private Methods — layout

        /// <summary>
        ///     Card rộng tối đa <see cref="CARD_MAX_WIDTH" />, cao vừa vùng an toàn (trừ bàn phím ảo), căn giữa
        ///     phần màn hình còn thấy được. Gọi mỗi frame khi mở — rẻ, và tự đúng khi xoay màn hình.
        /// </summary>
        private void ApplyCardLayout()
        {
            var frame = _cardFrame.rect.size;
            var width = Mathf.Min(frame.x - CARD_MARGIN * 2f, CARD_MAX_WIDTH);
            var height = Mathf.Min(frame.y - CARD_MARGIN * 2f - _keyboardInset, CARD_MAX_HEIGHT);
            var size = new Vector2(Mathf.Max(0f, width), Mathf.Max(0f, height));
            var position = new Vector2(0f, _keyboardInset * 0.5f + _cardOffsetY);
            var scale = new Vector3(_cardScale, _cardScale, 1f);

            // Chỉ gán khi đổi: gán lại transform (kể cả cùng giá trị) bắt canvas dựng lại batch mỗi frame.
            if (_card.sizeDelta != size) _card.sizeDelta = size;
            if (_card.anchoredPosition != position) _card.anchoredPosition = position;
            if (_card.localScale != scale) _card.localScale = scale;
        }

        private void UpdateKeyboardInset()
        {
            // Đang chạm ô khác hoặc vừa sang ô mới: chờ bàn phím bật lại (realtime — Android chặn luồng chính lúc bật/tắt).
            var focused = GetFocusedField();
            if (IsAnyFieldAwaitingFocus() || (focused != null && focused != _keyboardField))
                _keyboardHoldUntil = Time.realtimeSinceStartup + KEYBOARD_SWITCH_GRACE;
            _keyboardField = focused;

            var target = 0f;
            var keyboardHeight = BugLoggerKeyboard.GetHeight();
            if (keyboardHeight > 0f && _rootCanvas != null)
            {
                var scale = Mathf.Max(0.01f, _rootCanvas.scaleFactor);
                target = Mathf.Max(0f, (keyboardHeight - Screen.safeArea.yMin) / scale);
            }
            else if (Time.realtimeSinceStartup < _keyboardHoldUntil)
            {
                target = _keyboardTarget;
            }

            _keyboardTarget = target;
            var blend = 1f - Mathf.Exp(-KEYBOARD_SMOOTHING * Time.unscaledDeltaTime);
            _keyboardInset = Mathf.Abs(target - _keyboardInset) < 0.5f ? target : Mathf.Lerp(_keyboardInset, target, blend);
        }

        /// <summary>
        ///     Đưa ô đang gõ vào phần form còn thấy được — khi bàn phím vừa trồi lên xong (card đã co hết), khi QA
        ///     chuyển sang ô khác, hoặc khi khung nhìn đổi cỡ (bàn phím đổi chiều cao, xoay màn hình). Chỉ cuộn khi khung
        ///     nhìn đã đứng yên: cuộn theo cỡ đang co dở thì ô vẫn nằm dưới bàn phím; cỡ đổi lần nữa thì cuộn lại.
        /// </summary>
        private void RevealFocusedField()
        {
            var focused = GetFocusedField();
            var viewHeight = _scroll.viewport.rect.height;
            var isStable = _keyboardInset == _keyboardTarget && Mathf.Abs(viewHeight - _lastViewHeight) < 0.5f;
            _lastViewHeight = viewHeight;

            if (focused != _revealedField)
            {
                _revealedField = focused;
                _revealedViewHeight = -1f;
            }

            if (focused == null || _keyboardInset <= 0f || !isStable) return;
            if (Mathf.Abs(viewHeight - _revealedViewHeight) < 1f) return;

            _revealedViewHeight = viewHeight;
            ScrollIntoView((RectTransform)focused.transform);
        }

        private bool IsAnyFieldAwaitingFocus()
        {
            return _titleField.IsAwaitingFocus || _contentField.IsAwaitingFocus || _featureField.IsAwaitingFocus;
        }

        private BugLoggerField GetFocusedField()
        {
            if (_titleField.IsFocused) return _titleField;
            if (_contentField.IsFocused) return _contentField;
            return _featureField.IsFocused ? _featureField : null;
        }

        private void ScrollIntoView(RectTransform target)
        {
            var content = _scroll.content;
            var viewHeight = _scroll.viewport.rect.height;
            var contentHeight = content.rect.height;
            if (contentHeight <= viewHeight) return;

            // Content neo mép trên: anchoredPosition.y = khoảng đã cuộn xuống.
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(content, target);
            var top = -bounds.max.y;
            var bottom = -bounds.min.y;
            var from = content.anchoredPosition.y;
            var to = from;
            if (top - SCROLL_PADDING < from) to = top - SCROLL_PADDING;
            else if (bottom + SCROLL_PADDING > from + viewHeight) to = bottom + SCROLL_PADDING - viewHeight;

            to = Mathf.Clamp(to, 0f, contentHeight - viewHeight);
            if (Mathf.Approximately(to, from)) return;

            _scroll.StopMovement();
            BugTweener.To(content, BugTweener.CHANNEL_MOVE, SCROLL_DURATION,
                t => content.anchoredPosition = new Vector2(content.anchoredPosition.x, Mathf.Lerp(from, to, t)),
                BugEase.OutCubic);
        }

        #endregion

        #region Private Methods — options

        /// <summary>
        ///     Tải trước lựa chọn form (gọi khi nút nổi hiện) để lúc QA mở form là có ngay, không phải chờ server.
        /// </summary>
        public static void PrefetchOptions()
        {
            // Đang tải thì thôi: nút nổi bật/tắt liên tục (cheat đổi trạng thái) không bắn thêm request.
            if (_serverOptions == null && !OptionsFetch.IsRunning) OptionsFetch.Run().Forget();
        }

        private static async UniTask<BugFormOptions> FetchOptionsAsync()
        {
            // Không truyền token: package không ném vì mạng, request sống hết vòng đời app.
            var options = await BugLoggerApi.GetFormOptionsAsync();
            if (options.Source == BugFormOptionsSource.Server) _serverOptions = options;
            return options;
        }

        private async UniTaskVoid LoadOptionsAsync(CancellationToken cancellationToken)
        {
            if (_serverOptions != null)
            {
                ApplyOptions(_serverOptions, false);
                return;
            }

            // Lần trước chỉ có bản cache/mặc định (mất mạng) ⇒ vẫn dùng ngay để QA gửi được, tải lại ở nền.
            var hasPrevious = _options != null;
            if (hasPrevious) ApplyOptions(_options, false);
            else ShowSkeletons();

            var (cancelled, options) = await OptionsFetch.Run().AttachExternalCancellation(cancellationToken)
                .SuppressCancellationThrow();
            if (cancelled || this == null) return;

            // Không thay chip dưới tay QA: chỉ áp bản mới khi trước đó chưa có gì, hoặc có bản server mà QA chưa chọn.
            if (hasPrevious && (_isSelectionTouched || options.Source != BugFormOptionsSource.Server)) return;

            ApplyOptions(options, true);
        }

        private void ApplyOptions(BugFormOptions options, bool animate)
        {
            _options = options;
            HideSkeletons();

            var limits = options.Limits;
            _titleField.SetLimit(limits.TitleMax);
            _contentField.SetLimit(limits.FieldMax);

            if (_chipsBuiltFor != options)
            {
                _chipsBuiltFor = options;
                BuildCategoryChips(options.Categories);
                BuildOptionChips(_tagChips, _tagContainer, options.Tags, OnTagClicked);
                BuildOptionChips(_peopleChips, _peopleContainer, options.People, OnPersonClicked);
            }

            SelectDefaults(limits);

            _tagSection.SetActive(_tagChips.Count > 0 && limits.MaxTags > 0);
            _peopleSection.SetActive(_peopleChips.Count > 0 && limits.MaxMentions > 0);
            _tagHint.text = string.Format(BugLoggerStrings.LIMIT_HINT_FORMAT, limits.MaxTags);
            _peopleHint.text = string.Format(BugLoggerStrings.LIMIT_HINT_FORMAT, limits.MaxMentions);

            var note = !options.IntakeEnabled ? BugLoggerStrings.OPTIONS_INTAKE_DISABLED :
                options.Source != BugFormOptionsSource.Server ? BugLoggerStrings.OPTIONS_FALLBACK : null;
            _optionsNote.gameObject.SetActive(note != null);
            if (note != null) _optionsNote.text = note;

            if (!animate) return;

            FadeIn(_categoryGroup);
            FadeIn(_tagGroup);
            FadeIn(_peopleGroup);
        }

        private void SelectDefaults(BugFormLimits limits)
        {
            for (var i = 0; i < _categoryChips.Count; i++) _categoryChips[i].SetSelected(i == 0, false);

            // Giữ hành vi cũ: tag đầu tiên chọn sẵn (forum Discord bắt buộc có tag).
            for (var i = 0; i < _tagChips.Count; i++) _tagChips[i].SetSelected(i == 0 && limits.MaxTags > 0, false);

            for (var i = 0; i < _peopleChips.Count; i++) _peopleChips[i].SetSelected(false, false);
        }

        private void BuildCategoryChips(IReadOnlyList<string> categories)
        {
            ClearChips(_categoryChips);
            for (var i = 0; i < categories.Count; i++)
            {
                var chip = SpawnChip(_categoryContainer);
                chip.Setup(categories[i], categories[i], false, OnCategoryClicked);
                _categoryChips.Add(chip);
            }
        }

        private void BuildOptionChips(List<BugLoggerChip> chips, RectTransform container,
            IReadOnlyList<BugFormOption> source, Action<BugLoggerChip> onClick)
        {
            ClearChips(chips);
            for (var i = 0; i < source.Count; i++)
            {
                var chip = SpawnChip(container);
                chip.Setup(source[i].Id, source[i].Name, true, onClick);
                chips.Add(chip);
            }
        }

        private BugLoggerChip SpawnChip(RectTransform container)
        {
            var chip = Instantiate(_chipTemplate, container, false);
            chip.gameObject.SetActive(true);
            return chip;
        }

        private static void ClearChips(List<BugLoggerChip> chips)
        {
            for (var i = 0; i < chips.Count; i++)
                if (chips[i] != null)
                    Destroy(chips[i].gameObject);
            chips.Clear();
        }

        private void ShowSkeletons()
        {
            if (_skeletons.Count == 0)
            {
                SpawnSkeletons(_categoryContainer);
                SpawnSkeletons(_tagContainer);
                SpawnSkeletons(_peopleContainer);
            }

            _tagSection.SetActive(true);
            _peopleSection.SetActive(true);
            for (var i = 0; i < _skeletons.Count; i++)
            {
                var skeleton = _skeletons[i];
                skeleton.gameObject.SetActive(true);
                BugTweener.PingPong(skeleton, BugTweener.CHANNEL_LOOP, SKELETON_HALF_PERIOD,
                    t => skeleton.canvasRenderer.SetAlpha(Mathf.Lerp(1f, SKELETON_MIN_ALPHA, t)));
            }
        }

        private void SpawnSkeletons(RectTransform container)
        {
            for (var i = 0; i < SKELETON_COUNT; i++)
            {
                var skeleton = Instantiate(_skeletonTemplate, container, false);
                skeleton.gameObject.SetActive(false);
                // Bề rộng so le cho giống một hàng chip thật đang tải.
                if (skeleton.TryGetComponent<LayoutElement>(out var layout))
                    layout.preferredWidth = SkeletonWidths[i % SkeletonWidths.Length];
                _skeletons.Add(skeleton);
            }
        }

        private void HideSkeletons()
        {
            for (var i = 0; i < _skeletons.Count; i++)
            {
                BugTweener.Kill(_skeletons[i]);
                _skeletons[i].gameObject.SetActive(false);
            }
        }

        private static void FadeIn(CanvasGroup group)
        {
            group.alpha = 0f;
            BugTweener.To(group, BugTweener.CHANNEL_ALPHA, CHIPS_FADE_DURATION, t => group.alpha = t);
        }

        private static int CountSelected(List<BugLoggerChip> chips)
        {
            var count = 0;
            for (var i = 0; i < chips.Count; i++)
                if (chips[i].IsSelected)
                    count++;
            return count;
        }

        private static List<string> CollectSelected(List<BugLoggerChip> chips)
        {
            var selected = new List<string>();
            for (var i = 0; i < chips.Count; i++)
                if (chips[i].IsSelected)
                    selected.Add(chips[i].Value);
            return selected;
        }

        private string GetSelectedCategory()
        {
            for (var i = 0; i < _categoryChips.Count; i++)
                if (_categoryChips[i].IsSelected)
                    return _categoryChips[i].Value;
            return string.Empty;
        }

        private void ToggleLimited(List<BugLoggerChip> chips, BugLoggerChip chip, int max, string limitFormat)
        {
            if (chip.IsSelected)
            {
                chip.SetSelected(false, true);
                return;
            }

            if (CountSelected(chips) >= max)
            {
                chip.Shake();
                _overlay.ShowToast(string.Format(limitFormat, max), BugToastKind.Info);
                return;
            }

            chip.SetSelected(true, true);
        }

        #endregion

        #region Private Methods — submit

        private bool Validate(out string title, out string content)
        {
            title = _titleField.Text.Trim();
            content = _contentField.Text.Trim();

            RectTransform firstInvalid = null;
            if (content.Length == 0)
            {
                _contentField.ShowError(BugLoggerStrings.ERROR_CONTENT_REQUIRED);
                firstInvalid = (RectTransform)_contentField.transform;
            }

            if (title.Length == 0)
            {
                _titleField.ShowError(BugLoggerStrings.ERROR_TITLE_REQUIRED);
                firstInvalid = (RectTransform)_titleField.transform;
            }

            if (firstInvalid == null) return true;

            ScrollIntoView(firstInvalid);
            return false;
        }

        private void PlaySubmitted()
        {
            _isSubmitted = true;
            _submitButton.interactable = false;
            _submitLabel.text = BugLoggerStrings.SUBMITTED;
            _submitIcon.sprite = _iconCheck;

            // Tween gắn vào chính form (không phải nút con) ⇒ đóng/mở lại nhanh thì FinishClose / ResetForm huỷ
            // được, không có Complete cũ gọi Close lên lần mở mới.
            var from = _submitBackground.color;
            var icon = _submitIcon.rectTransform;
            BugTweener.To(this, BugTweener.CHANNEL_COLOR, SUCCESS_COLOR_DURATION,
                t => _submitBackground.color = Color.Lerp(from, BugLoggerPalette.Success, t));
            BugTweener.To(this, BugTweener.CHANNEL_SCALE, SUCCESS_HOLD,
                t => icon.localScale = Vector3.one * Mathf.LerpUnclamped(0.3f, 1f, t), BugEase.OutBack, Close);
        }

        #endregion

        #region Events

        private void OnCloseClicked()
        {
            Close();
        }

        private void OnSubmitClicked()
        {
            if (_isSubmitted || _isClosing) return;

            if (_options == null)
            {
                _overlay.ShowToast(BugLoggerStrings.ERROR_FORM_LOADING, BugToastKind.Info);
                return;
            }

            if (!Validate(out var title, out var content)) return;

            // Title gửi thô — server tự ghép tiền tố [version]-[category]-[feature] vào tên thread.
            var draft = new BugReportDraft
            {
                Kind = BugReportKind.Bug,
                Title = title,
                FeatureKey = _featureField.Text.Trim(),
                Category = GetSelectedCategory(),
                ReproduceRate = _hitsStepper.Value + BugLoggerStrings.REPRODUCE_SEPARATOR + _triesStepper.Value,
                Actual = content,
                TagIds = CollectSelected(_tagChips),
                MentionIds = CollectSelected(_peopleChips)
            };

            try
            {
                BugLoggerApi.Submit(draft, _attachSwitch.IsOn ? _screenshotJpg : null);
            }
            catch (ArgumentException ex)
            {
                // Package chặn bug thiếu Title/Actual theo cách trim của server — cho QA sửa rồi gửi lại.
                _overlay.ShowToast(ex.Message, BugToastKind.Error);
                return;
            }

            _draftTitle = string.Empty;
            _draftContent = string.Empty;
            PlaySubmitted();
        }

        private void OnThumbnailClicked()
        {
            if (_isClosing || _isSubmitted || _document == null || GetSource() == null) return;

            _editor.Open(GetSource, _document, OnImageEdited);
        }

        /// <summary>
        ///     Màn chỉnh sửa bấm Xong: thay ảnh gửi đi + thumbnail bằng bản đã sửa (<paramref name="jpg" /> null = đã hoàn
        ///     tác về ảnh gốc). QA đã chỉnh ảnh thì chắc chắn muốn gửi kèm ⇒ bật lại "Đính kèm".
        /// </summary>
        private void OnImageEdited(BugImageDocument document, byte[] jpg)
        {
            if (_source == null || !IsOpen || _isClosing) return;

            _document = document;
            _screenshotJpg = jpg ?? _originalJpg;
            var preview = BugLoggerScreenCapture.DecodePreview(_screenshotJpg);
            if (preview != null)
            {
                DestroyPreview();
                _preview = preview;
            }

            SetupScreenshot();
            if (_attachSwitch.IsOn) return;

            _attachSwitch.SetOn(true, true);
            OnAttachChanged(true);
        }

        private void OnAttachChanged(bool isOn)
        {
            var from = _thumbnailGroup.alpha;
            var to = isOn ? 1f : THUMB_DIM_ALPHA;
            BugTweener.To(_thumbnailGroup, BugTweener.CHANNEL_ALPHA, THUMB_FADE_DURATION,
                t => _thumbnailGroup.alpha = Mathf.Lerp(from, to, t));
        }

        private void OnTriesChanged(int tries)
        {
            _hitsStepper.SetRange(0, tries);
        }

        private void OnCategoryClicked(BugLoggerChip chip)
        {
            _isSelectionTouched = true;
            for (var i = 0; i < _categoryChips.Count; i++)
            {
                var other = _categoryChips[i];
                var selected = other == chip;
                if (other.IsSelected != selected) other.SetSelected(selected, true);
            }
        }

        private void OnTagClicked(BugLoggerChip chip)
        {
            _isSelectionTouched = true;
            ToggleLimited(_tagChips, chip, _options.Limits.MaxTags, BugLoggerStrings.ERROR_MAX_TAGS_FORMAT);
        }

        private void OnPersonClicked(BugLoggerChip chip)
        {
            _isSelectionTouched = true;
            ToggleLimited(_peopleChips, chip, _options.Limits.MaxMentions, BugLoggerStrings.ERROR_MAX_PEOPLE_FORMAT);
        }

        #endregion
    }
}
