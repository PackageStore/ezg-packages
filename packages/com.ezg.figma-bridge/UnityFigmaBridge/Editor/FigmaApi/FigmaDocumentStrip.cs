using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace UnityFigmaBridge.Editor.FigmaApi
{
    /// <summary>
    ///     Stream-copies a document response without what the import never reads: the glyph outlines of
    ///     TEXT nodes, and every node named with <see cref="FigmaDataUtils.IgnoreMarker"/> with its subtree.
    ///     The file is read twice because a node's name comes after its opening brace: the first pass
    ///     numbers the objects that are ignored nodes, the second leaves them out.
    /// </summary>
    internal static class FigmaDocumentStrip
    {
        private struct Container
        {
            public bool IsChildrenArray;
            public bool IsNode;
            public int Ordinal;
        }

        /// <returns>Names of the ignored nodes left out, outermost only.</returns>
        public static List<string> Write(string sourcePath, string targetPath)
        {
            var ignoredNames = new List<string>();
            var ignoredOrdinals = FindIgnoredNodes(sourcePath, ignoredNames);
            WriteWithout(sourcePath, targetPath, ignoredOrdinals);
            return ignoredNames;
        }

        /// <returns>The ordinal (1-based count of object starts) of each node object named as ignored.</returns>
        private static HashSet<int> FindIgnoredNodes(string path, List<string> ignoredNames)
        {
            var ignored = new HashSet<int>();
            var containers = new Stack<Container>();
            var ordinal = 0;
            string property = null;
            using var reader = OpenReader(path);
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonToken.StartObject:
                        ordinal++;
                        containers.Push(new Container
                        {
                            IsNode = containers.Count > 0 && containers.Peek().IsChildrenArray,
                            Ordinal = ordinal
                        });
                        break;
                    case JsonToken.StartArray:
                        containers.Push(new Container { IsChildrenArray = property == "children" });
                        break;
                    case JsonToken.EndObject:
                    case JsonToken.EndArray:
                        containers.Pop();
                        break;
                    case JsonToken.PropertyName:
                        if ((string)reader.Value == "name" && containers.Peek().IsNode && reader.Read() &&
                            reader.TokenType == JsonToken.String && FigmaDataUtils.IsIgnored((string)reader.Value) &&
                            !InsideIgnored(containers, ignored))
                        {
                            ignored.Add(containers.Peek().Ordinal);
                            ignoredNames.Add((string)reader.Value);
                        }
                        break;
                }
                property = reader.TokenType == JsonToken.PropertyName ? (string)reader.Value : null;
            }
            return ignored;
        }

        private static bool InsideIgnored(Stack<Container> containers, HashSet<int> ignored)
        {
            foreach (var container in containers)
                if (container.IsNode && ignored.Contains(container.Ordinal)) return true;
            return false;
        }

        /// <summary>
        ///     Figma writes a node's type before its geometry; geometry met first is kept. Every skip
        ///     counts the objects it passes, so ordinals stay those of the first pass.
        /// </summary>
        private static void WriteWithout(string sourcePath, string targetPath, HashSet<int> ignoredOrdinals)
        {
            using var reader = OpenReader(sourcePath);
            using var writer = new JsonTextWriter(File.CreateText(targetPath));
            var objectTypes = new Stack<string>();
            var typeValueNext = false;
            var ordinal = 0;
            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.PropertyName && reader.Value is "fillGeometry" or "strokeGeometry" &&
                    objectTypes.Peek() == "TEXT")
                {
                    reader.Read();
                    ordinal += SkipCounting(reader);
                    continue;
                }
                if (reader.TokenType == JsonToken.StartObject && ignoredOrdinals.Contains(++ordinal))
                {
                    ordinal += SkipCounting(reader);
                    typeValueNext = false;
                    continue;
                }
                if (typeValueNext && reader.TokenType == JsonToken.String)
                {
                    objectTypes.Pop();
                    objectTypes.Push((string)reader.Value);
                }
                typeValueNext = reader.TokenType == JsonToken.PropertyName && (string)reader.Value == "type";
                if (reader.TokenType == JsonToken.StartObject) objectTypes.Push(null);
                else if (reader.TokenType == JsonToken.EndObject) objectTypes.Pop();
                writer.WriteToken(reader, false);
            }
        }

        /// <summary>Moves the reader past the value it is on.</summary>
        /// <returns>Object starts inside that value, not counting the value itself.</returns>
        private static int SkipCounting(JsonReader reader)
        {
            if (reader.TokenType is not (JsonToken.StartObject or JsonToken.StartArray)) return 0;
            var depth = reader.Depth;
            var starts = 0;
            while (reader.Read() && reader.Depth > depth)
                if (reader.TokenType == JsonToken.StartObject) starts++;
            return starts;
        }

        private static JsonTextReader OpenReader(string path) =>
            new JsonTextReader(File.OpenText(path)) { DateParseHandling = DateParseHandling.None };
    }
}
