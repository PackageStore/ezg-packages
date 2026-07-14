using System.Collections.Generic;
using Ezg.ProceduralAnimation;
using UnityEditor;
using UnityEngine;

namespace Ezg.ProceduralAnimation.Editor
{
    public partial class InbetweenAnimationGeneratorWindow
    {
        private void DrawValidPathResults()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Valid Paths", EditorStyles.boldLabel);

            int displayCount = Mathf.Min(finalPaths.Count, 500);
            int exportableCount = 0;
            for (int i = 0; i < finalPaths.Count; i++)
            {
                if (finalPaths[i].IsExportable(graphAsset))
                {
                    exportableCount++;
                }
            }

            EditorGUILayout.LabelField($"Exportable: {exportableCount} / Total Connected: {finalPaths.Count}", EditorStyles.miniLabel);

            if (finalPaths.Count > graphAsset.generationOptions.maxBatchCountGuard)
            {
                EditorGUILayout.HelpBox(
                    $"Path count ({finalPaths.Count}) exceeds batch guard ({graphAsset.generationOptions.maxBatchCountGuard}). Consider reducing connections.",
                    MessageType.Warning);
            }

            pathSearchFilter = EditorGUILayout.TextField(new GUIContent("Filter", "Lọc danh sách path theo tên hiển thị. Không phân biệt hoa thường.\n<i>Ví dụ: Idle_Attack để chỉ hiện path có chứa Idle_Attack</i>"), pathSearchFilter);

            if (finalPaths.Count > 0 && (pathSelection.Length != finalPaths.Count))
            {
                pathSelection = new bool[finalPaths.Count];
            }

            resultsScrollPosition = EditorGUILayout.BeginScrollView(resultsScrollPosition, GUILayout.Height(150f));

            for (int i = 0; i < displayCount; i++)
            {
                PoseCombinationPath path = finalPaths[i];
                string displayName = path.GetPathDisplayName(graphAsset);

                if (!string.IsNullOrEmpty(pathSearchFilter) && !displayName.ToLowerInvariant().Contains(pathSearchFilter.ToLowerInvariant()))
                {
                    continue;
                }

                Color bgColor = i == selectedPathIndex ? new Color(0.3f, 0.5f, 0.8f, 0.3f) : Color.clear;
                bool valid = path.IsExportable(graphAsset);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (i < pathSelection.Length)
                    {
                        if (!valid)
                        {
                            pathSelection[i] = false;
                        }

                        using (new EditorGUI.DisabledScope(!valid))
                        {
                            pathSelection[i] = EditorGUILayout.Toggle(pathSelection[i], GUILayout.Width(20f));
                        }
                    }

                    if (!valid)
                    {
                        GUI.color = Color.red;
                    }
                    else if (i == selectedPathIndex)
                    {
                        GUI.color = Color.cyan;
                    }

                    string buttonLabel = valid ? displayName : $"{displayName} (invalid)";
                    if (GUILayout.Button(buttonLabel, EditorStyles.label))
                    {
                        selectedPathIndex = i;
                        StopPreview();
                    }

                    GUI.color = Color.white;
                }
            }

            EditorGUILayout.EndScrollView();

            if (selectedPathIndex >= 0 && selectedPathIndex < finalPaths.Count)
            {
                List<string> selectedErrors = finalPaths[selectedPathIndex].GetValidationErrors(graphAsset);
                if (selectedErrors.Count > 0)
                {
                    EditorGUILayout.HelpBox(string.Join("\n", selectedErrors), MessageType.Warning);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select All"))
                {
                    for (int i = 0; i < pathSelection.Length; i++)
                    {
                        pathSelection[i] = i < finalPaths.Count && finalPaths[i].IsExportable(graphAsset);
                    }
                }

                if (GUILayout.Button("Select None"))
                {
                    for (int i = 0; i < pathSelection.Length; i++) pathSelection[i] = false;
                }
            }
        }
    }
}
