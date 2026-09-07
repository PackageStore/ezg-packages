using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityFigmaBridge.Editor.FigmaApi;
using UnityFigmaBridge.Editor.Settings;
using UnityFigmaBridge.Editor.Utils;
using UnityFigmaBridge.Runtime.UI;

namespace UnityFigmaBridge.Editor.PostProcess
{
    /// <summary>
    ///     Finds every <see cref="IFigmaImportPostProcessor"/> in the project and runs them against a
    ///     <see cref="FigmaImportContext"/>, either built from a live import or rebuilt from what is
    ///     on disk.
    /// </summary>
    public static class PostProcessorRunner
    {
        private const string LOG_PREFIX = "[FigmaBridge][PostProcess]";

        /// <summary>Nested instance chains deeper than this are almost certainly a cycle.</summary>
        private const int MAX_NESTING_DEPTH = 8;

        public static void Run(FigmaImportContext context)
        {
            var processors = new List<IFigmaImportPostProcessor>();
            foreach (var type in TypeCache.GetTypesDerivedFrom<IFigmaImportPostProcessor>())
            {
                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition) continue;
                if (type.GetConstructor(Type.EmptyTypes) == null)
                {
                    Debug.LogWarning($"{LOG_PREFIX} {type.FullName} has no parameterless constructor - skipped");
                    continue;
                }

                try
                {
                    processors.Add((IFigmaImportPostProcessor)Activator.CreateInstance(type));
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"{LOG_PREFIX} Could not create {type.FullName}: {e.Message}");
                }
            }

            if (processors.Count == 0)
            {
                Debug.Log($"{LOG_PREFIX} No post-processors found");
                return;
            }

            foreach (var processor in processors.OrderBy(p => p.Order).ThenBy(p => p.GetType().FullName, StringComparer.Ordinal))
            {
                var typeName = processor.GetType().FullName;
                try
                {
                    EditorUtility.DisplayProgressBar(UnityFigmaBridgeImporter.PROGRESS_BOX_TITLE, $"Post-processor {typeName}", 1f);
                    processor.OnDocumentImported(context);
                    Debug.Log($"{LOG_PREFIX} {typeName} ok");
                }
                catch (Exception e)
                {
                    Debug.LogError($"{LOG_PREFIX} {typeName} failed: {e}");
                }
            }
            EditorUtility.ClearProgressBar();
        }

        /// <summary>
        ///     Context for a live import. Also writes the per-screen sidecars that
        ///     <see cref="BuildContextFromDisk"/> reads back later.
        /// </summary>
        public static FigmaImportContext BuildContext(FigmaImportProcessData data)
        {
            var screens = new List<FigmaImportedScreen>();
            foreach (var screenPrefab in data.ScreenPrefabs)
            {
                if (screenPrefab == null) continue;
                var prefabPath = AssetDatabase.GetAssetPath(screenPrefab);
                if (string.IsNullOrEmpty(prefabPath)) continue;

                data.ScreenPrefabNodes.TryGetValue(prefabPath, out var frameNode);
                var instances = FlattenInstances(data.InstanceSources, prefabPath);
                var frameName = frameNode?.name ?? Path.GetFileNameWithoutExtension(prefabPath);

                screens.Add(new FigmaImportedScreen
                {
                    Frame = frameNode ?? new Node { name = frameName, type = NodeType.FRAME },
                    FrameName = frameName,
                    PrefabPath = prefabPath,
                    Instances = instances
                });

                FigmaInstanceSidecar.Write(prefabPath, new FigmaInstanceSidecar.Payload
                {
                    screen = frameName,
                    prefabPath = prefabPath,
                    documentName = data.SourceFile?.name,
                    bridgeVersion = UnityFigmaBridgeImporter.PACKAGE_VERSION,
                    instances = instances,
                    shapeOnlyNodes = data.ShapeOnlyNodes.Where(n => n.PrefabPath == prefabPath).ToList()
                });
            }

            var componentPaths = new Dictionary<string, string>();
            foreach (var pair in data.ComponentData.ComponentInstances)
            {
                if (pair.Value.ComponentPrefab == null) continue;
                var path = AssetDatabase.GetAssetPath(pair.Value.ComponentPrefab);
                if (!string.IsNullOrEmpty(path)) componentPaths[pair.Key] = path;
            }

            return new FigmaImportContext
            {
                Settings = data.Settings,
                SourceFile = data.SourceFile,
                FontMap = data.FontMap,
                DocumentName = data.SourceFile?.name,
                ScreenPrefabFolder = FigmaPaths.FigmaScreenPrefabFolder,
                ComponentPrefabFolder = FigmaPaths.FigmaComponentPrefabFolder,
                ImageFillFolder = FigmaPaths.FigmaImageFillFolder,
                Screens = screens,
                ComponentPrefabPaths = componentPaths,
                ShapeOnlyNodes = data.ShapeOnlyNodes.ToList(),
                PostProcessOnly = false,
                Offline = data.Offline
            };
        }

        /// <summary>
        ///     Context rebuilt from the prefabs and sidecars on disk. <paramref name="cachedDocument"/>
        ///     may be null; frames are then name-only stubs.
        /// </summary>
        public static FigmaImportContext BuildContextFromDisk(UnityFigmaBridgeSettings settings, FigmaFile cachedDocument)
        {
            var screens = new List<FigmaImportedScreen>();
            var screenNodesByPath = new Dictionary<string, Node>();
            if (cachedDocument != null)
            {
                foreach (var pageNode in FigmaDataUtils.GetPageNodes(cachedDocument))
                foreach (var screenNode in FigmaDataUtils.GetScreenNodes(pageNode))
                {
                    var path = FigmaPaths.GetPathForScreenPrefab(screenNode, 0);
                    if (path != null && !screenNodesByPath.ContainsKey(path)) screenNodesByPath[path] = screenNode;
                }
            }

            var screensFolder = FigmaPaths.FigmaScreenPrefabFolder;
            if (Directory.Exists(screensFolder))
            {
                foreach (var file in Directory.GetFiles(screensFolder, "*.prefab").OrderBy(f => f, StringComparer.Ordinal))
                {
                    var prefabPath = file.Replace('\\', '/');
                    var frameName = Path.GetFileNameWithoutExtension(prefabPath);
                    var sidecar = FigmaInstanceSidecar.Read(prefabPath);
                    if (!string.IsNullOrEmpty(sidecar?.screen)) frameName = sidecar.screen;
                    screenNodesByPath.TryGetValue(prefabPath, out var frameNode);

                    screens.Add(new FigmaImportedScreen
                    {
                        Frame = frameNode ?? new Node { name = frameName, type = NodeType.FRAME },
                        FrameName = frameName,
                        PrefabPath = prefabPath,
                        Instances = sidecar?.instances ?? new List<FigmaInstanceSource>()
                    });
                }
            }

            var shapeOnly = new List<ShapeOnlyNode>();
            foreach (var screen in screens)
            {
                var sidecar = FigmaInstanceSidecar.Read(screen.PrefabPath);
                if (sidecar?.shapeOnlyNodes != null) shapeOnly.AddRange(sidecar.shapeOnlyNodes);
            }

            return new FigmaImportContext
            {
                Settings = settings,
                SourceFile = cachedDocument,
                FontMap = null,
                DocumentName = cachedDocument?.name,
                ScreenPrefabFolder = FigmaPaths.FigmaScreenPrefabFolder,
                ComponentPrefabFolder = FigmaPaths.FigmaComponentPrefabFolder,
                ImageFillFolder = FigmaPaths.FigmaImageFillFolder,
                Screens = screens,
                ComponentPrefabPaths = new Dictionary<string, string>(),
                ShapeOnlyNodes = shapeOnly,
                PostProcessOnly = true,
                Offline = true
            };
        }

        /// <summary>
        ///     Direct instances of a prefab plus, recursively, the instances inside each instanced
        ///     component, with hierarchy paths and source chains extended so they read from the
        ///     screen root.
        /// </summary>
        private static List<FigmaInstanceSource> FlattenInstances(
            Dictionary<string, List<FigmaInstanceSource>> instanceSources, string prefabPath)
        {
            var result = new List<FigmaInstanceSource>();
            if (!instanceSources.TryGetValue(prefabPath, out var direct)) return result;

            foreach (var instance in direct)
            {
                result.Add(instance);
                AppendNested(instanceSources, instance, result, 1);
            }
            return result;
        }

        private static void AppendNested(Dictionary<string, List<FigmaInstanceSource>> instanceSources,
            FigmaInstanceSource outer, List<FigmaInstanceSource> result, int depth)
        {
            if (depth > MAX_NESTING_DEPTH) return;
            if (string.IsNullOrEmpty(outer.ComponentPrefabPath)) return;
            if (!instanceSources.TryGetValue(outer.ComponentPrefabPath, out var nestedList)) return;

            foreach (var nested in nestedList)
            {
                // The nested entry's HierarchyPath starts at the component prefab root; the outer
                // instance root replaces that first segment in the screen.
                var relative = StripFirstSegment(nested.HierarchyPath);
                var flattened = new FigmaInstanceSource
                {
                    NodeId = nested.NodeId,
                    NodeName = nested.NodeName,
                    ComponentId = nested.ComponentId,
                    ComponentPrefabPath = nested.ComponentPrefabPath,
                    HierarchyPath = string.IsNullOrEmpty(relative) ? outer.HierarchyPath : $"{outer.HierarchyPath}/{relative}",
                    SourcePathChain = (outer.SourcePathChain ?? Array.Empty<string>())
                        .Concat(new[] { nested.ComponentPrefabPath }).ToArray()
                };
                result.Add(flattened);
                AppendNested(instanceSources, flattened, result, depth + 1);
            }
        }

        private static string StripFirstSegment(string hierarchyPath)
        {
            if (string.IsNullOrEmpty(hierarchyPath)) return string.Empty;
            var slash = hierarchyPath.IndexOf('/');
            return slash < 0 ? string.Empty : hierarchyPath.Substring(slash + 1);
        }

        /// <summary>'/'-joined names from the topmost ancestor that still carries a bridge marker down to <paramref name="transform"/>.</summary>
        public static string HierarchyPathWithinPrefab(Transform transform)
        {
            var segments = new List<string>();
            var current = transform;
            while (current != null)
            {
                segments.Add(current.name);
                var parent = current.parent;
                if (parent == null || parent.GetComponent<FigmaNodeObject>() == null) break;
                current = parent;
            }
            segments.Reverse();
            return string.Join("/", segments);
        }
    }
}
