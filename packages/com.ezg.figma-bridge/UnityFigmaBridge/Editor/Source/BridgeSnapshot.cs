using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityFigmaBridge.Editor.FigmaApi;

namespace UnityFigmaBridge.Editor.Source
{
    /// Reads the document one page per doc.snapshot call and joins the pages the way the plugin joins them,
    /// so one huge reply never holds a lane and a slow page fails alone, with its name. Only the pages the
    /// selection needs are read; the others stay in the document as empty pages marked bridgeUnread.
    static class BridgeSnapshot
    {
        public delegate Task<JToken> Requester(string op, JToken payload, TimeSpan timeout);

        static readonly string[] MergedMaps = { "components", "componentSets", "styles" };

        public static async Task<string> Read(Requester request, TimeSpan pageTimeout, TimeSpan listTimeout,
            BridgePageSelection selection = null, string documentName = null)
        {
            var pages = await ListPages(request, listTimeout).ConfigureAwait(false);
            if (pages == null || pages.Count == 0)
                return (string)(await request("doc.snapshot", new JObject(), pageTimeout).ConfigureAwait(false))["text"];

            var read = selection == null || selection.EveryPage
                ? new HashSet<string>(pages.Select(page => page.Id))
                : await Plan(request, pages, selection, pageTimeout, listTimeout).ConfigureAwait(false);

            var parts = new List<JObject>();
            var children = new JArray();
            var number = 0;
            foreach (var page in pages)
            {
                if (!read.Contains(page.Id))
                {
                    children.Add(Unread(page));
                    continue;
                }
                number++;
                var started = DateTime.UtcNow;
                JToken result;
                try
                {
                    var payload = new JObject { ["pageIds"] = new JArray(page.Id) };
                    result = await request("doc.snapshot", payload, pageTimeout).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    throw new Exception($"Figma page '{page.Name}' ({page.Id}, {number}/{read.Count}) did not snapshot: {e.Message}", e);
                }
                var part = JObject.Parse((string)result["text"]);
                parts.Add(part);
                foreach (var child in part["document"]?["children"] as JArray ?? new JArray()) children.Add(child);
                Debug.Log($"[FigmaBridge] Snapshot page {number}/{read.Count} '{page.Name}' in {(DateTime.UtcNow - started).TotalSeconds:0.0} s");
            }
            return Join(parts, children, documentName).ToString(Newtonsoft.Json.Formatting.None);
        }

        /// The page ids to read: the selection's pages, plus the pages holding the mains their instances use
        /// (plugin op doc.pages), never a page named with [ignore].
        static async Task<HashSet<string>> Plan(Requester request, IReadOnlyList<BridgePage> pages,
            BridgePageSelection selection, TimeSpan planTimeout, TimeSpan listTimeout)
        {
            var known = new HashSet<string>(pages.Select(page => page.Id));
            var picked = new HashSet<string>((selection.Pick?.Invoke(pages) ?? Array.Empty<string>()).Where(known.Contains));
            HashSet<string> componentPages = null;
            if (selection.ComponentPages)
            {
                componentPages = await ComponentPages(request, listTimeout).ConfigureAwait(false);
                if (componentPages == null) return NotIgnored(pages, known);
                picked.UnionWith(componentPages.Where(known.Contains));
            }
            if (picked.Count == 0)
            {
                Debug.Log("[FigmaBridge] No page to read: the settings select no page of this file.");
                return picked;
            }

            JToken plan;
            try
            {
                var payload = new JObject
                {
                    ["pageIds"] = new JArray(pages.Where(page => picked.Contains(page.Id)).Select(page => page.Id)),
                    ["dependencies"] = true,
                    ["skipIgnored"] = true,
                };
                plan = await request("doc.pages", payload, planTimeout).ConfigureAwait(false);
            }
            catch (Exception e) when (e.Message.Contains("unknown op"))
            {
                // An EZG Tools plugin older than doc.pages: every page that holds a component can hold a used main.
                componentPages ??= await ComponentPages(request, listTimeout).ConfigureAwait(false);
                Debug.LogWarning("[FigmaBridge] The EZG Tools plugin has no doc.pages: reopen it to update. " +
                                 "Reading the selected pages and every page that holds a component.");
                if (componentPages == null) return NotIgnored(pages, known);
                picked.UnionWith(componentPages.Where(known.Contains));
                return NotIgnored(pages, picked);
            }

            var read = new HashSet<string>();
            var lines = new List<string>();
            foreach (var page in plan["pages"] as JArray ?? new JArray())
            {
                read.Add((string)page["id"]);
                var via = (string)page["via"];
                lines.Add(via == null ? $"'{page["name"]}'" : $"'{page["name"]}' (instances use '{via}')");
            }
            var skipped = (plan["skipped"] as JArray ?? new JArray())
                .Select(page => $"'{page["name"]}' ({page["reason"]})").ToList();
            Debug.Log($"[FigmaBridge] Reading {read.Count} of {pages.Count} page(s): {string.Join(", ", lines)}" +
                      (skipped.Count > 0 ? $". Not read: {string.Join(", ", skipped)}" : "") +
                      $". Plan in {((double?)plan["ms"] ?? 0) / 1000:0.0} s.");
            return read;
        }

        static HashSet<string> NotIgnored(IReadOnlyList<BridgePage> pages, HashSet<string> ids) =>
            new HashSet<string>(pages.Where(page => ids.Contains(page.Id) && !FigmaDataUtils.IsIgnored(page.Name)).Select(page => page.Id));

        /// Ids of the pages that hold a component or component set; null when the list was cut.
        static async Task<HashSet<string>> ComponentPages(Requester request, TimeSpan timeout)
        {
            var payload = new JObject { ["include"] = new JArray("components") };
            var result = await request("inventory", payload, timeout).ConfigureAwait(false);
            if (result["truncated"] is JArray cut && cut.Count > 0) return null;
            return new HashSet<string>((result["components"] as JArray ?? new JArray()).Select(c => (string)c["pageId"]));
        }

        /// Null when the plugin has no inventory op: the caller falls back to one whole-file snapshot.
        static async Task<List<BridgePage>> ListPages(Requester request, TimeSpan timeout)
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
                .Select(p => new BridgePage((string)p["id"], (string)p["name"]))
                .ToList();
        }

        static JObject Unread(BridgePage page) => new JObject
        {
            ["id"] = page.Id,
            ["name"] = page.Name,
            ["type"] = "CANVAS",
            ["children"] = new JArray(),
            ["bridgeUnread"] = true,
        };

        /// The pages' maps merged first-wins, under a document whose pages are <paramref name="children"/>.
        public static JObject Join(IReadOnlyList<JObject> parts, JArray children, string documentName = null)
        {
            var doc = parts.Count > 0
                ? (JObject)parts[0].DeepClone()
                : new JObject
                {
                    ["document"] = new JObject { ["id"] = "0:0", ["name"] = "Document", ["type"] = "DOCUMENT" },
                    ["schemaVersion"] = 0,
                    ["name"] = string.IsNullOrEmpty(documentName) ? "Document" : documentName,
                    ["lastModified"] = DateTime.UtcNow.ToString("o"),
                    ["version"] = "bridge",
                };
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
