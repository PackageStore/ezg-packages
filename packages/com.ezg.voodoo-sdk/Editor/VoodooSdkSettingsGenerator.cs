#if UNITY_EDITOR
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Ezg.VoodooSdk.Editor
{
    /// <summary>
    /// Sinh <c>Assets/Resources/TinySauce/Settings.asset</c> từ config, và ghi App ID +
    /// client token vào <c>FacebookSettings.asset</c> mà TinySauce bundle.
    ///
    /// Ghi YAML thẳng thay vì tạo ScriptableObject qua API: chạy được cả trong batchmode
    /// khi TinySauceSettings chưa kịp compile, và không phụ thuộc thứ tự import.
    /// </summary>
    public static class VoodooSdkSettingsGenerator
    {
        #region Fields

        private const string ScriptGuidPattern = @"^guid:\s*([0-9a-f]{32})\s*$";
        private const int MonoBehaviourFileId = 11500000;

        #endregion

        #region Public

        /// <summary>Sinh/ghi đè Settings.asset. Trả về false kèm lý do nếu không làm được.</summary>
        public static bool Generate(VoodooSdkConfig config, out string error)
        {
            error = null;

            string guid = ReadSettingsScriptGuid(out error);
            if (guid == null)
                return false;

            string target = VoodooSdkPaths.Absolute(VoodooSdkPaths.SettingsAsset);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, BuildYaml(config, guid));

            PatchFacebookSettings(config);
            return true;
        }

        #endregion

        #region Private

        private static string ReadSettingsScriptGuid(out string error)
        {
            error = null;
            string meta = VoodooSdkPaths.Absolute(VoodooSdkPaths.SettingsScript) + ".meta";

            if (!File.Exists(meta))
            {
                error = $"Thiếu {VoodooSdkPaths.SettingsScript}.meta — TinySauce chưa được import?";
                return null;
            }

            Match match = Regex.Match(File.ReadAllText(meta), ScriptGuidPattern, RegexOptions.Multiline);
            if (!match.Success)
            {
                error = $"Không đọc được guid trong {meta}";
                return null;
            }

            return match.Groups[1].Value;
        }

        private static string BuildYaml(VoodooSdkConfig config, string scriptGuid)
        {
            var builder = new StringBuilder();
            builder.AppendLine("%YAML 1.1");
            builder.AppendLine("%TAG !u! tag:unity3d.com,2011:");
            builder.AppendLine("--- !u!114 &11400000");
            builder.AppendLine("MonoBehaviour:");
            builder.AppendLine("  m_ObjectHideFlags: 0");
            builder.AppendLine("  m_CorrespondingSourceObject: {fileID: 0}");
            builder.AppendLine("  m_PrefabInstance: {fileID: 0}");
            builder.AppendLine("  m_PrefabAsset: {fileID: 0}");
            builder.AppendLine("  m_GameObject: {fileID: 0}");
            builder.AppendLine("  m_Enabled: 1");
            builder.AppendLine("  m_EditorHideFlags: 0");
            builder.AppendLine($"  m_Script: {{fileID: {MonoBehaviourFileId}, guid: {scriptGuid}, type: 3}}");
            builder.AppendLine("  m_Name: Settings");
            builder.AppendLine("  m_EditorClassIdentifier:");
            builder.AppendLine($"  gameAnalyticsIosGameKey: {Yaml(config.gameAnalytics.ios.gameKey)}");
            builder.AppendLine($"  gameAnalyticsIosSecretKey: {Yaml(config.gameAnalytics.ios.secretKey)}");
            builder.AppendLine($"  gameAnalyticsAndroidGameKey: {Yaml(config.gameAnalytics.android.gameKey)}");
            builder.AppendLine($"  gameAnalyticsAndroidSecretKey: {Yaml(config.gameAnalytics.android.secretKey)}");
            builder.AppendLine($"  facebookAppId: {Yaml(config.facebook.appId)}");
            builder.AppendLine($"  facebookClientToken: {Yaml(config.facebook.clientToken)}");
            builder.AppendLine($"  adjustIOSToken: {Yaml(config.adjust.iosToken)}");
            builder.AppendLine($"  adjustAndroidToken: {Yaml(config.adjust.androidToken)}");
            builder.AppendLine("  EditorIdfa:");
            builder.AppendLine("  UseRemoteConfig: 0");
            // UseVoodooAnalytics là [ReadOnly] trong TinySauce — Voodoo bắt buộc bật.
            builder.AppendLine("  UseVoodooAnalytics: 1");
            builder.AppendLine($"  doesYourGameDisplayAds: {(config.gdpr.displaysAds ? 1 : 0)}");
            builder.AppendLine($"  companyName: {Yaml(config.gdpr.companyName)}");
            builder.AppendLine($"  privacyPolicyURL: {Yaml(config.gdpr.privacyPolicyUrl)}");
            builder.AppendLine($"  developerContactEmail: {Yaml(config.gdpr.contactEmail)}");
            return builder.ToString();
        }

        /// <summary>Bọc chuỗi thành scalar YAML double-quoted.</summary>
        private static string Yaml(string value)
        {
            string text = value ?? string.Empty;
            return "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        /// <summary>
        /// Ghi App ID + client token vào FacebookSettings.asset của TinySauce.
        /// Ba list song song, mỗi list đúng một phần tử (selectedAppIndex = 0).
        /// </summary>
        private static void PatchFacebookSettings(VoodooSdkConfig config)
        {
            string path = VoodooSdkPaths.Absolute(VoodooSdkPaths.FacebookSettingsAsset);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[VoodooSdk] Không thấy {VoodooSdkPaths.FacebookSettingsAsset} — bỏ qua bước vá Facebook.");
                return;
            }

            string content = File.ReadAllText(path);
            string label = string.IsNullOrWhiteSpace(config.facebook.appLabel)
                ? PlayerSettings.productName
                : config.facebook.appLabel;

            content = ReplaceFirstListEntry(content, "clientTokens", config.facebook.clientToken);
            content = ReplaceFirstListEntry(content, "appIds", config.facebook.appId);
            content = ReplaceFirstListEntry(content, "appLabels", label);

            File.WriteAllText(path, content);
        }

        private static string ReplaceFirstListEntry(string content, string key, string value)
        {
            // Bắt buộc dùng ${1}: value bắt đầu bằng chữ số (vd App ID) sẽ khiến "$1" bị đọc
            // thành số hiệu capture group "$11609..." và ném ArgumentException.
            string scalar = YamlScalar(value).Replace("$", "$$");
            return Regex.Replace(content, $@"(\n  {key}:\n  - ).*", "${1}" + scalar);
        }

        /// <summary>
        /// Ghi thô nếu an toàn, ngược lại bọc single-quote. Cần thiết vì productName hay chứa
        /// dấu hai chấm (vd "Kingdom Hero: Base War Battle") — ghi thô sẽ phá cấu trúc YAML.
        /// Không bọc chuỗi thuần chữ-số để giữ nguyên kiểu (appIds là số, không phải chuỗi).
        /// </summary>
        private static string YamlScalar(string value)
        {
            string text = value ?? string.Empty;
            if (text.Length > 0 && Regex.IsMatch(text, @"^[A-Za-z0-9_.\-]+$"))
                return text;
            return "'" + text.Replace("'", "''") + "'";
        }

        #endregion
    }
}
#endif
