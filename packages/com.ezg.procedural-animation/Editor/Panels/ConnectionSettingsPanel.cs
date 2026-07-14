using System.Collections.Generic;
using Ezg.ProceduralAnimation;
using UnityEditor;
using UnityEngine;

namespace Ezg.ProceduralAnimation.Editor
{
    public partial class InbetweenAnimationGeneratorWindow
    {
        private void DrawConnectionSettingsPanel()
        {
            if (graphAsset == null || graphAsset.connections == null)
            {
                return;
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Connection Settings", EditorStyles.boldLabel);

            if (selectedConnectionIndex < 0 || selectedConnectionIndex >= graphAsset.connections.Count)
            {
                EditorGUILayout.HelpBox("Select a connection string between stages to edit its settings.", MessageType.Info);
                return;
            }

            PoseConnection connection = graphAsset.connections[selectedConnectionIndex];

            using (new EditorGUI.IndentLevelScope())
            {
                PoseVariant fromV = graphAsset.FindVariantById(connection.fromVariantId);
                PoseVariant toV = graphAsset.FindVariantById(connection.toVariantId);
                string fromName = fromV != null ? fromV.name : "?";
                string toName = toV != null ? toV.name : "?";
                EditorGUILayout.LabelField($"{fromName} → {toName}", EditorStyles.miniBoldLabel);

                EditorGUI.BeginChangeCheck();
                connection.enabled = EditorGUILayout.Toggle(new GUIContent("Enabled", "Bật/tắt kết nối giữa hai variant. Khi <b>tắt</b>, các path đi qua kết nối này sẽ bị bỏ qua khi export."), connection.enabled);
                if (EditorGUI.EndChangeCheck()) MarkDirty();

                if (connection.segmentSettings == null)
                {
                    EditorGUILayout.HelpBox("This connection is missing segment settings.", MessageType.Warning);
                    if (GUILayout.Button("Create Segment Settings", GUILayout.Height(22f)))
                    {
                        connection.segmentSettings = new InbetweenSegmentSettings();
                        MarkDirty();
                    }

                    return;
                }

                EditorGUI.BeginChangeCheck();
                connection.segmentSettings.feelPreset = (FeelPresetAsset)EditorGUILayout.ObjectField(
                    new GUIContent("Feel Preset", "<b>FeelPresetAsset</b> áp dụng cho segment chuyển động giữa hai variant. Định nghĩa đường cong easing và cảm giác chuyển động.\n<i>Ví dụ: EaseInOut, Bouncy, Snappy</i>"), connection.segmentSettings.feelPreset, typeof(FeelPresetAsset), false);
                if (EditorGUI.EndChangeCheck()) MarkDirty();

                EditorGUI.BeginChangeCheck();
                connection.segmentSettings.duration = EditorGUILayout.FloatField(new GUIContent("Duration", "Thời lượng (giây) của segment chuyển động giữa hai variant.\n<i>Ví dụ: 0.5 (nửa giây)</i>"), connection.segmentSettings.duration);
                if (EditorGUI.EndChangeCheck()) MarkDirty();

                if (GUILayout.Button(connection.enabled ? "Detach Connection" : "Attach Connection", GUILayout.Height(22f)))
                {
                    connection.enabled = !connection.enabled;
                    MarkDirty();
                }

                if (GUILayout.Button("Delete Connection", GUILayout.Height(22f)))
                {
                    DeleteSelectedConnection();
                    return;
                }
            }
        }
    }
}
