using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ezg.ProceduralAnimation;
using UnityEditor;
using UnityEngine;

namespace Ezg.ProceduralAnimation.Editor
{
    /// <summary>
    /// Headless, string-based facade over the Procedural Animation toolchain so external
    /// drivers (UnityMCP unity_reflect / menu execution / batch scripts) can run the full
    /// pipeline without the InbetweenAnimationGeneratorWindow GUI:
    /// pose capture -> graph authoring -> validation -> batch clip generation.
    /// Every method takes primitives/asset paths and returns a human-readable report string.
    /// Partials: GraphEdit (structure edits), FeelPresets (feel tuning), Poses (bone/preview utilities).
    /// </summary>
    public static partial class ProceduralAnimationApi
    {
        // ─────────────────────────────── Asset creation ───────────────────────────────

        public static string CreatePoseAsset(string assetPath)
        {
            PoseAsset existing = AssetDatabase.LoadAssetAtPath<PoseAsset>(assetPath);
            if (existing != null)
            {
                return $"EXISTS {assetPath}";
            }

            AnimationClipWriter.EnsureAssetFolder(GetFolder(assetPath));
            PoseAsset pose = ScriptableObject.CreateInstance<PoseAsset>();
            AssetDatabase.CreateAsset(pose, assetPath);
            AssetDatabase.SaveAssets();
            return $"CREATED {assetPath}";
        }

        public static string CreateFeelPreset(string assetPath)
        {
            FeelPresetAsset existing = AssetDatabase.LoadAssetAtPath<FeelPresetAsset>(assetPath);
            if (existing != null)
            {
                return $"EXISTS {assetPath}";
            }

            AnimationClipWriter.EnsureAssetFolder(GetFolder(assetPath));
            FeelPresetAsset preset = ScriptableObject.CreateInstance<FeelPresetAsset>();
            AssetDatabase.CreateAsset(preset, assetPath);
            AssetDatabase.SaveAssets();
            return $"CREATED {assetPath}";
        }

        /// <summary>Creates the 35 DOTween-style ease presets (easeLinear .. easeInOutFlash) in the given folder.</summary>
        public static string CreateDefaultFeelPresets(string folder)
        {
            DefaultFeelPresetFactory.CreateDefaultPresets(folder);
            return $"CREATED 35 ease presets in {folder}";
        }

        public static string CreateGraph(string assetPath)
        {
            PoseCombinationGraphAsset existing = AssetDatabase.LoadAssetAtPath<PoseCombinationGraphAsset>(assetPath);
            if (existing != null)
            {
                return $"EXISTS {assetPath}";
            }

            AnimationClipWriter.EnsureAssetFolder(GetFolder(assetPath));
            PoseCombinationGraphAsset graph = ScriptableObject.CreateInstance<PoseCombinationGraphAsset>();
            AssetDatabase.CreateAsset(graph, assetPath);
            AssetDatabase.SaveAssets();
            return $"CREATED {assetPath}";
        }

        // ─────────────────────────────── Pose capture ───────────────────────────────

        /// <summary>
        /// Captures the current scene pose of a skeleton into a PoseAsset.
        /// skeletonRootHierarchyPath is the scene hierarchy path (e.g. "Stickman/bone_root").
        /// includedBonePathsCsv optionally restricts capture to a comma-separated set of relative bone paths.
        /// </summary>
        public static string CapturePose(string skeletonRootHierarchyPath, string poseAssetPath, string includedBonePathsCsv = null)
        {
            Transform root = FindSceneTransform(skeletonRootHierarchyPath);
            if (root == null)
            {
                return $"ERROR scene object not found: {skeletonRootHierarchyPath}";
            }

            PoseAsset pose = AssetDatabase.LoadAssetAtPath<PoseAsset>(poseAssetPath);
            if (pose == null)
            {
                CreatePoseAsset(poseAssetPath);
                pose = AssetDatabase.LoadAssetAtPath<PoseAsset>(poseAssetPath);
            }

            HashSet<string> included = null;
            if (!string.IsNullOrEmpty(includedBonePathsCsv))
            {
                included = new HashSet<string>(includedBonePathsCsv.Split(',').Select(s => s.Trim()));
            }

            PoseCaptureUtility.CaptureIntoPose(root, pose, included);
            return $"CAPTURED {pose.bones.Count} bones from '{skeletonRootHierarchyPath}' into {poseAssetPath}";
        }

        /// <summary>Applies a PoseAsset back onto a scene skeleton (inverse of CapturePose, undo-recorded).</summary>
        public static string ApplyPose(string skeletonRootHierarchyPath, string poseAssetPath)
        {
            Transform root = FindSceneTransform(skeletonRootHierarchyPath);
            if (root == null)
            {
                return $"ERROR scene object not found: {skeletonRootHierarchyPath}";
            }

            PoseAsset pose = AssetDatabase.LoadAssetAtPath<PoseAsset>(poseAssetPath);
            if (pose == null)
            {
                return $"ERROR pose asset not found: {poseAssetPath}";
            }

            PoseCaptureUtility.ApplyPoseToSkeleton(root, pose);
            return $"APPLIED {poseAssetPath} onto '{skeletonRootHierarchyPath}'";
        }

        // ─────────────────────────────── Graph authoring ───────────────────────────────

        /// <summary>Adds a stage and returns its generated id.</summary>
        public static string AddStage(string graphPath, string stageName)
        {
            PoseCombinationGraphAsset graph = LoadGraph(graphPath, out string error);
            if (graph == null)
            {
                return error;
            }

            PoseStage stage = new PoseStage
            {
                id = PoseCombinationGraphResolver.GenerateStageId(),
                name = stageName
            };
            graph.stages.Add(stage);
            Save(graph);
            return stage.id;
        }

        /// <summary>Adds a variant (with optional pose) to a stage and returns its generated id. stageRef = stage id OR stage name.</summary>
        public static string AddVariant(string graphPath, string stageRef, string variantName, string poseAssetPath = null)
        {
            PoseCombinationGraphAsset graph = LoadGraph(graphPath, out string error);
            if (graph == null)
            {
                return error;
            }

            PoseStage stage = graph.FindStageById(stageRef)
                ?? graph.stages.FirstOrDefault(s => s.name == stageRef);
            if (stage == null)
            {
                return $"ERROR stage not found: {stageRef}";
            }

            PoseVariant variant = new PoseVariant
            {
                id = PoseCombinationGraphResolver.GenerateVariantId(),
                name = variantName
            };

            if (!string.IsNullOrEmpty(poseAssetPath))
            {
                variant.pose = AssetDatabase.LoadAssetAtPath<PoseAsset>(poseAssetPath);
                if (variant.pose == null)
                {
                    return $"ERROR pose asset not found: {poseAssetPath}";
                }
            }

            stage.variants.Add(variant);
            Save(graph);
            return variant.id;
        }

        /// <summary>Assigns/replaces the PoseAsset on an existing variant. variantRef = variant id OR variant name.</summary>
        public static string SetVariantPose(string graphPath, string variantRef, string poseAssetPath)
        {
            PoseCombinationGraphAsset graph = LoadGraph(graphPath, out string error);
            if (graph == null)
            {
                return error;
            }

            PoseVariant variant = ResolveVariant(graph, variantRef);
            if (variant == null)
            {
                return $"ERROR variant not found: {variantRef}";
            }

            PoseAsset pose = AssetDatabase.LoadAssetAtPath<PoseAsset>(poseAssetPath);
            if (pose == null)
            {
                return $"ERROR pose asset not found: {poseAssetPath}";
            }

            variant.pose = pose;
            Save(graph);
            return $"OK variant '{variant.name}' -> {poseAssetPath}";
        }

        /// <summary>
        /// Creates a forward connection between two variants and returns its generated id.
        /// Variant refs accept id OR name. Falls back to the graph's defaultFeelPreset/defaultDuration
        /// when feelPresetPath is null / durationSeconds &lt;= 0.
        /// </summary>
        public static string Connect(string graphPath, string fromVariantRef, string toVariantRef, string feelPresetPath = null, float durationSeconds = -1f)
        {
            PoseCombinationGraphAsset graph = LoadGraph(graphPath, out string error);
            if (graph == null)
            {
                return error;
            }

            PoseVariant fromVariant = ResolveVariant(graph, fromVariantRef);
            PoseVariant toVariant = ResolveVariant(graph, toVariantRef);
            if (fromVariant == null || toVariant == null)
            {
                return $"ERROR variant not found: {(fromVariant == null ? fromVariantRef : toVariantRef)}";
            }

            string fromStageId = FindOwningStageId(graph, fromVariant.id);
            string toStageId = FindOwningStageId(graph, toVariant.id);
            if (graph.GetStageIndex(toStageId) <= graph.GetStageIndex(fromStageId))
            {
                return "ERROR connections must point to a later stage";
            }

            PoseConnection connection = PoseCombinationGraphResolver.CreateDefaultConnection(graph, fromStageId, fromVariant.id, toStageId, toVariant.id);

            if (!string.IsNullOrEmpty(feelPresetPath))
            {
                FeelPresetAsset preset = AssetDatabase.LoadAssetAtPath<FeelPresetAsset>(feelPresetPath);
                if (preset == null)
                {
                    return $"ERROR feel preset not found: {feelPresetPath}";
                }

                connection.segmentSettings.feelPreset = preset;
            }

            if (durationSeconds > 0f)
            {
                connection.segmentSettings.duration = durationSeconds;
            }

            graph.connections.Add(connection);
            Save(graph);
            return connection.id;
        }

        public static string SetConnectionEnabled(string graphPath, string connectionId, bool enabled)
        {
            PoseCombinationGraphAsset graph = LoadGraph(graphPath, out string error);
            if (graph == null)
            {
                return error;
            }

            PoseConnection connection = graph.FindConnectionById(connectionId);
            if (connection == null)
            {
                return $"ERROR connection not found: {connectionId}";
            }

            connection.enabled = enabled;
            Save(graph);
            return $"OK {connectionId} enabled={enabled}";
        }

        public static string SetDefaultFeel(string graphPath, string feelPresetPath, float defaultDurationSeconds)
        {
            PoseCombinationGraphAsset graph = LoadGraph(graphPath, out string error);
            if (graph == null)
            {
                return error;
            }

            FeelPresetAsset preset = AssetDatabase.LoadAssetAtPath<FeelPresetAsset>(feelPresetPath);
            if (preset == null)
            {
                return $"ERROR feel preset not found: {feelPresetPath}";
            }

            graph.defaultFeelPreset = preset;
            if (defaultDurationSeconds > 0f)
            {
                graph.defaultDuration = defaultDurationSeconds;
            }

            Save(graph);
            return $"OK default feel={feelPresetPath} duration={graph.defaultDuration}";
        }

        public static string SetGenerationOptions(
            string graphPath,
            string outputFolder,
            int frameRate = 30,
            bool generatePosition = true,
            bool generateRotation = true,
            bool generateScale = false,
            string clipNameTemplate = "{path}",
            bool overwriteExistingClips = true,
            int maxBatchCountGuard = 200)
        {
            PoseCombinationGraphAsset graph = LoadGraph(graphPath, out string error);
            if (graph == null)
            {
                return error;
            }

            PoseCombinationGenerationOptions options = graph.generationOptions;
            options.outputFolder = outputFolder;
            options.frameRate = frameRate;
            options.generatePosition = generatePosition;
            options.generateRotation = generateRotation;
            options.generateScale = generateScale;
            options.clipNameTemplate = clipNameTemplate;
            options.overwriteExistingClips = overwriteExistingClips;
            options.maxBatchCountGuard = maxBatchCountGuard;
            Save(graph);
            return $"OK outputFolder={outputFolder} fps={frameRate} template={clipNameTemplate} overwrite={overwriteExistingClips}";
        }

        // ─────────────────────────────── Queries ───────────────────────────────

        /// <summary>Dumps stages, variants (with ids + pose paths) and connections so a driver can address them.</summary>
        public static string DescribeGraph(string graphPath)
        {
            PoseCombinationGraphAsset graph = LoadGraph(graphPath, out string error);
            if (graph == null)
            {
                return error;
            }

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < graph.stages.Count; i++)
            {
                PoseStage stage = graph.stages[i];
                sb.AppendLine($"STAGE[{i}] id={stage.id} name='{stage.name}'");
                foreach (PoseVariant variant in stage.variants)
                {
                    sb.AppendLine($"  VARIANT id={variant.id} name='{variant.name}' pose={(variant.pose != null ? AssetDatabase.GetAssetPath(variant.pose) : "<none>")}");
                }
            }

            foreach (PoseConnection connection in graph.connections)
            {
                PoseVariant from = graph.FindVariantById(connection.fromVariantId);
                PoseVariant to = graph.FindVariantById(connection.toVariantId);
                string feel = connection.segmentSettings?.feelPreset != null ? connection.segmentSettings.feelPreset.name : "<none>";
                sb.AppendLine($"CONNECTION id={connection.id} '{from?.name}'->'{to?.name}' enabled={connection.enabled} feel={feel} duration={connection.segmentSettings?.duration}");
            }

            sb.AppendLine($"OPTIONS outputFolder={graph.generationOptions.outputFolder} fps={graph.generationOptions.frameRate} template={graph.generationOptions.clipNameTemplate} overwrite={graph.generationOptions.overwriteExistingClips}");
            return sb.ToString();
        }

        public static string ValidateGraph(string graphPath)
        {
            PoseCombinationGraphAsset graph = LoadGraph(graphPath, out string error);
            if (graph == null)
            {
                return error;
            }

            List<string> messages = PoseCombinationGraphResolver.ValidateGraph(graph);
            return messages.Count == 0 ? "VALID" : "INVALID\n" + string.Join("\n", messages);
        }

        /// <summary>Lists resolvable pose paths by index — the same indices GenerateClipForPath consumes.</summary>
        public static string ListValidPaths(string graphPath, bool onlyFinalStagePaths = true)
        {
            PoseCombinationGraphAsset graph = LoadGraph(graphPath, out string error);
            if (graph == null)
            {
                return error;
            }

            List<PoseCombinationPath> paths = ResolvePaths(graph, onlyFinalStagePaths);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"COUNT {paths.Count}");
            for (int i = 0; i < paths.Count; i++)
            {
                string exportable = paths[i].IsExportable(graph) ? "exportable" : "NOT-exportable";
                sb.AppendLine($"PATH[{i}] {paths[i].GetPathDisplayName(graph)} ({exportable})");
            }

            return sb.ToString();
        }

        // ─────────────────────────────── Generation ───────────────────────────────

        /// <summary>Batch-generates clips for every exportable path. Mirrors the window's batch export.</summary>
        public static string GenerateAllClips(string graphPath, string baseName = "anim", bool onlyFinalStagePaths = true)
        {
            PoseCombinationGraphAsset graph = LoadGraph(graphPath, out string error);
            if (graph == null)
            {
                return error;
            }

            List<PoseCombinationPath> paths = ResolvePaths(graph, onlyFinalStagePaths);
            int guard = graph.generationOptions.maxBatchCountGuard;
            if (guard > 0 && paths.Count > guard)
            {
                return $"ERROR path count {paths.Count} exceeds maxBatchCountGuard {guard} — raise the guard via SetGenerationOptions or prune the graph";
            }

            int generated = 0;
            List<string> failures = new List<string>();
            for (int i = 0; i < paths.Count; i++)
            {
                if (!paths[i].IsExportable(graph))
                {
                    failures.Add($"PATH[{i}] {paths[i].GetPathDisplayName(graph)}: {string.Join("; ", paths[i].GetValidationErrors(graph))}");
                    continue;
                }

                try
                {
                    PoseCombinationClipBuilder.BuildAndSaveClip(graph, paths[i], baseName, i);
                    generated++;
                }
                catch (System.Exception e)
                {
                    failures.Add($"PATH[{i}] {paths[i].GetPathDisplayName(graph)}: {e.Message}");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"GENERATED {generated}/{paths.Count} clips -> {graph.generationOptions.outputFolder}");
            foreach (string failure in failures)
            {
                sb.AppendLine($"SKIPPED {failure}");
            }

            return sb.ToString();
        }

        /// <summary>Generates one clip by path index (use ListValidPaths with the same onlyFinalStagePaths to see indices).</summary>
        public static string GenerateClipForPath(string graphPath, int pathIndex, string baseName = "anim", bool onlyFinalStagePaths = true)
        {
            PoseCombinationGraphAsset graph = LoadGraph(graphPath, out string error);
            if (graph == null)
            {
                return error;
            }

            List<PoseCombinationPath> paths = ResolvePaths(graph, onlyFinalStagePaths);
            if (pathIndex < 0 || pathIndex >= paths.Count)
            {
                return $"ERROR pathIndex {pathIndex} out of range 0..{paths.Count - 1}";
            }

            PoseCombinationPath path = paths[pathIndex];
            if (!path.IsExportable(graph))
            {
                return $"ERROR path not exportable: {string.Join("; ", path.GetValidationErrors(graph))}";
            }

            AnimationClip clip = PoseCombinationClipBuilder.BuildAndSaveClip(graph, path, baseName, pathIndex);
            return $"GENERATED {AssetDatabase.GetAssetPath(clip)}";
        }

        // ─────────────────────────────── Helpers ───────────────────────────────

        private static List<PoseCombinationPath> ResolvePaths(PoseCombinationGraphAsset graph, bool onlyFinalStagePaths)
        {
            List<PoseCombinationPath> paths = PoseCombinationGraphResolver.ResolveValidPaths(graph);
            return onlyFinalStagePaths
                ? PoseCombinationGraphResolver.FilterPathsReachingFinalStage(graph, paths)
                : paths;
        }

        private static PoseCombinationGraphAsset LoadGraph(string graphPath, out string error)
        {
            PoseCombinationGraphAsset graph = AssetDatabase.LoadAssetAtPath<PoseCombinationGraphAsset>(graphPath);
            error = graph == null ? $"ERROR graph asset not found: {graphPath}" : null;
            return graph;
        }

        private static void Save(PoseCombinationGraphAsset graph)
        {
            EditorUtility.SetDirty(graph);
            AssetDatabase.SaveAssets();
        }

        private static PoseVariant ResolveVariant(PoseCombinationGraphAsset graph, string variantRef)
        {
            PoseVariant byId = graph.FindVariantById(variantRef);
            if (byId != null)
            {
                return byId;
            }

            return graph.stages
                .SelectMany(s => s.variants)
                .FirstOrDefault(v => v.name == variantRef);
        }

        private static string FindOwningStageId(PoseCombinationGraphAsset graph, string variantId)
        {
            foreach (PoseStage stage in graph.stages)
            {
                if (stage.variants.Any(v => v.id == variantId))
                {
                    return stage.id;
                }
            }

            return null;
        }

        private static Transform FindSceneTransform(string hierarchyPath)
        {
            GameObject found = GameObject.Find(hierarchyPath);
            return found != null ? found.transform : null;
        }

        private static string GetFolder(string assetPath)
        {
            int lastSlash = assetPath.LastIndexOf('/');
            return lastSlash > 0 ? assetPath.Substring(0, lastSlash) : assetPath;
        }
    }
}
