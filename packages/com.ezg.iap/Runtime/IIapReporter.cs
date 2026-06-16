namespace Ezg.Feature.IAP
{
    /// <summary>
    /// Kênh báo cáo sự kiện IAP ra ngoài module (analytics + đồng bộ dữ liệu).
    /// Module chỉ phát dữ liệu thô; game tự bắn SDK (Firebase/AppsFlyer/GameAnalytics)
    /// và emit event nội bộ trong các callback này.
    /// </summary>
    public interface IIapReporter
    {
        /// <summary>Người chơi bấm mua (trước khi mở store).</summary>
        void OnPurchaseClick(IapPurchaseInfo info);

        /// <summary>Giao dịch đã validate hợp lệ (analytics doanh thu + user property).</summary>
        void OnPurchaseValidated(IapPurchaseInfo info);

        /// <summary>AppsFlyer trả về conversion/attribution data.</summary>
        void OnConversionData(string conversionJson);

        /// <summary>Yêu cầu game đồng bộ dữ liệu (thay cho emit ForceSyncData).</summary>
        void RequestSync();
    }
}
