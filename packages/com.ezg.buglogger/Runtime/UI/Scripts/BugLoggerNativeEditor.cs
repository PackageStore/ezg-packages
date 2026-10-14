using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Ezg.BugLogger.UI
{
    /// <summary>
    ///     Ô sửa chữ native của Android (EditText thật, nằm ngay trên bàn phím) cho form báo lỗi: QA gõ trong ô này, chữ
    ///     được ghi ngược vào ô của form. Đủ thao tác của ô nhập Android: con trỏ, kéo tay cầm chọn chữ, dán,
    ///     xoá/chèn giữa chữ, bộ gõ tiếng Việt.
    ///     <para>
    ///         Không dùng bàn phím của Unity vì app chạy GameActivity: <see cref="TouchScreenKeyboard" /> không có ô nhập
    ///         trên bàn phím, và mỗi lần nhận vị trí con trỏ (<c>selection</c>) nó gọi <c>restartInput</c> — khởi động lại
    ///         kết nối với bộ gõ. InputField ẩn ô nhập thì đẩy con trỏ xuống bàn phím MỖI FRAME ⇒ bộ gõ bị reset liên tục:
    ///         phím xoá, chữ chèn giữa, dấu tiếng Việt bị nuốt. Không ẩn ô nhập thì không có con trỏ, chỉ gõ/xoá ở cuối.
    ///     </para>
    ///     <para>
    ///         Dựng hoàn toàn bằng JNI (không có file .java) ⇒ không phụ thuộc Gradle/R8 của từng project. Ô sửa là một
    ///         Dialog nên lúc mở app mất focus: Unity bỏ qua chạm vào form, GameActivity còn dừng hẳn vòng lặp ⇒ form đứng
    ///         hình sau lớp mờ, chữ về ô lúc đóng ô sửa. Vì thế ô sửa tự ghi tên ô và form không co né bàn phím. Chạm ra
    ///         ngoài / phím Back / nút Xong là đóng (chữ luôn được giữ) — giống ô nhập Android kiểu cũ của Unity. Lỗi JNI
    ///         bất kỳ ⇒ tắt hẳn, ô nhập quay về bàn phím Unity.
    ///     </para>
    /// </summary>
    internal static class BugLoggerNativeEditor
    {
        #region Fields

#if UNITY_ANDROID
        // Ô mô tả cao tối đa ngần này dòng rồi cuộn trong ô — máy màn nhỏ vẫn còn chỗ cho bàn phím.
        private const int MULTILINE_MAX_LINES = 6;
        private const float LABEL_TEXT_SP = 13f;
        private const float EDIT_TEXT_SP = 16f;
        private const float DONE_TEXT_SP = 15f;
        // Dấu tiếng Việt chồng hai tầng ⇒ giãn dòng chút cho khỏi dính dòng trên.
        private const float EDIT_LINE_SPACING = 1.1f;
        private const float HIGHLIGHT_ALPHA = 0.35f;
        // Form phía sau đứng hình (xem trên) ⇒ mờ đủ đậm để rõ đó là nền, không phải ô đang gõ.
        private const float DIM_AMOUNT = 0.5f;

        // Kích thước theo dp.
        private const int PANEL_PADDING_X = 16;
        private const int PANEL_PADDING_TOP = 10;
        private const int PANEL_PADDING_BOTTOM = 12;
        private const int LABEL_GAP = 6;
        private const int CONTROL_MIN_HEIGHT = 44;
        private const int CONTROL_RADIUS = 12;
        private const int EDIT_PADDING_X = 12;
        private const int EDIT_PADDING_Y = 10;
        private const int EDIT_STROKE = 1;
        private const int DONE_PADDING_X = 18;
        private const int DONE_GAP = 10;
        private const int CURSOR_WIDTH = 2;
        private const int CURSOR_HEIGHT = 20;

        // Hằng của Android SDK (InputType, EditorInfo, Gravity, LayoutParams, WindowManager.LayoutParams, TypedValue).
        private const int TYPE_CLASS_TEXT = 0x1;
        private const int TYPE_TEXT_FLAG_CAP_SENTENCES = 0x4000;
        private const int TYPE_TEXT_FLAG_AUTO_CORRECT = 0x8000;
        private const int TYPE_TEXT_FLAG_MULTI_LINE = 0x20000;
        private const int IME_ACTION_DONE = 6;
        private const int IME_FLAG_NO_FULLSCREEN = 0x2000000;
        private const int IME_FLAG_NO_EXTRACT_UI = 0x10000000;
        private const int GRAVITY_CENTER = 17;
        private const int GRAVITY_BOTTOM = 80;
        private const int GRAVITY_TOP_START = 0x00800033;
        private const int GRAVITY_CENTER_VERTICAL_START = 0x00800013;
        private const int ORIENTATION_HORIZONTAL = 0;
        private const int ORIENTATION_VERTICAL = 1;
        private const int MATCH_PARENT = -1;
        private const int WRAP_CONTENT = -2;
        private const int COMPLEX_UNIT_SP = 2;
        private const int SOFT_INPUT_STATE_ALWAYS_VISIBLE = 5;
        private const int SOFT_INPUT_ADJUST_RESIZE = 0x10;
        private const int API_TEXT_CURSOR_DRAWABLE = 29;
        private const string INPUT_METHOD_SERVICE = "input_method";

        private static readonly object Gate = new object();

        // Luồng chính Unity.
        private static BugLoggerInputField _owner;
        private static Session _ownerSession;
        private static int _lastSessionId;
        private static int _appliedRevision;

        // Luồng UI Android ghi, luồng chính đọc — chỉ chạm khi giữ Gate.
        private static int _openSessionId;
        private static string _sharedText = string.Empty;
        private static int _sharedCaret;
        private static int _sharedTextSessionId;
        private static int _sharedRevision;

        // Chỉ luồng UI Android.
        private static Session _uiSession;
        private static float _density;
        private static int _sdkInt;

        private static volatile bool _isBroken;
#endif

        #endregion

        #region Properties

        /// <summary>Máy Android và ô sửa native chưa từng lỗi.</summary>
        public static bool IsAvailable
        {
            get
            {
#if UNITY_ANDROID
                return Application.platform == RuntimePlatform.Android && !_isBroken;
#else
                return false;
#endif
            }
        }

        /// <summary>Gõ qua ô sửa native — trừ khi có bàn phím cứng (khi đó InputField sửa ngay trong ô như máy tính).</summary>
        public static bool ShouldUse => IsAvailable && TouchScreenKeyboard.isSupported &&
                                        !TouchScreenKeyboard.isInPlaceEditingAllowed;

        /// <summary>Đang mở ô sửa native cho một ô nào đó của form.</summary>
        public static bool IsActive
        {
            get
            {
#if UNITY_ANDROID
                return _owner != null;
#else
                return false;
#endif
            }
        }

        #endregion

        #region Public Methods

        public static bool IsEditing(BugLoggerInputField field)
        {
#if UNITY_ANDROID
            return field != null && _owner == field;
#else
            return false;
#endif
        }

        /// <summary>
        ///     Mở ô sửa native cho <paramref name="field" />, con trỏ đặt ở <paramref name="caret" />. Trả false nếu không mở
        ///     được — caller dùng bàn phím Unity.
        /// </summary>
        public static bool Open(BugLoggerInputField field, int caret)
        {
#if UNITY_ANDROID
            if (field == null || !ShouldUse) return false;
            if (_owner == field) return true;

            try
            {
                if (_owner != null) End(true);

                var session = new Session
                {
                    Id = ++_lastSessionId,
                    Label = field.EditorLabel ?? string.Empty,
                    Text = field.text ?? string.Empty,
                    Hint = field.placeholder is Text placeholder ? placeholder.text : string.Empty,
                    Done = BugLoggerStrings.EDITOR_DONE,
                    Caret = caret,
                    Limit = field.characterLimit,
                    IsMultiLine = field.multiLine
                };

                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    session.Activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                if (session.Activity == null) return false;

                _owner = field;
                _ownerSession = session;
                lock (Gate) _openSessionId = session.Id;

                RunOnUi(session, () => ShowOnUi(session));
                return true;
            }
            catch (Exception ex)
            {
                MarkBroken(ex);
                _owner = null;
                _ownerSession = null;
                lock (Gate) _openSessionId = 0;
                return false;
            }
#else
            return false;
#endif
        }

        /// <summary>Đóng ô sửa native nếu nó đang mở cho <paramref name="field" />; chữ đã gõ được ghi vào ô trước.</summary>
        public static void Close(BugLoggerInputField field)
        {
#if UNITY_ANDROID
            if (field != null && _owner == field) End(true);
#endif
        }

        /// <summary>Đóng ô sửa native đang mở (nếu có) — form đóng thì ô sửa đóng theo, chữ đã gõ được giữ.</summary>
        public static void CloseActive()
        {
#if UNITY_ANDROID
            if (_owner != null) End(true);
#endif
        }

        /// <summary>Gọi mỗi frame từ ô đang sửa: kéo chữ mới về ô, nhận biết ô sửa đã đóng (Xong / Back / chạm ra ngoài).</summary>
        public static void Tick(BugLoggerInputField field)
        {
#if UNITY_ANDROID
            if (field == null || _owner != field) return;

            PullText();

            bool isOpen;
            lock (Gate) isOpen = _openSessionId == _ownerSession.Id;
            if (!isOpen) End(false);
#endif
        }

        #endregion

        #region Private Methods

#if UNITY_ANDROID
        // ---------- Luồng chính Unity ----------

        private static void End(bool closeUi)
        {
            PullText();

            var session = _ownerSession;
            lock (Gate)
            {
                if (_openSessionId == session.Id) _openSessionId = 0;
            }

            _owner = null;
            _ownerSession = null;

            if (!closeUi) return;

            try
            {
                RunOnUi(session, () => CloseOnUi(session.Id));
            }
            catch (Exception ex)
            {
                MarkBroken(ex);
            }
        }

        private static void PullText()
        {
            string text = null;
            var caret = 0;
            lock (Gate)
            {
                if (_sharedTextSessionId == _ownerSession.Id && _sharedRevision != _appliedRevision)
                {
                    text = _sharedText;
                    caret = _sharedCaret;
                    _appliedRevision = _sharedRevision;
                }
            }

            if (text != null) _owner.ApplyNativeText(text, caret);
        }

        private static void RunOnUi(Session session, Action action)
        {
            session.Activity.Call("runOnUiThread", new AndroidJavaRunnable(() => Guard(action)));
        }

        // ---------- Luồng UI Android ----------

        // Exception lọt ra khỏi code C# chạy trên luồng UI = exception Java không ai bắt ⇒ crash app.
        private static void Guard(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                MarkBroken(ex);
                lock (Gate) _openSessionId = 0;
                try
                {
                    _uiSession?.Dialog?.Call("dismiss");
                }
                catch (Exception)
                {
                    // Đã tắt ô sửa; Dialog không đóng được thì QA chạm ra ngoài / Back vẫn đóng.
                }
            }
        }

        private static void ShowOnUi(Session session)
        {
            // Form đã đóng ô sửa trước khi luồng UI kịp mở.
            lock (Gate)
            {
                if (_openSessionId != session.Id) return;
            }

            if (_uiSession != null)
            {
                var previous = _uiSession;
                _uiSession = null;
                previous.Dialog.Call("dismiss");
            }

            Build(session);
            _uiSession = session;
            session.Dialog.Call("show");
        }

        private static void CloseOnUi(int sessionId)
        {
            var session = _uiSession;
            if (session == null || session.Id != sessionId) return;

            HideKeyboard(session, session.Edit);
            session.Dialog.Call("dismiss");
        }

        private static void OnShown(Session session)
        {
            session.Edit.Call<bool>("requestFocus");
            using (var input = session.Activity.Call<AndroidJavaObject>("getSystemService", INPUT_METHOD_SERVICE))
                input?.Call<bool>("showSoftInput", session.Edit, 0);
        }

        private static void OnDismissed(Session session)
        {
            lock (Gate)
            {
                if (_openSessionId == session.Id) _openSessionId = 0;
            }

            if (_uiSession == session) _uiSession = null;

            // Cửa sổ game lấy lại focus — đừng để bàn phím treo trên đó (gõ vào đâu cũng không ăn).
            HideKeyboard(session, session.Decor);
        }

        private static void PublishText(Session session, AndroidJavaObject editable)
        {
            var text = editable.Call<string>("toString") ?? string.Empty;
            var caret = session.Edit != null ? session.Edit.Call<int>("getSelectionEnd") : text.Length;
            lock (Gate)
            {
                _sharedText = text;
                _sharedCaret = caret;
                _sharedTextSessionId = session.Id;
                _sharedRevision++;
            }
        }

        private static void HideKeyboard(Session session, AndroidJavaObject view)
        {
            if (view == null) return;

            using (var input = session.Activity.Call<AndroidJavaObject>("getSystemService", INPUT_METHOD_SERVICE))
            using (var token = view.Call<AndroidJavaObject>("getWindowToken"))
            {
                if (input != null && token != null) input.Call<bool>("hideSoftInputFromWindow", token, 0);
            }
        }

        private static void Build(Session session)
        {
            var activity = session.Activity;
            if (_density <= 0f)
            {
                using (var resources = activity.Call<AndroidJavaObject>("getResources"))
                using (var metrics = resources.Call<AndroidJavaObject>("getDisplayMetrics"))
                    _density = metrics.Get<float>("density");

                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                    _sdkInt = version.GetStatic<int>("SDK_INT");
            }

            using (var window = activity.Call<AndroidJavaObject>("getWindow"))
                session.Decor = window.Call<AndroidJavaObject>("getDecorView");

            int theme;
            int animation;
            using (var styles = new AndroidJavaClass("android.R$style"))
            {
                theme = styles.GetStatic<int>("Theme_Material_Dialog_NoActionBar");
                animation = styles.GetStatic<int>("Animation_InputMethod");
            }

            session.Dialog = new AndroidJavaObject("android.app.Dialog", activity, theme);
            // View dựng bằng context của Dialog để ăn theme tối (tay cầm chọn chữ, menu dán…).
            using (var context = session.Dialog.Call<AndroidJavaObject>("getContext"))
                session.Panel = BuildPanel(session, context);

            session.Dialog.Call("setContentView", session.Panel);
            session.Dialog.Call("setCancelable", true);
            session.Dialog.Call("setCanceledOnTouchOutside", true);
            session.Dialog.Call("setOnShowListener", session.Keep(new ShowListener(session)));
            session.Dialog.Call("setOnDismissListener", session.Keep(new DismissListener(session)));

            using (var window = session.Dialog.Call<AndroidJavaObject>("getWindow"))
            {
                using (var background = new AndroidJavaObject("android.graphics.drawable.ColorDrawable",
                           ToArgb(BugLoggerPalette.Card)))
                    window.Call("setBackgroundDrawable", background);
                using (var decor = window.Call<AndroidJavaObject>("getDecorView"))
                    decor.Call("setPadding", 0, 0, 0, 0);

                window.Call("setLayout", MATCH_PARENT, WRAP_CONTENT);
                window.Call("setGravity", GRAVITY_BOTTOM);
                window.Call("setDimAmount", DIM_AMOUNT);
                window.Call("setSoftInputMode", SOFT_INPUT_STATE_ALWAYS_VISIBLE | SOFT_INPUT_ADJUST_RESIZE);
                window.Call("setWindowAnimations", animation);
            }
        }

        private static AndroidJavaObject BuildPanel(Session session, AndroidJavaObject context)
        {
            var panel = new AndroidJavaObject("android.widget.LinearLayout", context);
            panel.Call("setOrientation", ORIENTATION_VERTICAL);
            panel.Call("setPadding", Dp(PANEL_PADDING_X), Dp(PANEL_PADDING_TOP), Dp(PANEL_PADDING_X),
                Dp(PANEL_PADDING_BOTTOM));

            if (session.Label.Length > 0)
                using (var label = NewText(context, session.Label, LABEL_TEXT_SP, BugLoggerPalette.TextSecondary))
                {
                    label.Call("setPadding", 0, 0, 0, Dp(LABEL_GAP));
                    panel.Call("addView", label);
                }

            using (var row = new AndroidJavaObject("android.widget.LinearLayout", context))
            {
                row.Call("setOrientation", ORIENTATION_HORIZONTAL);
                row.Call("setGravity", GRAVITY_BOTTOM);

                session.Edit = BuildEdit(session, context);
                using (var editLayout = new AndroidJavaObject("android.widget.LinearLayout$LayoutParams", 0,
                           WRAP_CONTENT, 1f))
                    row.Call("addView", session.Edit, editLayout);

                using (var done = BuildDone(session, context))
                using (var doneLayout = new AndroidJavaObject("android.widget.LinearLayout$LayoutParams", WRAP_CONTENT,
                           WRAP_CONTENT))
                {
                    doneLayout.Call("setMargins", Dp(DONE_GAP), 0, 0, 0);
                    row.Call("addView", done, doneLayout);
                }

                panel.Call("addView", row);
            }

            return panel;
        }

        private static AndroidJavaObject BuildEdit(Session session, AndroidJavaObject context)
        {
            var edit = new AndroidJavaObject("android.widget.EditText", context);

            // setInputType tự chuyển một dòng / nhiều dòng; số dòng tối đa đặt sau nó.
            var inputType = TYPE_CLASS_TEXT | TYPE_TEXT_FLAG_CAP_SENTENCES | TYPE_TEXT_FLAG_AUTO_CORRECT;
            if (session.IsMultiLine) inputType |= TYPE_TEXT_FLAG_MULTI_LINE;
            edit.Call("setInputType", inputType);
            var imeOptions = IME_FLAG_NO_FULLSCREEN | IME_FLAG_NO_EXTRACT_UI;
            if (!session.IsMultiLine) imeOptions |= IME_ACTION_DONE;
            edit.Call("setImeOptions", imeOptions);
            if (session.IsMultiLine) edit.Call("setMaxLines", MULTILINE_MAX_LINES);
            edit.Call("setGravity", session.IsMultiLine ? GRAVITY_TOP_START : GRAVITY_CENTER_VERTICAL_START);

            edit.Call("setTextSize", COMPLEX_UNIT_SP, EDIT_TEXT_SP);
            edit.Call("setLineSpacing", 0f, EDIT_LINE_SPACING);
            edit.Call("setTextColor", ToArgb(BugLoggerPalette.TextPrimary));
            edit.Call("setHintTextColor", ToArgb(BugLoggerPalette.TextMuted));
            edit.Call("setHighlightColor", ToArgb(BugLoggerPalette.WithAlpha(BugLoggerPalette.Accent, HIGHLIGHT_ALPHA)));
            edit.Call("setMinHeight", Dp(CONTROL_MIN_HEIGHT));
            edit.Call("setPadding", Dp(EDIT_PADDING_X), Dp(EDIT_PADDING_Y), Dp(EDIT_PADDING_X), Dp(EDIT_PADDING_Y));
            using (var background = NewRoundRect(BugLoggerPalette.Surface, BugLoggerPalette.Accent, Dp(EDIT_STROKE)))
                edit.Call("setBackground", background);

            if (_sdkInt >= API_TEXT_CURSOR_DRAWABLE)
                using (var cursor = new AndroidJavaObject("android.graphics.drawable.GradientDrawable"))
                {
                    cursor.Call("setColor", ToArgb(BugLoggerPalette.Accent));
                    cursor.Call("setSize", Dp(CURSOR_WIDTH), Dp(CURSOR_HEIGHT));
                    edit.Call("setTextCursorDrawable", cursor);
                }

            if (session.Limit > 0) SetLengthLimit(edit, session.Limit);

            edit.Call("setHint", session.Hint);
            edit.Call("setText", session.Text);
            edit.Call("setSelection", Mathf.Clamp(session.Caret, 0, edit.Call<int>("length")));

            // Gắn sau setText: chữ ban đầu là của form, không cần gửi ngược.
            edit.Call("addTextChangedListener", session.Keep(new TextWatcher(session)));
            edit.Call("setOnEditorActionListener", session.Keep(new EditorActionListener(session)));
            edit.Call<bool>("requestFocus");
            return edit;
        }

        private static AndroidJavaObject BuildDone(Session session, AndroidJavaObject context)
        {
            var done = NewText(context, session.Done, DONE_TEXT_SP, BugLoggerPalette.OnAccent);
            using (var typeface = new AndroidJavaClass("android.graphics.Typeface"))
            using (var bold = typeface.GetStatic<AndroidJavaObject>("DEFAULT_BOLD"))
                done.Call("setTypeface", bold);

            done.Call("setGravity", GRAVITY_CENTER);
            done.Call("setMinHeight", Dp(CONTROL_MIN_HEIGHT));
            done.Call("setPadding", Dp(DONE_PADDING_X), 0, Dp(DONE_PADDING_X), 0);
            using (var background = NewRoundRect(BugLoggerPalette.Accent, BugLoggerPalette.Accent, 0))
                done.Call("setBackground", background);

            done.Call("setOnClickListener", session.Keep(new ClickListener(session)));
            return done;
        }

        private static AndroidJavaObject NewText(AndroidJavaObject context, string text, float sizeSp, Color color)
        {
            var view = new AndroidJavaObject("android.widget.TextView", context);
            view.Call("setText", text);
            view.Call("setTextSize", COMPLEX_UNIT_SP, sizeSp);
            view.Call("setTextColor", ToArgb(color));
            return view;
        }

        private static AndroidJavaObject NewRoundRect(Color fill, Color stroke, int strokeWidth)
        {
            var drawable = new AndroidJavaObject("android.graphics.drawable.GradientDrawable");
            drawable.Call("setColor", ToArgb(fill));
            drawable.Call("setCornerRadius", (float)Dp(CONTROL_RADIUS));
            if (strokeWidth > 0) drawable.Call("setStroke", strokeWidth, ToArgb(stroke));
            return drawable;
        }

        // TextView.setFilters nhận InputFilter[] — mảng kiểu riêng phải dựng bằng JNI thô.
        private static void SetLengthLimit(AndroidJavaObject edit, int limit)
        {
            using (var filter = new AndroidJavaObject("android.text.InputFilter$LengthFilter", limit))
            using (var filterClass = new AndroidJavaClass("android.text.InputFilter"))
            {
                var filters = AndroidJNI.NewObjectArray(1, filterClass.GetRawClass(), filter.GetRawObject());
                try
                {
                    var method = AndroidJNI.GetMethodID(edit.GetRawClass(), "setFilters",
                        "([Landroid/text/InputFilter;)V");
                    ThrowIfJavaException();
                    AndroidJNI.CallVoidMethod(edit.GetRawObject(), method, new[] { new jvalue { l = filters } });
                    ThrowIfJavaException();
                }
                finally
                {
                    AndroidJNI.DeleteLocalRef(filters);
                }
            }
        }

        // ---------- Dùng chung ----------

        private static void MarkBroken(Exception ex)
        {
            _isBroken = true;
            Debug.LogWarning($"[BugLogger] Ô sửa chữ native lỗi — quay về bàn phím Unity. {ex.Message}");
        }

        private static void ThrowIfJavaException()
        {
            var exception = AndroidJNI.ExceptionOccurred();
            if (exception == IntPtr.Zero) return;

            AndroidJNI.ExceptionClear();
            AndroidJNI.DeleteLocalRef(exception);
            throw new InvalidOperationException("JNI call failed");
        }

        private static int Dp(int value)
        {
            return Mathf.RoundToInt(value * _density);
        }

        private static int ToArgb(Color color)
        {
            Color32 c = color;
            return (c.a << 24) | (c.r << 16) | (c.g << 8) | c.b;
        }
#endif

        #endregion

#if UNITY_ANDROID
        #region Nested Types

        /// <summary>
        ///     Một lần mở ô sửa: Dialog + view riêng, listener biết mình thuộc lần nào (đóng lần cũ không đè lần mới).
        ///     Không Dispose các AndroidJavaObject ở đây: hai luồng cùng giữ, để GC thu.
        /// </summary>
        private sealed class Session
        {
            public int Id;
            public string Label;
            public string Text;
            public string Hint;
            public string Done;
            public int Caret;
            public int Limit;
            public bool IsMultiLine;

            public AndroidJavaObject Activity;
            public AndroidJavaObject Decor;
            public AndroidJavaObject Dialog;
            public AndroidJavaObject Panel;
            public AndroidJavaObject Edit;

            // Giữ proxy sống bằng Dialog — GC thu proxy thì listener phía Java gọi vào đối tượng đã chết.
            private readonly List<AndroidJavaProxy> _listeners = new List<AndroidJavaProxy>();

            public AndroidJavaProxy Keep(AndroidJavaProxy listener)
            {
                _listeners.Add(listener);
                return listener;
            }
        }

        // Proxy JNI: tên + tham số phải khớp đúng interface Java (camelCase), sai là listener ném lỗi ở luồng UI.

        private sealed class TextWatcher : AndroidJavaProxy
        {
            private readonly Session _session;

            public TextWatcher(Session session) : base("android.text.TextWatcher")
            {
                _session = session;
            }

            public void beforeTextChanged(AndroidJavaObject text, int start, int count, int after)
            {
            }

            public void onTextChanged(AndroidJavaObject text, int start, int before, int count)
            {
            }

            public void afterTextChanged(AndroidJavaObject editable)
            {
                Guard(() => PublishText(_session, editable));
            }
        }

        private sealed class EditorActionListener : AndroidJavaProxy
        {
            private readonly Session _session;

            public EditorActionListener(Session session) : base("android.widget.TextView$OnEditorActionListener")
            {
                _session = session;
            }

            public bool onEditorAction(AndroidJavaObject view, int actionId, AndroidJavaObject keyEvent)
            {
                if (actionId != IME_ACTION_DONE) return false;

                Guard(() => CloseOnUi(_session.Id));
                return true;
            }
        }

        private sealed class ClickListener : AndroidJavaProxy
        {
            private readonly Session _session;

            public ClickListener(Session session) : base("android.view.View$OnClickListener")
            {
                _session = session;
            }

            public void onClick(AndroidJavaObject view)
            {
                Guard(() => CloseOnUi(_session.Id));
            }
        }

        private sealed class ShowListener : AndroidJavaProxy
        {
            private readonly Session _session;

            public ShowListener(Session session) : base("android.content.DialogInterface$OnShowListener")
            {
                _session = session;
            }

            public void onShow(AndroidJavaObject dialog)
            {
                Guard(() => OnShown(_session));
            }
        }

        private sealed class DismissListener : AndroidJavaProxy
        {
            private readonly Session _session;

            public DismissListener(Session session) : base("android.content.DialogInterface$OnDismissListener")
            {
                _session = session;
            }

            public void onDismiss(AndroidJavaObject dialog)
            {
                Guard(() => OnDismissed(_session));
            }
        }

        #endregion
#endif
    }
}
