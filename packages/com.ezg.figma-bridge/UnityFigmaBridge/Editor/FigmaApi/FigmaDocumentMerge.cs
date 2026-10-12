using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityFigmaBridge.Editor.FigmaApi
{
    /// <summary>
    ///     Joins a depth-2 file response and the <c>/nodes</c> responses of its top-level nodes into the
    ///     shape of a whole-file response. Nodes are copied token by token, so no part is held in memory whole.
    /// </summary>
    internal static class FigmaDocumentMerge
    {
        /// <summary>Per-subtree maps in a <c>/nodes</c> response; the whole file holds their union.</summary>
        private static readonly string[] SubtreeMaps = { "components", "componentSets", "styles" };

        public static JObject ReadFile(string path)
        {
            using var reader = new JsonTextReader(File.OpenText(path)) { DateParseHandling = DateParseHandling.None };
            return JObject.Load(reader);
        }

        public static IEnumerable<JObject> AllPages(JObject pageList) =>
            ((pageList["document"]?["children"] as JArray) ?? new JArray()).OfType<JObject>();

        /// <summary>Pages the import keeps: a page named with <see cref="FigmaDataUtils.IgnoreMarker"/> is left out whole.</summary>
        public static IEnumerable<JObject> Pages(JObject pageList) =>
            AllPages(pageList).Where(page => !FigmaDataUtils.IsIgnored((string)page["name"]));

        /// <param name="parts">Downloaded parts in document order (<see cref="FigmaDocumentDownload.Parts"/>).</param>
        public static void Write(JObject pageList, IReadOnlyList<FigmaDocumentDownload.Part> parts, string targetPath)
        {
            var maps = SubtreeMaps.ToDictionary(name => name, _ => new JObject());
            using var writer = new JsonTextWriter(File.CreateText(targetPath));
            writer.WriteStartObject();
            writer.WritePropertyName("document");
            writer.WriteStartObject();
            WriteWithoutChildren((JObject)pageList["document"], writer);
            writer.WritePropertyName("children");
            writer.WriteStartArray();
            foreach (var page in Pages(pageList))
            {
                writer.WriteStartObject();
                WriteWithoutChildren(page, writer);
                writer.WritePropertyName("children");
                writer.WriteStartArray();
                foreach (var part in parts.Where(part => part.PageId == (string)page["id"]))
                    foreach (var nodeId in part.NodeIds)
                        CopyNode(part.Path, nodeId, writer, maps);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();

            foreach (var map in maps)
            {
                writer.WritePropertyName(map.Key);
                map.Value.WriteTo(writer);
            }
            foreach (var property in pageList.Properties())
                if (property.Name != "document" && !maps.ContainsKey(property.Name)) property.WriteTo(writer);
            writer.WriteEndObject();
        }

        private static void WriteWithoutChildren(JObject node, JsonWriter writer)
        {
            foreach (var property in node.Properties())
                if (property.Name != "children") property.WriteTo(writer);
        }

        /// <summary>Copies <c>nodes[nodeId].document</c> of a response and folds its subtree maps into <paramref name="maps"/>.</summary>
        private static void CopyNode(string path, string nodeId, JsonWriter writer, Dictionary<string, JObject> maps)
        {
            using var reader = new JsonTextReader(File.OpenText(path)) { DateParseHandling = DateParseHandling.None };
            reader.Read();
            if (!SeekProperty(reader, "nodes") || !SeekProperty(reader, nodeId) || reader.TokenType != JsonToken.StartObject)
                throw new Exception($"Figma returned no node {nodeId}");

            var copied = false;
            while (reader.Read() && reader.TokenType == JsonToken.PropertyName)
            {
                var name = (string)reader.Value;
                reader.Read();
                if (name == "document")
                {
                    writer.WriteToken(reader, true);
                    copied = true;
                }
                else if (maps.TryGetValue(name, out var map) && reader.TokenType == JsonToken.StartObject)
                {
                    foreach (var entry in JObject.Load(reader).Properties()) map[entry.Name] = entry.Value;
                }
                else
                {
                    reader.Skip();
                }
            }
            if (!copied) throw new Exception($"Figma returned no document for node {nodeId}");
        }

        /// <summary>Walks the properties of the object the reader is on, leaving it on the value of <paramref name="name"/>.</summary>
        private static bool SeekProperty(JsonReader reader, string name)
        {
            if (reader.TokenType != JsonToken.StartObject) return false;
            while (reader.Read() && reader.TokenType == JsonToken.PropertyName)
            {
                var found = (string)reader.Value == name;
                reader.Read();
                if (found) return true;
                reader.Skip();
            }
            return false;
        }
    }
}
