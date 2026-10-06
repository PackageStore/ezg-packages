#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Ezg.Editor.Shared.EzgKit
{
    /// <summary>
    ///     Đích ghi <c>AppSecretsConfig</c> — ScriptableObject của game (template mới) chứa mọi key/URL riêng
    ///     của app: AppsFlyer dev key, App Store ID, cờ sandbox, webhook/bot Discord, endpoint backend, link
    ///     pháp lý. Runtime đọc nó qua <c>Resources.Load("AppSecretsConfig")</c>.
    ///     <para>
    ///         <b>Vì sao có lớp này:</b> template cũ để mấy số đó là <c>const</c> trong <c>GameConstant.cs</c>,
    ///         và kit 0.x vẫn ghi vào đó. Template mới bỏ các const đó đi → kit ghi vào hư không, Readiness báo
    ///         lỗi sai. Giờ mọi đường ghi/đọc các số đó đi qua đây trước; dự án KHÔNG có type
    ///         <c>AppSecretsConfig</c> (template cũ) mới rơi về regex trên GameConstant.
    ///     </para>
    ///     <para>
    ///         Tìm type theo tên (<see cref="TypeFinder" />) — kit không tham chiếu assembly game.
    ///     </para>
    /// </summary>
    internal static class AppSecretsSink
    {
        internal const string TYPE_NAME = "AppSecretsConfig";
        internal const string ASSET_NAME = "AppSecretsConfig";

        /// <summary>Nơi tạo asset khi dự án chưa có — đúng chỗ comment của AppSecretsConfig.cs chỉ định.</summary>
        internal const string DEFAULT_ASSET_PATH = "Assets/_Project/Resources/AppSecretsConfig.asset";

        #region Field names (khớp AppSecretsConfig.cs)

        internal const string F_APPSFLYER_KEY = "appsFlyerDevKey";
        internal const string F_IOS_APP_ID = "iosAppId";
        internal const string F_SANDBOX = "appsFlyerPurchaseSandbox";
        internal const string F_DISCORD_BUG = "discordBugWebhookUrl";
        internal const string F_DISCORD_FEEDBACK = "discordFeedbackWebhookUrl";
        internal const string F_DISCORD_BOT = "discordBotToken";
        internal const string F_DISCORD_CHANNEL = "discordBugChannelId";
        internal const string F_SERVER_TIME = "serverTimeUrl";
        internal const string F_EMPLOYEE_API = "employeeApiUrl";
        internal const string F_FEEDBACK = "feedbackFormUrl";
        internal const string F_PRIVACY = "privacyPolicyUrl";
        internal const string F_TERMS = "termsOfServiceUrl";

        /// <summary>Mọi field string theo thứ tự hiện trên trang (kèm nhãn + có phải secret không).</summary>
        internal static readonly (string Field, string Label, bool Secret, string Hint)[] StringFields =
        {
            (F_APPSFLYER_KEY, "AppsFlyer dev key", true, "AppsFlyer > App Settings > Dev key."),
            (F_IOS_APP_ID, "App Store ID (iOS)", false, "Số 10 chữ số ở App Store Connect > App Information."),
            (F_PRIVACY, "Privacy policy URL", false, "Link công khai — store review và consent MAX đều mở link này."),
            (F_TERMS, "Terms of service URL", false, "Link công khai điều khoản dịch vụ."),
            (F_FEEDBACK, "Form feedback / hỗ trợ", false, "Nút hỗ trợ trong Settings mở link này. Trống = tắt nút."),
            (F_DISCORD_BUG, "Discord webhook bug report", true, "Kênh nhận bug report kèm ảnh. Trống = tắt."),
            (F_DISCORD_FEEDBACK, "Discord webhook feedback", true, "Kênh nhận feedback người chơi. Trống = tắt."),
            (F_DISCORD_BOT, "Discord bot token", true, "Nằm trong bản build là lộ — chỉ điền khi thật sự cần luồng bot."),
            (F_DISCORD_CHANNEL, "Discord channel id (forum bug)", false, "Đi kèm bot token."),
            (F_SERVER_TIME, "Server time URL", false, "Endpoint trả unix timestamp (giây). Trống = chỉ dùng giờ máy."),
            (F_EMPLOYEE_API, "Employee API URL", false, "Danh sách nhân sự để tag trong bug report. Trống = tắt."),
        };

        #endregion

        #region Query

        /// <summary>Dự án có type AppSecretsConfig (template mới) không.</summary>
        internal static bool TypeExists => Type != null;

        internal static Type Type => TypeFinder.Find(TYPE_NAME, typeof(ScriptableObject));

        /// <summary>Asset hiện có (ưu tiên bản nằm trong Resources đúng tên); null = chưa tạo.</summary>
        internal static ScriptableObject FindAsset()
        {
            var type = Type;
            if (type == null) return null;

            ScriptableObject fallback = null;
            foreach (var guid in AssetDatabase.FindAssets("t:" + type.Name))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                var asset = AssetDatabase.LoadAssetAtPath(path, type) as ScriptableObject;
                if (asset == null) continue;
                if (Path.GetFileNameWithoutExtension(path) == ASSET_NAME && path.Contains("/Resources/")) return asset;
                fallback ??= asset;
            }

            return fallback;
        }

        internal static string AssetPath
        {
            get
            {
                var asset = FindAsset();
                return asset == null ? null : AssetDatabase.GetAssetPath(asset);
            }
        }

        /// <summary>Asset tồn tại nhưng nằm ngoài Resources / sai tên → runtime không load được.</summary>
        internal static string PlacementProblem()
        {
            var path = AssetPath;
            if (path == null) return null;
            if (!path.Contains("/Resources/")) return $"{path} nằm ngoài thư mục Resources — runtime không load được.";
            if (Path.GetFileNameWithoutExtension(path) != ASSET_NAME)
                return $"{path} phải đặt tên đúng \"{ASSET_NAME}\" — runtime load theo tên.";
            return null;
        }

        /// <summary>Giá trị string của field; null = không có asset / không có field.</summary>
        internal static string Read(string field)
        {
            var asset = FindAsset();
            if (asset == null) return null;
            var prop = new SerializedObject(asset).FindProperty(field);
            if (prop == null) return null;
            return prop.propertyType == SerializedPropertyType.Boolean
                ? prop.boolValue ? "true" : "false"
                : prop.stringValue;
        }

        internal static bool ReadBool(string field) => Read(field) == "true";

        /// <summary>Đọc mọi field string + bool thành dictionary (để dựng trang / API).</summary>
        internal static Dictionary<string, string> ReadAll()
        {
            var result = new Dictionary<string, string>();
            var asset = FindAsset();
            if (asset == null) return result;
            var so = new SerializedObject(asset);
            foreach (var (field, _, _, _) in StringFields)
            {
                var prop = so.FindProperty(field);
                if (prop != null) result[field] = prop.stringValue;
            }

            var sandbox = so.FindProperty(F_SANDBOX);
            if (sandbox != null) result[F_SANDBOX] = sandbox.boolValue ? "true" : "false";
            return result;
        }

        #endregion

        #region Write

        /// <summary>
        ///     Ghi các giá trị vào asset. Giá trị <c>null</c> = không đụng tới field đó; chuỗi rỗng = xoá trắng
        ///     (trang cho phép xoá một webhook có chủ đích). Asset chưa có thì tạo ở
        ///     <see cref="DEFAULT_ASSET_PATH" /> (dry-run chỉ ghi nhận sẽ tạo). Trả về false + error khi dự án
        ///     không có type AppSecretsConfig.
        /// </summary>
        internal static bool Write(IDictionary<string, string> values, bool dryRun, List<ChangeRow> changes,
            out string error)
        {
            error = null;
            var type = Type;
            if (type == null)
            {
                error = "Dự án không có type AppSecretsConfig (template cũ) — key nằm trong GameConstant.cs.";
                return false;
            }

            var asset = FindAsset();
            var willCreate = asset == null;
            const string sink = "AppSecretsConfig";

            if (willCreate)
            {
                changes?.Add(new ChangeRow(sink, "(asset)", "chưa có", DEFAULT_ASSET_PATH, false));
                if (dryRun)
                {
                    foreach (var pair in values)
                        if (pair.Value != null)
                            changes?.Add(new ChangeRow(sink, pair.Key, string.Empty, pair.Value,
                                string.IsNullOrEmpty(pair.Value), IsSecret(pair.Key)));
                    return true;
                }

                asset = Create(type);
                if (asset == null)
                {
                    error = $"Không tạo được {DEFAULT_ASSET_PATH}.";
                    return false;
                }
            }

            var so = new SerializedObject(asset);
            var dirty = false;
            foreach (var pair in values)
            {
                if (pair.Value == null) continue;
                var prop = so.FindProperty(pair.Key);
                if (prop == null)
                {
                    changes?.Add(new ChangeRow(sink, pair.Key, "(không có field)", pair.Value, true));
                    continue;
                }

                if (prop.propertyType == SerializedPropertyType.Boolean)
                {
                    var wanted = pair.Value == "true" || pair.Value == "1";
                    var same = prop.boolValue == wanted;
                    changes?.Add(new ChangeRow(sink, pair.Key, prop.boolValue ? "bật" : "tắt", wanted ? "bật" : "tắt", same));
                    if (!same && !dryRun)
                    {
                        prop.boolValue = wanted;
                        dirty = true;
                    }

                    continue;
                }

                var current = prop.stringValue ?? string.Empty;
                var value = pair.Value.Trim();
                var matched = current == value;
                changes?.Add(new ChangeRow(sink, pair.Key, current, value, matched, IsSecret(pair.Key)));
                if (matched || dryRun) continue;
                prop.stringValue = value;
                dirty = true;
            }

            if (!dryRun && dirty)
            {
                Undo.RecordObject(asset, "EzgKit: AppSecretsConfig");
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssetIfDirty(asset);
            }

            return true;
        }

        internal static bool IsSecret(string field)
        {
            foreach (var (name, _, secret, _) in StringFields)
                if (name == field)
                    return secret;
            return false;
        }

        private static ScriptableObject Create(Type type)
        {
            var folder = Path.GetDirectoryName(DEFAULT_ASSET_PATH)?.Replace('\\', '/');
            EnsureFolder(folder);
            var asset = ScriptableObject.CreateInstance(type);
            AssetDatabase.CreateAsset(asset, DEFAULT_ASSET_PATH);
            AssetDatabase.SaveAssets();
            return asset;
        }

        /// <summary>Tạo cây thư mục <c>Assets/…</c> qua AssetDatabase (để có .meta ngay).</summary>
        internal static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
            var parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        #endregion
    }
}
#endif
