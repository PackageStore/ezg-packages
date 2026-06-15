using UnityEngine;

namespace Ezg.Package.AdsManager
{
    /// <summary>
    /// Điểm truy cập key/ad-unit-id của mediation. Giá trị KHÔNG còn hardcode mà đọc từ
    /// <see cref="AdsConfig"/> asset (Resources/AdsConfig). Có thể inject config khác qua
    /// <see cref="SetConfig"/> (phục vụ test / đóng package sau này).
    /// </summary>
    public static class MediationConstant
    {
        #region Fields

        private const string CONFIG_RESOURCE_NAME = "AdsConfig";

        private static AdsConfig _config;

        private static AdsConfig Config
        {
            get
            {
                if (_config == null)
                {
                    _config = Resources.Load<AdsConfig>(CONFIG_RESOURCE_NAME);
                    if (_config == null)
                    {
                        Debug.LogError(
                            $"[Ads] Không tìm thấy AdsConfig trong Resources/{CONFIG_RESOURCE_NAME}. " +
                            "Tạo asset qua Create > Ezg > Ads > Config, đặt trong thư mục Resources và đặt tên 'AdsConfig'.");
                    }
                }

                return _config;
            }
        }

        #endregion

        #region Public Methods

        /// <summary> Inject config thủ công (override Resources). Gọi trước khi khởi tạo ads. </summary>
        public static void SetConfig(AdsConfig config)
        {
            _config = config;
        }

        /// <summary> Hằng số ad-unit-id cho AppLovin MAX. </summary>
        public static class Max
        {
            /// <summary> SDK key của AppLovin MAX. </summary>
            public static string SdkKey => Config != null ? Config.MaxSdkKey : string.Empty;

            /// <summary> Ad-unit-id banner MAX. </summary>
            public static string BannerStringId => Config != null ? Config.MaxBannerId : string.Empty;

            /// <summary> Ad-unit-id interstitial MAX. </summary>
            public static string InterstitialStringId => Config != null ? Config.MaxInterstitialId : string.Empty;

            /// <summary> Ad-unit-id rewarded MAX theo nền tảng hiện tại. </summary>
            public static string RewardedStringId => Config != null ? Config.MaxRewardedId : string.Empty;
        }

        /// <summary> Hằng số ad-unit-id cho IronSource/LevelPlay. </summary>
        public static class IronSource
        {
            /// <summary> Lấy SDK key IronSource theo nền tảng hiện tại. </summary>
            /// <returns>SDK key chuỗi, hoặc rỗng nếu chưa cấu hình config.</returns>
            public static string GetSdkKey() => Config != null ? Config.IronSourceSdkKey : string.Empty;

            /// <summary> Lấy ad-unit-id rewarded IronSource theo nền tảng hiện tại. </summary>
            /// <returns>Ad-unit-id chuỗi, hoặc rỗng nếu chưa cấu hình config.</returns>
            public static string GetRewardedVideoAdUnitId() => Config != null ? Config.IronSourceRewardedId : string.Empty;
        }

        #endregion
    }
}
