using UnityEditor;
using UnityEngine;
using Ezg.ProceduralAnimation;

namespace Ezg.ProceduralAnimation.Editor
{
    internal static class InbetweenGeneratorPersistence
    {
        public static bool TryGetAssetsRelativePath(string absolutePath, out string assetPath)
        {
            assetPath = string.Empty;
            if (string.IsNullOrEmpty(absolutePath))
            {
                return false;
            }

            string dataPath = Application.dataPath.Replace("\\", "/");
            string normalized = absolutePath.Replace("\\", "/");
            if (!normalized.StartsWith(dataPath))
            {
                return false;
            }

            assetPath = "Assets" + normalized.Substring(dataPath.Length);
            return true;
        }

        public static bool TryPickAssetsFolder(string title, out string assetPath)
        {
            string absolute = EditorUtility.OpenFolderPanel(title, Application.dataPath, string.Empty);
            if (string.IsNullOrEmpty(absolute))
            {
                assetPath = string.Empty;
                return false;
            }

            if (TryGetAssetsRelativePath(absolute, out assetPath))
            {
                return true;
            }

            EditorUtility.DisplayDialog("Invalid Folder", "Choose a folder inside this project's Assets folder.", "OK");
            return false;
        }

        public static bool TryGetFolderAssetPath(DefaultAsset folderAsset, string invalidFolderMessage, out string assetPath)
        {
            assetPath = string.Empty;
            if (folderAsset == null)
            {
                return true;
            }

            assetPath = AssetDatabase.GetAssetPath(folderAsset);
            if (AssetDatabase.IsValidFolder(assetPath))
            {
                return true;
            }

            EditorUtility.DisplayDialog("Invalid Folder", invalidFolderMessage, "OK");
            return false;
        }

        public static PoseCombinationGraphAsset CreateGraphAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Pose Combination Graph",
                "PoseCombinationGraph",
                "asset",
                "Choose where to save the graph asset.",
                "Assets/ProceduralAnimation");

            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            PoseCombinationGraphAsset newGraph = ScriptableObject.CreateInstance<PoseCombinationGraphAsset>();
            AssetDatabase.CreateAsset(newGraph, path);
            AssetDatabase.SaveAssets();
            return newGraph;
        }

        public static PoseCombinationGraphAsset LoadGraphAsset()
        {
            string path = EditorUtility.OpenFilePanel("Load Pose Combination Graph", "Assets/ProceduralAnimation", "asset");
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            if (!TryGetAssetsRelativePath(path, out string assetPath))
            {
                EditorUtility.DisplayDialog("Invalid File", "Choose a file inside this project's Assets folder.", "OK");
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<PoseCombinationGraphAsset>(assetPath);
        }

        public static void SaveGraphAsset(PoseCombinationGraphAsset graphAsset)
        {
            if (graphAsset == null)
            {
                return;
            }

            EditorUtility.SetDirty(graphAsset);
            AssetDatabase.SaveAssets();
        }
    }
}
