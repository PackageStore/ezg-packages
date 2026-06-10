using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Ezg.Package.CsvReader
{
#if UNITY_EDITOR

    public class BasePostProcessor : AssetPostprocessor
    {
        #region Event Handlers

        /// <summary>
        ///     Responds to changes in project assets, automatically re-importing changed CSVs or deleting deleted assets.
        /// </summary>
        /// <param name="importedAssets">Imported assets path array.</param>
        /// <param name="deletedAssets">Deleted assets path array.</param>
        /// <param name="movedAssets">Moved assets path array.</param>
        /// <param name="movedFromAssetPaths">Previous path of moved assets.</param>
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (_isImporting) return;

            var importedCsv = importedAssets.Where(IsManagedCsvPath).ToArray();
            var deletedCsv = deletedAssets.Where(IsManagedCsvPath).ToArray();
            var movedCsv = movedAssets.Where(IsManagedCsvPath).ToArray();
            var movedFromCsv = movedFromAssetPaths.Where(IsManagedCsvPath).ToArray();

            var csvChanged = importedCsv.Length > 0 || deletedCsv.Length > 0
                                                    || movedCsv.Length > 0 || movedFromCsv.Length > 0;

            if (!csvChanged) return;

            _isImporting = true;
            try
            {
                foreach (var deletedCsvPath in deletedCsv.Concat(movedFromCsv).Distinct())
                    DeleteGeneratedAsset(deletedCsvPath);

                var csvList = importedCsv.Concat(movedCsv).Distinct().ToArray();
                if (csvList.Length > 0)
                    ImportData(csvList, false);

                AssetDatabase.SaveAssets();
                AssetPathGenerate.Generate();
            }
            finally
            {
                _isImporting = false;
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Generates a custom ScriptableObject asset and imports raw CSV string data into it.
        /// </summary>
        /// <param name="data">The raw CSV text content.</param>
        /// <param name="assetfile">The relative path to save the generated asset.</param>
        /// <param name="classCollection">The target ScriptableObject type to instantiate.</param>
        /// <param name="saveAssets">If true, saves assets using AssetDatabase.SaveAssets().</param>
        public static void GenCustomAsset(string data, string assetfile, Type classCollection, bool saveAssets = true)
        {
            var asset = CreateOrLoadAsset(assetfile, classCollection, saveAssets);
            ((ICsvCustomData)asset).ImportData(data);
            EditorUtility.SetDirty((Object)asset);
            if (saveAssets) AssetDatabase.SaveAssets();
        }

        #endregion

        #region Fields

        // Project-specific naming đọc từ CsvReaderConfig (xem CsvReaderSettings) để mỗi project custom riêng.
        public static string COLLECTION_SUFFIX => CsvReaderSettings.Current.collectionSuffix;
        public static string DATA_SUFFIX => CsvReaderSettings.Current.dataSuffix;

        /// <summary>
        ///     Các collection dùng chung 1 model class (phần đuôi là số — ví dụ Skill_1, LevelReward_2).
        /// </summary>
        private static List<string> EXCEPTION_LIST => CsvReaderSettings.Current.sharedModelPrefixes;

        private static bool _isImporting;

        #endregion

        #region Private Methods

        /// <summary>
        ///     Helper method to find a Type in any currently loaded assembly.
        /// </summary>
        /// <param name="typeName">The name of the type to find.</param>
        /// <returns>The found Type, or null if it cannot be found.</returns>
        private static Type FindTypeInAllAssemblies(string typeName)
        {
            var type = Type.GetType(typeName);
            if (type != null) return type;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType(typeName);
                if (type != null) return type;
            }

            return null;
        }

        /// <summary>
        ///     Processes and imports the CSV files at the specified paths.
        /// </summary>
        /// <param name="importedAssets">An array of file paths for the CSV assets to import.</param>
        /// <param name="saveAssets">If true, saves the assets after importing.</param>
        private static void ImportData(string[] importedAssets, bool saveAssets = true)
        {
            foreach (var str in importedAssets)
            {
                var inException = false;
                var stringForRemove = "";

                var data = AssetDatabase.LoadAssetAtPath<TextAsset>(str);

                var fileName = Path.GetFileNameWithoutExtension(str);
                if (!CsvPathUtility.TryGetAssetPathFromCsv(str, out var fullPath))
                {
                    Debug.LogWarning($"[CsvImport] Skip unsupported CSV path: {str}");
                    continue;
                }

                var filePath = Path.GetDirectoryName(fullPath);

                foreach (var item in EXCEPTION_LIST)
                    if (fileName.StartsWith(item))
                    {
                        stringForRemove = fileName.Substring(item.Length, fileName.Length - item.Length);
                        if (int.TryParse(stringForRemove, out _))
                        {
                            inException = true;
                            stringForRemove = "_" + stringForRemove;
                        }
                    }

                if (!string.IsNullOrEmpty(filePath))
                    if (filePath.Substring(filePath.Length - 1) == @"/")
                        filePath = filePath.Remove(filePath.Length - 1, 1);

                var assetfile = Path.ChangeExtension(fullPath, ".asset");

                var collectionName = (inException ? fileName.Replace(stringForRemove, "") : fileName) +
                                     COLLECTION_SUFFIX;
                var classCollection = FindTypeInAllAssemblies(collectionName);

                var className = (inException ? fileName.Replace(stringForRemove, "") : fileName) + DATA_SUFFIX;
                var classData = FindTypeInAllAssemblies(className);

                if (classCollection == null)
                {
                    Debug.LogWarning($"[CsvImport] Skip '{str}': Collection class '{collectionName}' not found.");
                    continue;
                }

                if (classData == null)
                {
                    Debug.LogWarning($"[CsvImport] Skip '{str}': Data class '{className}' not found.");
                    continue;
                }

                CreateDirectory(filePath);
                GenAsset(assetfile, data, classCollection, classData, saveAssets);
            }
        }

        /// <summary>
        ///     Creates or updates a ScriptableObject collection asset, deserializing raw CSV data into it.
        /// </summary>
        /// <param name="assetfile">The relative path of the asset to write.</param>
        /// <param name="data">The text asset containing the CSV raw text.</param>
        /// <param name="classCollection">The Type of the collection class.</param>
        /// <param name="classData">The Type of the model class containing the record elements.</param>
        /// <param name="saveAssets">If true, saves assets after updating.</param>
        private static void GenAsset(string assetfile, TextAsset data, Type classCollection, Type classData,
            bool saveAssets = true)
        {
            if (typeof(ICsvCustomData).IsAssignableFrom(classCollection))
            {
                GenCustomAsset(data.text, assetfile, classCollection, saveAssets);
                Debug.Log("Success import asset: " + assetfile);
                return;
            }

            var gm = AssetDatabase.LoadAssetAtPath(assetfile, classCollection);

            if (gm == null)
            {
                gm = ScriptableObject.CreateInstance(classCollection);
                AssetDatabase.CreateAsset(gm, assetfile);
            }

            var fields = gm.GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            Func<FieldInfo, bool> query = delegate(FieldInfo fieldInfo)
            {
                return fieldInfo.FieldType == classData
                       || fieldInfo.FieldType == classData.MakeArrayType();
            };
            var filledFields = fields.Where(query).ToList();

            var field = filledFields.Count > 0 ? filledFields[0] : null;

            if (field == null)
                field = gm.GetType().GetField("dataGroups",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            // Fully-qualified: type 'CsvReader' trùng tên với leaf namespace 'Ezg.Package.CsvReader'.
            var value = global::Ezg.Package.CsvReader.CsvReader.Deserialize(data.text, classData, assetfile);
            try
            {
                field.SetValue(gm, value);
            }
            catch
            {
                Debug.LogWarning("Chỉ có một phần tử, cần sửa lại cấu trúc data của: " + classData);
                return;
            }

            var method = gm.GetType().GetMethod("Convert");
            method?.Invoke(gm, null);

            if (field.IsPrivate) field.SetValue(gm, null);

            EditorUtility.SetDirty(gm);
            if (saveAssets) AssetDatabase.SaveAssets();

#if DEBUG_LOG || UNITY_EDITOR
            Debug.Log("Success import asset: " + assetfile);
#endif
        }

        /// <summary>
        ///     Ensures that the specified directory directory structure exists.
        /// </summary>
        /// <param name="filePath">The directory path to create.</param>
        private static void CreateDirectory(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || Directory.Exists(filePath)) return;
            Directory.CreateDirectory(filePath);
        }

        /// <summary>
        ///     Instantiates a new ScriptableObject asset or loads an existing one at the specified path.
        /// </summary>
        /// <param name="assetfile">The relative path of the asset.</param>
        /// <param name="classCollection">The ScriptableObject Type of the collection.</param>
        /// <param name="saveAssets">If true, saves assets and refreshes AssetDatabase.</param>
        /// <returns>The loaded or newly created ScriptableObject collection asset.</returns>
        private static object CreateOrLoadAsset(string assetfile, Type classCollection, bool saveAssets = true)
        {
            var result = AssetDatabase.LoadAssetAtPath(assetfile, classCollection);

            if (result == null)
            {
                var dir = Path.GetDirectoryName(assetfile);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var instance = ScriptableObject.CreateInstance(classCollection);
                AssetDatabase.CreateAsset(instance, assetfile);
                if (saveAssets)
                {
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }

                return instance;
            }

            return result;
        }

        /// <summary>
        ///     Deletes the generated ScriptableObject asset associated with the given CSV path.
        /// </summary>
        /// <param name="csvPath">The path to the source CSV file.</param>
        private static void DeleteGeneratedAsset(string csvPath)
        {
            if (!CsvPathUtility.TryGetAssetPathFromCsv(csvPath, out var assetPath))
                return;

            if (AssetDatabase.LoadAssetAtPath<Object>(assetPath) != null)
                AssetDatabase.DeleteAsset(assetPath);
        }

        /// <summary>
        ///     Checks whether the specified path points to a managed CSV file.
        /// </summary>
        /// <param name="path">The file path to evaluate.</param>
        /// <returns>True if the path is managed, false otherwise.</returns>
        private static bool IsManagedCsvPath(string path)
        {
            return CsvPathUtility.TryGetAssetPathFromCsv(path, out _);
        }

        #endregion
    }
#endif
}