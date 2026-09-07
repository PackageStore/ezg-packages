using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace UnityFigmaBridge.Editor.PostProcess
{
    /// <summary>
    ///     Per-screen JSON written beside the raw screen prefab (<c>&lt;Frame&gt;.instances.json</c>)
    ///     so <c>Run Post-Processors (no Sync)</c> can rebuild a <see cref="FigmaImportContext"/>
    ///     without the document or the network. Holds what is lost once the bridge strips its
    ///     temporary markers: which GameObject came from which component prefab.
    /// </summary>
    public static class FigmaInstanceSidecar
    {
        public const string SUFFIX = ".instances.json";

        [Serializable]
        public sealed class Payload
        {
            public string screen;
            public string prefabPath;
            public string documentName;
            public string bridgeVersion;
            public List<FigmaInstanceSource> instances = new();
            public List<ShapeOnlyNode> shapeOnlyNodes = new();
        }

        public static string PathFor(string screenPrefabPath)
        {
            if (string.IsNullOrEmpty(screenPrefabPath)) return null;
            var withoutExtension = screenPrefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                ? screenPrefabPath.Substring(0, screenPrefabPath.Length - ".prefab".Length)
                : screenPrefabPath;
            return withoutExtension + SUFFIX;
        }

        public static void Write(string screenPrefabPath, Payload payload)
        {
            var path = PathFor(screenPrefabPath);
            if (path == null) return;
            try
            {
                var json = JsonConvert.SerializeObject(payload, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FigmaBridge][PostProcess] Could not write sidecar '{path}': {e.Message}");
            }
        }

        public static Payload Read(string screenPrefabPath)
        {
            var path = PathFor(screenPrefabPath);
            if (path == null || !File.Exists(path)) return null;
            try
            {
                return JsonConvert.DeserializeObject<Payload>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FigmaBridge][PostProcess] Could not read sidecar '{path}': {e.Message}");
                return null;
            }
        }
    }
}
