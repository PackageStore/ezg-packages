using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ezg.UserSegment.Engine
{
    public sealed class HistoryEntry
    {
        [JsonProperty("n")] public int N;
        [JsonProperty("f")] public long F;
        [JsonProperty("l")] public long L;
    }

    /// <summary>
    ///     Tầng lịch sử action — §4.5, §C.10.3. Blob { v, uid, actions: { id: { n, f, l } } }. Merge theo quy tắc Seed:
    ///     n max, f min khác 0, l max; uid: bản có giữ, bản trống lấy của bên kia.
    /// </summary>
    public sealed class ActionHistory
    {
        public const int VERSION = 1;

        [JsonProperty("v")] public int V = VERSION;
        [JsonProperty("uid")] public string Uid;

        [JsonProperty("actions")]
        public Dictionary<string, HistoryEntry> Actions = new Dictionary<string, HistoryEntry>(StringComparer.Ordinal);

        /// <summary>null nếu blob rỗng hoặc không parse được.</summary>
        public static ActionHistory Parse(string blob)
        {
            if (string.IsNullOrWhiteSpace(blob)) return null;
            try
            {
                var o = JObject.Parse(blob);
                var h = new ActionHistory
                {
                    V = o["v"]?.Type == JTokenType.Integer ? o["v"].Value<int>() : VERSION,
                    Uid = o["uid"]?.Type == JTokenType.String ? o["uid"].Value<string>() : null
                };
                if (o["actions"] is JObject acts)
                    foreach (var p in acts.Properties())
                    {
                        if (!(p.Value is JObject e)) return null;
                        h.Actions[p.Name] = new HistoryEntry
                        {
                            N = e["n"]?.Value<int>() ?? 0,
                            F = e["f"]?.Value<long>() ?? 0,
                            L = e["l"]?.Value<long>() ?? 0
                        };
                    }

                return h;
            }
            catch
            {
                return null;
            }
        }

        public string ToJson()
        {
            var o = new JObject { ["v"] = V };
            if (!string.IsNullOrEmpty(Uid)) o["uid"] = Uid;
            var acts = new JObject();
            var keys = new List<string>(Actions.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys)
            {
                var e = Actions[k];
                acts[k] = new JObject { ["n"] = e.N, ["f"] = e.F, ["l"] = e.L };
            }

            o["actions"] = acts;
            return o.ToString(Formatting.None);
        }

        public static ActionHistory Merge(ActionHistory a, ActionHistory b)
        {
            if (a == null) return b == null ? new ActionHistory() : b.Clone();
            if (b == null) return a.Clone();
            var r = a.Clone();
            if (string.IsNullOrEmpty(r.Uid)) r.Uid = b.Uid;
            foreach (var kv in b.Actions)
            {
                if (!r.Actions.TryGetValue(kv.Key, out var e))
                {
                    r.Actions[kv.Key] = new HistoryEntry { N = kv.Value.N, F = kv.Value.F, L = kv.Value.L };
                    continue;
                }

                e.N = Math.Max(e.N, kv.Value.N);
                e.F = MinNonZero(e.F, kv.Value.F);
                e.L = Math.Max(e.L, kv.Value.L);
            }

            return r;
        }

        public ActionHistory Clone()
        {
            var c = new ActionHistory { V = V, Uid = Uid };
            foreach (var kv in Actions) c.Actions[kv.Key] = new HistoryEntry { N = kv.Value.N, F = kv.Value.F, L = kv.Value.L };
            return c;
        }

        public int Count(string actionId) => Actions.TryGetValue(actionId, out var e) ? e.N : 0;
        public long LastAt(string actionId) => Actions.TryGetValue(actionId, out var e) ? e.L : 0;

        /// <summary>Ghi lúc selected: n += 1, f nếu chưa có, l = at.</summary>
        public void Record(string actionId, long at)
        {
            if (!Actions.TryGetValue(actionId, out var e))
            {
                Actions[actionId] = new HistoryEntry { N = 1, F = at, L = at };
                return;
            }

            e.N++;
            if (e.F == 0) e.F = at;
            e.L = Math.Max(e.L, at);
        }

        /// <summary>Hoàn lại khi action_failed offline / no_permission: n −= 1, n == 0 → xoá entry.</summary>
        public void Refund(string actionId)
        {
            if (!Actions.TryGetValue(actionId, out var e)) return;
            e.N--;
            if (e.N <= 0) Actions.Remove(actionId);
        }

        /// <summary>MarkActionUsed: n = max(n, 1); f = l = at nếu chưa có — §4.5.</summary>
        public void MarkUsed(string actionId, long at)
        {
            if (!Actions.TryGetValue(actionId, out var e))
            {
                Actions[actionId] = new HistoryEntry { N = 1, F = at, L = at };
                return;
            }

            e.N = Math.Max(e.N, 1);
            if (e.F == 0) e.F = at;
            if (e.L == 0) e.L = at;
        }

        public void RemoveEntry(string actionId) => Actions.Remove(actionId);

        private static long MinNonZero(long a, long b)
        {
            if (a == 0) return b;
            if (b == 0) return a;
            return Math.Min(a, b);
        }
    }
}
