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
        /// Đã hết giãn cách sau fullscreen ad gần nhất hay chưa — adapter tự tính theo
        /// <see cref="TimeDelayShowInterstitialAds" />.
        /// Gán <c>false</c> = báo "vừa có ad, đếm lại"; gán <c>true</c> là NO-OP: giãn cách là bất
        /// biến của module, host không tắt được bằng một phép gán.
        /// </summary>
        bool CanShowInterstitial { get; set; }

        /// <summary>
        /// Giãn cách tối thiểu (giây) giữa một fullscreen ad (interstitial HOẶC rewarded) và
        /// interstitial kế tiếp.
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
