using System;
using Ezg.Package.Singleton;
using UnityEngine;

namespace Ezg.Package.AdsManager
{
    /// <summary>
    /// Singleton quản lý toàn bộ quảng cáo trong game (rewarded, interstitial, banner, mrec).
    /// Delegate thực thi cho adapter (mặc định là <see cref="MaxAdsvertising" />).
    /// <para>
    /// Chỉ những format được bật trong <see cref="AdsConfig.EnabledFormats" /> mới được khởi tạo.
    /// Gọi một format không bật là <b>no-op an toàn</b> (không throw, không log lỗi) — nhờ vậy code
    /// dùng chung giữa các project không cần #if hay xoá callsite khi project không dùng format đó.
    /// Muốn nhánh theo format thì kiểm tra <see cref="HasBanner" /> / <see cref="HasInterstitial" /> /
    /// <see cref="HasRewarded" />.
    /// </para>
    /// </summary>
    public class AdsManager : Singleton<AdsManager>
    {
        #region Fields

        public IRemoteConfigAdvertising advertisingRemoteConfig;
        public Vector2 size;
        public float intervalTime;
        public bool canShowInter;
        public int count;

        private IAdProvider _provider;
        private IBannerAds _banner;
        private IInterstitialAds _interstitial;
        private IRewardedAds _rewarded;
        private IMRecAds _mrec;

        private bool _initialized;
        private bool _isTestAds;
        private bool _isDebugAds;

        private IAdsTracker _tracker = new NullAdsTracker();
        private Func<int> _currentLevelProvider = () => int.MaxValue;

        public event Action OnBannerLoaded;
        public event Action OnBannerFailed;

        #endregion

        #region Public Methods

        /// <summary>
        /// Đang chạy chế độ debug ads: không có mediation SDK, mọi ad auto-thành-công, không bắn tracking.
        /// Luôn false ở build production (xem <see cref="AdsConfig.IsDebugAds" />).
        /// </summary>
        public bool IsDebugAds => _isDebugAds;

        /// <summary> Project này có bật banner không. </summary>
        public bool HasBanner => _banner != null;

        /// <summary> Project này có bật interstitial không. </summary>
        public bool HasInterstitial => _interstitial != null;

        /// <summary> Project này có bật rewarded không. </summary>
        public bool HasRewarded => _rewarded != null;

        /// <summary> Project này có bật MRec không. </summary>
        public bool HasMRec => _mrec != null;

        /// <summary>
        /// Inject tracker analytics + nguồn lấy level hiện tại từ host project, rồi khởi tạo ads
        /// theo tập format trong <see cref="AdsConfig" />. Gọi 1 lần lúc bootstrap (Splash).
        /// Gọi lại lần nữa sẽ bị bỏ qua.
        /// </summary>
        /// <param name="tracker">Tracker analytics để ghi nhận sự kiện quảng cáo.</param>
        /// <param name="currentLevelProvider">Hàm trả về level hiện tại của người chơi.</param>
        public void Configure(IAdsTracker tracker, Func<int> currentLevelProvider)
        {
            if (tracker != null) _tracker = tracker;
            if (currentLevelProvider != null) _currentLevelProvider = currentLevelProvider;

            InitAds();
        }

        /// <summary>
        /// Bật/tắt chế độ test ads.
        /// </summary>
        /// <param name="isTest">True để bật test ads.</param>
        public void SetTestAds(bool isTest)
        {
            _isTestAds = isTest;
        }

        /// <summary>
        /// Kiểm tra xem có thể hiển thị interstitial hay không (dựa theo cooldown và điều kiện adapter).
        /// </summary>
        /// <returns>True nếu đủ điều kiện hiển thị interstitial.</returns>
        public bool CanShowInter()
        {
            return _interstitial != null && _interstitial.CanShowInter() && canShowInter;
        }

        #region Reward Ads

        /// <summary>
        /// Kiểm tra xem rewarded video có sẵn sàng để hiển thị không.
        /// Ở chế độ debug ads luôn true (khi format bật) vì <see cref="DebugAdsProvider" /> trả thưởng ngay.
        /// </summary>
        /// <returns>True nếu rewarded ad đã load xong. False nếu project không bật rewarded.</returns>
        public bool IsVideoRewardReady()
        {
            return _rewarded != null && _rewarded.IsReadyVideoAds();
        }

        /// <summary>
        /// Hiển thị rewarded video.
        /// Nếu project không bật rewarded thì gọi <paramref name="onFail" /> để caller có đường thoát.
        /// </summary>
        /// <param name="sourceId">Placement/source định danh nơi gọi ad.</param>
        /// <param name="onFinish">Callback khi người dùng xem xong và nhận thưởng.</param>
        /// <param name="onClose">Callback khi người dùng đóng ad trước khi hoàn thành.</param>
        /// <param name="onFail">Callback khi ad thất bại.</param>
        public void ShowRewardedVideo(string sourceId, Action onFinish = null, Action onClose = null,
            Action onFail = null)
        {
            if (_rewarded == null)
            {
                onFail?.Invoke();
                return;
            }

            _tracker.OnRewardClick(sourceId);
            _rewarded.ShowRewardVideo(onFinish, onClose, onFail, source: sourceId);
        }

        #endregion

        #region Interstitial Ads

        /// <summary>
        /// Kiểm tra xem interstitial có sẵn sàng để hiển thị không.
        /// Ở chế độ debug ads luôn true (khi format bật) vì <see cref="DebugAdsProvider" /> kết thúc ngay.
        /// </summary>
        /// <returns>True nếu interstitial ad đã load xong. False nếu project không bật interstitial.</returns>
        public bool IsInterstitialReady()
        {
            return _interstitial != null && _interstitial.IsInterstitialReady();
        }

        /// <summary>
        /// Load interstitial nếu chưa sẵn sàng.
        /// </summary>
        public void LoadInterstitial()
        {
            if (_interstitial == null) return;

            if (!_interstitial.IsInterstitialReady())
            {
                _interstitial.LoadInterstitial();
            }
        }

        /// <summary>
        /// Hiển thị interstitial ad. No-op (gọi <paramref name="onClose" />) nếu project không bật interstitial.
        /// </summary>
        /// <param name="onFinish">Callback khi ad hoàn thành.</param>
        /// <param name="onClose">Callback khi người dùng đóng ad.</param>
        /// <param name="onFail">Callback khi ad thất bại.</param>
        /// <param name="source">Placement/source định danh nơi gọi ad.</param>
        public void ShowInterstitial(Action onFinish = null, Action onClose = null, Action onFail = null,
            string source = null)
        {
            if (_interstitial == null)
            {
                onClose?.Invoke();
                return;
            }

            if (_isDebugAds)
            {
                count = 0;
                canShowInter = false;
            }

            _interstitial.ShowInterstitial(onFinish, onClose, onFail, source);
        }

        #endregion

        #region Banner Ads

        /// <summary>
        /// Load/show banner ad. No-op nếu project không bật banner.
        /// </summary>
        public void LoadBanner()
        {
            _banner?.ShowBannerAds();
        }

        /// <summary>
        /// Ẩn banner ad. No-op nếu project không bật banner.
        /// </summary>
        public void HideBanner()
        {
            _banner?.HideBannerAds();
        }

        #endregion

        #region Mrec

        /// <summary>
        /// Tạo MRec tại vị trí chỉ định. No-op nếu project không bật MRec.
        /// </summary>
        /// <param name="pos">Vị trí hiển thị MRec.</param>
        public void CreateMRec(Vector2 pos)
        {
            _mrec?.CreateMRec(pos);
        }

        /// <summary>
        /// Hiển thị MRec ad. No-op nếu project không bật MRec.
        /// </summary>
        public void ShowMrec()
        {
            _mrec?.ShowMRec();
        }

        /// <summary>
        /// Ẩn MRec ad. No-op nếu project không bật MRec.
        /// </summary>
        public void HideMrec()
        {
            _mrec?.HideMRec();
        }

        #endregion

        #endregion

        #region Private Methods

        /// <summary>
        /// Khởi tạo advertising adapter theo tập format bật trong <see cref="AdsConfig" />,
        /// và chỉ giữ tham chiếu tới những format đó.
        /// </summary>
        private void InitAds()
        {
            if (_initialized) return;
            _initialized = true;

            var config = MediationConstant.Current;
            var formats = config != null ? config.EnabledFormats : AdFormats.None;

            // Format đã tick trong AdsConfig nhưng bị tự tắt vì thiếu ad-unit-id. Phải báo TÊN format:
            // nếu không, format biến mất im lặng và triệu chứng chỉ là "đã bật Rewarded mà đọc ra None".
            var missingId = config != null ? config.MissingIdFormats : AdFormats.None;
            if (missingId != AdFormats.None)
            {
                Debug.LogError($"[Ads] Format [{missingId}] đã bật trong AdsConfig nhưng THIẾU ad-unit-id " +
                               "→ tự bị tắt. Điền ad-unit-id tương ứng ở Resources/AdsConfig, " +
                               "hoặc bật debugAds để test mà không cần id.");
            }

            if (formats == AdFormats.None)
            {
                var reason = missingId != AdFormats.None
                    ? $"mọi format đã bật đều thiếu ad-unit-id ([{missingId}])"
                    : "enabledFormats trong AdsConfig không tick format nào";
                Debug.LogWarning($"[Ads] Không có format quảng cáo nào khả dụng ({reason}) " +
                                 "— bỏ qua khởi tạo mediation, mọi lệnh show ad sẽ vào nhánh fail.");
                return;
            }

            // Debug ads: provider giả, KHÔNG init mediation SDK / không dùng ad-unit-id thật,
            // và ép tracker rỗng để không bắn tracking quảng cáo.
            _isDebugAds = config.IsDebugAds;
            if (_isDebugAds)
            {
                _tracker = new NullAdsTracker();
            }

            var provider = _isDebugAds
                ? (IAdProvider)new DebugAdsProvider()
                : new MaxAdsvertising();

            _provider = provider;
            advertisingRemoteConfig = provider as IRemoteConfigAdvertising;

            if ((formats & AdFormats.Banner) != 0) _banner = provider as IBannerAds;
            if ((formats & AdFormats.Interstitial) != 0) _interstitial = provider as IInterstitialAds;
            if ((formats & AdFormats.Rewarded) != 0) _rewarded = provider as IRewardedAds;
            if ((formats & AdFormats.MRec) != 0) _mrec = provider as IMRecAds;

            if (_banner != null)
            {
                _banner.OnBannerLoaded += HandleBannerLoaded;
                _banner.OnBannerFailed += HandleBannerFailed;
            }

            _provider.Initialize(formats, _tracker, _currentLevelProvider);
        }

        private void OnApplicationPause(bool isPaused)
        {
            _provider?.OnApplicationPause(isPaused);
        }

        #endregion

        #region Event Handlers

        private void HandleBannerLoaded() => OnBannerLoaded?.Invoke();

        private void HandleBannerFailed() => OnBannerFailed?.Invoke();

        #endregion
    }
}
