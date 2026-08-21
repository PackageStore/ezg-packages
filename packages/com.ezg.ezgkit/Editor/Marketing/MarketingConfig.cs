#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;

namespace Ezg.Editor.Shared.Marketing
{
    /// <summary>
    ///     DTO cho <c>marketing_config.json</c> — bản chép 1:1 sheet marketing (package name, MAX/Admob/
    ///     Unity/Facebook ad id, AppsFlyer dev key, Apple ID).
    ///     <para>
    ///         Đây là SOURCE OF TRUTH: mọi nơi trong project chứa các số đó (AdsConfig.asset,
    ///         AppLovinSettings.asset, FacebookSettings.asset, AndroidManifest.xml, PlayerSettings,
    ///         GameConstant.cs) đều do <see cref="MarketingConfigApplier" /> ghi ra từ file này.
    ///         Sửa tay ở các nơi đó sẽ bị lần Apply sau ghi đè.
    ///     </para>
    ///     <para>
    ///         Field đặt tên khớp KEY JSON (yêu cầu của <see cref="JsonUtility" />), nên không theo
    ///         chuẩn `_camelCase` của runtime code.
    ///     </para>
    /// </summary>
    [Serializable]
    public class MarketingConfig
    {
        #region Nested types

        /// <summary>Bộ ad-unit-id MAX của một nền tảng.</summary>
        [Serializable]
        public class MaxPlatform
        {
            public string rewarded;
            public string interstitial;

            /// <summary>Sheet marketing không cấp banner id — để rỗng là giữ nguyên AdsConfig.</summary>
            public string banner;
        }

        [Serializable]
        public class MaxSection
        {
            /// <summary>SDK key dùng chung cho cả 2 nền tảng (sheet chỉ có một ô).</summary>
            public string sdkKey;

            public MaxPlatform android;
            public MaxPlatform ios;
        }

        /// <summary>
        ///     Admob theo nền tảng. Chỉ <see cref="appId" /> là thứ app phải nhúng (AppLovin post-process
        ///     ghi vào AndroidManifest + Info.plist); <see cref="rewarded" />/<see cref="interstitial" />
        ///     là ad unit khai bên dashboard MAX, giữ ở đây để đối chiếu chứ app không đọc.
        /// </summary>
        [Serializable]
        public class AdmobPlatform
        {
            public string appId;
            public string rewarded;
            public string interstitial;
        }

        [Serializable]
        public class AdmobSection
        {
            public AdmobPlatform android;
            public AdmobPlatform ios;
        }

        /// <summary>Placement Audience Network — khai bên dashboard MAX, app không đọc.</summary>
        [Serializable]
        public class FacebookPlatform
        {
            public string rewarded;
            public string interstitial;
        }

        [Serializable]
        public class FacebookSection
        {
            public string appId;
            public string clientToken;
            public string appLabel;
            public FacebookPlatform android;
            public FacebookPlatform ios;
        }

        /// <summary>Unity Ads — game id + placement khai bên dashboard MAX, app không đọc.</summary>
        [Serializable]
        public class UnityAdsPlatform
        {
            public string gameId;
            public string rewarded;
            public string interstitial;
        }

        [Serializable]
        public class UnityAdsSection
        {
            public UnityAdsPlatform android;
            public UnityAdsPlatform ios;
        }

        /// <summary>
        ///     Consent flow / ATT của MAX — nằm trong <c>ProjectSettings/AppLovinInternalSettings.json</c>
        ///     chứ không phải AppLovinSettings.asset, và là thứ Apple soi khi review iOS.
        /// </summary>
        [Serializable]
        public class AppLovinSection
        {
            public bool consentFlowEnabled;
            public string privacyPolicyUrl;
            public string termsOfServiceUrl;

            /// <summary>Chuỗi hiện trong hộp ATT (iOS). Rỗng = dùng bản mặc định của MAX.</summary>
            public string attDescriptionEn;
        }

        /// <summary>
        ///     Link ngoài game (nút Rate, Policy, ToS, fanpage). Rỗng ở googlePlay/appStore thì
        ///     <see cref="MarketingConfigApplier" /> tự dựng từ packageName/appleId.
        /// </summary>
        [Serializable]
        public class LinksSection
        {
            public string googlePlay;
            public string appStore;
            public string facebookPage;
        }

        #endregion

        #region Fields

        public string packageName;
        public string gameName;

        /// <summary>App Store ID (numeric). Rỗng = chưa có app trên ASC → giữ nguyên GameConstant.IOSAppId.</summary>
        public string appleId;

        public string appsflyerDevKey;
        public MaxSection max;
        public AdmobSection admob;
        public FacebookSection facebook;
        public UnityAdsSection unityAds;
        public AppLovinSection applovin;
        public LinksSection links;

        #endregion

        #region Derived

        /// <summary>Link Google Play — lấy từ JSON, rỗng thì dựng từ <see cref="packageName" />.</summary>
        public string GooglePlayUrl =>
            !string.IsNullOrEmpty(links?.googlePlay) ? links.googlePlay :
            string.IsNullOrEmpty(packageName) ? "" :
            $"https://play.google.com/store/apps/details?id={packageName}";

        /// <summary>Link App Store — lấy từ JSON, rỗng thì dựng từ <see cref="appleId" />.</summary>
        public string AppStoreUrl =>
            !string.IsNullOrEmpty(links?.appStore) ? links.appStore :
            string.IsNullOrEmpty(appleId) ? "" :
            $"https://apps.apple.com/app/id{appleId}";

        #endregion

        #region Load

        /// <summary>
        ///     Đường dẫn tuyệt đối tới file JSON nguồn.
        ///     <para>
        ///         Cố ý nằm ở <c>ProjectSettings/</c> chứ không nằm trong <c>Assets/</c>: tool này dùng
        ///         CHUNG cho mọi dự án (đi qua branch <c>code-template</c>), còn dữ liệu thì mỗi dự án một
        ///         khác. Để trong Assets thì mỗi lần merge template là số của dự án này đè lên dự án kia.
        ///         <c>ProjectSettings/</c> vẫn được track git, và AppLovin cũng cất consent flow ở đây.
        ///     </para>
        /// </summary>
        public static string JsonPath =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? "",
                "ProjectSettings/MarketingConfig.json");

        /// <summary>
        ///     Đọc + validate file JSON. Trả về null (và log lỗi) khi thiếu file hoặc thiếu section —
        ///     mọi section đều bắt buộc có mặt để applier không phải null-check từng nhánh.
        /// </summary>
        public static MarketingConfig Load()
        {
            if (!File.Exists(JsonPath))
            {
                Debug.LogError($"[Marketing] Khong thay file config: {JsonPath}");
                return null;
            }

            MarketingConfig cfg;
            try
            {
                cfg = JsonUtility.FromJson<MarketingConfig>(File.ReadAllText(JsonPath));
            }
            catch (Exception e)
            {
                Debug.LogError($"[Marketing] JSON hong: {e.Message}");
                return null;
            }

            // facebook phải soi tới .android/.ios y như max/admob/unityAds: JSON thiếu nhánh nào thì
            // JsonUtility để null nhánh đó, mà page "Khai bên dashboard MAX" deref thẳng
            // cfg.facebook.android.rewarded -> lọt qua đây là NRE mỗi lượt vẽ, hỏng luôn cửa sổ.
            if (cfg?.max?.android == null || cfg.max.ios == null
                || cfg.admob?.android == null || cfg.admob.ios == null
                || cfg.facebook?.android == null || cfg.facebook.ios == null
                || cfg.unityAds?.android == null || cfg.unityAds.ios == null
                || cfg.applovin == null || cfg.links == null)
            {
                Debug.LogError("[Marketing] JSON thieu section "
                               + "(max/admob/facebook/unityAds/applovin/links) - xem lai file.");
                return null;
            }

            return cfg;
        }

        #endregion
    }
}
#endif
