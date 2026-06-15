using AppsFlyerSDK;
using Cysharp.Threading.Tasks;
using Firebase.Analytics;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Ezg.Tracking
{
    /// <summary>
    /// Generic, game-agnostic tracking engine. Forwards events and user properties to Firebase Analytics
    /// and AppsFlyer. Consuming projects can use either of two zero-coupling styles:
    /// <list type="bullet">
    /// <item>Typed: pass any plain object whose public instance fields describe the payload.</item>
    /// <item>Dictionary: pass an <see cref="IDictionary{TKey,TValue}"/> of name → value with no custom
    /// classes at all (recommended for new projects — zero boilerplate).</item>
    /// </list>
    /// This class knows nothing about any specific game.
    /// </summary>
    public static class TrackingService
    {
        #region Fields

        public static bool IsTracking = true;

        public static bool IsInitFirebase;

        /// <summary>
        /// Sentinel for optional numeric values: any int/long/float/double equal to this value is treated as
        /// "unset" and skipped. Applies to both the typed and dictionary styles.
        /// </summary>
        public const int NULL_NUMBER = -1;

        /// <summary>
        /// Optional hook supplied by the consuming project. Returns the user-property payload — either a typed
        /// object (public fields) or an <see cref="IDictionary{TKey,TValue}"/> of name → value. Invoked before
        /// every Firebase event and whenever <see cref="SetUserProperty(object)"/> is called without an explicit
        /// payload. Leave null to disable user property tracking.
        /// </summary>
        public static Func<object> UserPropertyProvider;

        private const int FIREBASE_UA_MAX_LENGTH = 36;
        private const string UA_UNATTRIBUTED = "Unattributed";
        private const string UA_UNAVAILABLE = "Unavailable";

        private const BindingFlags CONFIG_FIELD_FLAGS = BindingFlags.Public | BindingFlags.Instance;

        // Cache FieldInfo[] theo Type để tránh reflection (GetFields) lặp lại mỗi lần gửi event.
        private static readonly Dictionary<Type, FieldInfo[]> _fieldCache = new Dictionary<Type, FieldInfo[]>();

        #endregion

        #region Public Methods — Firebase events

        /// <summary>
        /// Sends a Firebase event using a typed config object (public fields become parameters).
        /// </summary>
        /// <param name="eventName">The Firebase event name.</param>
        /// <param name="config">The optional config object, or null for a parameterless event.</param>
        public static async UniTask SendFirebase(string eventName, object config = null)
        {
            if (!IsInitFirebase)
            {
                return;
            }

            try
            {
                SetUserProperty();
                if (config == null)
                {
                    FirebaseAnalytics.LogEvent(eventName);
                    return;
                }

                LogToFirebase(eventName, BuildFirebaseParameters(eventName, AsPairs(config)));
            }
            catch (Exception e)
            {
                Debug.LogError($"[TrackingService] Firebase event '{eventName}' failed: {e}");
            }
        }

        /// <summary>
        /// Sends a Firebase event using a plain dictionary of name → value (no custom classes required).
        /// </summary>
        /// <param name="eventName">The Firebase event name.</param>
        /// <param name="parameters">The event parameters, or null for a parameterless event.</param>
        public static async UniTask SendFirebase(string eventName, IDictionary<string, object> parameters)
        {
            if (!IsInitFirebase)
            {
                return;
            }

            try
            {
                SetUserProperty();
                if (parameters == null)
                {
                    FirebaseAnalytics.LogEvent(eventName);
                    return;
                }

                LogToFirebase(eventName, BuildFirebaseParameters(eventName, parameters));
            }
            catch (Exception e)
            {
                Debug.LogError($"[TrackingService] Firebase event '{eventName}' failed: {e}");
            }
        }

        /// <summary>
        /// Sends a Firebase event using any enum as the event name (converted via <c>ToString</c>). Gives the
        /// call site type-safety with its own enum while the engine stays agnostic of the enum type.
        /// </summary>
        /// <typeparam name="TEnum">Any enum type.</typeparam>
        /// <param name="eventName">The enum value whose member name is used as the event name.</param>
        /// <param name="config">The optional config object.</param>
        public static UniTask SendFirebase<TEnum>(TEnum eventName, object config = null) where TEnum : struct, Enum
            => SendFirebase(eventName.ToString(), config);

        /// <summary>
        /// Sends a Firebase event using any enum as the event name and a plain dictionary of parameters.
        /// </summary>
        /// <typeparam name="TEnum">Any enum type.</typeparam>
        /// <param name="eventName">The enum value whose member name is used as the event name.</param>
        /// <param name="parameters">The event parameters.</param>
        public static UniTask SendFirebase<TEnum>(TEnum eventName, IDictionary<string, object> parameters) where TEnum : struct, Enum
            => SendFirebase(eventName.ToString(), parameters);

        #endregion

        #region Public Methods — User properties

        /// <summary>
        /// Sets Firebase user properties from a typed object. When <paramref name="config"/> is null, the
        /// registered <see cref="UserPropertyProvider"/> is used (no-op if none is registered). A payload that
        /// is actually an <see cref="IDictionary{TKey,TValue}"/> is handled transparently.
        /// </summary>
        /// <param name="config">The user property object, or null to use the provider.</param>
        public static void SetUserProperty(object config = null)
        {
            if (!IsInitFirebase)
            {
                return;
            }

            try
            {
                if (config == null)
                {
                    config = UserPropertyProvider?.Invoke();
                    if (config == null)
                    {
                        return;
                    }
                }

                ApplyUserProperties(AsPairs(config));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TrackingService] SetUserProperty failed: {e}");
            }
        }

        /// <summary>
        /// Sets Firebase user properties from a plain dictionary of name → value.
        /// </summary>
        /// <param name="userProperties">The user properties to set.</param>
        public static void SetUserProperty(IDictionary<string, object> userProperties)
        {
            if (!IsInitFirebase || userProperties == null)
            {
                return;
            }

            try
            {
                ApplyUserProperties(userProperties);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TrackingService] SetUserProperty failed: {e}");
            }
        }

        #endregion

        #region Public Methods — AppsFlyer events

        /// <summary>
        /// Sends an AppsFlyer event using a typed config object. Only string fields are forwarded.
        /// </summary>
        /// <param name="eventName">The AppsFlyer event name.</param>
        /// <param name="config">The optional config object.</param>
        public static void SendAppsFlyer(string eventName, object config = null)
        {
            if (!IsInitFirebase)
            {
                return;
            }
            try
            {
                if (config == null)
                {
                    AppsFlyer.sendEvent(eventName, null);
                    return;
                }

                AppsFlyer.sendEvent(eventName, BuildAppsFlyerDict(AsPairs(config)));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TrackingService] AppsFlyer event '{eventName}' failed: {e}");
            }
        }

        /// <summary>
        /// Sends an AppsFlyer event using a plain dictionary. Only string values are forwarded.
        /// </summary>
        /// <param name="eventName">The AppsFlyer event name.</param>
        /// <param name="parameters">The event parameters.</param>
        public static void SendAppsFlyer(string eventName, IDictionary<string, object> parameters)
        {
            if (!IsInitFirebase)
            {
                return;
            }
            try
            {
                if (parameters == null)
                {
                    AppsFlyer.sendEvent(eventName, null);
                    return;
                }

                AppsFlyer.sendEvent(eventName, BuildAppsFlyerDict(parameters));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TrackingService] AppsFlyer event '{eventName}' failed: {e}");
            }
        }

        /// <summary>
        /// Sends an AppsFlyer event using any enum as the event name (converted via <c>ToString</c>).
        /// </summary>
        /// <typeparam name="TEnum">Any enum type.</typeparam>
        /// <param name="eventName">The enum value whose member name is used as the event name.</param>
        /// <param name="config">The optional config object.</param>
        public static void SendAppsFlyer<TEnum>(TEnum eventName, object config = null) where TEnum : struct, Enum
            => SendAppsFlyer(eventName.ToString(), config);

        /// <summary>
        /// Sends an AppsFlyer event using any enum as the event name and a plain dictionary of parameters.
        /// </summary>
        /// <typeparam name="TEnum">Any enum type.</typeparam>
        /// <param name="eventName">The enum value whose member name is used as the event name.</param>
        /// <param name="parameters">The event parameters.</param>
        public static void SendAppsFlyer<TEnum>(TEnum eventName, IDictionary<string, object> parameters) where TEnum : struct, Enum
            => SendAppsFlyer(eventName.ToString(), parameters);

        #endregion

        #region Public Methods — User acquisition

        /// <summary>
        /// Sets user acquisition properties based on conversion data.
        /// </summary>
        /// <param name="conversionData">The conversion data dictionary.</param>
        public static void SetUAProperties(Dictionary<string, string> conversionData)
        {
            if (!IsInitFirebase) return;

            string uaNetwork, uaCampaign, uaAdgroup, uaCreative;

            if (conversionData == null)
            {
                uaNetwork = UA_UNATTRIBUTED;
                uaCampaign = UA_UNATTRIBUTED;
                uaAdgroup = UA_UNATTRIBUTED;
                uaCreative = UA_UNATTRIBUTED;
            }
            else
            {
                conversionData.TryGetValue("media_source", out var mediaSource);
                conversionData.TryGetValue("campaign", out var campaign);

                if (!conversionData.TryGetValue("adset", out var adset))
                    conversionData.TryGetValue("adset_id", out adset);

                if (!conversionData.TryGetValue("ad", out var ad))
                    conversionData.TryGetValue("ad_id", out ad);

                bool isOrganic = !string.IsNullOrEmpty(mediaSource) &&
                                 mediaSource.IndexOf("Organic", StringComparison.OrdinalIgnoreCase) >= 0;

                if (isOrganic)
                {
                    uaNetwork = mediaSource;
                    uaCampaign = HasUsableValue(campaign) ? campaign : mediaSource;
                    uaAdgroup = HasUsableValue(adset) ? adset : mediaSource;
                    uaCreative = HasUsableValue(ad) ? ad : mediaSource;
                }
                else
                {
                    uaNetwork = HasUsableValue(mediaSource) ? mediaSource : UA_UNAVAILABLE;
                    uaCampaign = HasUsableValue(campaign) ? campaign : UA_UNAVAILABLE;
                    uaAdgroup = HasUsableValue(adset) ? adset : UA_UNAVAILABLE;
                    uaCreative = HasUsableValue(ad) ? ad : UA_UNAVAILABLE;
                }
            }

            FirebaseAnalytics.SetUserProperty("ua_network", TruncateForFirebase(uaNetwork));
            FirebaseAnalytics.SetUserProperty("ua_campaign", TruncateForFirebase(uaCampaign));
            FirebaseAnalytics.SetUserProperty("ua_adgroup", TruncateForFirebase(uaAdgroup));
            FirebaseAnalytics.SetUserProperty("ua_creative", TruncateForFirebase(uaCreative));

#if UNITY_EDITOR
            Debug.Log($"[UA] network={uaNetwork} | campaign={uaCampaign} | adgroup={uaAdgroup} | creative={uaCreative}");
#endif
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Normalizes a payload (typed object or dictionary) into a sequence of name → value pairs.
        /// </summary>
        /// <param name="payload">The typed config object or <see cref="IDictionary{TKey,TValue}"/>.</param>
        /// <returns>The name/value pairs to track.</returns>
        private static IEnumerable<KeyValuePair<string, object>> AsPairs(object payload)
        {
            if (payload is IDictionary<string, object> dict)
            {
                return dict;
            }
            return EnumerateFields(payload);
        }

        /// <summary>
        /// Yields each public instance field of a config object as a name → value pair.
        /// </summary>
        /// <param name="config">The config object to reflect over.</param>
        /// <returns>The field name/value pairs.</returns>
        private static IEnumerable<KeyValuePair<string, object>> EnumerateFields(object config)
        {
            foreach (var prop in GetCachedFields(config.GetType()))
            {
                yield return new KeyValuePair<string, object>(prop.Name, prop.GetValue(config));
            }
        }

        /// <summary>
        /// Builds the Firebase parameter list from name → value pairs, filtering null/empty strings and
        /// <see cref="NULL_NUMBER"/> sentinels.
        /// </summary>
        private static List<Parameter> BuildFirebaseParameters(string eventName, IEnumerable<KeyValuePair<string, object>> pairs)
        {
            var parameters = new List<Parameter>();
#if UNITY_EDITOR
            var param = "";
#endif
            foreach (var kv in pairs)
            {
                if (!TryResolveValue(kv.Key, kv.Value, out var parameter, out var stringValue))
                {
                    continue;
                }

                parameters.Add(parameter);
#if UNITY_EDITOR
                param += kv.Key + ": " + stringValue + "\n";
#endif
            }
#if UNITY_EDITOR
            Debug.Log("-----------FIREBASE TRACKING-----------");
            Debug.Log(eventName);
            Debug.Log(param);
#endif
            return parameters;
        }

        /// <summary>
        /// Dispatches a built parameter list to Firebase, logging with or without parameters as appropriate.
        /// </summary>
        private static void LogToFirebase(string eventName, List<Parameter> parameters)
        {
            if (parameters.Count == 0)
            {
                FirebaseAnalytics.LogEvent(eventName);
            }
            else
            {
                FirebaseAnalytics.LogEvent(eventName, parameters.ToArray());
            }
        }

        /// <summary>
        /// Applies name → value pairs as Firebase user properties (filtered through <see cref="TryResolveValue"/>).
        /// </summary>
        private static void ApplyUserProperties(IEnumerable<KeyValuePair<string, object>> pairs)
        {
#if UNITY_EDITOR
            var logDict = new Dictionary<string, string>();
#endif
            foreach (var kv in pairs)
            {
                if (!TryResolveValue(kv.Key, kv.Value, out _, out var stringValue))
                {
                    continue;
                }

#if UNITY_EDITOR
                logDict[kv.Key] = stringValue;
#endif
                FirebaseAnalytics.SetUserProperty(kv.Key, stringValue);
            }
#if UNITY_EDITOR
            var sb = new System.Text.StringBuilder();
            sb.Append("SET PROPERTIES: {\n");
            foreach (var kv in logDict)
                sb.Append($"  \"{kv.Key}\": \"{kv.Value}\",\n");
            sb.Append("}");
            Debug.Log(sb.ToString());
#endif
        }

        /// <summary>
        /// Builds an AppsFlyer string dictionary from name → value pairs (only string values are forwarded).
        /// </summary>
        private static Dictionary<string, string> BuildAppsFlyerDict(IEnumerable<KeyValuePair<string, object>> pairs)
        {
            var dict = new Dictionary<string, string>();
            foreach (var kv in pairs)
            {
                var value = kv.Value as string;
                if (!string.IsNullOrEmpty(value))
                    dict.Add(kv.Key, value);
            }
            return dict;
        }

        /// <summary>
        /// Returns the cached public instance fields for a config type, computing them once per type.
        /// </summary>
        /// <param name="type">The config type to reflect over.</param>
        /// <returns>The cached array of <see cref="FieldInfo"/>.</returns>
        private static FieldInfo[] GetCachedFields(Type type)
        {
            if (!_fieldCache.TryGetValue(type, out var fields))
            {
                fields = type.GetFields(CONFIG_FIELD_FLAGS);
                _fieldCache[type] = fields;
            }
            return fields;
        }

        /// <summary>
        /// Resolves a name → value pair into a Firebase <see cref="Parameter"/> and its string representation,
        /// filtering out null/empty strings and <see cref="NULL_NUMBER"/> sentinels. Switches on the value's
        /// runtime type so both typed fields and dictionary entries are handled uniformly.
        /// </summary>
        /// <param name="name">The parameter name.</param>
        /// <param name="value">The value to resolve.</param>
        /// <param name="parameter">The resolved Firebase parameter when the value is usable.</param>
        /// <param name="stringValue">The resolved string representation when the value is usable.</param>
        /// <returns>True if the value is usable; otherwise false.</returns>
        private static bool TryResolveValue(string name, object value, out Parameter parameter, out string stringValue)
        {
            parameter = null;
            stringValue = null;

            switch (value)
            {
                case string s:
                    if (string.IsNullOrEmpty(s)) return false;
                    parameter = new Parameter(name, s);
                    stringValue = s;
                    return true;
                case float f:
                    if (f == NULL_NUMBER) return false;
                    parameter = new Parameter(name, f);
                    stringValue = f.ToString();
                    return true;
                case double d:
                    if (d == NULL_NUMBER) return false;
                    parameter = new Parameter(name, d);
                    stringValue = d.ToString();
                    return true;
                case long l:
                    if (l == NULL_NUMBER) return false;
                    parameter = new Parameter(name, l);
                    stringValue = l.ToString();
                    return true;
                case int i:
                    if (i == NULL_NUMBER) return false;
                    parameter = new Parameter(name, i);
                    stringValue = i.ToString();
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Checks if a string has a usable value (not null or whitespace).
        /// </summary>
        /// <param name="value">The string to check.</param>
        /// <returns>True if the string is usable, false otherwise.</returns>
        private static bool HasUsableValue(string value) => !string.IsNullOrWhiteSpace(value);

        /// <summary>
        /// Truncates a string to fit the maximum length allowed by Firebase user properties.
        /// </summary>
        /// <param name="value">The string to truncate.</param>
        /// <returns>The truncated string.</returns>
        private static string TruncateForFirebase(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= FIREBASE_UA_MAX_LENGTH) return value;
            return value.Substring(0, FIREBASE_UA_MAX_LENGTH);
        }

        #endregion
    }
}
