using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UnityFigmaBridge.Editor.FigmaApi
{
    /// <summary>
    ///     Downloads a document as a page list plus <c>/nodes</c> responses for groups of top-level nodes.
    ///     Glyph outlines of stroked text can make one page a GB of JSON, and Figma resets the HTTP/2
    ///     stream of a response that size part-way (curl error 92).
    /// </summary>
    internal static class FigmaDocumentDownload
    {
        /// <summary>Top-level nodes per first request. A group whose stream drops is halved, so this only sets the request count.</summary>
        private const int TOP_LEVEL_NODES_PER_REQUEST = 4;

        private const int SINGLE_NODE_ATTEMPTS = 3;
        private const int RETRY_DELAY_MS = 1500;

        internal sealed class Part
        {
            public string PageId;
            public List<string> NodeIds;
            public string Path;
        }

        /// <summary>The depth-2 file response: every top-level field, the pages, and their top-level nodes without children.</summary>
        public static async Task<JObject> PageList(string fileId, string accessToken, string path, string progressTitle)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await FigmaFileDownload.ToFile($"https://api.figma.com/v1/files/{fileId}?depth=2", accessToken, path,
                        progressTitle, "Downloading page list");
                    return FigmaDocumentMerge.ReadFile(path);
                }
                catch (FigmaApiRequestException e) when (e.IsTransient && attempt < SINGLE_NODE_ATTEMPTS)
                {
                    Debug.LogWarning($"[FigmaBridge] Page list failed ({FirstLine(e)}), attempt {attempt + 1} of {SINGLE_NODE_ATTEMPTS}");
                    await Task.Delay(RETRY_DELAY_MS);
                }
            }
        }

        /// <param name="pathOf">Where a part's response goes, by part number.</param>
        /// <param name="downloaded">
        ///     Receives the parts in document order as they land, so the caller can delete them when a
        ///     later part throws. Together they hold every node the import keeps at the top level.
        /// </param>
        public static async Task Parts(string fileId, string accessToken, JObject pageList,
            Func<int, string> pathOf, string progressTitle, List<Part> downloaded)
        {
            var pending = PlanParts(pageList);
            var singleNodeFailures = 0;
            while (pending.Count > 0)
            {
                var part = pending[0];
                part.Path = pathOf(downloaded.Count);
                try
                {
                    await FigmaFileDownload.ToFile(NodesUrl(fileId, part.NodeIds), accessToken, part.Path, progressTitle,
                        $"Downloading document part {downloaded.Count + 1}/{downloaded.Count + pending.Count}");
                    pending.RemoveAt(0);
                    downloaded.Add(part);
                    singleNodeFailures = 0;
                }
                catch (FigmaApiRequestException e) when (e.IsTransient &&
                                                         (part.NodeIds.Count > 1 || singleNodeFailures < SINGLE_NODE_ATTEMPTS - 1))
                {
                    if (part.NodeIds.Count > 1)
                    {
                        var half = part.NodeIds.Count / 2;
                        pending.RemoveAt(0);
                        pending.Insert(0, new Part { PageId = part.PageId, NodeIds = part.NodeIds.GetRange(half, part.NodeIds.Count - half) });
                        pending.Insert(0, new Part { PageId = part.PageId, NodeIds = part.NodeIds.GetRange(0, half) });
                        Debug.LogWarning($"[FigmaBridge] Document part of {part.NodeIds.Count} nodes failed ({FirstLine(e)}), split in two");
                    }
                    else
                    {
                        singleNodeFailures++;
                        Debug.LogWarning($"[FigmaBridge] Node {part.NodeIds[0]} failed ({FirstLine(e)}), attempt " +
                                         $"{singleNodeFailures + 1} of {SINGLE_NODE_ATTEMPTS}");
                    }
                    await Task.Delay(RETRY_DELAY_MS);
                }
                catch
                {
                    File.Delete(part.Path);
                    throw;
                }
            }
        }

        /// <summary>Pages and top-level nodes no part requests.</summary>
        public static List<string> IgnoredNames(JObject pageList) =>
            FigmaDocumentMerge.AllPages(pageList)
                .Concat(FigmaDocumentMerge.Pages(pageList).SelectMany(TopLevelNodes))
                .Select(node => (string)node["name"])
                .Where(FigmaDataUtils.IsIgnored)
                .ToList();

        private static IEnumerable<JToken> TopLevelNodes(JObject page) => (page["children"] as JArray) ?? new JArray();

        private static List<Part> PlanParts(JObject pageList)
        {
            var parts = new List<Part>();
            foreach (var page in FigmaDocumentMerge.Pages(pageList))
            {
                var nodeIds = TopLevelNodes(page)
                    .Where(node => !FigmaDataUtils.IsIgnored((string)node["name"]))
                    .Select(node => (string)node["id"])
                    .ToList();
                for (var start = 0; start < nodeIds.Count; start += TOP_LEVEL_NODES_PER_REQUEST)
                    parts.Add(new Part
                    {
                        PageId = (string)page["id"],
                        NodeIds = nodeIds.GetRange(start, Mathf.Min(TOP_LEVEL_NODES_PER_REQUEST, nodeIds.Count - start))
                    });
            }
            return parts;
        }

        // We need geometry=paths to get rotation and full transform
        private static string NodesUrl(string fileId, IEnumerable<string> nodeIds) =>
            $"https://api.figma.com/v1/files/{fileId}/nodes?ids={string.Join(",", nodeIds.Select(Uri.EscapeDataString))}&geometry=paths";

        private static string FirstLine(Exception e) => (e.Message ?? string.Empty).Split('\n')[0];
    }
}
