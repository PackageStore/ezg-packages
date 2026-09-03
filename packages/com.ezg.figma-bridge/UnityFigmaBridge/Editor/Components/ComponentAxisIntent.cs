using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Editor.Utils;

namespace UnityFigmaBridge.Editor.Components
{
    public sealed class ComponentAxisIntent
    {
        public string SetName;
        public string SetNodeId;
        public List<string> RuntimeAxes;
        public List<string> DesignAxes;
        public List<string> Variants;

        /// <summary>
        /// Reads the UNITY: directive from a component set's description and writes axis-intent.json
        /// beside the set's variant prefabs. A set with no directive produces empty RuntimeAxes and
        /// DesignAxes; unclassified axes appear in Variants.
        /// </summary>
        public static void WriteAxisIntent(Node parentNode, string setName, FigmaFile figmaFile,
            string componentPrefabFolder)
        {
            var allAxes = CollectAxes(parentNode);

            string description = null;
            if (figmaFile.componentSets != null &&
                figmaFile.componentSets.TryGetValue(parentNode.id, out var setEntry))
                description = setEntry.description;

            var intent = Parse(setName, parentNode.id, description, allAxes);

            var safeSetName = FigmaPaths.MakeValidFileName(setName.Trim());
            var jsonPath = Path.Combine(componentPrefabFolder, safeSetName, "axis-intent.json");
            File.WriteAllText(jsonPath, JsonConvert.SerializeObject(intent, Formatting.Indented));
        }

        static ComponentAxisIntent Parse(string setName, string setNodeId, string description,
            List<string> allAxes)
        {
            var intent = new ComponentAxisIntent
            {
                SetName = setName,
                SetNodeId = setNodeId,
                RuntimeAxes = new List<string>(),
                DesignAxes = new List<string>()
            };

            if (!string.IsNullOrEmpty(description))
            {
                foreach (var rawLine in description.Split('\n'))
                {
                    var line = rawLine.Trim().TrimStart('\r');
                    if (!line.StartsWith("UNITY:", StringComparison.OrdinalIgnoreCase)) continue;

                    var body = line.Substring(6).Trim();
                    if (!TryParseBody(body, intent))
                        Debug.LogWarning(
                            $"[ComponentAxisIntent] Could not parse UNITY line for set '{setName}': {line}");
                    break;
                }
            }

            var classified = new HashSet<string>(intent.RuntimeAxes);
            classified.UnionWith(intent.DesignAxes);
            intent.Variants = allAxes.Where(a => !classified.Contains(a)).ToList();

            return intent;
        }

        static bool TryParseBody(string body, ComponentAxisIntent intent)
        {
            var segments = body.Split(';');
            bool parsed = false;
            foreach (var seg in segments)
            {
                var trimmed = seg.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                var eqIndex = trimmed.IndexOf('=');
                if (eqIndex < 0) return false;

                var key = trimmed.Substring(0, eqIndex).Trim();
                var value = trimmed.Substring(eqIndex + 1).Trim();
                var axes = value.Split(',').Select(v => v.Trim()).Where(v => v.Length > 0).ToList();

                if (key.Equals("runtime-axis", StringComparison.OrdinalIgnoreCase))
                {
                    intent.RuntimeAxes = axes;
                    parsed = true;
                }
                else if (key.Equals("design-axis", StringComparison.OrdinalIgnoreCase))
                {
                    intent.DesignAxes = axes;
                    parsed = true;
                }
                else
                {
                    return false;
                }
            }

            return parsed;
        }

        static List<string> CollectAxes(Node setNode)
        {
            var axes = new List<string>();
            if (setNode.children == null) return axes;

            var seen = new HashSet<string>();
            foreach (var child in setNode.children)
            {
                foreach (var pair in child.name.Split(','))
                {
                    var eqIndex = pair.IndexOf('=');
                    if (eqIndex <= 0) continue;
                    var axis = pair.Substring(0, eqIndex).Trim();
                    if (seen.Add(axis)) axes.Add(axis);
                }
            }

            return axes;
        }
    }
}
