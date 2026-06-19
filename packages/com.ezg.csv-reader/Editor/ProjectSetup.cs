#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Ezg.Package.CsvReader
{
    /// <summary>
    ///     One-shot project bootstrap cho CSV Reader pipeline. Menu chuột phải <c>Create/Ezg/Csv Reader/Project config</c>:
    ///     (1) tạo asset <see cref="CsvReaderConfig" /> nếu project chưa có, và
    ///     (2) sinh file <c>GenDataManager.cs</c> vào thư mục cấu hình (<see cref="CsvReaderConfig.dataManagerDirectory" />)
    ///     với namespace lấy từ <see cref="CsvReaderConfig.dataManagerNamespace" />.
    ///     Mọi path/namespace đều externalize qua CsvReaderConfig — package không hardcode path của game nào.
    /// </summary>
    public static class ProjectSetup
    {
        #region Fields

        private const string MENU_PATH = "Assets/Create/Ezg/Csv Reader/Project config";
        private const string GEN_DATA_MANAGER_FILE = "GenDataManager.cs";

        private const string TEMPLATE_RELATIVE_PATH =
            "Packages/com.ezg.csv-reader/Editor/Templates/GenDataManager.cs.txt";

        private const string NAMESPACE_TOKEN = "__NAMESPACE__";
        private const string SCRIPT_PATH_TOKEN = "__SCRIPTPATH__";

        #endregion

        #region Public Methods

        /// <summary>
        ///     Entry point của menu chuột phải Create/Ezg/Csv Reader/Project config.
        /// </summary>
        [MenuItem(MENU_PATH)]
        public static void RunProjectSetup()
        {
            var config = ResolveOrCreateConfig();
            if (config == null)
                return; // user huỷ ở dialog tạo config

            GenerateGenDataManager(config);

            AssetDatabase.Refresh();
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Tìm <see cref="CsvReaderConfig" /> hiện có; nếu chưa có thì cho user chọn nơi tạo
        ///     (giữ package reusable — không hardcode path của bất kỳ game nào).
        /// </summary>
        /// <returns>Config đã resolve/tạo, hoặc null nếu user huỷ.</returns>
        private static CsvReaderConfig ResolveOrCreateConfig()
        {
            var guids = AssetDatabase.FindAssets($"t:{nameof(CsvReaderConfig)}");
            if (guids.Length > 0)
            {
                var existingPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                if (guids.Length > 1)
                    Debug.LogWarning(
                        $"[CsvReader] Tìm thấy {guids.Length} CsvReaderConfig. Đang dùng: {existingPath}");
                return AssetDatabase.LoadAssetAtPath<CsvReaderConfig>(existingPath);
            }

            var targetPath = EditorUtility.SaveFilePanelInProject(
                "Create CsvReaderConfig",
                "CsvReaderConfig",
                "asset",
                "Chọn thư mục (nên là một Resources/) để tạo asset CsvReaderConfig.");
            if (string.IsNullOrEmpty(targetPath))
                return null;

            var config = ScriptableObject.CreateInstance<CsvReaderConfig>();
            AssetDatabase.CreateAsset(config, targetPath);
            AssetDatabase.SaveAssets();
            CsvReaderSettings.Invalidate();
            Debug.Log($"[CsvReader] Created CsvReaderConfig at {targetPath}");
            return config;
        }

        /// <summary>
        ///     Sinh file GenDataManager.cs từ template (.txt) trong package, thay token namespace + script path
        ///     bằng giá trị trong config. Nếu file đã tồn tại thì hỏi xác nhận ghi đè.
        /// </summary>
        /// <param name="config">Config cung cấp thư mục đích + namespace.</param>
        private static void GenerateGenDataManager(CsvReaderConfig config)
        {
            var dir = config.dataManagerDirectory;
            var ns = config.dataManagerNamespace;
            if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(ns))
            {
                Debug.LogError(
                    "[CsvReader] dataManagerDirectory / dataManagerNamespace chưa được cấu hình trong CsvReaderConfig.");
                return;
            }

            var templateFull = Path.GetFullPath(TEMPLATE_RELATIVE_PATH);
            if (!File.Exists(templateFull))
            {
                Debug.LogError($"[CsvReader] Không tìm thấy template: {TEMPLATE_RELATIVE_PATH}");
                return;
            }

            var outFolder = Application.dataPath + dir;
            var outFile = Path.Combine(outFolder, GEN_DATA_MANAGER_FILE);

            if (File.Exists(outFile))
            {
                var overwrite = EditorUtility.DisplayDialog(
                    "GenDataManager.cs đã tồn tại",
                    $"File đã tồn tại:\n{outFile}\n\nGhi đè?",
                    "Ghi đè",
                    "Huỷ");
                if (!overwrite)
                {
                    Debug.Log("[CsvReader] Bỏ qua sinh GenDataManager.cs (user huỷ ghi đè).");
                    return;
                }
            }

            var content = File.ReadAllText(templateFull)
                .Replace(NAMESPACE_TOKEN, ns)
                .Replace(SCRIPT_PATH_TOKEN, dir);

            if (!Directory.Exists(outFolder))
                Directory.CreateDirectory(outFolder);

            File.WriteAllText(outFile, content);
            Debug.Log($"[CsvReader] Generated {GEN_DATA_MANAGER_FILE} at {outFile}");
        }

        #endregion
    }
}
#endif
