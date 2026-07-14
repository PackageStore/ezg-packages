using System;
using System.Collections.Generic;
using System.Text;

namespace Ezg.ProceduralAnimation
{
    public static class PoseCombinationGraphResolver
    {
        public static string GenerateStageId()
        {
            return Guid.NewGuid().ToString("N");
        }

        public static string GenerateVariantId()
        {
            return Guid.NewGuid().ToString("N");
        }

        public static string GenerateConnectionId()
        {
            return Guid.NewGuid().ToString("N");
        }

        public static PoseConnection CreateDefaultConnection(PoseCombinationGraphAsset graph, string fromStageId, string fromVariantId, string toStageId, string toVariantId)
        {
            PoseConnection connection = new PoseConnection
            {
                id = GenerateConnectionId(),
                fromStageId = fromStageId,
                fromVariantId = fromVariantId,
                toStageId = toStageId,
                toVariantId = toVariantId,
                enabled = true
            };

            if (graph.defaultFeelPreset != null)
            {
                connection.segmentSettings.feelPreset = graph.defaultFeelPreset;
            }

            if (graph.defaultDuration > 0f)
            {
                connection.segmentSettings.duration = graph.defaultDuration;
            }

            return connection;
        }

        public static List<PoseCombinationPath> ResolveValidPaths(PoseCombinationGraphAsset graph)
        {
            List<PoseCombinationPath> results = new List<PoseCombinationPath>();

            if (graph.stages == null || graph.stages.Count == 0)
            {
                return results;
            }

            if (graph.stages.Count == 1)
            {
                PoseStage onlyStage = graph.stages[0];
                for (int i = 0; i < onlyStage.variants.Count; i++)
                {
                    PoseVariant variant = onlyStage.variants[i];
                    if (variant == null || string.IsNullOrEmpty(variant.id))
                    {
                        continue;
                    }

                    PoseCombinationPath path = new PoseCombinationPath();
                    path.stageIndices.Add(0);
                    path.variantIds.Add(variant.id);
                    results.Add(path);
                }

                return results;
            }

            List<string> currentPath = new List<string>();
            List<int> currentStages = new List<int>();
            List<string> currentConnections = new List<string>();

            PoseStage firstStage = graph.stages[0];
            for (int i = 0; i < firstStage.variants.Count; i++)
            {
                PoseVariant variant = firstStage.variants[i];
                if (variant == null || string.IsNullOrEmpty(variant.id))
                {
                    continue;
                }

                currentPath.Clear();
                currentPath.Add(variant.id);
                currentStages.Clear();
                currentStages.Add(0);
                currentConnections.Clear();
                ExpandPaths(graph, 0, variant.id, currentPath, currentStages, currentConnections, results);
            }

            results.Sort((a, b) =>
            {
                for (int i = 0; i < Math.Min(a.variantIds.Count, b.variantIds.Count); i++)
                {
                    int va = graph.GetVariantIndex(a.variantIds[i]);
                    int vb = graph.GetVariantIndex(b.variantIds[i]);
                    if (va != vb)
                    {
                        return va.CompareTo(vb);
                    }
                }

                return a.variantIds.Count.CompareTo(b.variantIds.Count);
            });

            return results;
        }

        private static void ExpandPaths(
            PoseCombinationGraphAsset graph,
            int fromStageIndex,
            string fromVariantId,
            List<string> currentVariantIds,
            List<int> currentStageIndices,
            List<string> currentConnectionIds,
            List<PoseCombinationPath> results)
        {
            if (fromStageIndex >= graph.stages.Count - 1)
            {
                PoseCombinationPath completePath = new PoseCombinationPath();
                completePath.stageIndices.AddRange(currentStageIndices);
                completePath.variantIds.AddRange(currentVariantIds);
                completePath.connectionIds.AddRange(currentConnectionIds);
                results.Add(completePath);
                return;
            }

            bool hasAnyEnabledConnection = false;

            List<string> outgoingConnections = FindEnabledOutgoingConnections(graph, fromStageIndex, fromVariantId);

            for (int i = 0; i < outgoingConnections.Count; i++)
            {
                PoseConnection connection = graph.FindConnectionById(outgoingConnections[i]);
                if (connection == null)
                {
                    continue;
                }

                PoseVariant toVariant = graph.FindVariantById(connection.toVariantId);
                if (toVariant == null || string.IsNullOrEmpty(toVariant.id))
                {
                    continue;
                }

                int toStageIndex = graph.GetStageIndex(connection.toStageId);
                if (toStageIndex <= fromStageIndex)
                {
                    continue;
                }

                hasAnyEnabledConnection = true;

                int beforeVariantCount = currentVariantIds.Count;
                int beforeStageCount = currentStageIndices.Count;
                int beforeConnectionCount = currentConnectionIds.Count;
                currentVariantIds.Add(toVariant.id);
                currentStageIndices.Add(toStageIndex);
                currentConnectionIds.Add(connection.id);

                ExpandPaths(graph, toStageIndex, toVariant.id, currentVariantIds, currentStageIndices, currentConnectionIds, results);

                while (currentVariantIds.Count > beforeVariantCount)
                {
                    currentVariantIds.RemoveAt(currentVariantIds.Count - 1);
                }

                while (currentStageIndices.Count > beforeStageCount)
                {
                    currentStageIndices.RemoveAt(currentStageIndices.Count - 1);
                }

                while (currentConnectionIds.Count > beforeConnectionCount)
                {
                    currentConnectionIds.RemoveAt(currentConnectionIds.Count - 1);
                }
            }

            if (!hasAnyEnabledConnection)
            {
                PoseCombinationPath deadEndPath = new PoseCombinationPath();
                deadEndPath.stageIndices.AddRange(currentStageIndices);
                deadEndPath.variantIds.AddRange(currentVariantIds);
                deadEndPath.connectionIds.AddRange(currentConnectionIds);
                results.Add(deadEndPath);
            }
        }

        private static List<string> FindEnabledOutgoingConnections(PoseCombinationGraphAsset graph, int fromStageIndex, string fromVariantId)
        {
            List<string> result = new List<string>();

            if (graph.connections == null)
            {
                return result;
            }

            for (int i = 0; i < graph.connections.Count; i++)
            {
                PoseConnection connection = graph.connections[i];
                if (connection == null || !connection.enabled)
                {
                    continue;
                }

                if (connection.fromVariantId != fromVariantId)
                {
                    continue;
                }

                PoseVariant toVariant = graph.FindVariantById(connection.toVariantId);
                if (toVariant == null)
                {
                    continue;
                }

                int actualFromStageIndex = graph.GetStageIndex(connection.fromStageId);
                int actualToStageIndex = graph.GetStageIndex(connection.toStageId);
                if (actualFromStageIndex != fromStageIndex || actualToStageIndex <= fromStageIndex)
                {
                    continue;
                }

                result.Add(connection.id);
            }

            result.Sort((a, b) =>
            {
                PoseConnection connectionA = graph.FindConnectionById(a);
                PoseConnection connectionB = graph.FindConnectionById(b);
                int stageA = graph.GetStageIndex(connectionA != null ? connectionA.toStageId : string.Empty);
                int stageB = graph.GetStageIndex(connectionB != null ? connectionB.toStageId : string.Empty);
                if (stageA != stageB)
                {
                    return stageA.CompareTo(stageB);
                }

                int variantA = graph.GetVariantIndex(connectionA != null ? connectionA.toVariantId : string.Empty);
                int variantB = graph.GetVariantIndex(connectionB != null ? connectionB.toVariantId : string.Empty);
                return variantA.CompareTo(variantB);
            });

            return result;
        }

        public static List<PoseCombinationPath> FilterPathsReachingFinalStage(PoseCombinationGraphAsset graph, List<PoseCombinationPath> paths)
        {
            int lastStageIndex = graph.stages.Count - 1;
            List<PoseCombinationPath> result = new List<PoseCombinationPath>();

            for (int i = 0; i < paths.Count; i++)
            {
                PoseCombinationPath path = paths[i];
                if (path.stageIndices.Count > 0 && path.stageIndices[path.stageIndices.Count - 1] == lastStageIndex)
                {
                    result.Add(path);
                }
            }

            return result;
        }

        public static List<string> ValidateGraph(PoseCombinationGraphAsset graph)
        {
            List<string> messages = new List<string>();

            if (graph == null)
            {
                messages.Add("Graph asset is null.");
                return messages;
            }

            if (graph.stages == null || graph.stages.Count == 0)
            {
                messages.Add("Graph has no stages.");
                return messages;
            }

            for (int i = 0; i < graph.stages.Count; i++)
            {
                PoseStage stage = graph.stages[i];
                if (string.IsNullOrEmpty(stage.id))
                {
                    messages.Add($"Stage {i + 1} has no ID.");
                    continue;
                }

                if (stage.variants == null || stage.variants.Count == 0)
                {
                    messages.Add($"Stage '{stage.name}' (stage {i + 1}) has no variants.");
                    continue;
                }

                for (int j = 0; j < stage.variants.Count; j++)
                {
                    PoseVariant variant = stage.variants[j];
                    if (variant == null)
                    {
                        messages.Add($"Stage '{stage.name}' variant {j + 1} is null.");
                        continue;
                    }

                    if (string.IsNullOrEmpty(variant.id))
                    {
                        messages.Add($"Stage '{stage.name}' variant {j + 1} has no ID.");
                    }

                    if (variant.pose == null)
                    {
                        messages.Add($"Variant '{variant.name}' in stage '{stage.name}' has no PoseAsset assigned.");
                    }
                }
            }

            for (int c = 0; c < graph.connections.Count; c++)
            {
                PoseConnection connection = graph.connections[c];
                if (connection == null)
                {
                    messages.Add($"Connection {c + 1} is null.");
                    continue;
                }

                if (string.IsNullOrEmpty(connection.id))
                {
                    messages.Add($"Connection {c + 1} has no ID.");
                }

                if (graph.FindVariantById(connection.fromVariantId) == null)
                {
                    messages.Add($"Connection '{connection.id}' references missing 'from' variant '{connection.fromVariantId}'.");
                }

                if (graph.FindVariantById(connection.toVariantId) == null)
                {
                    messages.Add($"Connection '{connection.id}' references missing 'to' variant '{connection.toVariantId}'.");
                }

                int fromStageIndex = graph.GetStageIndex(connection.fromStageId);
                int toStageIndex = graph.GetStageIndex(connection.toStageId);
                if (fromStageIndex >= 0 && toStageIndex >= 0 && toStageIndex <= fromStageIndex)
                {
                    messages.Add($"Connection '{connection.id}' must point to a later stage.");
                }

                if (connection.segmentSettings == null)
                {
                    messages.Add($"Connection '{connection.id}' has no segment settings.");
                }
                else if (connection.segmentSettings.feelPreset == null)
                {
                    messages.Add($"Connection '{connection.id}' has no feel preset assigned.");
                }

                if (connection.segmentSettings != null && connection.segmentSettings.duration <= 0f)
                {
                    messages.Add($"Connection '{connection.id}' has invalid duration ({connection.segmentSettings.duration}).");
                }
            }

            List<PoseCombinationPath> allPaths = ResolveValidPaths(graph);
            List<PoseCombinationPath> finalPaths = FilterPathsReachingFinalStage(graph, allPaths);

            if (finalPaths.Count == 0)
            {
                messages.Add("No valid paths reach the final stage. Check that enabled forward connections exist.");
            }

            return messages;
        }

        public static InbetweenGenerationSettings ConvertPathToGenerationSettings(
            PoseCombinationGraphAsset graph,
            PoseCombinationPath path,
            string clipName)
        {
            if (graph == null || path == null)
            {
                throw new ArgumentNullException();
            }

            InbetweenGenerationSettings settings = new InbetweenGenerationSettings();
            PoseCombinationGenerationOptions options = graph.generationOptions ?? new PoseCombinationGenerationOptions();

            settings.poses = path.GetPoseAssets(graph);
            settings.segments = path.GetSegmentSettings(graph);

            settings.frameRate = options.frameRate;
            settings.generatePosition = options.generatePosition;
            settings.generateRotation = options.generateRotation;
            settings.generateScale = options.generateScale;
            settings.clipName = clipName;
            settings.outputFolder = options.outputFolder;

            return settings;
        }

        public static string ResolveClipName(PoseCombinationGraphAsset graph, PoseCombinationPath path, string baseName, int batchIndex)
        {
            if (graph == null || graph.generationOptions == null)
            {
                return $"{baseName}_{path.GetPathDisplayName(graph)}";
            }

            string template = graph.generationOptions.clipNameTemplate ?? "{base}_{path}";
            string pathName = path.GetPathDisplayName(graph);

            StringBuilder sb = new StringBuilder(template);
            sb.Replace("{base}", baseName);
            sb.Replace("{path}", pathName);
            sb.Replace("{index}", (batchIndex + 1).ToString());
            sb.Replace("{index0}", batchIndex.ToString());

            return sb.ToString();
        }
    }
}
