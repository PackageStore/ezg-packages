using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Ezg.BugLogger
{
    /// <summary>
    /// Writer JSON tối giản, chỉ ghi tiến, escape chuỗi đầy đủ (ngoặc kép, backslash, mọi ký tự điều
    /// khiển, U+2028/U+2029, surrogate lẻ). Đủ cho report schema 1 — package không được phụ thuộc thư
    /// viện JSON, còn <c>JsonUtility</c> không ghi được map <c>extra</c>. Không đụng Unity API nên chạy
    /// được trên thread pool.
    /// </summary>
    internal sealed class BugJsonWriter
    {
        #region Fields

        private const int MAX_DEPTH = 8;
        private const string HEX = "0123456789abcdef";

        private readonly StringBuilder _builder;
        private readonly bool[] _hasMember = new bool[MAX_DEPTH];
        private int _depth;

        #endregion

        #region Public

        /// <summary>Tạo writer với dung lượng buffer ban đầu.</summary>
        public BugJsonWriter(int capacity)
        {
            _builder = new StringBuilder(capacity);
        }

        /// <summary>Mở object gốc.</summary>
        public BugJsonWriter BeginObject()
        {
            _builder.Append('{');
            Push();
            return this;
        }

        /// <summary>Mở một member kiểu object.</summary>
        public BugJsonWriter BeginObject(string name)
        {
            WriteName(name);
            _builder.Append('{');
            Push();
            return this;
        }

        /// <summary>Đóng object hiện tại.</summary>
        public BugJsonWriter EndObject()
        {
            _builder.Append('}');
            _depth--;
            return this;
        }

        /// <summary>Ghi member chuỗi; null ghi thành chuỗi rỗng.</summary>
        public BugJsonWriter Property(string name, string value)
        {
            WriteName(name);
            AppendString(_builder, value);
            return this;
        }

        /// <summary>Ghi member số nguyên.</summary>
        public BugJsonWriter Property(string name, long value)
        {
            WriteName(name);
            _builder.Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        /// <summary>Ghi member mảng chuỗi; null ghi thành mảng rỗng.</summary>
        public BugJsonWriter Property(string name, IReadOnlyList<string> values)
        {
            WriteName(name);
            _builder.Append('[');
            if (values != null)
            {
                for (var i = 0; i < values.Count; i++)
                {
                    if (i > 0)
                        _builder.Append(',');

                    AppendString(_builder, values[i]);
                }
            }

            _builder.Append(']');
            return this;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return _builder.ToString();
        }

        /// <summary>Ghi <paramref name="value"/> thành chuỗi JSON có ngoặc kép, đã escape.</summary>
        public static void AppendString(StringBuilder builder, string value)
        {
            builder.Append('"');
            if (value != null)
            {
                for (var i = 0; i < value.Length; i++)
                {
                    var c = value[i];
                    switch (c)
                    {
                        case '"':
                            builder.Append("\\\"");
                            break;
                        case '\\':
                            builder.Append("\\\\");
                            break;
                        case '\n':
                            builder.Append("\\n");
                            break;
                        case '\r':
                            builder.Append("\\r");
                            break;
                        case '\t':
                            builder.Append("\\t");
                            break;
                        case '\b':
                            builder.Append("\\b");
                            break;
                        case '\f':
                            builder.Append("\\f");
                            break;
                        default:
                            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                            {
                                builder.Append(c).Append(value[i + 1]);
                                i++;
                            }
                            else if (c < ' ' || c == '\u2028' || c == '\u2029' || char.IsSurrogate(c))
                            {
                                // Surrogate lẻ sang UTF-8 sẽ thành U+FFFD; escape để giữ nguyên.
                                AppendUnicodeEscape(builder, c);
                            }
                            else
                            {
                                builder.Append(c);
                            }

                            break;
                    }
                }
            }

            builder.Append('"');
        }

        #endregion

        #region Private

        private void Push()
        {
            _depth++;
            _hasMember[_depth] = false;
        }

        private void WriteName(string name)
        {
            if (_hasMember[_depth])
                _builder.Append(',');

            _hasMember[_depth] = true;
            AppendString(_builder, name);
            _builder.Append(':');
        }

        private static void AppendUnicodeEscape(StringBuilder builder, char c)
        {
            builder.Append("\\u")
                .Append(HEX[(c >> 12) & 0xF])
                .Append(HEX[(c >> 8) & 0xF])
                .Append(HEX[(c >> 4) & 0xF])
                .Append(HEX[c & 0xF]);
        }

        #endregion
    }
}
