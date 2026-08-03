using UnityEngine;

namespace Ezg.Package.AdsManager
{
    /// <summary>
    /// Config asset chứa toàn bộ key/ad-unit-id của mediation và tập format quảng cáo được bật.
    /// Tạo asset qua menu: Create > Ezg > Ads > Config (đặt trong thư mục Resources, tên "AdsConfig").
    /// Mục đích: tách dữ liệu app-specific (secret) ra khỏi code để module có thể tái sử dụng / đóng package,
    /// và cho phép mỗi project chỉ bật những format nó thật sự dùng.
    /// </summary>
    [CreateAssetMenu(fileName = "AdsConfig", menuName = "Ezg/Ads/Config", order = 0)]
    public class AdsConfig : ScriptableObject
    {
        #region Fields

        [Header("Debug")]
        [Tooltip("Debug ads: KHÔNG init mediation SDK (không dùng ad-unit-id thật), mọi lệnh show ad trả " +
                 "thành công ngay, và KHÔNG bắn tracking quảng cáo. Bật hết format nên MỌI nút ads " +
                 "đều bypass được, bỏ qua cấu hình Formats bên dưới.\n\n" +
                 "CÓ HIỆU LỰC Ở MỌI BUILD, kể cả release — nhớ tắt trước khi build production. " +
                 "Build từ Unity Editor sẽ hiện hộp thoại xác nhận nếu cờ này còn bật.")]
        [SerializeField]
        private bool debugAds;

        [Header("Formats")]
        [Tooltip("Format quảng cáo project này dùng. Format bật nhưng thiếu ad-unit-id sẽ tự bị tắt.\n\n" +
                 "Bị BỎ QUA khi debugAds bật — lúc đó mọi format đều khả dụng để mọi nút ads bypass được.")]
        [SerializeField]
        private AdFormats enabledFormats = AdFormats.Banner | AdFormats.Interstitial | AdFormats.Rewarded;

        /// <summary>
        /// Tập format dùng khi debug ads: bật HẾT, để mọi nút ads trong game đều bypass được
        /// mà không phụ thuộc checkbox <see cref="enabledFormats" /> hay có ad-unit-id hay không.
        /// </summary>
        private const AdFormats DEBUG_FORMATS =
            AdFormats.Banner | AdFormats.Interstitial | AdFormats.Rewarded | AdFormats.MRec;

        [Header("AppLovin MAX")]
        [SerializeField] private string maxAndroidSdkKey;
        [SerializeField] private string maxAndroidBannerId;
        [SerializeField] private string maxAndroidInterstitialId;
        [SerializeField] private string maxAndroidRewardedId;
        [SerializeField] private string maxIosSdkKey;
        [SerializeField] private string maxIosBannerId;
        [SerializeField] private string maxIosInterstitialId;
        [SerializeField] private string maxIosRewardedId;

        [Header("IronSource / LevelPlay")]
        [SerializeField] private string ironSourceAndroidKey;
        [SerializeField] private string ironSourceIosKey;
        [SerializeField] private string ironSourceRewardedAndroidId;
        [SerializeField] private string ironSourceRewardedIosId;

        #endregion

        #region Public Methods

        /// <summary>
        /// Chế độ debug ads có đang bật hay không — có hiệu lực ở MỌI build, kể cả release.
        /// <para>
        /// Vì vậy PHẢI tắt trước khi build production: bật mà ship là mất doanh thu ads và mất tracking.
        /// Editor có cảnh báo xác nhận lúc build (AdsBuildGuard) để không lỡ tay, nhưng cảnh báo đó
        /// chỉ chạy khi build từ Unity Editor — build qua CI/script thì không có.
        /// </para>
        /// </summary>
        public bool IsDebugAds => debugAds;

        /// <summary>
        /// Tập format thực sự khả dụng: format được bật trong inspector VÀ có đủ ad-unit-id.
        /// Thiếu id thì format tự tắt — tránh việc SDK tạo/serve một format không cấu hình rồi log lỗi liên tục.
        /// <para>
        /// NGOẠI LỆ khi <see cref="IsDebugAds" />: trả về <see cref="DEBUG_FORMATS" /> (bật hết).
        /// Debug ads không init SDK và không dùng ad-unit-id thật nên để id rỗng là bình thường —
        /// nếu vẫn lọc theo id thì mọi format bị tắt sạch (đọc ra <see cref="AdFormats.None" />),
        /// AdsManager bỏ qua khởi tạo và mọi nút ads rơi vào nhánh fail. Debug ads phải bypass hết.
        /// </para>
        /// </summary>
        public AdFormats EnabledFormats
        {
            get
            {
                if (debugAds) return DEBUG_FORMATS;

                var formats = enabledFormats;
                if (string.IsNullOrEmpty(MaxBannerId)) formats &= ~AdFormats.Banner;
                if (string.IsNullOrEmpty(MaxInterstitialId)) formats &= ~AdFormats.Interstitial;
                if (string.IsNullOrEmpty(MaxRewardedId)) formats &= ~AdFormats.Rewarded;
                return formats;
            }
        }

        /// <summary>
        /// Tập format được tick trong inspector, CHƯA lọc theo ad-unit-id.
        /// So với <see cref="EnabledFormats" /> để biết cấu hình có bị tắt bớt hay không.
        /// </summary>
        public AdFormats RequestedFormats => enabledFormats;

        /// <summary>
        /// Format được tick trong inspector nhưng bị tự tắt vì THIẾU ad-unit-id — nguyên nhân khiến
        /// <see cref="EnabledFormats" /> đọc ra ít format hơn mong đợi (hoặc ra <see cref="AdFormats.None" />
        /// dù đã tick). AdsManager log cảnh báo dựa trên giá trị này lúc init để lỗi không im lặng.
        /// Luôn <see cref="AdFormats.None" /> ở chế độ debug ads (chế độ đó không cần id).
        /// </summary>
        public AdFormats MissingIdFormats => debugAds ? AdFormats.None : enabledFormats & ~EnabledFormats;

        /// <summary> Kiểm tra một format có được bật và đủ cấu hình hay không. </summary>
        /// <param name="format">Format cần kiểm tra.</param>
        /// <returns>True nếu format khả dụng.</returns>
        public bool Has(AdFormats format) => (EnabledFormats & format) != 0;

        /// <summary> SDK key của AppLovin MAX theo nền tảng build hiện tại. </summary>
        public string MaxSdkKey =>
#if UNITY_IOS
            maxIosSdkKey;
#else
            maxAndroidSdkKey;
#endif

        /// <summary> Ad-unit-id banner MAX theo nền tảng build hiện tại. </summary>
        public string MaxBannerId =>
#if UNITY_IOS
            maxIosBannerId;
#else
            maxAndroidBannerId;
#endif

        /// <summary> Ad-unit-id interstitial MAX theo nền tảng build hiện tại. </summary>
        public string MaxInterstitialId =>
#if UNITY_IOS
            maxIosInterstitialId;
#else
            maxAndroidInterstitialId;
#endif

        /// <summary> Ad-unit-id rewarded MAX theo nền tảng build hiện tại. </summary>
        public string MaxRewardedId =>
#if UNITY_IOS
            maxIosRewardedId;
#else
            maxAndroidRewardedId;
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
