using System;
using System.IO;
using System.Reflection;
using Ezg.Package.CsvReader;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using CsvReaderLib = Ezg.Package.CsvReader.CsvReader;

namespace Ezg.Package.Localize
{
#if UNITY_EDITOR
    public static class GenAsset
    {
        #region Private Methods

        /// <summary>
        ///     Validates whether a key has a valid lowercase format.
        /// </summary>
        /// <param name="key">The key string to validate.</param>
        /// <returns>True if key is in lowercase, otherwise false.</returns>
        private static bool IsValidKeyFormat(string key)
        {
            return key.Equals(key.ToLower());
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Generates a ScriptableObject configuration asset from a CSV file.
        /// </summary>
        /// <param name="nameCsv">The CSV file name.</param>
        /// <param name="path">The directory path of the CSV file.</param>
        /// <param name="classCollection">The Type of the collection class.</param>
        /// <param name="classData">The Type of the data class.</param>
        /// <returns>The generated asset object.</returns>
        public static object GenConfig(string nameCsv, string path, Type classCollection, Type classData)
        {
            try
            {
                var assetPath = path.Replace("Csv", "Resources");
                var assetFile = assetPath + nameCsv.Replace(".csv", ".asset");
                Directory.CreateDirectory(assetPath);

                var content = AssetDatabase.LoadAssetAtPath<TextAsset>(path + nameCsv).text;

                if (classCollection == null || classData == null) Debug.LogError("Chưa tạo class hoặc tên sai format");

                return Gen(assetFile, content, classCollection, classData);
            }
            catch (Exception e)
            {
                Debug.LogError($"{path + nameCsv}");
                throw;
            }
        }

        /// <summary>
        ///     Creates or updates a ScriptableObject asset and populates it with deserialized CSV data.
        /// </summary>
        /// <param name="path">The target asset path.</param>
        /// <param name="data">The raw CSV content.</param>
        /// <param name="classCollection">The Type of the collection class.</param>
        /// <param name="classData">The Type of the data class.</param>
        /// <returns>The generated or loaded ScriptableObject asset.</returns>
        public static Object Gen(string path, string data, Type classCollection, Type classData)
        {
            var gm = AssetDatabase.LoadAssetAtPath(path, classCollection);

            if (gm == null)
            {
                gm = ScriptableObject.CreateInstance(classCollection);
                AssetDatabase.CreateAsset(gm, path);
            }

            var type = classData;
            var field = gm.GetType().GetField("dataGroups",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var method = gm.GetType().GetMethod("Convert");

            if (field != null)
            {
                field.SetValue(gm, CsvReaderLib.Deserialize(data, type, path));

                method?.Invoke(gm, null);

                if (field.IsPrivate) field.SetValue(gm, null);
            }

            EditorUtility.SetDirty(gm);
            AssetDatabase.SaveAssets();

            return gm;
        }

        #endregion
    }
#endif
}