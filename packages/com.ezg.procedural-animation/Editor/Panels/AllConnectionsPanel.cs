using System.Collections.Generic;
using Ezg.ProceduralAnimation;
using UnityEditor;
using UnityEngine;

namespace Ezg.ProceduralAnimation.Editor
{
    public partial class InbetweenAnimationGeneratorWindow
    {
        private void DrawAllConnectionSettingsPanel()
        {
            if (graphAsset == null || graphAsset.connections == null)
            {
                return;
            }

            EditorGUILayout.Space(8f);
            allConnectionsFoldout = EditorGUILayout.Foldout(
                allConnectionsFoldout,
                InbetweenGeneratorContent.AllConnectionsFoldout,
                true);

            if (!allConnectionsFoldout)
            {
                return;
            }

            if (graphAsset.connections.Count == 0)
            {
                EditorGUILayout.HelpBox("No connections in this graph.", MessageType.Info);
                return;
            }

            DrawAllConnectionBatchControls();

            List<int> sortedConnectionIndices = GetSortedConnectionIndices();
            string previousGroup = null;

            using (new EditorGUI.IndentLevelScope())
            {
                for (int sortedIndex = 0; sortedIndex < sortedConnectionIndices.Count; sortedIndex++)
                {
                    int connectionIndex = sortedConnectionIndices[sortedIndex];
                    PoseConnection connection = graphAsset.connections[connectionIndex];
                    if (connection == null)
                    {
                        continue;
                    }

                    string groupName = GetConnectionStageGroupName(connection);
                    if (groupName != previousGroup)
                    {
                        EditorGUILayout.Space(4f);
                        EditorGUILayout.LabelField(groupName, EditorStyles.miniBoldLabel);
                        previousGroup = groupName;
                    }

                    DrawAllConnectionSettingsRow(connection, connectionIndex);
                }
            }
        }

        private void DrawAllConnectionBatchControls()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Batch Connection Settings", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField(
                    new GUIContent(
                        "Apply default connection values to existing connections.",
                        "Use the row buttons to overwrite only Feel Preset or only Duration."),
                    EditorStyles.miniLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    graphAsset.defaultFeelPreset = (FeelPresetAsset)EditorGUILayout.ObjectField(
                        new GUIContent(
                            "Default Feel Preset",
                            "<b>FeelPresetAsset</b> mặc định áp dụng cho tất cả kết nối mới tạo. Có thể batch apply cho các kết nối hiện tại ở đây.\n<i>Ví dụ: EaseInOut</i>"),
                        graphAsset.defaultFeelPreset,
                        typeof(FeelPresetAsset),
                        false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        MarkDirty();
                    }

                    using (new EditorGUI.DisabledScope(graphAsset.defaultFeelPreset == null))
                    {
                        if (GUILayout.Button(
                            new GUIContent(
                                "Apply Feel To All",
                                "Overwrite all connection Feel Presets with the default Feel Preset."),
                            EditorStyles.miniButton,
                            GUILayout.Width(130f)))
                        {
                            if (EditorUtility.DisplayDialog(
                                "Apply Feel Preset To All Connections",
                                $"Overwrite Feel Preset for all {graphAsset.connections.Count} connections?",
                                "Apply",
                                "Cancel"))
                            {
                                ApplyDefaultFeelPresetToAllConnections();
                            }
                        }
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    graphAsset.defaultDuration = EditorGUILayout.FloatField(
                        new GUIContent(
                            "Default Duration",
                            "Thời lượng mặc định (giây) cho tất cả kết nối mới tạo. Có thể batch apply cho các kết nối hiện tại ở đây.\n<i>Ví dụ: 0.3</i>"),
                        graphAsset.defaultDuration);
                    if (EditorGUI.EndChangeCheck())
                    {
                        MarkDirty();
                    }

                    using (new EditorGUI.DisabledScope(graphAsset.defaultDuration <= 0f))
                    {
                        if (GUILayout.Button(
                            new GUIContent(
                                "Apply Duration To All",
                                "Overwrite all connection Durations with the default Duration."),
                            EditorStyles.miniButton,
                            GUILayout.Width(130f)))
                        {
                            if (EditorUtility.DisplayDialog(
                                "Apply Duration To All Connections",
                                $"Overwrite Duration for all {graphAsset.connections.Count} connections?",
                                "Apply",
                                "Cancel"))
                            {
                                ApplyDefaultDurationToAllConnections();
                            }
                        }
                    }
                }

                if (graphAsset.defaultFeelPreset == null)
                {
                    EditorGUILayout.HelpBox("Set a Default Feel Preset before applying feel to all connections.", MessageType.Warning);
                }

                if (graphAsset.defaultDuration <= 0f)
                {
                    EditorGUILayout.HelpBox("Set a Default Duration greater than 0 before applying duration to all connections.", MessageType.Warning);
                }
            }
        }

        private void ApplyDefaultFeelPresetToAllConnections()
        {
            bool changed = false;

            for (int i = 0; i < graphAsset.connections.Count; i++)
            {
                PoseConnection connection = graphAsset.connections[i];
                if (connection == null)
                {
                    continue;
                }

                if (connection.segmentSettings == null)
                {
                    connection.segmentSettings = new InbetweenSegmentSettings();
                    changed = true;
                }

                if (connection.segmentSettings.feelPreset != graphAsset.defaultFeelPreset)
                {
                    connection.segmentSettings.feelPreset = graphAsset.defaultFeelPreset;
                    changed = true;
                }
            }

            if (changed)
            {
                MarkDirty();
            }
        }

        private void ApplyDefaultDurationToAllConnections()
        {
            bool changed = false;

            for (int i = 0; i < graphAsset.connections.Count; i++)
            {
                PoseConnection connection = graphAsset.connections[i];
                if (connection == null)
                {
                    continue;
                }

                if (connection.segmentSettings == null)
                {
                    connection.segmentSettings = new InbetweenSegmentSettings();
                    changed = true;
                }

                if (!Mathf.Approximately(connection.segmentSettings.duration, graphAsset.defaultDuration))
                {
                    connection.segmentSettings.duration = graphAsset.defaultDuration;
                    changed = true;
                }
            }

            if (changed)
            {
                MarkDirty();
            }
        }

        private List<int> GetSortedConnectionIndices()
        {
            List<int> indices = new List<int>();
            for (int i = 0; i < graphAsset.connections.Count; i++)
            {
                indices.Add(i);
            }

            indices.Sort((left, right) =>
            {
                PoseConnection leftConnection = graphAsset.connections[left];
                PoseConnection rightConnection = graphAsset.connections[right];
                int leftFromStage = leftConnection != null ? graphAsset.GetStageIndex(leftConnection.fromStageId) : int.MaxValue;
                int rightFromStage = rightConnection != null ? graphAsset.GetStageIndex(rightConnection.fromStageId) : int.MaxValue;
                if (leftFromStage != rightFromStage)
                {
                    return leftFromStage.CompareTo(rightFromStage);
                }

                int leftToStage = leftConnection != null ? graphAsset.GetStageIndex(leftConnection.toStageId) : int.MaxValue;
                int rightToStage = rightConnection != null ? graphAsset.GetStageIndex(rightConnection.toStageId) : int.MaxValue;
                if (leftToStage != rightToStage)
                {
                    return leftToStage.CompareTo(rightToStage);
                }

                string leftName = leftConnection != null ? GetConnectionDisplayName(leftConnection) : string.Empty;
                string rightName = rightConnection != null ? GetConnectionDisplayName(rightConnection) : string.Empty;
                return string.CompareOrdinal(leftName, rightName);
            });

            return indices;
        }

        private void DrawAllConnectionSettingsRow(PoseConnection connection, int connectionIndex)
        {
            bool hasValidSegmentSettings = connection.segmentSettings != null;
            bool hasFeelPreset = hasValidSegmentSettings && connection.segmentSettings.feelPreset != null;
            bool hasValidDuration = hasValidSegmentSettings && connection.segmentSettings.duration > 0f;
            bool hasMissingReferences = graphAsset.FindVariantById(connection.fromVariantId) == null ||
                graphAsset.FindVariantById(connection.toVariantId) == null;
            bool hasWarning = hasMissingReferences || !hasValidSegmentSettings || !hasFeelPreset || !hasValidDuration;

            Color previousColor = GUI.color;
            if (selectedConnectionIndex == connectionIndex)
            {
                GUI.color = Color.cyan;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUIContent connectionTitle = new GUIContent(GetConnectionDisplayName(connection), GetConnectionTooltip(connection));

                    EditorGUI.BeginChangeCheck();
                    connection.enabled = EditorGUILayout.Toggle(
                        new GUIContent(string.Empty, "Bật/tắt connection này. Khi <b>tắt</b>, path đi qua connection này sẽ không export được."),
                        connection.enabled,
                        GUILayout.Width(16f));
                    if (EditorGUI.EndChangeCheck())
                    {
                        MarkDirty();
                    }

                    EditorGUILayout.LabelField(connectionTitle, EditorStyles.miniBoldLabel, GUILayout.ExpandWidth(true));

                    if (GUILayout.Button(new GUIContent("Select", "Chọn và highlight connection này trên graph."), EditorStyles.miniButton, GUILayout.Width(48f)))
                    {
                        SelectConnection(connectionIndex);
                    }
                }

                GUI.color = previousColor;

                if (hasWarning)
                {
                    EditorGUILayout.HelpBox(GetConnectionWarningMessage(connection), MessageType.Warning);
                }

                if (!hasValidSegmentSettings)
                {
                    if (GUILayout.Button("Create Segment Settings", EditorStyles.miniButton))
                    {
                        connection.segmentSettings = new InbetweenSegmentSettings();
                        MarkDirty();
                    }

                    return;
                }

                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.LabelField(new GUIContent("Feel Preset", "<b>FeelPresetAsset</b> dùng cho riêng connection này."), EditorStyles.miniLabel);

                    EditorGUI.BeginChangeCheck();
                    connection.segmentSettings.feelPreset = (FeelPresetAsset)EditorGUILayout.ObjectField(
                        connection.segmentSettings.feelPreset,
                        typeof(FeelPresetAsset),
                        false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        MarkDirty();
                    }

                    EditorGUILayout.LabelField(new GUIContent("Duration (seconds)", "Thời lượng riêng của connection này, tính bằng giây.\n<i>Ví dụ: 0.5</i>"), EditorStyles.miniLabel);

                    EditorGUI.BeginChangeCheck();
                    connection.segmentSettings.duration = EditorGUILayout.FloatField(connection.segmentSettings.duration);
                    if (EditorGUI.EndChangeCheck())
                    {
                        MarkDirty();
                    }
                }
            }

            GUI.color = previousColor;
        }

        private string GetConnectionWarningMessage(PoseConnection connection)
        {
            List<string> warnings = new List<string>();
            if (graphAsset.FindVariantById(connection.fromVariantId) == null ||
                graphAsset.FindVariantById(connection.toVariantId) == null)
            {
                warnings.Add("Missing variant reference.");
            }

            if (connection.segmentSettings == null)
            {
                warnings.Add("Missing segment settings.");
                return string.Join("\n", warnings);
            }

            if (connection.segmentSettings.feelPreset == null)
            {
                warnings.Add("Missing Feel Preset.");
            }

            if (connection.segmentSettings.duration <= 0f)
            {
                warnings.Add("Duration must be greater than 0.");
            }

            return string.Join("\n", warnings);
        }

        private void SelectConnection(int connectionIndex)
        {
            selectedConnectionIndex = connectionIndex;
            if (connectionIndex >= 0 && connectionIndex < graphAsset.connections.Count)
            {
                selectedGraphVariantId = graphAsset.connections[connectionIndex].fromVariantId;
            }

            pendingLinkStageId = null;
            pendingLinkVariantId = null;
            StopPreview();
            Repaint();
        }

        private string GetConnectionStageGroupName(PoseConnection connection)
        {
            PoseStage fromStage = graphAsset.FindStageById(connection.fromStageId);
            PoseStage toStage = graphAsset.FindStageById(connection.toStageId);
            return $"{(fromStage != null ? fromStage.name : "?")} → {(toStage != null ? toStage.name : "?")}";
        }

        private string GetConnectionDisplayName(PoseConnection connection)
        {
            PoseVariant fromVariant = graphAsset.FindVariantById(connection.fromVariantId);
            PoseVariant toVariant = graphAsset.FindVariantById(connection.toVariantId);
            return $"{(fromVariant != null ? fromVariant.name : "?")} → {(toVariant != null ? toVariant.name : "?")}";
        }

        private string GetConnectionTooltip(PoseConnection connection)
        {
            string tooltip = "Click để chọn connection này trên graph.";
            if (connection.segmentSettings == null)
            {
                return tooltip + "\n<b>Lỗi:</b> thiếu segment settings.";
            }

            if (connection.segmentSettings.feelPreset == null)
            {
                tooltip += "\n<b>Lỗi:</b> thiếu Feel Preset.";
            }

            if (connection.segmentSettings.duration <= 0f)
            {
                tooltip += "\n<b>Lỗi:</b> Duration phải lớn hơn 0.";
            }

            return tooltip;
        }
    }
}
