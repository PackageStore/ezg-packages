using UnityEngine;

namespace Ezg.Package.AdsManager
{
    /// <summary>
    /// Config asset chứa toàn bộ key/ad-unit-id của mediation.
    /// Tạo asset qua menu: Create > Ezg > Ads > Config (đặt trong thư mục Resources, tên "AdsConfig").
    /// Mục đích: tách dữ liệu app-specific (secret) ra khỏi code để module có thể tái sử dụng / đóng package.
    /// </summary>
    [CreateAssetMenu(fileName = "AdsConfig", menuName = "Ezg/Ads/Config", order = 0)]
    public class AdsConfig : ScriptableObject
    {
        #region Fields

        [Header("AppLovin MAX")]
        [SerializeField] private string maxSdkKey;
        [SerializeField] private string maxBannerId;
        [SerializeField] private string maxInterstitialId;
        [SerializeField] private string maxRewardedAndroidId;
        [SerializeField] private string maxRewardedIosId;

        [Header("IronSource / LevelPlay")]
        [SerializeField] private string ironSourceAndroidKey;
        [SerializeField] private string ironSourceIosKey;
        [SerializeField] private string ironSourceRewardedAndroidId;
        [SerializeField] private string ironSourceRewardedIosId;

        #endregion

        #region Public Methods

        /// <summary> SDK key của AppLovin MAX. </summary>
        public string MaxSdkKey => maxSdkKey;

        /// <summary> Ad-unit-id banner MAX. </summary>
        public string MaxBannerId => maxBannerId;

        /// <summary> Ad-unit-id interstitial MAX. </summary>
        public string MaxInterstitialId => maxInterstitialId;

        /// <summary> Ad-unit-id rewarded MAX theo nền tảng build hiện tại. </summary>
        public string MaxRewardedId =>
    #if UNITY_IOS
            maxRewardedIosId;
    #else
            maxRewardedAndroidId;
    #endif

        /// <summary> SDK key IronSource theo nền tảng build hiện tại. </summary>
        public string IronSourceSdkKey =>
    #if UNITY_IOS
            ironSourceIosKey;
    #else
            ironSourceAndroidKey;
    #endif

        /// <summary> Ad-unit-id rewarded IronSource theo nền tảng build hiện tại. </summary>
        public string IronSourceRewardedId =>
    #if UNITY_IOS
            ironSourceRewardedIosId;
    #else
            ironSourceRewardedAndroidId;
    #endif

        #endregion
    }
}
