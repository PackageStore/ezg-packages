// EZG Feature Hub — record các .unitypackage đã cài (lưu ở ProjectSettings/, không vào Assets).
// .unitypackage không để lại dấu vết trong manifest nên phải tự track tên + sha256 để biết trạng thái.
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Ezg.FeatureHub.Editor
{
    public static class FeatureHubInstallRecord
    {
        #region Fields

        private static InstallRecord _cache;

        #endregion

        #region Public Methods

        /// <summary>Lấy record của một asset theo tên (null nếu chưa cài).</summary>
        public static InstalledUnityPackage Get(string assetName)
        {
            var record = Load();
            return record.unityPackages.Find(p => p.name == assetName);
        }

        /// <summary>Tính trạng thái cài đặt dựa trên record + sha256 trong catalog.</summary>
        public static UnityPackageStatus GetStatus(CatalogAsset asset)
        {
            var installed = Get(asset.name);
            if (installed != null)
            {
                // Catalog có sha256 và khác với bản đã cài -> có bản mới.
                if (!string.IsNullOrEmpty(asset.sha256) &&
                    !string.Equals(installed.sha256, asset.sha256, StringComparison.OrdinalIgnoreCase))
                    return UnityPackageStatus.UpdateAvailable;

                return UnityPackageStatus.Installed;
            }

            // Chưa có record -> thử nhận diện "đã có sẵn" qua dấu chân asset trong project
            // (import thủ công / trước khi có Feature Hub / máy khác). Tính sống mỗi lần gọi để
            // không bị kẹt trạng thái nếu user xóa asset sau đó. KHÔNG so sha vì không biết version thật.
            return DetectByMarker(asset) ? UnityPackageStatus.Installed : UnityPackageStatus.NotInstalled;
        }

        /// <summary>
        /// True nếu bất kỳ markerPath/markerGuid của asset trỏ tới asset đang tồn tại trong project.
        /// Asset không khai báo marker -> luôn false (giữ hành vi cũ: chỉ dựa vào install-record).
        /// </summary>
        public static bool DetectByMarker(CatalogAsset asset)
        {
            if (asset == null)
                return false;

            if (asset.markerGuids != null)
            {
                foreach (var guid in asset.markerGuids)
                {
                    if (string.IsNullOrEmpty(guid))
                        continue;
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(path) && ProjectPathExists(path))
                        return true;
                }
            }

            if (asset.markerPaths != null)
            {
                foreach (var rel in asset.markerPaths)
                {
                    if (!string.IsNullOrEmpty(rel) && ProjectPathExists(rel))
                        return true;
                }
            }

            return false;
        }

        /// <summary>Ghi nhận một asset vừa cài thành công.</summary>
        public static void MarkInstalled(CatalogAsset asset, string sha256)
        {
            var record = Load();
            var entry = record.unityPackages.Find(p => p.name == asset.name);
            if (entry == null)
            {
                entry = new InstalledUnityPackage { name = asset.name };
                record.unityPackages.Add(entry);
            }

            entry.fileName = asset.fileName;
            entry.sha256 = sha256 ?? asset.sha256 ?? string.Empty;
            entry.installedAtUtc = DateTime.UtcNow.ToString("o");
            Save(record);
        }

        /// <summary>Xóa record một asset (khi user gỡ thủ công / muốn reset trạng thái).</summary>
        public static void Remove(string assetName)
        {
            var record = Load();
            int removed = record.unityPackages.RemoveAll(p => p.name == assetName);
            if (removed > 0)
                Save(record);
        }

        #endregion

        #region Private Methods

        /// <summary>True nếu path tương đối project (vd "Assets/...") tồn tại trên đĩa, là file hoặc folder.</summary>
        private static bool ProjectPathExists(string projectRelative)
        {
            if (string.IsNullOrEmpty(projectRelative))
                return false;

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string full = Path.Combine(projectRoot, projectRelative);
            return File.Exists(full) || Directory.Exists(full);
        }

        private static string RecordPath()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(
                projectRoot, "ProjectSettings",
                FeatureHubConstants.RECORD_DIR_NAME,
                FeatureHubConstants.RECORD_FILE_NAME);
        }

        private static InstallRecord Load()
        {
            if (_cache != null)
                return _cache;

            try
            {
                string path = RecordPath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    _cache = Newtonsoft.Json.JsonConvert.DeserializeObject<InstallRecord>(json)
                             ?? new InstallRecord();
                    return _cache;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FeatureHub] Không đọc được install-record, tạo mới. {e.Message}");
            }

            _cache = new InstallRecord();
            return _cache;
        }

        private static void Save(InstallRecord record)
        {
            _cache = record;
            try
            {
                string path = RecordPath();
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(
                    record, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[FeatureHub] Ghi install-record thất bại: {e.Message}");
            }
        }

        #endregion
    }
}
