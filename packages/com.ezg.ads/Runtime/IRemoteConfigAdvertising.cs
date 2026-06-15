namespace Ezg.Package.AdsManager
{
    /// <summary>
    /// Interface chứa các cấu hình remote config liên quan đến quảng cáo.
    /// Cho phép điều chỉnh hành vi hiển thị ads từ xa mà không cần cập nhật app.
    /// </summary>
    public interface IRemoteConfigAdvertising
    {
        /// <summary>
        /// Có đang hiển thị reward không.
        /// </summary>
        bool IsShowReward { get; set; }

        /// <summary>
        /// Biến để kiểm tra trạng thái inter có thể show được không.
        /// </summary>
        bool CanShowInterstitial { get; set; }

        /// <summary>
        /// Thời gian đếm khi show inter.
        /// </summary>
        int CountTimeShowInterstitialAds { get; set; }

        /// <summary>
        /// Thời gian giữa 2 show inter.
        /// </summary>
        int TimeDelayShowInterstitialAds { get; set; }

        /// <summary>
        /// Biến bật tắt show inter bằng remote config.
        /// </summary>
        bool IsShowInterstitialAds { get; set; }

        /// <summary>
        /// Biến từ level bao nhiêu sẽ show inter bằng remote config.
        /// </summary>
        int ShowInterstitialAdsFromLevel { get; set; }

        /// <summary>
        /// Biến bật tắt show banner bằng remote config.
        /// </summary>
        bool IsShowBannerAds { get; set; }

        /// <summary>
        /// Biến từ level bao nhiêu sẽ show banner bằng remote config.
        /// </summary>
        int ShowBannerAdsFromLevel { get; set; }
    }
}
