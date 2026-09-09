#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Ezg.Editor.Shared.Marketing;
using Ezg.Editor.Shared.Social;
using UnityEditor;
using UnityEngine;

namespace Ezg.Editor.Shared.Publisher
{
    /// <summary>
    ///     Ghi MỘT ID của SDK vào đúng chỗ trong project — phần "ghi" đối xứng với phần "đọc" của
    ///     <see cref="SdkCatalog.ReadSlot" />. Cùng bảng (SDK, key) → file:
    ///     <list type="bullet">
    ///         <item>Meta <c>appId</c>/<c>clientToken</c> → <c>FacebookSettings.asset</c> (mảng <c>appIds</c>/<c>clientTokens</c>[0])
    ///             + <c>AndroidManifest.xml</c> (meta-data ApplicationId/ClientToken, authorities ContentProvider)
    ///             + <c>MarketingConfig.json</c> <c>facebook.appId/clientToken</c>.</item>
    ///         <item>AppsFlyer <c>devKey</c>/<c>iosAppId</c> → <c>GameConstant.AppsFlyerId</c>/<c>IOSAppId</c>
    ///             + <c>MarketingConfig.json</c> <c>appsflyerDevKey</c>/<c>appleId</c>.</item>
    ///         <item>GameAnalytics <c>gameKey</c>/<c>secretKey</c> → <c>Assets/Resources/GameAnalytics/Settings.asset</c>
    ///             (entry của platform Android; chưa có Settings.asset / chưa có platform thì tạo qua reflection
    ///             đúng như menu Assets &gt; GameAnalytics &gt; Select Settings).</item>
    ///     </list>
    ///     <para>
    ///         Dùng cho hai lối: ô nhập trên <see cref="PublisherPage" /> (người dùng gõ ID game tự tạo rồi bấm
    ///         "Điền") và <see cref="PublisherSdkApplier" /> (ID publisher cấp sẵn khi "Chuyển sang"). Cùng
    ///         luật với <c>MarketingConfigApplier</c>: đọc-ghi giữ BOM, chỉ thay phần giá trị, <c>ImportAsset</c>
    ///         file .cs để recompile; <c>MarketingConfig.json</c> ghi cùng để nút "Apply Config" của tab
    ///         Marketing không đè ngược. Google Sheet marketing vẫn phải đổi tay — nói rõ trong changes.
    ///     </para>
    ///     <para>Chuỗi trong <c>changes</c> không dấu: chúng đi vào <see cref="EditorUtility.DisplayDialog" />.</para>
    /// </summary>
    internal static class PublisherIdWriter
    {
        #region Constants

        private const string GA_SETTINGS_TYPE = "GameAnalyticsSDK.Setup.Settings";
        private const string GA_ADD_PLATFORM = "AddPlatform";
        private const int RUNTIME_PLATFORM_ANDROID = (int)RuntimePlatform.Android;

        /// <summary>Marker cho dòng "không đổi" — <c>SdkSwitcher.BuildPlan</c> lọc theo chuỗi này.</summary>
        internal const string UNCHANGED = "giu nguyen";

        #endregion

        /// <summary>Một ID cần ghi.</summary>
        internal readonly struct Entry
        {
            internal readonly SdkKind Kind;
            internal readonly string Key;
            internal readonly string Value;

            internal Entry(SdkKind kind, string key, string value)
            {
                Kind = kind;
                Key = key;
                Value = value ?? "";
            }
        }

        /// <summary>Tool có biết ghi (SDK, key) này vào file không. false = ID ngoài Unity (Partner ID…) — chỉ hiện.</summary>
        internal static bool CanWrite(SdkKind kind, string key) =>
            (kind, key) switch
            {
                (SdkKind.Meta, "appId") or (SdkKind.Meta, "clientToken") => true,
                (SdkKind.AppsFlyer, "devKey") or (SdkKind.AppsFlyer, "iosAppId") => true,
                (SdkKind.GameAnalytics, "gameKey") or (SdkKind.GameAnalytics, "secretKey") => true,
                _ => false,
            };

        /// <summary>
        ///     Ghi một loạt ID. <paramref name="dryRun" /> = chỉ liệt kê sẽ đổi gì. Trả về false khi có lỗi
        ///     chặn (file không có, regex không khớp) — <paramref name="error" /> nói rõ; những ID đã ghi được
        ///     trước đó vẫn nằm trong <paramref name="changes" />.
        /// </summary>
        internal static bool Write(IReadOnlyList<Entry> entries, bool dryRun, out List<string> changes, out string error)
        {
            changes = new List<string>();
            error = null;

            var meta = new List<Entry>();
            var appsFlyer = new List<Entry>();
            var ga = new List<Entry>();
            foreach (var entry in entries)
            {
                if (!CanWrite(entry.Kind, entry.Key))
                {
                    error = $"{SdkCatalog.NameOf(entry.Kind)}.{entry.Key}: tool khong biet ghi ID nay (ngoai Unity).";
                    return false;
                }

                switch (entry.Kind)
                {
                    case SdkKind.Meta: meta.Add(entry); break;
                    case SdkKind.AppsFlyer: appsFlyer.Add(entry); break;
                    case SdkKind.GameAnalytics: ga.Add(entry); break;
                }
            }

            if (meta.Count > 0 && !WriteMeta(meta, dryRun, changes, out error)) return false;
            if (appsFlyer.Count > 0 && !WriteAppsFlyer(appsFlyer, dryRun, changes, out error)) return false;
            if (ga.Count > 0 && !WriteGameAnalytics(ga, dryRun, changes, out error)) return false;
            return true;
        }

        #region Meta

        private static bool WriteMeta(List<Entry> entries, bool dryRun, List<string> changes, out string error)
        {
            error = null;
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(SdkCatalog.FACEBOOK_SETTINGS_PATH);
            if (asset == null)
            {
                error = $"Khong thay {SdkCatalog.FACEBOOK_SETTINGS_PATH} — gan Facebook SDK truoc (Chuyen sang se cai).";
                return false;
            }

            var so = new SerializedObject(asset);
            var dirty = false;
            string appId = null, clientToken = null;
            foreach (var entry in entries)
            {
                var property = entry.Key == "appId" ? "appIds" : "clientTokens";
                if (entry.Key == "appId") appId = entry.Value;
                else clientToken = entry.Value;
                dirty |= SetFirstArrayElement(so, property, entry.Value, changes, ref error);
                if (error != null) return false;
            }

            if (!dryRun && dirty)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssetIfDirty(asset);
            }

            // AndroidManifest: FB SDK sinh meta-data từ FacebookSettings khi bấm Regenerate — ghi thẳng để
            // không phải bấm thêm, và để catalog thấy khớp ngay.
            var manifestPath = Path.Combine(ProjectRoot(), SdkCatalog.ANDROID_MANIFEST);
            if (File.Exists(manifestPath))
            {
                var text = ReadText(manifestPath, out var hadBom);
                var manifestDirty = false;
                if (appId != null)
                {
                    manifestDirty |= ReplaceCapture(ref text, "(com\\.facebook\\.sdk\\.ApplicationId\"\\s+android:value=\"fb)([^\"]*)(\")",
                        appId, "AndroidManifest ApplicationId", changes);
                    manifestDirty |= ReplaceCapture(ref text, "(android:authorities=\"com\\.facebook\\.app\\.FacebookContentProvider)([^\"]*)(\")",
                        appId, "AndroidManifest FacebookContentProvider", changes);
                }

                if (clientToken != null)
                    manifestDirty |= ReplaceCapture(ref text, "(com\\.facebook\\.sdk\\.ClientToken\"\\s+android:value=\")([^\"]*)(\")",
                        clientToken, "AndroidManifest ClientToken", changes);

                if (!dryRun && manifestDirty) WriteText(manifestPath, text, hadBom);
            }
            else changes.Add("AndroidManifest.xml: khong co — Facebook > Regenerate Android Manifest sau khi gan SDK.");

            if (appId != null) SyncMarketingJson("facebook", "appId", appId, dryRun, changes);
            if (clientToken != null) SyncMarketingJson("facebook", "clientToken", clientToken, dryRun, changes);
            return true;
        }

        /// <summary>Phần tử [0] của mảng string trong SerializedObject. Trả về true nếu đổi.</summary>
        private static bool SetFirstArrayElement(SerializedObject so, string propertyName, string value, List<string> changes,
            ref string error)
        {
            var prop = so.FindProperty(propertyName);
            if (prop == null || !prop.isArray)
            {
                error = $"FacebookSettings.asset: khong co mang '{propertyName}' — SDK doi format?";
                return false;
            }

            if (prop.arraySize == 0) prop.arraySize = 1;
            var element = prop.GetArrayElementAtIndex(0);
            var current = element.stringValue ?? "";
            if (current == value)
            {
                changes.Add($"FacebookSettings.{propertyName}[0]: {UNCHANGED} ({Display(value)})");
                return false;
            }

            changes.Add($"FacebookSettings.{propertyName}[0]: {Display(current)} -> {Display(value)}");
            element.stringValue = value;
            return true;
        }

        #endregion

        #region AppsFlyer (GameConstant)

        /// <summary>Const GameConstant → field trong MarketingConfig.json phải đồng bộ.</summary>
        private static readonly Dictionary<string, string> _marketingField = new()
        {
            { SdkCatalog.CONST_APPSFLYER, "appsflyerDevKey" },
            { SdkCatalog.CONST_IOS_APP_ID, "appleId" },
        };

        private static bool WriteAppsFlyer(List<Entry> entries, bool dryRun, List<string> changes, out string error)
        {
            error = null;
            var path = SocialChecks.FindGameConstant();
            if (path == null || !File.Exists(path))
            {
                error = "Khong tim thay GameConstant.cs trong du an.";
                return false;
            }

            var text = ReadText(path, out var hadBom);
            var dirty = false;
            foreach (var entry in entries)
            {
                var name = entry.Key == "devKey" ? SdkCatalog.CONST_APPSFLYER : SdkCatalog.CONST_IOS_APP_ID;
                var match = Regex.Match(text, "(public const string " + name + "\\s*=\\s*\")([^\"]*)(\";)");
                if (!match.Success)
                {
                    error = $"GameConstant.cs khong co `public const string {name}` — them tay roi chay lai.";
                    return false;
                }

                var current = match.Groups[2].Value;
                if (current == entry.Value) changes.Add($"GameConstant.{name}: {UNCHANGED} ({Display(entry.Value)})");
                else
                {
                    changes.Add($"GameConstant.{name}: {Display(current)} -> {Display(entry.Value)}");
                    text = text.Remove(match.Groups[2].Index, current.Length).Insert(match.Groups[2].Index, entry.Value);
                    dirty = true;
                }

                if (_marketingField.TryGetValue(name, out var field)) SyncMarketingJson(null, field, entry.Value, dryRun, changes);
            }

            if (dryRun || !dirty) return true;

            WriteText(path, text, hadBom);
            var assetPath = "Assets" + path.Replace('\\', '/').Substring(Application.dataPath.Length);
            AssetDatabase.ImportAsset(assetPath);
            return true;
        }

        #endregion

        #region GameAnalytics

        /// <summary>
        ///     Settings.asset của GA giữ N list song song theo <c>Platforms</c>; key nằm ở entry của platform
        ///     Android (CPI test chạy Android). Không có Android thì thêm platform bằng chính <c>AddPlatform</c>
        ///     của SDK (reflection — package không tham chiếu assembly GA) để 12 list vẫn cùng độ dài.
        /// </summary>
        private static bool WriteGameAnalytics(List<Entry> entries, bool dryRun, List<string> changes, out string error)
        {
            error = null;
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(SdkCatalog.GA_SETTINGS_PATH);
            if (asset == null)
            {
                var type = FindType(GA_SETTINGS_TYPE);
                if (type == null)
                {
                    error = "Khong thay GameAnalytics SDK trong project — gan SDK truoc (Chuyen sang se cai).";
                    return false;
                }

                changes.Add($"GA Settings.asset: chua co -> tao moi tai {SdkCatalog.GA_SETTINGS_PATH} (platform Android)");
                if (dryRun)
                {
                    foreach (var entry in entries) changes.Add($"GA Settings.{entry.Key}[Android]: (rong) -> {Display(entry.Value)}");
                    return true;
                }

                asset = ScriptableObject.CreateInstance(type);
                if (!AddPlatform(asset, out error)) return false;
                var folder = Path.GetDirectoryName(SdkCatalog.GA_SETTINGS_PATH)?.Replace('\\', '/');
                if (folder != null && !AssetDatabase.IsValidFolder(folder))
                {
                    Directory.CreateDirectory(Path.Combine(ProjectRoot(), folder));
                    AssetDatabase.Refresh();
                }

                AssetDatabase.CreateAsset(asset, SdkCatalog.GA_SETTINGS_PATH);
            }

            var so = new SerializedObject(asset);
            var platforms = so.FindProperty("Platforms");
            var index = -1;
            if (platforms != null)
                for (var i = 0; i < platforms.arraySize; i++)
                    if (platforms.GetArrayElementAtIndex(i).intValue == RUNTIME_PLATFORM_ANDROID)
                    {
                        index = i;
                        break;
                    }

            if (index < 0)
            {
                changes.Add("GA Settings.asset: chua co platform Android -> them");
                if (!dryRun)
                {
                    if (!AddPlatform(asset, out error)) return false;
                    so = new SerializedObject(asset);
                    platforms = so.FindProperty("Platforms");
                }

                index = platforms == null ? 0 : Mathf.Max(0, platforms.arraySize - 1);
            }

            var dirty = false;
            foreach (var entry in entries)
            {
                var list = so.FindProperty(entry.Key);
                if (list == null || !list.isArray)
                {
                    error = $"GA Settings.asset: khong co list '{entry.Key}' — SDK doi format?";
                    return false;
                }

                if (index >= list.arraySize)
                {
                    // Dry-run khi vừa "thêm platform" trên giấy: list chưa dài ra — coi như rỗng.
                    changes.Add($"GA Settings.{entry.Key}[Android]: (rong) -> {Display(entry.Value)}");
                    continue;
                }

                var element = list.GetArrayElementAtIndex(index);
                var current = element.stringValue ?? "";
                if (current == entry.Value)
                {
                    changes.Add($"GA Settings.{entry.Key}[Android]: {UNCHANGED} ({Display(entry.Value)})");
                    continue;
                }

                changes.Add($"GA Settings.{entry.Key}[Android]: {Display(current)} -> {Display(entry.Value)}");
                element.stringValue = entry.Value;
                dirty = true;
            }

            if (dryRun || !dirty) return true;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            return true;
        }

        private static bool AddPlatform(ScriptableObject asset, out string error)
        {
            error = null;
            var method = asset.GetType().GetMethod(GA_ADD_PLATFORM, BindingFlags.Public | BindingFlags.Instance);
            if (method == null)
            {
                error = $"GA Settings: khong co method {GA_ADD_PLATFORM} — SDK doi API, them platform Android tay (Assets > GameAnalytics > Select Settings).";
                return false;
            }

            method.Invoke(asset, new object[] { RuntimePlatform.Android });
            return true;
        }

        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }

            return null;
        }

        #endregion

        #region MarketingConfig.json

        /// <summary>
        ///     Ghi field trong <c>MarketingConfig.json</c>: <paramref name="section" /> null = field cấp gốc,
        ///     có = field trong object đó (regex khoanh trong <c>"section": { … }</c> tới trước <c>}</c> đầu tiên —
        ///     đủ vì field cần ghi đứng trước mọi object con).
        /// </summary>
        private static void SyncMarketingJson(string section, string field, string value, bool dryRun, List<string> changes)
        {
            string path;
            try
            {
                path = MarketingConfig.JsonPath;
            }
            catch (Exception)
            {
                return;
            }

            if (!File.Exists(path)) return;

            var text = File.ReadAllText(path);
            var pattern = section == null
                ? "(\"" + field + "\"\\s*:\\s*\")([^\"]*)(\")"
                : "(\"" + section + "\"\\s*:\\s*\\{[^}]*?\"" + field + "\"\\s*:\\s*\")([^\"]*)(\")";
            var match = Regex.Match(text, pattern);
            var label = section == null ? field : section + "." + field;
            if (!match.Success)
            {
                changes.Add($"MarketingConfig.json: khong co field {label} — bo qua.");
                return;
            }

            var current = match.Groups[2].Value;
            if (current == value)
            {
                changes.Add($"MarketingConfig.json.{label}: {UNCHANGED}");
                return;
            }

            changes.Add($"MarketingConfig.json.{label}: {Display(current)} -> {Display(value)}  "
                        + "(Google Sheet marketing van giu gia tri cu — doi tay trong sheet, khong thi lan Tai sheet ke tiep se de nguoc)");
            if (dryRun) return;

            text = text.Remove(match.Groups[2].Index, current.Length).Insert(match.Groups[2].Index, value);
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        #endregion

        #region Helpers

        /// <summary>Thay nhóm 2 của regex. Không khớp = ghi vào changes (file đổi format) chứ không im lặng. Trả về true nếu đổi.</summary>
        private static bool ReplaceCapture(ref string text, string pattern, string value, string label, List<string> changes)
        {
            var match = Regex.Match(text, pattern);
            if (!match.Success)
            {
                changes.Add($"{label}: khong tim thay cho ghi (regex khong khop) — bo qua.");
                return false;
            }

            var current = match.Groups[2].Value;
            if (current == value)
            {
                changes.Add($"{label}: {UNCHANGED}");
                return false;
            }

            changes.Add($"{label}: {Display(current)} -> {Display(value)}");
            text = text.Remove(match.Groups[2].Index, current.Length).Insert(match.Groups[2].Index, value);
            return true;
        }

        private static string ReadText(string path, out bool hadBom)
        {
            var bytes = File.ReadAllBytes(path);
            hadBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            return new UTF8Encoding(false).GetString(bytes, hadBom ? 3 : 0, bytes.Length - (hadBom ? 3 : 0));
        }

        private static void WriteText(string path, string text, bool hadBom) =>
            File.WriteAllText(path, text, new UTF8Encoding(hadBom));

        private static string ProjectRoot() => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        private static string Display(string value) => string.IsNullOrEmpty(value) ? "(rong)" : value;

        #endregion
    }
}
#endif
