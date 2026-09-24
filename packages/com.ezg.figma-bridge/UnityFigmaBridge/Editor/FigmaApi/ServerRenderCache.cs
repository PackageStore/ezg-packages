using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityFigmaBridge.Editor.Settings;
using UnityFigmaBridge.Editor.Utils;

namespace UnityFigmaBridge.Editor.FigmaApi
{
    /// <summary>
    ///     Lets an online import skip the Figma render and download of a server-rendered node whose
    ///     PNG on disk still matches it. A render is keyed by a hash of its node subtree in the
    ///     downloaded document JSON, where instance children come resolved, so editing a master
    ///     component changes the hash of every render that shows it. The node's position on its
    ///     page is left out. The manifest at <see cref="ManifestPath"/> is per machine; deleting it
    ///     fetches every render again.
    /// </summary>
    public sealed class ServerRenderCache
    {
        /// <summary>Bump whenever the render request, the download import or the slicer changes what a render PNG holds.</summary>
        public const int FormatVersion = 1;

        /// <summary>Project-relative; under Library so it stays per machine and out of version control.</summary>
        public const string ManifestPath = "Library/FigmaBridge/server-render-cache.json";

        private sealed class Entry
        {
            public string nodeId;
            public string hash;
            public long length;
            public long lastWriteTicks;
        }

        private readonly Dictionary<string, Entry> m_Manifest;
        private readonly Dictionary<string, (string path, string hash)> m_RenderById = new();

        /// <summary>Render nodes that must be requested and downloaded; every other one is reused from disk.</summary>
        public List<ServerRenderNodeData> StaleNodes { get; } = new();

        private ServerRenderCache(Dictionary<string, Entry> manifest)
        {
            m_Manifest = manifest;
        }

        /// <summary>Hashes every render node and fills <see cref="StaleNodes"/> with the ones the manifest and disk do not match.</summary>
        /// <param name="documentJsonPath">
        ///     Raw JSON of the document this import downloaded. The deserialized model drops fields
        ///     that change pixels (vector paths, boolean operations), so the hash reads the raw nodes.
        /// </param>
        /// <param name="repeatNodeIds">Renders imported with wrap Repeat (<see cref="FigmaDataUtils.GetPatternSourceNodeIds"/>).</param>
        public static ServerRenderCache Check(string documentJsonPath, List<ServerRenderNodeData> renderNodes, int renderScale,
            UnityFigmaBridgeSettings settings, HashSet<string> repeatNodeIds)
        {
            var cache = new ServerRenderCache(LoadManifest());
            // The first entry of an id decides its file path, so it decides the hash too
            var renderById = new Dictionary<string, ServerRenderNodeData>();
            foreach (var renderNode in renderNodes)
                if (!renderById.ContainsKey(renderNode.SourceNode.id)) renderById[renderNode.SourceNode.id] = renderNode;

            var paths = renderById.Keys.ToDictionary(id => id, id => FigmaPaths.GetPathForServerRenderedImage(id, renderNodes));
            // Renders sharing one file (Export frames with one name) would each take the other's PNG as their own
            var sharedPaths = new HashSet<string>(paths.Values.GroupBy(path => path).Where(group => group.Count() > 1).Select(group => group.Key));
            var sources = FindNodeSources(documentJsonPath, new HashSet<string>(renderById.Keys));

            foreach (var pair in renderById)
            {
                var path = paths[pair.Key];
                if (sharedPaths.Contains(path) || !sources.TryGetValue(pair.Key, out var source)) continue;
                var repeats = repeatNodeIds != null && repeatNodeIds.Contains(pair.Key);
                cache.m_RenderById[pair.Key] = (path, Hash(source, pair.Value.RenderType, renderScale, settings, repeats));
            }

            cache.StaleNodes.AddRange(renderNodes.Where(renderNode => !cache.IsOnDisk(renderNode.SourceNode.id)));
            return cache;
        }

        /// <summary>
        ///     Remember the renders this import downloaded. Call it after the slicer has rewritten
        ///     them, so the recorded file length and write time are final and a PNG deleted or edited
        ///     afterwards no longer matches.
        /// </summary>
        public void Record(IEnumerable<string> downloadedNodeIds)
        {
            var changed = false;
            foreach (var nodeId in downloadedNodeIds)
            {
                if (nodeId == null || !m_RenderById.TryGetValue(nodeId, out var render)) continue;
                var file = new FileInfo(render.path);
                if (!file.Exists) continue;
                m_Manifest[render.path] = new Entry
                {
                    nodeId = nodeId,
                    hash = render.hash,
                    length = file.Length,
                    lastWriteTicks = file.LastWriteTimeUtc.Ticks
                };
                changed = true;
            }
            if (changed) SaveManifest();
        }

        private bool IsOnDisk(string nodeId)
        {
            if (!m_RenderById.TryGetValue(nodeId, out var render) || !m_Manifest.TryGetValue(render.path, out var entry)) return false;
            if (entry.nodeId != nodeId || entry.hash != render.hash) return false;
            var file = new FileInfo(render.path);
            return file.Exists && file.Length == entry.length && file.LastWriteTimeUtc.Ticks == entry.lastWriteTicks &&
                   AssetImporter.GetAtPath(render.path) is TextureImporter { textureType: TextureImporterType.Sprite };
        }

        private static string Hash(JObject source, ServerRenderType renderType, int renderScale, UnityFigmaBridgeSettings settings,
            bool repeats)
        {
            var useAbsoluteBounds = renderType == ServerRenderType.PatternSource;
            var sliced = settings.SliceServerRenders && renderType == ServerRenderType.Substitution;
            var text = new StringWriter(CultureInfo.InvariantCulture);
            text.Write($"{FormatVersion}|{renderType}|{useAbsoluteBounds}|{renderScale}|{sliced}|{repeats}|" +
                       $"{settings.SpriteMipmaps}|{settings.SpriteCompression}|");
            var json = new JsonTextWriter(text);
            WriteCanonical(json, source, true, false);
            json.Flush();

            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        ///     Properties in ordinal order, so the hash does not depend on how Figma orders them. The
        ///     page position is dropped: x and y of every absolute box, and the translation of the
        ///     root's relative transform. Descendants keep their relative transforms, which place them
        ///     inside the render.
        /// </summary>
        private static void WriteCanonical(JsonWriter writer, JToken token, bool isRoot, bool isAbsoluteBox)
        {
            switch (token)
            {
                case JObject node:
                    writer.WriteStartObject();
                    foreach (var property in node.Properties().OrderBy(property => property.Name, StringComparer.Ordinal))
                    {
                        if (isAbsoluteBox && property.Name is "x" or "y") continue;
                        writer.WritePropertyName(property.Name);
                        if (isRoot && property.Name == "relativeTransform" && property.Value is JArray rows)
                        {
                            writer.WriteStartArray();
                            foreach (var row in rows)
                                WriteCanonical(writer, row is JArray { Count: 3 } cells ? new JArray(cells[0], cells[1]) : row, false, false);
                            writer.WriteEndArray();
                        }
                        else
                        {
                            WriteCanonical(writer, property.Value, false,
                                property.Name is "absoluteBoundingBox" or "absoluteRenderBounds");
                        }
                    }
                    writer.WriteEndObject();
                    break;
                case JArray array:
                    writer.WriteStartArray();
                    foreach (var item in array) WriteCanonical(writer, item, false, false);
                    writer.WriteEndArray();
                    break;
                default:
                    token.WriteTo(writer);
                    break;
            }
        }

        private static Dictionary<string, JObject> FindNodeSources(string documentJsonPath, HashSet<string> nodeIds)
        {
            var sources = new Dictionary<string, JObject>();
            if (!File.Exists(documentJsonPath)) return sources;
            try
            {
                // Dates stay strings, so they hash as Figma wrote them
                using var reader = new JsonTextReader(File.OpenText(documentJsonPath)) { DateParseHandling = DateParseHandling.None };
                CollectNodeSources(JToken.ReadFrom(reader)["document"], nodeIds, sources);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ServerRenderCache] '{documentJsonPath}' did not parse, every server render is fetched again: {e.Message}");
            }
            return sources;
        }

        private static void CollectNodeSources(JToken token, HashSet<string> nodeIds, Dictionary<string, JObject> sources)
        {
            if (token is not JObject node) return;
            var nodeId = (string)node["id"];
            if (nodeId != null && nodeIds.Contains(nodeId) && !sources.ContainsKey(nodeId)) sources[nodeId] = node;
            if (node["children"] is not JArray children) return;
            foreach (var child in children) CollectNodeSources(child, nodeIds, sources);
        }

        private static Dictionary<string, Entry> LoadManifest()
        {
            try
            {
                if (File.Exists(ManifestPath))
                    return JsonConvert.DeserializeObject<Dictionary<string, Entry>>(File.ReadAllText(ManifestPath)) ??
                           new Dictionary<string, Entry>();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ServerRenderCache] '{ManifestPath}' did not parse, every server render is fetched again: {e.Message}");
            }
            return new Dictionary<string, Entry>();
        }

        private void SaveManifest()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath));
                File.WriteAllText(ManifestPath, JsonConvert.SerializeObject(m_Manifest, Formatting.Indented));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ServerRenderCache] Could not write '{ManifestPath}': {e.Message}");
            }
        }
    }
}
