using System.Collections.Generic;
using Ezg.ProceduralAnimation;
using UnityEditor;
using UnityEngine;

namespace Ezg.ProceduralAnimation.Editor
{
    public partial class InbetweenAnimationGeneratorWindow
    {
        private void PickFeelPresetFolder()
        {
            if (InbetweenGeneratorPersistence.TryPickAssetsFolder("Feel Preset Folder", out string assetPath))
            {
                feelPresetFolder = assetPath;
            }
        }

        private void SetFeelPresetFolderFromAsset(DefaultAsset folderAsset)
        {
            if (!InbetweenGeneratorPersistence.TryGetFolderAssetPath(
                    folderAsset,
                    "Drag a folder from this project's Assets folder.",
                    out string assetPath))
            {
                return;
            }

            feelPresetFolder = assetPath;
        }

        private void PickPoseCaptureFolder()
        {
            if (InbetweenGeneratorPersistence.TryPickAssetsFolder("Pose Capture Folder", out string assetPath))
            {
                poseCaptureFolder = assetPath;
                MarkPoseCaptureSettingsDirty();
            }
        }

        private void SetPoseCaptureFolderFromAsset(DefaultAsset folderAsset)
        {
            if (!InbetweenGeneratorPersistence.TryGetFolderAssetPath(
                    folderAsset,
                    "Drag a folder from this project's Assets folder.",
                    out string assetPath))
            {
                return;
            }

            poseCaptureFolder = assetPath;
            MarkPoseCaptureSettingsDirty();
        }

        private void SetGraphOutputFolderFromAsset(DefaultAsset folderAsset)
        {
            if (folderAsset == null)
            {
                graphAsset.generationOptions.outputFolder = string.Empty;
                MarkDirty(false);
                return;
            }

            if (!InbetweenGeneratorPersistence.TryGetFolderAssetPath(
                    folderAsset,
                    "Drag a folder from this project's Assets folder.",
                    out string assetPath))
            {
                return;
            }

            graphAsset.generationOptions.outputFolder = assetPath;
            MarkDirty(false);
        }

        private bool HasValidPoseCaptureFolder()
        {
            return !string.IsNullOrEmpty(poseCaptureFolder) &&
                poseCaptureFolder.StartsWith("Assets") &&
                AssetDatabase.IsValidFolder(poseCaptureFolder);
        }

        private void DrawPoseCaptureFolderField()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DefaultAsset folderAsset = !string.IsNullOrEmpty(poseCaptureFolder)
                    ? AssetDatabase.LoadAssetAtPath<DefaultAsset>(poseCaptureFolder)
                    : null;

                EditorGUI.BeginChangeCheck();
                DefaultAsset newFolderAsset = (DefaultAsset)EditorGUILayout.ObjectField(
                    InbetweenGeneratorContent.PoseCaptureFolder,
                    folderAsset,
                    typeof(DefaultAsset),
                    false);
                if (EditorGUI.EndChangeCheck())
                {
                    SetPoseCaptureFolderFromAsset(newFolderAsset);
                }

                if (GUILayout.Button("Pick", GUILayout.Width(48f)))
                {
                    PickPoseCaptureFolder();
                }
            }
        }

        private void DrawGraphOutputFolderField()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DefaultAsset outputFolderAsset = !string.IsNullOrEmpty(graphAsset.generationOptions.outputFolder)
                    ? AssetDatabase.LoadAssetAtPath<DefaultAsset>(graphAsset.generationOptions.outputFolder)
                    : null;

                EditorGUI.BeginChangeCheck();
                DefaultAsset newOutputFolderAsset = (DefaultAsset)EditorGUILayout.ObjectField(
                    InbetweenGeneratorContent.OutputFolder,
                    outputFolderAsset,
                    typeof(DefaultAsset),
                    false);
                if (EditorGUI.EndChangeCheck())
                {
                    SetGraphOutputFolderFromAsset(newOutputFolderAsset);
                }

                if (GUILayout.Button("Pick", GUILayout.Width(48f)))
                {
                    PickGraphOutputFolder();
                }
            }
        }

        private void PickGraphOutputFolder()
        {
            if (InbetweenGeneratorPersistence.TryPickAssetsFolder("Output Folder", out string assetPath))
            {
                graphAsset.generationOptions.outputFolder = assetPath;
                MarkDirty(false);
            }
        }
    }
}
