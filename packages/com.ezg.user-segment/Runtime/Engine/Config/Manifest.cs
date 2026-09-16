using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Ezg.UserSegment.Engine
{
    /// <summary>Capability manifest của build — §8.5. Chỉ dùng local; SDK export JSON deterministic — §C.6.7.</summary>
    public sealed class Manifest
    {
        public string Sdk = string.Empty;
        public readonly SortedSet<string> Actions = new SortedSet<string>(StringComparer.Ordinal);
        public readonly SortedSet<string> Rewards = new SortedSet<string>(StringComparer.Ordinal);
        public readonly SortedSet<string> Screens = new SortedSet<string>(StringComparer.Ordinal);
        public readonly SortedSet<string> CustomEvents = new SortedSet<string>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, CustomType> CustomState =
            new SortedDictionary<string, CustomType>(StringComparer.Ordinal);

        public static Manifest FromOptions(SdkOptions o, IEnumerable<ActionType> registeredActions, string sdkVersion)
        {
            var m = new Manifest { Sdk = sdkVersion };
            if (registeredActions != null)
                foreach (var a in registeredActions)
                    m.Actions.Add(a.ToString());
            if (o.Rewards != null) foreach (var r in o.Rewards) m.Rewards.Add(r);
            if (o.Screens != null) foreach (var s in o.Screens) m.Screens.Add(s);
            if (o.CustomEvents != null) foreach (var c in o.CustomEvents) m.CustomEvents.Add(c);
            if (o.CustomState != null) foreach (var kv in o.CustomState) m.CustomState[kv.Key] = kv.Value;
            return m;
        }

        public SegType CustomStateType(string key)
        {
            switch (CustomState[key])
            {
                case CustomType.Bool: return SegType.Bool;
                case CustomType.String: return SegType.String;
                default: return SegType.Number;
            }
        }

        public string ToJson()
        {
            var o = new JObject
            {
                ["sdk"] = Sdk,
                ["actions"] = new JArray(Actions.ToArray()),
                ["rewards"] = new JArray(Rewards.ToArray()),
                ["screens"] = new JArray(Screens.ToArray()),
                ["custom_events"] = new JArray(CustomEvents.ToArray())
            };
            var cs = new JObject();
            foreach (var kv in CustomState) cs[kv.Key] = kv.Value.ToString().ToUpperInvariant();
            o["custom_state"] = cs;
            return o.ToString(Newtonsoft.Json.Formatting.Indented);
        }
    }
}
