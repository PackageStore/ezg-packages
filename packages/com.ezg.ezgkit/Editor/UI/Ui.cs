#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ezg.Editor.Shared.EzgKit
{
    /// <summary>
    ///     Bộ dựng UI dùng chung cho mọi trang — mọi trang ra cùng một kiểu card, hàng field, pill, nút. Màu chỉ
    ///     mang nghĩa trạng thái (xanh = xong, vàng = còn việc, đỏ = hỏng); kiểu dáng nằm hết trong
    ///     <c>EzgKit.uss</c>, file C# chỉ gắn class.
    /// </summary>
    internal static class Ui
    {
        #region Layout

        /// <summary>Card có tiêu đề. Trả về vùng nội dung để thêm field vào.</summary>
        internal static VisualElement Card(VisualElement parent, string title, string subtitle = null, string css = null)
        {
            var card = new VisualElement();
            card.AddToClassList("ezg-card");
            if (css != null)
                foreach (var c in css.Split(' '))
                    if (c.Length > 0)
                        card.AddToClassList(c);
            if (!string.IsNullOrEmpty(title))
            {
                var header = new VisualElement();
                header.AddToClassList("ezg-card-header");
                header.Add(Text(title, "ezg-card-title"));
                if (!string.IsNullOrEmpty(subtitle)) header.Add(Text(subtitle, "ezg-card-subtitle"));
                card.Add(header);
            }

            var content = new VisualElement();
            content.AddToClassList("ezg-card-content");
            card.Add(content);
            parent.Add(card);
            return content;
        }

        /// <summary>Phần gấp được (dùng cho mục ít dùng: ironSource, Unity Ads…).</summary>
        internal static VisualElement Fold(VisualElement parent, string title, bool open)
        {
            var fold = new Foldout { text = title, value = open };
            fold.AddToClassList("ezg-fold");
            parent.Add(fold);
            return fold.contentContainer;
        }

        internal static Label Text(string text, string css = null)
        {
            var label = new Label(text ?? string.Empty);
            label.AddToClassList("ezg-text");
            if (css != null)
                foreach (var c in css.Split(' '))
                    if (c.Length > 0)
                        label.AddToClassList(c);
            label.enableRichText = false;
            return label;
        }

        internal static Label Hint(VisualElement parent, string text)
        {
            var label = Text(text, "ezg-hint");
            parent.Add(label);
            return label;
        }

        internal static VisualElement Row(VisualElement parent, string css = null)
        {
            var row = new VisualElement();
            row.AddToClassList("ezg-hrow");
            if (css != null)
                foreach (var c in css.Split(' '))
                    if (c.Length > 0)
                        row.AddToClassList(c);
            parent?.Add(row);
            return row;
        }

        internal static VisualElement Spacer(VisualElement parent)
        {
            var spacer = new VisualElement();
            spacer.AddToClassList("ezg-spacer");
            parent.Add(spacer);
            return spacer;
        }

        #endregion

        #region Status visuals

        internal static Label Pill(SetupState state, string text = null)
        {
            var pill = Text(text ?? SetupStateText.Label(state), "ezg-pill " + SetupStateText.Css(state));
            return pill;
        }

        internal static Label Pill(EzgStatus status, string text)
        {
            var pill = Text(text, "ezg-pill " + SetupStateText.Css(status));
            return pill;
        }

        internal static VisualElement Dot(SetupState state)
        {
            var dot = new VisualElement();
            dot.AddToClassList("ezg-dot");
            dot.AddToClassList(SetupStateText.Css(state));
            return dot;
        }

        internal static VisualElement Dot(EzgStatus status)
        {
            var dot = new VisualElement();
            dot.AddToClassList("ezg-dot");
            dot.AddToClassList(SetupStateText.Css(status));
            return dot;
        }

        /// <summary>Khung thông báo màu theo mức (lỗi / cảnh báo / ok / thông tin).</summary>
        internal static Label Message(VisualElement parent, string text, EzgStatus level)
        {
            var label = Text(text, "ezg-msg " + SetupStateText.Css(level));
            parent.Add(label);
            return label;
        }

        /// <summary>Danh sách việc còn phải làm (todo của trang).</summary>
        internal static void Todos(VisualElement parent, IList<TodoItem> todos, string title = "Còn việc")
        {
            if (todos == null || todos.Count == 0) return;
            var content = Card(parent, title, null, "ezg-card-todos");
            foreach (var todo in todos)
            {
                var row = Row(content, "ezg-todo");
                row.Add(Dot(todo.Level));
                var col = new VisualElement();
                col.AddToClassList("ezg-grow");
                col.Add(Text(todo.Text, "ezg-todo-text"));
                if (!string.IsNullOrEmpty(todo.Fix)) col.Add(Text("→ " + todo.Fix, "ezg-todo-fix"));
                row.Add(col);
            }
        }

        #endregion

        #region Fields

        /// <summary>Một hàng: nhãn trái — control phải — (gợi ý / lỗi bên dưới).</summary>
        internal sealed class FieldRow
        {
            internal VisualElement Root;
            internal VisualElement Control;
            internal Label Validation;

            internal void SetValidation(string message, EzgStatus level = EzgStatus.Error)
            {
                Validation.text = message ?? string.Empty;
                Validation.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
                Validation.RemoveFromClassList("st-error");
                Validation.RemoveFromClassList("st-partial");
                Validation.RemoveFromClassList("st-done");
                Validation.AddToClassList(SetupStateText.Css(level));
            }
        }

        private static FieldRow MakeRow(VisualElement parent, string label, string hint, VisualElement control)
        {
            var root = new VisualElement();
            root.AddToClassList("ezg-field");
            var line = new VisualElement();
            line.AddToClassList("ezg-field-line");
            var labelElement = Text(label, "ezg-field-label");
            line.Add(labelElement);
            var right = new VisualElement();
            right.AddToClassList("ezg-field-control");
            right.Add(control);
            line.Add(right);
            root.Add(line);

            var below = new VisualElement();
            below.AddToClassList("ezg-field-below");
            var validation = Text(string.Empty, "ezg-validation");
            validation.style.display = DisplayStyle.None;
            below.Add(validation);
            if (!string.IsNullOrEmpty(hint)) below.Add(Text(hint, "ezg-hint"));
            root.Add(below);
            parent.Add(root);
            return new FieldRow { Root = root, Control = right, Validation = validation };
        }

        /// <summary>Ô text. <paramref name="validate" /> chạy mỗi lần gõ — trả null = hợp lệ.</summary>
        internal static TextField TextRow(VisualElement parent, string label, string value, string hint = null,
            Func<string, string> validate = null, bool secret = false, Action<string> onChange = null,
            string placeholder = null)
        {
            var field = new TextField { value = value ?? string.Empty, isPasswordField = secret };
            field.AddToClassList("ezg-input");
            var container = new VisualElement();
            container.AddToClassList("ezg-hrow");
            field.AddToClassList("ezg-grow");
            container.Add(field);
            if (secret)
            {
                var toggle = new Button { text = "Hiện" };
                toggle.AddToClassList("ezg-btn-mini");
                toggle.clicked += () =>
                {
                    field.isPasswordField = !field.isPasswordField;
                    toggle.text = field.isPasswordField ? "Hiện" : "Ẩn";
                };
                container.Add(toggle);
            }

            var row = MakeRow(parent, label, hint, container);
            SetPlaceholder(field, placeholder);
            void Check(string v) => row.SetValidation(validate?.Invoke(v));
            Check(field.value);
            field.RegisterValueChangedCallback(evt =>
            {
                Check(evt.newValue);
                onChange?.Invoke(evt.newValue);
            });
            field.userData = row;
            return field;
        }

        /// <summary>Ô text + nút "Chọn…" mở hộp chọn file / thư mục, trả về đường dẫn tương đối project nếu được.</summary>
        internal static TextField PathRow(VisualElement parent, string label, string value, bool folder, string hint = null,
            Func<string, string> validate = null, Action<string> onChange = null, string extension = "")
        {
            var field = new TextField { value = value ?? string.Empty };
            field.AddToClassList("ezg-input");
            field.AddToClassList("ezg-grow");
            var container = new VisualElement();
            container.AddToClassList("ezg-hrow");
            container.Add(field);
            var pick = new Button { text = "Chọn…" };
            pick.AddToClassList("ezg-btn-mini");
            pick.clicked += () =>
            {
                var start = string.IsNullOrEmpty(field.value) ? ProjectPaths.Root : ProjectPaths.Abs(field.value);
                var picked = folder
                    ? EditorUtility.OpenFolderPanel(label, start, string.Empty)
                    : EditorUtility.OpenFilePanel(label, System.IO.Path.GetDirectoryName(start) ?? ProjectPaths.Root, extension);
                if (string.IsNullOrEmpty(picked)) return;
                field.value = ProjectPaths.IsInsideProject(picked) ? ProjectPaths.Rel(picked) : picked;
            };
            container.Add(pick);
            var row = MakeRow(parent, label, hint, container);
            void Check(string v) => row.SetValidation(validate?.Invoke(v));
            Check(field.value);
            field.RegisterValueChangedCallback(evt =>
            {
                Check(evt.newValue);
                onChange?.Invoke(evt.newValue);
            });
            field.userData = row;
            return field;
        }

        internal static Toggle ToggleRow(VisualElement parent, string label, bool value, string hint = null,
            Action<bool> onChange = null)
        {
            var toggle = new Toggle { value = value };
            toggle.AddToClassList("ezg-toggle");
            MakeRow(parent, label, hint, toggle);
            if (onChange != null) toggle.RegisterValueChangedCallback(evt => onChange(evt.newValue));
            return toggle;
        }

        internal static DropdownField DropdownRow(VisualElement parent, string label, List<string> choices, int index,
            string hint = null, Action<int> onChange = null)
        {
            var dropdown = new DropdownField(choices, Mathf.Clamp(index, 0, Math.Max(0, choices.Count - 1)));
            dropdown.AddToClassList("ezg-input");
            MakeRow(parent, label, hint, dropdown);
            if (onChange != null) dropdown.RegisterValueChangedCallback(_ => onChange(dropdown.index));
            return dropdown;
        }

        /// <summary>Hàng chỉ đọc: nhãn — giá trị — chấm trạng thái.</summary>
        internal static Label InfoRow(VisualElement parent, string label, string value, EzgStatus status = EzgStatus.None,
            string note = null)
        {
            var root = new VisualElement();
            root.AddToClassList("ezg-field");
            var line = new VisualElement();
            line.AddToClassList("ezg-field-line");
            line.Add(Text(label, "ezg-field-label"));
            var right = new VisualElement();
            right.AddToClassList("ezg-field-control");
            right.AddToClassList("ezg-hrow");
            right.Add(Dot(status));
            var valueLabel = Text(string.IsNullOrEmpty(value) ? "—" : value, "ezg-value");
#if UNITY_2022_2_OR_NEWER
            valueLabel.selection.isSelectable = true;
#endif
            right.Add(valueLabel);
            line.Add(right);
            root.Add(line);
            if (!string.IsNullOrEmpty(note))
            {
                var below = new VisualElement();
                below.AddToClassList("ezg-field-below");
                below.Add(Text(note, "ezg-hint"));
                root.Add(below);
            }

            parent.Add(root);
            return valueLabel;
        }

        private static void SetPlaceholder(TextField field, string placeholder)
        {
            if (string.IsNullOrEmpty(placeholder)) return;
#if UNITY_2023_1_OR_NEWER
            field.textEdition.placeholder = placeholder;
            field.textEdition.hidePlaceholderOnFocus = true;
#else
            field.tooltip = placeholder;
#endif
        }

        #endregion

        #region Buttons

        internal static Button Button(VisualElement parent, string text, Action onClick, string kind = "secondary",
            string tooltip = null)
        {
            var button = new Button(() =>
            {
                try
                {
                    onClick?.Invoke();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }) { text = text, tooltip = tooltip ?? string.Empty };
            button.AddToClassList("ezg-btn");
            button.AddToClassList("ezg-btn-" + kind);
            parent?.Add(button);
            return button;
        }

        internal static Button Link(VisualElement parent, string text, string url) =>
            Button(parent, text + " ↗", () => Application.OpenURL(url), "link", url);

        #endregion

        #region Diff table

        /// <summary>Bảng thay đổi: chỉ ô lệch được tô; ô khớp gom lại một dòng đếm.</summary>
        internal static void DiffTable(VisualElement parent, ApplyResult result, bool showMatched = false)
        {
            var title = result.DryRun
                ? result.ChangedCount == 0 ? "Xem thay đổi — không có ô nào cần ghi" : $"Xem thay đổi — {result.ChangedCount} ô sẽ ghi"
                : result.ChangedCount == 0 ? "Đã áp dụng — không có gì thay đổi" : $"Đã áp dụng — {result.ChangedCount} ô đã ghi";
            var content = Card(parent, title, null, result.DryRun ? "ezg-card-diff" : "ezg-card-applied");

            if (!result.Ok && !string.IsNullOrEmpty(result.Error)) Message(content, result.Error, EzgStatus.Error);

            var header = Row(content, "ezg-diff-head");
            header.Add(Text("Nơi ghi", "ezg-diff-sink"));
            header.Add(Text("Ô", "ezg-diff-field"));
            header.Add(Text("Hiện tại", "ezg-diff-old"));
            header.Add(Text("Sẽ thành", "ezg-diff-new"));

            var matched = 0;
            foreach (var row in result.Rows)
            {
                if (row.Matched && !showMatched)
                {
                    matched++;
                    continue;
                }

                var line = Row(content, row.Matched ? "ezg-diff-row ezg-diff-same" : "ezg-diff-row");
                line.Add(Text(row.Sink, "ezg-diff-sink"));
                line.Add(Text(row.Field, "ezg-diff-field"));
                line.Add(Text(Show(row.OldValue, row.Secret), "ezg-diff-old"));
                line.Add(Text(Show(row.NewValue, row.Secret), "ezg-diff-new"));
            }

            if (matched > 0) Hint(content, $"{matched} ô khác đã khớp, không đổi.");
            foreach (var note in result.Notes) Hint(content, "• " + note);
        }

        private static string Show(string value, bool secret) =>
            string.IsNullOrEmpty(value) ? "(trống)" : secret ? Mask.Secret(value) : value;

        #endregion
    }
}
#endif
