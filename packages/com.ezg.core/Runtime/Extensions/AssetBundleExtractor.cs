#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Ezg.Core.Extensions
{
    public class AssetBundleExtractor : EditorWindow
    {
        #region Event Handlers

        /// <summary>
        ///     Draws the GUI elements for the AssetBundle Extractor editor window.
        /// </summary>
        private void OnGUI()
        {
            GUILayout.Label("AssetBundle Extractor", EditorStyles.boldLabel);

            GUILayout.Space(10);
            GUILayout.Label("Chọn thư mục chứa AssetBundles:");
            if (GUILayout.Button(string.IsNullOrEmpty(inputFolderPath) ? "Chọn Input Folder" : inputFolderPath))
            {
                var path = EditorUtility.OpenFolderPanel("Chọn thư mục chứa AssetBundles", "", "");
                if (!string.IsNullOrEmpty(path)) inputFolderPath = path;
            }

            GUILayout.Space(10);
            GUILayout.Label("Chọn thư mục để lưu Asset:");
            if (GUILayout.Button(string.IsNullOrEmpty(outputFolderPath) ? "Chọn Output Folder" : outputFolderPath))
            {
                var path = EditorUtility.OpenFolderPanel("Chọn thư mục xuất Asset", "", "");
                if (!string.IsNullOrEmpty(path)) outputFolderPath = path;
            }

            GUILayout.Space(20);
            if (GUILayout.Button("Extract ALL AssetBundles", GUILayout.Height(40)))
            {
                if (string.IsNullOrEmpty(inputFolderPath) || string.IsNullOrEmpty(outputFolderPath))
                {
                    EditorUtility.DisplayDialog("Lỗi", "Hãy chọn Input và Output Folder!", "OK");
                    return;
                }

                ExtractAssetBundles(inputFolderPath, outputFolderPath);
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Opens and displays the AssetBundle Extractor editor window.
        /// </summary>
        [MenuItem("Tools/AssetBundle Extractor")]
        public static void ShowWindow()
        {
            GetWindow<AssetBundleExtractor>("AssetBundle Extractor");
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Extracts all asset bundles from the input directory and exports the assets into the output directory as prefabs or
        ///     assets.
        /// </summary>
        /// <param name="inputFolder">The folder path containing the asset bundles.</param>
        /// <param name="outputFolder">The folder path to save the extracted assets.</param>
        private void ExtractAssetBundles(string inputFolder, string outputFolder)
        {
            var bundleFiles = Directory.GetFiles(inputFolder, "*", SearchOption.TopDirectoryOnly)
                .Where(f => !f.EndsWith(".manifest")).ToArray();

            foreach (var bundlePath in bundleFiles)
            {
                var bundleName = Path.GetFileName(bundlePath);
                var saveDir = Path.Combine(outputFolder, bundleName);
                Directory.CreateDirectory(saveDir);

                var bundle = AssetBundle.LoadFromFile(bundlePath);
                if (bundle == null)
                {
                    Debug.LogError($"Không load được AssetBundle: {bundleName}");
                    continue;
                }

                var assetNames = bundle.GetAllAssetNames();
                Debug.Log($"Extracting {assetNames.Length} assets from {bundleName}");

                foreach (var assetName in assetNames)
                {
                    var asset = bundle.LoadAsset(assetName);
                    if (asset != null)
                    {
                        var assetFileName = Path.GetFileNameWithoutExtension(assetName);
                        var localPath = Path.Combine(saveDir, assetFileName + ".prefab");

                        if (asset is GameObject)
                            PrefabUtility.SaveAsPrefabAsset((GameObject)asset, localPath);
                        else
                            AssetDatabase.CreateAsset(Instantiate(asset), localPath);
                    }
                }

                bundle.Unload(false);
            }

            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Hoàn tất", "Đã extract xong tất cả AssetBundles!", "OK");
        }

        #endregion

        #region Fields

        private string inputFolderPath = "";
        private string outputFolderPath = "";

        #endregion
    }

#endif
}