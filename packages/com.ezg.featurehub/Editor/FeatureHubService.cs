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
using UnityEditor.PackageManager.Requests;
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

        /// <summary>Tải index các dự án feature (features/index.json). onDone(index, errorOrNull).</summary>
        public static void LoadFeaturesIndex(Action<FeaturesIndex, string> onDone)
        {
            EditorDownloader.DownloadText(FeatureHubConstants.FEATURES_INDEX_URL, (ok, text, error) =>
            {
                if (!ok)
                {
                    onDone?.Invoke(null, $"Tải features index lỗi: {error}");
                    return;
                }

                try
                {
                    var index = JsonConvert.DeserializeObject<FeaturesIndex>(text);
                    onDone?.Invoke(index ?? new FeaturesIndex(), null);
                }
                catch (Exception e)
                {
                    onDone?.Invoke(null, $"Parse features index lỗi: {e.Message}");
                }
            });
        }

        /// <summary>Tải catalog.json của một dự án feature. Shape giống asset-catalog.json nên
        /// deserialize thẳng vào AssetCatalog (field "project" thừa được Newtonsoft bỏ qua).</summary>
        public static void LoadFeatureCatalog(string catalogUrl, Action<AssetCatalog, string> onDone)
        {
            EditorDownloader.DownloadText(catalogUrl, (ok, text, error) =>
            {
                if (!ok)
                {
                    onDone?.Invoke(null, $"Tải feature catalog lỗi: {error}");
                    return;
                }

                try
                {
                    var catalog = JsonConvert.DeserializeObject<AssetCatalog>(text);
                    onDone?.Invoke(catalog ?? new AssetCatalog(), null);
                }
                catch (Exception e)
                {
                    onDone?.Invoke(null, $"Parse feature catalog lỗi: {e.Message}");
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

        /// <summary>
        /// Liệt kê MỌI package đã resolve trong project qua PackageManager (direct + transitive +
        /// embedded + local + built-in module) -> Dictionary&lt;name, resolvedVersion&gt;. Đây là cách
        /// duy nhất bắt được package "đã có sẵn" mà KHÔNG phải dependency trực tiếp trong manifest.json
        /// (vd kéo theo bởi gói khác, hoặc cài bằng tay). Client.List là async nên trả về qua callback;
        /// offlineMode=true để khỏi gọi mạng (chỉ đọc trạng thái đã resolve), includeIndirect=true để
        /// lấy cả dependency gián tiếp.
        /// </summary>
        public static void LoadResolvedPackages(Action<Dictionary<string, string>> onDone)
        {
            ListRequest request;
            try
            {
                request = Client.List(offlineMode: true, includeIndirectDependencies: true);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FeatureHub] Client.List khởi tạo lỗi: {e.Message}");
                onDone?.Invoke(new Dictionary<string, string>());
                return;
            }

            void Tick()
            {
                if (!request.IsCompleted)
                    return;

                EditorApplication.update -= Tick;

                var map = new Dictionary<string, string>();
                if (request.Status == StatusCode.Success && request.Result != null)
                {
                    foreach (var pkg in request.Result)
                        if (pkg != null && !string.IsNullOrEmpty(pkg.name))
                            map[pkg.name] = pkg.version;
                }
                else if (request.Status >= StatusCode.Failure)
                {
                    Debug.LogWarning($"[FeatureHub] Client.List lỗi: {request.Error?.message}");
                }

                onDone?.Invoke(map);
            }

            EditorApplication.update += Tick;
        }

        #endregion

        #region Public Methods — Scoped Registry

        /// <summary>
        /// Registry cần có cho project: ưu tiên lấy từ template (server), nếu rỗng dùng
        /// fallback default EZG (khớp manifest dự án hiện tại).
        /// </summary>
        public static List<ScopedRegistry> GetRequiredRegistries(UnityTemplate template)
        {
            if (template?.scopedRegistries != null && template.scopedRegistries.Count > 0)
                return template.scopedRegistries;

            return new List<ScopedRegistry>
            {
                new ScopedRegistry
                {
                    name = FeatureHubConstants.EZG_REGISTRY_NAME,
                    url = FeatureHubConstants.EZG_REGISTRY_URL,
                    scopes = new List<string>(FeatureHubConstants.EZG_REGISTRY_SCOPES),
                },
            };
        }

        /// <summary>
        /// Kiểm tra project đã khai báo đủ scoped registry (match theo url + đủ scopes) chưa.
        /// Trả về true nếu đủ; <paramref name="missing"/> chứa các registry/scope còn thiếu.
        /// </summary>
        public static bool ValidateScopedRegistries(UnityTemplate template, out List<ScopedRegistry> missing)
        {
            missing = new List<ScopedRegistry>();
            var required = GetRequiredRegistries(template);
            if (required.Count == 0)
                return true;

            JArray registries = ReadManifestRegistries();

            foreach (var reg in required)
            {
                JObject existing = FindRegistryByUrl(registries, reg.url);
                var missingScopes = new List<string>();

                if (existing == null)
                {
                    missingScopes.AddRange(reg.scopes);
                }
                else
                {
                    var current = new HashSet<string>();
                    if (existing["scopes"] is JArray scopes)
                        foreach (var s in scopes)
                            current.Add(s.ToString());

                    foreach (var scope in reg.scopes)
                        if (!current.Contains(scope))
                            missingScopes.Add(scope);
                }

                if (missingScopes.Count > 0)
                    missing.Add(new ScopedRegistry { name = reg.name, url = reg.url, scopes = missingScopes });
            }

            return missing.Count == 0;
        }

        /// <summary>Merge các scoped registry cần có vào manifest. KHÔNG đụng tới dependencies.</summary>
        public static void EnsureScopedRegistries(UnityTemplate template, bool resolveNow, Action<bool, string> onDone)
        {
            try
            {
                var required = GetRequiredRegistries(template);
                string path = ManifestPath();
                var root = JObject.Parse(File.ReadAllText(path));
                MergeRegistries(root, required);
                File.WriteAllText(path, root.ToString(Formatting.Indented));

                if (resolveNow)
                    ResolveNow();

                onDone?.Invoke(true, null);
            }
            catch (Exception e)
            {
                onDone?.Invoke(false, $"Ghi scoped registry lỗi: {e.Message}");
            }
        }

        /// <summary>
        /// Đảm bảo manifest.json có một dependency (name -> version/url). Trả về true nếu vừa THÊM
        /// mới (chưa từng có), false nếu đã tồn tại sẵn (không ghi đè để tôn trọng lựa chọn của project).
        /// Dùng cho git-dependency mà UPM không thể resolve gián tiếp qua package.json (vd rlottie).
        /// </summary>
        public static bool EnsureDependency(string name, string versionOrUrl)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(versionOrUrl))
                return false;

            try
            {
                string path = ManifestPath();
                if (!File.Exists(path))
                    return false;

                var root = JObject.Parse(File.ReadAllText(path));
                if (!(root[DEPENDENCIES_KEY] is JObject deps))
                {
                    deps = new JObject();
                    root[DEPENDENCIES_KEY] = deps;
                }

                if (deps[name] != null)
                    return false; // đã có (bất kể version/url) -> không đụng vào

                deps[name] = versionOrUrl;
                File.WriteAllText(path, root.ToString(Formatting.Indented));
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FeatureHub] Ghi dependency '{name}' lỗi: {e.Message}");
                return false;
            }
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

        #region Public Methods — Uninstall .unitypackage

        /// <summary>
        /// Gỡ một asset/feature đã cài: XÓA các markerPaths/markerGuids (thư mục/asset gốc) khỏi project
        /// rồi xóa install-record. Chỉ áp dụng cho asset CÓ khai báo marker (mỗi feature = 1 folder gốc
        /// dạng "Assets/_Project/Features/.../<Name>"), nên xóa marker = gỡ trọn feature.
        /// onDone(success, errorOrNull). Lưu ý: gói chứa script bị xóa sẽ gây recompile + domain reload,
        /// nên record được xóa TRƯỚC khi xóa asset để trạng thái luôn nhất quán dù closure bị nuốt.
        /// </summary>
        public static void UninstallUnityPackage(CatalogAsset asset, Action<bool, string> onDone)
        {
            if (asset == null)
            {
                onDone?.Invoke(false, "Asset rỗng.");
                return;
            }

            var targets = ResolveMarkerAssetPaths(asset);

            // Không có marker / không còn asset nào trên đĩa -> không biết xóa gì. Vẫn xóa record để
            // gỡ trạng thái "đã cài" (vd user đã xóa thủ công, chỉ còn kẹt record).
            if (targets.Count == 0)
            {
                bool hadRecord = FeatureHubInstallRecord.Get(asset.name) != null;
                FeatureHubInstallRecord.Remove(asset.name);
                onDone?.Invoke(
                    hadRecord,
                    hadRecord
                        ? null
                        : "Không xác định được file để gỡ (asset thiếu markerPaths hoặc đã bị xóa).");
                return;
            }

            // Xóa record trước (xem chú thích trên).
            FeatureHubInstallRecord.Remove(asset.name);

            var failed = new List<string>();
            int deleted = 0;
            foreach (var path in targets)
            {
                try
                {
                    if (AssetDatabase.DeleteAsset(path))
                        deleted++;
                    else
                        failed.Add(path);
                }
                catch (Exception e)
                {
                    failed.Add($"{path} ({e.Message})");
                }
            }

            AssetDatabase.Refresh();

            if (failed.Count > 0)
                onDone?.Invoke(deleted > 0,
                    $"Đã xóa {deleted} mục, lỗi {failed.Count}: {string.Join("; ", failed)}");
            else
                onDone?.Invoke(true, null);
        }

        /// <summary>
        /// Gom các path đích để gỡ: markerGuids (resolve ra path) + markerPaths (path tương đối project),
        /// khử trùng lặp và chỉ giữ lại path còn TỒN TẠI trên đĩa (file hoặc folder). Path luôn ở dạng
        /// "Assets/..." để AssetDatabase.DeleteAsset xử lý (xóa luôn .meta đi kèm).
        /// </summary>
        private static List<string> ResolveMarkerAssetPaths(CatalogAsset asset)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void TryAdd(string path)
            {
                if (string.IsNullOrEmpty(path) || !seen.Add(path))
                    return;
                string full = Path.Combine(ProjectRoot(), path);
                if (File.Exists(full) || Directory.Exists(full))
                    result.Add(path);
            }

            if (asset.markerGuids != null)
            {
                foreach (var guid in asset.markerGuids)
                {
                    if (string.IsNullOrEmpty(guid))
                        continue;
                    TryAdd(AssetDatabase.GUIDToAssetPath(guid));
                }
            }

            if (asset.markerPaths != null)
            {
                foreach (var rel in asset.markerPaths)
                    TryAdd(rel);
            }

            return result;
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
            // importPackageCompleted/Cancelled/Failed là event GLOBAL. Tool này chỉ chạy DUY NHẤT
            // một import tại một thời điểm (window khóa _busy; batch queue chạy tuần tự), nên
            // terminal-event đầu tiên sau khi subscribe CHÍNH là import của ta.
            //
            // Việc GHI install-record + xóa file tạm do FeatureHubImportFinalizer đảm nhận (qua
            // event + recovery sau domain reload). Lý do: gói .unitypackage CÓ SCRIPT làm Unity
            // recompile + domain reload, xóa sạch các closure ở đây trước khi MarkInstalled kịp
            // chạy -> record trống -> trạng thái "Chưa cài" dù đã import. Finalizer sống sót qua
            // reload nên luôn ghi được record. Các closure dưới chỉ còn lo gọi onDone cho window.
            //
            // KHÔNG được lọc theo tên gói để quyết định teardown: ở chế độ Dialog, Unity báo về
            // tên gói "nhúng" bên trong .unitypackage (tên lúc đóng gói gốc), KHÁC tên file tạm ta
            // tải về. Dùng cờ một-lần (_handled) + luôn Unsubscribe ở event đầu để tránh rò rỉ handler.
            string expectedName = Path.GetFileNameWithoutExtension(tempPath);
            bool handled = false;

            // Ghi pending TRƯỚC khi import: nếu domain reload xảy ra, finalizer sẽ finalize từ đây.
            string sha = !string.IsNullOrEmpty(asset.sha256) ? asset.sha256 : Sha256File(tempPath);
            FeatureHubImportFinalizer.AddPending(new PendingInstall
            {
                name = asset.name,
                fileName = asset.fileName,
                sha256 = sha,
                tempPath = tempPath,
            });

            AssetDatabase.ImportPackageCallback onCompleted = null;
            AssetDatabase.ImportPackageCallback onCancelled = null;
            AssetDatabase.ImportPackageFailedCallback onFailed = null;

            void Unsubscribe()
            {
                AssetDatabase.importPackageCompleted -= onCompleted;
                AssetDatabase.importPackageCancelled -= onCancelled;
                AssetDatabase.importPackageFailed -= onFailed;
            }

            onCompleted = packageName =>
            {
                if (handled)
                    return;
                handled = true;
                Unsubscribe();
                if (!IsExpected(packageName, expectedName))
                    Debug.Log($"[FeatureHub] Import hoàn tất '{packageName}' (khác tên file tạm '{expectedName}') — vẫn ghi nhận cho '{asset.name}'.");
                // Record + xóa temp do finalizer xử lý (đã subscribe trước nên chạy trước callback này).
                onDone?.Invoke(true, null);
            };

            onCancelled = packageName =>
            {
                if (handled)
                    return;
                handled = true;
                Unsubscribe();
                FeatureHubImportFinalizer.Drop(asset.name);
                onDone?.Invoke(false, "Đã hủy import.");
            };

            onFailed = (packageName, errorMessage) =>
            {
                if (handled)
                    return;
                handled = true;
                Unsubscribe();
                FeatureHubImportFinalizer.Drop(asset.name);
                onDone?.Invoke(false, $"Import lỗi: {errorMessage}");
            };

            AssetDatabase.importPackageCompleted += onCompleted;
            AssetDatabase.importPackageCancelled += onCancelled;
            AssetDatabase.importPackageFailed += onFailed;

            // interactive=false: import toàn bộ không hỏi; true: mở hộp thoại Import của Unity.
            AssetDatabase.ImportPackage(tempPath, interactive);

            // Chế độ Dialog: nếu user ĐÓNG/CANCEL hộp thoại bằng nút X, Unity KHÔNG luôn fire
            // importPackageCancelled -> không có terminal-event nào -> onDone không chạy -> window
            // kẹt 'busy'. Watchdog theo dõi cửa sổ PackageImport: khi nó đã hiện rồi biến mất mà
            // vẫn chưa có event, coi như hủy để gỡ kẹt UI.
            if (interactive)
            {
                WatchInteractiveDialog(
                    () => handled,
                    () =>
                    {
                        if (handled)
                            return;
                        handled = true;
                        Unsubscribe();
                        FeatureHubImportFinalizer.Drop(asset.name);
                        onDone?.Invoke(false, "Đã đóng hộp thoại import.");
                    });
            }
        }

        /// <summary>So tên gói Unity báo về với tên file tạm (chỉ để log; KHÔNG dùng để teardown).</summary>
        private static bool IsExpected(string reported, string expectedName) =>
            !string.IsNullOrEmpty(reported) &&
            string.Equals(Path.GetFileName(reported), expectedName, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Poll cửa sổ PackageImport của Unity. Khi nó đã mở rồi đóng mà chưa có terminal-event,
        /// gọi onClosedWithoutResult (user bấm X/Cancel mà Unity không fire callback).
        /// </summary>
        private static void WatchInteractiveDialog(Func<bool> isHandled, Action onClosedWithoutResult)
        {
            bool sawDialog = false;
            int grace = 0;

            void Tick()
            {
                if (isHandled())
                {
                    EditorApplication.update -= Tick;
                    return;
                }

                if (IsPackageImportOpen())
                {
                    sawDialog = true;
                    grace = 0;
                    return;
                }

                if (!sawDialog)
                    return; // dialog chưa kịp hiện — chờ tiếp.

                // Dialog đã đóng: chờ vài tick cho importPackageCompleted kịp fire (bấm Import cũng
                // đóng dialog) trước khi kết luận là hủy.
                if (++grace < 30)
                    return;

                EditorApplication.update -= Tick;
                if (!isHandled())
                    onClosedWithoutResult();
            }

            EditorApplication.update += Tick;
        }

        private static bool IsPackageImportOpen()
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (w != null && w.GetType().Name == "PackageImport")
                    return true;
            }

            return false;
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
            MergeRegistries(root, template?.scopedRegistries);
        }

        /// <summary>Merge danh sách registry vào manifest root (match theo url, union scopes). Idempotent.</summary>
        private static void MergeRegistries(JObject root, List<ScopedRegistry> registriesToAdd)
        {
            if (registriesToAdd == null || registriesToAdd.Count == 0)
                return;

            if (!(root[REGISTRIES_KEY] is JArray registries))
            {
                registries = new JArray();
                root[REGISTRIES_KEY] = registries;
            }

            foreach (var reg in registriesToAdd)
            {
                JObject existing = FindRegistryByUrl(registries, reg.url);
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

        /// <summary>Đọc mảng scopedRegistries hiện có trong manifest (rỗng nếu chưa có/đọc lỗi).</summary>
        private static JArray ReadManifestRegistries()
        {
            try
            {
                string path = ManifestPath();
                if (!File.Exists(path))
                    return new JArray();

                var root = JObject.Parse(File.ReadAllText(path));
                return root[REGISTRIES_KEY] as JArray ?? new JArray();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FeatureHub] Đọc scopedRegistries lỗi: {e.Message}");
                return new JArray();
            }
        }

        private static JObject FindRegistryByUrl(JArray registries, string url)
        {
            if (registries == null)
                return null;

            foreach (var item in registries)
            {
                if (item is JObject obj &&
                    string.Equals(obj["url"]?.ToString(), url, StringComparison.OrdinalIgnoreCase))
                    return obj;
            }

            return null;
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
