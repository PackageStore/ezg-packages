#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Ezg.Editor.Shared.EzgKit;
using UnityEditor;
using UnityEngine;

namespace Ezg.Editor.Shared.Firebase
{
    /// <summary>
    ///     Phần logic của trang Firebase tách khỏi UI (bản 0.x nằm trong <c>FirebaseSetupPage</c>): đọc file
    ///     key, phát hiện config của dự án khác, lấy SHA-1 bằng keytool, tạo / sửa <c>FirebaseConfig.asset</c>.
    /// </summary>
    internal static class FirebaseSetupHelpers
    {
        #region Key

        internal sealed class KeyInfo
        {
            internal string Email;
            internal string ProjectId;
            internal string Error;
        }

        /// <summary>Chỉ đọc file key để hiện thông tin — KHÔNG gọi API, không xin token.</summary>
        internal static KeyInfo ReadKey(string path)
        {
            if (string.IsNullOrEmpty(path)) return new KeyInfo { Error = "Chưa chọn file service account." };
            if (!FirebaseServiceAccount.TryLoad(path, out var key, out var error)) return new KeyInfo { Error = error };
            return new KeyInfo { Email = key.client_email, ProjectId = key.project_id };
        }

        #endregion

        #region Mismatch

        /// <summary>
        ///     Config đang nằm trong <c>Assets/</c> có thể là của DỰ ÁN KHÁC (đi theo template) — build vẫn chạy, số
        ///     liệu bắn sang project người ta. So project id offline; null = không lệch.
        /// </summary>
        internal static string Mismatch(string expected)
        {
            if (string.IsNullOrEmpty(expected)) return null;

            var android = FirebaseAppProvisioner.LocalAndroidConfigProjectId;
            if (!string.IsNullOrEmpty(android) && android != expected)
                return $"google-services.json đang trỏ sang project '{android}' chứ không phải '{expected}' — build sẽ bắn số liệu sang dự án khác.";

            var ios = FirebaseAppProvisioner.LocalIosConfigProjectId;
            if (!string.IsNullOrEmpty(ios) && ios != expected)
                return $"GoogleService-Info.plist đang trỏ sang project '{ios}' chứ không phải '{expected}'.";

            var xml = FirebaseAppProvisioner.GeneratedXmlProjectId;
            if (!string.IsNullOrEmpty(xml) && xml != expected)
                return $"google-services.xml trong FirebaseApp.androidlib còn trỏ sang project '{xml}'. Reimport Assets/google-services.json để generator chạy lại.";

            return null;
        }

        #endregion

        #region FirebaseConfig asset (com.ezg.firebase)

        internal const string CONFIG_TYPE = "FirebaseConfig";
        internal const string CONFIG_PATH = "Assets/_Project/Resources/FirebaseConfig.asset";

        /// <summary><c>gs://&lt;storage_bucket&gt;</c> lấy từ google-services.json; null = chưa có json.</summary>
        internal static string ExpectedBucket()
        {
            var path = Path.Combine(Application.dataPath, "google-services.json");
            if (!File.Exists(path)) return null;
            var match = Regex.Match(File.ReadAllText(path), "\"storage_bucket\"\\s*:\\s*\"([^\"]+)\"");
            return match.Success ? "gs://" + match.Groups[1].Value.Trim() : null;
        }

        internal static ScriptableObject FindConfig() => Resources.Load<ScriptableObject>(CONFIG_TYPE);

        internal static bool ConfigTypeExists => TypeFinder.Find(CONFIG_TYPE, typeof(ScriptableObject)) != null;

        /// <summary>
        ///     Tạo <c>FirebaseConfig.asset</c> (nếu chưa có) và đặt <c>storageBucketUrl</c> theo json. Package
        ///     com.ezg.firebase mặc định trỏ bucket của một game khác — thiếu asset là save-sync ghi nhầm chỗ.
        /// </summary>
        internal static bool EnsureConfig(string bucket, bool dryRun, List<ChangeRow> changes, out string error)
        {
            error = null;
            var type = TypeFinder.Find(CONFIG_TYPE, typeof(ScriptableObject));
            if (type == null)
            {
                error = "Dự án chưa cài com.ezg.firebase (không có type FirebaseConfig).";
                return false;
            }

            var asset = FindConfig();
            if (asset == null)
            {
                changes?.Add(new ChangeRow("FirebaseConfig", "(asset)", "chưa có", CONFIG_PATH, false));
                if (dryRun)
                {
                    changes?.Add(new ChangeRow("FirebaseConfig", "storageBucketUrl", string.Empty, bucket ?? string.Empty, false));
                    return true;
                }

                AppSecretsSink.EnsureFolder(Path.GetDirectoryName(CONFIG_PATH)?.Replace('\\', '/'));
                asset = ScriptableObject.CreateInstance(type);
                AssetDatabase.CreateAsset(asset, CONFIG_PATH);
                AssetDatabase.SaveAssets();
            }

            if (string.IsNullOrEmpty(bucket)) return true;
            Setup.SerializedAsset.Write(asset, "FirebaseConfig", new List<(string, object, bool)> { ("storageBucketUrl", bucket, false) },
                dryRun, changes);
            return true;
        }

        #endregion

        #region SHA-1 (keytool)

        /// <summary>
        ///     Chạy <c>keytool -list</c> trên keystore đang khai trong PlayerSettings để lấy SHA-1. Mật khẩu chỉ
        ///     đi qua tham số của tiến trình con. Trả về SHA hoặc null + <paramref name="message" /> giải thích.
        /// </summary>
        internal static string ReadShaFromKeystore(string keystorePass, out string message)
        {
            var keystore = PlayerSettings.Android.keystoreName;
            var alias = PlayerSettings.Android.keyaliasName;

            if (string.IsNullOrEmpty(keystore))
            {
                message = "PlayerSettings chưa khai keystore (Publishing Settings > Custom Keystore).";
                return null;
            }

            var marker = keystore.IndexOf("}: ", StringComparison.Ordinal);
            if (marker >= 0) keystore = keystore.Substring(marker + 3);
            if (!Path.IsPathRooted(keystore)) keystore = Path.Combine(ProjectPaths.Root, keystore);

            if (!File.Exists(keystore))
            {
                message = $"Không thấy keystore: {keystore}";
                return null;
            }

            if (string.IsNullOrEmpty(keystorePass))
            {
                message = "Chưa có mật khẩu keystore.";
                return null;
            }

            var arguments = $"-list -v -keystore \"{keystore}\" -storepass \"{keystorePass}\"";
            if (!string.IsNullOrEmpty(alias)) arguments += $" -alias \"{alias}\"";
            var result = ProcessRunner.RunSync(FindKeytool(), arguments, null, 15000);
            if (result.StartError != null)
            {
                message = result.StartError + " — cài JDK hoặc đặt JAVA_HOME (Unity có JDK riêng: Preferences > External Tools).";
                return null;
            }

            var match = Regex.Match(result.Output, @"SHA1:\s*([0-9A-Fa-f:]{47,})");
            if (!match.Success)
            {
                message = "keytool không trả về SHA1 — thường là sai mật khẩu hoặc sai alias.\n" + result.Error.Trim();
                return null;
            }

            message = "Đã lấy SHA-1 từ keystore.";
            return match.Groups[1].Value;
        }

        private static string FindKeytool()
        {
            var executable = Application.platform == RuntimePlatform.WindowsEditor ? "keytool.exe" : "keytool";
            var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            var candidates = new[]
            {
                string.IsNullOrEmpty(javaHome) ? null : Path.Combine(javaHome, "bin", executable),
                Path.Combine(EditorPrefs.GetString("JdkPath", string.Empty), "bin", executable),
                "/usr/bin/" + executable,
            };

            foreach (var candidate in candidates)
                if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
                    return candidate;
            return executable;
        }

        #endregion

        #region Console URL

        internal static string ProjectUrl(string id) =>
            string.IsNullOrEmpty(id) ? "https://console.firebase.google.com/" : $"https://console.firebase.google.com/project/{id}/overview";

        internal static string SettingsUrl(string id) =>
            string.IsNullOrEmpty(id) ? "https://console.firebase.google.com/" : $"https://console.firebase.google.com/project/{id}/settings/general";

        internal static string ServiceAccountsUrl(string id) =>
            string.IsNullOrEmpty(id)
                ? "https://console.cloud.google.com/iam-admin/serviceaccounts"
                : $"https://console.cloud.google.com/iam-admin/serviceaccounts?project={id}";

        #endregion
    }
}
#endif
