#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Ezg.Editor.Shared.Marketing
{
    /// <summary>
    ///     Nạp <c>marketing_config.json</c> (sheet marketing) và ghi thẳng vào mọi nơi project thật sự
    ///     đọc các số đó. Trước đây các giá trị này nằm rải rác 6 chỗ và đang là số của project TEMPLATE
    ///     khác — sai ad-unit-id nghĩa là doanh thu ads chảy sang app khác mà build vẫn chạy, không log gì.
    ///
    ///     <para><b>Đích ghi (sink):</b></para>
    ///     <list type="bullet">
    ///         <item>
    ///             <c>Assets/_Project/Resources/AdsConfig.asset</c> — MAX sdk key + rewarded/interstitial/
    ///             banner id cho cả 2 nền tảng. Đây là thứ DUY NHẤT runtime đọc lúc init mediation.
    ///         </item>
    ///         <item>
    ///             <c>Assets/MaxSdk/Resources/AppLovinSettings.asset</c> — sdk key (dùng cho Integration
    ///             Manager) + Admob app id; AppLovin post-process ghi app id vào AndroidManifest/Info.plist
    ///             lúc build. Sai app id = Google Mobile Ads adapter crash lúc mở app.
    ///         </item>
    ///         <item>
    ///             <c>Assets/FacebookSDK/SDK/Resources/FacebookSettings.asset</c> — app id + client token.
    ///         </item>
    ///         <item>
    ///             <c>Assets/Plugins/Android/AndroidManifest.xml</c> — manifest này là bản CHÉP TAY (không
    ///             do FB SDK sinh lại), nên app id/client token trong đó phải sửa kèm, nếu không Android
    ///             vẫn chạy bằng app id cũ dù asset đã đúng.
    ///         </item>
    ///         <item>PlayerSettings — applicationIdentifier (Android + iOS) và productName.</item>
    ///         <item>
    ///             <c>GameConstant.cs</c> — <c>AppsFlyerId</c> (dev key) và <c>IOSAppId</c>. Hai giá trị này
    ///             là <c>const</c> nên phải sửa vào source; thay đổi hiện rõ trong git diff.
    ///         </item>
    ///     </list>
    ///
    ///     <para>
    ///         Các số CÒN LẠI trong sheet (Admob rewarded/inter unit, Unity game id + placement, FB
    ///         placement) khai bên dashboard AppLovin MAX chứ app không nhúng — script chỉ liệt kê lại để
    ///         đối chiếu, không có sink.
    ///     </para>
    /// </summary>
    public static class MarketingConfigApplier
    {
        #region Constants

        /// <summary>
        ///     Manifest chính của Unity — vị trí này do Unity quy định nên giống nhau ở mọi dự án.
        ///     Các sink còn lại KHÔNG hardcode đường dẫn (xem <see cref="FindAsset" />): mỗi dự án đặt
        ///     AdsConfig/GameConstant một chỗ, và dự án không tích hợp Facebook thì không có
        ///     FacebookSettings.asset.
        /// </summary>
        private const string ANDROID_MANIFEST_PATH = "Assets/Plugins/Android/AndroidManifest.xml";

        #endregion

        #region Path discovery

        /// <summary>
        ///     Tìm asset theo TÊN FILE trong toàn project. Chỉ nhận file nằm dưới <c>Assets/</c> — kết quả
        ///     trong <c>Packages/</c> là bản gốc read-only của package, ghi vào đó vừa vô nghĩa vừa mất
        ///     khi package cập nhật.
        /// </summary>
        /// <param name="assetName">Tên file không kèm đuôi, ví dụ "AdsConfig".</param>
        /// <param name="extension">Đuôi mong đợi, ví dụ ".asset" hoặc ".cs".</param>
        /// <returns>Đường dẫn kiểu Assets/... hoặc null nếu dự án này không có.</returns>
        private static string FindAsset(string assetName, string extension)
        {
            var guids = AssetDatabase.FindAssets(assetName);
            string found = null;

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                if (Path.GetFileName(path) != assetName + extension) continue;

                if (found != null)
                {
                    Debug.LogWarning($"[Marketing] Co nhieu file ten '{assetName}{extension}': "
                                     + $"dung '{found}', bo qua '{path}'.");
                    continue;
                }

                found = path;
            }

            return found;
        }

        #endregion

        #region Types

        /// <summary>
        ///     Một ô config đã đối chiếu giữa JSON và project. Ghi nhận CẢ ô đã khớp (
        ///     <see cref="Matched" /> = true) để cửa sổ trạng thái hiện được toàn bộ thông tin cho PM,
        ///     chứ không chỉ phần lệch.
        /// </summary>
        public readonly struct Change
        {
            public readonly string Sink;
            public readonly string Field;
            public readonly string OldValue;
            public readonly string NewValue;
            public readonly bool Matched;

            public Change(string sink, string field, string oldValue, string newValue, bool matched)
            {
                Sink = sink;
                Field = field;
                OldValue = oldValue;
                NewValue = newValue;
                Matched = matched;
            }

            /// <summary>Giá trị đang thực sự nằm trong project (sau khi apply thì bằng NewValue).</summary>
            public string Current => Matched ? OldValue : NewValue;

            public override string ToString() =>
                $"  [{Sink}] {Field}\n      cu : {Quote(OldValue)}\n      moi: {Quote(NewValue)}";

            private static string Quote(string v) => string.IsNullOrEmpty(v) ? "(rong)" : v;
        }

        /// <summary>Ảnh chụp trạng thái setup — dữ liệu cho <c>MarketingSetupPage</c>.</summary>
        public class Status
        {
            public MarketingConfig Config;

            /// <summary>Mọi ô đã đối chiếu, cả khớp lẫn lệch.</summary>
            public List<Change> Rows = new();

            /// <summary>Sink không có trong dự án này.</summary>
            public List<string> Skipped = new();

            /// <summary>Việc phải làm tay (ngoài Unity hoặc sheet chưa có số).</summary>
            public List<string> Todos = new();

            public List<string> Errors = new();

            public int PendingCount
            {
                get
                {
                    var count = 0;
                    foreach (var row in Rows)
                        if (!row.Matched)
                            count++;
                    return count;
                }
            }
        }

        #endregion

        #region Menu

        /// <summary>
        ///     Nút chính: Google Sheet -> marketing_config.json -> asset/manifest/PlayerSettings/code.
        ///     Fetch hỏng thì DỪNG, không apply bằng file JSON cũ (apply nhầm bản cũ mà báo "xong" còn
        ///     tệ hơn báo lỗi).
        /// </summary>
        [MenuItem("Ezg/Marketing/Setup All (1 Click)", false, 99)]
        public static void SetupAll()
        {
            if (!MarketingSheetFetcher.Fetch(out var fetchReport))
            {
                Debug.LogError($"[Marketing] {fetchReport}");
                EditorUtility.DisplayDialog("Marketing - khong tai duoc sheet", fetchReport, "OK");
                return;
            }

            Debug.Log($"[Marketing] {fetchReport}");
            EditorUtility.DisplayDialog("Marketing - da setup",
                Truncate(fetchReport + "\n" + Run(false), 3000), "OK");
        }

        [MenuItem("Ezg/Marketing/Check Config (Dry Run)", false, 120)]
        public static void CheckConfig() =>
            EditorUtility.DisplayDialog("Marketing config - check", Truncate(Run(true), 3000), "OK");

        [MenuItem("Ezg/Marketing/Apply Config (khong tai sheet)", false, 121)]
        public static void ApplyConfig()
        {
            if (!EditorUtility.DisplayDialog(
                    "Apply marketing config",
                    "Ghi toan bo thong so trong marketing_config.json vao AdsConfig, AppLovinSettings, "
                    + "FacebookSettings, AndroidManifest, PlayerSettings va GameConstant.cs.\n\n"
                    + "Chay 'Check Config (Dry Run)' truoc de xem se doi nhung gi.",
                    "Apply", "Huy"))
                return;

            EditorUtility.DisplayDialog("Marketing config - applied", Truncate(Run(false), 3000), "OK");
        }

        /// <summary>
        ///     Entry point cho batchmode/CI/automation:
        ///     <c>-executeMethod Ezg.Editor.Shared.Marketing.MarketingConfigApplier.ApplyFromCli</c>.
        ///     Không mở dialog — <see cref="Run" /> chỉ log ra Console.
        /// </summary>
        public static void ApplyFromCli() => Run(false);

        /// <summary>Bản dry-run không dialog, dùng cho automation.</summary>
        public static string CheckToText() => Run(true);

        #endregion

        #region Core

        /// <summary>Chạy toàn bộ sink, log report ra Console và trả report về cho caller.</summary>
        private static string Run(bool dryRun) => Report(Collect(dryRun), dryRun);

        /// <summary>
        ///     Đối chiếu (và ghi, nếu <paramref name="dryRun" /> = false) toàn bộ sink, trả về ảnh chụp
        ///     trạng thái. Đây là API cho <c>MarketingSetupPage</c> — bảng được dựng từ đây chứ không
        ///     parse lại text báo cáo.
        /// </summary>
        public static Status Collect(bool dryRun)
        {
            var status = new Status { Config = MarketingConfig.Load() };
            if (status.Config == null)
            {
                status.Errors.Add($"Khong doc duoc {MarketingConfig.JsonPath} - xem Console.");
                return status;
            }

            var cfg = status.Config;
            var changes = status.Rows;
            var errors = status.Errors;

            // Sink không có trong dự án này (chưa tích hợp SDK đó) là chuyện BÌNH THƯỜNG với một tool
            // dùng chung — ghi vào `Skipped` để báo cáo, không phải `Errors`.
            var skipped = status.Skipped;

            ApplyAdsConfig(cfg, dryRun, changes, skipped);
            ApplyAppLovinSettings(cfg, dryRun, changes, skipped);
            ApplyAppLovinConsentFlow(cfg, dryRun, changes, errors, skipped);
            ApplyFacebookSettings(cfg, dryRun, changes, errors, skipped);
            ApplyAndroidManifest(cfg, dryRun, changes, skipped);
            ApplyPlayerSettings(cfg, dryRun, changes);
            ApplyGameConstant(cfg, dryRun, changes, skipped);

            status.Todos.AddRange(CollectManualTodos(cfg));

            if (!dryRun && status.PendingCount > 0)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            return status;
        }

        private static void ApplyAdsConfig(MarketingConfig cfg, bool dryRun, List<Change> changes,
            List<string> skipped)
        {
            var path = FindAsset("AdsConfig", ".asset");
            var asset = path == null ? null : AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (asset == null)
            {
                skipped.Add("AdsConfig.asset - khong tim thay (du an chua dung package Ezg.Ads?).");
                return;
            }

            var so = new SerializedObject(asset);
            const string sink = "AdsConfig";

            SetString(so, "maxAndroidSdkKey", cfg.max.sdkKey, sink, changes);
            SetString(so, "maxIosSdkKey", cfg.max.sdkKey, sink, changes);
            SetString(so, "maxAndroidRewardedId", cfg.max.android.rewarded, sink, changes);
            SetString(so, "maxAndroidInterstitialId", cfg.max.android.interstitial, sink, changes);
            SetString(so, "maxAndroidBannerId", cfg.max.android.banner, sink, changes);
            SetString(so, "maxIosRewardedId", cfg.max.ios.rewarded, sink, changes);
            SetString(so, "maxIosInterstitialId", cfg.max.ios.interstitial, sink, changes);
            SetString(so, "maxIosBannerId", cfg.max.ios.banner, sink, changes);

            if (dryRun) return;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        private static void ApplyAppLovinSettings(MarketingConfig cfg, bool dryRun, List<Change> changes,
            List<string> skipped)
        {
            var path = FindAsset("AppLovinSettings", ".asset");
            var asset = path == null ? null : AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (asset == null)
            {
                skipped.Add("AppLovinSettings.asset - khong tim thay (du an chua cai MAX SDK).");
                return;
            }

            var so = new SerializedObject(asset);
            const string sink = "AppLovinSettings";

            SetString(so, "sdkKey", cfg.max.sdkKey, sink, changes);
            SetString(so, "adMobAndroidAppId", cfg.admob.android.appId, sink, changes);
            SetString(so, "adMobIosAppId", cfg.admob.ios.appId, sink, changes);

            if (dryRun) return;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        private static void ApplyFacebookSettings(MarketingConfig cfg, bool dryRun, List<Change> changes,
            List<string> errors, List<string> skipped)
        {
            var path = FindAsset("FacebookSettings", ".asset");
            var asset = path == null ? null : AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (asset == null)
            {
                skipped.Add("FacebookSettings.asset - khong tim thay (du an chua cai Facebook SDK).");
                return;
            }

            var so = new SerializedObject(asset);
            const string sink = "FacebookSettings";

            // FB SDK giữ 3 mảng song song + selectedAppIndex; project chỉ dùng 1 app nên luôn ghi phần tử 0.
            SetFirstArrayElement(so, "appIds", cfg.facebook.appId, sink, changes, errors);
            SetFirstArrayElement(so, "clientTokens", cfg.facebook.clientToken, sink, changes, errors);
            SetFirstArrayElement(so, "appLabels", cfg.facebook.appLabel, sink, changes, errors);

            if (dryRun) return;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        private static void ApplyAndroidManifest(MarketingConfig cfg, bool dryRun, List<Change> changes,
            List<string> skipped)
        {
            var full = ToAbsolute(ANDROID_MANIFEST_PATH);
            if (!File.Exists(full))
            {
                // Dự án chưa custom manifest thì Unity tự sinh lúc build — không có gì để sửa.
                skipped.Add($"{ANDROID_MANIFEST_PATH} - khong co (du an dung manifest Unity tu sinh).");
                return;
            }

            var text = ReadText(full, out var hadBom);
            const string sink = "AndroidManifest";

            ReplaceCapture(ref text,
                new Regex("(<manifest[^>]*\\spackage=\")([^\"]*)(\")"),
                cfg.packageName, sink, "package", changes);
            ReplaceCapture(ref text,
                new Regex("(android:name=\"com\\.facebook\\.sdk\\.ApplicationId\" android:value=\"fb)([^\"]*)(\")"),
                cfg.facebook.appId, sink, "facebook.sdk.ApplicationId", changes);
            ReplaceCapture(ref text,
                new Regex("(android:name=\"com\\.facebook\\.sdk\\.ClientToken\" android:value=\")([^\"]*)(\")"),
                cfg.facebook.clientToken, sink, "facebook.sdk.ClientToken", changes);
            ReplaceCapture(ref text,
                new Regex("(android:authorities=\"com\\.facebook\\.app\\.FacebookContentProvider)([^\"]*)(\")"),
                cfg.facebook.appId, sink, "FacebookContentProvider", changes);

            if (dryRun) return;
            WriteText(full, text, hadBom);
        }

        private static void ApplyPlayerSettings(MarketingConfig cfg, bool dryRun, List<Change> changes)
        {
            const string sink = "PlayerSettings";

            foreach (var target in new[] { NamedBuildTarget.Android, NamedBuildTarget.iOS })
            {
                var current = PlayerSettings.GetApplicationIdentifier(target);
                var field = $"applicationIdentifier[{target.TargetName}]";

                if (string.IsNullOrEmpty(cfg.packageName) || current == cfg.packageName)
                {
                    changes.Add(new Change(sink, field, current, current, true));
                    continue;
                }

                changes.Add(new Change(sink, field, current, cfg.packageName, false));
                if (!dryRun) PlayerSettings.SetApplicationIdentifier(target, cfg.packageName);
            }

            var productName = PlayerSettings.productName;
            if (string.IsNullOrEmpty(cfg.gameName) || productName == cfg.gameName)
            {
                changes.Add(new Change(sink, "productName", productName, productName, true));
                return;
            }

            changes.Add(new Change(sink, "productName", productName, cfg.gameName, false));
            if (!dryRun) PlayerSettings.productName = cfg.gameName;
        }

        private static void ApplyGameConstant(MarketingConfig cfg, bool dryRun, List<Change> changes,
            List<string> skipped)
        {
            var path = FindAsset("GameConstant", ".cs");
            var full = path == null ? null : ToAbsolute(path);
            if (full == null || !File.Exists(full))
            {
                skipped.Add("GameConstant.cs - khong tim thay trong du an nay.");
                return;
            }

            var text = ReadText(full, out var hadBom);
            const string sink = "GameConstant.cs";

            ReplaceCapture(ref text,
                new Regex("(public const string AppsFlyerId = \")([^\"]*)(\";)"),
                cfg.appsflyerDevKey, sink, "AppsFlyerId", changes);
            ReplaceCapture(ref text,
                new Regex("(public const string IOSAppId = \")([^\"]*)(\";)"),
                cfg.appleId, sink, "IOSAppId", changes);
            ReplaceCapture(ref text,
                new Regex("(public const string PackNameAndroidFree = \")([^\"]*)(\";)"),
                cfg.packageName, sink, "PackNameAndroidFree", changes);
            // Bản premium PHẢI khác bản free, nếu không GameSystems.IsPremium bật nhầm và IAP Android
            // chết câm (xem comment tại chỗ khai báo const).
            ReplaceCapture(ref text,
                new Regex("(public const string PackNameAndroidPremium = \")([^\"]*)(\";)"),
                string.IsNullOrEmpty(cfg.packageName) ? "" : cfg.packageName + ".premium",
                sink, "PackNameAndroidPremium", changes);

            // Link store nằm trên nhiều dòng (chuỗi dài) nên regex phải cho phép xuống dòng sau '='.
            ReplaceCapture(ref text,
                new Regex("(public const string LinkStoreFree =\\s*\")([^\"]*)(\";)"),
                cfg.GooglePlayUrl, sink, "LinkStoreFree", changes);
            ReplaceCapture(ref text,
                new Regex("(public const string LinkStorePremium =\\s*\")([^\"]*)(\";)"),
                string.IsNullOrEmpty(cfg.GooglePlayUrl) ? "" : cfg.GooglePlayUrl + ".premium",
                sink, "LinkStorePremium", changes);
            ReplaceCapture(ref text,
                new Regex("(public const string LinkStoreIos =\\s*\")([^\"]*)(\";)"),
                cfg.AppStoreUrl, sink, "LinkStoreIos", changes);
            ReplaceCapture(ref text,
                new Regex("(public const string LinkFacebook =\\s*\")([^\"]*)(\";)"),
                cfg.links.facebookPage, sink, "LinkFacebook", changes);
            ReplaceCapture(ref text,
                new Regex("(public const string LinkPrivacyPolicy =\\s*\")([^\"]*)(\";)"),
                cfg.applovin.privacyPolicyUrl, sink, "LinkPrivacyPolicy", changes);
            ReplaceCapture(ref text,
                new Regex("(public const string LinkTermsOfService =\\s*\")([^\"]*)(\";)"),
                cfg.applovin.termsOfServiceUrl, sink, "LinkTermsOfService", changes);

            if (dryRun) return;
            WriteText(full, text, hadBom);
        }

        /// <summary>
        ///     Consent flow / ATT của MAX. Dữ liệu nằm ở <c>ProjectSettings/AppLovinInternalSettings.json</c>
        ///     và được cache trong <c>AppLovinInternalSettings.Instance</c>, nên phải ghi qua chính type đó
        ///     (nếu sửa file JSON tay thì instance đang mở sẽ ghi đè lại lúc Save).
        ///     <para>
        ///         Gọi bằng reflection: type nằm trong asmdef <c>MaxSdk.IntegrationManager.Editor</c> mà
        ///         <c>Ezg.Editor</c> không reference; reflection cũng giúp script không vỡ khi gỡ/cài lại
        ///         plugin MAX.
        ///     </para>
        /// </summary>
        private static void ApplyAppLovinConsentFlow(MarketingConfig cfg, bool dryRun, List<Change> changes,
            List<string> errors, List<string> skipped)
        {
            const string sink = "AppLovin ConsentFlow";

            // Type nằm trong namespace AppLovinMax.Scripts.IntegrationManager.Editor, nhưng dò theo TÊN
            // NGẮN để không vỡ khi AppLovin đổi namespace giữa các bản plugin (đã đổi ít nhất một lần).
            System.Type type = null;
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                System.Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException)
                {
                    // Assembly nào load hụt thì bỏ qua — không phải assembly của MAX.
                    continue;
                }

                foreach (var candidate in types)
                {
                    if (candidate.Name != "AppLovinInternalSettings") continue;
                    type = candidate;
                    break;
                }

                if (type != null) break;
            }

            if (type == null)
            {
                skipped.Add($"{sink} - khong tim thay type AppLovinInternalSettings (du an chua cai MAX SDK).");
                return;
            }

            var instance = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                               ?.GetValue(null);
            if (instance == null)
            {
                errors.Add($"{sink}: khong lay duoc AppLovinInternalSettings.Instance");
                return;
            }

            var dirty = false;
            dirty |= SetProperty(instance, type, "ConsentFlowEnabled", cfg.applovin.consentFlowEnabled,
                dryRun, sink, changes, errors);
            dirty |= SetProperty(instance, type, "ConsentFlowPrivacyPolicyUrl", cfg.applovin.privacyPolicyUrl,
                dryRun, sink, changes, errors);
            dirty |= SetProperty(instance, type, "ConsentFlowTermsOfServiceUrl",
                cfg.applovin.termsOfServiceUrl, dryRun, sink, changes, errors);

            if (!string.IsNullOrEmpty(cfg.applovin.attDescriptionEn))
            {
                // MAX chỉ dùng chuỗi custom khi cờ override bật — set chuỗi mà quên cờ thì iOS vẫn
                // hiện text mặc định của plugin.
                dirty |= SetProperty(instance, type, "OverrideDefaultUserTrackingUsageDescriptions", true,
                    dryRun, sink, changes, errors);
                dirty |= SetProperty(instance, type, "UserTrackingUsageDescriptionEn",
                    cfg.applovin.attDescriptionEn, dryRun, sink, changes, errors);
            }

            if (dryRun || !dirty) return;
            type.GetMethod("Save", BindingFlags.Public | BindingFlags.Instance)?.Invoke(instance, null);
        }

        #endregion

        #region Write helpers

        /// <summary>
        ///     Ghi một string property nếu khác giá trị hiện tại. Giá trị JSON rỗng = "chưa có" nên bỏ qua,
        ///     KHÔNG xoá giá trị đang dùng (ví dụ banner id không có trong sheet).
        ///     <para>
        ///         Ô nào cũng được ghi vào <paramref name="changes" />, kể cả ô đã khớp — cửa sổ trạng
        ///         thái cần liệt kê ĐỦ mọi thông số cho PM, không chỉ phần đang lệch.
        ///     </para>
        /// </summary>
        private static void SetString(SerializedObject so, string propertyName, string value, string sink,
            List<Change> changes)
        {
            var prop = so.FindProperty(propertyName);
            if (prop == null)
            {
                Debug.LogWarning($"[Marketing] {sink}: khong co field '{propertyName}' - bo qua.");
                return;
            }

            var current = prop.stringValue;
            if (string.IsNullOrEmpty(value) || current == value)
            {
                changes.Add(new Change(sink, propertyName, current, current, true));
                return;
            }

            changes.Add(new Change(sink, propertyName, current, value, false));
            prop.stringValue = value;
        }

        /// <summary>
        ///     Ghi một property (string hoặc bool) qua reflection. Trả về true nếu giá trị khác giá trị
        ///     hiện tại. String rỗng = "chưa có" nên bỏ qua, giống <see cref="SetString" />.
        /// </summary>
        private static bool SetProperty(object instance, System.Type type, string propertyName, object value,
            bool dryRun, string sink, List<Change> changes, List<string> errors)
        {
            var prop = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null)
            {
                errors.Add($"{sink}: khong co property '{propertyName}'");
                return false;
            }

            var current = prop.GetValue(instance);
            var currentText = current?.ToString();

            if ((value is string s && string.IsNullOrEmpty(s)) || Equals(current, value))
            {
                changes.Add(new Change(sink, propertyName, currentText, currentText, true));
                return false;
            }

            changes.Add(new Change(sink, propertyName, currentText, value.ToString(), false));
            if (!dryRun) prop.SetValue(instance, value);
            return true;
        }

        private static void SetFirstArrayElement(SerializedObject so, string propertyName, string value,
            string sink, List<Change> changes, List<string> errors)
        {
            var prop = so.FindProperty(propertyName);
            if (prop == null || !prop.isArray)
            {
                errors.Add($"{sink}: '{propertyName}' khong phai mang - bo qua.");
                return;
            }

            if (prop.arraySize == 0) prop.arraySize = 1;
            var element = prop.GetArrayElementAtIndex(0);
            var current = element.stringValue;
            var field = $"{propertyName}[0]";

            if (string.IsNullOrEmpty(value) || current == value)
            {
                changes.Add(new Change(sink, field, current, current, true));
                return;
            }

            changes.Add(new Change(sink, field, current, value, false));
            element.stringValue = value;
        }

        /// <summary>
        ///     Thay nhóm 2 của regex bằng <paramref name="value" /> (nhóm 1/3 là prefix/suffix neo).
        ///     Không khớp regex nào là lỗi thật — file đã đổi format, im lặng bỏ qua thì sẽ ship id cũ.
        /// </summary>
        private static void ReplaceCapture(ref string text, Regex regex, string value, string sink,
            string field, List<Change> changes)
        {
            var match = regex.Match(text);
            if (!match.Success)
            {
                Debug.LogWarning($"[Marketing] {sink}: khong tim thay '{field}' (regex khong khop) - bo qua.");
                return;
            }

            var current = match.Groups[2].Value;
            if (string.IsNullOrEmpty(value) || current == value)
            {
                changes.Add(new Change(sink, field, current, current, true));
                return;
            }

            changes.Add(new Change(sink, field, current, value, false));
            text = text.Remove(match.Groups[2].Index, current.Length)
                       .Insert(match.Groups[2].Index, value);
        }

        private static string ToAbsolute(string assetPath) =>
            Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));

        private static string ReadText(string path, out bool hadBom)
        {
            var bytes = File.ReadAllBytes(path);
            hadBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            return new UTF8Encoding(false).GetString(bytes, hadBom ? 3 : 0, bytes.Length - (hadBom ? 3 : 0));
        }

        private static void WriteText(string path, string text, bool hadBom) =>
            File.WriteAllText(path, text, new UTF8Encoding(hadBom));

        #endregion

        #region Report

        private static string Report(Status status, bool dryRun)
        {
            var sb = new StringBuilder();
            sb.AppendLine(dryRun
                ? "[Marketing] CHECK (dry run) - chua ghi gi."
                : "[Marketing] APPLY - da ghi vao project.");
            sb.AppendLine($"Nguon: {MarketingConfig.JsonPath}");
            sb.AppendLine();

            var cfg = status.Config;
            var errors = status.Errors;
            if (cfg == null)
            {
                foreach (var error in errors) sb.AppendLine(error);
                var failure = sb.ToString();
                Debug.LogError(failure);
                return failure;
            }

            var pending = status.PendingCount;
            if (pending == 0)
            {
                sb.AppendLine("Khong co o nao lech - project dang khop sheet marketing.");
            }
            else
            {
                sb.AppendLine($"{pending} o {(dryRun ? "SE doi" : "da doi")}:");
                foreach (var change in status.Rows)
                    if (!change.Matched)
                        sb.AppendLine(change.ToString());
            }

            if (status.Skipped.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Sink khong co trong du an nay (bo qua, khong phai loi):");
                foreach (var item in status.Skipped) sb.AppendLine($"  - {item}");
            }

            if (status.Todos.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"CON PHAI LAM TAY ({status.Todos.Count}) - script khong tu lam duoc:");
                foreach (var item in status.Todos) sb.AppendLine($"  - {item}");
            }

            sb.AppendLine();
            sb.AppendLine("Chi de doi chieu (khai ben dashboard AppLovin MAX, app KHONG nhung):");
            sb.AppendLine($"  Admob unit   : android rw={cfg.admob.android.rewarded} inter={cfg.admob.android.interstitial}");
            sb.AppendLine($"                 ios     rw={cfg.admob.ios.rewarded} inter={cfg.admob.ios.interstitial}");
            sb.AppendLine($"  Unity Ads    : android id={cfg.unityAds.android.gameId} rw={cfg.unityAds.android.rewarded} inter={cfg.unityAds.android.interstitial}");
            sb.AppendLine($"                 ios     id={cfg.unityAds.ios.gameId} rw={cfg.unityAds.ios.rewarded} inter={cfg.unityAds.ios.interstitial}");
            sb.AppendLine($"  FB placement : android rw={cfg.facebook.android.rewarded} inter={cfg.facebook.android.interstitial}");
            sb.AppendLine($"                 ios     rw={cfg.facebook.ios.rewarded} inter={cfg.facebook.ios.interstitial}");

            if (errors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("LOI:");
                foreach (var error in errors) sb.AppendLine($"  {error}");
            }

            var text = sb.ToString();
            if (errors.Count > 0) Debug.LogError(text);
            else Debug.Log(text);
            return text;
        }

        /// <summary>
        ///     Những thứ KHÔNG sink nào ghi hộ được — hoặc vì sheet chưa có số, hoặc vì nó nằm ngoài
        ///     Unity (dashboard MAX, Firebase console). Liệt kê ra để "bấm 1 nút" không bị hiểu nhầm là
        ///     đã xong 100%.
        /// </summary>
        private static List<string> CollectManualTodos(MarketingConfig cfg)
        {
            var todo = new List<string>();

            if (string.IsNullOrEmpty(cfg.appleId))
                todo.Add("Sheet chua co Apple ID -> GameConstant.IOSAppId + LinkStoreIos giu gia tri cu; "
                         + "attribution AppsFlyer tren iOS con sai.");
            if (string.IsNullOrEmpty(cfg.links.facebookPage))
                todo.Add("Sheet chua co link fanpage -> GameConstant.LinkFacebook giu gia tri cu.");
            if (string.IsNullOrEmpty(cfg.max.android.banner) && string.IsNullOrEmpty(cfg.max.ios.banner))
                todo.Add("Sheet khong co banner ad-unit-id -> AdsConfig giu banner id cu "
                         + "(hien enabledFormats khong bat Banner nen chua anh huong).");

            if (!File.Exists(ToAbsolute("Assets/google-services.json")))
                todo.Add("Thieu Assets/google-services.json (tai tu Firebase console, dung app Android "
                         + $"'{cfg.packageName}').");
            if (!File.Exists(ToAbsolute("Assets/GoogleService-Info.plist")))
                todo.Add("Thieu Assets/GoogleService-Info.plist (tai tu Firebase console, dung app iOS "
                         + $"'{cfg.packageName}').");

            var adsConfigPath = FindAsset("AdsConfig", ".asset");
            var adsConfig = adsConfigPath == null
                ? null
                : AssetDatabase.LoadAssetAtPath<ScriptableObject>(adsConfigPath);
            if (adsConfig != null
                && new SerializedObject(adsConfig).FindProperty("debugAds")?.boolValue == true)
                todo.Add("AdsConfig.debugAds dang BAT - build release voi co nay la mat sach doanh thu ads.");

            todo.Add("Tao/doi chieu ad unit ben dashboard AppLovin MAX (Admob/Unity/FB placement o duoi) - "
                     + "Unity Editor khong voi toi day.");

            return todo;
        }

        private static string Truncate(string text, int max) =>
            text.Length <= max ? text : text.Substring(0, max) + "\n...(xem Console)";

        #endregion
    }
}
#endif
