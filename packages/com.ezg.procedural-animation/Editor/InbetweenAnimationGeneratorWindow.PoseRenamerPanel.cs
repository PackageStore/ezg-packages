using System.Collections.Generic;
using Ezg.ProceduralAnimation;
using UnityEditor;
using UnityEngine;

namespace Ezg.ProceduralAnimation.Editor
{
    public partial class InbetweenAnimationGeneratorWindow
    {
        [SerializeField]
        private List<PoseAsset> poseRenamerBatchPoseAssets = new List<PoseAsset>();
        [SerializeField]
        private string poseRenamerOldPath = string.Empty;
        [SerializeField]
        private string poseRenamerNewPath = string.Empty;

        private void DrawPoseRenamerTab()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Pose Renamer", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Add PoseAsset files, then replace matching bone path text in every pose entry.", MessageType.Info);

            SerializedObject windowObject = new SerializedObject(this);
            windowObject.Update();
            SerializedProperty posesProperty = windowObject.FindProperty(nameof(poseRenamerBatchPoseAssets));
            EditorGUILayout.PropertyField(posesProperty, new GUIContent("Pose Assets"), true);
            windowObject.ApplyModifiedProperties();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Selected Pose Assets"))
                {
                    AddSelectedPoseAssetsToPoseRenamer();
                }

                if (GUILayout.Button("Clear List"))
                {
                    poseRenamerBatchPoseAssets.Clear();
                }
            }

            poseRenamerOldPath = EditorGUILayout.TextField("Old Path", poseRenamerOldPath);
            poseRenamerNewPath = EditorGUILayout.TextField("New Path", poseRenamerNewPath);

            using (new EditorGUI.DisabledScope(poseRenamerBatchPoseAssets.Count == 0 || string.IsNullOrEmpty(poseRenamerOldPath)))
            {
                if (GUILayout.Button("Rename Pose Bone Paths", GUILayout.Height(28f)))
                {
                    RenamePoseBonePaths();
                }
            }
        }

        private void AddSelectedPoseAssetsToPoseRenamer()
        {
            int addedCount = 0;
            foreach (UnityEngine.Object selectedObject in Selection.objects)
            {
                PoseAsset pose = selectedObject as PoseAsset;
                if (pose == null)
                {
                    string path = AssetDatabase.GetAssetPath(selectedObject);
                    pose = AssetDatabase.LoadAssetAtPath<PoseAsset>(path);
                }

                if (pose == null || poseRenamerBatchPoseAssets.Contains(pose))
                {
                    continue;
                }

                poseRenamerBatchPoseAssets.Add(pose);
                addedCount++;
            }

            if (addedCount == 0)
            {
                Debug.LogWarning("No new PoseAsset files were selected.");
            }
        }

        private void RenamePoseBonePaths()
        {
            if (poseRenamerBatchPoseAssets.Count == 0 || string.IsNullOrEmpty(poseRenamerOldPath))
            {
                Debug.LogWarning("Add at least one PoseAsset and enter an old path value.");
                return;
            }

            int changedPoseCount = 0;
            int changedEntryCount = 0;

            foreach (PoseAsset pose in poseRenamerBatchPoseAssets)
            {
                if (pose == null || pose.bones == null)
                {
                    continue;
                }

                bool changedPose = false;
                for (int i = 0; i < pose.bones.Count; i++)
                {
                    BonePoseData bone = pose.bones[i];
                    if (bone == null || string.IsNullOrEmpty(bone.bonePath) || !bone.bonePath.Contains(poseRenamerOldPath))
                    {
                        continue;
                    }

                    if (!changedPose)
                    {
                        Undo.RecordObject(pose, "Rename Pose Bone Paths");
                        changedPose = true;
                    }

                    bone.bonePath = bone.bonePath.Replace(poseRenamerOldPath, poseRenamerNewPath);
                    changedEntryCount++;
                }

                if (!changedPose)
                {
                    continue;
                }

                EditorUtility.SetDirty(pose);
                changedPoseCount++;
            }

            if (changedPoseCount > 0)
            {
                AssetDatabase.SaveAssets();
            }

            Debug.Log($"Renamed {changedEntryCount} pose bone path entries across {changedPoseCount} PoseAsset files.");
        }
    }
}
