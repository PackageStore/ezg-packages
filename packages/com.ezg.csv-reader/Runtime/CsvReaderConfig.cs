using System.Collections.Generic;
using UnityEngine;

namespace Ezg.Package.CsvReader
{
    /// <summary>
    ///     Project-specific configuration cho CSV import pipeline. Mỗi project tạo 1 asset
    ///     <see cref="CsvReaderConfig" /> riêng để custom path/suffix mà không phải sửa code module.
    ///     Resolver: <see cref="CsvReaderSettings" /> (editor-only lookup + fallback default).
    /// </summary>
    public class CsvReaderConfig : ScriptableObject
    {
        [Header("Feature-local CsvConfig pattern")]
        [Tooltip("Tên thư mục đánh dấu CSV feature-local.")]
        public string csvConfigFolderName = "CsvConfig";

        [Tooltip("Tên thư mục Resources cạnh CsvConfig để chứa asset sinh ra.")]
        public string resourcesFolderName = "Resources";

        [Header("Import pipeline naming")]
        [Tooltip("Hậu tố tên class Collection (Foo -> FooCollection).")]
        public string collectionSuffix = "Collection";

        [Tooltip("Hậu tố tên class Model (Foo -> FooModel).")]
        public string dataSuffix = "Model";

        [Tooltip("Tên file cache lưu MD5 của các CSV (đặt trong Assets/).")]
        public string cachedFileName = "FileInfo.txt";

        [Tooltip("Các prefix collection dùng chung 1 model class (Skill_1, LevelReward_2...).")]
        public List<string> sharedModelPrefixes = new()
        {
            "Skill_", "Passive_", "ItemScaleStage", "LevelReward_", "LevelOrder_", "UnlockFeature_"
        };

        [Header("Generated CsvAssetDir class")]
        [Tooltip("Thư mục (tương đối project root) ghi file class hằng số sinh ra.")]
        public string generatedClassDirectory = "/Assets/_Project/Features/_Shared/GameData/";

        [Tooltip("Tên class hằng số sinh ra.")]
        public string generatedClassName = "CsvAssetDir";

        [Header("DataManager generation (Project setup)")]
        [Tooltip("Thư mục (tương đối Application.dataPath / thư mục Assets) chứa GenDataManager.cs + " +
                 "DataManager.Generated.cs. Phải bắt đầu và kết thúc bằng '/'.")]
        public string dataManagerDirectory = "/_Project/Features/_Shared/GameData/";

        [Tooltip("Namespace gán cho file GenDataManager.cs sinh ra bởi menu Project setup.")]
        public string dataManagerNamespace = "Ezg.Feature.DataConfig";
    }
}
