using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UnityFigmaBridge.Editor.Source
{
    /// Reads the document one page per doc.snapshot call and joins the pages the way the plugin joins them,
    /// so one huge reply never holds a lane and a slow page fails alone, with its name.
    static class BridgeSnapshot
    {
        public delegate Task<JToken> Requester(string op, JToken payload, TimeSpan timeout);

        static readonly string[] MergedMaps = { "components", "componentSets", "styles" };

        public static async Task<string> Read(Requester request, TimeSpan pageTimeout, TimeSpan listTimeout)
        {
            var pages = await ListPages(request, listTimeout).ConfigureAwait(false);
            if (pages == null || pages.Count == 0)
                return (string)(await request("doc.snapshot", new JObject(), pageTimeout).ConfigureAwait(false))["text"];

            var parts = new List<JObject>();
            for (var i = 0; i < pages.Count; i++)
            {
                var (id, name) = pages[i];
                var started = DateTime.UtcNow;
                JToken result;
                try
                {
                    var payload = new JObject { ["pageIds"] = new JArray(id) };
                    result = await request("doc.snapshot", payload, pageTimeout).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    throw new Exception($"Figma page '{name}' ({id}, {i + 1}/{pages.Count}) did not snapshot: {e.Message}", e);
                }
                parts.Add(JObject.Parse((string)result["text"]));
                Debug.Log($"[FigmaBridge] Snapshot page {i + 1}/{pages.Count} '{name}' in {(DateTime.UtcNow - started).TotalSeconds:0.0} s");
            }
            return Join(parts).ToString(Newtonsoft.Json.Formatting.None);
        }

        /// Null when the plugin has no inventory op: the caller falls back to one whole-file snapshot.
        static async Task<List<(string id, string name)>> ListPages(Requester request, TimeSpan timeout)
        {
            JToken result;
            try
            {
                var payload = new JObject { ["include"] = new JArray("pages") };
                result = await request("inventory", payload, timeout).ConfigureAwait(false);
            }
            catch (Exception e) when (e.Message.Contains("unknown op"))
            {
                return null;
            }
            return (result["pages"] as JArray ?? new JArray())
                .Select(p => ((string)p["id"], (string)p["name"]))
                .ToList();
        }

        public static JObject Join(IReadOnlyList<JObject> parts)
        {
            var doc = (JObject)parts[0].DeepClone();
            var children = new JArray(parts.SelectMany(p => p["document"]?["children"] as JArray ?? new JArray()));
            doc["document"]["children"] = children;
            foreach (var key in MergedMaps)
            {
                var merged = new JObject();
                foreach (var part in parts)
                foreach (var prop in (part[key] as JObject ?? new JObject()).Properties())
                    if (merged[prop.Name] == null) merged[prop.Name] = prop.Value;
                doc[key] = merged;
            }
            return doc;
        }
    }
}
