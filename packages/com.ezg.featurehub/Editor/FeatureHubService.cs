// EZG Feature Hub — logic load catalog/template + cài đặt.
//  - .unitypackage: tải về Temp/ -> verify sha256 -> AssetDatabase.ImportPackage -> xóa temp -> ghi record.
//  - UPM: ghi vào Packages/manifest.json (+ scopedRegistries), tải .tgz cho dep "file:", rồi Client.Resolve().
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Ezg.FeatureHub.Editor
{
    public static class FeatureHubService
    {
        #region Constants

        private const string FILE_PREFIX = "file:";
        private const string DEPENDENCIES_KEY = "dependencies";
        private const string REGISTRIES_KEY = "scopedRegistries";

        #endregion

        #region Public Methods — Load

        public static void LoadCatalog(Action<AssetCatalog, string> onDone)
        {
            EditorDownloader.DownloadText(FeatureHubConstants.CATALOG_URL, (ok, text, error) =>
            {
                if (!ok)
                {
                    onDone?.Invoke(null, $"Tải catalog lỗi: {error}");
                    return;
                }

                try
                {
                    var catalog = JsonConvert.DeserializeObject<AssetCatalog>(text);
                    onDone?.Invoke(catalog ?? new AssetCatalog(), null);
                }
                catch (Exception e)
                {
                    onDone?.Invoke(null, $"Parse catalog lỗi: {e.Message}");
                }
            });
        }

        public static void LoadTemplate(Action<UnityTemplate, string> onDone)
        {
            EditorDownloader.DownloadText(FeatureHubConstants.TEMPLATE_URL, (ok, text, error) =>
            {
                if (!ok)
                {
                    onDone?.Invoke(null, $"Tải template lỗi: {error}");
                    return;
                }

                try
                {
                    var template = JsonConvert.DeserializeObject<UnityTemplate>(text);
                    onDone?.Invoke(template ?? new UnityTemplate(), null);
                }
                catch (Exception e)
                {
                    onDone?.Invoke(null, $"Parse template lỗi: {e.Message}");
                }
            });
        }

        /// <summary>Đọc dependencies hiện có trong Packages/manifest.json để so trạng thái.</summary>
        public static Dictionary<string, string> LoadProjectDependencies()
        {
            var result = new Dictionary<string, string>();
            try
            {
                string path = ManifestPath();
                if (!File.Exists(path))
                    return result;

                var root = JObject.Parse(File.ReadAllText(path));
                if (root[DEPENDENCIES_KEY] is JObject deps)
                {
                    foreach (var prop in deps.Properties())
                        result[prop.Name] = prop.Value?.ToString();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FeatureHub] Đọc manifest lỗi: {e.Message}");
            }

            return result;
        }

        #endregion

        #region Public Methods — Install .unitypackage

        /// <summary>Tải + import một .unitypackage. onDone(success, errorOrNull).</summary>
        public static void InstallUnityPackage(
            CatalogAsset asset,
            bool interactive,
            Action<float> onProgress,
            Action<bool, string> onDone)
        {
            string tempPath = Path.Combine(TempDir(), asset.fileName);

            EditorDownloader.DownloadToFile(asset.url, tempPath, onProgress, (ok, error) =>
            {
                if (!ok)
                {
                    TryDelete(tempPath);
                    onDone?.Invoke(false, $"Tải thất bại: {error}");
                    return;
                }

                // Verify sha256 nếu catalog có khai báo.
                if (!string.IsNullOrEmpty(asset.sha256))
                {
                    string actual = Sha256File(tempPath);
                    if (!string.Equals(actual, asset.sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        TryDelete(tempPath);
                        onDone?.Invoke(false, "SHA-256 không khớp — bỏ qua để an toàn.");
                        return;
                    }
                }

                ImportThenCleanup(asset, tempPath, interactive, onDone);
            });
        }

        #endregion

        #region Public Methods — Install UPM

        /// <summary>
        /// Ghi 1 UPM dependency vào manifest (+ registries). Với dep "file:" sẽ tải .tgz về Packages/.
        /// resolveNow=false để gom nhiều dep rồi Resolve một lần (xem ResolveNow).
        /// </summary>
        public static void InstallUpm(
            string id,
            string value,
            UnityTemplate template,
            bool resolveNow,
            Action<float> onProgress,
            Action<bool, string> onDone)
        {
            if (value != null && value.StartsWith(FILE_PREFIX))
            {
                string fileName = value.Substring(FILE_PREFIX.Length);
                TemplateFile tgz = template?.files?.localPackages?.Find(f => f.fileName == fileName);
                if (tgz == null)
                {
                    onDone?.Invoke(false, $"Template thiếu localPackages cho '{fileName}'.");
                    return;
                }

                string dest = Path.Combine(PackagesDir(), fileName);
                EditorDownloader.DownloadToFile(tgz.url, dest, onProgress, (ok, error) =>
                {
                    if (!ok)
                    {
                        onDone?.Invoke(false, $"Tải .tgz lỗi: {error}");
                        return;
                    }

                    if (!string.IsNullOrEmpty(tgz.sha256))
                    {
                        string actual = Sha256File(dest);
                        if (!string.Equals(actual, tgz.sha256, StringComparison.OrdinalIgnoreCase))
                        {
                            TryDelete(dest);
                            onDone?.Invoke(false, "SHA-256 .tgz không khớp.");
                            return;
                        }
                    }

                    ApplyManifest(id, value, template, resolveNow, onDone);
                });
            }
            else
            {
                ApplyManifest(id, value, template, resolveNow, onDone);
            }
        }

        /// <summary>Trigger Unity resolve packages (gọi 1 lần sau khi đã ghi nhiều dep).</summary>
        public static void ResolveNow()
        {
            AssetDatabase.Refresh();
            Client.Resolve();
        }

        #endregion

        #region Private Methods — Import

        private static void ImportThenCleanup(
            CatalogAsset asset,
            string tempPath,
            bool interactive,
            Action<bool, string> onDone)
        {
            AssetDatabase.ImportPackageCallback onCompleted = null;
            AssetDatabase.ImportPackageCallback onCancelled = null;
            AssetDatabase.ImportPackageFailedCallback onFailed = null;

            void Unsubscribe()
            {
                AssetDatabase.importPackageCompleted -= onCompleted;
                AssetDatabase.importPackageCancelled -= onCancelled;
                AssetDatabase.importPackageFailed -= onFailed;
            }

            onCompleted = _ =>
            {
                Unsubscribe();
                string sha = !string.IsNullOrEmpty(asset.sha256) ? asset.sha256 : Sha256File(tempPath);
                TryDelete(tempPath);
                FeatureHubInstallRecord.MarkInstalled(asset, sha);
                onDone?.Invoke(true, null);
            };

            onCancelled = _ =>
            {
                Unsubscribe();
                TryDelete(tempPath);
                onDone?.Invoke(false, "Đã hủy import.");
            };

            onFailed = (_, errorMessage) =>
            {
                Unsubscribe();
                TryDelete(tempPath);
                onDone?.Invoke(false, $"Import lỗi: {errorMessage}");
            };

            AssetDatabase.importPackageCompleted += onCompleted;
            AssetDatabase.importPackageCancelled += onCancelled;
            AssetDatabase.importPackageFailed += onFailed;

            // interactive=false: import toàn bộ không hỏi; true: mở hộp thoại Import của Unity.
            AssetDatabase.ImportPackage(tempPath, interactive);
        }

        #endregion

        #region Private Methods — Manifest

        private static void ApplyManifest(
            string id,
            string value,
            UnityTemplate template,
            bool resolveNow,
            Action<bool, string> onDone)
        {
            try
            {
                string path = ManifestPath();
                var root = JObject.Parse(File.ReadAllText(path));

                if (!(root[DEPENDENCIES_KEY] is JObject deps))
                {
                    deps = new JObject();
                    root[DEPENDENCIES_KEY] = deps;
                }

                deps[id] = value;
                EnsureRegistries(root, template);

                File.WriteAllText(path, root.ToString(Formatting.Indented));

                if (resolveNow)
                    ResolveNow();

                onDone?.Invoke(true, null);
            }
            catch (Exception e)
            {
                onDone?.Invoke(false, $"Ghi manifest lỗi: {e.Message}");
            }
        }

        /// <summary>Merge scopedRegistries của template vào manifest (match theo url, union scopes).</summary>
        private static void EnsureRegistries(JObject root, UnityTemplate template)
        {
            if (template?.scopedRegistries == null || template.scopedRegistries.Count == 0)
                return;

            if (!(root[REGISTRIES_KEY] is JArray registries))
            {
                registries = new JArray();
                root[REGISTRIES_KEY] = registries;
            }

            foreach (var reg in template.scopedRegistries)
            {
                JObject existing = null;
                foreach (var item in registries)
                {
                    if (item is JObject obj &&
                        string.Equals(obj["url"]?.ToString(), reg.url, StringComparison.OrdinalIgnoreCase))
                    {
                        existing = obj;
                        break;
                    }
                }

                if (existing == null)
                {
                    registries.Add(new JObject
                    {
                        ["name"] = reg.name,
                        ["url"] = reg.url,
                        ["scopes"] = new JArray(reg.scopes),
                    });
                    continue;
                }

                // Union scopes vào registry đã có.
                if (!(existing["scopes"] is JArray scopes))
                {
                    scopes = new JArray();
                    existing["scopes"] = scopes;
                }

                var current = new HashSet<string>();
                foreach (var s in scopes)
                    current.Add(s.ToString());

                foreach (var scope in reg.scopes)
                {
                    if (current.Add(scope))
                        scopes.Add(scope);
                }
            }
        }

        #endregion

        #region Private Methods — Paths & Hash

        private static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }

        private static string PackagesDir()
        {
            return Path.Combine(ProjectRoot(), "Packages");
        }

        private static string ManifestPath()
        {
            return Path.Combine(PackagesDir(), "manifest.json");
        }

        private static string TempDir()
        {
            return Path.Combine(ProjectRoot(), "Temp", FeatureHubConstants.TEMP_DIR_NAME);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FeatureHub] Không xóa được file tạm '{path}': {e.Message}");
            }
        }

        private static string Sha256File(string path)
        {
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(stream);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        #endregion
    }
}
