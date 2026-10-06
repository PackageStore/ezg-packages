#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Ezg.Editor.Shared.EzgKit
{
    /// <summary>
    ///     Object JSON giữ THỨ TỰ key. Cần vì <see cref="UnityEngine.JsonUtility" /> không làm được
    ///     dictionary (state file có <c>pages.&lt;id&gt;</c>, API trả về danh sách trang có key động), và
    ///     ghi lại file mà đảo thứ tự key là git diff rác mỗi lần lưu.
    /// </summary>
    internal sealed class JsonObject : IEnumerable<KeyValuePair<string, object>>
    {
        private readonly List<KeyValuePair<string, object>> _items = new();

        internal int Count => _items.Count;

        internal object this[string key]
        {
            get => TryGet(key, out var value) ? value : null;
            set => Set(key, value);
        }

        internal bool Has(string key) => IndexOf(key) >= 0;

        internal bool TryGet(string key, out object value)
        {
            var index = IndexOf(key);
            value = index >= 0 ? _items[index].Value : null;
            return index >= 0;
        }

        internal JsonObject Set(string key, object value)
        {
            var index = IndexOf(key);
            if (index >= 0) _items[index] = new KeyValuePair<string, object>(key, value);
            else _items.Add(new KeyValuePair<string, object>(key, value));
            return this;
        }

        internal bool Remove(string key)
        {
            var index = IndexOf(key);
            if (index < 0) return false;
            _items.RemoveAt(index);
            return true;
        }

        internal string Str(string key, string fallback = "")
        {
            if (!TryGet(key, out var value) || value == null) return fallback;
            return value is string s ? s : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        internal bool Bool(string key, bool fallback = false)
        {
            if (!TryGet(key, out var value) || value == null) return fallback;
            return value switch
            {
                bool b => b,
                string s => s == "true" || s == "1",
                double d => Math.Abs(d) > double.Epsilon,
                // Giá trị set thẳng trong C# (Set(key, 1)) là int/long/float boxed, không phải double như khi parse.
                int i => i != 0,
                long l => l != 0,
                float f => Math.Abs(f) > float.Epsilon,
                _ => fallback,
            };
        }

        internal int Int(string key, int fallback = 0)
        {
            if (!TryGet(key, out var value) || value == null) return fallback;
            return value switch
            {
                double d => (int)d,
                // Giá trị set thẳng trong C# là int/long/float boxed — trước đây rơi xuống fallback (vd format ads luôn = 0).
                int n => n,
                long l => (int)l,
                float f => (int)f,
                string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
                bool b => b ? 1 : 0,
                _ => fallback,
            };
        }

        internal JsonObject Obj(string key) => TryGet(key, out var value) ? value as JsonObject : null;

        /// <summary>Object con theo <paramref name="key" />; chưa có thì tạo mới và gắn vào.</summary>
        internal JsonObject ObjOrNew(string key)
        {
            var existing = Obj(key);
            if (existing != null) return existing;
            var created = new JsonObject();
            Set(key, created);
            return created;
        }

        internal List<object> List(string key) => TryGet(key, out var value) ? value as List<object> : null;

        private int IndexOf(string key)
        {
            for (var i = 0; i < _items.Count; i++)
                if (string.Equals(_items[i].Key, key, StringComparison.Ordinal))
                    return i;
            return -1;
        }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    ///     JSON tối giản cho kit: parse ra <see cref="JsonObject" /> / <see cref="List{T}" /> / string /
    ///     double / bool / null và ghi ngược lại. Không phụ thuộc thư viện ngoài — package dùng chung cho
    ///     mọi dự án, không được kéo Newtonsoft theo.
    /// </summary>
    internal static class MiniJson
    {
        #region Parse

        /// <summary>Parse; JSON hỏng thì trả null và <paramref name="error" /> nói vì sao.</summary>
        internal static object Parse(string text, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(text))
            {
                error = "JSON rỗng.";
                return null;
            }

            try
            {
                var parser = new Parser(text);
                var value = parser.ParseValue();
                parser.SkipWhitespace();
                if (!parser.AtEnd) throw new FormatException($"Thừa ký tự ở vị trí {parser.Position}.");
                return value;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return null;
            }
        }

        internal static JsonObject ParseObject(string text) => Parse(text, out _) as JsonObject;

        /// <summary>Parser kèm vị trí — <see cref="JsonText" /> dùng lại để sửa đúng chỗ trong file.</summary>
        internal sealed class Parser
        {
            private readonly string _text;
            private int _pos;

            internal Parser(string text) => _text = text;

            internal int Position => _pos;

            internal bool AtEnd => _pos >= _text.Length;

            /// <summary>Gọi lại mỗi khi parse xong một member của object: (đường dẫn key, start, end của value).</summary>
            internal Action<List<string>, int, int> OnMember;

            /// <summary>Gọi khi gặp dấu đóng <c>}</c> của một object: (đường dẫn, vị trí dấu <c>{</c>, vị trí dấu <c>}</c>).</summary>
            internal Action<List<string>, int, int> OnObject;

            private readonly List<string> _path = new();

            internal object ParseValue()
            {
                SkipWhitespace();
                if (AtEnd) throw new FormatException("JSON kết thúc đột ngột.");

                var c = _text[_pos];
                switch (c)
                {
                    case '{': return ParseObjectValue();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default: return ParseNumber();
                }
            }

            private JsonObject ParseObjectValue()
            {
                var open = _pos;
                var result = new JsonObject();
                _pos++; // {
                SkipWhitespace();
                if (Peek('}'))
                {
                    _pos++;
                    OnObject?.Invoke(_path, open, _pos - 1);
                    return result;
                }

                while (true)
                {
                    SkipWhitespace();
                    var key = ParseString();
                    SkipWhitespace();
                    if (!Peek(':')) throw new FormatException($"Thiếu ':' sau key \"{key}\" (vị trí {_pos}).");
                    _pos++;
                    SkipWhitespace();
                    var start = _pos;
                    _path.Add(key);
                    var value = ParseValue();
                    OnMember?.Invoke(_path, start, _pos);
                    _path.RemoveAt(_path.Count - 1);
                    result.Set(key, value);
                    SkipWhitespace();
                    if (Peek(','))
                    {
                        _pos++;
                        continue;
                    }

                    if (Peek('}'))
                    {
                        _pos++;
                        OnObject?.Invoke(_path, open, _pos - 1);
                        return result;
                    }

                    throw new FormatException($"Thiếu ',' hoặc '}}' ở vị trí {_pos}.");
                }
            }

            private List<object> ParseArray()
            {
                var result = new List<object>();
                _pos++; // [
                SkipWhitespace();
                if (Peek(']'))
                {
                    _pos++;
                    return result;
                }

                while (true)
                {
                    _path.Add("[]");
                    result.Add(ParseValue());
                    _path.RemoveAt(_path.Count - 1);
                    SkipWhitespace();
                    if (Peek(','))
                    {
                        _pos++;
                        continue;
                    }

                    if (Peek(']'))
                    {
                        _pos++;
                        return result;
                    }

                    throw new FormatException($"Thiếu ',' hoặc ']' ở vị trí {_pos}.");
                }
            }

            private string ParseString()
            {
                if (!Peek('"')) throw new FormatException($"Cần chuỗi ở vị trí {_pos}.");
                _pos++;
                var sb = new StringBuilder();
                while (!AtEnd)
                {
                    var c = _text[_pos++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }

                    if (AtEnd) break;
                    var e = _text[_pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_pos + 4 > _text.Length) throw new FormatException("Escape \\u thiếu ký tự.");
                            sb.Append((char)int.Parse(_text.Substring(_pos, 4), NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture));
                            _pos += 4;
                            break;
                        default: sb.Append(e); break;
                    }
                }

                throw new FormatException("Chuỗi không đóng ngoặc kép.");
            }

            private double ParseNumber()
            {
                var start = _pos;
                while (!AtEnd && "+-0123456789.eE".IndexOf(_text[_pos]) >= 0) _pos++;
                if (start == _pos) throw new FormatException($"Ký tự lạ '{_text[_pos]}' ở vị trí {_pos}.");
                return double.Parse(_text.Substring(start, _pos - start), NumberStyles.Float,
                    CultureInfo.InvariantCulture);
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(_text, _pos, word, 0, word.Length) != 0)
                    throw new FormatException($"Cần '{word}' ở vị trí {_pos}.");
                _pos += word.Length;
            }

            private bool Peek(char c) => !AtEnd && _text[_pos] == c;

            internal void SkipWhitespace()
            {
                while (!AtEnd && char.IsWhiteSpace(_text[_pos])) _pos++;
            }
        }

        #endregion

        #region Serialize

        internal static string Serialize(object value, bool pretty = true)
        {
            var sb = new StringBuilder();
            Write(sb, value, pretty, 0);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object value, bool pretty, int depth)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    return;
                case string s:
                    WriteString(sb, s);
                    return;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    return;
                case JsonObject obj:
                    WriteObject(sb, obj, pretty, depth);
                    return;
                case IDictionary<string, object> dict:
                {
                    var converted = new JsonObject();
                    foreach (var pair in dict) converted.Set(pair.Key, pair.Value);
                    WriteObject(sb, converted, pretty, depth);
                    return;
                }
                case IEnumerable enumerable:
                    WriteArray(sb, enumerable, pretty, depth);
                    return;
                case float f:
                    sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                    return;
                case double d:
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    return;
                case int or long or short or byte or uint or ulong:
                    sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
                case Enum e:
                    WriteString(sb, e.ToString());
                    return;
                default:
                    WriteString(sb, Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
            }
        }

        private static void WriteObject(StringBuilder sb, JsonObject obj, bool pretty, int depth)
        {
            if (obj.Count == 0)
            {
                sb.Append("{}");
                return;
            }

            sb.Append('{');
            var first = true;
            foreach (var pair in obj)
            {
                if (!first) sb.Append(',');
                first = false;
                NewLine(sb, pretty, depth + 1);
                WriteString(sb, pair.Key);
                sb.Append(pretty ? ": " : ":");
                Write(sb, pair.Value, pretty, depth + 1);
            }

            NewLine(sb, pretty, depth);
            sb.Append('}');
        }

        private static void WriteArray(StringBuilder sb, IEnumerable items, bool pretty, int depth)
        {
            var any = false;
            sb.Append('[');
            foreach (var item in items)
            {
                if (any) sb.Append(',');
                any = true;
                NewLine(sb, pretty, depth + 1);
                Write(sb, item, pretty, depth + 1);
            }

            if (any) NewLine(sb, pretty, depth);
            sb.Append(']');
        }

        private static void NewLine(StringBuilder sb, bool pretty, int depth)
        {
            if (!pretty) return;
            sb.Append('\n');
            sb.Append(' ', depth * 2);
        }

        internal static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }

            sb.Append('"');
        }

        internal static string Quote(string s)
        {
            var sb = new StringBuilder();
            WriteString(sb, s ?? string.Empty);
            return sb.ToString();
        }

        #endregion
    }

    /// <summary>
    ///     Sửa MỘT giá trị string trong file JSON mà giữ nguyên mọi byte còn lại (thứ tự key, dòng trống,
    ///     các key <c>$comment</c>). Dùng cho <c>.claude/project-profile.json</c>: file đó được người viết
    ///     tay có format riêng, ghi lại cả file bằng serializer là git diff đổi cả trăm dòng chỉ vì một ô.
    /// </summary>
    internal static class JsonText
    {
        /// <summary>
        ///     Đặt <paramref name="path" /> (ví dụ <c>["localize", "sheet"]</c>) = <paramref name="value" />.
        ///     Key chưa có thì chèn vào cuối object cha (object cha chưa có thì tạo). Trả về false nếu file
        ///     không parse được hoặc chỗ đó không phải object/string.
        /// </summary>
        internal static bool SetString(ref string text, string[] path, string value, out string error)
        {
            error = null;
            var spans = new Dictionary<string, (int Start, int End)>();
            var objects = new Dictionary<string, (int Open, int Close)>();
            var parser = new MiniJson.Parser(text)
            {
                OnMember = (p, s, e) => spans[string.Join("\u0001", p)] = (s, e),
                OnObject = (p, o, c) => objects[string.Join("\u0001", p)] = (o, c),
            };

            try
            {
                parser.ParseValue();
            }
            catch (Exception exception)
            {
                error = "JSON hỏng: " + exception.Message;
                return false;
            }

            var key = string.Join("\u0001", path);
            if (spans.TryGetValue(key, out var span))
            {
                var current = text.Substring(span.Start, span.End - span.Start).Trim();
                if (!current.StartsWith("\"", StringComparison.Ordinal) && current != "null")
                {
                    error = $"{string.Join(".", path)} không phải chuỗi.";
                    return false;
                }

                text = text.Remove(span.Start, span.End - span.Start).Insert(span.Start, MiniJson.Quote(value));
                return true;
            }

            // Tìm object cha sâu nhất đang có rồi chèn phần còn thiếu vào đó.
            for (var depth = path.Length - 1; depth >= 0; depth--)
            {
                var parentKey = string.Join("\u0001", path, 0, depth);
                if (!objects.TryGetValue(parentKey, out var parent)) continue;

                var insertion = BuildInsertion(path, depth, value, text, parent.Open);
                var before = text.Substring(parent.Open + 1, parent.Close - parent.Open - 1);
                var empty = before.Trim().Length == 0;
                var indent = IndentOf(text, parent.Open);
                var childIndent = indent + "  ";
                var piece = (empty ? "\n" : ",\n") + childIndent + insertion + "\n" + indent;

                // Cắt khoảng trắng cuối trước dấu '}' để không chồng dòng trống.
                var closeAt = parent.Close;
                var trimFrom = closeAt;
                while (trimFrom > parent.Open + 1 && char.IsWhiteSpace(text[trimFrom - 1])) trimFrom--;
                text = text.Substring(0, trimFrom) + piece + text.Substring(closeAt);
                return true;
            }

            error = "Không tìm thấy object gốc trong file.";
            return false;
        }

        private static string BuildInsertion(string[] path, int depth, string value, string text, int open)
        {
            // path[depth..] còn thiếu: key cuối là string, các key giữa là object lồng nhau (một dòng).
            var sb = new StringBuilder();
            sb.Append(MiniJson.Quote(path[depth])).Append(": ");
            var closes = 0;
            for (var i = depth + 1; i < path.Length; i++)
            {
                sb.Append("{ ").Append(MiniJson.Quote(path[i])).Append(": ");
                closes++;
            }

            sb.Append(MiniJson.Quote(value));
            for (var i = 0; i < closes; i++) sb.Append(" }");
            return sb.ToString();
        }

        /// <summary>Indent của dòng chứa vị trí <paramref name="index" />.</summary>
        private static string IndentOf(string text, int index)
        {
            var lineStart = text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
            var end = lineStart;
            while (end < text.Length && (text[end] == ' ' || text[end] == '\t')) end++;
            return text.Substring(lineStart, end - lineStart);
        }
    }
}
#endif
