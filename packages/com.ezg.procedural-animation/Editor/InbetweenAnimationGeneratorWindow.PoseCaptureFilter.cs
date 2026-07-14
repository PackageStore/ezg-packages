using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Ezg.ProceduralAnimation.Editor
{
    public partial class InbetweenAnimationGeneratorWindow
    {
        private const float PoseCaptureBoneTableMaxHeight = 220f;

        private void DrawPoseCaptureFilter()
        {
            EditorGUILayout.Space(4f);
            poseCaptureFilterFoldout = EditorGUILayout.Foldout(poseCaptureFilterFoldout, "Pose Capture Filter", true);
            if (!poseCaptureFilterFoldout)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (skeletonRoot == null)
                {
                    EditorGUILayout.HelpBox("Assign a Skeleton Root to choose which bones are captured.", MessageType.Info);
                    return;
                }

                List<string> bonePaths = GetPoseCaptureBonePaths();
                EnsurePoseCaptureSelectionInitialized(bonePaths);
                List<string> filteredBonePaths = GetFilteredPoseCaptureBonePaths(bonePaths);

                int selectedCount = CountSelectedPoseCaptureBones(bonePaths);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Select All", GUILayout.Width(80f)))
                    {
                        SelectAllPoseCaptureBones(filteredBonePaths);
                    }

                    if (GUILayout.Button("Select None", GUILayout.Width(90f)))
                    {
                        selectedPoseCaptureBonePaths.Clear();
                        MarkPoseCaptureSettingsDirty();
                    }

                    if (GUILayout.Button("Invert", GUILayout.Width(60f)))
                    {
                        InvertPoseCaptureBoneSelection(bonePaths);
                    }

                    GUILayout.Label($"{selectedCount}/{bonePaths.Count} selected", EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                    EditorGUI.BeginChangeCheck();
                    rememberPoseCaptureSelection = EditorGUILayout.ToggleLeft("Remember Selection", rememberPoseCaptureSelection, GUILayout.Width(150f));
                    if (EditorGUI.EndChangeCheck())
                    {
                        MarkPoseCaptureSettingsDirty();
                    }
                }

                EditorGUI.BeginChangeCheck();
                poseCaptureBoneSearchFilter = EditorGUILayout.TextField("Search", poseCaptureBoneSearchFilter);
                if (EditorGUI.EndChangeCheck())
                {
                    MarkPoseCaptureSettingsDirty();
                }

                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    GUILayout.Label("Capture", GUILayout.Width(60f));
                    GUILayout.Label("Bone Path");
                }

                poseCaptureBoneScrollPosition = EditorGUILayout.BeginScrollView(
                    poseCaptureBoneScrollPosition,
                    GUILayout.MaxHeight(PoseCaptureBoneTableMaxHeight));

                for (int i = 0; i < filteredBonePaths.Count; i++)
                {
                    string bonePath = filteredBonePaths[i];
                    DrawPoseCaptureBoneRow(bonePath);
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private List<string> GetPoseCaptureBonePaths()
        {
            List<string> bonePaths = new List<string>();
            if (skeletonRoot == null)
            {
                return bonePaths;
            }

            Transform[] bones = skeletonRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < bones.Length; i++)
            {
                bonePaths.Add(BonePathUtility.GetRelativePath(skeletonRoot, bones[i]));
            }

            return bonePaths;
        }

        private List<string> GetFilteredPoseCaptureBonePaths(List<string> bonePaths)
        {
            List<string> filteredBonePaths = new List<string>();
            for (int i = 0; i < bonePaths.Count; i++)
            {
                string bonePath = bonePaths[i];
                if (MatchesPoseCaptureBoneSearch(bonePath))
                {
                    filteredBonePaths.Add(bonePath);
                }
            }

            return filteredBonePaths;
        }

        private void EnsurePoseCaptureSelectionInitialized(List<string> bonePaths)
        {
            if (poseCaptureBoneSelectionInitialized)
            {
                return;
            }

            SelectAllPoseCaptureBones(bonePaths, false);
            poseCaptureBoneSelectionInitialized = true;
        }

        private void DrawPoseCaptureBoneRow(string bonePath)
        {
            bool selected = selectedPoseCaptureBonePaths.Contains(bonePath);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                selected = EditorGUILayout.Toggle(selected, GUILayout.Width(60f));
                if (EditorGUI.EndChangeCheck())
                {
                    SetPoseCaptureBoneSelected(bonePath, selected);
                }

                EditorGUILayout.LabelField(GetPoseCaptureBoneDisplayPath(bonePath));
            }
        }

        private bool MatchesPoseCaptureBoneSearch(string bonePath)
        {
            if (string.IsNullOrWhiteSpace(poseCaptureBoneSearchFilter))
            {
                return true;
            }

            string displayPath = GetPoseCaptureBoneDisplayPath(bonePath);
            return displayPath.IndexOf(poseCaptureBoneSearchFilter, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string GetPoseCaptureBoneDisplayPath(string bonePath)
        {
            return string.IsNullOrEmpty(bonePath) ? "<Root>" : bonePath;
        }

        private int CountSelectedPoseCaptureBones(List<string> bonePaths)
        {
            int selectedCount = 0;
            for (int i = 0; i < bonePaths.Count; i++)
            {
                if (selectedPoseCaptureBonePaths.Contains(bonePaths[i]))
                {
                    selectedCount++;
                }
            }

            return selectedCount;
        }

        private void SelectAllPoseCaptureBones(List<string> bonePaths, bool markDirty = true)
        {
            selectedPoseCaptureBonePaths.Clear();
            selectedPoseCaptureBonePaths.AddRange(bonePaths);
            if (markDirty)
            {
                MarkPoseCaptureSettingsDirty();
            }
        }

        private void InvertPoseCaptureBoneSelection(List<string> bonePaths)
        {
            List<string> invertedSelection = new List<string>();
            for (int i = 0; i < bonePaths.Count; i++)
            {
                string bonePath = bonePaths[i];
                if (!selectedPoseCaptureBonePaths.Contains(bonePath))
                {
                    invertedSelection.Add(bonePath);
                }
            }

            selectedPoseCaptureBonePaths.Clear();
            selectedPoseCaptureBonePaths.AddRange(invertedSelection);
            MarkPoseCaptureSettingsDirty();
        }

        private void SetPoseCaptureBoneSelected(string bonePath, bool selected)
        {
            if (selected)
            {
                if (!selectedPoseCaptureBonePaths.Contains(bonePath))
                {
                    selectedPoseCaptureBonePaths.Add(bonePath);
                    MarkPoseCaptureSettingsDirty();
                }

                return;
            }

            selectedPoseCaptureBonePaths.Remove(bonePath);
            MarkPoseCaptureSettingsDirty();
        }

        private bool TryGetPoseCaptureIncludedBonePaths(out HashSet<string> includedBonePaths)
        {
            List<string> bonePaths = GetPoseCaptureBonePaths();
            EnsurePoseCaptureSelectionInitialized(bonePaths);

            includedBonePaths = new HashSet<string>();
            for (int i = 0; i < bonePaths.Count; i++)
            {
                string bonePath = bonePaths[i];
                if (selectedPoseCaptureBonePaths.Contains(bonePath))
                {
                    includedBonePaths.Add(bonePath);
                }
            }

            if (includedBonePaths.Count > 0)
            {
                return true;
            }

            EditorUtility.DisplayDialog("No Bones Selected", "Select at least one bone before capturing a pose.", "OK");
            return false;
        }

        private void ResetPoseCaptureSelectionAfterCapture()
        {
            if (rememberPoseCaptureSelection)
            {
                return;
            }

            SelectAllPoseCaptureBones(GetPoseCaptureBonePaths());
        }

        private void ResetPoseCaptureSelectionState()
        {
            selectedPoseCaptureBonePaths.Clear();
            poseCaptureBoneSelectionInitialized = false;
            MarkPoseCaptureSettingsDirty();
        }

        private void MarkPoseCaptureSettingsDirty()
        {
            if (graphAsset == null)
            {
                return;
            }

            MarkDirty(false);
        }
    }
}
