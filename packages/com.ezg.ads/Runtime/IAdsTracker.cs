namespace Ezg.Package.AdsManager
{
    /// <summary>
    /// Cổng analytics cho module Ads. Module KHÔNG gọi thẳng Firebase/AppsFlyer/GameAnalytics
    /// mà bắn các sự kiện vòng đời quảng cáo qua interface này; host project tự implement để map
    /// sang hệ analytics riêng. Nếu host chưa cấu hình, module dùng <see cref="NullAdsTracker"/> (no-op).
    /// </summary>
    public interface IAdsTracker
    {
        /// <summary> Mediation SDK đã init xong (đăng ký auto-track impression nếu cần). </summary>
        void OnAdsInitialized();

        /// <summary> User bấm yêu cầu xem rewarded. </summary>
        void OnRewardClick(string source);

        /// <summary> Bắt đầu show rewarded (ad đã sẵn sàng). </summary>
        void OnRewardShow(string source);

        /// <summary> Rewarded load thất bại. </summary>
        void OnRewardLoadFailed(string source);

        /// <summary> Rewarded bắt đầu hiển thị. </summary>
        void OnRewardDisplayed(string source);

        /// <summary> Rewarded xem xong, đã trả thưởng. </summary>
        void OnRewardCompleted(string source);

        /// <summary> Có doanh thu quảng cáo (mọi format). </summary>
        void OnAdRevenuePaid(AdRevenueInfo info);
    }

    /// <summary> Implementation rỗng — dùng khi host chưa inject tracker để tránh NPE. </summary>
    public sealed class NullAdsTracker : IAdsTracker
    {
        public void OnAdsInitialized() { }
        public void OnRewardClick(string source) { }
        public void OnRewardShow(string source) { }
        public void OnRewardLoadFailed(string source) { }
        public void OnRewardDisplayed(string source) { }
        public void OnRewardCompleted(string source) { }
        public void OnAdRevenuePaid(AdRevenueInfo info) { }
    }
}
