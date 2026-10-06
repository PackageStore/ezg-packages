using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityFigmaBridge.Editor.Bridge
{
    public sealed class PartAssembler
    {
        const string PartPrefix = "{\"kind\":\"part\"";

        sealed class PartSet
        {
            public int Total;
            public readonly Dictionary<int, string> Parts = new Dictionary<int, string>();
            public long Chars;
            public DateTime At;
        }

        readonly int _maxChars;
        readonly TimeSpan _ttl;
        readonly Func<DateTime> _now;
        readonly Dictionary<string, PartSet> _sets = new Dictionary<string, PartSet>();
        readonly Dictionary<string, DateTime> _dropped = new Dictionary<string, DateTime>();

        public PartAssembler(int maxChars, TimeSpan ttl, Func<DateTime> now = null)
        {
            _maxChars = maxChars;
            _ttl = ttl;
            _now = now ?? (() => DateTime.UtcNow);
        }

        /// A non-part text returns as is. A part returns null until its set is complete, then the joined text.
        public string Accept(string text)
        {
            var t = _now();
            foreach (var id in _sets.Where(kv => t - kv.Value.At > _ttl).Select(kv => kv.Key).ToList()) _sets.Remove(id);
            foreach (var id in _dropped.Where(kv => t - kv.Value > _ttl).Select(kv => kv.Key).ToList()) _dropped.Remove(id);
            if (!text.StartsWith(PartPrefix, StringComparison.Ordinal)) return text;
            if (!TryParsePart(text, out var partId, out var seq, out var total, out var data)) return text;
            if (_dropped.ContainsKey(partId)) return null;
            if (!_sets.TryGetValue(partId, out var set)) _sets[partId] = set = new PartSet { Total = total, At = t };
            if (set.Total != total) return Drop(partId, t);
            set.Chars += data.Length - (set.Parts.TryGetValue(seq, out var old) ? old.Length : 0);
            set.Parts[seq] = data;
            if (set.Chars > _maxChars) return Drop(partId, t);
            if (set.Parts.Count < set.Total) return null;
            _sets.Remove(partId);
            var sb = new StringBuilder((int)set.Chars);
            for (var i = 0; i < set.Total; i++) sb.Append(set.Parts[i]);
            return sb.ToString();
        }

        string Drop(string id, DateTime t)
        {
            _sets.Remove(id);
            _dropped[id] = t;
            return null;
        }

        static bool TryParsePart(string text, out string id, out int seq, out int total, out string data)
        {
            id = data = null;
            seq = total = 0;
            JObject o;
            try
            {
                using (var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None })
                    o = JToken.ReadFrom(reader) as JObject;
            }
            catch (JsonException)
            {
                return false;
            }
            if (o == null || !TryString(o["kind"], out var kind) || kind != "part") return false;
            if (!TryString(o["id"], out id) || !TryString(o["data"], out data)) return false;
            if (!TryInt(o["seq"], out seq) || !TryInt(o["total"], out total)) return false;
            return total >= 1 && seq >= 0 && seq < total;
        }

        static bool TryString(JToken token, out string value)
        {
            value = token is JValue v && v.Type == JTokenType.String ? (string)v.Value : null;
            return value != null;
        }

        static bool TryInt(JToken token, out int value)
        {
            value = 0;
            if (!(token is JValue v) || (v.Type != JTokenType.Integer && v.Type != JTokenType.Float)) return false;
            var d = Convert.ToDouble(v.Value);
            if (d != Math.Floor(d) || d < int.MinValue || d > int.MaxValue) return false;
            value = (int)d;
            return true;
        }
    }
}
