#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Ezg.Editor.Shared.EzgKit;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Ezg.Editor.Shared.Firebase
{
    /// <summary>
    ///     Một nút: tạo app Android + iOS trong project Firebase (nếu chưa có) rồi tải
    ///     <c>google-services.json</c> / <c>GoogleService-Info.plist</c> về đúng chỗ trong
    ///     <c>Assets/</c>. Thay cho việc vào Firebase console bấm tay 2 lần mỗi dự án mới.
    ///
    ///     <para>
    ///         <b>Chạy lại vô hại (idempotent):</b> app được nhận diện theo <c>packageName</c> /
    ///         <c>bundleId</c>. Đã có thì tool DÙNG LẠI chứ không tạo trùng — bấm bao nhiêu lần cũng chỉ
    ///         là tải lại config.
    ///     </para>
    ///     <para>
    ///         <b>Không thể hoàn tác:</b> Firebase KHÔNG cho sửa <c>packageName</c>/<c>bundleId</c> của
    ///         app sau khi tạo, cũng không cho tái sử dụng id của app đã xoá. Vì thế
    ///         <see cref="MenuRun" /> luôn chạy dry-run trước và hỏi lại, và tool đọc id từ
    ///         PlayerSettings (thứ đi vào bản build thật) chứ không cho gõ tay.
    ///     </para>
    ///     <para>
    ///         <b>Sau khi ghi config:</b> <c>Firebase.Editor.dll</c> có một AssetPostprocessor
    ///         (<c>GenerateXmlFromGoogleServicesJson</c>) tự dựng lại
    ///         <c>Assets/Plugins/Android/FirebaseApp.androidlib/res/values/google-services.xml</c> từ
    ///         file JSON. Tool KHÔNG tự ghi file xml đó, chỉ đọc lại để kiểm tra generator đã chạy —
    ///         xml lệch project id nghĩa là build sẽ bắn số liệu sang project khác.
    ///     </para>
    /// </summary>
    internal static class FirebaseAppProvisioner
    {
        #region Constants

        private const string API = "https://firebase.googleapis.com/v1beta1";
        private const string CRM = "https://cloudresourcemanager.googleapis.com/v1";

        private const string ANDROID_CONFIG_ASSET = "google-services.json";
        private const string IOS_CONFIG_ASSET = "GoogleService-Info.plist";

        private const string GENERATED_XML_PATH =
            "Assets/Plugins/Android/FirebaseApp.androidlib/res/values/google-services.xml";

        /// <summary>Tạo app xong Google trả về long-running operation; thực tế xong trong vài giây.</summary>
        private const int OPERATION_TIMEOUT_SECONDS = 60;

        private const int OPERATION_POLL_MS = 2000;

        /// <summary>Package mặc định của Unity — dấu hiệu chưa ai đặt id thật cho dự án.</summary>
        private static readonly string[] PLACEHOLDER_IDS =
        {
            "com.Company.ProductName", "com.DefaultCompany", "com.unity3d",
        };

        #endregion

        #region Types

        // Phan hoi cua API: JsonUtility gan qua reflection nen compiler khong thay cho nao assign.
#pragma warning disable CS0649

        [Serializable]
        private class AndroidApp
        {
            public string name;
            public string appId;
            public string displayName;
            public string packageName;
        }

        [Serializable]
        private class IosApp
        {
            public string name;
            public string appId;
            public string displayName;
            public string bundleId;
        }

        [Serializable]
        private class AndroidAppList
        {
            public List<AndroidApp> apps;
        }

        [Serializable]
        private class IosAppList
        {
            public List<IosApp> apps;
        }

        [Serializable]
        private class ConfigResponse
        {
            public string configFilename;
            public string configFileContents;
        }

        [Serializable]
        private class OperationStatus
        {
            public string name;
            public bool done;
            public OperationError error;
        }

        [Serializable]
        private class OperationError
        {
            public int code;
            public string message;
        }

        [Serializable]
        private class ShaCertificate
        {
            public string name;
            public string shaHash;
            public string certType;
        }

        [Serializable]
        private class ShaList
        {
            public List<ShaCertificate> certificates;
        }

        /// <summary>Một dòng của <c>GET /v1beta1/projects</c>.</summary>
        [Serializable]
        private class ProjectSummary
        {
            public string projectId;
            public string displayName;
        }

        /// <summary>
        ///     Bọc mảng của <c>GET /v1beta1/projects</c>. PHẢI có lớp bọc: <see cref="JsonUtility" />
        ///     không parse được mảng ở top level, mà endpoint này trả về
        ///     <c>{"results":[{"projectId":...}]}</c> nên tên field phải đúng là <c>results</c>.
        /// </summary>
        [Serializable]
        private class ProjectList
        {
            public ProjectSummary[] results;
        }

#pragma warning restore CS0649

        [Serializable]
        private class CreateAndroidRequest
        {
            public string displayName;
            public string packageName;
        }

        [Serializable]
        private class CreateIosRequest
        {
            public string displayName;
            public string bundleId;
        }

        [Serializable]
        private class CreateIosWithStoreRequest
        {
            public string displayName;
            public string bundleId;
            public string appStoreId;
        }

        [Serializable]
        private class CreateProjectRequest
        {
            public string projectId;
            public string name;
        }

        #endregion

        #region Menu

        [MenuItem("Ezg/Firebase/Tao app + tai config (1 Click)", false, 99)]
        private static void MenuRun()
        {
            if (!Run(true, null, out var plan))
            {
                EditorUtility.DisplayDialog("Firebase", plan, "Dong");
                EzgKitWindow.Open(EzgKitWindow.Tab.Firebase);
                return;
            }

            if (!EditorUtility.DisplayDialog("Firebase - xac nhan",
                    plan + "\n\nLuu y: packageName / bundleId cua app Firebase KHONG sua duoc sau khi tao.",
                    "Lam di", "Huy"))
                return;

            Run(false, null, out var report);
            EditorUtility.DisplayDialog("Firebase", report, "Dong");
        }

        [MenuItem("Ezg/Firebase/Kiem tra (Dry Run)", false, 120)]
        private static void MenuDryRun()
        {
            Run(true, null, out var report);
            EditorUtility.DisplayDialog("Firebase - dry run", report, "Dong");
        }

        // Menu "Cai dat..." nam trong EzgKitWindow - no la chu cua cua so do.

        #endregion

        #region Public API

        internal static string AndroidPackage => PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);

        internal static string IosBundle => PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS);

        internal static bool AndroidConfigExists =>
            File.Exists(Path.Combine(Application.dataPath, ANDROID_CONFIG_ASSET));

        internal static bool IosConfigExists =>
            File.Exists(Path.Combine(Application.dataPath, IOS_CONFIG_ASSET));

        /// <summary>
        ///     <c>project_id</c> trong <c>google-services.json</c> đang nằm ở <c>Assets/</c>
        ///     (null = chưa có file / không đọc được). Dùng để phát hiện config đi theo code-template của
        ///     DỰ ÁN KHÁC: file vẫn tồn tại, build vẫn chạy, số liệu bắn sang project người ta.
        ///     Chỉ đọc file, không gọi API.
        /// </summary>
        internal static string LocalAndroidConfigProjectId =>
            MatchInFile(ANDROID_CONFIG_ASSET, "\"project_id\"\\s*:\\s*\"([^\"]+)\"");

        /// <summary><c>PROJECT_ID</c> trong <c>GoogleService-Info.plist</c> đang nằm ở <c>Assets/</c>.</summary>
        internal static string LocalIosConfigProjectId =>
            MatchInFile(IOS_CONFIG_ASSET, "<key>PROJECT_ID</key>\\s*<string>([^<]+)</string>");

        /// <summary>App id Android (<c>mobilesdk_app_id</c>) trong config đang nằm ở <c>Assets/</c>.</summary>
        internal static string LocalAndroidAppId =>
            MatchInFile(ANDROID_CONFIG_ASSET, "\"mobilesdk_app_id\"\\s*:\\s*\"([^\"]+)\"");

        /// <summary>App id iOS (<c>GOOGLE_APP_ID</c>) trong plist đang nằm ở <c>Assets/</c>.</summary>
        internal static string LocalIosAppId =>
            MatchInFile(IOS_CONFIG_ASSET, "<key>GOOGLE_APP_ID</key>\\s*<string>([^<]+)</string>");

        /// <summary>
        ///     <c>project_id</c> trong file xml mà generator của Firebase sinh ra. Đây là giá trị THẬT SỰ
        ///     đi vào bản build Android — lệch với <c>google-services.json</c> nghĩa là generator chưa
        ///     chạy lại, và bản build sẽ bắn số liệu theo file xml chứ không theo file json.
        /// </summary>
        internal static string GeneratedXmlProjectId =>
            MatchInProjectFile(GENERATED_XML_PATH, "name=\"project_id\"[^>]*>([^<]+)<");

        private static string MatchInFile(string assetRelativePath, string pattern) =>
            MatchInProjectFile("Assets/" + assetRelativePath, pattern);

        /// <summary><paramref name="projectRelativePath" /> tính từ thư mục chứa <c>Assets/</c>.</summary>
        private static string MatchInProjectFile(string projectRelativePath, string pattern)
        {
            var path = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                projectRelativePath);
            if (!File.Exists(path)) return null;

            try
            {
                var match = Regex.Match(File.ReadAllText(path), pattern);
                return match.Success ? match.Groups[1].Value.Trim() : null;
            }
            catch (Exception)
            {
                // File hỏng/đang bị lock: coi như không biết, KHÔNG được chặn cửa sổ mở lên.
                return null;
            }
        }

        /// <summary>
        ///     Chạy toàn bộ luồng. <paramref name="dryRun" /> chỉ ĐỌC (GET) — không tạo app, không ghi
        ///     file. <paramref name="shaHash" /> rỗng thì bỏ qua bước đăng ký SHA.
        ///     Trả về false nghĩa là có lỗi (đã nằm trong <paramref name="report" />).
        /// </summary>
        internal static bool Run(bool dryRun, string shaHash, out string report)
        {
            var log = new StringBuilder();
            log.AppendLine(dryRun
                ? "[Firebase] DRY RUN - chua tao gi, chua ghi gi."
                : "[Firebase] THUC THI.");

            var source = FirebaseSource.Load();
            var errors = new List<string>();

            if (!ValidateInputs(source, log, errors)) return Finish(log, errors, out report);

            if (!FirebaseServiceAccount.TryLoad(FirebaseSource.KeyPath, out var key, out var keyError))
            {
                errors.Add(keyError);
                return Finish(log, errors, out report);
            }

            log.AppendLine($"Service account : {key.client_email}");

            var scope = source.createProjectIfMissing
                ? FirebaseServiceAccount.SCOPE_FIREBASE + " " + FirebaseServiceAccount.SCOPE_CLOUD_PLATFORM
                : FirebaseServiceAccount.SCOPE_FIREBASE;

            if (!FirebaseServiceAccount.TryGetAccessToken(key, scope, out var token, out var tokenError))
            {
                errors.Add(tokenError);
                return Finish(log, errors, out report);
            }

            if (!EnsureProject(source, token, dryRun, log, errors)) return Finish(log, errors, out report);

            var androidAppId = EnsureAndroidApp(source, token, dryRun, log, errors);
            var iosAppId = EnsureIosApp(source, token, dryRun, log, errors);

            if (!dryRun)
            {
                if (androidAppId != null)
                    DownloadConfig($"{API}/projects/{source.projectId}/androidApps/{androidAppId}/config",
                        ANDROID_CONFIG_ASSET, token, log, errors);
                if (iosAppId != null)
                    DownloadConfig($"{API}/projects/{source.projectId}/iosApps/{iosAppId}/config",
                        IOS_CONFIG_ASSET, token, log, errors);

                if (androidAppId != null || iosAppId != null) AssetDatabase.Refresh();
                if (androidAppId != null) VerifyGeneratedXml(source.projectId, androidAppId, log, errors);
            }

            if (!string.IsNullOrEmpty(shaHash) && androidAppId != null)
                EnsureSha(source.projectId, androidAppId, shaHash, token, dryRun, log, errors);

            AppendManualTodos(log, source);
            return Finish(log, errors, out report);
        }

        /// <summary>
        ///     Liệt kê project mà service account này truy cập được. Chỉ GET, không tạo gì, không ghi
        ///     gì, không đụng <c>AssetDatabase</c>.
        ///     <para>
        ///         Dùng cho ca CROSS-PROJECT: file key mang <c>project_id</c> của project chứa service
        ///         account, nhưng account đó có thể được gán role trên project khác — lúc ấy project id
        ///         cần khai KHÔNG nằm trong file key, và đây là cách lấy danh sách mà không phải gõ tay.
        ///     </para>
        ///     Trả về false kèm <paramref name="error" /> đọc được (thông điệp của Google đã được
        ///     <see cref="FirebaseRest" /> bóc sẵn: thiếu quyền / chưa bật API / key hỏng).
        /// </summary>
        internal static bool TryListProjects(out List<string> projectIds, out string error)
        {
            projectIds = new List<string>();

            if (!FirebaseServiceAccount.TryLoad(FirebaseSource.KeyPath, out var key, out error))
                return false;

            if (!FirebaseServiceAccount.TryGetAccessToken(key, FirebaseServiceAccount.SCOPE_FIREBASE,
                    out var token, out error))
                return false;

            if (!FirebaseRest.Get($"{API}/projects", token, "Dang do project...", out var body, out error))
            {
                error = "Do project that bai: " + error
                        + "\nThuong la chua bat Firebase Management API tren project cua service account, "
                        + "hoac key thieu role roles/firebase.admin.";
                return false;
            }

            var list = SafeParse<ProjectList>(body);
            if (list?.results != null)
                foreach (var project in list.results)
                    if (!string.IsNullOrEmpty(project.projectId))
                        projectIds.Add(project.projectId);

            if (projectIds.Count > 0) return true;

            error = $"Service account {key.client_email} khong thay project Firebase nao. Gan role "
                    + "roles/firebase.admin cho account nay tren project can dung (Cloud Console > IAM), "
                    + "roi bam lai.";
            return false;
        }

        #endregion

        #region Steps

        private static bool ValidateInputs(FirebaseSource source, StringBuilder log, List<string> errors)
        {
            log.AppendLine($"Project id      : {source.projectId}");
            log.AppendLine($"Android package : {AndroidPackage}");
            log.AppendLine($"iOS bundle      : {IosBundle}");
            log.AppendLine();

            if (string.IsNullOrEmpty(source.projectId))
                errors.Add("Chua co project id. Mo Ezg > EzgKit > tab Firebase roi chon file service "
                           + "account key (.json) - project id tu lay trong file do. Key thuoc project "
                           + "khac project can dung thi bam 'Do project kha dung' de chon.");

            foreach (var pair in new[]
                     {
                         ("Android", AndroidPackage),
                         ("iOS", IosBundle),
                     })
            {
                if (string.IsNullOrEmpty(pair.Item2))
                {
                    errors.Add($"PlayerSettings thieu application identifier cho {pair.Item1}.");
                    continue;
                }

                foreach (var placeholder in PLACEHOLDER_IDS)
                {
                    if (!pair.Item2.StartsWith(placeholder, StringComparison.OrdinalIgnoreCase)) continue;

                    errors.Add($"Identifier {pair.Item1} con la placeholder cua Unity ({pair.Item2}) - "
                               + "dat id that trong PlayerSettings truoc, vi Firebase khong cho sua sau "
                               + "khi tao app.");
                }
            }

            return errors.Count == 0;
        }

        private static bool EnsureProject(FirebaseSource source, string token, bool dryRun,
            StringBuilder log, List<string> errors)
        {
            if (FirebaseRest.Get($"{API}/projects/{source.projectId}", token,
                    "Dang kiem project Firebase...", out _, out var error))
            {
                log.AppendLine($"Project         : co san ({source.projectId})");
                return true;
            }

            if (!FirebaseRest.IsNotFoundOrForbidden(error))
            {
                errors.Add("Doc project that bai: " + error);
                return false;
            }

            if (!source.createProjectIfMissing)
            {
                errors.Add($"Khong thay project '{source.projectId}' (hoac service account khong co quyen "
                           + "tren no). Kiem lai project id, gan role roles/firebase.admin cho service "
                           + "account, hoac bat 'Tao project neu chua co' trong tab Firebase cua "
                           + "Ezg > EzgKit.\n"
                           + "Chi tiet: " + error);
                return false;
            }

            if (dryRun)
            {
                log.AppendLine($"Project         : SE TAO MOI ({source.projectId}) + bat Firebase");
                return true;
            }

            return CreateProject(source, token, log, errors);
        }

        /// <summary>
        ///     Tạo GCP project rồi bật Firebase lên nó. Cần quyền cấp tổ chức
        ///     (<c>roles/resourcemanager.projectCreator</c>) — service account thường KHÔNG có, nên lỗi ở
        ///     đây là chuyện bình thường và thông điệp của Google nói rõ thiếu gì.
        /// </summary>
        private static bool CreateProject(FirebaseSource source, string token, StringBuilder log,
            List<string> errors)
        {
            var request = new CreateProjectRequest
            {
                projectId = source.projectId,
                name = string.IsNullOrEmpty(PlayerSettings.productName)
                    ? source.projectId
                    : PlayerSettings.productName,
            };

            if (!FirebaseRest.PostJson($"{CRM}/projects", token, JsonUtility.ToJson(request),
                    "Dang tao GCP project...", out var body, out var error))
            {
                errors.Add("Tao GCP project that bai: " + error);
                return false;
            }

            if (!PollOperation(CRM, body, token, "Dang tao GCP project...", out var pollError))
            {
                errors.Add("Tao GCP project that bai: " + pollError);
                return false;
            }

            if (!FirebaseRest.PostJson($"{API}/projects/{source.projectId}:addFirebase", token, "{}",
                    "Dang bat Firebase...", out var addBody, out var addError))
            {
                errors.Add("Bat Firebase tren project that bai: " + addError);
                return false;
            }

            if (!PollOperation(API, addBody, token, "Dang bat Firebase...", out var addPollError))
            {
                errors.Add("Bat Firebase tren project that bai: " + addPollError);
                return false;
            }

            log.AppendLine($"Project         : DA TAO ({source.projectId})");
            return true;
        }

        /// <summary>Trả về appId của app Android, hoặc null nếu chưa có (dry run) / lỗi.</summary>
        private static string EnsureAndroidApp(FirebaseSource source, string token, bool dryRun,
            StringBuilder log, List<string> errors)
        {
            var listUrl = $"{API}/projects/{source.projectId}/androidApps?pageSize=100";
            if (!FirebaseRest.Get(listUrl, token, "Dang liet ke app Android...", out var body, out var error))
            {
                errors.Add("Liet ke app Android that bai: " + error);
                return null;
            }

            var existing = FindAndroid(body, AndroidPackage);
            if (existing != null)
            {
                log.AppendLine($"App Android     : dung lai '{existing.displayName}' ({existing.appId})");
                return existing.appId;
            }

            if (dryRun)
            {
                log.AppendLine($"App Android     : SE TAO MOI - {AndroidPackage} "
                               + $"('{source.ResolvedAndroidName}')");
                return null;
            }

            var request = new CreateAndroidRequest
            {
                displayName = source.ResolvedAndroidName,
                packageName = AndroidPackage,
            };

            if (!FirebaseRest.PostJson($"{API}/projects/{source.projectId}/androidApps", token,
                    JsonUtility.ToJson(request), "Dang tao app Android...", out var createBody,
                    out var createError))
            {
                errors.Add("Tao app Android that bai: " + createError);
                return null;
            }

            if (!PollOperation(API, createBody, token, "Dang tao app Android...", out var pollError))
            {
                errors.Add("Tao app Android that bai: " + pollError);
                return null;
            }

            // Đọc lại danh sách thay vì parse response của operation: hình dạng LRO của Google có thể
            // đổi, còn danh sách app thì không.
            if (!FirebaseRest.Get(listUrl, token, "Dang doc lai app Android...", out var afterBody,
                    out var afterError))
            {
                errors.Add("Doc lai app Android that bai: " + afterError);
                return null;
            }

            var created = FindAndroid(afterBody, AndroidPackage);
            if (created == null)
            {
                errors.Add($"Da tao app Android nhung khong thay lai trong danh sach ({AndroidPackage}).");
                return null;
            }

            log.AppendLine($"App Android     : DA TAO '{created.displayName}' ({created.appId})");
            return created.appId;
        }

        /// <summary>Trả về appId của app iOS, hoặc null nếu chưa có (dry run) / lỗi.</summary>
        private static string EnsureIosApp(FirebaseSource source, string token, bool dryRun,
            StringBuilder log, List<string> errors)
        {
            var listUrl = $"{API}/projects/{source.projectId}/iosApps?pageSize=100";
            if (!FirebaseRest.Get(listUrl, token, "Dang liet ke app iOS...", out var body, out var error))
            {
                errors.Add("Liet ke app iOS that bai: " + error);
                return null;
            }

            var existing = FindIos(body, IosBundle);
            if (existing != null)
            {
                log.AppendLine($"App iOS         : dung lai '{existing.displayName}' ({existing.appId})");
                return existing.appId;
            }

            if (dryRun)
            {
                log.AppendLine($"App iOS         : SE TAO MOI - {IosBundle} ('{source.ResolvedIosName}')");
                return null;
            }

            var json = string.IsNullOrEmpty(source.appStoreId)
                ? JsonUtility.ToJson(new CreateIosRequest
                {
                    displayName = source.ResolvedIosName,
                    bundleId = IosBundle,
                })
                : JsonUtility.ToJson(new CreateIosWithStoreRequest
                {
                    displayName = source.ResolvedIosName,
                    bundleId = IosBundle,
                    appStoreId = source.appStoreId,
                });

            if (!FirebaseRest.PostJson($"{API}/projects/{source.projectId}/iosApps", token, json,
                    "Dang tao app iOS...", out var createBody, out var createError))
            {
                errors.Add("Tao app iOS that bai: " + createError);
                return null;
            }

            if (!PollOperation(API, createBody, token, "Dang tao app iOS...", out var pollError))
            {
                errors.Add("Tao app iOS that bai: " + pollError);
                return null;
            }

            if (!FirebaseRest.Get(listUrl, token, "Dang doc lai app iOS...", out var afterBody,
                    out var afterError))
            {
                errors.Add("Doc lai app iOS that bai: " + afterError);
                return null;
            }

            var created = FindIos(afterBody, IosBundle);
            if (created == null)
            {
                errors.Add($"Da tao app iOS nhung khong thay lai trong danh sach ({IosBundle}).");
                return null;
            }

            log.AppendLine($"App iOS         : DA TAO '{created.displayName}' ({created.appId})");
            return created.appId;
        }

        private static void DownloadConfig(string url, string assetName, string token, StringBuilder log,
            List<string> errors)
        {
            if (!FirebaseRest.Get(url, token, $"Dang tai {assetName}...", out var body, out var error))
            {
                errors.Add($"Tai {assetName} that bai: {error}");
                return;
            }

            ConfigResponse response;
            try
            {
                response = JsonUtility.FromJson<ConfigResponse>(body);
            }
            catch (Exception exception)
            {
                errors.Add($"Phan hoi config {assetName} khong doc duoc: {exception.Message}");
                return;
            }

            if (string.IsNullOrEmpty(response?.configFileContents))
            {
                errors.Add($"Phan hoi config {assetName} rong.");
                return;
            }

            byte[] content;
            try
            {
                content = Convert.FromBase64String(response.configFileContents);
            }
            catch (FormatException exception)
            {
                errors.Add($"Config {assetName} khong phai base64: {exception.Message}");
                return;
            }

            var path = Path.Combine(Application.dataPath, assetName);
            var existed = File.Exists(path);
            if (existed && SameBytes(File.ReadAllBytes(path), content))
            {
                log.AppendLine($"{assetName,-24}: khong doi");
                return;
            }

            File.WriteAllBytes(path, content);
            log.AppendLine($"{assetName,-24}: {(existed ? "da ghi de" : "da tao moi")} -> Assets/{assetName}");

            // Import ĐỒNG BỘ: AssetPostprocessor của Firebase phải chạy NGAY để VerifyGeneratedXml đọc
            // được file xml mới, không thì bước kiểm báo lệch oan ở lần chạy đầu.
            AssetDatabase.ImportAsset($"Assets/{assetName}",
                ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        }

        /// <summary>
        ///     Đăng ký SHA-1/SHA-256 của keystore — cần cho Google Sign-In, Play Integrity, Dynamic Links.
        ///     Không có thì Firebase vẫn chạy analytics/crashlytics bình thường.
        /// </summary>
        private static void EnsureSha(string projectId, string androidAppId, string shaHash, string token,
            bool dryRun, StringBuilder log, List<string> errors)
        {
            var normalized = shaHash.Replace(":", string.Empty).Replace(" ", string.Empty).Trim()
                .ToLowerInvariant();
            var certType = normalized.Length switch
            {
                40 => "SHA_1",
                64 => "SHA_256",
                _ => null,
            };

            if (certType == null)
            {
                errors.Add($"SHA khong dung do dai (40 ky tu = SHA-1, 64 = SHA-256), dang co "
                           + $"{normalized.Length}.");
                return;
            }

            var url = $"{API}/projects/{projectId}/androidApps/{androidAppId}/sha";
            if (!FirebaseRest.Get(url, token, "Dang doc SHA da dang ky...", out var body, out var error))
            {
                errors.Add("Doc danh sach SHA that bai: " + error);
                return;
            }

            if (body != null && body.ToLowerInvariant().Contains(normalized))
            {
                log.AppendLine($"SHA             : da co ({certType})");
                return;
            }

            if (dryRun)
            {
                log.AppendLine($"SHA             : SE DANG KY {certType}");
                return;
            }

            var request = "{\"shaHash\":\"" + normalized + "\",\"certType\":\"" + certType + "\"}";
            if (!FirebaseRest.PostJson(url, token, request, "Dang dang ky SHA...", out _, out var postError))
            {
                errors.Add("Dang ky SHA that bai: " + postError);
                return;
            }

            log.AppendLine($"SHA             : DA DANG KY {certType}");
        }

        /// <summary>
        ///     Kiểm generator của Firebase đã dựng lại file xml chưa. Đây là chỗ lỗi im lặng nguy hiểm
        ///     nhất: xml cũ còn trỏ sang project khác thì build vẫn chạy, vẫn có số liệu — chỉ là số liệu
        ///     chảy sang project của dự án khác.
        /// </summary>
        private static void VerifyGeneratedXml(string projectId, string androidAppId, StringBuilder log,
            List<string> errors)
        {
            var path = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                GENERATED_XML_PATH);
            if (!File.Exists(path))
            {
                log.AppendLine($"XML androidlib  : chua co ({GENERATED_XML_PATH}) - se sinh khi build.");
                return;
            }

            var xml = File.ReadAllText(path);
            var actualProject = Regex.Match(xml, @"name=""project_id""[^>]*>([^<]+)<").Groups[1].Value;
            var actualAppId = Regex.Match(xml, @"name=""google_app_id""[^>]*>([^<]+)<").Groups[1].Value;

            if (actualProject == projectId && actualAppId == androidAppId)
            {
                log.AppendLine($"XML androidlib  : khop ({actualProject})");
                return;
            }

            errors.Add($"{GENERATED_XML_PATH} van dang tro sang project '{actualProject}' "
                       + $"(app {actualAppId}) thay vi '{projectId}' (app {androidAppId}). "
                       + "Chon Assets/google-services.json > Reimport de generator cua Firebase chay lai; "
                       + "neu van lech thi build Android se ban so lieu sang project khac.");
        }

        #endregion

        #region Helpers

        private static AndroidApp FindAndroid(string body, string packageName)
        {
            var list = SafeParse<AndroidAppList>(body);
            if (list?.apps == null) return null;

            foreach (var app in list.apps)
                if (string.Equals(app.packageName, packageName, StringComparison.Ordinal))
                    return app;
            return null;
        }

        private static IosApp FindIos(string body, string bundleId)
        {
            var list = SafeParse<IosAppList>(body);
            if (list?.apps == null) return null;

            foreach (var app in list.apps)
                if (string.Equals(app.bundleId, bundleId, StringComparison.Ordinal))
                    return app;
            return null;
        }

        private static T SafeParse<T>(string body) where T : class
        {
            if (string.IsNullOrEmpty(body)) return null;

            try
            {
                return JsonUtility.FromJson<T>(body);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Firebase] Phan hoi khong doc duoc ({typeof(T).Name}): {exception.Message}");
                return null;
            }
        }

        /// <summary>
        ///     Chờ long-running operation xong. <paramref name="apiRoot" /> là gốc API tương ứng vì
        ///     <c>operation.name</c> của Google là đường dẫn tương đối.
        /// </summary>
        private static bool PollOperation(string apiRoot, string createBody, string token, string progress,
            out string error)
        {
            error = null;

            var operation = SafeParse<OperationStatus>(createBody);
            if (operation == null)
            {
                error = "Phan hoi tao khong doc duoc.";
                return false;
            }

            if (operation.error != null && !string.IsNullOrEmpty(operation.error.message))
            {
                error = operation.error.message;
                return false;
            }

            if (operation.done || string.IsNullOrEmpty(operation.name)) return true;

            var deadline = DateTime.UtcNow.AddSeconds(OPERATION_TIMEOUT_SECONDS);
            while (DateTime.UtcNow < deadline)
            {
                if (!Sleep(progress))
                {
                    error = "Nguoi dung huy.";
                    return false;
                }

                if (!FirebaseRest.Get($"{apiRoot}/{operation.name}", token, progress, out var body,
                        out var getError))
                {
                    error = getError;
                    return false;
                }

                var status = SafeParse<OperationStatus>(body);
                if (status?.error != null && !string.IsNullOrEmpty(status.error.message))
                {
                    error = status.error.message;
                    return false;
                }

                if (status is { done: true }) return true;
            }

            error = $"Qua {OPERATION_TIMEOUT_SECONDS}s van chua xong. Vao Firebase console kiem xem app da "
                    + "tao chua roi bam lai (tool dung lai app da co, khong tao trung).";
            return false;
        }

        /// <summary>Ngủ giữa 2 lần poll, chia nhỏ để progress bar còn huỷ được. false = user huỷ.</summary>
        private static bool Sleep(string progress)
        {
            const int step = 200;
            for (var elapsed = 0; elapsed < OPERATION_POLL_MS; elapsed += step)
            {
                if (EditorUtility.DisplayCancelableProgressBar("Firebase", progress,
                        (float)elapsed / OPERATION_POLL_MS))
                {
                    EditorUtility.ClearProgressBar();
                    return false;
                }

                Thread.Sleep(step);
            }

            EditorUtility.ClearProgressBar();
            return true;
        }

        private static bool SameBytes(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;

            for (var i = 0; i < left.Length; i++)
                if (left[i] != right[i])
                    return false;
            return true;
        }

        /// <summary>
        ///     Những thứ Management API KHÔNG có endpoint — liệt kê ra để "bấm 1 nút" không bị hiểu nhầm
        ///     là đã xong 100% (cùng tinh thần <c>MarketingConfigApplier.CollectManualTodos</c>).
        /// </summary>
        private static void AppendManualTodos(StringBuilder log, FirebaseSource source)
        {
            log.AppendLine();
            log.AppendLine("CON PHAI LAM TAY - khong co API, cap them quyen cung khong mo ra duoc:");
            log.AppendLine("  - Upload APNs auth key (.p8) cho push iOS: console > Project settings > "
                           + "Cloud Messaging. Firebase khong co endpoint cho viec nay.");
            log.AppendLine("  - Tao app record tren App Store Connect (ASC API khong co POST /v1/apps) "
                           + "va tao app tren Play Console (Play API chi quan app da co).");
            log.AppendLine("CO API nhung tool CHUA lam (xem tab Firebase):");
            log.AppendLine("  - Nang Blaze / link billing (Cloud Billing API), tao GA4 property + "
                           + "firebaseLink (Analytics Admin API), day default Remote Config.");
            if (string.IsNullOrEmpty(source.appStoreId))
                log.AppendLine("  - Chua co Apple ID (appStoreId) -> app iOS trong Firebase chua link "
                               + "App Store; dien vao tab Firebase cua Ezg > EzgKit roi bam lai.");
        }

        private static bool Finish(StringBuilder log, List<string> errors, out string report)
        {
            if (errors.Count > 0)
            {
                log.AppendLine();
                log.AppendLine($"LOI ({errors.Count}):");
                foreach (var error in errors) log.AppendLine("  - " + error);
            }

            report = log.ToString();
            if (errors.Count > 0) Debug.LogError(report);
            else Debug.Log(report);
            return errors.Count == 0;
        }

        #endregion
    }
}
#endif
