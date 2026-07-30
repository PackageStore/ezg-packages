#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Ezg.Package.CsvReader
{
    /// <summary>
    ///     Utility class responsible for managing and pipeline importing of CSV configuration assets in the editor.
    /// </summary>
    public static class CsvImportManager
    {
        #region Public Methods

        /// <summary>
        ///     Import tất cả CSV được quản lý. Nếu isForce=true thì reimport toàn bộ, ngược lại chỉ import file có thay đổi (theo
        ///     MD5).
        /// </summary>
        /// <param name="isForce">If true, forces a full re-import of all assets regardless of modifications.</param>
        public static void ImportAllData(bool isForce = false)
        {
            if (isForce)
            {
                var forcedPaths = CsvPathUtility.EnumerateManagedCsvAssetPaths().ToArray();

                AssetDatabase.StartAssetEditing();
                try
                {
                    // ForceUpdate (không phải Default): Default bỏ qua khi content hash của Unity không đổi,
                    // nên "Force reload" sẽ im lặng không làm gì trong trường hợp .asset stale mà .csv thì y nguyên.
                    foreach (var path in forcedPaths)
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }

                Debug.Log($"[CsvImportManager] Force reimport {forcedPaths.Length} CSV.");
                return;
            }

            var allCsv = CsvPathUtility.EnumerateManagedCsvAssetPaths()
                .Select(path => new FileInfo(Path.Combine(Directory.GetCurrentDirectory(), path)))
                .ToArray();

            var allInfo = new Dictionary<string, string>();
            var cacheFilePath = "Assets/" + CsvReaderSettings.Current.cachedFileName;
            var allCsvEncode = File.Exists(cacheFilePath)
                ? File.ReadAllLines(cacheFilePath)
                : new string[0];

            if (allCsvEncode.Length > 0)
                allInfo = allCsvEncode.ToDictionary(x => x.Split(';')[0], x => x.Split(';')[1]);

            var changedPaths = new List<string>();

            foreach (var file in allCsv)
            {
                var finalPath = CsvPathUtility.ToAssetPath(file.FullName);
                var encodeSaved = allInfo.GetValueOrDefault(finalPath);
                var fileEncode = GenMD5File(file.FullName);

                if (encodeSaved != fileEncode)
                {
                    changedPaths.Add(finalPath);
                    if (string.IsNullOrEmpty(encodeSaved))
                        allInfo.Add(finalPath, fileEncode);
                    else
                        allInfo[finalPath] = fileEncode;
                }
            }

            if (changedPaths.Count > 0)
            {
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (var path in changedPaths)
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }
            }

            // Cache chỉ giữ file còn tồn tại: entry của CSV đã xoá/đổi tên tích tụ mãi trong file tracked
            // bởi git, gây diff rác và làm cache phình ra vô nghĩa.
            var livePaths = new HashSet<string>(allCsv.Select(f => CsvPathUtility.ToAssetPath(f.FullName)));
            using (var sw = new StreamWriter(cacheFilePath))
            {
                foreach (var entry in allInfo)
                    if (livePaths.Contains(entry.Key))
                        sw.WriteLine($"{entry.Key};{entry.Value}");
            }

            Debug.Log(changedPaths.Count > 0
                ? $"[CsvImportManager] Reimport {changedPaths.Count}/{allCsv.Length} CSV có thay đổi."
                : $"[CsvImportManager] Không CSV nào thay đổi ({allCsv.Length} file đã up-to-date) — " +
                  "dùng 'Force Reload all data CSV' nếu cần dựng lại asset.");
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Displays status log for the generic type import operations.
        /// </summary>
        /// <typeparam name="T">The type being logged.</typeparam>
        /// <param name="isSuccess">True if the operation succeeded.</param>
        /// <param name="message">An optional log message detail.</param>
        private static void ShowStatus<T>(bool isSuccess = true, string message = "")
        {
            Debug.Log((!isSuccess ? "Failed: " : "Success: ") + typeof(T).Name +
                      (!string.IsNullOrEmpty(message) ? " - " + message : ""));
        }

        /// <summary>
        ///     Sinh MD5 hash (hex lowercase) cho 1 file. Thay cho dependency Core.Security để module độc lập.
        /// </summary>
        /// <param name="filePath">Đường dẫn file cần hash.</param>
        /// <returns>Chuỗi MD5 hex viết thường.</returns>
        private static string GenMD5File(string filePath)
        {
            using var md5 = MD5.Create();
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var hash = md5.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        #endregion

        #region Fields

        private static UnityWebRequest _webRequest;

        #endregion
    }
}
#endif