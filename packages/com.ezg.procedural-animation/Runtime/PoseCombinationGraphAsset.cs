using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ezg.ProceduralAnimation
{
    [CreateAssetMenu(menuName = "EZG Technical Art/Procedural Animation/Pose Combination Graph")]
    public class PoseCombinationGraphAsset : ScriptableObject
    {
        public List<PoseStage> stages = new List<PoseStage>();
        public List<PoseConnection> connections = new List<PoseConnection>();
        public List<string> suppressedDefaultConnectionKeys = new List<string>();
        public PoseCombinationGenerationOptions generationOptions = new PoseCombinationGenerationOptions();
        public PoseCaptureGraphSettings poseCaptureSettings = new PoseCaptureGraphSettings();

        public FeelPresetAsset defaultFeelPreset;
        public float defaultDuration = 0.5f;
    }

    [Serializable]
    public class PoseCaptureGraphSettings
    {
        public string poseCaptureFolder = string.Empty;
        public bool rememberSelection;
        public string boneSearchFilter = string.Empty;
        public List<string> selectedBonePaths = new List<string>();
        public bool boneSelectionInitialized;
    }

    [Serializable]
    public class PoseStage
    {
        public string id;
        public string name = "Stage";
        public List<PoseVariant> variants = new List<PoseVariant>();
    }

    [Serializable]
    public class PoseVariant
    {
        public string id;
        public string name = "Variant";
        public PoseAsset pose;
        public Texture2D previewImage;
        public string notes;
    }

    [Serializable]
    public class PoseConnection
    {
        public string id;
        public string fromStageId;
        public string fromVariantId;
        public string toStageId;
        public string toVariantId;
        public bool enabled = true;
        public InbetweenSegmentSettings segmentSettings = new InbetweenSegmentSettings();
    }

    [Serializable]
    public class PoseCombinationPath
    {
        public List<int> stageIndices = new List<int>();
        public List<string> variantIds = new List<string>();
        public List<string> connectionIds = new List<string>();

        public List<PoseAsset> GetPoseAssets(PoseCombinationGraphAsset graph)
        {
            List<PoseAsset> result = new List<PoseAsset>();
            for (int i = 0; i < variantIds.Count; i++)
            {
                PoseVariant variant = graph.FindVariantById(variantIds[i]);
                result.Add(variant?.pose);
            }

            return result;
        }

        public List<InbetweenSegmentSettings> GetSegmentSettings(PoseCombinationGraphAsset graph)
        {
            List<InbetweenSegmentSettings> result = new List<InbetweenSegmentSettings>();
            for (int i = 0; i < connectionIds.Count; i++)
            {
                PoseConnection connection = graph.FindConnectionById(connectionIds[i]);
                result.Add(connection?.segmentSettings);
            }

            return result;
        }

        public string GetPathDisplayName(PoseCombinationGraphAsset graph)
        {
            List<string> parts = new List<string>();
            for (int i = 0; i < variantIds.Count; i++)
            {
                PoseVariant variant = graph.FindVariantById(variantIds[i]);
                parts.Add(variant != null ? variant.name : "?");
            }

            return string.Join("_", parts);
        }

        public bool HasAllPoses(PoseCombinationGraphAsset graph)
        {
            for (int i = 0; i < variantIds.Count; i++)
            {
                PoseVariant variant = graph.FindVariantById(variantIds[i]);
                if (variant == null || variant.pose == null)
                {
                    return false;
                }
            }

            return true;
        }

        public List<string> GetValidationErrors(PoseCombinationGraphAsset graph)
        {
            List<string> errors = new List<string>();

            for (int i = 0; i < variantIds.Count; i++)
            {
                PoseVariant variant = graph.FindVariantById(variantIds[i]);
                if (variant == null)
                {
                    errors.Add($"Variant '{variantIds[i]}' is missing from the graph.");
                    continue;
                }

                if (variant.pose == null)
                {
                    errors.Add($"Variant '{variant.name}' has no PoseAsset assigned.");
                }
            }

            for (int i = 0; i < connectionIds.Count; i++)
            {
                PoseConnection connection = graph.FindConnectionById(connectionIds[i]);
                if (connection == null)
                {
                    errors.Add($"Connection '{connectionIds[i]}' is missing from the graph.");
                    continue;
                }

                PoseVariant fromVariant = graph.FindVariantById(connection.fromVariantId);
                PoseVariant toVariant = graph.FindVariantById(connection.toVariantId);
                string connectionName = $"{(fromVariant != null ? fromVariant.name : "?")} -> {(toVariant != null ? toVariant.name : "?")}";

                if (!connection.enabled)
                {
                    errors.Add($"Connection '{connectionName}' is disabled.");
                }

                if (connection.segmentSettings == null)
                {
                    errors.Add($"Connection '{connectionName}' has no segment settings.");
                    continue;
                }

                if (connection.segmentSettings.feelPreset == null)
                {
                    errors.Add($"Connection '{connectionName}' has no Feel Preset assigned.");
                }

                if (connection.segmentSettings.duration <= 0f)
                {
                    errors.Add($"Connection '{connectionName}' duration must be greater than zero.");
                }
            }

            return errors;
        }

        public bool IsExportable(PoseCombinationGraphAsset graph)
        {
            return GetValidationErrors(graph).Count == 0;
        }

        public bool HasAllValidSegments(PoseCombinationGraphAsset graph)
        {
            for (int i = 0; i < connectionIds.Count; i++)
            {
                PoseConnection connection = graph.FindConnectionById(connectionIds[i]);
                if (connection == null
                    || connection.segmentSettings == null
                    || connection.segmentSettings.feelPreset == null
                    || connection.segmentSettings.duration <= 0f)
                {
                    return false;
                }
            }

            return true;
        }
    }

    public static class PoseCombinationGraphExtensions
    {
        public static PoseStage FindStageById(this PoseCombinationGraphAsset graph, string id)
        {
            for (int i = 0; i < graph.stages.Count; i++)
            {
                if (graph.stages[i].id == id)
                {
                    return graph.stages[i];
                }
            }

            return null;
        }

        public static PoseVariant FindVariantById(this PoseCombinationGraphAsset graph, string id)
        {
            for (int i = 0; i < graph.stages.Count; i++)
            {
                for (int j = 0; j < graph.stages[i].variants.Count; j++)
                {
                    if (graph.stages[i].variants[j].id == id)
                    {
                        return graph.stages[i].variants[j];
                    }
                }
            }

            return null;
        }

        public static PoseConnection FindConnectionById(this PoseCombinationGraphAsset graph, string id)
        {
            for (int i = 0; i < graph.connections.Count; i++)
            {
                if (graph.connections[i].id == id)
                {
                    return graph.connections[i];
                }
            }

            return null;
        }

        public static int GetStageIndex(this PoseCombinationGraphAsset graph, string stageId)
        {
            for (int i = 0; i < graph.stages.Count; i++)
            {
                if (graph.stages[i].id == stageId)
                {
                    return i;
                }
            }

            return -1;
        }

        public static int GetVariantIndex(this PoseCombinationGraphAsset graph, string variantId)
        {
            for (int i = 0; i < graph.stages.Count; i++)
            {
                for (int j = 0; j < graph.stages[i].variants.Count; j++)
                {
                    if (graph.stages[i].variants[j].id == variantId)
                    {
                        return j;
                    }
                }
            }

            return -1;
        }
    }
}
