#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using Ezg.Editor.Shared.EzgKit;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Ezg.Editor.Shared.Setup
{
    /// <summary>
    ///     Đọc / ghi asset của SDK bên thứ ba qua reflection + <see cref="SerializedObject" />: kit không tham
    ///     chiếu assembly của MAX, Facebook hay com.ezg.ads — dự án chưa cài SDK nào thì phần đó trả "không có",
    ///     không vỡ compile.
    /// </summary>
    internal static class SerializedAsset
    {
        /// <summary>Asset đầu tiên dưới <c>Assets/</c> có type tên <paramref name="typeName" /> (ưu tiên đúng tên file).</summary>
        internal static ScriptableObject Find(string typeName, string preferredName = null)
        {
            var type = TypeFinder.Find(typeName, typeof(ScriptableObject));
            if (type == null) return null;

            ScriptableObject fallback = null;
            foreach (var guid in AssetDatabase.FindAssets("t:" + type.Name))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                var asset = AssetDatabase.LoadAssetAtPath(path, type) as ScriptableObject;
                if (asset == null) continue;
                if (preferredName != null && asset.name == preferredName) return asset;
                fallback ??= asset;
            }

            return fallback;
        }

        internal static string GetString(Object asset, string field)
        {
            if (asset == null) return null;
            var prop = new SerializedObject(asset).FindProperty(field);
            return prop == null ? null : prop.propertyType == SerializedPropertyType.String ? prop.stringValue : null;
        }

        internal static bool? GetBool(Object asset, string field)
        {
            if (asset == null) return null;
            var prop = new SerializedObject(asset).FindProperty(field);
            return prop == null || prop.propertyType != SerializedPropertyType.Boolean ? null : prop.boolValue;
        }

        internal static int? GetInt(Object asset, string field)
        {
            if (asset == null) return null;
            var prop = new SerializedObject(asset).FindProperty(field);
            if (prop == null) return null;
            return prop.propertyType switch
            {
                SerializedPropertyType.Integer => prop.intValue,
                SerializedPropertyType.Enum => prop.intValue,
                _ => null,
            };
        }

        internal static string FirstArrayElement(Object asset, string field)
        {
            if (asset == null) return null;
            var prop = new SerializedObject(asset).FindProperty(field);
            if (prop == null || !prop.isArray || prop.arraySize == 0) return null;
            return prop.GetArrayElementAtIndex(0).stringValue;
        }

        /// <summary>
        ///     Ghi nhiều field một lượt. Value kiểu string / bool / int; field là mảng string thì ghi phần tử 0
        ///     (đặt tên kèm hậu tố <c>[0]</c>). Value null = không đụng. Trả về số ô thật sự đổi.
        /// </summary>
        internal static int Write(Object asset, string sink, IEnumerable<(string Field, object Value, bool Secret)> values,
            bool dryRun, List<ChangeRow> changes)
        {
            if (asset == null) return 0;
            var so = new SerializedObject(asset);
            var changed = 0;

            foreach (var (fieldName, value, secret) in values)
            {
                if (value == null) continue;
                var arrayElement = fieldName.EndsWith("[0]", StringComparison.Ordinal);
                var field = arrayElement ? fieldName.Substring(0, fieldName.Length - 3) : fieldName;
                var prop = so.FindProperty(field);
                if (prop == null)
                {
                    changes?.Add(new ChangeRow(sink, fieldName, "(không có field)", Convert.ToString(value), true));
                    continue;
                }

                if (arrayElement)
                {
                    if (!prop.isArray) continue;
                    var current = prop.arraySize > 0 ? prop.GetArrayElementAtIndex(0).stringValue : string.Empty;
                    var wanted = Convert.ToString(value)?.Trim() ?? string.Empty;
                    var same = current == wanted;
                    changes?.Add(new ChangeRow(sink, fieldName, current, wanted, same, secret));
                    if (same || dryRun) continue;
                    if (prop.arraySize == 0) prop.arraySize = 1;
                    prop.GetArrayElementAtIndex(0).stringValue = wanted;
                    changed++;
                    continue;
                }

                switch (prop.propertyType)
                {
                    case SerializedPropertyType.Boolean:
                    {
                        var wanted = value is bool b ? b : Convert.ToString(value) == "true";
                        var same = prop.boolValue == wanted;
                        changes?.Add(new ChangeRow(sink, fieldName, prop.boolValue ? "bật" : "tắt", wanted ? "bật" : "tắt", same));
                        if (same || dryRun) continue;
                        prop.boolValue = wanted;
                        changed++;
                        break;
                    }
                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.Enum:
                    {
                        var wanted = Convert.ToInt32(value);
                        var same = prop.intValue == wanted;
                        changes?.Add(new ChangeRow(sink, fieldName, prop.intValue.ToString(), wanted.ToString(), same));
                        if (same || dryRun) continue;
                        prop.intValue = wanted;
                        changed++;
                        break;
                    }
                    default:
                    {
                        var current = prop.stringValue ?? string.Empty;
                        var wanted = Convert.ToString(value)?.Trim() ?? string.Empty;
                        var same = current == wanted;
                        changes?.Add(new ChangeRow(sink, fieldName, current, wanted, same, secret));
                        if (same || dryRun) continue;
                        prop.stringValue = wanted;
                        changed++;
                        break;
                    }
                }
            }

            if (!dryRun && changed > 0)
            {
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssetIfDirty(asset);
            }

            return changed;
        }
    }

    /// <summary>AdsConfig của <c>com.ezg.ads</c> — thứ runtime đọc lúc init mediation.</summary>
    internal static class AdsConfigBridge
    {
        internal const string TYPE = "AdsConfig";

        /// <summary>Bit của enum <c>AdFormats</c> (com.ezg.ads). Giữ ở đây để không tham chiếu assembly ads.</summary>
        internal static readonly (int Bit, string Label)[] Formats =
        {
            (1 << 0, "Banner"),
            (1 << 1, "Interstitial"),
            (1 << 2, "Rewarded"),
            (1 << 3, "MRec"),
            (1 << 4, "Native"),
        };

        internal static ScriptableObject Find() => SerializedAsset.Find(TYPE, TYPE);

        internal static bool Installed => TypeFinder.Find(TYPE, typeof(ScriptableObject)) != null;
    }

    /// <summary>
    ///     AppLovin MAX: <c>AppLovinSettings.asset</c> (sdk key, AdMob app id) và consent flow / ATT trong
    ///     <c>AppLovinInternalSettings</c> (lưu ở <c>ProjectSettings/AppLovinInternalSettings.json</c>).
    /// </summary>
    internal static class AppLovinBridge
    {
        internal static bool Installed => SettingsType != null;

        private static Type SettingsType => TypeFinder.Find("AppLovinSettings", typeof(ScriptableObject));

        private static Type InternalType => TypeFinder.Find("AppLovinInternalSettings", typeof(ScriptableObject));

        internal static ScriptableObject FindSettings() => SerializedAsset.Find("AppLovinSettings", "AppLovinSettings");

        /// <summary>
        ///     Lấy (và tạo nếu chưa có) AppLovinSettings qua <c>AppLovinSettings.Instance</c> — đúng đường MAX tự
        ///     tạo asset, nên vị trí khớp Integration Manager.
        /// </summary>
        internal static ScriptableObject FindOrCreateSettings()
        {
            var existing = FindSettings();
            if (existing != null) return existing;
            var type = SettingsType;
            var instance = type?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            AssetDatabase.SaveAssets();
            return instance as ScriptableObject;
        }

        #region Consent flow

        private static object InternalInstance =>
            InternalType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

        internal static object GetConsent(string property)
        {
            var instance = InternalInstance;
            return instance?.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance);
        }

        internal static bool ConsentEnabled => GetConsent("ConsentFlowEnabled") is true;

        internal static string ConsentPrivacyUrl => GetConsent("ConsentFlowPrivacyPolicyUrl") as string;

        internal static string ConsentTermsUrl => GetConsent("ConsentFlowTermsOfServiceUrl") as string;

        internal static string AttText =>
            GetConsent("OverrideDefaultUserTrackingUsageDescriptions") is true
                ? GetConsent("UserTrackingUsageDescriptionEn") as string
                : null;

        /// <summary>Ghi consent flow. Chuỗi rỗng = giữ nguyên; null = không đụng.</summary>
        internal static bool WriteConsent(bool? enabled, string privacy, string terms, string att, bool dryRun,
            List<ChangeRow> changes, out string error)
        {
            error = null;
            var instance = InternalInstance;
            if (instance == null)
            {
                error = "Không có AppLovinInternalSettings (dự án chưa cài MAX SDK).";
                return false;
            }

            var type = instance.GetType();
            var dirty = false;
            const string sink = "AppLovin ConsentFlow";

            bool Set(string property, object value)
            {
                if (value == null || value is string s && s.Length == 0) return false;
                var prop = type.GetProperty(property, BindingFlags.Public | BindingFlags.Instance);
                if (prop == null)
                {
                    changes?.Add(new ChangeRow(sink, property, "(không có property)", Convert.ToString(value), true));
                    return false;
                }

                var current = prop.GetValue(instance);
                var same = Equals(current, value);
                changes?.Add(new ChangeRow(sink, property, Convert.ToString(current), Convert.ToString(value), same));
                if (same || dryRun) return false;
                prop.SetValue(instance, value);
                return true;
            }

            if (enabled.HasValue) dirty |= Set("ConsentFlowEnabled", enabled.Value);
            dirty |= Set("ConsentFlowPrivacyPolicyUrl", privacy);
            dirty |= Set("ConsentFlowTermsOfServiceUrl", terms);
            if (!string.IsNullOrEmpty(att))
            {
                dirty |= Set("OverrideDefaultUserTrackingUsageDescriptions", true);
                dirty |= Set("UserTrackingUsageDescriptionEn", att);
            }

            if (!dryRun && dirty) type.GetMethod("Save", BindingFlags.Public | BindingFlags.Instance)?.Invoke(instance, null);
            return true;
        }

        #endregion
    }

    /// <summary>Facebook SDK: <c>FacebookSettings.asset</c> giữ 3 mảng song song, dự án chỉ dùng phần tử 0.</summary>
    internal static class FacebookBridge
    {
        internal static bool Installed => TypeFinder.Find("FacebookSettings", typeof(ScriptableObject)) != null;

        internal static ScriptableObject Find() => SerializedAsset.Find("FacebookSettings", "FacebookSettings");

        internal static string AppId => SerializedAsset.FirstArrayElement(Find(), "appIds");

        internal static string ClientToken => SerializedAsset.FirstArrayElement(Find(), "clientTokens");

        /// <summary>Có chỗ nào trong code game gọi <c>FB.Init</c> không (SDK cài mà không init = không attribution).</summary>
        internal static bool HasInitCall()
        {
            return SourceIndex.Get("ezgkit.fbinit", files =>
            {
                foreach (var file in files)
                    if (!file.IsThirdParty && file.Text.Contains("FB.Init("))
                        return true;
                return false;
            });
        }
    }
}
#endif
