#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Ezg.Editor.Shared.EzgKit
{
    /// <summary>
    ///     Trạng thái của một thứ được bày ra trong EzgKit. Bốn mức, vì đó là bốn cách xử lý khác nhau
    ///     của người dùng: kệ nó (<see cref="None" />), xong rồi (<see cref="Ok" />), phải làm gì đó
    ///     (<see cref="Warn" />), hỏng phải sửa ngay (<see cref="Error" />).
    /// </summary>
    internal enum EzgStatus
    {
        /// <summary>Chưa biết / không áp dụng. KHÔNG phải lỗi — không tô màu cảnh báo.</summary>
        None = 0,

        Ok = 1,

        /// <summary>Có giá trị nhưng lệch, hoặc còn thiếu thứ bắt buộc — người dùng phải làm tiếp.</summary>
        Warn = 2,

        /// <summary>Sai tới mức chạy tiếp là hỏng (config trỏ sang dự án khác, fetch thất bại…).</summary>
        Error = 3,
    }

    /// <summary>
    ///     Ngôn ngữ hình ảnh dùng chung cho MỌI tab của EzgKit. Tồn tại vì trước đây mỗi page tự chế
    ///     <c>Row()</c>, tự chế badge <c>[OK]/[!]</c>, tự chọn <c>helpBox</c> — ba tab nhìn như ba tool
    ///     khác nhau và không tab nào phân biệt nổi "đang khớp" với "đang lệch" chỉ bằng mắt.
    ///     <para>
    ///         <b>Quy ước màu:</b> màu CHỈ mang nghĩa trạng thái (xanh = khớp, vàng = còn việc, đỏ =
    ///         hỏng). Không dùng màu để trang trí — bày màu vô nghĩa là mắt hết bắt được cái quan trọng.
    ///     </para>
    ///     <para>
    ///         <b>Style dựng lazy trong lúc vẽ</b> (<see cref="Build" />): <see cref="EditorStyles" />
    ///         chỉ dùng được trong OnGUI, và đổi skin Light/Dark thì toàn bộ màu phải dựng lại.
    ///     </para>
    /// </summary>
    internal static class EzgKitStyles
    {
        #region Metrics

        /// <summary>Bề rộng cột nhãn. Dùng CHUNG cho mọi tab — cột nhãn lệch nhau là nhìn ra ngay.</summary>
        internal const float LABEL_WIDTH = 172f;

        /// <summary>Cột nav dọc: đủ cho tên tool dài nhất + số bước + icon trạng thái.</summary>
        internal const float SIDEBAR_WIDTH = 212f;

        internal const float ICON_WIDTH = 20f;

        /// <summary>Chiều cao nút hành động chính. Đủ to để không bấm nhầm nút phụ bên cạnh.</summary>
        internal const float BUTTON_HEIGHT = 30f;

        #endregion

        #region Palette

        internal static Color OkColor => Get(ref _ok);
        internal static Color WarnColor => Get(ref _warn);
        internal static Color ErrorColor => Get(ref _error);
        internal static Color MutedColor => Get(ref _muted);
        internal static Color AccentColor => Get(ref _accent);
        internal static Color DividerColor => Get(ref _divider);

        private static Color _ok, _warn, _error, _muted, _accent, _divider;

        private static Color Get(ref Color field)
        {
            Build();
            return field;
        }

        #endregion

        #region Styles

        /// <summary>Tiêu đề của cả trang — mỗi tab đúng một cái, ở đầu vùng nội dung.</summary>
        internal static GUIStyle PageTitle => GetStyle(ref _pageTitle);

        /// <summary>Tiêu đề một khối trong trang.</summary>
        internal static GUIStyle SectionTitle => GetStyle(ref _sectionTitle);

        /// <summary>Chữ giải thích: nhỏ, xám, tự xuống dòng. Không bao giờ mang thông tin bắt buộc.</summary>
        internal static GUIStyle Hint => GetStyle(ref _hint);

        /// <summary>Nhãn xám một dòng, không xuống dòng — dùng cho meta ở header bar.</summary>
        internal static GUIStyle MutedLabel => GetStyle(ref _mutedLabel);

        /// <summary>Nền khối nội dung. Thay cho <c>EditorStyles.helpBox</c> lồng nhau nhiều tầng.</summary>
        internal static GUIStyle CardStyle => GetStyle(ref _card);

        /// <summary>Thanh ngang đậm màu ở đỉnh cửa sổ và đáy cột nav.</summary>
        internal static GUIStyle BarStyle => GetStyle(ref _bar);

        internal static GUIStyle SidebarStyle => GetStyle(ref _sidebar);

        /// <summary>Một mục trong cột nav. Vẽ bằng Toggle-kiểu-nút nên cần cả on/off state.</summary>
        internal static GUIStyle NavItemStyle => GetStyle(ref _navItem);

        /// <summary>Số thứ tự bước, hình vuông đậm màu đứng đầu mục nav / thẻ bước.</summary>
        internal static GUIStyle StepBadgeStyle => GetStyle(ref _stepBadge);

        /// <summary>Giá trị chỉ-đọc, cỡ chữ thường. Đi kèm <see cref="KeyValue" />.</summary>
        internal static GUIStyle ValueStyle => GetStyle(ref _value);

        /// <summary>Giá trị rỗng — xám nhạt + nghiêng, để "chưa có" không bị đọc nhầm thành giá trị.</summary>
        internal static GUIStyle EmptyValueStyle => GetStyle(ref _emptyValue);

        private static GUIStyle _pageTitle, _sectionTitle, _hint, _mutedLabel, _card, _bar, _sidebar;
        private static GUIStyle _navItem, _stepBadge, _value, _emptyValue;

        private static GUIStyle GetStyle(ref GUIStyle field)
        {
            Build();
            return field;
        }

        #endregion

        #region Build

        private static bool _built;
        private static bool _builtDark;

        /// <summary>
        ///     Dựng lại toàn bộ style khi chưa dựng, hoặc khi user đổi skin Light↔Dark giữa phiên
        ///     (texture nền dựng theo skin cũ sẽ chọi hẳn với nền mới).
        /// </summary>
        private static void Build()
        {
            var dark = EditorGUIUtility.isProSkin;
            if (_built && _builtDark == dark) return;

            _built = true;
            _builtDark = dark;
            ReleaseTextures();
            _pillStyles.Clear();

            // IconContent tự chọn biến thể "d_" theo skin: không xoá cache thì sau khi user đổi
            // Light↔Dark, icon trạng thái vẫn là bộ của skin cũ.
            _icons.Clear();

            _ok = dark ? Rgb(0x6FC276) : Rgb(0x1E7D32);
            _warn = dark ? Rgb(0xE7B44B) : Rgb(0x9A6400);
            _error = dark ? Rgb(0xF07470) : Rgb(0xC62828);
            _muted = dark ? Rgb(0x9B9B9B) : Rgb(0x6B6B6B);
            _accent = dark ? Rgb(0x4C7EB8) : Rgb(0x3A72B0);
            _divider = dark ? Rgb(0x2A2A2A) : Rgb(0xB4B4B4);

            var cardBg = dark ? Rgb(0x424242) : Rgb(0xE2E2E2);
            var barBg = dark ? Rgb(0x303030) : Rgb(0xCCCCCC);
            var sidebarBg = dark ? Rgb(0x353535) : Rgb(0xD4D4D4);

            _pageTitle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 15,
                margin = new RectOffset(0, 0, 2, 0),
            };

            _sectionTitle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                margin = new RectOffset(0, 0, 6, 2),
            };

            _hint = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                richText = true,
            };
            _hint.normal.textColor = _muted;

            _mutedLabel = new GUIStyle(EditorStyles.miniLabel);
            _mutedLabel.normal.textColor = _muted;

            // padding thoáng: card sát chữ nhìn như hộp lỗi của Unity chứ không như một khối nội dung.
            _card = new GUIStyle
            {
                padding = new RectOffset(10, 10, 8, 8),
                margin = new RectOffset(0, 0, 2, 4),
            };
            _card.normal.background = Tex(cardBg);

            _bar = new GUIStyle
            {
                padding = new RectOffset(12, 12, 8, 8),
            };
            _bar.normal.background = Tex(barBg);

            _sidebar = new GUIStyle
            {
                padding = new RectOffset(8, 8, 10, 8),
            };
            _sidebar.normal.background = Tex(sidebarBg);

            // Mục nav: canh trái, cao 30 để chạm được thoải mái, nền chỉ hiện khi hover/chọn.
            _navItem = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fixedHeight = 30f,
                padding = new RectOffset(8, 6, 0, 0),
                margin = new RectOffset(0, 0, 1, 1),
                richText = true,
            };
            _navItem.hover.background = Tex(dark ? Rgb(0x3F3F3F) : Rgb(0xC6C6C6));
            _navItem.hover.textColor = _navItem.normal.textColor;
            _navItem.onNormal.background = Tex(_accent);
            _navItem.onNormal.textColor = Color.white;
            _navItem.onHover.background = _navItem.onNormal.background;
            _navItem.onHover.textColor = Color.white;
            _navItem.onActive.background = _navItem.onNormal.background;
            _navItem.onActive.textColor = Color.white;

            // Chữ trắng cố định: badge luôn nằm trên nền đặc màu trạng thái, không phải nền cửa sổ.
            _stepBadge = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fixedWidth = 18f,
                fixedHeight = 18f,
                margin = new RectOffset(0, 6, 2, 0),
            };
            _stepBadge.normal.textColor = Color.white;

            // Hàng tiêu đề gấp được: chiều cao CỐ ĐỊNH vì rect được xin bằng GUIContent.none — không có
            // chữ bên trong thì style phải tự quyết chiều cao, không thì hàng co lại còn vài pixel.
            _foldoutHeader = new GUIStyle
            {
                fixedHeight = 20f,
                margin = new RectOffset(0, 0, 2, 2),
            };

            // Kế thừa EditorStyles.foldout để giữ nguyên tam giác + phần thụt lề sau nó; chỉ tô đậm chữ
            // cho tiêu đề khối nổi hơn nội dung bên trong.
            _foldoutTitle = new GUIStyle(EditorStyles.foldout)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                fixedWidth = 0f,
                stretchWidth = true,
            };

            _foldoutTrailing = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                padding = new RectOffset(0, 0, 0, 0),
            };
            _foldoutTrailing.normal.textColor = _muted;

            _value = new GUIStyle(EditorStyles.label)
            {
                richText = true,
            };

            _emptyValue = new GUIStyle(EditorStyles.label)
            {
                fontStyle = FontStyle.Italic,
            };
            _emptyValue.normal.textColor = _muted;
        }

        private static Color Rgb(int hex) =>
            new((hex >> 16 & 0xFF) / 255f, (hex >> 8 & 0xFF) / 255f, (hex & 0xFF) / 255f, 1f);

        #endregion

        #region Textures

        private static readonly Dictionary<Color, Texture2D> _textures = new();

        /// <summary>
        ///     Texture 1x1 màu đặc, cache theo màu. <see cref="HideFlags.HideAndDontSave" /> để không bị
        ///     dọn giữa hai lượt vẽ và không rơi vào scene/asset nào.
        /// </summary>
        private static Texture2D Tex(Color color)
        {
            if (_textures.TryGetValue(color, out var cached) && cached != null) return cached;

            var texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            _textures[color] = texture;
            return texture;
        }

        /// <summary>
        ///     Huỷ hẳn texture cũ trước khi dựng bộ mới. <see cref="HideFlags.HideAndDontSave" /> nghĩa
        ///     là Unity KHÔNG tự dọn — chỉ <c>Clear()</c> cái dictionary thì mỗi lần user đổi skin lại
        ///     bỏ lại một bộ texture mồ côi nằm trong bộ nhớ tới lúc khởi động lại Editor.
        /// </summary>
        private static void ReleaseTextures()
        {
            foreach (var texture in _textures.Values)
                if (texture != null)
                    Object.DestroyImmediate(texture);

            _textures.Clear();
        }

        #endregion

        #region Status

        internal static Color ColorOf(EzgStatus status) =>
            status switch
            {
                EzgStatus.Ok => OkColor,
                EzgStatus.Warn => WarnColor,
                EzgStatus.Error => ErrorColor,
                _ => MutedColor,
            };

        internal static MessageType MessageTypeOf(EzgStatus status) =>
            status switch
            {
                EzgStatus.Warn => MessageType.Warning,
                EzgStatus.Error => MessageType.Error,
                _ => MessageType.Info,
            };

        /// <summary>
        ///     Icon built-in của Unity — có sẵn trên mọi máy, không phụ thuộc font hay emoji.
        ///     <para>
        ///         <see cref="EzgStatus.None" /> KHÔNG có icon: "không áp dụng" mà vẫn mọc một dấu "i" thì
        ///         một khối 20 dòng chỉ-đọc thành 20 dấu hiệu vô nghĩa, và mắt hết bắt được dòng thật sự
        ///         có vấn đề. Chỗ vẽ vẫn chừa đủ bề rộng để cột không lệch (<see cref="StatusIcon" />).
        ///     </para>
        /// </summary>
        internal static Texture IconOf(EzgStatus status) =>
            status switch
            {
                EzgStatus.Ok => Builtin("TestPassed"),
                EzgStatus.Warn => Builtin("console.warnicon.sml"),
                EzgStatus.Error => Builtin("console.erroricon.sml"),
                _ => null,
            };

        private static readonly Dictionary<string, Texture> _icons = new();

        private static readonly GUILayoutOption[] _iconOptions =
            { GUILayout.Width(ICON_WIDTH), GUILayout.ExpandWidth(false) };

        private static Texture Builtin(string name)
        {
            if (_icons.TryGetValue(name, out var cached) && cached != null) return cached;

            var icon = EditorGUIUtility.IconContent(name)?.image;
            _icons[name] = icon;
            return icon;
        }

        /// <summary>
        ///     Icon trạng thái một ô, canh giữa. Bề rộng CỐ ĐỊNH và luôn được chừa kể cả khi không vẽ
        ///     icon — dòng có icon và dòng không icon vẫn thẳng cột với nhau.
        /// </summary>
        internal static void StatusIcon(EzgStatus status, string tooltip = null)
        {
            // Option dựng sẵn: StatusIcon được gọi từ mọi KeyValue/CardHeader/CardFoldout và mọi ô nhập
            // của SetupGui — một tab mở hết foldout là hàng trăm lần mỗi lượt vẽ, mà mỗi lần viết
            // GUILayout.Width(...) tại chỗ là cấp phát thêm 2 object + 1 mảng params.
            var rect = GUILayoutUtility.GetRect(ICON_WIDTH, EditorGUIUtility.singleLineHeight,
                _iconOptions);

            var icon = IconOf(status);
            if (icon == null) return;

            var size = Mathf.Min(16f, rect.height);
            var drawRect = new Rect(rect.x + (rect.width - size) * 0.5f,
                rect.y + (rect.height - size) * 0.5f, size, size);

            GUI.DrawTexture(drawRect, icon, ScaleMode.ScaleToFit);
            if (!string.IsNullOrEmpty(tooltip)) GUI.Label(drawRect, new GUIContent(string.Empty, tooltip));
        }

        /// <summary>
        ///     Chip trạng thái có chữ. Dùng ở chỗ cần nói RÕ trạng thái ("3 ô lệch", "Đã xong") chứ
        ///     không phải chỗ chỉ cần đánh dấu — chip nhiều quá thì mất tác dụng nhấn mạnh.
        /// </summary>
        internal static void Pill(EzgStatus status, string text, string tooltip = null)
        {
            // ExpandWidth(false): chip phải ôm sát chữ. Style kế thừa miniLabel nên nếu để giãn thì
            // một chip đặt sau FlexibleSpace sẽ phình ra nửa hàng.
            GUILayout.Label(new GUIContent(text, tooltip), PillStyle(status),
                GUILayout.Height(18f), GUILayout.ExpandWidth(false));
        }

        private static readonly Dictionary<EzgStatus, GUIStyle> _pillStyles = new();

        private static GUIStyle PillStyle(EzgStatus status)
        {
            Build();
            if (_pillStyles.TryGetValue(status, out var cached)) return cached;

            var color = ColorOf(status);
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(8, 8, 0, 0),
                margin = new RectOffset(4, 4, 2, 2),
                stretchWidth = false,
            };

            // Nền = chính màu trạng thái pha rất nhạt: đủ để tách khỏi card, không chói.
            style.normal.background = Tex(Color.Lerp(color,
                _builtDark ? Rgb(0x2E2E2E) : Rgb(0xF0F0F0), 0.76f));
            style.normal.textColor = color;

            _pillStyles[status] = style;
            return style;
        }

        #endregion

        #region Layout scopes

        /// <summary>Khối nội dung có nền. Tuỳ chọn tiêu đề + icon trạng thái + chữ phụ bên phải.</summary>
        internal sealed class CardScope : GUI.Scope
        {
            internal CardScope() => EditorGUILayout.BeginVertical(CardStyle);

            internal CardScope(string title, EzgStatus status = EzgStatus.None, string trailing = null)
            {
                EditorGUILayout.BeginVertical(CardStyle);
                CardHeader(title, status, trailing);
            }

            protected override void CloseScope() => EditorGUILayout.EndVertical();
        }

        /// <summary>Thanh ngang đậm màu (header cửa sổ, footer cột nav).</summary>
        internal sealed class BarScope : GUI.Scope
        {
            internal BarScope(params GUILayoutOption[] options) =>
                EditorGUILayout.BeginVertical(BarStyle, options);

            protected override void CloseScope() => EditorGUILayout.EndVertical();
        }

        /// <summary>
        ///     Đặt <see cref="EditorGUIUtility.labelWidth" /> rồi TRẢ LẠI giá trị cũ. Đặt mà quên trả là
        ///     mọi thứ vẽ sau đó trong cùng cửa sổ bị lệch cột — lỗi rất khó lần ra.
        /// </summary>
        internal sealed class LabelWidthScope : GUI.Scope
        {
            private readonly float _previous;

            internal LabelWidthScope(float width = LABEL_WIDTH)
            {
                _previous = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = width;
            }

            protected override void CloseScope() => EditorGUIUtility.labelWidth = _previous;
        }

        #endregion

        #region Widgets

        /// <summary>Tiêu đề trang + một dòng nói trang này để làm gì.</summary>
        internal static void PageHeader(string title, string subtitle = null)
        {
            EditorGUILayout.LabelField(title, PageTitle);
            if (!string.IsNullOrEmpty(subtitle)) EditorGUILayout.LabelField(subtitle, Hint);
            EditorGUILayout.Space(4);
        }

        internal static void SectionHeader(string title, string subtitle = null)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(title, SectionTitle);
            if (!string.IsNullOrEmpty(subtitle)) EditorGUILayout.LabelField(subtitle, Hint);
        }

        /// <summary>
        ///     Tiêu đề của một card KHÔNG gấp được. Thứ tự phần tử cố ý khớp <see cref="FoldoutHeader" />
        ///     (tiêu đề trái — chữ đếm — icon sát mép phải) để card gấp được và card thường nhìn như
        ///     cùng một thứ, chỉ khác ở cái mũi tên.
        /// </summary>
        internal static void CardHeader(string title, EzgStatus status = EzgStatus.None,
            string trailing = null)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (!string.IsNullOrEmpty(trailing))
                    GUILayout.Label(trailing, MutedLabel, GUILayout.ExpandWidth(false));
                if (status != EzgStatus.None) StatusIcon(status);
            }

            Divider(2f);
        }

        /// <summary>
        ///     Dòng nhãn → giá trị chỉ-đọc. <see cref="EditorGUILayout.SelectableLabel" /> để copy được
        ///     (app id, project id hay phải dán sang chỗ khác) mà vẫn không sửa được.
        /// </summary>
        internal static void KeyValue(string label, string value, EzgStatus status = EzgStatus.None,
            string note = null, string emptyHint = null)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                StatusIcon(status);
                EditorGUILayout.LabelField(label, GUILayout.Width(LABEL_WIDTH));

                if (string.IsNullOrEmpty(value))
                    EditorGUILayout.LabelField("— " + (emptyHint ?? "chưa có"), EmptyValueStyle);
                else
                    EditorGUILayout.SelectableLabel(value, ValueStyle,
                        GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }

            if (string.IsNullOrEmpty(note)) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(ICON_WIDTH + 4f);
                EditorGUILayout.LabelField(note, Hint);
            }
        }

        /// <summary>
        ///     Dòng "giá trị hiện tại → giá trị sẽ ghi đè". Chỉ dùng khi hai bên KHÁC nhau: bày cả hai
        ///     lúc chúng giống nhau là thêm nhiễu chứ không thêm thông tin.
        /// </summary>
        internal static void DiffRow(string label, string current, string incoming)
        {
            KeyValue(label, current, EzgStatus.Warn);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(ICON_WIDTH + 4f);
                EditorGUILayout.LabelField("→ sẽ ghi", GUILayout.Width(LABEL_WIDTH - 4f));
                EditorGUILayout.SelectableLabel(string.IsNullOrEmpty(incoming) ? "— (rỗng)" : incoming,
                    ValueStyle, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
        }

        /// <summary>Đường kẻ ngang mảnh. Tách khối trong cùng một card mà không cần thêm card lồng.</summary>
        internal static void Divider(float space = 4f)
        {
            EditorGUILayout.Space(space);
            var rect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, DividerColor);
            EditorGUILayout.Space(space);
        }

        /// <summary>Nút hành động chính của một trang. Cao hơn nút thường để không bấm nhầm.</summary>
        internal static bool PrimaryButton(string label, params GUILayoutOption[] options)
        {
            var previous = GUI.backgroundColor;
            GUI.backgroundColor = Color.Lerp(previous, AccentColor, 0.55f);

            var clicked = GUILayout.Button(label, WithDefaultHeight(options));

            GUI.backgroundColor = previous;
            return clicked;
        }

        /// <summary>Nút phụ — cùng chiều cao nút chính để hàng nút thẳng, nhưng không tô màu.</summary>
        internal static bool SecondaryButton(string label, params GUILayoutOption[] options) =>
            GUILayout.Button(label, WithDefaultHeight(options));

        /// <summary>
        ///     Chèn <see cref="BUTTON_HEIGHT" /> vào ĐẦU danh sách option. Unity áp option theo thứ tự và
        ///     cái sau đè cái trước, nên caller chỉ truyền <c>Width</c> vẫn giữ được chiều cao chuẩn
        ///     (trước đây truyền option là mất luôn chiều cao, nút phụ thấp hơn nút chính cạnh nó), mà
        ///     caller cố tình truyền <c>Height</c> thì vẫn thắng.
        /// </summary>
        private static GUILayoutOption[] WithDefaultHeight(GUILayoutOption[] options)
        {
            if (options == null || options.Length == 0) return _defaultButtonOptions;

            var merged = new GUILayoutOption[options.Length + 1];
            merged[0] = _defaultButtonOptions[0];
            options.CopyTo(merged, 1);
            return merged;
        }

        private static readonly GUILayoutOption[] _defaultButtonOptions = { GUILayout.Height(BUTTON_HEIGHT) };

        /// <summary>Hàng nút mở trang web. Bỏ qua link rỗng — thiếu id thì nút đó không có ý nghĩa.</summary>
        internal static void Links(params (string Label, string Url)[] links)
        {
            if (links == null || links.Length == 0) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (var link in links)
                {
                    if (string.IsNullOrEmpty(link.Url)) continue;
                    if (GUILayout.Button(link.Label, EditorStyles.miniButton, GUILayout.MaxWidth(200f)))
                        Application.OpenURL(link.Url);
                }

                GUILayout.FlexibleSpace();
            }
        }

        internal static void Banner(string text, EzgStatus status) =>
            EditorGUILayout.HelpBox(text, MessageTypeOf(status));

        internal static void Bullets(IEnumerable<string> items, string emptyText = null)
        {
            var any = false;
            foreach (var item in items)
            {
                any = true;
                EditorGUILayout.LabelField("•  " + item, EditorStyles.wordWrappedLabel);
            }

            if (!any && !string.IsNullOrEmpty(emptyText))
                EditorGUILayout.LabelField(emptyText, Hint);
        }

        /// <summary>
        ///     Đoạn hướng dẫn dài, GẤP LẠI mặc định và nhớ trạng thái theo máy. Hướng dẫn cần cho lần
        ///     đầu, nhưng từ lần thứ hai nó chỉ đẩy phần việc thật xuống dưới màn hình.
        /// </summary>
        internal static void CollapsibleHelp(string prefsKey, string title, string body,
            bool defaultOpen = false)
        {
            // Cache trong RAM, khoá bằng prefsKey THÔ: OnGUI chạy mỗi lần chuột nhúc nhích, không đọc
            // EditorPrefs từng lượt vẽ và cũng không nối chuỗi tiền tố từng lượt vẽ — chuỗi đầy đủ chỉ
            // dựng lúc thật sự chạm EditorPrefs (lần đầu, và mỗi lần user bấm).
            if (!_helpOpen.TryGetValue(prefsKey, out var open))
                open = _helpOpen[prefsKey] = EditorPrefs.GetBool(HELP_PREFIX + prefsKey, defaultOpen);

            using (new CardScope())
            {
                var next = FoldoutHeader(open, title);
                if (next != open)
                {
                    _helpOpen[prefsKey] = next;
                    EditorPrefs.SetBool(HELP_PREFIX + prefsKey, next);
                }

                if (next) EditorGUILayout.LabelField(body, EditorStyles.wordWrappedLabel);
            }
        }

        private const string HELP_PREFIX = "Ezg.Kit.Help.";

        private static readonly Dictionary<string, bool> _helpOpen = new();

        /// <summary>
        ///     Foldout của một card, kèm icon trạng thái và chữ đếm bên phải. Trả về trạng thái mở mới.
        /// </summary>
        internal static bool CardFoldout(bool open, string title, EzgStatus status = EzgStatus.None,
            string trailing = null) => FoldoutHeader(open, title, status, trailing);

        /// <summary>
        ///     Hàng tiêu đề gấp/mở dùng chung cho MỌI khối gấp được của EzgKit.
        ///     <para>
        ///         <b>Cả hàng là vùng bấm được</b>, không riêng chữ tiêu đề. Bản trước ghép
        ///         <see cref="EditorGUILayout.Foldout" /> vào một hàng ngang cạnh
        ///         <c>GUILayout.FlexibleSpace()</c>; FlexibleSpace có stretch weight rất lớn nên nuốt hết
        ///         chỗ trống, foldout co lại đúng bề rộng chữ, còn icon trạng thái bên trái và chữ đếm
        ///         bên phải thành vùng chết. Người dùng bấm vào "22 dòng" hay vào icon thì không có gì
        ///         xảy ra, trong khi khối khác (foldout chiếm trọn bề ngang) lại bấm đâu cũng được —
        ///         cùng một thứ mà hai chỗ hành xử khác nhau.
        ///     </para>
        ///     <para>
        ///         Vì vậy ở đây tự xin MỘT rect trọn bề ngang rồi tự vẽ mũi tên + icon + tiêu đề + chữ
        ///         đếm, và tự bắt chuột trên toàn bộ rect đó. Con trỏ đổi thành <see cref="MouseCursor.Link" />
        ///         để nhìn là biết bấm được.
        ///     </para>
        /// </summary>
        internal static bool FoldoutHeader(bool open, string title, EzgStatus status = EzgStatus.None,
            string trailing = null)
        {
            var rect = GUILayoutUtility.GetRect(GUIContent.none, FoldoutHeaderStyle,
                GUILayout.ExpandWidth(true));

            // Bấm ở BẤT KỲ đâu trên hàng — kể cả icon và chữ đếm — đều gấp/mở.
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                open = !open;
                GUI.changed = true;
                e.Use();
            }

            if (e.type != EventType.Repaint) return open;

            // Phải sang trái: chữ đếm sát mép, rồi icon trạng thái. Icon nằm BÊN PHẢI (khác dòng
            // KeyValue) vì mũi tên + tiêu đề phải do MỘT lời gọi Draw của style foldout đảm nhiệm —
            // đó là cách Unity tự vẽ foldout, tam giác lấy đúng texture theo skin và chữ tự thụt vào
            // sau nó. Tách ra tự vẽ mũi tên vào một rect hẹp thì texture tam giác bị kéo giãn.
            // Icon ở SÁT mép phải, sau cả chữ đếm: mép phải là một đường thẳng cố định nên quét mắt
            // dọc theo nó là thấy ngay khối nào còn vấn đề, dù chữ đếm mỗi khối một độ dài.
            var right = rect.xMax;

            if (status != EzgStatus.None)
            {
                var icon = IconOf(status);
                right -= ICON_WIDTH;
                if (icon != null)
                {
                    var size = Mathf.Min(16f, rect.height);
                    GUI.DrawTexture(
                        new Rect(right + (ICON_WIDTH - size) * 0.5f,
                            rect.y + (rect.height - size) * 0.5f, size, size),
                        icon, ScaleMode.ScaleToFit);
                }
            }

            if (!string.IsNullOrEmpty(trailing))
            {
                var trailingWidth = FoldoutTrailingStyle.CalcSize(Temp(trailing)).x;
                right -= trailingWidth;
                FoldoutTrailingStyle.Draw(new Rect(right, rect.y, trailingWidth, rect.height),
                    Temp(trailing), false, false, false, false);
            }

            // `on: open` để style foldout chọn tam giác xuống (mở) hay sang phải (đóng).
            FoldoutTitleStyle.Draw(new Rect(rect.x, rect.y, Mathf.Max(0f, right - rect.x - 6f),
                rect.height), Temp(title), false, false, open, false);

            return open;
        }

        /// <summary>Chiều cao + khoảng đệm của hàng tiêu đề gấp được.</summary>
        private static GUIStyle FoldoutHeaderStyle => GetStyle(ref _foldoutHeader);

        private static GUIStyle FoldoutTitleStyle => GetStyle(ref _foldoutTitle);

        private static GUIStyle FoldoutTrailingStyle => GetStyle(ref _foldoutTrailing);

        private static GUIStyle _foldoutHeader, _foldoutTitle, _foldoutTrailing;

        /// <summary>
        ///     <see cref="GUIContent" /> dùng lại cho các lời gọi <c>Draw</c>/<c>CalcSize</c> thủ công —
        ///     hàng tiêu đề được vẽ rất nhiều lần mỗi lượt OnGUI, cấp phát mới mỗi lần là rác GC đều đặn.
        /// </summary>
        private static readonly GUIContent _temp = new();

        private static GUIContent Temp(string text)
        {
            _temp.text = text;
            return _temp;
        }

        /// <summary>
        ///     Ô vuông số thứ tự bước, tô theo trạng thái của bước đó. Nhãn số dựng sẵn cho vài bước đầu
        ///     — <c>step.ToString()</c> mỗi lượt OnGUI là rác GC đều đặn cho một con số không đổi.
        /// </summary>
        internal static void StepBadge(int step, EzgStatus status)
        {
            var rect = GUILayoutUtility.GetRect(18f, 18f, _badgeOptions);
            EditorGUI.DrawRect(rect, ColorOf(status));
            GUI.Label(rect, StepLabel(step), StepBadgeStyle);
        }

        private static readonly GUILayoutOption[] _badgeOptions =
            { GUILayout.Width(18f), GUILayout.ExpandWidth(false) };

        private static readonly string[] _stepLabels = { "1", "2", "3", "4", "5", "6", "7", "8", "9" };

        private static string StepLabel(int step) =>
            step >= 1 && step <= _stepLabels.Length ? _stepLabels[step - 1] : step.ToString();

        #endregion
    }
}
#endif
