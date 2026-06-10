using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Ezg.Package.CsvReader
{
    /// <summary>
    ///     Utility class responsible for generating paths for CSV assets.
    /// </summary>
    public static class AssetPathGenerate
    {
        #region Public Methods

        /// <summary>
        ///     Generates the directory constant class based on CSV files found in the project (CsvConfig pattern).
        /// </summary>
        public static void Generate()
        {
            var entries = new Dictionary<string, string>(StringComparer.Ordinal);
            var orderedKeys = new List<string>();

            AddCsvConfigEntries(entries, orderedKeys);

            var propertiesBuf = new StringBuilder();
            foreach (var key in orderedKeys)
                propertiesBuf.Append(string.Format(FORMAT, key, entries[key]));

            var code = TEMPLATE.Replace("${name}", CsvReaderSettings.Current.generatedClassName);
            code = code.Replace("${format}", propertiesBuf.ToString());

            WriteFile(code);
        }

        #endregion

        #region Fields

        private const string FORMAT = "\npublic const string {0} = \"{1}\";";
        private const string TEMPLATE = "public static class ${name}\r\n{${format}\n}";

        #endregion

        #region Private Methods

        /// <summary>
        ///     Collects CSV asset entries.
        /// </summary>
        /// <param name="entries">A dictionary to store key-value mapping of assets.</param>
        /// <param name="orderedKeys">A list to preserve order of entries.</param>
        private static void AddCsvConfigEntries(Dictionary<string, string> entries, List<string> orderedKeys)
        {
            foreach (var csvPath in CsvPathUtility.EnumerateCsvConfigAssetPaths())
            {
                var fileName = CsvPathUtility.GetCsvAssetValue(csvPath);
                AddEntry(entries, orderedKeys, fileName, fileName, csvPath);
            }
        }

        /// <summary>
        ///     Adds a single entry to the dictionary, ignoring duplicate entries and warning the developer.
        /// </summary>
        /// <param name="entries">The entries dictionary.</param>
        /// <param name="orderedKeys">The list of ordered keys.</param>
        /// <param name="key">The entry key.</param>
        /// <param name="value">The entry value.</param>
        /// <param name="sourcePath">The path of the source asset/CSV causing this entry.</param>
        private static void AddEntry(Dictionary<string, string> entries, List<string> orderedKeys,
            string key, string value, string sourcePath)
        {
            if (entries.TryGetValue(key, out var existingValue))
            {
                Debug.LogWarning(
                    $"[CsvAssetDir] Duplicate entry '{key}' from '{sourcePath}' ignored. Existing: '{existingValue}', new: '{value}'.");
                return;
            }

            entries.Add(key, value);
            orderedKeys.Add(key);
        }

        /// <summary>
        ///     Writes the generated code to CsvAssetDir.cs file.
        /// </summary>
        /// <param name="code">The fully formatted class content string.</param>
        private static void WriteFile(string code)
        {
            var writeFolder = Environment.CurrentDirectory + CsvReaderSettings.Current.generatedClassDirectory;
            if (!Directory.Exists(writeFolder))
                Directory.CreateDirectory(writeFolder);

            File.WriteAllText(writeFolder + CsvReaderSettings.Current.generatedClassName + ".cs", code);
        }

        #endregion
    }
}