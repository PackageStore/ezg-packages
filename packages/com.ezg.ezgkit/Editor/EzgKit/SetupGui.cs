#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Ezg.Editor.Shared.EzgKit
{
    /// <summary>
    ///     Tầng WIDGET NHẬP LIỆU của EzgKit, dựng trên ngôn ngữ hình ảnh của <see cref="EzgKitStyles" />
    ///     (<see cref="EzgKitStyles" /> lo màu/khối/trạng thái, file này lo mấy loại ô nhập).
    ///     <para>
    ///         Mục đích: mọi tab đều phân biệt được BA loại thông tin, vì đó là ba thứ người dùng phải
    ///         xử lý khác nhau hoàn toàn:
    ///     </para>
    ///     <list type="number">
    ///         <item>
    ///             <see cref="ManualField" /> — thứ tool KHÔNG tự biết, phải người điền. Luôn kèm hướng
    ///             dẫn "lấy ở đâu" + nút mở đúng trang, để không phải đi hỏi hay tự dò console.
    ///         </item>
    ///         <item>
    ///             <see cref="InfoRow" /> — thứ tool tự đọc được (PlayerSettings, file config, key).
    ///             Chỉ hiện để soi, KHÔNG cho sửa: sửa ở đây là lệch với nguồn thật.
    ///         </item>
    ///         <item>
    ///             <see cref="ManualStep" /> — thứ KHÔNG có API nên vĩnh viễn phải làm tay ngoài Unity.
    ///             Bày ra kèm link để "bấm 1 nút" không bị hiểu nhầm là xong 100%.
    ///         </item>
    ///     </list>
    ///     <para>
    ///         <b>Hướng dẫn gấp lại mặc định.</b> Đoạn "lấy ở đâu" dài 3–4 dòng mỗi ô: bày hết ra thì
    ///         phần việc thật bị đẩy xuống dưới màn hình và người dùng phải cuộn qua thứ họ đã đọc từ
    ///         lần đầu. Nút <i>Lấy ở đâu?</i> mở lại khi cần, và nhớ trạng thái theo máy.
    ///     </para>
    /// </summary>
    internal static class SetupGui
    {
        #region Constants

        /// <summary>Giữ tên cũ cho code gọi sẵn; nguồn thật là <see cref="EzgKitStyles.LABEL_WIDTH" />.</summary>
        internal const float LABEL_WIDTH = EzgKitStyles.LABEL_WIDTH;

        #endregion

        #region Manual input

        /// <summary>
        ///     Mức độ "phải điền" của một ô. Ba mức vì đó là ba thông điệp khác hẳn nhau với người dùng:
        ///     phải tự đi lấy, không cần đụng vào, hay bỏ qua cũng được.
        /// </summary>
        internal enum FieldNeed
        {
            /// <summary>Tool không có cách nào tự biết — người dùng phải đi lấy về. Trống = cảnh báo.</summary>
            Required,

            /// <summary>
            ///     Tool TỰ SUY RA được từ một nguồn khác trên cùng trang (ví dụ project id nằm sẵn trong
            ///     file service account key). Vẫn sửa được cho ca ngoại lệ, nhưng nhãn phải nói rõ là tự
            ///     lấy — không thì người dùng tưởng mình phải đi tra tay. Trống = cảnh báo, vì đáng lẽ
            ///     phải có.
            /// </summary>
            Derived,

            /// <summary>Bỏ trống vẫn chạy được. KHÔNG cảnh báo — cảnh báo ở đây là dạy người dùng
            /// bỏ qua cảnh báo.</summary>
            Optional,
        }

        /// <summary>
        ///     Ô người dùng tự điền. <paramref name="howTo" /> là đường đi cụ thể tới chỗ lấy giá trị
        ///     (không phải mô tả chung chung), <paramref name="links" /> mở thẳng trang đó.
        ///     <para>
        ///         <paramref name="need" /> phải truyền tường minh (nằm trước <c>params</c>) — xem
        ///         <see cref="FieldNeed" />.
        ///     </para>
        /// </summary>
        internal static string ManualField(string label, string value, string howTo, FieldNeed need,
            params (string Label, string Url)[] links)
        {
            using (new EzgKitStyles.CardScope())
            {
                string result;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EzgKitStyles.StatusIcon(IconState(need, value));
                    using (new EzgKitStyles.LabelWidthScope())
                    {
                        result = EditorGUILayout.TextField(Label(label, need), value);
                    }
                }

                EmptyNote(need, value);
                HowTo(label, howTo, links);
                return result;
            }
        }

        /// <summary>
        ///     Ô mật khẩu: giá trị KHÔNG được lưu ở đâu (xem <c>FirebaseSetupPage</c>), nên nói rõ ngay
        ///     trên GUI để không ai đi tìm chỗ đã lưu.
        /// </summary>
        internal static string ManualPasswordField(string label, string value, string howTo)
        {
            using (new EzgKitStyles.CardScope())
            {
                string result;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EzgKitStyles.StatusIcon(IconState(FieldNeed.Optional, value));
                    using (new EzgKitStyles.LabelWidthScope())
                    {
                        result = EditorGUILayout.PasswordField(Label(label, FieldNeed.Optional), value);
                    }
                }

                HowTo(label, howTo);
                return result;
            }
        }

        /// <summary>Ô điền tay có kèm nút chọn file.</summary>
        internal static string ManualFilePathField(string label, string value, string howTo, string title,
            string extension, FieldNeed need, params (string Label, string Url)[] links)
        {
            using (new EzgKitStyles.CardScope())
            {
                var result = value;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EzgKitStyles.StatusIcon(IconState(need, value));
                    using (new EzgKitStyles.LabelWidthScope())
                    {
                        result = EditorGUILayout.TextField(Label(label, need), result);
                    }

                    if (GUILayout.Button("Chọn...", EditorStyles.miniButton, GUILayout.Width(70)))
                    {
                        var picked = EditorUtility.OpenFilePanel(title, "~", extension);
                        if (!string.IsNullOrEmpty(picked)) result = picked;
                    }
                }

                EmptyNote(need, value);
                HowTo(label, howTo, links);
                return result;
            }
        }

        private static string Label(string label, FieldNeed need) =>
            need switch
            {
                FieldNeed.Derived => label + "  (tự lấy)",
                FieldNeed.Optional => label + "  (tuỳ chọn)",
                _ => label,
            };

        /// <summary>
        ///     Ô tuỳ chọn đang TRỐNG thì không có icon — trước đây nó ăn dấu tick xanh, tức báo "xong"
        ///     cho một ô chưa có gì trong đó.
        /// </summary>
        private static EzgStatus IconState(FieldNeed need, string value)
        {
            if (!string.IsNullOrEmpty(value)) return EzgStatus.Ok;
            return need == FieldNeed.Optional ? EzgStatus.None : EzgStatus.Warn;
        }

        private static void EmptyNote(FieldNeed need, string value)
        {
            if (!string.IsNullOrEmpty(value) || need == FieldNeed.Optional) return;

            var text = need == FieldNeed.Derived
                ? "Chưa tự lấy được — kiểm tra lại nguồn ở trên, hoặc điền tay."
                : "Bắt buộc — đang trống.";

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EzgKitStyles.ICON_WIDTH + 4f);
                var previous = GUI.contentColor;
                GUI.contentColor = EzgKitStyles.WarnColor;
                EditorGUILayout.LabelField(text, EditorStyles.miniLabel);
                GUI.contentColor = previous;
            }
        }

        #endregion

        #region How-to (gấp lại mặc định)

        /// <summary>Nhớ ô nào đang mở hướng dẫn, theo máy — người quen tool không phải gấp lại mỗi phiên.</summary>
        private static readonly Dictionary<string, bool> _howToOpen = new();

        private const string HOW_TO_PREFIX = "Ezg.Kit.HowTo.";

        private static void HowTo(string key, string howTo, params (string Label, string Url)[] links)
        {
            var hasHelp = !string.IsNullOrEmpty(howTo);
            var hasLinks = links is { Length: > 0 };
            if (!hasHelp && !hasLinks) return;

            // Khoá cache là `key` thô; chuỗi prefs đầy đủ chỉ dựng khi thật sự chạm EditorPrefs.
            if (!_howToOpen.TryGetValue(key, out var open))
                open = _howToOpen[key] = EditorPrefs.GetBool(HOW_TO_PREFIX + key, false);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EzgKitStyles.ICON_WIDTH + 4f);

                if (hasHelp)
                {
                    var toggled = GUILayout.Toggle(open, open ? "▾  Lấy ở đâu?" : "▸  Lấy ở đâu?",
                        EditorStyles.miniButton, GUILayout.Width(110));
                    if (toggled != open)
                    {
                        open = toggled;
                        _howToOpen[key] = open;
                        EditorPrefs.SetBool(HOW_TO_PREFIX + key, open);
                    }
                }

                // Link luôn hiện: mở đúng trang là việc làm thường xuyên, không phải việc đọc một lần.
                if (hasLinks)
                    foreach (var link in links)
                    {
                        if (string.IsNullOrEmpty(link.Url)) continue;
                        if (GUILayout.Button(link.Label, EditorStyles.miniButton, GUILayout.MaxWidth(180)))
                            Application.OpenURL(link.Url);
                    }

                GUILayout.FlexibleSpace();
            }

            if (!open || !hasHelp) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EzgKitStyles.ICON_WIDTH + 4f);
                EditorGUILayout.LabelField(howTo, EzgKitStyles.Hint);
            }
        }

        #endregion

        #region Read-only info

        /// <summary>Dòng chỉ-đọc. Chuyển tiếp sang <see cref="EzgKitStyles.KeyValue" />.</summary>
        internal static void InfoRow(string label, string value, EzgStatus status = EzgStatus.None,
            string note = null) => EzgKitStyles.KeyValue(label, value, status, note);

        #endregion

        #region Manual steps (no API)

        /// <summary>
        ///     Một việc KHÔNG tự động được. <paramref name="why" /> nói vì sao (thiếu API chứ không phải
        ///     thiếu quyền) để không ai đi cấp thêm quyền cho vô ích. Lời giải thích gấp lại mặc định —
        ///     thứ cần hằng ngày là cái nút mở trang, không phải đoạn văn.
        /// </summary>
        internal static void ManualStep(string title, string why,
            params (string Label, string Url)[] links)
        {
            using (new EzgKitStyles.CardScope())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    // Chừa đúng bề rộng icon (không vẽ icon: đây không phải trạng thái, mà là một việc)
                    // để tiêu đề thẳng cột với dòng hướng dẫn bên dưới.
                    EzgKitStyles.StatusIcon(EzgStatus.None);
                    EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                }

                HowTo(title, why, links);
            }
        }

        internal static void Links(params (string Label, string Url)[] links) =>
            EzgKitStyles.Links(links);

        internal static void Section(string title, string subtitle = null) =>
            EzgKitStyles.SectionHeader(title, subtitle);

        #endregion
    }
}
#endif
