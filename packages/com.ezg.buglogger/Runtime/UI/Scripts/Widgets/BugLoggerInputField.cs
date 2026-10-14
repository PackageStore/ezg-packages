using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ezg.BugLogger.UI
{
    /// <summary>
    ///     InputField legacy chỉnh cho ô nhập nằm trong form cuộn trên điện thoại:
    ///     <list type="bullet">
    ///         <item>Android: chạm ô = mở ô sửa native nằm trên bàn phím (<see cref="BugLoggerNativeEditor" />) — đủ thao
    ///         tác của ô nhập Android, chữ đồng bộ ngược vào ô theo từng phím. Bàn phím của Unity trên GameActivity không
    ///         sửa được giữa chữ (lý do ở <see cref="BugLoggerNativeEditor" />). Có bàn phím cứng thì sửa ngay trong ô như
    ///         trên máy tính.</item>
    ///         <item>iOS giữ ô nhập native trên bàn phím (kính lúp, chọn chữ, dán đầy đủ); chạm vào chữ trong form thì dời
    ///         con trỏ của ô native tới đó.</item>
    ///         <item>Ô chưa focus: vuốt = cuộn form (chuyển cử chỉ cho <see cref="ScrollRect" /> cha). Bản gốc mở bàn
    ///         phím ngay lúc chạm xuống và nuốt cả cử chỉ kéo ⇒ không cuộn được khi ngón tay đặt trên ô.</item>
    ///         <item>Chạm vào ô chưa focus: mở bàn phím và đặt con trỏ đúng chỗ chạm (bản gốc nhảy về cuối chữ).</item>
    ///     </list>
    /// </summary>
    internal sealed class BugLoggerInputField : InputField, IInitializePotentialDragHandler
    {
        #region Fields

        // Ngón tay xê dịch dưới ngưỡng này vẫn tính là chạm (mở ô); quá ngưỡng mới là vuốt cuộn form.
        private const float TAP_SLOP_INCHES = 0.06f;
        private const float FALLBACK_DPI = 160f;

        private ScrollRect _scroll;
        private bool _isTrackingScroll;
        private bool _isScrolling;
        private int _pendingCaret = -1;
        private int _pendingDrawStart;
        private int _pendingTapFrame;
        private bool _isPressedUnfocused;
        private string _editedText = string.Empty;

        #endregion

        #region Properties

        /// <summary>Tên ô, hiện trên ô sửa native của Android để QA biết đang gõ vào ô nào.</summary>
        public string EditorLabel { get; set; } = string.Empty;

        /// <summary>Đang gõ vào ô này: InputField đang focus, hoặc ô sửa native của Android đang mở cho nó.</summary>
        public bool IsEditing => isFocused || BugLoggerNativeEditor.IsEditing(this);

        /// <summary>
        ///     Ngón tay đang đặt trên ô chưa focus, hoặc vừa chạm và ô sắp mở. Form dựa vào đây để giữ cỡ card khi QA
        ///     chuyển sang ô khác (bàn phím tắt rồi bật lại).
        /// </summary>
        public bool IsAwaitingFocus => _isPressedUnfocused || _pendingCaret >= 0;

        // InputField tự xử lý phím/con trỏ trong ô (máy tính, Android có bàn phím cứng) — ngược với gõ qua ô nhập
        // native của bàn phím (iOS, đường lui của Android): khi đó chạm/kéo trên ô đang focus là việc của ô native.
        private static bool IsEditingInPlace =>
            !TouchScreenKeyboard.isSupported || TouchScreenKeyboard.isInPlaceEditingAllowed;

        #endregion

        #region Initialize

        protected override void Awake()
        {
            base.Awake();
            // Không ẩn ô nhập của bàn phím Unity: ẩn thì InputField đẩy con trỏ xuống bàn phím mỗi frame, mà trên
            // GameActivity mỗi lần đẩy là một lần khởi động lại bộ gõ ⇒ phím xoá / chữ chèn giữa bị nuốt. Android gõ qua ô
            // sửa native, bàn phím Unity chỉ là đường lui khi ô native lỗi; iOS thì đây là ô nhập native trên bàn phím.
            shouldHideMobileInput = false;
            // Chọn ô bằng điều hướng không tự bật bàn phím Unity khi Android gõ qua ô native.
            if (BugLoggerNativeEditor.IsAvailable) shouldActivateOnSelect = false;
            _scroll = GetComponentInParent<ScrollRect>(true);
        }

        protected override void OnDisable()
        {
            BugLoggerNativeEditor.Close(this);

            // Tắt giữa lúc đang chuyển cử chỉ cuộn: EndDrag sau đó tới ô đã tắt, không tới ScrollRect ⇒ tự kết thúc.
            if (_isScrolling && _scroll != null) _scroll.OnEndDrag(new PointerEventData(EventSystem.current));
            _isTrackingScroll = false;
            _isScrolling = false;
            _isPressedUnfocused = false;
            _pendingCaret = -1;
            base.OnDisable();
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();
            ApplyPendingCaret();
            BugLoggerNativeEditor.Tick(this);

            // Chữ đang gõ tới hết frame này — OnDeselect dựa vào đây khi bàn phím trả chữ về cũ.
            if (isFocused) _editedText = text;
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Chữ QA vừa gõ ở ô sửa native: ghi vào ô (bắn onValueChanged như gõ thường — bộ đếm, xoá lỗi) và kéo khung
        ///     chữ tới chỗ con trỏ native để ô trên form hiện đúng đoạn đang sửa.
        /// </summary>
        public void ApplyNativeText(string value, int caret)
        {
            text = value;
            caret = Mathf.Clamp(caret, 0, text.Length);
            caretPositionInternal = caret;
            caretSelectPositionInternal = caret;
            UpdateLabel();
        }

        #endregion

        #region Events

        public override void OnPointerDown(PointerEventData eventData)
        {
            // Sửa trong ô + đã focus: giữ hành vi gốc (đặt con trỏ / bắt đầu kéo chọn chữ). Còn lại đợi nhả tay.
            if (!isFocused) _isPressedUnfocused = true;
            else if (IsEditingInPlace) base.OnPointerDown(eventData);
        }

        public override void OnPointerUp(PointerEventData eventData)
        {
            _isPressedUnfocused = false;
            base.OnPointerUp(eventData);
        }

        public override void OnDeselect(BaseEventData eventData)
        {
            // Bàn phím đóng kiểu Canceled (phím Back Android) ⇒ bàn phím lẫn InputField gốc trả chữ về như lúc mở ô: QA
            // mất sạch chữ vừa gõ. Form báo lỗi không có khái niệm "huỷ sửa" nên luôn giữ chữ đã gõ tới frame trước.
            var wasEditing = isFocused;
            base.OnDeselect(eventData);
            if (wasEditing && text != _editedText) text = _editedText;
        }

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            // Chạm vào ô lúc form đang trôi quán tính thì dừng trôi, như chạm vào chỗ trống của form.
            if (_scroll != null && !(isFocused && IsEditingInPlace)) _scroll.OnInitializePotentialDrag(eventData);
        }

        public override void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && textComponent != null)
            {
                // Android: gõ qua ô sửa native; không mở được thì đi tiếp đường bàn phím Unity bên dưới.
                if (!isFocused && IsActive() && IsInteractable() && !readOnly &&
                    BugLoggerNativeEditor.ShouldUse && BugLoggerNativeEditor.Open(this, GetCharacterIndexAt(eventData)))
                    return;

                if (!isFocused)
                {
                    // Tính ngay theo khung chữ đang hiện — sau khi kích hoạt khung đã cuộn về cuối, tính lúc đó lệch dòng.
                    _pendingCaret = GetCharacterIndexAt(eventData);
                    _pendingDrawStart = m_DrawStart;
                    _pendingTapFrame = Time.frameCount;
                }
                else if (!IsEditingInPlace)
                {
                    MoveCaret(GetCharacterIndexAt(eventData), m_DrawStart);
                }
            }

            base.OnPointerClick(eventData);
        }

        public override void OnBeginDrag(PointerEventData eventData)
        {
            // Đang sửa trong ô thì kéo = chọn chữ; còn lại kéo = cuộn form.
            if ((isFocused && IsEditingInPlace) || _scroll == null)
            {
                base.OnBeginDrag(eventData);
                return;
            }

            _isTrackingScroll = true;
            TryStartScroll(eventData);
        }

        public override void OnDrag(PointerEventData eventData)
        {
            if (_isScrolling)
            {
                _scroll.OnDrag(eventData);
                return;
            }

            if (_isTrackingScroll)
            {
                TryStartScroll(eventData);
                return;
            }

            base.OnDrag(eventData);
        }

        public override void OnEndDrag(PointerEventData eventData)
        {
            if (!_isTrackingScroll)
            {
                base.OnEndDrag(eventData);
                return;
            }

            if (_isScrolling) _scroll.OnEndDrag(eventData);
            _isTrackingScroll = false;
            _isScrolling = false;
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Chạm mở ô ⇒ InputField kích hoạt trong LateUpdate (cùng frame) và nhảy con trỏ về cuối chữ, kéo theo khung
        ///     chữ của ô nhiều dòng. Kích hoạt xong mới đặt con trỏ vào chỗ đã chạm và trả khung chữ về chỗ QA đang nhìn.
        /// </summary>
        private void ApplyPendingCaret()
        {
            if (_pendingCaret < 0) return;

            if (isFocused)
            {
                MoveCaret(_pendingCaret, _pendingDrawStart);
                _pendingCaret = -1;
                return;
            }

            if (Time.frameCount - _pendingTapFrame > 1) _pendingCaret = -1;
        }

        private int GetCharacterIndexAt(PointerEventData eventData)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(textComponent.rectTransform,
                eventData.pointerPressRaycast.screenPosition, eventData.pressEventCamera, out var localPoint);
            return GetCharacterIndexFromPosition(localPoint) + m_DrawStart;
        }

        private void MoveCaret(int caret, int drawStart)
        {
            caret = Mathf.Clamp(caret, 0, text.Length);
            m_DrawStart = Mathf.Clamp(drawStart, 0, caret);
            selectionAnchorPosition = caret;
            selectionFocusPosition = caret;

            // Ô nhập native của bàn phím giữ con trỏ riêng (InputField đọc ngược từ nó mỗi frame) ⇒ phải gán xuống.
            var keyboard = touchScreenKeyboard;
            if (keyboard != null && keyboard.canSetSelection) keyboard.selection = new RangeInt(caret, 0);
            ForceLabelUpdate();
        }

        private void TryStartScroll(PointerEventData eventData)
        {
            var dpi = Screen.dpi > 0f ? Screen.dpi : FALLBACK_DPI;
            var slop = Mathf.Max(EventSystem.current != null ? EventSystem.current.pixelDragThreshold : 0,
                dpi * TAP_SLOP_INCHES);
            if ((eventData.position - eventData.pressPosition).sqrMagnitude < slop * slop) return;

            // Từ đây là vuốt cuộn: nhả tay không còn tính là chạm mở ô.
            _isScrolling = true;
            _isPressedUnfocused = false;
            eventData.eligibleForClick = false;
            _scroll.OnInitializePotentialDrag(eventData);
            _scroll.OnBeginDrag(eventData);
        }

        #endregion
    }
}
