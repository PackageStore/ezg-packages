namespace Ezg.Package.AdsManager
{
    /// <summary> Loại format quảng cáo (dùng để host map sang nhãn analytics riêng). </summary>
    public enum AdFormat
    {
        Rewarded,
        Interstitial,
        Banner,
        Mrec
    }

    /// <summary>
    /// Dữ liệu doanh thu 1 lần impression — POCO thuần, KHÔNG chứa type của SDK mediation.
    /// Adapter (MAX/IronSource) tự đổ dữ liệu từ SDK vào đây rồi đẩy cho <see cref="IAdsTracker"/>.
    /// </summary>
    public struct AdRevenueInfo
    {
        /// <summary> Format quảng cáo. </summary>
        public AdFormat Format;

        /// <summary> Nền tảng quảng cáo, ví dụ "AppLovin". </summary>
        public string AdPlatform;

        /// <summary> Tên mạng quảng cáo thắng impression. </summary>
        public string NetworkName;

        /// <summary> Ad-unit-id (tham số callback). </summary>
        public string AdUnitId;

        /// <summary> Định danh ad-unit từ SDK (AdUnitIdentifier). </summary>
        public string AdUnitIdentifier;

        /// <summary> Nhãn format từ SDK (ví dụ "REWARDED"). </summary>
        public string AdFormatLabel;

        /// <summary> Placement do SDK trả về. </summary>
        public string Placement;

        /// <summary> Placement/source do game truyền khi show ad. </summary>
        public string Source;

        /// <summary> Mã quốc gia. </summary>
        public string CountryCode;

        /// <summary> Doanh thu. </summary>
        public double Revenue;

        /// <summary> Mã tiền tệ, ví dụ "USD". </summary>
        public string Currency;
    }
}
