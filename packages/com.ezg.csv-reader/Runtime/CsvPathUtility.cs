using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Ezg.Package.CsvReader
{
    /// <summary>
    ///     Utility class containing helper methods to resolve and query paths for CSV configuration files and assets.
    /// </summary>
    public static class CsvPathUtility
    {
        #region Private Methods

        /// <summary>
        ///     Resolves the 'CsvConfig' directory path if the target CSV path is located inside one.
        /// </summary>
        /// <param name="csvPath">The CSV path to query.</param>
        /// <returns>The resolved path of the CsvConfig directory, or null if not found.</returns>
        private static string GetCsvConfigDirectory(string csvPath)
        {
            var normalizedPath = NormalizePath(csvPath);
            var currentDirectory = NormalizePath(Path.GetDirectoryName(normalizedPath));

            while (!string.IsNullOrEmpty(currentDirectory))
            {
                if (string.Equals(Path.GetFileName(currentDirectory), CsvConfigFolderName,
                        StringComparison.OrdinalIgnoreCase))
                    return currentDirectory;

                var parentDirectory = NormalizePath(Path.GetDirectoryName(currentDirectory));
                if (string.IsNullOrEmpty(parentDirectory) || parentDirectory == currentDirectory)
                    break;

                currentDirectory = parentDirectory;
            }

            return null;
        }

        #endregion

        #region Fields

        public static string CsvConfigFolderName => CsvReaderSettings.Current.csvConfigFolderName;
        public static string ResourcesFolderName => CsvReaderSettings.Current.resourcesFolderName;

        #endregion

        #region Public Methods

        /// <summary>
        ///     Replaces backslashes with forward slashes to normalize file paths across different operating systems.
        /// </summary>
        /// <param name="path">The raw file path string.</param>
        /// <returns>The normalized path string.</returns>
        public static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/');
        }

        /// <summary>
        ///     Cố gắng chuyển đường dẫn .csv thành đường dẫn .asset tương ứng.
        ///     CSV nằm trong thư mục con tên "CsvConfig" → asset trong ../Resources/ (feature-local).
        /// </summary>
        /// <param name="csvPath">The path to the source CSV file.</param>
        /// <param name="assetPath">The output path to the generated ScriptableObject asset.</param>
        /// <returns>True if the path mapping is successful, false otherwise.</returns>
        public static bool TryGetAssetPathFromCsv(string csvPath, out string assetPath)
        {
            assetPath = null;

            if (!IsCsvFilePath(csvPath))
                return false;

            var normalizedPath = NormalizePath(csvPath);

            var csvConfigDirectory = GetCsvConfigDirectory(normalizedPath);
            if (string.IsNullOrEmpty(csvConfigDirectory))
                return false;

            var parentDirectory = NormalizePath(Path.GetDirectoryName(csvConfigDirectory));
            if (string.IsNullOrEmpty(parentDirectory))
                return false;

            var fileName = Path.GetFileNameWithoutExtension(normalizedPath);
            assetPath = $"{parentDirectory}/{ResourcesFolderName}/{fileName}.asset";
            return true;
        }

        /// <summary>
        ///     Extracts the base asset name (without directory or extension) from the specified CSV path.
        /// </summary>
        /// <param name="csvPath">The CSV path.</param>
        /// <returns>The CSV file name without extension.</returns>
        public static string GetCsvAssetValue(string csvPath)
        {
            return Path.GetFileNameWithoutExtension(csvPath);
        }

        /// <summary>
        ///     Determines whether the given path represents a file with a .csv extension.
        /// </summary>
        /// <param name="path">The file path.</param>
        /// <returns>True if the file is a CSV file, false otherwise.</returns>
        public static bool IsCsvFilePath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                   string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        ///     Liệt kê tất cả các file .csv nằm trong thư mục CsvConfig (feature-local pattern).
        /// </summary>
        /// <returns>An enumerable of relative CSV config asset paths.</returns>
        public static IEnumerable<string> EnumerateCsvConfigAssetPaths()
        {
            var assetsRoot = NormalizePath(Application.dataPath);
            if (!Directory.Exists(assetsRoot))
                yield break;

            foreach (var absolutePath in Directory.EnumerateFiles(assetsRoot, "*.csv", SearchOption.AllDirectories))
            {
                var assetPath = ToAssetPath(absolutePath);
                if (IsCsvConfigPath(assetPath))
                    yield return assetPath;
            }
        }

        /// <summary>
        ///     Liệt kê tất cả các file .csv được quản lý bởi pipeline (cả legacy lẫn CsvConfig).
        /// </summary>
        /// <returns>An enumerable of relative CSV asset paths managed by the system.</returns>
        public static IEnumerable<string> EnumerateManagedCsvAssetPaths()
        {
            var assetsRoot = NormalizePath(Application.dataPath);
            if (!Directory.Exists(assetsRoot))
                yield break;

            foreach (var absolutePath in Directory.EnumerateFiles(assetsRoot, "*.csv", SearchOption.AllDirectories))
            {
                var assetPath = ToAssetPath(absolutePath);
                if (TryGetAssetPathFromCsv(assetPath, out _))
                    yield return assetPath;
            }
        }

        /// <summary>
        ///     Attempts to find the relative asset path of a managed CSV file by its file name.
        /// </summary>
        /// <param name="fileName">The base CSV file name (with extension).</param>
        /// <param name="csvPath">The output matched CSV relative path.</param>
        /// <returns>True if the managed CSV file is found, false otherwise.</returns>
        public static bool TryFindManagedCsvPath(string fileName, out string csvPath)
        {
            csvPath = null;

            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            foreach (var managedPath in EnumerateManagedCsvAssetPaths())
                if (string.Equals(Path.GetFileName(managedPath), fileName, StringComparison.OrdinalIgnoreCase))
                {
                    csvPath = managedPath;
                    return true;
                }

            return false;
        }

        /// <summary>
        ///     Evaluates if the specified CSV path is located inside a 'CsvConfig' directory.
        /// </summary>
        /// <param name="csvPath">The CSV path.</param>
        /// <returns>True if it is inside a CsvConfig folder, false otherwise.</returns>
        public static bool IsCsvConfigPath(string csvPath)
        {
            return !string.IsNullOrEmpty(GetCsvConfigDirectory(csvPath));
        }

        /// <summary>
        ///     Converts an absolute system directory path to a Unity relative asset path starting with 'Assets/'.
        /// </summary>
        /// <param name="absoluteOrAssetPath">The target path string.</param>
        /// <returns>A normalized Unity relative asset path.</returns>
        public static string ToAssetPath(string absoluteOrAssetPath)
        {
            var normalizedPath = NormalizePath(absoluteOrAssetPath);

            if (normalizedPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return normalizedPath;

            var assetsRoot = NormalizePath(Application.dataPath);
            if (!normalizedPath.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
                return normalizedPath;

            var relativePath = normalizedPath.Substring(assetsRoot.Length).TrimStart('/');
            return NormalizePath(Path.Combine("Assets", relativePath));
        }

        #endregion
    }
}