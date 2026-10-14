using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Ezg.Package.CheatGate
{
    /// <summary>
    ///     Reader for the only JSON this package sees: one flat object (the check response, the grant payload) whose values
    ///     are strings, integers, true / false or null. Anything else (nested objects, arrays, fractions, trailing data) is
    ///     refused. Never throws and never loops on malformed input: the response comes from the network.
    ///     Plain C# (no UnityEngine) so the grant logic is testable outside Unity.
    /// </summary>
    public static class CheatGateJson
    {
        private const int MaxLength = 16 * 1024;

        /// <summary>Values: string, long, bool or null. Null when <paramref name="json" /> is not such an object.</summary>
        public static Dictionary<string, object> ParseFlatObject(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > MaxLength) return null;
            var reader = new Reader(json);
            return reader.ReadDocument();
        }

        public static bool TryGetString(Dictionary<string, object> obj, string key, out string value)
        {
            value = null;
            if (obj == null || !obj.TryGetValue(key, out var raw) || !(raw is string text)) return false;
            value = text;
            return true;
        }

        public static bool TryGetLong(Dictionary<string, object> obj, string key, out long value)
        {
            value = 0;
            if (obj == null || !obj.TryGetValue(key, out var raw) || !(raw is long number)) return false;
            value = number;
            return true;
        }

        public static bool TryGetBool(Dictionary<string, object> obj, string key, out bool value)
        {
            value = false;
            if (obj == null || !obj.TryGetValue(key, out var raw) || !(raw is bool flag)) return false;
            value = flag;
            return true;
        }

        private sealed class Reader
        {
            private readonly string _s;
            private int _i;

            public Reader(string s)
            {
                _s = s;
            }

            public Dictionary<string, object> ReadDocument()
            {
                SkipSpace();
                if (!Take('{')) return null;
                var result = new Dictionary<string, object>();
                SkipSpace();
                if (Take('}')) return End() ? result : null;
                while (true)
                {
                    SkipSpace();
                    if (!ReadString(out var key)) return null;
                    SkipSpace();
                    if (!Take(':')) return null;
                    SkipSpace();
                    if (!ReadValue(out var value)) return null;
                    result[key] = value;
                    SkipSpace();
                    if (Take(',')) continue;
                    if (Take('}')) return End() ? result : null;
                    return null;
                }
            }

            private bool End()
            {
                SkipSpace();
                return _i == _s.Length;
            }

            private void SkipSpace()
            {
                while (_i < _s.Length && (_s[_i] == ' ' || _s[_i] == '\t' || _s[_i] == '\n' || _s[_i] == '\r')) _i++;
            }

            private bool Take(char c)
            {
                if (_i >= _s.Length || _s[_i] != c) return false;
                _i++;
                return true;
            }

            private bool TakeWord(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) return false;
                _i += word.Length;
                return true;
            }

            private bool ReadValue(out object value)
            {
                value = null;
                if (_i >= _s.Length) return false;
                var c = _s[_i];
                if (c == '"')
                {
                    if (!ReadString(out var text)) return false;
                    value = text;
                    return true;
                }

                if (c == '-' || (c >= '0' && c <= '9'))
                {
                    if (!ReadInteger(out var number)) return false;
                    value = number;
                    return true;
                }

                if (TakeWord("true"))
                {
                    value = true;
                    return true;
                }

                if (TakeWord("false"))
                {
                    value = false;
                    return true;
                }

                return TakeWord("null");
            }

            private bool ReadInteger(out long value)
            {
                value = 0;
                var start = _i;
                if (_s[_i] == '-') _i++;
                var digitsStart = _i;
                while (_i < _s.Length && _s[_i] >= '0' && _s[_i] <= '9') _i++;
                var digits = _i - digitsStart;
                if (digits == 0 || digits > 18) return false;
                if (digits > 1 && _s[digitsStart] == '0') return false;
                // Fractions and exponents are not part of this format.
                if (_i < _s.Length && (_s[_i] == '.' || _s[_i] == 'e' || _s[_i] == 'E')) return false;
                return long.TryParse(_s.Substring(start, _i - start), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
            }

            private bool ReadString(out string value)
            {
                value = null;
                if (!Take('"')) return false;
                var sb = new StringBuilder();
                while (_i < _s.Length)
                {
                    var c = _s[_i++];
                    if (c == '"')
                    {
                        value = sb.ToString();
                        return true;
                    }

                    if (c < ' ') return false;
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }

                    if (_i >= _s.Length) return false;
                    var e = _s[_i++];
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
                            if (_i + 4 > _s.Length) return false;
                            if (!int.TryParse(_s.Substring(_i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code)) return false;
                            sb.Append((char)code);
                            _i += 4;
                            break;
                        default:
                            return false;
                    }
                }

                return false;
            }
        }
    }
}
